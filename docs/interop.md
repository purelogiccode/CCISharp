# Interoperability report

CCISharp is verified against the unmodified reference `libCCI` tools
(`cci_encode`, `cci_extract`, `cci_info`) build `0.1.0-1b0087e`. The binaries are
checked in under `CCISharp.Tests/references/libCCI` for the Windows CI job and
are also available in the XISOSharp reference tree.

libCCI is GPL-2.0-or-later and is used **only as an oracle**. No libCCI code is
copied, translated or linked into this MIT package.

## Test matrix

| Test | Direction | What is verified |
|---|---|---|
| `ReferenceEncoder_OutputIsReadable` | libCCI → CCISharp | reference `.cci` opens, validates and decodes sector-by-sector |
| `ReferenceHcEncoder_OutputIsReadable` | libCCI → CCISharp | level-12 reference output opens, validates and decodes |
| `CciSharpOutput_IsReadableByReferenceDecoder` | CCISharp → libCCI | fast output extracts byte-identically with `cci_extract` |
| `CciSharpHcOutput_IsReadableByReferenceDecoder` | CCISharp → libCCI | HC level-12 output extracts byte-identically |
| `CciSharpParallelOutput_IsReadableByReferenceDecoder` | CCISharp → libCCI | `Jobs = 4` HC output extracts byte-identically |
| `CciSharpParallelOutput_IsByteIdenticalToSequential` | CCISharp | `Jobs = 2/4/8/16` output equals `Jobs = 1`, split and single-part |
| `TenPartSet_OpensFromMultiDigitPart` | CCISharp | `game.10.cci` resolves a 10-part set |

The interop tests run in the Windows `build-and-test` job. They fail rather
than skip when `CCI_LIBCCI_DIR` points at a directory without the tools, and
also look in the checked-in `references/libCCI` folder and a sibling
`CSharp_XISOSharp/References/libCCI-0.1.0-1b0087e/build/Release` checkout.

## Reproducing the check

```powershell
# Synthetic 4 MiB corpus: 1 MiB zeros, 1 MiB 0x5A, 2 MiB random.
# (Any non-copyrighted ISO works; CCISharp never needs game data.)

dotnet run --project CCISharp.Cli -c Release -- pack source.iso fast --level 9 --single -y
dotnet run --project CCISharp.Cli -c Release -- pack source.iso hc   --level 12 --single -y
dotnet run --project CCISharp.Cli -c Release -- pack source.iso hc4  --level 12 --jobs 4 --single -y

cci_encode source.iso ref 0 12
cci_extract fast.cci fast-ref.iso
cci_extract hc.cci   hc-ref.iso
cci_extract hc4.cci  hc4-ref.iso
cci_info hc.cci
```

## Results (2026-11-06)

Synthetic 4 MiB corpus described above:

| Producer | Size (bytes) | SHA-256 (first 16 hex) | Extract by libCCI | Extract by CCISharp |
|---|---:|---|---:|---:|
| CCISharp level 9 | 2,125,860 | `86E220937D215285` | identical | identical |
| CCISharp level 12 | 2,125,860 | `829B1532CDDC2ACA` | identical | identical |
| CCISharp level 12, `Jobs = 4` | 2,125,860 | `829B1532CDDC2ACA` | identical | identical |
| libCCI level 12 | 2,125,860 | `829B1532CDDC2ACA` | identical | identical |

`cci_info` reports 2048 logical sectors / 4,194,304 bytes for the CCISharp
images. All three CCISharp variants round-trip through `cci_extract`; the
libCCI image round-trips through CCISharp `unpack`.

A second 512-sector corpus (per-sector repetition, random and ramp data) gave
309,284 bytes for all producers; the HC and libCCI files were byte-identical,
while the fast file differed. On the unit-test `TestImages.BuildMixed` corpus
HC level 12 is strictly smaller than the fast level-9 encoder.

## Byte-level differences

- CCISharp's fast encoder is a managed `lz4_flex`-compatible encoder. Its block
  parse differs from liblz4's HC encoder, so **fast output is not byte-identical
  to libCCI** even when the total size matches. It is fully valid LZ4 and every
  tested libCCI decoder accepts it.
- CCISharp's HC encoder matched libCCI's level-12 output byte-for-byte on the
  synthetic corpora above. This is an observed result for these inputs, **not a
  guarantee**: CCISharp implements an independent hash-chain/lazy-matching HC
  encoder and does not port liblz4. Match choices can differ on other data.
- `Jobs = N` never changes CCISharp output; parallel and sequential files are
  byte-identical by construction.
- Split points follow the configured threshold, not libCCI's. Both tools write
  complete independent parts with the same naming scheme.

## What is intentionally out of scope

- Bit-exact CCI output versus Repackinator/XboxToolkit for every input.
- Reading or writing STFS/CON/LIVE/PIRS containers (use `StfsSharp`).
- Signature or hash verification of the underlying Xbox image.
