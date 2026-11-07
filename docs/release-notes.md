# Release notes

## 1.1.0 — 2026-11-06

### Added

- **LZ4-HC encoder.** `CciWriteOptions.Level` now accepts `10..12`; level 12 is
  the highest-effort preset and corresponds to the reference encoder's
  `LZ4HC_CLEVEL_MAX`. The fast encoder remains the default (`9`).
  `Lz4Hc.Compress` exposes the encoder directly.
- **Parallel sector compression.** `CciWriteOptions.Jobs` (1..64) encodes
  sectors on a bounded worker pool while writing in sector order. Output is
  byte-identical to `Jobs = 1` for every level and split configuration.
- **Multi-digit split discovery.** The reader accepts `name.10.cci` and higher
  when a contiguous set starting at `name.1.cci` exists. The writer still emits
  `name.1.cci` … `name.9.cci` for reference compatibility.
- **`CCISharp.Cli`.** `pack`, `unpack`, `info` and `validate` commands for
  interop and CI smoke tests.
- **Fuzz and benchmark projects.** A deterministic mutation fuzzer covers
  headers, indexes, sector blobs and split sets; BenchmarkDotNet covers
  compression, extraction, random reads and split overhead.
- **Documentation:** `docs/format.md`, `docs/api.md`, `docs/interop.md` and
  `docs/benchmarks.md`.

### Changed

- `CciWriteOptions.Level` validation now accepts `0..12` instead of `0..9`.
- CCI compression no longer has to be single-threaded in consumers.
- Encoder state (hash table, HC chains, scratch buffer) is pooled per worker, so
  a whole image is compressed without per-sector allocations.

### Compatibility

- No public API was removed or renamed. The package remains MIT, dependency-free,
  trimmable and AOT-compatible.
- Existing level `0..9` output is unchanged.
- The container layout, index and split naming are unchanged.

## 1.0.1 — prior release

- Rejects oversized CCI indexes without allocating (64 MiB cap).
- Hardens header/index validation against hostile inputs.

## 1.0.0 — initial release

- Read/write single-part and split CCI containers.
- Random-access sector reads, seekable stream, extraction and validation.
