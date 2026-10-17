using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using Jellyfin.Extensions.Json;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Controller.Configuration;
using MediaBrowser.Controller.MediaEncoding;
using MediaBrowser.Model.Configuration;
using MediaBrowser.Model.Dlna;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.LakeHp;
using Microsoft.Extensions.Logging;

namespace Emby.Server.Implementations.LakeHp;

/// <summary>
/// Per-user/per-device surround output (headphone virtualizer or matrix surround) and filter selection.
/// </summary>
public class LakeHpManager : ILakeHpManager
{
    private const string FilterName = "lakehp";
    private const string MatrixFilterName = "matrixenc";
    private const string FilterFolder = "lakehp";
    private const string StoreFile = "lakehp.json";

    /// <summary>
    /// Input layouts both ffmpeg filters accept, by ffmpeg layout name.
    /// </summary>
    private static readonly HashSet<string> _layouts = new(StringComparer.OrdinalIgnoreCase)
    {
        "5.0", "5.0(side)", "5.1", "5.1(side)", "7.0", "7.1"
    };

    private static readonly string[] _filterExtensions = [".dll", ".bin"];

    private readonly IServerConfigurationManager _config;
    private readonly IMediaEncoder _mediaEncoder;
    private readonly ILogger<LakeHpManager> _logger;
    private readonly JsonSerializerOptions _jsonOptions = JsonDefaults.Options;
    private readonly Lock _lock = new();
    private Dictionary<Guid, LakeHpPreferences>? _prefs;

    /// <summary>
    /// Initializes a new instance of the <see cref="LakeHpManager"/> class.
    /// </summary>
    /// <param name="config">The server configuration manager.</param>
    /// <param name="mediaEncoder">The media encoder.</param>
    /// <param name="logger">The logger.</param>
    public LakeHpManager(IServerConfigurationManager config, IMediaEncoder mediaEncoder, ILogger<LakeHpManager> logger)
    {
        _config = config;
        _mediaEncoder = mediaEncoder;
        _logger = logger;
    }

    private string StorePath => Path.Combine(_config.ApplicationPaths.ConfigurationDirectoryPath, StoreFile);

    /// <inheritdoc />
    public string? GetFilterPath()
    {
        var configured = _config.GetEncodingOptions().LakeHpFilterPath;
        if (!string.IsNullOrWhiteSpace(configured))
        {
            return File.Exists(configured) ? configured : null;
        }

        var folder = Path.Combine(_config.ApplicationPaths.ProgramDataPath, FilterFolder);
        if (!Directory.Exists(folder))
        {
            return null;
        }

        return Directory.EnumerateFiles(folder)
            .Where(f => _filterExtensions.Contains(Path.GetExtension(f), StringComparer.OrdinalIgnoreCase))
            .Order(StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();
    }

    /// <inheritdoc />
    public bool IsFfmpegSupported() => _mediaEncoder.SupportsFilter(FilterName);

    /// <inheritdoc />
    public bool IsAvailable(LakeHpOutputMode mode) => mode switch
    {
        LakeHpOutputMode.Headphones => IsFfmpegSupported() && GetFilterPath() is not null,
        LakeHpOutputMode.MatrixSurround => _mediaEncoder.SupportsFilter(MatrixFilterName),
        _ => true
    };

    /// <inheritdoc />
    public LakeHpOutputMode GetActiveMode(Guid userId, string? deviceId)
    {
        if (userId.Equals(Guid.Empty))
        {
            return LakeHpOutputMode.Off;
        }

        var mode = GetWantedMode(userId, deviceId);
        return IsAvailable(mode) ? mode : LakeHpOutputMode.Off;
    }

    /// <inheritdoc />
    public LakeHpPreferences GetPreferences(Guid userId)
    {
        lock (_lock)
        {
            var prefs = Load();
            return prefs.TryGetValue(userId, out var p) ? Copy(p) : new LakeHpPreferences { UserId = userId };
        }
    }

    /// <inheritdoc />
    public void UpdatePreferences(Guid userId, string? deviceId, LakeHpStatusUpdate update)
    {
        lock (_lock)
        {
            var prefs = Load();
            if (!prefs.TryGetValue(userId, out var p))
            {
                p = new LakeHpPreferences { UserId = userId };
                prefs[userId] = p;
            }

            if (update.UserMode.HasValue)
            {
                p.Mode = update.UserMode.Value;
            }

            if (update.DeviceMode.HasValue && !string.IsNullOrEmpty(deviceId))
            {
                var others = p.Devices.Where(d => !string.Equals(d.DeviceId, deviceId, StringComparison.Ordinal));
                p.Devices = update.DeviceMode.Value == LakeHpDeviceMode.Default
                    ? others.ToArray()
                    : others.Append(new LakeHpDevicePreference { DeviceId = deviceId, Mode = update.DeviceMode.Value }).ToArray();
            }

            Save(prefs);
        }
    }

    /// <inheritdoc />
    public string? GetFilter(MediaStream? audioStream, EncodingOptions encodingOptions, Guid? userId, string? deviceId)
    {
        if (audioStream?.Channels is not > 2 || !userId.HasValue)
        {
            return null;
        }

        var mode = GetWantedMode(userId.Value, deviceId);
        if (mode == LakeHpOutputMode.Off)
        {
            return null;
        }

        var layout = DownMixAlgorithmsHelper.InferChannelLayout(audioStream);
        if (!_layouts.Contains(layout))
        {
            _logger.LogDebug("LakeHP: channel layout {Layout} is not supported, using the normal downmix", layout);
            return null;
        }

        if (!IsAvailable(mode))
        {
            _logger.LogWarning("LakeHP: {Mode} is selected but not available on this server; using the normal downmix", mode);
            return null;
        }

        return mode == LakeHpOutputMode.MatrixSurround
            ? GetMatrixFilter(encodingOptions)
            : GetHeadphoneFilter(encodingOptions);
    }

    /// <inheritdoc />
    public void ForceStereo(DeviceProfile profile)
    {
        var stereo = new ProfileCondition(ProfileConditionType.LessThanEqual, ProfileConditionValue.AudioChannels, "2", true);

        // the same profile object is reused for every media source of a request
        var applied = profile.CodecProfiles.Any(p => p.Type == CodecType.Audio
            && p.Codec is null
            && p.Container is null
            && p.Conditions.Any(c => c.Condition == stereo.Condition && c.Property == stereo.Property && string.Equals(c.Value, stereo.Value, StringComparison.Ordinal)));
        if (applied)
        {
            return;
        }

        profile.CodecProfiles =
        [
            .. profile.CodecProfiles,
            new CodecProfile { Type = CodecType.VideoAudio, Conditions = [stereo] },
            new CodecProfile { Type = CodecType.Audio, Conditions = [stereo] }
        ];

        foreach (var transcodingProfile in profile.TranscodingProfiles)
        {
            transcodingProfile.MaxAudioChannels = "2";
        }
    }

    private static LakeHpPreferences Copy(LakeHpPreferences p) => new()
    {
        UserId = p.UserId,
        Mode = p.Mode,
        Devices = p.Devices.Select(d => new LakeHpDevicePreference { DeviceId = d.DeviceId, Mode = d.Mode }).ToArray()
    };

    private static string GetMatrixFilter(EncodingOptions encodingOptions)
    {
        var mode = string.Equals(encodingOptions.LakeHpMatrixMode, "pl", StringComparison.OrdinalIgnoreCase) ? "pl" : "plii";
        return string.Format(
            CultureInfo.InvariantCulture,
            "matrixenc=mode={0}:lfe={1}",
            mode,
            encodingOptions.LakeHpMatrixLfe ? "0.707" : "0");
    }

    private string? GetHeadphoneFilter(EncodingOptions encodingOptions)
    {
        var path = GetFilterPath();
        if (path is null)
        {
            return null;
        }

        // ffmpeg takes forward slashes on Windows too; quotes cannot be passed through -af "..."
        path = path.Replace('\\', '/');
        if (path.IndexOfAny(['\'', '"']) >= 0)
        {
            _logger.LogWarning("LakeHP: the filter path {Path} contains quotes, which ffmpeg cannot be given here", path);
            return null;
        }

        var room = Math.Clamp(encodingOptions.LakeHpRoom, 1, 3);
        var surround = Math.Clamp(encodingOptions.LakeHpSurroundAzimuth, 90f, 165f);
        var rear = Math.Clamp(encodingOptions.LakeHpRearAzimuth, 90f, 165f);

        return string.Format(
            CultureInfo.InvariantCulture,
            "lakehp=file='{0}':room={1}:saz={2:0.#}:raz={3:0.#}",
            path,
            room,
            surround,
            rear);
    }

    private LakeHpOutputMode GetWantedMode(Guid userId, string? deviceId)
    {
        var p = GetPreferences(userId);
        var device = string.IsNullOrEmpty(deviceId)
            ? LakeHpDeviceMode.Default
            : p.Devices.FirstOrDefault(d => string.Equals(d.DeviceId, deviceId, StringComparison.Ordinal))?.Mode ?? LakeHpDeviceMode.Default;

        return device switch
        {
            LakeHpDeviceMode.Off => LakeHpOutputMode.Off,
            LakeHpDeviceMode.Headphones => LakeHpOutputMode.Headphones,
            LakeHpDeviceMode.MatrixSurround => LakeHpOutputMode.MatrixSurround,
            _ => p.Mode
        };
    }

    private Dictionary<Guid, LakeHpPreferences> Load()
    {
        if (_prefs is not null)
        {
            return _prefs;
        }

        _prefs = [];
        try
        {
            if (File.Exists(StorePath))
            {
                var list = JsonSerializer.Deserialize<LakeHpPreferences[]>(File.ReadAllText(StorePath), _jsonOptions) ?? [];
                foreach (var p in list)
                {
                    p.Devices ??= [];
                    _prefs[p.UserId] = p;
                }
            }
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            _logger.LogError(ex, "LakeHP: could not read {Path}; starting with no saved settings", StorePath);
        }

        return _prefs;
    }

    private void Save(Dictionary<Guid, LakeHpPreferences> prefs)
    {
        var tmp = StorePath + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(prefs.Values.ToArray(), _jsonOptions));
        File.Move(tmp, StorePath, true);
    }
}
