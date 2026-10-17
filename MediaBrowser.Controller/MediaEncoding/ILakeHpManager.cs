using System;
using MediaBrowser.Model.Configuration;
using MediaBrowser.Model.Dlna;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.LakeHp;

namespace MediaBrowser.Controller.MediaEncoding;

/// <summary>
/// Per-user and per-device surround output: the LakeHP headphone virtualizer (ffmpeg "lakehp")
/// or matrix surround for receivers (ffmpeg "matrixenc") instead of the normal stereo downmix.
/// </summary>
public interface ILakeHpManager
{
    /// <summary>
    /// Gets the headphone filter file that will be used, or null when none is found.
    /// </summary>
    /// <returns>The full path, or null.</returns>
    string? GetFilterPath();

    /// <summary>
    /// Gets a value indicating whether ffmpeg has the lakehp filter.
    /// </summary>
    /// <returns>True when the filter is available.</returns>
    bool IsFfmpegSupported();

    /// <summary>
    /// Gets a value indicating whether an output mode can be used on this server.
    /// </summary>
    /// <param name="mode">The output mode.</param>
    /// <returns>True when the mode can be used; always true for <see cref="LakeHpOutputMode.Off"/>.</returns>
    bool IsAvailable(LakeHpOutputMode mode);

    /// <summary>
    /// Gets the output that surround audio for this user and device gets, taking into account
    /// what the server is able to use.
    /// </summary>
    /// <param name="userId">The user id.</param>
    /// <param name="deviceId">The device id, if known.</param>
    /// <returns>The output mode in effect.</returns>
    LakeHpOutputMode GetActiveMode(Guid userId, string? deviceId);

    /// <summary>
    /// Gets the stored settings of one user.
    /// </summary>
    /// <param name="userId">The user id.</param>
    /// <returns>The settings; defaults when the user has none stored.</returns>
    LakeHpPreferences GetPreferences(Guid userId);

    /// <summary>
    /// Changes one user's settings.
    /// </summary>
    /// <param name="userId">The user id.</param>
    /// <param name="deviceId">The device the device override applies to.</param>
    /// <param name="update">The change.</param>
    void UpdatePreferences(Guid userId, string? deviceId, LakeHpStatusUpdate update);

    /// <summary>
    /// Builds the ffmpeg filter for an audio stream, or returns null when the normal downmix
    /// applies (output off, not available, or a channel layout the filters do not handle).
    /// </summary>
    /// <param name="audioStream">The source audio stream.</param>
    /// <param name="encodingOptions">The encoding options.</param>
    /// <param name="userId">The user id, if known.</param>
    /// <param name="deviceId">The device id, if known.</param>
    /// <returns>The filter string, or null.</returns>
    string? GetFilter(MediaStream? audioStream, EncodingOptions encodingOptions, Guid? userId, string? deviceId);

    /// <summary>
    /// Makes a client's device profile ask for stereo audio, so that surround sources are
    /// transcoded (and rendered) on the server instead of direct played.
    /// </summary>
    /// <param name="profile">The device profile of the current request.</param>
    void ForceStereo(DeviceProfile profile);
}
