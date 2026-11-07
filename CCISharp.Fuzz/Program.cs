using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;

namespace CCISharp.Fuzz;

/// <summary>
/// Bounded, deterministic mutation fuzzer for the CCI reader, split-set
/// discovery, sector decode and validation paths. It starts from synthetic
/// images (no copyrighted game data), mutates bytes or file sets, then drives
/// every public read API. Unexpected exceptions fail the run.
/// </summary>
internal static class Program
{
    private static int Main(string[] args)
    {
        int seconds = ReadInt(args, "--seconds", 30);
        int iterationsLimit = ReadInt(args, "--iterations", 0);
        int seed = ReadInt(args, "--seed", Environment.TickCount);
        Random random = new(seed);
        Console.WriteLine($"FUZZ: seed={seed} seconds={seconds} iterations={iterationsLimit}");

        string root = Path.Combine(Path.GetTempPath(), $"ccisharp_fuzz_{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(root);
            string single = CreateSingle(root);
            string splitDirectory = CreateSplit(root);
            string[] baseFiles = Directory.GetFiles(splitDirectory);

            Stopwatch clock = Stopwatch.StartNew();
            int iterations = 0;
            int failures = 0;
            while (iterationsLimit <= 0
                       ? clock.Elapsed < TimeSpan.FromSeconds(seconds)
                       : iterations < iterationsLimit)
            {
                iterations++;
                string variantDirectory = Path.Combine(root, $"variant_{iterations}");
                string variant = Path.Combine(variantDirectory, "game.cci");
                Directory.CreateDirectory(variantDirectory);

                try
                {
                    if (random.Next(2) == 0)
                    {
                        MutateFile(single, variant, random);
                        RunSingleChecks(variant, root, iterations);
                    }
                    else
                    {
                        BuildSplitVariant(baseFiles, variantDirectory, random);
                        RunSplitChecks(variantDirectory, root, iterations);
                    }
                }
                catch (Exception ex) when (IsExpected(ex))
                {
                    // Expected: corrupt input is rejected by the documented contract.
                }
                catch (Exception ex)
                {
                    failures++;
                    Console.Error.WriteLine(
                        $"FUZZ FAILURE iteration={iterations} seed={seed}");
                    Console.Error.WriteLine(ex);
                    if (failures >= 5)
                    {
                        break;
                    }
                }
                finally
                {
                    TryDeleteDirectory(variantDirectory);
                }
            }

            Console.WriteLine($"FUZZ: iterations={iterations} failures={failures}");
            return failures == 0 ? 0 : 1;
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    private static void RunSingleChecks(string path, string root, int iteration)
    {
        CciError error = CciFile.Open(path, out CciFile? file);
        if (error != CciError.None || file is null)
        {
            return;
        }

        using (file)
        {
            _ = file.Validate(deep: false, out _);
            _ = file.Validate(deep: true, out _);

            byte[] sector = new byte[CciFormat.SectorSize];
            for (int sample = 0; sample < 4; sample++)
            {
                ulong index = file.TotalSectors == 0
                    ? 0
                    : (ulong)RandomNumberGenerator.GetInt32(int.MaxValue) % file.TotalSectors;
                _ = file.ReadSector(index, sector);
                _ = file.ReadSector(ulong.MaxValue, sector);
            }

            if (file.Length > 0)
            {
                int length = Math.Min(8192, (int)Math.Min(int.MaxValue, file.Length));
                _ = file.Read(0, new byte[length]);
                _ = file.Read((ulong)file.Length, new byte[1]);
            }

            using CciStream stream = file.OpenStream();
            byte[] buffer = new byte[64 * 1024];
            _ = stream.Read(buffer, 0, buffer.Length);
            if (stream.Length > 0)
            {
                stream.Seek(Math.Max(0, stream.Length - 100), SeekOrigin.Begin);
                _ = stream.Read(buffer, 0, buffer.Length);
            }

            string extract = Path.Combine(root, $"extract_{iteration}.iso");
            try
            {
                _ = file.ExtractToIso(extract);
            }
            finally
            {
                TryDeleteFile(extract);
            }
        }
    }

    private static void RunSplitChecks(string directory, string root, int iteration)
    {
        string[] candidates = Directory.GetFiles(directory, "*.cci");
        Array.Sort(candidates, StringComparer.OrdinalIgnoreCase);

        foreach (string path in candidates)
        {
            CciError error = CciFile.Open(path, out CciFile? file);
            if (error != CciError.None || file is null)
            {
                continue;
            }

            using (file)
            {
                AssertPartsOrdered(file);
                _ = file.Validate(deep: false, out _);
                byte[] sector = new byte[CciFormat.SectorSize];
                if (file.TotalSectors > 0)
                {
                    _ = file.ReadSector(0, sector);
                    _ = file.ReadSector(file.TotalSectors - 1, sector);
                }

                string extract = Path.Combine(root, $"split_extract_{iteration}.iso");
                try
                {
                    _ = file.ExtractToIso(extract);
                }
                finally
                {
                    TryDeleteFile(extract);
                }
            }
        }
    }

    private static void AssertPartsOrdered(CciFile file)
    {
        ulong expected = 0;
        foreach (CciPartInfo part in file.Parts)
        {
            if (part.StartSector != expected)
            {
                throw new InvalidOperationException("CCI parts are not contiguous.");
            }

            expected = checked(expected + part.SectorCount);
        }

        if (expected != file.TotalSectors)
        {
            throw new InvalidOperationException("CCI part sector totals do not match TotalSectors.");
        }
    }

    private static string CreateSingle(string root)
    {
        string path = Path.Combine(root, "base.cci");
        string source = Path.Combine(root, "base.iso");
        File.WriteAllBytes(source, BuildImage(64));
        using (FileStream input = File.OpenRead(source))
        using (FileStream output = File.Create(path))
        {
            CciError error = CciWriter.Compress(
                input,
                input.Length,
                output,
                new CciWriteOptions { SplitBytes = 0, Level = 12 });
            if (error != CciError.None)
            {
                throw new InvalidOperationException($"Could not create fuzz fixture: {error}");
            }
        }

        return path;
    }

    private static string CreateSplit(string root)
    {
        string directory = Path.Combine(root, "split");
        Directory.CreateDirectory(directory);
        string source = Path.Combine(root, "split.iso");
        File.WriteAllBytes(source, BuildImage(24));

        CciError error = CciWriter.Compress(
            source,
            Path.Combine(directory, "game.cci"),
            new CciWriteOptions { SplitBytes = 6 * CciFormat.SectorSize, Level = 9 });
        if (error != CciError.None)
        {
            throw new InvalidOperationException($"Could not create split fuzz fixture: {error}");
        }

        return directory;
    }

    private static byte[] BuildImage(int sectors)
    {
        byte[] raw = new byte[sectors * CciFormat.SectorSize];
        uint state = 0x5EED5EEDu;
        for (int sector = 0; sector < sectors; sector++)
        {
            Span<byte> span = raw.AsSpan(sector * CciFormat.SectorSize, CciFormat.SectorSize);
            if (sector % 4 == 0)
            {
                span.Clear();
            }
            else if (sector % 4 == 1)
            {
                span.Fill((byte)(sector * 13));
            }
            else
            {
                for (int index = 0; index < span.Length; index++)
                {
                    state = (state * 1664525u) + 1013904223u;
                    span[index] = (byte)(state >> 16);
                }
            }
        }

        return raw;
    }

    private static void BuildSplitVariant(string[] baseFiles, string directory, Random random)
    {
        int operation = random.Next(8);
        for (int index = 0; index < baseFiles.Length; index++)
        {
            string destination = Path.Combine(directory, Path.GetFileName(baseFiles[index]));
            File.Copy(baseFiles[index], destination, overwrite: true);
        }

        string[] parts = Directory.GetFiles(directory, "*.cci");
        Array.Sort(parts, StringComparer.OrdinalIgnoreCase);
        if (parts.Length == 0)
        {
            return;
        }

        switch (operation)
        {
            case 0:
                MutateFile(parts[random.Next(parts.Length)], parts[random.Next(parts.Length)], random);
                break;
            case 1:
                File.Delete(parts[random.Next(parts.Length)]);
                break;
            case 2:
                {
                    string part = parts[random.Next(parts.Length)];
                    string renamed = Path.Combine(directory, "game.10.cci");
                    File.Move(part, renamed, overwrite: true);
                    break;
                }
            case 3:
                {
                    string part = parts[random.Next(parts.Length)];
                    File.Move(part, Path.ChangeExtension(part, ".CCI"), overwrite: true);
                    break;
                }
            case 4:
                {
                    string part = parts[random.Next(parts.Length)];
                    byte[] bytes = File.ReadAllBytes(part);
                    File.WriteAllBytes(part, bytes.AsSpan(0, random.Next(bytes.Length)).ToArray());
                    break;
                }
            case 5:
                {
                    string extra = Path.Combine(directory, $"game.{random.Next(1, 20)}.cci");
                    File.WriteAllBytes(extra, RandomNumberGenerator.GetBytes(random.Next(0, 4096)));
                    break;
                }
            case 6:
                {
                    string part = parts[random.Next(parts.Length)];
                    byte[] bytes = File.ReadAllBytes(part);
                    int offset = random.Next(bytes.Length);
                    int count = Math.Min(bytes.Length - offset, random.Next(1, 128));
                    bytes.AsSpan(offset, count).Clear();
                    File.WriteAllBytes(part, bytes);
                    break;
                }
            default:
                {
                    string part = parts[random.Next(parts.Length)];
                    byte[] bytes = File.ReadAllBytes(part);
                    int original = bytes.Length;
                    Array.Resize(ref bytes, original + random.Next(1, 512));
                    random.NextBytes(bytes.AsSpan(original));
                    File.WriteAllBytes(part, bytes);
                    break;
                }
        }
    }

    private static void MutateFile(string source, string destination, Random random)
    {
        byte[] data = File.ReadAllBytes(source);
        int operation = random.Next(10);
        if (operation < 6)
        {
            int flips = random.Next(1, 9);
            for (int index = 0; index < flips && data.Length > 0; index++)
            {
                int offset = random.Next(data.Length);
                data[offset] ^= (byte)(1 << random.Next(8));
            }
        }
        else if (operation < 8 && data.Length > 64)
        {
            Array.Resize(ref data, random.Next(64, data.Length));
        }
        else if (operation < 9)
        {
            int extra = random.Next(1, 4097);
            int original = data.Length;
            Array.Resize(ref data, original + extra);
            random.NextBytes(data.AsSpan(original));
        }
        else if (data.Length > 0)
        {
            int start = random.Next(data.Length);
            int length = Math.Min(data.Length - start, random.Next(1, 4097));
            data.AsSpan(start, length).Clear();
        }

        File.WriteAllBytes(destination, data);
    }

    private static bool IsExpected(Exception exception) =>
        exception is IOException or InvalidDataException or UnauthorizedAccessException or
            ArgumentException or NotSupportedException or CciException or FormatException;

    private static int ReadInt(string[] args, string name, int fallback)
    {
        for (int index = 0; index + 1 < args.Length; index++)
        {
            if (string.Equals(args[index], name, StringComparison.OrdinalIgnoreCase) &&
                int.TryParse(args[index + 1], NumberStyles.Integer, CultureInfo.InvariantCulture,
                    out int value))
            {
                return value;
            }
        }

        return fallback;
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Best effort.
        }
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Best effort.
        }
    }
}
