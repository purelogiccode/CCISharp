using System.Buffers.Binary;

namespace CCISharp;

/// <summary>
/// The random-access index of one CCI part: <c>(sectors + 1)</c> little-endian
/// <see cref="uint"/> entries.
/// </summary>
internal sealed class CciIndex
{
    private readonly uint[] _entries;

    private CciIndex(uint[] entries)
    {
        _entries = entries;
    }

    /// <summary>Number of logical sectors covered by this index.</summary>
    public int SectorCount => _entries.Length - 1;

    /// <summary>Encoded entry at <paramref name="index"/>.</summary>
    public uint this[int index] => _entries[index];

    /// <summary>Real byte offset encoded in an index entry.</summary>
    public static long Position(uint entry) => (long)(entry & 0x7FFFFFFFu) << CciFormat.IndexAlignment;

    /// <summary>Whether an index entry marks an LZ4-compressed block.</summary>
    public static bool IsLz4(uint entry) => (entry & 0x80000000u) != 0;

    /// <summary>
    /// Parses and validates the raw index bytes. The caller must already have
    /// checked that the index fits inside the part.
    /// </summary>
    public static CciError TryRead(
        ReadOnlySpan<byte> raw,
        int sectorCount,
        ulong indexOffset,
        int maxSectorSpan,
        bool validate,
        out CciIndex? index)
    {
        index = null;
        if (sectorCount < 0 || raw.Length != ((long)sectorCount + 1) * 4)
        {
            return CciError.InvalidIndex;
        }

        uint[] entries = new uint[sectorCount + 1];
        for (int i = 0; i < entries.Length; i++)
        {
            entries[i] = BinaryPrimitives.ReadUInt32LittleEndian(raw[(i * 4)..]);
        }

        if (validate)
        {
            if (entries.Length == 0 || Position(entries[0]) < CciFormat.HeaderSize)
            {
                return CciError.InvalidIndex;
            }

            long previous = Position(entries[0]);
            for (int i = 1; i < entries.Length; i++)
            {
                long current = Position(entries[i]);
                long span = current - previous;
                bool lz4 = IsLz4(entries[i - 1]);

                if (current < previous || span <= 0 || span > maxSectorSpan)
                {
                    return CciError.InvalidIndex;
                }

                if (!lz4 && span != CciFormat.SectorSize)
                {
                    return CciError.InvalidIndex;
                }

                if (lz4 && span < 2)
                {
                    return CciError.InvalidIndex;
                }

                previous = current;
            }

            // The sentinel is the end offset of the last block. It must not run
            // past the index itself (allow a small gap for non-reference writers).
            if ((ulong)previous > indexOffset)
            {
                return CciError.InvalidIndex;
            }
        }

        index = new CciIndex(entries);
        return CciError.None;
    }

    /// <summary>Resolves the byte span of one local sector.</summary>
    public bool TryGetSpan(int sector, int maxSectorSpan, out long offset, out int length, out bool lz4)
    {
        offset = 0;
        length = 0;
        lz4 = false;

        if (sector < 0 || sector >= SectorCount)
        {
            return false;
        }

        offset = Position(_entries[sector]);
        long end = Position(_entries[sector + 1]);
        long span = end - offset;
        lz4 = IsLz4(_entries[sector]);

        if (offset < CciFormat.HeaderSize || span <= 0 || span > maxSectorSpan)
        {
            return false;
        }

        if (!lz4 && span != CciFormat.SectorSize)
        {
            return false;
        }

        if (lz4 && span < 2)
        {
            return false;
        }

        length = (int)span;
        return true;
    }
}
