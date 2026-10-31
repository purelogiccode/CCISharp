namespace CCISharp.Tests;

public sealed class CciRoundTripTests
{
    [Fact]
    public void WriteRead_SinglePart()
    {
        using TempDir dir = new();
        byte[] raw = TestImages.BuildMixed(40);
        string source = WriteSource(dir, raw);
        string cciPath = dir.File("game.cci");

        Assert.Equal(
            CciError.None,
            CciWriter.Compress(source, cciPath, new CciWriteOptions { SplitBytes = 0, Level = 9 }));

        Assert.True(File.Exists(cciPath));

        Assert.Equal(CciError.None, CciFile.Open(cciPath, out CciFile? opened));
        using CciFile file = opened!;

        Assert.Equal(40UL, file.TotalSectors);
        Assert.Equal(raw.Length, file.Length);
        Assert.Single(file.Parts);

        byte[] sector = new byte[CciFormat.SectorSize];
        for (ulong index = 0; index < file.TotalSectors; index++)
        {
            Assert.Equal(CciError.None, file.ReadSector(index, sector));
            Assert.Equal(raw.AsSpan((int)(index * CciFormat.SectorSize), CciFormat.SectorSize).ToArray(), sector);
        }
    }

    [Fact]
    public void Read_ArbitraryRange()
    {
        using TempDir dir = new();
        byte[] raw = TestImages.BuildMixed(40);
        string source = WriteSource(dir, raw);
        string cciPath = dir.File("game.cci");

        Assert.Equal(CciError.None, CciWriter.Compress(source, cciPath, new CciWriteOptions { SplitBytes = 0 }));
        Assert.Equal(CciError.None, CciFile.Open(cciPath, out CciFile? opened));
        using CciFile file = opened!;

        byte[] buffer = new byte[5000];
        Assert.Equal(CciError.None, file.Read(123, buffer));
        Assert.Equal(raw.AsSpan(123, buffer.Length).ToArray(), buffer);

        Assert.Equal(CciError.Range, file.Read((ulong)raw.Length - 10, buffer));
    }

    [Fact]
    public void ExtractToIso_EqualsSource()
    {
        using TempDir dir = new();
        byte[] raw = TestImages.BuildMixed(40);
        string source = WriteSource(dir, raw);
        string cciPath = dir.File("game.cci");
        string output = dir.File("out.iso");

        Assert.Equal(CciError.None, CciWriter.Compress(source, cciPath, new CciWriteOptions { SplitBytes = 0 }));
        Assert.Equal(CciError.None, CciFile.Open(cciPath, out CciFile? opened));
        using CciFile file = opened!;

        Assert.Equal(CciError.None, file.ExtractToIso(output));
        Assert.Equal(raw, File.ReadAllBytes(output));

        Assert.Equal(CciError.None, file.Validate(deep: true, out CciValidationReport? report));
        Assert.NotNull(report);
        Assert.True(report.IsValid);
    }

    [Fact]
    public void Stream_SeekAndRead()
    {
        using TempDir dir = new();
        byte[] raw = TestImages.BuildMixed(40);
        string source = WriteSource(dir, raw);
        string cciPath = dir.File("game.cci");

        Assert.Equal(CciError.None, CciWriter.Compress(source, cciPath, new CciWriteOptions { SplitBytes = 0 }));
        Assert.Equal(CciError.None, CciFile.Open(cciPath, out CciFile? opened));
        using CciFile file = opened!;
        using CciStream stream = file.OpenStream();

        const int offset = (3 * CciFormat.SectorSize) + 17;
        stream.Seek(offset, SeekOrigin.Begin);

        byte[] buffer = new byte[4096];
        int read = stream.Read(buffer, 0, buffer.Length);

        Assert.Equal(buffer.Length, read);
        Assert.Equal(raw.AsSpan(offset, buffer.Length).ToArray(), buffer);
        Assert.Equal(offset + buffer.Length, stream.Position);
    }

    [Fact]
    public void Open_FromStream()
    {
        using TempDir dir = new();
        byte[] raw = TestImages.BuildMixed(20);
        string source = WriteSource(dir, raw);
        string cciPath = dir.File("game.cci");

        Assert.Equal(CciError.None, CciWriter.Compress(source, cciPath, new CciWriteOptions { SplitBytes = 0 }));

        using FileStream stream = File.OpenRead(cciPath);
        Assert.Equal(CciError.None, CciFile.Open(stream, ownsStream: false, out CciFile? opened));
        using CciFile file = opened!;

        Assert.Null(file.FirstPath);
        Assert.Equal(20UL, file.TotalSectors);

        byte[] sector = new byte[CciFormat.SectorSize];
        Assert.Equal(CciError.None, file.ReadSector(7, sector));
        Assert.Equal(raw.AsSpan(7 * CciFormat.SectorSize, CciFormat.SectorSize).ToArray(), sector);
    }

    [Fact]
    public void Writer_RefusesToOverwriteByDefault()
    {
        using TempDir dir = new();
        byte[] raw = TestImages.BuildMixed(4);
        string source = WriteSource(dir, raw);
        string cciPath = dir.File("game.cci");

        Assert.Equal(CciError.None, CciWriter.Compress(source, cciPath, new CciWriteOptions { SplitBytes = 0 }));
        Assert.Equal(CciError.AlreadyExists, CciWriter.Compress(source, cciPath, new CciWriteOptions { SplitBytes = 0 }));
        Assert.Equal(CciError.None, CciWriter.Compress(source, cciPath, new CciWriteOptions { SplitBytes = 0, Overwrite = true }));
    }

    [Fact]
    public void LevelZero_StoresEverySectorRaw()
    {
        using TempDir dir = new();
        byte[] raw = TestImages.BuildMixed(12);
        string source = WriteSource(dir, raw);
        string cciPath = dir.File("game.cci");

        Assert.Equal(
            CciError.None,
            CciWriter.Compress(source, cciPath, new CciWriteOptions { SplitBytes = 0, Level = 0 }));

        byte[] file = File.ReadAllBytes(cciPath);
        ulong indexOffset = TestImages.IndexOffset(file);

        for (int sector = 0; sector < 12; sector++)
        {
            uint entry = TestImages.IndexEntry(file, indexOffset, sector);
            uint next = TestImages.IndexEntry(file, indexOffset, sector + 1);

            Assert.Equal(0u, entry & 0x80000000u);
            Assert.Equal(CciFormat.SectorSize, (int)((next & 0x7FFFFFFFu) - (entry & 0x7FFFFFFFu)) << CciFormat.IndexAlignment);
        }
    }

    [Fact]
    public void PartialLastSector_IsZeroPadded()
    {
        using TempDir dir = new();
        byte[] raw = new byte[(2 * CciFormat.SectorSize) + 100];
        raw.AsSpan().Fill(0x42);
        string source = WriteSource(dir, raw);
        string cciPath = dir.File("game.cci");
        string output = dir.File("out.iso");

        Assert.Equal(CciError.None, CciWriter.Compress(source, cciPath, new CciWriteOptions { SplitBytes = 0 }));
        Assert.Equal(CciError.None, CciFile.Open(cciPath, out CciFile? opened));
        using CciFile file = opened!;

        Assert.Equal(3UL, file.TotalSectors);
        Assert.Equal(3 * CciFormat.SectorSize, file.Length);
        Assert.Equal(CciError.None, file.ExtractToIso(output));

        byte[] extracted = File.ReadAllBytes(output);
        Assert.Equal(3 * CciFormat.SectorSize, extracted.Length);
        Assert.Equal(raw, extracted.AsSpan(0, raw.Length).ToArray());
        Assert.True(extracted.AsSpan(raw.Length).ToArray().All(static value => value == 0));
    }

    [Fact]
    public void OpenOrThrow_NotFound()
    {
        using TempDir dir = new();
        CciException exception = Assert.Throws<CciException>(() => CciFile.OpenOrThrow(dir.File("missing.cci")));
        Assert.Equal(CciError.NotFound, exception.Error);
    }

    [Fact]
    public void EmptySource_IsRejected()
    {
        using TempDir dir = new();
        string source = dir.File("empty.iso");
        File.WriteAllBytes(source, []);

        Assert.Equal(
            CciError.InvalidSize,
            CciWriter.Compress(source, dir.File("game.cci"), new CciWriteOptions { SplitBytes = 0 }));
    }

    private static string WriteSource(TempDir dir, byte[] raw)
    {
        string path = dir.File("source.iso");
        File.WriteAllBytes(path, raw);
        return path;
    }
}
