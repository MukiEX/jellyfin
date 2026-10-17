#pragma warning disable CA1819 // stored as JSON, arrays keep it simple

using System;

namespace MediaBrowser.Model.LakeHp;

/// <summary>
/// One user's surround output settings: an account default plus per-device overrides.
/// </summary>
public class LakeHpPreferences
{
    /// <summary>
    /// Gets or sets the user id.
    /// </summary>
    public Guid UserId { get; set; }

    /// <summary>
    /// Gets or sets the output used on this user's devices by default.
    /// </summary>
    public LakeHpOutputMode Mode { get; set; }

    /// <summary>
    /// Gets or sets the per-device overrides.
    /// </summary>
    public LakeHpDevicePreference[] Devices { get; set; } = [];
}
