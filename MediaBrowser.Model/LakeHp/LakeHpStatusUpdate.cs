namespace MediaBrowser.Model.LakeHp;

/// <summary>
/// A change to the calling user's surround output settings. Fields left null are not changed.
/// </summary>
public class LakeHpStatusUpdate
{
    /// <summary>
    /// Gets or sets the new account default.
    /// </summary>
    public LakeHpOutputMode? UserMode { get; set; }

    /// <summary>
    /// Gets or sets the new override for the calling device.
    /// </summary>
    public LakeHpDeviceMode? DeviceMode { get; set; }
}
