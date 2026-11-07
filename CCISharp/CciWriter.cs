using System.Buffers.Binary;

namespace CCISharp;

/// <summary>
/// Creates CCI ("CCIM") containers from raw ISO images or sector streams.
/// </summary>
public static class CciWriter
{
    /// <summary>Compresses a raw ISO file into one or more CCI parts.</summary>
    public static CciError Compress(string sourcePath, string outputPath, CciWriteOptions? options = null)
    {
        if (string.IsNullOrWhiteSpace(sourcePath) || string.IsNullOrWhiteSpace(outputPath))
        {
            return CciError.InvalidArgument;
        }

        options ??= new CciWriteOptions();
        CciError validation = ValidateOptions(options);
        if (validation != CciError.None)
        {
            return validation;
        }

        long inputLength;
        try
        {
            FileInfo info = new(sourcePath);
            if (!info.Exists)
            {
                return CciError.NotFound;
            }

            inputLength = info.Length;
        }
        catch (UnauthorizedAccessException)
        {
            return CciError.AccessDenied;
        }
        catch (ArgumentException)
        {
            return CciError.InvalidArgument;
        }
        catch (IOException)
        {
            return CciError.Io;
        }

        if (inputLength <= 0)
        {
            return CciError.InvalidSize;
        }

        options.CancellationToken.ThrowIfCancellationRequested();

        bool split = options.SplitBytes > 0;
        string singlePath;
        string basePath;

        if (outputPath.EndsWith(CciFormat.Extension, StringComparison.OrdinalIgnoreCase))
        {
            singlePath = outputPath;
            basePath = outputPath[..^CciFormat.Extension.Length];
        }
        else
        {
            singlePath = outputPath + CciFormat.Extension;
            basePath = outputPath;
        }

        // Refuse before writing anything if the target already exists.
        if (!options.Overwrite)
        {
            if (split)
            {
                for (int part = 1; part <= 9; part++)
                {
                    if (File.Exists(PartPath(basePath, part)))
                    {
                        return CciError.AlreadyExists;
                    }
                }
            }
            else if (File.Exists(singlePath))
            {
                return CciError.AlreadyExists;
            }
        }

        try
        {
            using FileStream source = new(
                sourcePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                options.BufferSize,
                FileOptions.SequentialScan);

            return WriteParts(source, inputLength, singlePath, basePath, split, options);
        }
        catch (OperationCanceledException)
        {
            return CciError.Cancelled;
        }
        catch (UnauthorizedAccessException)
        {
            return CciError.AccessDenied;
        }
        catch (ArgumentException)
        {
            return CciError.InvalidArgument;
        }
        catch (IOException)
        {
            return CciError.Io;
        }
    }

    /// <summary>Asynchronous wrapper around <see cref="Compress(string, string, CciWriteOptions?)"/>.</summary>
    public static Task<CciError> CompressAsync(string sourcePath, string outputPath, CciWriteOptions? options = null)
    {
        options ??= new CciWriteOptions();
        return Task.Run(() => Compress(sourcePath, outputPath, options), options.CancellationToken);
    }

    /// <summary>
    /// Compresses a readable stream into a single CCI container on a seekable
    /// output stream. Split output is not supported here; use the path overload.
    /// </summary>
    public static CciError Compress(Stream source, long inputLength, Stream output, CciWriteOptions? options = null)
    {
        if (source is null || output is null)
        {
            return CciError.InvalidArgument;
        }

        options ??= new CciWriteOptions();
        CciError validation = ValidateOptions(options);
        if (validation != CciError.None)
        {
            return validation;
        }

        if (inputLength <= 0 || !source.CanRead || !output.CanWrite || !output.CanSeek)
        {
            return CciError.InvalidArgument;
        }

        long totalSectors = (inputLength + CciFormat.SectorSize - 1) / CciFormat.SectorSize;
        if (totalSectors <= 0 || totalSectors > int.MaxValue)
        {
            return CciError.InvalidSize;
        }

        using CciSectorPipeline pipeline = new(source, options.Level, options.Jobs, options.CancellationToken);
        try
        {
            options.CancellationToken.ThrowIfCancellationRequested();

            output.SetLength(0);
            Span<byte> placeholder = stackalloc byte[CciFormat.HeaderSize];
            placeholder.Clear();
            output.Seek(0, SeekOrigin.Begin);
            output.Write(placeholder);

            long position = CciFormat.HeaderSize;
            List<uint> entries = new((int)totalSectors);
            long bytesOut = 0;
            long reportInterval = Math.Max(1, totalSectors / 100);
            long index = 0;

            while (pipeline.TryNext(out ReadOnlySpan<byte> block, out bool compressed))
            {
                options.CancellationToken.ThrowIfCancellationRequested();

                if (position > CciFormat.MaxRepresentablePosition)
                {
                    return CciError.Range;
                }

                entries.Add((uint)(position >> CciFormat.IndexAlignment) |
                            (compressed ? 0x80000000u : 0u));
                output.Write(block);

                position += block.Length;
                bytesOut += block.Length;

                if (index % reportInterval == 0)
                {
                    options.Progress?.Report(new CciProgress(
                        index,
                        totalSectors,
                        pipeline.BytesRead,
                        bytesOut,
                        0));
                }

                index++;
            }

            if (pipeline.Error != CciError.None)
            {
                return pipeline.Error;
            }

            if (index != totalSectors)
            {
                return CciError.Truncated;
            }

            long indexOffset = position;
            if (indexOffset > CciFormat.MaxRepresentablePosition)
            {
                return CciError.Range;
            }

            Span<byte> word = stackalloc byte[4];
            foreach (uint entry in entries)
            {
                BinaryPrimitives.WriteUInt32LittleEndian(word, entry);
                output.Write(word);
            }

            BinaryPrimitives.WriteUInt32LittleEndian(word, (uint)(indexOffset >> CciFormat.IndexAlignment));
            output.Write(word);

            CciHeader header = new(
                (ulong)totalSectors * CciFormat.SectorSize,
                (ulong)indexOffset,
                CciFormat.SectorSize,
                CciFormat.Version,
                CciFormat.IndexAlignment);

            Span<byte> headerBytes = stackalloc byte[CciFormat.HeaderSize];
            header.Write(headerBytes);
            output.Seek(0, SeekOrigin.Begin);
            output.Write(headerBytes);
            output.Flush();

            options.Progress?.Report(new CciProgress(totalSectors, totalSectors, pipeline.BytesRead, bytesOut, 0));
            return CciError.None;
        }
        catch (OperationCanceledException)
        {
            return CciError.Cancelled;
        }
        catch (IOException)
        {
            return CciError.Io;
        }
        catch (UnauthorizedAccessException)
        {
            return CciError.AccessDenied;
        }
    }

    private static CciError WriteParts(
        Stream source,
        long inputLength,
        string singlePath,
        string basePath,
        bool split,
        CciWriteOptions options)
    {
        long totalSectors = (inputLength + CciFormat.SectorSize - 1) / CciFormat.SectorSize;
        if (totalSectors <= 0)
        {
            return CciError.InvalidSize;
        }

        using CciSectorPipeline pipeline = new(source, options.Level, options.Jobs, options.CancellationToken);
        List<(string Temp, string Final)> files = new();
        bool success = false;
        Span<byte> placeholder = stackalloc byte[CciFormat.HeaderSize];
        Span<byte> word = stackalloc byte[4];
        Span<byte> headerBytes = stackalloc byte[CciFormat.HeaderSize];

        // A split boundary is decided after the next sector has been encoded,
        // so one encoded block can be parked until the next part starts.
        byte[]? pendingBytes = null;
        bool pendingCompressed = false;
        int pendingLength = 0;

        try
        {
            long sectorsDone = 0;
            long bytesOut = 0;
            bool endOfInput = false;
            int partIndex = 0;
            long reportInterval = Math.Max(1, totalSectors / 100);

            while (!endOfInput)
            {
                if (partIndex >= 9)
                {
                    return CciError.TooManyParts;
                }

                string finalPath = split ? PartPath(basePath, partIndex + 1) : singlePath;
                string tempPath = finalPath + ".tmp-" + Guid.NewGuid().ToString("N");
                files.Add((tempPath, finalPath));

                using (FileStream output = new(
                           tempPath,
                           FileMode.CreateNew,
                           FileAccess.Write,
                           FileShare.None,
                           options.BufferSize,
                           FileOptions.SequentialScan))
                {
                    placeholder.Clear();
                    output.Write(placeholder);

                    long position = CciFormat.HeaderSize;
                    int partSectors = 0;
                    List<uint> entries = new();

                    while (true)
                    {
                        options.CancellationToken.ThrowIfCancellationRequested();

                        ReadOnlySpan<byte> block;
                        bool compressed;
                        if (pendingBytes is not null)
                        {
                            block = pendingBytes.AsSpan(0, pendingLength);
                            compressed = pendingCompressed;
                            pendingBytes = null;
                        }
                        else if (pipeline.TryNext(out ReadOnlySpan<byte> next, out bool nextCompressed))
                        {
                            block = next;
                            compressed = nextCompressed;
                        }
                        else
                        {
                            endOfInput = true;
                            break;
                        }

                        if (split && partSectors > 0)
                        {
                            long estimated = position + ((partSectors + 2) * 4L) + CciFormat.SectorSize;
                            if (estimated > options.SplitBytes)
                            {
                                pendingBytes = block.ToArray();
                                pendingLength = block.Length;
                                pendingCompressed = compressed;
                                break;
                            }
                        }

                        if (position > CciFormat.MaxRepresentablePosition)
                        {
                            return CciError.Range;
                        }

                        entries.Add((uint)(position >> CciFormat.IndexAlignment) |
                                    (compressed ? 0x80000000u : 0u));
                        output.Write(block);

                        position += block.Length;
                        partSectors++;
                        sectorsDone++;
                        bytesOut += block.Length;

                        if (sectorsDone % reportInterval == 0)
                        {
                            options.Progress?.Report(new CciProgress(
                                sectorsDone,
                                totalSectors,
                                Math.Min(inputLength, pipeline.BytesRead),
                                bytesOut,
                                partIndex));
                        }
                    }

                    if (partSectors == 0)
                    {
                        break;
                    }

                    if (pipeline.Error != CciError.None)
                    {
                        return pipeline.Error;
                    }

                    long indexOffset = position;
                    if (indexOffset > CciFormat.MaxRepresentablePosition)
                    {
                        return CciError.Range;
                    }

                    foreach (uint entry in entries)
                    {
                        BinaryPrimitives.WriteUInt32LittleEndian(word, entry);
                        output.Write(word);
                    }

                    BinaryPrimitives.WriteUInt32LittleEndian(word, (uint)(indexOffset >> CciFormat.IndexAlignment));
                    output.Write(word);

                    CciHeader header = new(
                        (ulong)partSectors * CciFormat.SectorSize,
                        (ulong)indexOffset,
                        CciFormat.SectorSize,
                        CciFormat.Version,
                        CciFormat.IndexAlignment);

                    header.Write(headerBytes);
                    output.Seek(0, SeekOrigin.Begin);
                    output.Write(headerBytes);
                }

                partIndex++;
                if (!split || endOfInput)
                {
                    break;
                }
            }

            if (files.Count == 0)
            {
                return CciError.InvalidSize;
            }

            if (pipeline.Error != CciError.None)
            {
                return pipeline.Error;
            }

            if (sectorsDone != totalSectors)
            {
                return CciError.Truncated;
            }

            foreach ((string temp, string final) in files)
            {
                File.Move(temp, final, options.Overwrite);
            }

            success = true;
            options.Progress?.Report(new CciProgress(
                sectorsDone,
                totalSectors,
                Math.Min(inputLength, pipeline.BytesRead),
                bytesOut,
                Math.Max(0, files.Count - 1)));

            return CciError.None;
        }
        catch (OperationCanceledException)
        {
            return CciError.Cancelled;
        }
        catch (UnauthorizedAccessException)
        {
            return CciError.AccessDenied;
        }
        catch (ArgumentException)
        {
            return CciError.InvalidArgument;
        }
        catch (IOException)
        {
            return CciError.Io;
        }
        finally
        {
            if (!success)
            {
                foreach ((string temp, _) in files)
                {
                    TryDelete(temp);
                }
            }
        }
    }

    private static string PartPath(string basePath, int partNumber) =>
        string.Concat(basePath, ".", partNumber.ToString(System.Globalization.CultureInfo.InvariantCulture), CciFormat.Extension);

    internal static int Acceleration(int level) => level == 0 ? 0 : Math.Clamp(10 - level, 1, 10);

    private static CciError ValidateOptions(CciWriteOptions options)
    {
        if (options.Level is < 0 or > 12)
        {
            return CciError.InvalidArgument;
        }

        if (options.Jobs is < 1 or > CciWriteOptions.MaxJobs)
        {
            return CciError.InvalidArgument;
        }

        if (options.SplitBytes is > 0 and < CciFormat.SectorSize)
        {
            return CciError.InvalidArgument;
        }

        if (options.BufferSize < 4096)
        {
            return CciError.InvalidArgument;
        }

        return CciError.None;
    }

    internal static int ReadFull(Stream stream, byte[] buffer, int offset, int count)
    {
        int total = 0;
        while (total < count)
        {
            int read = stream.Read(buffer, offset + total, count - total);
            if (read == 0)
            {
                break;
            }

            total += read;
        }

        return total;
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
