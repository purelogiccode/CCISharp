namespace CCISharp;

/// <summary>
/// Decodes one 2048-byte CCI sector block.
/// </summary>
internal static class CciSectorCodec
{
    private const int SectorSize = CciFormat.SectorSize;

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
