# CCISharp

[![CI](https://github.com/purelogiccode/CCISharp/actions/workflows/ci.yml/badge.svg?branch=master)](https://github.com/purelogiccode/CCISharp/actions/workflows/ci.yml)
[![NuGet](https://img.shields.io/nuget/v/CCISharp.svg)](https://www.nuget.org/packages/CCISharp)
[![NuGet Downloads](https://img.shields.io/nuget/dt/CCISharp.svg)](https://www.nuget.org/packages/CCISharp)
[![.NET](https://img.shields.io/badge/.NET-8.0%20%7C%209.0%20%7C%2010.0-512bd4.svg)](https://dotnet.microsoft.com/download)
[![Platform](https://img.shields.io/badge/platform-Windows%20%7C%20Linux%20%7C%20macOS-0078d7.svg)](https://github.com/purelogiccode/CCISharp)
[![Format](https://img.shields.io/badge/format-CCI%20%2F%20CCIM-6f42c1.svg)](https://github.com/Team-Resurgent/libCCI/blob/main/FORMAT.md)
[![Compression](https://img.shields.io/badge/compression-LZ4-0b7285.svg)](https://github.com/lz4/lz4)
![Trimmable](https://img.shields.io/badge/trimming-compatible-success)
![AOT](https://img.shields.io/badge/AOT-compatible-success)
[![License: MIT](https://img.shields.io/badge/License-MIT-green.svg)](LICENSE)
[![GitHub stars](https://img.shields.io/github/stars/purelogiccode/CCISharp)](https://github.com/purelogiccode/CCISharp/stargazers)
[![GitHub issues](https://img.shields.io/github/issues/purelogiccode/CCISharp)](https://github.com/purelogiccode/CCISharp/issues)

**CCISharp** is a pure-managed C# library for reading and writing **CCI**
("CCIM", Cerbios Compressed Image) original-Xbox disc images. It is an
independent MIT implementation of the format specified by
[Team-Resurgent/libCCI](https://github.com/Team-Resurgent/libCCI)
(`FORMAT.md`).

CCI is the compressed disc-image format used by the Cerbios BIOS and created
by Repackinator/XboxToolkit. Each 2048-byte sector is either stored raw or
compressed as a single LZ4 block, with a random-access index.

## Features

- **Read and write** single-part `.cci` containers.
- **Split sets** (`name.1.cci` … `name.9.cci`) with transparent stitching.
- **Random access** to any sector, plus a seekable read-only `Stream`.
- **One-call extraction** to a raw ISO with progress, cancellation and
  preallocation.
- **Validation** of headers, indexes and (optionally) every sector.
- **Pure managed**, no native dependencies, trimmable and AOT-compatible.
- Targets `net8.0`, `net9.0` and `net10.0`.

## Requirements

| Requirement | Value |
|---|---|
| Target frameworks | `net8.0`, `net9.0`, `net10.0` |
| Runtime | .NET 8, .NET 9 or .NET 10 |
| Platforms | Windows, Linux, macOS |
| Dependencies | none |

## Installation

```sh
dotnet add package CCISharp
```

## Quick start

### Read a CCI image

```csharp
using CCISharp;

CciError error = CciFile.Open(@"C:\games\game.cci", out CciFile? cci);
if (error != CciError.None || cci is null)
{
    Console.Error.WriteLine(error);
    return;
}

using (cci)
{
    Console.WriteLine($"{cci.TotalSectors} sectors, {cci.Length} bytes");
    Console.WriteLine($"{cci.Slices.Count} part(s)");

    byte[] sector = new byte[CciFormat.SectorSize];
    cci.ReadSector(0, sector);
}
```

### Extract to ISO

```csharp
using CciFile cci = CciFile.OpenOrThrow(@"C:\games\game.cci");
CciError error = cci.ExtractToIso(@"C:\games\game.iso", new CciExtractOptions
{
    Overwrite = false,
    Progress = new Progress<CciProgress>(p =>
        Console.WriteLine($"{p.SectorsDone}/{p.TotalSectors} sectors")),
});
```

### Stream and seek

```csharp
using CciFile cci = CciFile.OpenOrThrow(@"C:\games\game.1.cci");
using Stream stream = cci.OpenStream();

stream.Seek(0x10000, SeekOrigin.Begin);
byte[] buffer = new byte[8192];
int read = stream.Read(buffer);
```

### Create a CCI

```csharp
CciError error = CciWriter.Compress(
    @"C:\games\game.iso",
    @"C:\games\game.cci",
    new CciWriteOptions
    {
        Level = 9,                 // 0 = store, 1..9 = speed/ratio presets
        SplitBytes = CciFormat.DefaultSplitBytes,
        Overwrite = false,
    });
```

With `SplitBytes > 0`, output parts are written as `game.1.cci`,
`game.2.cci`, …; a single-part image is written as `game.cci`.

### Validate

```csharp
using CciFile cci = CciFile.OpenOrThrow(@"C:\games\game.cci");
CciError error = cci.Validate(deep: true, out CciValidationReport? report);
if (error != CciError.None)
{
    foreach (CciValidationIssue issue in report!.Issues)
        Console.Error.WriteLine($"{issue.PartIndex}:{issue.Sector}: {issue.Reason}");
}
```

## Split sets

A split image is a sequence of independent CCI containers, each covering a
contiguous range of sectors. Open the first part (`game.1.cci`) and the
library discovers the remaining parts (`game.2.cci` … `game.9.cci`), exactly
like the reference implementation.

- `game.cci` is treated as a single-part image.
- `game.1.cci` triggers split discovery.
- Parts are concatenated by sector range, not by file size.

## Format notes

The on-disk format is documented in
[`FORMAT.md`](https://github.com/Team-Resurgent/libCCI/blob/main/FORMAT.md).
Key facts:

- 32-byte little-endian header, magic `CCIM` (`0x4D494343`).
- 2048-byte logical sectors.
- Index of `sectors + 1` `uint32` entries at the end of each part.
- Bit 31 of an index entry marks LZ4; the rest is `offset >> 2`.
- Compressed block layout: `[pad][LZ4 block][zero padding]`.
- Version 1, index alignment 2.

CCISharp's encoder is compatible with the format but is not byte-identical to
libCCI/Repackinator output (different LZ4 encoders). Any conforming LZ4 block
is accepted by readers.

## Error handling

Public APIs return `CciError`. `CciError.None` means success. Argument errors
(`null` paths, negative sizes) throw standard .NET argument exceptions.
`CciFile.OpenOrThrow` is a convenience wrapper that throws `CciException` with
the error code and message.

## Building

```sh
dotnet build CCISharp.sln -c Release
dotnet test CCISharp.Tests/CCISharp.Tests.csproj -c Release
```

The repository uses MinVer; release tags are `v1.0.0`, `v1.0.1`, ….

## Interoperability

Verified against the reference `libCCI` tools:

```sh
cci_encode game.iso game        # -> game.cci
cci_extract game.cci out.iso    # -> raw ISO
cci_info game.cci               # -> sector count / size
```

The `References/libCCI-0.1.0-1b0087e` copy used during development is for
testing only and is not part of this package.

## License

MIT. See [LICENSE](LICENSE) and [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).
The CCI format specification comes from Team-Resurgent/libCCI
(GPL-2.0-or-later); no libCCI code is included in this MIT package.

