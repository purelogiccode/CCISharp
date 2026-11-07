# CCISharp API guide

CCISharp targets `net8.0`, `net9.0` and `net10.0`, has no runtime dependencies,
and is trimmable/AOT-compatible. All public entry points return `CciError`
unless they are explicitly throwing convenience wrappers.

## Reading

```csharp
CciError error = CciFile.Open(@"C:\games\game.1.cci", out CciFile? cci);
if (error != CciError.None || cci is null)
{
    Console.Error.WriteLine(error.ToMessage());
    return;
}

using (cci)
{
    Console.WriteLine($"{cci.TotalSectors} sectors, {cci.Length} bytes");
    Console.WriteLine($"{cci.Parts.Count} part(s)");

    byte[] sector = new byte[CciFormat.SectorSize];
    cci.ReadSector(0, sector);

    using CciStream stream = cci.OpenStream();
    stream.Seek(0x10000, SeekOrigin.Begin);
}
```

`CciFile.Open(path)` resolves split sets. `CciFile.Open(stream, ownsStream)`
opens a single part from a seekable stream. `CciFile.OpenOrThrow` throws
`CciException` with the error code.

### Parts

`CciFile.Parts` exposes one `CciPartInfo` per part:

| Property | Meaning |
|---|---|
| `Path` | file path, or empty for stream input |
| `StartSector` | first logical sector covered by the part |
| `SectorCount` | sectors in the part |
| `UncompressedSize` | decoded bytes |
| `CompressedSize` | part file length |
| `IndexOffset` | header index offset |
| `Ratio` | compressed / uncompressed |

### Random access

- `ReadSector(ulong sector, Span<byte> buffer)` decodes one 2048-byte sector.
- `Read(ulong offset, Span<byte> buffer)` reads an arbitrary byte range.
- `OpenStream()` returns a seekable, read-only `CciStream`; it caches one sector.
- `ReadSector` is safe to call concurrently; `CciStream` instances are not
  thread-safe.

### Extraction and validation

- `ExtractToIso(path, CciExtractOptions)` writes through a temporary file and
  optionally preallocates the destination.
- `Validate(deep, out CciValidationReport)` checks the header/index and, when
  `deep` is true, decodes every sector. Issues are returned as
  `CciValidationIssue(partIndex, sector, reason)`.

## Writing

```csharp
CciError error = CciWriter.Compress(
    @"C:\games\game.iso",
    @"C:\games\game.cci",
    new CciWriteOptions
    {
        Level = 12,                // 10..12 = LZ4-HC
        Jobs = 4,                  // byte-identical to Jobs = 1
        SplitBytes = CciFormat.DefaultSplitBytes,
        Overwrite = false,
    });
```

| `Level` | Encoder |
|---:|---|
| 0 | store sectors raw |
| 1..9 | fast LZ4; 9 is the smallest |
| 10..12 | LZ4-HC; 12 is the highest effort |

`Jobs` accepts `1..CciWriteOptions.MaxJobs` (64). The path overload splits into
`name.1.cci` … `name.9.cci`; the stream overload produces a single part and
requires a seekable output stream.

`CciWriteOptions` also exposes `BufferSize`, `Overwrite`, `Progress` and
`CancellationToken`. `CompressAsync` wraps the synchronous path in `Task.Run`.

## LZ4

- `Lz4.Compress(input, destination, acceleration, hashTable)` — fast encoder.
- `Lz4.Decompress(source, destination)` — block decoder; throws
  `InvalidDataException` for malformed input.
- `Lz4.MaxCompressedOutputSize(length)` — destination size required by the
  fast encoder.
- `Lz4Hc.Compress(input, destination, level)` — managed high-compression
  encoder (levels 10..12).

`Lz4.Compress` at acceleration 1 is byte-identical to `lz4_flex` 0.11.3, which is
the encoder used by xdvdfs for CISO v2. `Lz4Hc` is not a liblz4 port; it is an
independent HC implementation and is documented as best-effort byte parity, not
a guarantee.

## Naming and discovery

`CciFile` accepts:

- `game.cci` — single part.
- `game.1.cci` … `game.9.cci` — the reference split layout.
- `game.10.cci` and higher — accepted when the contiguous set starts at `.1`.

See [format.md](format.md#split-sets) for the ambiguity rule.

## Errors

`CciError.None` means success. Argument problems (null paths, negative values)
throw standard .NET argument exceptions; I/O and format problems are returned as
error codes. `CciException` carries the code in `Error`.

Common codes: `NotFound`, `AlreadyExists`, `InvalidHeader`, `InvalidSize`,
`InvalidIndex`, `CorruptSector`, `Truncated`, `Range`, `TooManyParts`,
`Cancelled`.

## Command-line tool

`CCISharp.Cli` provides pack/unpack/info/validate for interop and smoke tests:

```sh
dotnet run --project CCISharp.Cli -- pack game.iso game --level 12 --jobs 4 --single -y
dotnet run --project CCISharp.Cli -- info game.cci
dotnet run --project CCISharp.Cli -- validate game.cci --deep
dotnet run --project CCISharp.Cli -- unpack game.cci game.iso -y
```
