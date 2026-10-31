namespace CCISharp;

/// <summary>
/// Constants from the CCI ("CCIM") format specification
/// (<c>FORMAT.md</c> in Team-Resurgent/libCCI).
/// </summary>
public static class CciFormat
{
    /// <summary>CCI magic <c>0x4D494343</c> (ASCII <c>CCIM</c>).</summary>
    public const uint Magic = 0x4D494343u;

    /// <summary>Header size in bytes.</summary>
    public const int HeaderSize = 32;

    /// <summary>Logical (XGD) sector size in bytes.</summary>
    public const int SectorSize = 2048;

    /// <summary>The only defined format version.</summary>
    public const byte Version = 1;

    /// <summary>Index positions are stored shifted right by this value (4-byte alignment).</summary>
    public const byte IndexAlignment = 2;

    /// <summary>
    /// Default split point (~4 GiB, <c>0xFFBF6000</c>): safely below FATX's
    /// 32-bit <c>0xFFFFFFFF</c> per-file cap and consistent with CISO's split point.
    /// </summary>
    public const long DefaultSplitBytes = 0xFFBF6000L;

    /// <summary>Largest byte offset representable by a 31-bit index position.</summary>
    internal const long MaxRepresentablePosition = 0x7FFFFFFFL << IndexAlignment;

    /// <summary>File extension used by CCI images.</summary>
    public const string Extension = ".cci";
}
