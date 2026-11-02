using System.Buffers;
using System.Globalization;

namespace CCISharp;

/// <summary>
/// A CCI ("CCIM") compressed Xbox disc image, optionally split across several
/// independent parts (<c>name.1.cci</c> … <c>name.9.cci</c>).
/// </summary>
public sealed class CciFile : IDisposable
{
    private sealed class Part : IDisposable
    {
        public Part(string path, Stream stream, bool ownsStream, CciHeader header, CciIndex index, ulong startSector)
        {
            Path = path;
            Stream = stream;
            OwnsStream = ownsStream;
            Header = header;
            Index = index;
            StartSector = startSector;
        }

        public string Path { get; }

        public Stream Stream { get; }

        public bool OwnsStream { get; }

        public CciHeader Header { get; }

        public CciIndex Index { get; }

        public ulong StartSector { get; }

        public object Sync { get; } = new();

        public void Dispose()
        {
            if (OwnsStream)
            {
                Stream.Dispose();
            }
        }
    }

    private readonly Part[] _parts;
    private readonly CciPartInfo[] _partInfos;
    private readonly int _maxSectorSpan;
    private bool _disposed;

    private CciFile(Part[] parts, int maxSectorSpan)
    {
        _parts = parts;
        _maxSectorSpan = maxSectorSpan;

        _partInfos = new CciPartInfo[parts.Length];
        ulong totalSectors = 0;
        long compressedSize = 0;

        for (int i = 0; i < parts.Length; i++)
        {
            Part part = parts[i];
            ulong sectors = part.Header.SectorCount;
            totalSectors += sectors;

            long partLength;
            try
            {
                partLength = part.Stream.Length;
            }
            catch (NotSupportedException)
            {
                partLength = 0;
            }

            compressedSize += partLength;
            _partInfos[i] = new CciPartInfo(
                part.Path,
                part.StartSector,
                sectors,
                sectors * (ulong)CciFormat.SectorSize,
                partLength,
                part.Header.IndexOffset);
        }

        TotalSectors = totalSectors;
        Length = checked((long)(totalSectors * (ulong)CciFormat.SectorSize));
        CompressedSize = compressedSize;
    }

    /// <summary>Header of the first part (all parts share the same format settings).</summary>
    public CciHeader Header => _parts[0].Header;

    /// <summary>Metadata for every part, in image order.</summary>
    public IReadOnlyList<CciPartInfo> Parts => _partInfos;

    /// <summary>Total number of 2048-byte logical sectors.</summary>
    public ulong TotalSectors { get; }

    /// <summary>Total decoded size in bytes.</summary>
    public long Length { get; }

    /// <summary>Sum of the compressed part file sizes (best effort for non-file streams).</summary>
    public long CompressedSize { get; }

    /// <summary>Path of the first part, or <c>null</c> when opened from a stream.</summary>
    public string? FirstPath => string.IsNullOrEmpty(_parts[0].Path) ? null : _parts[0].Path;

    /// <summary>Opens a CCI image (single part or split set) from a path.</summary>
    public static CciError Open(string path, out CciFile? cci) => Open(path, null, out cci);

    /// <summary>Opens a CCI image (single part or split set) from a path.</summary>
    public static CciError Open(string path, CciReaderOptions? options, out CciFile? cci)
    {
        cci = null;
        if (string.IsNullOrWhiteSpace(path))
        {
            return CciError.InvalidArgument;
        }

        options ??= new CciReaderOptions();
        if (options.MaxSectorSpan < CciFormat.SectorSize)
        {
            return CciError.InvalidArgument;
        }

        List<string> partPaths = DiscoverParts(path);
        if (partPaths.Count == 0)
        {
            return CciError.NotFound;
        }

        List<Part> parts = new(partPaths.Count);
        ulong startSector = 0;

        foreach (string partPath in partPaths)
        {
            FileStream stream;
            try
            {
                stream = new FileStream(
                    partPath,
                    FileMode.Open,
                    FileAccess.Read,
                    options.Share,
                    65536,
                    FileOptions.RandomAccess);
            }
            catch (FileNotFoundException)
            {
                DisposeParts(parts);
                return CciError.NotFound;
            }
            catch (DirectoryNotFoundException)
            {
                DisposeParts(parts);
                return CciError.NotFound;
            }
            catch (UnauthorizedAccessException)
            {
                DisposeParts(parts);
                return CciError.AccessDenied;
            }
            catch (ArgumentException)
            {
                DisposeParts(parts);
                return CciError.InvalidArgument;
            }
            catch (IOException)
            {
                DisposeParts(parts);
                return CciError.Io;
            }

            CciError error = LoadPart(stream, ownsStream: true, partPath, startSector, options, out Part? part);
            if (error != CciError.None || part is null)
            {
                stream.Dispose();
                DisposeParts(parts);
                return error;
            }

            parts.Add(part);
            startSector = checked(startSector + part.Header.SectorCount);
        }

        if (parts.Count == 0)
        {
            return CciError.NotFound;
        }

        try
        {
            cci = new CciFile([.. parts], options.MaxSectorSpan);
        }
        catch (OverflowException)
        {
            DisposeParts(parts);
            return CciError.InvalidSize;
        }

        return CciError.None;
    }

    /// <summary>Opens a CCI image from a path and throws on failure.</summary>
    public static CciFile OpenOrThrow(string path, CciReaderOptions? options = null)
    {
        CciError error = Open(path, options, out CciFile? cci);
        if (error != CciError.None || cci is null)
        {
            throw new CciException(error);
        }

        return cci;
    }

    /// <summary>Opens a single CCI container from a seekable stream.</summary>
    public static CciError Open(Stream stream, bool ownsStream, out CciFile? cci) =>
        Open(stream, ownsStream, null, out cci);

    /// <summary>Opens a single CCI container from a seekable stream.</summary>
    public static CciError Open(Stream stream, bool ownsStream, CciReaderOptions? options, out CciFile? cci)
    {
        cci = null;
        if (stream is null)
        {
            return CciError.InvalidArgument;
        }

        options ??= new CciReaderOptions();
        if (!stream.CanRead || !stream.CanSeek || options.MaxSectorSpan < CciFormat.SectorSize)
        {
            return CciError.InvalidArgument;
        }

        CciError error = LoadPart(stream, ownsStream, string.Empty, 0, options, out Part? part);
        if (error != CciError.None || part is null)
        {
            return error;
        }

        try
        {
            cci = new CciFile([part], options.MaxSectorSpan);
        }
        catch (OverflowException)
        {
            part.Dispose();
            return CciError.InvalidSize;
        }

        return CciError.None;
    }

    /// <summary>Reads one logical sector into <paramref name="buffer"/>.</summary>
    public CciError ReadSector(ulong sector, Span<byte> buffer)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (buffer.Length < CciFormat.SectorSize)
        {
            return CciError.InvalidArgument;
        }

        Part? part = FindPart(sector);
        if (part is null)
        {
            return CciError.Range;
        }

        int localSector = checked((int)(sector - part.StartSector));
        if (!part.Index.TryGetSpan(localSector, _maxSectorSpan, out long offset, out int length, out bool lz4))
        {
            return CciError.CorruptSector;
        }

        byte[] blob = ArrayPool<byte>.Shared.Rent(length);
        try
        {
            lock (part.Sync)
            {
                try
                {
                    part.Stream.Seek(offset, SeekOrigin.Begin);
                    part.Stream.ReadExactly(blob, 0, length);
                }
                catch (EndOfStreamException)
                {
                    return CciError.Truncated;
                }
                catch (IOException)
                {
                    return CciError.Io;
                }
            }

            return CciSectorCodec.TryDecode(blob.AsSpan(0, length), lz4, buffer);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(blob);
        }
    }

    /// <summary>Reads an arbitrary byte range. The range must be inside the image.</summary>
    public CciError Read(ulong offset, Span<byte> buffer)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (buffer.Length == 0)
        {
            return CciError.None;
        }

        if (offset >= (ulong)Length || (ulong)buffer.Length > (ulong)Length - offset)
        {
            return CciError.Range;
        }

        byte[] sector = ArrayPool<byte>.Shared.Rent(CciFormat.SectorSize);
        try
        {
            int written = 0;
            while (written < buffer.Length)
            {
                ulong position = offset + (ulong)written;
                ulong sectorIndex = position / CciFormat.SectorSize;
                int inSector = (int)(position % CciFormat.SectorSize);

                CciError error = ReadSector(sectorIndex, sector);
                if (error != CciError.None)
                {
                    return error;
                }

                int copy = Math.Min(CciFormat.SectorSize - inSector, buffer.Length - written);
                sector.AsSpan(inSector, copy).CopyTo(buffer[written..]);
                written += copy;
            }

            return CciError.None;
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(sector);
        }
    }

    /// <summary>Returns a seekable, read-only stream over the decoded image.</summary>
    public CciStream OpenStream()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return new CciStream(this);
    }

    /// <summary>Extracts the decoded image to a raw ISO file.</summary>
    public CciError ExtractToIso(string outputPath, CciExtractOptions? options = null)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (string.IsNullOrWhiteSpace(outputPath))
        {
            return CciError.InvalidArgument;
        }

        options ??= new CciExtractOptions();

        try
        {
            if (File.Exists(outputPath) && !options.Overwrite)
            {
                return CciError.AlreadyExists;
            }
        }
        catch (UnauthorizedAccessException)
        {
            return CciError.AccessDenied;
        }
        catch (IOException)
        {
            return CciError.Io;
        }

        string tempPath = outputPath + ".tmp";
        byte[] sector = ArrayPool<byte>.Shared.Rent(CciFormat.SectorSize);
        CciError failure = CciError.None;
        try
        {
            TryDelete(tempPath);

            using (FileStream output = new(
                       tempPath,
                       FileMode.Create,
                       FileAccess.Write,
                       FileShare.None,
                       1 << 20,
                       FileOptions.SequentialScan))
            {
                if (options.Preallocate)
                {
                    output.SetLength(Length);
                }

                long reportInterval = Math.Max(1, (long)TotalSectors / 100);
                long bytesOut = 0;

                for (ulong index = 0; index < TotalSectors; index++)
                {
                    options.CancellationToken.ThrowIfCancellationRequested();

                    CciError error = ReadSector(index, sector);
                    if (error != CciError.None)
                    {
                        failure = error;
                        break;
                    }

                    output.Write(sector, 0, CciFormat.SectorSize);
                    bytesOut += CciFormat.SectorSize;

                    if ((long)index % reportInterval == 0)
                    {
                        options.Progress?.Report(new CciProgress(
                            (long)index,
                            (long)TotalSectors,
                            (long)(index * (ulong)CciFormat.SectorSize),
                            bytesOut,
                            0));
                    }
                }

                if (failure == CciError.None)
                {
                    options.Progress?.Report(new CciProgress(
                        (long)TotalSectors,
                        (long)TotalSectors,
                        Length,
                        bytesOut,
                        0));
                }
            }

            if (failure != CciError.None)
            {
                TryDelete(tempPath);
                return failure;
            }

            File.Move(tempPath, outputPath, options.Overwrite);
            return CciError.None;
        }
        catch (OperationCanceledException)
        {
            TryDelete(tempPath);
            return CciError.Cancelled;
        }
        catch (UnauthorizedAccessException)
        {
            TryDelete(tempPath);
            return CciError.AccessDenied;
        }
        catch (IOException)
        {
            TryDelete(tempPath);
            return CciError.Io;
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(sector);
        }
    }

    /// <summary>Validates every part header/index and, optionally, every sector.</summary>
    public CciError Validate(bool deep, out CciValidationReport? report)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        List<CciValidationIssue> issues = new();
        byte[] sector = ArrayPool<byte>.Shared.Rent(CciFormat.SectorSize);
        bool corrupt = false;

        try
        {
            for (int partIndex = 0; partIndex < _parts.Length; partIndex++)
            {
                Part part = _parts[partIndex];
                ulong sectors = part.Header.SectorCount;

                for (ulong local = 0; local < sectors; local++)
                {
                    if (!part.Index.TryGetSpan((int)local, _maxSectorSpan, out _, out _, out _))
                    {
                        issues.Add(new CciValidationIssue(partIndex, local, "index span is invalid"));
                        corrupt = true;
                        continue;
                    }

                    if (deep)
                    {
                        CciError error = ReadSector(part.StartSector + local, sector);
                        if (error != CciError.None)
                        {
                            issues.Add(new CciValidationIssue(partIndex, local, error.ToMessage()));
                            corrupt = true;
                        }
                    }
                }
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(sector);
        }

        report = new CciValidationReport(
            issues.Count == 0,
            _partInfos,
            TotalSectors,
            (ulong)Length,
            CompressedSize,
            Length == 0 ? 0 : (double)CompressedSize / Length,
            issues);

        return issues.Count == 0 ? CciError.None : corrupt ? CciError.CorruptSector : CciError.InvalidIndex;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        DisposeParts(_parts);
    }

    private static void DisposeParts(IEnumerable<Part> parts)
    {
        foreach (Part part in parts)
        {
            part.Dispose();
        }
    }

    private static CciError LoadPart(
        Stream stream,
        bool ownsStream,
        string path,
        ulong startSector,
        CciReaderOptions options,
        out Part? part)
    {
        part = null;

        long fileLength;
        try
        {
            fileLength = stream.Length;
        }
        catch (IOException)
        {
            return CciError.Io;
        }

        if (fileLength < CciFormat.HeaderSize)
        {
            return CciError.Truncated;
        }

        byte[] headerBytes = new byte[CciFormat.HeaderSize];
        try
        {
            stream.Seek(0, SeekOrigin.Begin);
            stream.ReadExactly(headerBytes, 0, headerBytes.Length);
        }
        catch (EndOfStreamException)
        {
            return CciError.Truncated;
        }
        catch (IOException)
        {
            return CciError.Io;
        }

        CciError error = CciHeader.TryRead(headerBytes, out CciHeader header);
        if (error != CciError.None)
        {
            return error;
        }

        if (header.SectorCount > int.MaxValue || header.IndexOffset > long.MaxValue)
        {
            return CciError.InvalidSize;
        }

        long indexOffset = (long)header.IndexOffset;
        long indexLength = checked(((long)header.SectorCount + 1) * 4);

        // Cap the index before any allocation: a hostile header can claim a huge
        // sector count on a large part, and the int casts below would otherwise
        // truncate the length and throw from ArrayPool.Rent instead of returning
        // an error code.
        if (indexLength > options.MaxIndexBytes)
        {
            return CciError.InvalidIndex;
        }

        if (indexOffset < CciFormat.HeaderSize ||
            indexOffset > fileLength ||
            indexLength > fileLength - indexOffset)
        {
            return CciError.InvalidIndex;
        }

        int indexSize = (int)indexLength;
        byte[] indexBytes = ArrayPool<byte>.Shared.Rent(indexSize);
        try
        {
            try
            {
                stream.Seek(indexOffset, SeekOrigin.Begin);
                stream.ReadExactly(indexBytes, 0, indexSize);
            }
            catch (EndOfStreamException)
            {
                return CciError.Truncated;
            }
            catch (IOException)
            {
                return CciError.Io;
            }

            error = CciIndex.TryRead(
                indexBytes.AsSpan(0, indexSize),
                (int)header.SectorCount,
                header.IndexOffset,
                options.MaxSectorSpan,
                options.ValidateIndex,
                out CciIndex? index);

            if (error != CciError.None || index is null)
            {
                return error == CciError.None ? CciError.InvalidIndex : error;
            }

            part = new Part(path, stream, ownsStream, header, index, startSector);
            return CciError.None;
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(indexBytes);
        }
    }

    private static List<string> DiscoverParts(string path)
    {
        List<string> single = [path];
        if (!path.EndsWith(CciFormat.Extension, StringComparison.OrdinalIgnoreCase))
        {
            return single;
        }

        string stem = path[..^CciFormat.Extension.Length];
        if (stem.Length < 2 || stem[^2] != '.' || !char.IsDigit(stem[^1]))
        {
            return single;
        }

        string baseName = stem[..^2];
        string extension = path[^CciFormat.Extension.Length..];
        List<string> parts = new(9);

        for (int index = 1; index <= 9; index++)
        {
            string candidate = string.Concat(
                baseName,
                ".",
                index.ToString(CultureInfo.InvariantCulture),
                extension);

            if (!File.Exists(candidate))
            {
                break;
            }

            parts.Add(candidate);
        }

        return parts.Count > 0 ? parts : single;
    }

    private Part? FindPart(ulong sector)
    {
        int low = 0;
        int high = _parts.Length - 1;

        while (low <= high)
        {
            int mid = low + ((high - low) / 2);
            Part part = _parts[mid];
            ulong end = part.StartSector + part.Header.SectorCount;

            if (sector < part.StartSector)
            {
                high = mid - 1;
            }
            else if (sector >= end)
            {
                low = mid + 1;
            }
            else
            {
                return part;
            }
        }

        return null;
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (IOException)
        {
            // Best effort.
        }
        catch (UnauthorizedAccessException)
        {
            // Best effort.
        }
    }
}

