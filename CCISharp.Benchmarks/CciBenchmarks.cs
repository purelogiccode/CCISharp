using BenchmarkDotNet.Attributes;

namespace CCISharp.Benchmarks;

/// <summary>
/// End-to-end CCI benchmarks over a deterministic synthetic image. No game
/// data is used; every fixture is generated at setup time.
/// </summary>
[MemoryDiagnoser]
public class CciBenchmarks
{
    private const int ImageSectors = 2048; // 4 MiB

    private string _root = string.Empty;
    private string _source = string.Empty;
    private string _cci = string.Empty;
    private CciFile _reader = null!;
    private ulong[] _randomSectors = [];
    private byte[] _readBuffer = [];

    [GlobalSetup]
    public void Setup()
    {
        _root = Path.Combine(Path.GetTempPath(), "ccisharp-bench", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        _source = Path.Combine(_root, "source.iso");
        File.WriteAllBytes(_source, BuildImage(ImageSectors));

        _cci = Path.Combine(_root, "game.cci");
        CciError error = CciWriter.Compress(_source, _cci, new CciWriteOptions
        {
            SplitBytes = 0,
            Level = 12,
        });
        if (error != CciError.None)
        {
            throw new InvalidOperationException($"Could not create benchmark fixture: {error}");
        }

        _reader = CciFile.OpenOrThrow(_cci);
        _readBuffer = new byte[CciFormat.SectorSize];

        Random random = new(12345);
        _randomSectors = new ulong[4096];
        for (int index = 0; index < _randomSectors.Length; index++)
        {
            _randomSectors[index] = (ulong)random.Next(ImageSectors);
        }
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _reader.Dispose();
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
            // Best effort.
        }
        catch (UnauthorizedAccessException)
        {
            // Best effort.
        }
    }

    [Benchmark]
    public long CompressFast()
    {
        string output = Path.Combine(_root, "fast.cci");
        CciError error = CciWriter.Compress(_source, output, new CciWriteOptions
        {
            SplitBytes = 0,
            Level = 9,
            Overwrite = true,
        });
        if (error != CciError.None)
        {
            throw new InvalidOperationException(error.ToMessage());
        }

        return new FileInfo(output).Length;
    }

    [Benchmark]
    public long CompressHc()
    {
        string output = Path.Combine(_root, "hc.cci");
        CciError error = CciWriter.Compress(_source, output, new CciWriteOptions
        {
            SplitBytes = 0,
            Level = 12,
            Overwrite = true,
        });
        if (error != CciError.None)
        {
            throw new InvalidOperationException(error.ToMessage());
        }

        return new FileInfo(output).Length;
    }

    [Benchmark]
    public long CompressHcParallel()
    {
        string output = Path.Combine(_root, "hc-parallel.cci");
        CciError error = CciWriter.Compress(_source, output, new CciWriteOptions
        {
            SplitBytes = 0,
            Level = 12,
            Jobs = 4,
            Overwrite = true,
        });
        if (error != CciError.None)
        {
            throw new InvalidOperationException(error.ToMessage());
        }

        return new FileInfo(output).Length;
    }

    [Benchmark]
    public long CompressSplit()
    {
        CciError error = CciWriter.Compress(_source, Path.Combine(_root, "split.cci"), new CciWriteOptions
        {
            SplitBytes = 1 << 20,
            Level = 12,
            Jobs = 4,
            Overwrite = true,
        });
        if (error != CciError.None)
        {
            throw new InvalidOperationException(error.ToMessage());
        }

        long total = 0;
        for (int part = 1; part <= 9; part++)
        {
            string path = Path.Combine(_root, $"split.{part}.cci");
            if (!File.Exists(path))
            {
                break;
            }

            total += new FileInfo(path).Length;
        }

        return total;
    }

    [Benchmark]
    public long Decompress()
    {
        string output = Path.Combine(_root, "decompressed.iso");
        CciError error = _reader.ExtractToIso(output, new CciExtractOptions { Overwrite = true });
        if (error != CciError.None)
        {
            throw new InvalidOperationException(error.ToMessage());
        }

        return new FileInfo(output).Length;
    }

    [Benchmark]
    public int RandomSectorReads()
    {
        int total = 0;
        foreach (ulong sector in _randomSectors)
        {
            CciError error = _reader.ReadSector(sector, _readBuffer);
            if (error != CciError.None)
            {
                throw new InvalidOperationException(error.ToMessage());
            }

            total += _readBuffer[0];
        }

        return total;
    }

    private static byte[] BuildImage(int sectors)
    {
        byte[] raw = new byte[sectors * CciFormat.SectorSize];
        uint state = 0xC0FFEEu;

        for (int sector = 0; sector < sectors; sector++)
        {
            Span<byte> span = raw.AsSpan(sector * CciFormat.SectorSize, CciFormat.SectorSize);
            switch (sector % 4)
            {
                case 0:
                    span.Clear();
                    break;
                case 1:
                    span.Fill((byte)(sector * 7));
                    break;
                default:
                    for (int index = 0; index < span.Length; index++)
                    {
                        state = (state * 1664525u) + 1013904223u;
                        span[index] = (byte)(state >> 16);
                    }

                    break;
            }
        }

        return raw;
    }
}
