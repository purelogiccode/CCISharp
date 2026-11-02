using System.Buffers.Binary;

namespace CCISharp.Tests;

public sealed class CciCorruptionTests
{
    [Fact]
    public void BadMagic_IsRejected()
    {
        using TempDir dir = new();
        (string path, byte[] bytes) = CreateValid(dir);

        bytes[0] ^= 0xFF;
        File.WriteAllBytes(path, bytes);

        Assert.Equal(CciError.InvalidHeader, CciFile.Open(path, out _));
    }

    [Fact]
    public void TruncatedHeader_IsRejected()
    {
        using TempDir dir = new();
        (string path, byte[] bytes) = CreateValid(dir);

        File.WriteAllBytes(path, bytes.AsSpan(0, 10).ToArray());

        Assert.Equal(CciError.Truncated, CciFile.Open(path, out _));
    }

    [Fact]
    public void IndexOffsetBeyondEof_IsRejected()
    {
        using TempDir dir = new();
        (string path, byte[] bytes) = CreateValid(dir);

        TestImages.WriteUInt64(bytes, 16, (ulong)bytes.Length + 100);
        File.WriteAllBytes(path, bytes);

        Assert.Equal(CciError.InvalidIndex, CciFile.Open(path, out _));
    }

    [Fact]
    public void NonMonotonicIndex_IsRejected()
    {
        using TempDir dir = new();
        (string path, byte[] bytes) = CreateValid(dir);
        ulong indexOffset = TestImages.IndexOffset(bytes);

        TestImages.WriteUInt32(bytes, checked((int)indexOffset + 4), 0);
        File.WriteAllBytes(path, bytes);

        Assert.Equal(CciError.InvalidIndex, CciFile.Open(path, out _));
    }

    [Fact]
    public void TruncatedIndex_IsRejected()
    {
        using TempDir dir = new();
        (string path, byte[] bytes) = CreateValid(dir);

        File.WriteAllBytes(path, bytes.AsSpan(0, bytes.Length - 4).ToArray());

        Assert.Equal(CciError.InvalidIndex, CciFile.Open(path, out _));
    }

    [Fact]
    public void CorruptCompressedSector_IsReportedOnRead()
    {
        using TempDir dir = new();
        byte[] raw = new byte[8 * CciFormat.SectorSize];
        string source = dir.File("source.iso");
        File.WriteAllBytes(source, raw);
        string path = dir.File("game.cci");

        Assert.Equal(CciError.None, CciWriter.Compress(source, path, new CciWriteOptions { SplitBytes = 0 }));

        byte[] bytes = File.ReadAllBytes(path);
        ulong indexOffset = TestImages.IndexOffset(bytes);
        uint entry = TestImages.IndexEntry(bytes, indexOffset, 0);

        Assert.NotEqual(0u, entry & 0x80000000u);

        int blockOffset = (int)(entry & 0x7FFFFFFFu) << CciFormat.IndexAlignment;
        bytes[blockOffset] = 0xFF;
        File.WriteAllBytes(path, bytes);

        Assert.Equal(CciError.None, CciFile.Open(path, out CciFile? opened));
        using CciFile file = opened!;

        byte[] sector = new byte[CciFormat.SectorSize];
        Assert.Equal(CciError.CorruptSector, file.ReadSector(0, sector));
    }

    [Fact]
    public void EmptyFile_IsRejected()
    {
        using TempDir dir = new();
        string path = dir.File("empty.cci");
        File.WriteAllBytes(path, []);

        Assert.Equal(CciError.Truncated, CciFile.Open(path, out _));
    }

    [Fact]
    public void Validate_DetectsCorruptSector()
    {
        using TempDir dir = new();
        byte[] raw = new byte[8 * CciFormat.SectorSize];
        string source = dir.File("source.iso");
        File.WriteAllBytes(source, raw);
        string path = dir.File("game.cci");

        Assert.Equal(CciError.None, CciWriter.Compress(source, path, new CciWriteOptions { SplitBytes = 0 }));

        byte[] bytes = File.ReadAllBytes(path);
        ulong indexOffset = TestImages.IndexOffset(bytes);
        uint entry = TestImages.IndexEntry(bytes, indexOffset, 0);
        int blockOffset = (int)(entry & 0x7FFFFFFFu) << CciFormat.IndexAlignment;
        bytes[blockOffset] = 0xFF;
        File.WriteAllBytes(path, bytes);

        Assert.Equal(CciError.None, CciFile.Open(path, out CciFile? opened));
        using CciFile file = opened!;

        Assert.Equal(CciError.CorruptSector, file.Validate(deep: true, out CciValidationReport? report));
        Assert.NotNull(report);
        Assert.False(report.IsValid);
        Assert.NotEmpty(report.Issues);
    }

    [Fact]
    public void OversizedIndex_IsRejectedWithoutThrowing()
    {
        using TempDir dir = new();
        (string path, byte[] bytes) = CreateValid(dir);

        // ~34 GiB claimed (16.7M sectors) needs an index just over the 64 MiB
        // cap; a corrupt header must be rejected by the cap, not by an
        // allocation failure.
        TestImages.WriteUInt64(bytes, 8, 16_777_216UL * CciFormat.SectorSize);
        File.WriteAllBytes(path, bytes);

        Assert.Equal(CciError.InvalidIndex, CciFile.Open(path, out _));
    }

    [Fact]
    public void IndexCapOption_IsEnforced()
    {
        using TempDir dir = new();
        (string path, _) = CreateValid(dir);

        Assert.Equal(
            CciError.InvalidIndex,
            CciFile.Open(path, new CciReaderOptions { MaxIndexBytes = 16 }, out _));
    }

    [Fact]
    public void IndexLongerThanIntMax_IsRejectedWithoutThrowing()
    {
        using TempDir dir = new();
        string path = dir.File("huge.cci");

        // Sparse 2.5 GiB part whose header claims a 2.4 GiB index: the old
        // (int) cast wrapped negative and ArrayPool.Rent threw. The cap must
        // return InvalidIndex before the index is read.
        const ulong sectors = 600_000_000; // index = 2_400_000_004 bytes > int.MaxValue
        using (FileStream stream = File.Create(path))
        {
            stream.SetLength(2_500_000_000);
            byte[] header = new byte[CciFormat.HeaderSize];
            BinaryPrimitives.WriteUInt32LittleEndian(header, CciFormat.Magic);
            BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(4), CciFormat.HeaderSize);
            BinaryPrimitives.WriteUInt64LittleEndian(header.AsSpan(8), sectors * CciFormat.SectorSize);
            BinaryPrimitives.WriteUInt64LittleEndian(header.AsSpan(16), CciFormat.HeaderSize);
            BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(24), CciFormat.SectorSize);
            header[28] = CciFormat.Version;
            header[29] = CciFormat.IndexAlignment;
            stream.Write(header);
        }

        Assert.Equal(CciError.InvalidIndex, CciFile.Open(path, out _));
    }

    [Fact]
    public void SectorCountNearIntMax_IsRejectedWithoutThrowing()
    {
        using TempDir dir = new();
        (string path, byte[] bytes) = CreateValid(dir);

        TestImages.WriteUInt64(bytes, 8, (ulong)int.MaxValue * CciFormat.SectorSize);
        File.WriteAllBytes(path, bytes);

        Assert.Equal(CciError.InvalidIndex, CciFile.Open(path, out _));
    }

    private static (string Path, byte[] Bytes) CreateValid(TempDir dir)
    {
        byte[] raw = TestImages.BuildMixed(8);
        string source = dir.File("source.iso");
        File.WriteAllBytes(source, raw);
        string path = dir.File("game.cci");

        Assert.Equal(CciError.None, CciWriter.Compress(source, path, new CciWriteOptions { SplitBytes = 0 }));
        return (path, File.ReadAllBytes(path));
    }
}
