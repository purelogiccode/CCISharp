namespace CCISharp.Tests;

/// <summary>
/// Split-set discovery accepts multi-digit part names for reading while the
/// writer keeps emitting the reference-compatible single-digit names.
/// </summary>
public sealed class CciMultiDigitSplitTests
{
    [Fact]
    public void TenPartSet_OpensFromMultiDigitPart()
    {
        using TempDir dir = new();
        byte[] raw = TestImages.BuildMixed(10);
        CreateParts(dir, raw, partCount: 10);

        Assert.Equal(CciError.None, CciFile.Open(dir.File("game.10.cci"), out CciFile? opened));
        using CciFile file = opened!;

        Assert.Equal(10, file.Parts.Count);
        Assert.Equal(10UL, file.TotalSectors);

        byte[] sector = new byte[CciFormat.SectorSize];
        for (ulong index = 0; index < file.TotalSectors; index++)
        {
            Assert.Equal(CciError.None, file.ReadSector(index, sector));
            Assert.Equal(raw.AsSpan((int)(index * CciFormat.SectorSize), CciFormat.SectorSize).ToArray(), sector);
        }
    }

    [Fact]
    public void TenPartSet_OpensFromFirstPart()
    {
        using TempDir dir = new();
        byte[] raw = TestImages.BuildMixed(10);
        CreateParts(dir, raw, partCount: 10);

        Assert.Equal(CciError.None, CciFile.Open(dir.File("game.1.cci"), out CciFile? opened));
        using CciFile file = opened!;

        Assert.Equal(10, file.Parts.Count);
        Assert.Equal(10UL, file.TotalSectors);
    }

    [Fact]
    public void StandaloneMultiDigitName_OpensAsSinglePart()
    {
        using TempDir dir = new();
        byte[] raw = TestImages.BuildMixed(2);
        CreateParts(dir, raw, partCount: 1, requestedName: "game.10.cci");

        Assert.Equal(CciError.None, CciFile.Open(dir.File("game.10.cci"), out CciFile? opened));
        using CciFile file = opened!;

        Assert.Single(file.Parts);
        Assert.Equal(2UL, file.TotalSectors);
    }

    [Fact]
    public void Gap_StopsDiscoveryAtFirstMissingPart()
    {
        using TempDir dir = new();
        byte[] raw = TestImages.BuildMixed(4);
        CreateParts(dir, raw, partCount: 3);
        File.Move(dir.File("game.3.cci"), dir.File("game.4.cci"));

        Assert.Equal(CciError.None, CciFile.Open(dir.File("game.1.cci"), out CciFile? opened));
        using CciFile file = opened!;

        Assert.Equal(2, file.Parts.Count);
    }

    [Fact]
    public void Writer_StillEmitsSingleDigitParts()
    {
        using TempDir dir = new();
        byte[] raw = TestImages.BuildMixed(20);
        string source = dir.File("source.iso");
        File.WriteAllBytes(source, raw);

        Assert.Equal(
            CciError.None,
            CciWriter.Compress(source, dir.File("game.cci"),
                new CciWriteOptions { SplitBytes = 6 * CciFormat.SectorSize }));

        Assert.True(File.Exists(dir.File("game.1.cci")));
        Assert.True(File.Exists(dir.File("game.2.cci")));
        Assert.DoesNotContain(
            Directory.EnumerateFiles(dir.Path, "game.*.cci"),
            path => Path.GetFileName(path) is "game.10.cci" or "game.0.cci");
    }

    private static void CreateParts(TempDir dir, byte[] raw, int partCount, string? requestedName = null)
    {
        int sectorsPerPart = (int)((ulong)raw.Length / CciFormat.SectorSize) / partCount;
        for (int part = 1; part <= partCount; part++)
        {
            int startSector = (part - 1) * sectorsPerPart;
            int sectorCount = part == partCount
                ? ((int)(raw.Length / CciFormat.SectorSize)) - startSector
                : sectorsPerPart;
            string name = partCount == 1 && requestedName is not null
                ? requestedName
                : $"game.{part}.cci";

            using MemoryStream input = new(raw, startSector * CciFormat.SectorSize, sectorCount * CciFormat.SectorSize);
            using FileStream output = File.Create(dir.File(name));
            Assert.Equal(
                CciError.None,
                CciWriter.Compress(input, sectorCount * CciFormat.SectorSize, output,
                    new CciWriteOptions { SplitBytes = 0 }));
        }
    }
}
