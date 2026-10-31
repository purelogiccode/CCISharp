using System.Buffers;

namespace CCISharp;

/// <summary>
/// Encodes and decodes one 2048-byte CCI sector block.
/// </summary>
internal static class CciSectorCodec
{
    private const int SectorSize = CciFormat.SectorSize;

    private static int AlignUp(int value, int alignment) => (value + alignment - 1) / alignment * alignment;

    /// <summary>
    /// Encodes a sector into <paramref name="destination"/> (at least 2048 bytes).
    /// <paramref name="acceleration"/> 0 stores the sector raw.
    /// </summary>
    public static CciError TryEncode(
        ReadOnlySpan<byte> sector,
        Span<byte> destination,
        int acceleration,
        int[] hashTable,
        out int written,
        out bool compressed)
    {
        written = 0;
        compressed = false;

        if (sector.Length != SectorSize || destination.Length < SectorSize)
        {
            return CciError.InvalidArgument;
        }

        if (acceleration <= 0)
        {
            sector.CopyTo(destination);
            written = SectorSize;
            return CciError.None;
        }

        int max = Lz4.MaxCompressedOutputSize(SectorSize);
        byte[] scratch = ArrayPool<byte>.Shared.Rent(max);
        try
        {
            int compressedLength;
            try
            {
                compressedLength = Lz4.Compress(sector, scratch.AsSpan(0, max), acceleration, hashTable);
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
                scratch.AsSpan(0, compressedLength).CopyTo(destination[1..]);
                destination.Slice(1 + compressedLength, pad).Clear();

                written = span;
                compressed = true;
                return CciError.None;
            }

            sector.CopyTo(destination);
            written = SectorSize;
            return CciError.None;
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(scratch);
        }
    }

    /// <summary>Decodes one sector block into a 2048-byte destination.</summary>
    public static CciError TryDecode(ReadOnlySpan<byte> blob, bool lz4, Span<byte> sector)
    {
        if (sector.Length < SectorSize)
        {
            return CciError.InvalidArgument;
        }

        if (!lz4)
        {
            if (blob.Length != SectorSize)
            {
                return CciError.CorruptSector;
            }

            blob.CopyTo(sector);
            return CciError.None;
        }

        if (blob.Length < 2)
        {
            return CciError.CorruptSector;
        }

        int pad = blob[0];
        if (pad > 3)
        {
            return CciError.CorruptSector;
        }

        int compressedLength = blob.Length - 1 - pad;
        if (compressedLength < 1)
        {
            return CciError.CorruptSector;
        }

        try
        {
            int decoded = Lz4.Decompress(blob.Slice(1, compressedLength), sector);
            return decoded == SectorSize ? CciError.None : CciError.CorruptSector;
        }
        catch (InvalidDataException)
        {
            return CciError.CorruptSector;
        }
    }
}
