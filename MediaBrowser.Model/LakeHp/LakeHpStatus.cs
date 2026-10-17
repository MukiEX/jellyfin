namespace MediaBrowser.Model.LakeHp;

/// <summary>
/// The surround output settings as seen by the calling user and device.
/// </summary>
public class LakeHpStatus
{
    /// <summary>
    /// Gets or sets a value indicating whether the headphone virtualizer can be used (filters found and ffmpeg supports it).
    /// </summary>
    public bool HeadphonesAvailable { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether matrix surround can be used (ffmpeg supports it).
    /// </summary>
    public bool MatrixSurroundAvailable { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether a headphone filter file was found.
    /// </summary>
    public bool FilterFound { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether ffmpeg has the lakehp filter.
    /// </summary>
    public bool FfmpegSupported { get; set; }

    /// <summary>
    /// Gets or sets the filter file in use, for administrators. Null for other users.
    /// </summary>
    public string? FilterPath { get; set; }

    /// <summary>
    /// Gets or sets the account default of this user.
    /// </summary>
    public LakeHpOutputMode UserMode { get; set; }

    /// <summary>
    /// Gets or sets the override for the calling device.
    /// </summary>
    public LakeHpDeviceMode DeviceMode { get; set; }

    /// <summary>
    /// Gets or sets the output surround audio played on the calling device will actually get.
    /// </summary>
    public LakeHpOutputMode ActiveMode { get; set; }
}
