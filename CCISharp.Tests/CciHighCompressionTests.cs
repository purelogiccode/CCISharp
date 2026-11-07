namespace CCISharp.Tests;

/// <summary>
/// LZ4-HC encoder coverage: valid output, round-trip and a ratio that is never
/// worse than the fast encoder on the synthetic corpus.
/// </summary>
public sealed class CciHighCompressionTests
{
    [Theory]
    [InlineData(10)]
    [InlineData(11)]
    [InlineData(12)]
    public void HcLevels_RoundTrip(int level)
    {
        using TempDir dir = new();
        byte[] raw = TestImages.BuildMixed(40);
        string source = WriteSource(dir, raw);
        string cciPath = dir.File("game.cci");
        string output = dir.File("out.iso");

        Assert.Equal(
            CciError.None,
            CciWriter.Compress(source, cciPath, new CciWriteOptions { SplitBytes = 0, Level = level }));

        Assert.Equal(CciError.None, CciFile.Open(cciPath, out CciFile? opened));
        using CciFile file = opened!;

        byte[] sector = new byte[CciFormat.SectorSize];
        for (ulong index = 0; index < file.TotalSectors; index++)
        {
            Assert.Equal(CciError.None, file.ReadSector(index, sector));
            Assert.Equal(raw.AsSpan((int)(index * CciFormat.SectorSize), CciFormat.SectorSize).ToArray(), sector);
        }

        Assert.Equal(CciError.None, file.ExtractToIso(output));
        Assert.Equal(raw, File.ReadAllBytes(output));
        Assert.Equal(CciError.None, file.Validate(deep: true, out CciValidationReport? report));
        Assert.True(report!.IsValid);
    }

    [Fact]
    public void Hc12_IsNotLargerThanFast9()
    {
        using TempDir dir = new();
        byte[] raw = TestImages.BuildMixed(64);
        string source = WriteSource(dir, raw);
        string fast = dir.File("fast.cci");
        string hc = dir.File("hc.cci");

        Assert.Equal(
            CciError.None,
            CciWriter.Compress(source, fast, new CciWriteOptions { SplitBytes = 0, Level = 9 }));
        Assert.Equal(
            CciError.None,
            CciWriter.Compress(source, hc, new CciWriteOptions { SplitBytes = 0, Level = 12 }));

        Assert.True(
            new FileInfo(hc).Length <= new FileInfo(fast).Length,
            $"HC output ({new FileInfo(hc).Length}) is larger than fast output ({new FileInfo(fast).Length}).");
    }

    [Fact]
    public void Hc12_BeatsFast9OnRepetitiveCorpus()
    {
        using TempDir dir = new();
        byte[] raw = BuildRepetitiveCorpus(48);
        string source = WriteSource(dir, raw);
        string fast = dir.File("fast.cci");
        string hc = dir.File("hc.cci");

        Assert.Equal(
            CciError.None,
            CciWriter.Compress(source, fast, new CciWriteOptions { SplitBytes = 0, Level = 9 }));
        Assert.Equal(
            CciError.None,
            CciWriter.Compress(source, hc, new CciWriteOptions { SplitBytes = 0, Level = 12 }));

        Assert.True(
            new FileInfo(hc).Length < new FileInfo(fast).Length,
            $"HC output ({new FileInfo(hc).Length}) did not beat fast output ({new FileInfo(fast).Length}).");
    }

    [Fact]
    public void LevelOutsideRange_IsRejected()
    {
        using TempDir dir = new();
        string source = WriteSource(dir, TestImages.BuildMixed(4));

        Assert.Equal(
            CciError.InvalidArgument,
            CciWriter.Compress(source, dir.File("game.cci"), new CciWriteOptions { SplitBytes = 0, Level = 13 }));
        Assert.Equal(
            CciError.InvalidArgument,
            CciWriter.Compress(source, dir.File("game.cci"), new CciWriteOptions { SplitBytes = 0, Level = -1 }));
    }

    [Fact]
    public void Lz4HcEncoder_RoundTripsDirectly()
    {
        byte[] input = BuildRepetitiveCorpus(1);
        byte[] compressed = new byte[Lz4.MaxCompressedOutputSize(input.Length)];
        int written = Lz4Hc.Compress(input, compressed, 12);

        byte[] decoded = new byte[input.Length];
        Assert.Equal(input.Length, Lz4.Decompress(compressed.AsSpan(0, written), decoded));
        Assert.Equal(input, decoded);
    }

    [Fact]
    public void Lz4HcEncoder_RejectsInvalidLevel()
    {
        byte[] input = new byte[64];
        byte[] destination = new byte[Lz4.MaxCompressedOutputSize(input.Length)];

        Assert.Throws<ArgumentOutOfRangeException>(() => Lz4Hc.Compress(input, destination, 9));
    }

    private static byte[] BuildRepetitiveCorpus(int sectors)
    {
        byte[] raw = new byte[sectors * CciFormat.SectorSize];
        byte[] pattern =
        [
            0x00, 0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07,
            0x10, 0x11, 0x12, 0x13, 0x14, 0x15, 0x16, 0x17,
        ];

        for (int sector = 0; sector < sectors; sector++)
        {
            Span<byte> span = raw.AsSpan(sector * CciFormat.SectorSize, CciFormat.SectorSize);
            for (int i = 0; i < span.Length; i++)
            {
                span[i] = pattern[(i + (sector * 3)) % pattern.Length];
            }

            if (sector % 5 == 0)
            {
                span[100] = (byte)sector;
                span[1500] = (byte)(sector * 7);
            }
        }

        return raw;
    }

    private static string WriteSource(TempDir dir, byte[] raw)
    {
        string path = dir.File("source.iso");
        File.WriteAllBytes(path, raw);
        return path;
    }
}
