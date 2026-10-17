namespace MediaBrowser.Model.LakeHp;

/// <summary>
/// How surround audio is folded to stereo for a user or device.
/// </summary>
public enum LakeHpOutputMode
{
    /// <summary>
    /// The normal downmix.
    /// </summary>
    Off = 0,

    /// <summary>
    /// The LakeHP headphone virtualizer: binaural stereo for headphones.
    /// </summary>
    Headphones = 1,

    /// <summary>
    /// Matrix surround (Pro Logic II compatible Lt/Rt) for a receiver with a matrix decoder.
    /// </summary>
    MatrixSurround = 2
}
