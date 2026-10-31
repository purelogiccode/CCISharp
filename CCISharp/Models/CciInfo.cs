using System.Runtime.InteropServices;

namespace CCISharp;

/// <summary>Progress reported by compression, extraction and validation.</summary>
[StructLayout(LayoutKind.Auto)]
public readonly record struct CciProgress(
    long SectorsDone,
    long TotalSectors,
    long BytesIn,
    long BytesOut,
    int PartIndex)
{
    /// <summary>Completion in the range 0..1, or 1 when the total is unknown/zero.</summary>
    public double Fraction => TotalSectors <= 0 ? 1.0 : Math.Clamp((double)SectorsDone / TotalSectors, 0.0, 1.0);
}

/// <summary>Metadata for one part of a CCI image.</summary>
public sealed record CciPartInfo(
    string Path,
    ulong StartSector,
    ulong SectorCount,
    ulong UncompressedSize,
    long CompressedSize,
    ulong IndexOffset)
{
    /// <summary>Compressed size divided by uncompressed size.</summary>
    public double Ratio => UncompressedSize == 0 ? 0 : (double)CompressedSize / UncompressedSize;
}

/// <summary>One issue found by <see cref="CciFile.Validate"/>.</summary>
public sealed record CciValidationIssue(int PartIndex, ulong? Sector, string Reason);

/// <summary>Result of <see cref="CciFile.Validate"/>.</summary>
public sealed record CciValidationReport(
    bool IsValid,
    IReadOnlyList<CciPartInfo> Parts,
    ulong TotalSectors,
    ulong UncompressedSize,
    long CompressedSize,
    double Ratio,
    IReadOnlyList<CciValidationIssue> Issues);
