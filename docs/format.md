# CCI ("CCIM") format notes

CCISharp is an independent, MIT-licensed implementation of the CCI container
described by Team-Resurgent/libCCI's
[`FORMAT.md`](https://github.com/Team-Resurgent/libCCI/blob/main/FORMAT.md).
This page documents the details CCISharp implements and the rules it applies
when the reference specification leaves room for interpretation.

No libCCI source is included or translated; libCCI is used only as a test
oracle.

## Constants

| Name | Value | Notes |
|---|---|---|
| Magic | `0x4D494343` | ASCII `CCIM` |
| Header size | 32 | bytes |
| Sector size | 2048 | logical XGD sector |
| Version | 1 | only defined version |
| Index alignment | 2 | positions are stored shifted right by 2 |
| Default split point | `0xFFBF6000` | ~4 GiB, same as CISO |
| Index cap | 64 MiB | per part, ~34 GiB of sectors |

All integers are little-endian.

## File layout

```
+-------------------+ offset 0
| Header (32 bytes) |
+-------------------+ offset 32
| sector 0 block    |
| sector 1 block    |
| ...               |
| sector N-1 block  |
+-------------------+ offset = index_offset
| index[0..N]       | N+1 uint32 entries; the last is the end offset
+-------------------+
```

`sector_count = uncompressed_size / 2048` and must be exact; a header whose
size is not sector-aligned is rejected with `CciError.InvalidSize`.

## Header

| Offset | Size | Field | Value |
|---:|---:|---|---|
| 0 | 4 | magic | `0x4D494343` |
| 4 | 4 | header size | 32 |
| 8 | 8 | uncompressed size | decoded bytes in this part |
| 16 | 8 | index offset | byte offset of the index |
| 24 | 4 | block size | 2048 |
| 28 | 1 | version | 1 |
| 29 | 1 | index alignment | 2 |
| 30 | 2 | reserved | 0 |

## Index

Each entry is a `uint32`:

```
position = (entry & 0x7FFFFFFF) << index_alignment
lz4      = (entry & 0x80000000) != 0
```

For sector `i`, the block spans `[position(i), position(i+1))`. Entry `N` is the
sentinel end offset and equals `index_offset`. CCISharp validates that entries
are monotonic, start at or after the header, do not cross the index, and that a
raw block is exactly 2048 bytes.

## Sector blocks

**Raw** (`lz4 == 0`): exactly 2048 bytes.

**LZ4** (`lz4 == 1`):

```
+---------------+-------------------+------------------+
| pad (1 byte)  | LZ4 block         | pad zero bytes   |
+---------------+-------------------+------------------+
```

`pad` is 0–3, the number of trailing zero bytes. The compressed block length is
`span - 1 - pad`; decoding must produce exactly 2048 bytes. The 4-byte alignment
is why the index stores positions shifted by 2.

A sector is stored raw when the compressed block is not smaller than
`2048 - (4 + 4) = 2040` bytes, matching the reference threshold.

## Split sets

Each part is a complete, independent CCI covering a contiguous sector range.

- **Writer:** emits `name.1.cci` … `name.9.cci`. Splitting stops with
  `CciError.TooManyParts` before a tenth part would be created. A non-split
  image is `name.cci`.
- **Reader:** parses the final numeric sub-extension of any `.cci` name. Names
  such as `name.10.cci` are accepted when the contiguous set `name.1.cci` …
  `name.10.cci` exists, so images produced by other tools with more than nine
  parts remain readable.
- **Ambiguity rule:** a numeric name that is not part of a contiguous set
  starting at `.1` is treated as a standalone image. For example, `game.10.cci`
  with no `game.1.cci` is a single-part image.
- **Discovery stops at the first missing part.** A gap is not bridged. Opening
  any part whose number is inside the discovered `1..N` range resolves the whole
  set; opening a number beyond the discovered range falls back to the named file
  alone.
- **Stitching:** parts are concatenated at sector granularity, using each part's
  own header and index; file sizes are never used to infer boundaries.

This keeps the writer compatible with libCCI/Repackinator/Cerbios while allowing
the reader to consume larger multi-digit sets.

## Encoders

- Levels `1..9` use the fast managed LZ4 block encoder, mapping level to LZ4
  acceleration (`10 - level`).
- Levels `10..12` use the managed LZ4-HC encoder. Level 12 is the highest-effort
  preset and corresponds to the reference encoder's `LZ4HC_CLEVEL_MAX` preset.
  HC output is a valid LZ4 block but is not guaranteed to be byte-identical to
  liblz4/libCCI across all inputs.
- Level `0` stores every sector raw.

The HC encoder uses a bounded hash-chain match finder with one-step lazy
matching. It never changes the container layout: only the bytes inside an LZ4
block differ from the fast encoder.

## Parallel compression

`CciWriteOptions.Jobs` (> 1) encodes sector payloads on a bounded worker pool
but writes them in sector order. The split decision is also made in sector
order, so `Jobs = N` produces byte-identical output to `Jobs = 1` for the same
level and options. Parallelism does not change the index, header, alignment or
split points.

## Limits and hostile input

- `uncompressed_size` must be non-zero, sector-aligned and fit in the reader's
  `ulong`/`long` ranges.
- The index length is capped at 64 MiB before any allocation.
- One sector blob is capped by `CciReaderOptions.MaxSectorSpan` (1 MiB by
  default) even when index validation is disabled.
- The decoder rejects truncated blocks, invalid padding, zero offsets,
  out-of-range matches and blocks that do not decode to exactly 2048 bytes.
