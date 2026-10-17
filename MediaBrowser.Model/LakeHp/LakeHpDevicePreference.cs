namespace MediaBrowser.Model.LakeHp;

/// <summary>
/// A per-device override of the surround output.
/// </summary>
public class LakeHpDevicePreference
{
    /// <summary>
    /// Gets or sets the device id.
    /// </summary>
    public string DeviceId { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the mode for this device.
    /// </summary>
    public LakeHpDeviceMode Mode { get; set; }
}
