# Benchmarks

`CCISharp.Benchmarks` uses BenchmarkDotNet over deterministic synthetic images.
No game data is used. The image is 4 MiB (2048 sectors): one quarter zeros, one
quarter filled, and half pseudo-random.

## Running

```sh
dotnet run --project CCISharp.Benchmarks -c Release -- --filter "*CciBenchmarks*" -j short
```

Use the default job (omit `-j short`) for publishable numbers; short-run results
are useful for spotting regressions but have wide error bars.

## Reference results

Machine: AMD Ryzen 9 7900 (12 physical / 24 logical cores), 64-bit Windows 11,
.NET 10.0.14, BenchmarkDotNet 0.15.8, ShortRun job (3 warmups, 3 iterations).

| Method | Mean | Allocated |
|---|---:|---:|
| `CompressFast` (level 9, jobs 1) | 3.74 ms | 2.14 MB |
| `CompressHc` (level 12, jobs 1) | 10.74 ms | 2.26 MB |
| `CompressHcParallel` (level 12, jobs 4) | 5.65 ms | 2.77 MB |
| `CompressSplit` (level 12, jobs 4, 1 MiB parts) | 6.69 ms | 4.87 MB |
| `Decompress` (extract to ISO) | 4.84 ms | 1.05 MB |
| `RandomSectorReads` (4096 random sectors) | 22.04 ms | — |

The numbers move a few percent between runs; ShortRun's error bars are wide.
The allocation column shows the per-operation managed allocation after encoder
pooling: HC contexts and scratch buffers are reused across batches instead of
being recreated per batch.

## Interpreting the numbers

- HC is roughly 2–3× slower than the fast encoder on this corpus, in exchange
  for a smaller or equal image on compressible data. Use level `9` for speed
  and level `12` for the smallest output.
- `Jobs = 4` nearly halves HC wall time on this 4 MiB image. Larger images
  amortise the pipeline setup better; the batch size is capped at 1024 sectors
  so memory stays bounded.
- `RandomSectorReads` is dominated by per-sector seek/decode; the reader caches
  nothing across calls by design.
- Extraction is I/O-bound on the 4 MiB fixture; use a larger fixture to measure
  sustained write throughput.
