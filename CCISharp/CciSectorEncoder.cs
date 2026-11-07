namespace CCISharp;

/// <summary>
/// Reusable, single-threaded CCI sector encoder. One instance belongs to one
/// worker: it owns the fast encoder's hash table, the HC match finder and the
/// scratch buffer, so compressing a whole image does not allocate per sector.
/// </summary>
internal sealed class CciSectorEncoder : IDisposable
{
    private const int SectorSize = CciFormat.SectorSize;

    private readonly int _level;
    private readonly int _acceleration;
    private readonly int[]? _hashTable;
    private readonly Lz4HcEncoder? _hc;
    private readonly byte[] _scratch = new byte[Lz4.MaxCompressedOutputSize(SectorSize)];

    public CciSectorEncoder(int level)
    {
        _level = level;
        if (level >= 10)
        {
            _hc = new Lz4HcEncoder();
        }
        else if (level > 0)
        {
            _hashTable = new int[Lz4.HashTableSize];
            _acceleration = CciWriter.Acceleration(level);
        }
    }

    /// <summary>
    /// Encodes a sector into <paramref name="destination"/> (at least 2048
    /// bytes). Level 0 stores the sector raw.
    /// </summary>
    public CciError Encode(
        ReadOnlySpan<byte> sector,
        Span<byte> destination,
        out int written,
        out bool compressed)
    {
        written = 0;
        compressed = false;

        if (sector.Length != SectorSize || destination.Length < SectorSize)
        {
            return CciError.InvalidArgument;
        }

        if (_level <= 0)
        {
            sector.CopyTo(destination);
            written = SectorSize;
            return CciError.None;
        }

        int compressedLength;
        try
        {
            compressedLength = _hc is not null
                ? _hc.Compress(sector, _scratch, _level)
                : Lz4.Compress(sector, _scratch, _acceleration, _hashTable!);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidDataException)
        {
            compressedLength = 0;
        }

        // Reference threshold: compressed < 2048 - (4 + 4).
        if (compressedLength > 0 && compressedLength < SectorSize - (4 + (1 << CciFormat.IndexAlignment)))
        {
            int pad = AlignUp(compressedLength + 1, 1 << CciFormat.IndexAlignment) - (compressedLength + 1);
            int span = compressedLength + 1 + pad;

            destination[0] = (byte)pad;
            _scratch.AsSpan(0, compressedLength).CopyTo(destination[1..]);
            destination.Slice(1 + compressedLength, pad).Clear();

            written = span;
            compressed = true;
            return CciError.None;
        }

        sector.CopyTo(destination);
        written = SectorSize;
        return CciError.None;
    }

    private static int AlignUp(int value, int alignment) => (value + alignment - 1) / alignment * alignment;

    /// <summary>Releases the encoder. All state is managed memory; kept for <c>using</c> scoping.</summary>
    public void Dispose()
    {
        // Nothing unmanaged to release.
    }
}
