using System.Buffers.Binary;

namespace CCISharp.Tests;

internal static class TestImages
{
    public static byte[] BuildMixed(int sectors)
    {
        byte[] raw = new byte[sectors * CciFormat.SectorSize];
        uint state = 0x1234abcdu;

        for (int sector = 0; sector < sectors; sector++)
        {
            Span<byte> span = raw.AsSpan(sector * CciFormat.SectorSize, CciFormat.SectorSize);

            if (sector % 3 == 0)
            {
                span.Clear();
            }
            else if (sector % 3 == 1)
            {
                span.Fill((byte)((sector * 7) + 1));
            }
            else
            {
                for (int i = 0; i < span.Length; i++)
                {
                    state = (state * 1103515245u) + 12345u;
                    span[i] = (byte)(state >> 17);
                }
            }
        }

        return raw;
    }

    public static byte[] BuildRandom(int sectors)
    {
        byte[] raw = new byte[sectors * CciFormat.SectorSize];
        uint state = 0xC0FFEEu;

        for (int i = 0; i < raw.Length; i++)
        {
            state = (state * 1664525u) + 1013904223u;
            raw[i] = (byte)(state >> 16);
        }

        return raw;
    }

    public static uint IndexEntry(byte[] file, ulong indexOffset, int entry)
    {
        int offset = checked((int)indexOffset + (entry * 4));
        return BinaryPrimitives.ReadUInt32LittleEndian(file.AsSpan(offset, 4));
    }

    public static ulong IndexOffset(byte[] file) =>
        BinaryPrimitives.ReadUInt64LittleEndian(file.AsSpan(16, 8));

    public static void WriteUInt32(byte[] file, int offset, uint value) =>
        BinaryPrimitives.WriteUInt32LittleEndian(file.AsSpan(offset, 4), value);

    public static void WriteUInt64(byte[] file, int offset, ulong value) =>
        BinaryPrimitives.WriteUInt64LittleEndian(file.AsSpan(offset, 8), value);
}
