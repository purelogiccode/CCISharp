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
