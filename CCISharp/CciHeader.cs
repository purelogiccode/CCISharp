using System.Buffers.Binary;

using System.Runtime.InteropServices;

namespace CCISharp;

/// <summary>
/// The 32-byte CCI part header.
/// </summary>
[StructLayout(LayoutKind.Auto)]
public readonly record struct CciHeader(
    ulong UncompressedSize,
    ulong IndexOffset,
    uint BlockSize,
    byte Version,
    byte IndexAlignment)
{
    /// <summary>Number of 2048-byte logical sectors in this part.</summary>
    public ulong SectorCount => UncompressedSize / CciFormat.SectorSize;

    /// <summary>
    /// Parses and validates a 32-byte CCI header.
    /// </summary>
    public static CciError TryRead(ReadOnlySpan<byte> source, out CciHeader header)
    {
        header = default;
        if (source.Length < CciFormat.HeaderSize)
        {
            return CciError.Truncated;
        }

        uint magic = BinaryPrimitives.ReadUInt32LittleEndian(source);
        if (magic != CciFormat.Magic)
        {
            return CciError.InvalidHeader;
        }

        uint headerSize = BinaryPrimitives.ReadUInt32LittleEndian(source[4..]);
        if (headerSize != CciFormat.HeaderSize)
        {
            return CciError.UnsupportedHeaderSize;
        }

        ulong uncompressedSize = BinaryPrimitives.ReadUInt64LittleEndian(source[8..]);
        if (uncompressedSize == 0 || uncompressedSize % CciFormat.SectorSize != 0)
        {
            return CciError.InvalidSize;
        }

        ulong indexOffset = BinaryPrimitives.ReadUInt64LittleEndian(source[16..]);
        if (indexOffset < CciFormat.HeaderSize)
        {
            return CciError.InvalidIndex;
        }

        uint blockSize = BinaryPrimitives.ReadUInt32LittleEndian(source[24..]);
        if (blockSize != CciFormat.SectorSize)
        {
            return CciError.UnsupportedBlockSize;
        }

        byte version = source[28];
        if (version != CciFormat.Version)
        {
            return CciError.UnsupportedVersion;
        }

        byte indexAlignment = source[29];
        if (indexAlignment != CciFormat.IndexAlignment)
        {
            return CciError.UnsupportedIndexAlignment;
        }

        header = new CciHeader(uncompressedSize, indexOffset, blockSize, version, indexAlignment);
        return CciError.None;
    }

    /// <summary>Writes this header into a 32-byte destination.</summary>
    public void Write(Span<byte> destination)
    {
        if (destination.Length < CciFormat.HeaderSize)
        {
            throw new ArgumentException($"Destination must be at least {CciFormat.HeaderSize} bytes.", nameof(destination));
        }

        destination[..CciFormat.HeaderSize].Clear();
        BinaryPrimitives.WriteUInt32LittleEndian(destination, CciFormat.Magic);
        BinaryPrimitives.WriteUInt32LittleEndian(destination[4..], CciFormat.HeaderSize);
        BinaryPrimitives.WriteUInt64LittleEndian(destination[8..], UncompressedSize);
        BinaryPrimitives.WriteUInt64LittleEndian(destination[16..], IndexOffset);
        BinaryPrimitives.WriteUInt32LittleEndian(destination[24..], CciFormat.SectorSize);
        destination[28] = Version;
        destination[29] = IndexAlignment;
    }
}
