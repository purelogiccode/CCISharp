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
| `CompressFast` (level 9, jobs 1) | 4.30 ms | 2.72 MB |
| `CompressHc` (level 12, jobs 1) | 11.43 ms | 6.72 MB |
| `CompressHcParallel` (level 12, jobs 4) | 6.44 ms | 20.59 MB |
| `CompressSplit` (level 12, jobs 4, 1 MiB parts) | 8.76 ms | 22.70 MB |
| `Decompress` (extract to ISO) | 8.71 ms | 1.05 MB |
| `RandomSectorReads` (4096 random sectors) | 22.38 ms | — |

The allocation numbers in this table were captured **before** the encoder-pool
change that reuses HC contexts across batches. Re-running after that change
reports about 2.16 MB for `CompressHc` and 2.64 MB for `CompressHcParallel`; the
times are unchanged within run-to-run noise.

## Interpreting the numbers

- HC is roughly 2.5–3× slower than the fast encoder on this corpus, in exchange
  for a smaller or equal image on compressible data. Use `--level 9` for speed
  and `--level 12` for the smallest output.
- `Jobs = 4` nearly halves HC wall time on this 4 MiB image. Larger images
  amortise the pipeline setup better; the batch size is capped at 1024 sectors
  so memory stays bounded.
- `RandomSectorReads` is dominated by per-sector seek/decode; the reader caches
  nothing across calls by design.
- Extraction is I/O-bound on the 4 MiB fixture; use a larger fixture to measure
  sustained write throughput.
