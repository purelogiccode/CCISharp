namespace CCISharp.Tests;

/// <summary>
/// Parallel sector encoding must be byte-identical to the sequential writer
/// for every level and split configuration.
/// </summary>
public sealed class CciParallelTests
{
    [Theory]
    [InlineData(0, 2, 0L)]
    [InlineData(1, 2, 0L)]
    [InlineData(9, 2, 0L)]
    [InlineData(9, 4, 0L)]
    [InlineData(9, 16, 0L)]
    [InlineData(12, 2, 0L)]
    [InlineData(12, 4, 0L)]
    [InlineData(12, 16, 0L)]
    [InlineData(0, 4, 40960L)]
    [InlineData(9, 4, 40960L)]
    [InlineData(12, 4, 40960L)]
    [InlineData(12, 8, 8192L)]
    public void ParallelOutput_IsByteIdenticalToSequential(int level, int jobs, long splitBytes)
    {
        using TempDir dir = new();
        byte[] raw = TestImages.BuildMixed(40);
        string source = WriteSource(dir, raw);
        string sequentialBase = dir.File($"sequential-l{level}");
        string parallelBase = dir.File($"parallel-l{level}-j{jobs}");

        Assert.Equal(
            CciError.None,
            CciWriter.Compress(source, sequentialBase, new CciWriteOptions
            {
                Level = level,
                Jobs = 1,
                SplitBytes = splitBytes,
            }));
        Assert.Equal(
            CciError.None,
            CciWriter.Compress(source, parallelBase, new CciWriteOptions
            {
                Level = level,
                Jobs = jobs,
                SplitBytes = splitBytes,
            }));

        string[] sequentialParts = DiscoverOutputs(sequentialBase, splitBytes > 0);
        string[] parallelParts = DiscoverOutputs(parallelBase, splitBytes > 0);

        Assert.Equal(sequentialParts.Length, parallelParts.Length);
        for (int i = 0; i < sequentialParts.Length; i++)
        {
            Assert.Equal(File.ReadAllBytes(sequentialParts[i]), File.ReadAllBytes(parallelParts[i]));
        }
    }

    [Theory]
    [InlineData(9, 4)]
    [InlineData(12, 4)]
    public void ParallelOutput_RoundTrips(int level, int jobs)
    {
        using TempDir dir = new();
        byte[] raw = TestImages.BuildMixed(40);
        string source = WriteSource(dir, raw);
        string cciPath = dir.File("game.cci");
        string output = dir.File("out.iso");

        Assert.Equal(
            CciError.None,
            CciWriter.Compress(source, cciPath, new CciWriteOptions
            {
                Level = level,
                Jobs = jobs,
                SplitBytes = 40960,
            }));

        Assert.Equal(CciError.None, CciFile.Open(dir.File("game.1.cci"), out CciFile? opened));
        using CciFile file = opened!;
        Assert.Equal(CciError.None, file.ExtractToIso(output));
        Assert.Equal(raw, File.ReadAllBytes(output));
    }

    [Fact]
    public void StreamOverload_ParallelOutput_IsByteIdentical()
    {
        using TempDir dir = new();
        byte[] raw = TestImages.BuildMixed(40);
        string source = WriteSource(dir, raw);
        string sequential = dir.File("sequential.cci");
        string parallel = dir.File("parallel.cci");

        using (FileStream input = File.OpenRead(source))
        using (FileStream output = File.Create(sequential))
        {
            Assert.Equal(
                CciError.None,
                CciWriter.Compress(input, raw.Length, output, new CciWriteOptions { SplitBytes = 0, Jobs = 1 }));
        }

        using (FileStream input = File.OpenRead(source))
        using (FileStream output = File.Create(parallel))
        {
            Assert.Equal(
                CciError.None,
                CciWriter.Compress(input, raw.Length, output, new CciWriteOptions { SplitBytes = 0, Jobs = 8 }));
        }

        Assert.Equal(File.ReadAllBytes(sequential), File.ReadAllBytes(parallel));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(65)]
    [InlineData(-1)]
    public void InvalidJobs_AreRejected(int jobs)
    {
        using TempDir dir = new();
        string source = WriteSource(dir, TestImages.BuildMixed(4));

        Assert.Equal(
            CciError.InvalidArgument,
            CciWriter.Compress(source, dir.File("game.cci"), new CciWriteOptions { SplitBytes = 0, Jobs = jobs }));
    }

    private static string[] DiscoverOutputs(string basePath, bool split)
    {
        if (!split)
        {
            return [basePath + CciFormat.Extension];
        }

        List<string> parts = new();
        for (int index = 1; index <= 9; index++)
        {
            string candidate = $"{basePath}.{index}{CciFormat.Extension}";
            if (!File.Exists(candidate))
            {
                break;
            }

            parts.Add(candidate);
        }

        return [.. parts];
    }

    private static string WriteSource(TempDir dir, byte[] raw)
    {
        string path = dir.File("source.iso");
        File.WriteAllBytes(path, raw);
        return path;
    }
}
