namespace MediaBrowser.Model.LakeHp;

/// <summary>
/// The surround output chosen for one device.
/// </summary>
public enum LakeHpDeviceMode
{
    /// <summary>
    /// Follow the user's account setting.
    /// </summary>
    Default = 0,

    /// <summary>
    /// The normal downmix on this device.
    /// </summary>
    Off = 1,

    /// <summary>
    /// The headphone virtualizer on this device.
    /// </summary>
    Headphones = 2,

    /// <summary>
    /// Matrix surround on this device.
    /// </summary>
    MatrixSurround = 3
}
