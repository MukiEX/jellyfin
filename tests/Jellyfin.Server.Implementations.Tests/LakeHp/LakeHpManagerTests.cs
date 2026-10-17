using System;
using System.IO;
using System.Linq;
using Emby.Server.Implementations.LakeHp;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Configuration;
using MediaBrowser.Controller.MediaEncoding;
using MediaBrowser.Model.Configuration;
using MediaBrowser.Model.Dlna;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.LakeHp;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Jellyfin.Server.Implementations.Tests.LakeHp;

public sealed class LakeHpManagerTests : IDisposable
{
    private static readonly Guid _user = Guid.Parse("8d1c9a4c-0f4e-4d62-9a8d-6f1a2b3c4d5e");
    private readonly string _root;
    private readonly EncodingOptions _encoding = new();

    public LakeHpManagerTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "lakehp-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(_root, "config"));
        Directory.CreateDirectory(Path.Combine(_root, "lakehp"));
    }

    public void Dispose() => Directory.Delete(_root, true);

    [Fact]
    public void GetFilter_UserOff_ReturnsNull()
    {
        AddFilterFile();
        var m = Create();

        Assert.Null(m.GetFilter(Stream(6, "5.1"), _encoding, _user, "dev1"));
        Assert.Equal(LakeHpOutputMode.Off, m.GetActiveMode(_user, "dev1"));
    }

    [Fact]
    public void GetFilter_Headphones_BuildsFilter()
    {
        var path = AddFilterFile();
        var m = Create();
        m.UpdatePreferences(_user, "dev1", new LakeHpStatusUpdate { UserMode = LakeHpOutputMode.Headphones });
        _encoding.LakeHpRoom = 3;
        _encoding.LakeHpSurroundAzimuth = 136;
        _encoding.LakeHpRearAzimuth = 150;

        var ffmpegPath = path.Replace('\\', '/');
        Assert.Equal($"lakehp=file='{ffmpegPath}':room=3:saz=136:raz=150", m.GetFilter(Stream(8, "7.1"), _encoding, _user, "dev1"));
        Assert.Equal(LakeHpOutputMode.Headphones, m.GetActiveMode(_user, "dev1"));
    }

    [Theory]
    [InlineData("plii", true, "matrixenc=mode=plii:lfe=0.707")]
    [InlineData("pl", false, "matrixenc=mode=pl:lfe=0")]
    public void GetFilter_MatrixSurround_BuildsFilter(string matrixMode, bool lfe, string expected)
    {
        var m = Create();
        m.UpdatePreferences(_user, "receiver", new LakeHpStatusUpdate { DeviceMode = LakeHpDeviceMode.MatrixSurround });
        _encoding.LakeHpMatrixMode = matrixMode;
        _encoding.LakeHpMatrixLfe = lfe;

        Assert.Equal(expected, m.GetFilter(Stream(6, "5.1(side)"), _encoding, _user, "receiver"));
        Assert.Null(m.GetFilter(Stream(6, "5.1(side)"), _encoding, _user, "phone"));
    }

    [Theory]
    [InlineData(2, "stereo")]
    [InlineData(7, "6.1")]
    public void GetFilter_UnsupportedLayout_ReturnsNull(int channels, string layout)
    {
        AddFilterFile();
        var m = Create();
        m.UpdatePreferences(_user, null, new LakeHpStatusUpdate { UserMode = LakeHpOutputMode.Headphones });

        Assert.Null(m.GetFilter(Stream(channels, layout), _encoding, _user, "dev1"));
    }

    [Fact]
    public void DeviceOverrides_WinOverUserSetting()
    {
        AddFilterFile();
        var m = Create();
        m.UpdatePreferences(_user, "tv", new LakeHpStatusUpdate { UserMode = LakeHpOutputMode.Headphones, DeviceMode = LakeHpDeviceMode.Off });
        m.UpdatePreferences(_user, "receiver", new LakeHpStatusUpdate { DeviceMode = LakeHpDeviceMode.MatrixSurround });

        Assert.Equal(LakeHpOutputMode.Off, m.GetActiveMode(_user, "tv"));
        Assert.Equal(LakeHpOutputMode.MatrixSurround, m.GetActiveMode(_user, "receiver"));
        Assert.Equal(LakeHpOutputMode.Headphones, m.GetActiveMode(_user, "laptop"));

        m.UpdatePreferences(_user, "laptop", new LakeHpStatusUpdate { UserMode = LakeHpOutputMode.Off });
        Assert.Equal(LakeHpOutputMode.MatrixSurround, m.GetActiveMode(_user, "receiver"));
        Assert.Equal(LakeHpOutputMode.Off, m.GetActiveMode(_user, "laptop"));

        m.UpdatePreferences(_user, "tv", new LakeHpStatusUpdate { DeviceMode = LakeHpDeviceMode.Default });
        Assert.DoesNotContain(m.GetPreferences(_user).Devices, d => d.DeviceId == "tv");
    }

    [Fact]
    public void Preferences_ArePersisted()
    {
        AddFilterFile();
        Create().UpdatePreferences(_user, "phone", new LakeHpStatusUpdate { DeviceMode = LakeHpDeviceMode.Headphones });

        Assert.Equal(LakeHpOutputMode.Headphones, Create().GetActiveMode(_user, "phone"));
    }

    [Fact]
    public void NoFilterFile_HeadphonesUnavailable_MatrixStillWorks()
    {
        var m = Create();
        m.UpdatePreferences(_user, "phone", new LakeHpStatusUpdate { UserMode = LakeHpOutputMode.Headphones });
        m.UpdatePreferences(_user, "receiver", new LakeHpStatusUpdate { DeviceMode = LakeHpDeviceMode.MatrixSurround });

        Assert.Null(m.GetFilterPath());
        Assert.False(m.IsAvailable(LakeHpOutputMode.Headphones));
        Assert.Equal(LakeHpOutputMode.Off, m.GetActiveMode(_user, "phone"));
        Assert.Null(m.GetFilter(Stream(6, "5.1"), _encoding, _user, "phone"));
        Assert.Equal(LakeHpOutputMode.MatrixSurround, m.GetActiveMode(_user, "receiver"));
    }

    [Fact]
    public void NoFfmpegFilters_NothingAvailable()
    {
        AddFilterFile();
        var m = Create(ffmpegSupported: false);
        m.UpdatePreferences(_user, null, new LakeHpStatusUpdate { UserMode = LakeHpOutputMode.MatrixSurround });

        Assert.False(m.IsAvailable(LakeHpOutputMode.Headphones));
        Assert.False(m.IsAvailable(LakeHpOutputMode.MatrixSurround));
        Assert.Equal(LakeHpOutputMode.Off, m.GetActiveMode(_user, "dev1"));
        Assert.Null(m.GetFilter(Stream(6, "5.1"), _encoding, _user, "dev1"));
    }

    [Fact]
    public void ForceStereo_AddsConditionsOnce()
    {
        var profile = new DeviceProfile
        {
            TranscodingProfiles = [new TranscodingProfile { Type = DlnaProfileType.Video, MaxAudioChannels = "6" }]
        };
        var m = Create();

        m.ForceStereo(profile);
        m.ForceStereo(profile);

        Assert.Equal(2, profile.CodecProfiles.Length);
        Assert.All(profile.CodecProfiles, p => Assert.Equal("2", p.Conditions.Single().Value));
        Assert.Equal("2", profile.TranscodingProfiles[0].MaxAudioChannels);
    }

    private static MediaStream Stream(int channels, string layout)
        => new() { Type = MediaStreamType.Audio, Channels = channels, ChannelLayout = layout };

    private string AddFilterFile()
    {
        var path = Path.Combine(_root, "lakehp", "filters.dll");
        File.WriteAllBytes(path, [0x4d, 0x5a]);
        return path;
    }

    private LakeHpManager Create(bool ffmpegSupported = true)
    {
        var paths = new Mock<IServerApplicationPaths>();
        paths.SetupGet(p => p.ProgramDataPath).Returns(_root);
        paths.SetupGet(p => p.ConfigurationDirectoryPath).Returns(Path.Combine(_root, "config"));

        var config = new Mock<IServerConfigurationManager>();
        config.SetupGet(c => c.ApplicationPaths).Returns(paths.Object);
        config.Setup(c => c.GetConfiguration("encoding")).Returns(_encoding);

        var encoder = new Mock<IMediaEncoder>();
        encoder.Setup(e => e.SupportsFilter("lakehp")).Returns(ffmpegSupported);
        encoder.Setup(e => e.SupportsFilter("matrixenc")).Returns(ffmpegSupported);

        return new LakeHpManager(config.Object, encoder.Object, NullLogger<LakeHpManager>.Instance);
    }
}
