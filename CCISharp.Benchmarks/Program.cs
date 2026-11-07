using BenchmarkDotNet.Running;

namespace CCISharp.Benchmarks;

/// <summary>
/// Benchmark runner entry point. Forwards command-line arguments to
/// BenchmarkDotNet's <c>BenchmarkSwitcher</c> so any benchmark in this assembly
/// can be selected and run.
/// </summary>
internal static class Program
{
    private static void Main(string[] args) =>
        BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args);
}
