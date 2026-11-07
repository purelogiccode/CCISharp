using System.Diagnostics;

namespace CCISharp.Tests;

/// <summary>
/// Optional interoperability tests against the reference
/// <c>Team-Resurgent/libCCI</c> tools. They run only on Windows and only when
/// the reference build is present; otherwise they are no-ops.
/// </summary>
public sealed class CciReferenceInteropTests
{
    [Fact]
    public void ReferenceEncoder_OutputIsReadable()
    {
        string? tools = FindReferenceTools();
        if (tools is null)
        {
            return;
        }

        using TempDir dir = new();
        byte[] raw = TestImages.BuildMixed(40);
        string source = dir.File("source.iso");
        File.WriteAllBytes(source, raw);

        RunTool(Path.Combine(tools, "cci_encode.exe"), source, dir.File("reference"));

        Assert.Equal(CciError.None, CciFile.Open(dir.File("reference.cci"), out CciFile? opened));
        using CciFile file = opened!;

        Assert.Equal(40UL, file.TotalSectors);

        byte[] sector = new byte[CciFormat.SectorSize];
        for (ulong index = 0; index < file.TotalSectors; index++)
        {
            Assert.Equal(CciError.None, file.ReadSector(index, sector));
            Assert.Equal(raw.AsSpan((int)(index * CciFormat.SectorSize), CciFormat.SectorSize).ToArray(), sector);
        }
    }

    [Fact]
    public void CciSharpOutput_IsReadableByReferenceDecoder()
    {
        string? tools = FindReferenceTools();
        if (tools is null)
        {
            return;
        }

        using TempDir dir = new();
        byte[] raw = TestImages.BuildMixed(40);
        string source = dir.File("source.iso");
        string cci = dir.File("ours.cci");
        string extracted = dir.File("extracted.iso");
        File.WriteAllBytes(source, raw);

        Assert.Equal(CciError.None, CciWriter.Compress(source, cci, new CciWriteOptions { SplitBytes = 0 }));

        RunTool(Path.Combine(tools, "cci_extract.exe"), cci, extracted);
        Assert.Equal(raw, File.ReadAllBytes(extracted));

        RunTool(Path.Combine(tools, "cci_info.exe"), cci);
    }

    [Fact]
    public void CciSharpHcOutput_IsReadableByReferenceDecoder()
    {
        string? tools = FindReferenceTools();
        if (tools is null)
        {
            return;
        }

        using TempDir dir = new();
        byte[] raw = TestImages.BuildMixed(40);
        string source = dir.File("source.iso");
        string cci = dir.File("ours-hc.cci");
        string extracted = dir.File("extracted-hc.iso");
        File.WriteAllBytes(source, raw);

        Assert.Equal(
            CciError.None,
            CciWriter.Compress(source, cci, new CciWriteOptions { SplitBytes = 0, Level = 12 }));

        RunTool(Path.Combine(tools, "cci_extract.exe"), cci, extracted);
        Assert.Equal(raw, File.ReadAllBytes(extracted));

        RunTool(Path.Combine(tools, "cci_info.exe"), cci);
    }

    [Fact]
    public void CciSharpParallelOutput_IsReadableByReferenceDecoder()
    {
        string? tools = FindReferenceTools();
        if (tools is null)
        {
            return;
        }

        using TempDir dir = new();
        byte[] raw = TestImages.BuildMixed(40);
        string source = dir.File("source.iso");
        string cci = dir.File("ours-parallel.cci");
        string extracted = dir.File("extracted-parallel.iso");
        File.WriteAllBytes(source, raw);

        Assert.Equal(
            CciError.None,
            CciWriter.Compress(source, cci, new CciWriteOptions { SplitBytes = 0, Level = 12, Jobs = 4 }));

        RunTool(Path.Combine(tools, "cci_extract.exe"), cci, extracted);
        Assert.Equal(raw, File.ReadAllBytes(extracted));

        RunTool(Path.Combine(tools, "cci_info.exe"), cci);
    }

    [Fact]
    public void ReferenceHcEncoder_OutputIsReadable()
    {
        string? tools = FindReferenceTools();
        if (tools is null)
        {
            return;
        }

        using TempDir dir = new();
        byte[] raw = TestImages.BuildMixed(40);
        string source = dir.File("source.iso");
        File.WriteAllBytes(source, raw);

        RunTool(Path.Combine(tools, "cci_encode.exe"), source, dir.File("reference-hc"), "0", "12");

        Assert.Equal(CciError.None, CciFile.Open(dir.File("reference-hc.cci"), out CciFile? opened));
        using CciFile file = opened!;

        Assert.Equal(40UL, file.TotalSectors);
        Assert.Equal(CciError.None, file.Validate(deep: true, out CciValidationReport? report));
        Assert.True(report!.IsValid);

        byte[] sector = new byte[CciFormat.SectorSize];
        for (ulong index = 0; index < file.TotalSectors; index++)
        {
            Assert.Equal(CciError.None, file.ReadSector(index, sector));
            Assert.Equal(raw.AsSpan((int)(index * CciFormat.SectorSize), CciFormat.SectorSize).ToArray(), sector);
        }
    }

    private static string? FindReferenceTools()
    {
        string? env = Environment.GetEnvironmentVariable("CCI_LIBCCI_DIR");
        if (!string.IsNullOrEmpty(env))
        {
            return HasTools(env)
                ? env
                : throw new InvalidOperationException(
                    $"CCI_LIBCCI_DIR='{env}' does not contain cci_encode.exe, cci_extract.exe and cci_info.exe.");
        }

        if (!OperatingSystem.IsWindows())
        {
            // The reference build is Windows-only; Linux/macOS CI cannot run it.
            return null;
        }

        // Checked in beside the tests (see references/libCCI/README.md) so the
        // Windows CI job verifies interoperability instead of silently passing.
        string bundled = Path.Combine(AppContext.BaseDirectory, "references", "libCCI");
        if (HasTools(bundled))
        {
            return bundled;
        }

        // Local development: a sibling XISOSharp checkout holds the reference build.
        string? directory = AppContext.BaseDirectory;
        for (int i = 0; i < 10 && directory is not null; i++)
        {
            string candidate = Path.Combine(
                directory,
                "CSharp_XISOSharp",
                "References",
                "libCCI-0.1.0-1b0087e",
                "build",
                "Release");

            if (HasTools(candidate))
            {
                return candidate;
            }

            directory = Path.GetDirectoryName(directory.TrimEnd(Path.DirectorySeparatorChar));
        }

        throw new InvalidOperationException(
            "The libCCI reference tools were not found. They are checked in under " +
            "CCISharp.Tests/references/libCCI; rebuild the test project or set CCI_LIBCCI_DIR.");
    }

    private static bool HasTools(string directory) =>
        File.Exists(Path.Combine(directory, "cci_encode.exe")) &&
        File.Exists(Path.Combine(directory, "cci_extract.exe")) &&
        File.Exists(Path.Combine(directory, "cci_info.exe"));

    private static void RunTool(string executable, params string[] arguments)
    {
        ProcessStartInfo startInfo = new(executable)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };

        foreach (string argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using Process process = Process.Start(startInfo) ?? throw new InvalidOperationException($"Could not start {executable}.");
        string stdout = process.StandardOutput.ReadToEnd();
        string stderr = process.StandardError.ReadToEnd();
        process.WaitForExit();

        Assert.True(process.ExitCode == 0, $"{Path.GetFileName(executable)} failed ({process.ExitCode}): {stdout} {stderr}");
    }
}
