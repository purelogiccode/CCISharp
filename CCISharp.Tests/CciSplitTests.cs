namespace CCISharp.Tests;

public sealed class CciSplitTests
{
    [Fact]
    public void WriteSplit_ProducesIndependentNumberedParts()
    {
        using TempDir dir = new();
        byte[] raw = TestImages.BuildRandom(40);
        string source = WriteSource(dir, raw);
        string outputBase = dir.File("game.cci");

        Assert.Equal(
            CciError.None,
            CciWriter.Compress(source, outputBase, new CciWriteOptions { SplitBytes = 40960, Level = 9 }));

        Assert.False(File.Exists(outputBase));
        Assert.True(File.Exists(dir.File("game.1.cci")));
        Assert.True(File.Exists(dir.File("game.2.cci")));

        Assert.Equal(CciError.None, CciFile.Open(dir.File("game.1.cci"), out CciFile? opened));
        using CciFile file = opened!;

        Assert.True(file.Parts.Count >= 2);
        Assert.Equal(40UL, file.TotalSectors);

        byte[] sector = new byte[CciFormat.SectorSize];
        for (ulong index = 0; index < file.TotalSectors; index++)
        {
            Assert.Equal(CciError.None, file.ReadSector(index, sector));
            Assert.Equal(raw.AsSpan((int)(index * CciFormat.SectorSize), CciFormat.SectorSize).ToArray(), sector);
        }

        Assert.Equal(CciError.None, file.Validate(deep: true, out CciValidationReport? report));
        Assert.NotNull(report);
        Assert.True(report.Parts.Count >= 2);
    }

    [Fact]
    public void SplitPart_CanBeOpenedFromAnyNumberedPart()
    {
        using TempDir dir = new();
        byte[] raw = TestImages.BuildRandom(40);
        string source = WriteSource(dir, raw);

        Assert.Equal(
            CciError.None,
            CciWriter.Compress(source, dir.File("game.cci"), new CciWriteOptions { SplitBytes = 40960 }));

        Assert.Equal(CciError.None, CciFile.Open(dir.File("game.2.cci"), out CciFile? opened));
        using CciFile file = opened!;

        Assert.True(file.Parts.Count >= 2);
        Assert.Equal(40UL, file.TotalSectors);
    }

    [Fact]
    public void TooManyParts_IsRejected()
    {
        using TempDir dir = new();
        byte[] raw = TestImages.BuildRandom(40);
        string source = WriteSource(dir, raw);

        Assert.Equal(
            CciError.TooManyParts,
            CciWriter.Compress(source, dir.File("game.cci"), new CciWriteOptions { SplitBytes = CciFormat.SectorSize }));

        Assert.False(File.Exists(dir.File("game.1.cci")));
    }

    [Fact]
    public void SinglePart_KeepsUnsuffixedName()
    {
        using TempDir dir = new();
        byte[] raw = TestImages.BuildMixed(8);
        string source = WriteSource(dir, raw);

        Assert.Equal(
            CciError.None,
            CciWriter.Compress(source, dir.File("game.cci"), new CciWriteOptions { SplitBytes = 0 }));

        Assert.True(File.Exists(dir.File("game.cci")));
        Assert.False(File.Exists(dir.File("game.1.cci")));

        Assert.Equal(CciError.None, CciFile.Open(dir.File("game.cci"), out CciFile? opened));
        using CciFile file = opened!;
        Assert.Single(file.Parts);
    }

    private static string WriteSource(TempDir dir, byte[] raw)
    {
        string path = dir.File("source.iso");
        File.WriteAllBytes(path, raw);
        return path;
    }
}
