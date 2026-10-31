namespace CCISharp.Tests;

public sealed class CciHeaderTests
{
    [Fact]
    public void RoundTrip()
    {
        CciHeader header = new(3 * CciFormat.SectorSize, 32, CciFormat.SectorSize, CciFormat.Version, CciFormat.IndexAlignment);
        Span<byte> buffer = stackalloc byte[CciFormat.HeaderSize];

        header.Write(buffer);

        Assert.Equal(CciError.None, CciHeader.TryRead(buffer, out CciHeader parsed));
        Assert.Equal(header, parsed);
        Assert.Equal(3UL, parsed.SectorCount);
    }

    [Fact]
    public void RejectsBadMagic()
    {
        Span<byte> buffer = stackalloc byte[CciFormat.HeaderSize];
        new CciHeader(CciFormat.SectorSize, 32, CciFormat.SectorSize, CciFormat.Version, CciFormat.IndexAlignment).Write(buffer);
        buffer[0] ^= 0xFF;

        Assert.Equal(CciError.InvalidHeader, CciHeader.TryRead(buffer, out _));
    }

    [Fact]
    public void RejectsBadHeaderSize()
    {
        Span<byte> buffer = stackalloc byte[CciFormat.HeaderSize];
        new CciHeader(CciFormat.SectorSize, 32, CciFormat.SectorSize, CciFormat.Version, CciFormat.IndexAlignment).Write(buffer);
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(buffer[4..], 31);

        Assert.Equal(CciError.UnsupportedHeaderSize, CciHeader.TryRead(buffer, out _));
    }

    [Fact]
    public void RejectsBadBlockSize()
    {
        Span<byte> buffer = stackalloc byte[CciFormat.HeaderSize];
        new CciHeader(CciFormat.SectorSize, 32, CciFormat.SectorSize, CciFormat.Version, CciFormat.IndexAlignment).Write(buffer);
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(buffer[24..], 4096);

        Assert.Equal(CciError.UnsupportedBlockSize, CciHeader.TryRead(buffer, out _));
    }

    [Fact]
    public void RejectsBadVersion()
    {
        Span<byte> buffer = stackalloc byte[CciFormat.HeaderSize];
        new CciHeader(CciFormat.SectorSize, 32, CciFormat.SectorSize, CciFormat.Version, CciFormat.IndexAlignment).Write(buffer);
        buffer[28] = 2;

        Assert.Equal(CciError.UnsupportedVersion, CciHeader.TryRead(buffer, out _));
    }

    [Fact]
    public void RejectsBadAlignment()
    {
        Span<byte> buffer = stackalloc byte[CciFormat.HeaderSize];
        new CciHeader(CciFormat.SectorSize, 32, CciFormat.SectorSize, CciFormat.Version, CciFormat.IndexAlignment).Write(buffer);
        buffer[29] = 1;

        Assert.Equal(CciError.UnsupportedIndexAlignment, CciHeader.TryRead(buffer, out _));
    }

    [Fact]
    public void RejectsZeroSize()
    {
        Span<byte> buffer = stackalloc byte[CciFormat.HeaderSize];
        new CciHeader(0, 32, CciFormat.SectorSize, CciFormat.Version, CciFormat.IndexAlignment).Write(buffer);

        Assert.Equal(CciError.InvalidSize, CciHeader.TryRead(buffer, out _));
    }

    [Fact]
    public void RejectsTruncatedHeader()
    {
        Span<byte> buffer = stackalloc byte[CciFormat.HeaderSize - 1];

        Assert.Equal(CciError.Truncated, CciHeader.TryRead(buffer, out _));
    }
}
