using System.Globalization;
using System.Reflection;

namespace CCISharp.Cli;

/// <summary>
/// Minimal CCI command-line tool used for interop checks and CI smoke tests.
/// Commands: <c>pack</c>, <c>unpack</c>, <c>info</c> and <c>validate</c>.
/// </summary>
internal static class Program
{
    private static int Main(string[] args)
    {
        if (args.Length == 0 || IsHelp(args[0]))
        {
            PrintUsage();
            return 0;
        }

        if (args[0] is "--version" or "-v")
        {
            Console.WriteLine($"CCISharp.Cli {GetVersion()}");
            return 0;
        }

        try
        {
            return args[0].ToLowerInvariant() switch
            {
                "pack" => Pack(args),
                "unpack" => Unpack(args),
                "info" => Info(args),
                "validate" => Validate(args),
                _ => Unknown(args[0]),
            };
        }
        catch (OperationCanceledException)
        {
            Console.Error.WriteLine("cancelled");
            return 1;
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or
                                       ArgumentException or NotSupportedException or CciException)
        {
            Console.Error.WriteLine(ex.Message);
            return 1;
        }
    }

    private static int Pack(string[] args)
    {
        string? source = null;
        string? output = null;
        int level = 9;
        int jobs = 1;
        long splitBytes = CciFormat.DefaultSplitBytes;
        bool overwrite = false;

        for (int index = 1; index < args.Length; index++)
        {
            switch (args[index])
            {
                case "--level":
                    level = ParseInt(args, ref index, "--level", 0, 12);
                    break;
                case "--jobs":
                    jobs = ParseInt(args, ref index, "--jobs", 1, CciWriteOptions.MaxJobs);
                    break;
                case "--split":
                    splitBytes = ParseLong(args, ref index, "--split", 0);
                    break;
                case "--single":
                    splitBytes = 0;
                    break;
                case "--overwrite":
                    overwrite = true;
                    break;
                case "-y":
                    overwrite = true;
                    break;
                default:
                    if (args[index].StartsWith('-'))
                    {
                        throw new ArgumentException($"unknown pack option: {args[index]}");
                    }

                    if (source is null)
                    {
                        source = args[index];
                    }
                    else if (output is null)
                    {
                        output = args[index];
                    }
                    else
                    {
                        throw new ArgumentException($"unexpected argument: {args[index]}");
                    }

                    break;
            }
        }

        if (source is null || output is null)
        {
            throw new ArgumentException("usage: pack <source.iso> <output.cci> [--level 0..12] [--jobs 1..64] [--split bytes|--single] [-y]");
        }

        CciError error = CciWriter.Compress(source, output, new CciWriteOptions
        {
            Level = level,
            Jobs = jobs,
            SplitBytes = splitBytes,
            Overwrite = overwrite,
            Progress = Console.IsErrorRedirected
                ? null
                : new Progress<CciProgress>(value =>
                {
                    if (value.TotalSectors > 0)
                    {
                        Console.Error.Write($"\r{value.Fraction * 100,6:F1}%");
                    }
                }),
        });

        if (error != CciError.None)
        {
            Console.Error.WriteLine();
            Console.Error.WriteLine(error.ToMessage());
            return 1;
        }

        Console.Error.WriteLine();
        return 0;
    }

    private static int Unpack(string[] args)
    {
        string? source = null;
        string? output = null;
        bool overwrite = false;

        for (int index = 1; index < args.Length; index++)
        {
            if (args[index] is "--overwrite" or "-y")
            {
                overwrite = true;
            }
            else if (args[index].StartsWith('-'))
            {
                throw new ArgumentException($"unknown unpack option: {args[index]}");
            }
            else if (source is null)
            {
                source = args[index];
            }
            else if (output is null)
            {
                output = args[index];
            }
            else
            {
                throw new ArgumentException($"unexpected argument: {args[index]}");
            }
        }

        if (source is null || output is null)
        {
            throw new ArgumentException("usage: unpack <image.cci> <output.iso> [-y]");
        }

        CciError error = CciFile.Open(source, out CciFile? file);
        if (error != CciError.None || file is null)
        {
            Console.Error.WriteLine(error.ToMessage());
            return 1;
        }

        using (file)
        {
            error = file.ExtractToIso(output, new CciExtractOptions { Overwrite = overwrite });
        }

        if (error != CciError.None)
        {
            Console.Error.WriteLine(error.ToMessage());
            return 1;
        }

        return 0;
    }

    private static int Info(string[] args)
    {
        if (args.Length != 2)
        {
            throw new ArgumentException("usage: info <image.cci>");
        }

        CciError error = CciFile.Open(args[1], out CciFile? file);
        if (error != CciError.None || file is null)
        {
            Console.Error.WriteLine(error.ToMessage());
            return 1;
        }

        using (file)
        {
            Console.WriteLine($"format: CCI v{CciFormat.Version}");
            Console.WriteLine($"parts: {file.Parts.Count}");
            Console.WriteLine($"sectors: {file.TotalSectors}");
            Console.WriteLine($"uncompressed: {file.Length}");
            Console.WriteLine($"compressed: {file.CompressedSize}");

            foreach (CciPartInfo part in file.Parts)
            {
                Console.WriteLine(
                    $"{Path.GetFileName(part.Path)}: start={part.StartSector} sectors={part.SectorCount} " +
                    $"size={part.CompressedSize} ratio={part.Ratio:F4}");
            }
        }

        return 0;
    }

    private static int Validate(string[] args)
    {
        bool deep = false;
        string? path = null;

        for (int index = 1; index < args.Length; index++)
        {
            if (args[index] is "--deep" or "-d")
            {
                deep = true;
            }
            else if (args[index].StartsWith('-'))
            {
                throw new ArgumentException($"unknown validate option: {args[index]}");
            }
            else if (path is null)
            {
                path = args[index];
            }
            else
            {
                throw new ArgumentException($"unexpected argument: {args[index]}");
            }
        }

        if (path is null)
        {
            throw new ArgumentException("usage: validate <image.cci> [--deep]");
        }

        CciError error = CciFile.Open(path, out CciFile? file);
        if (error != CciError.None || file is null)
        {
            Console.Error.WriteLine(error.ToMessage());
            return 1;
        }

        using (file)
        {
            error = file.Validate(deep, out CciValidationReport? report);
            if (report is null)
            {
                Console.Error.WriteLine(error.ToMessage());
                return 1;
            }

            foreach (CciValidationIssue issue in report.Issues)
            {
                Console.Error.WriteLine($"{issue.PartIndex}:{issue.Sector}: {issue.Reason}");
            }

            Console.WriteLine(report.IsValid ? "PASS" : "FAIL");
            Console.WriteLine($"parts: {report.Parts.Count}");
            Console.WriteLine($"sectors: {report.TotalSectors}");
            Console.WriteLine($"uncompressed: {report.UncompressedSize}");
            Console.WriteLine($"compressed: {report.CompressedSize}");
            Console.WriteLine($"ratio: {report.Ratio:F4}");
            return report.IsValid ? 0 : 1;
        }
    }

    private static int Unknown(string command)
    {
        Console.Error.WriteLine($"unknown command: {command}");
        PrintUsage();
        return 1;
    }

    private static int ParseInt(string[] args, ref int index, string option, int min, int max)
    {
        if (index + 1 >= args.Length ||
            !int.TryParse(args[index + 1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int value) ||
            value < min || value > max)
        {
            throw new ArgumentException($"{option} requires an integer {min}..{max}");
        }

        index++;
        return value;
    }

    private static long ParseLong(string[] args, ref int index, string option, long min)
    {
        if (index + 1 >= args.Length ||
            !long.TryParse(args[index + 1], NumberStyles.Integer, CultureInfo.InvariantCulture, out long value) ||
            value < min)
        {
            throw new ArgumentException($"{option} requires an integer >= {min}");
        }

        index++;
        return value;
    }

    private static bool IsHelp(string argument) =>
        argument is "-h" or "--help" or "help";

    private static string GetVersion() =>
        typeof(Program).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
        ?? typeof(Program).Assembly.GetName().Version?.ToString()
        ?? "unknown";

    private static void PrintUsage()
    {
        Console.WriteLine("CCISharp.Cli - CCI (CCIM) reader/writer tool");
        Console.WriteLine();
        Console.WriteLine("usage:");
        Console.WriteLine("  ccisharp pack <source.iso> <output.cci> [--level 0..12] [--jobs 1..64]");
        Console.WriteLine("                [--split bytes|--single] [-y]");
        Console.WriteLine("  ccisharp unpack <image.cci> <output.iso> [-y]");
        Console.WriteLine("  ccisharp info <image.cci>");
        Console.WriteLine("  ccisharp validate <image.cci> [--deep]");
        Console.WriteLine();
        Console.WriteLine("Levels 0 = store, 1..9 = fast LZ4, 10..12 = LZ4-HC (12 is highest effort).");
    }
}
