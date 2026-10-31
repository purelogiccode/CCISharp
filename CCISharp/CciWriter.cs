using System.Buffers;
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

        int[] hashTable = new int[Lz4.HashTableSize];
        byte[] sector = ArrayPool<byte>.Shared.Rent(CciFormat.SectorSize);
        byte[] block = ArrayPool<byte>.Shared.Rent(CciFormat.SectorSize);

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
            int acceleration = Acceleration(options.Level);
            long bytesIn = 0;
            long bytesOut = 0;
            long reportInterval = Math.Max(1, totalSectors / 100);

            for (long index = 0; index < totalSectors; index++)
            {
                options.CancellationToken.ThrowIfCancellationRequested();

                int read = ReadFull(source, sector, CciFormat.SectorSize);
                if (read == 0)
                {
                    return CciError.Truncated;
                }

                if (read < CciFormat.SectorSize)
                {
                    Array.Clear(sector, read, CciFormat.SectorSize - read);
                }

                CciError encode = CciSectorCodec.TryEncode(
                    sector.AsSpan(0, CciFormat.SectorSize),
                    block,
                    acceleration,
                    hashTable,
                    out int written,
                    out bool compressed);

                if (encode != CciError.None)
                {
                    return encode;
                }

                if (position > CciFormat.MaxRepresentablePosition)
                {
                    return CciError.Range;
                }

                entries.Add((uint)(position >> CciFormat.IndexAlignment) |
                            (compressed ? 0x80000000u : 0u));
                output.Write(block, 0, written);

                position += written;
                bytesIn += read;
                bytesOut += written;

                if (index % reportInterval == 0)
                {
                    options.Progress?.Report(new CciProgress(index, totalSectors, bytesIn, bytesOut, 0));
                }
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

            options.Progress?.Report(new CciProgress(totalSectors, totalSectors, bytesIn, bytesOut, 0));
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
        finally
        {
            ArrayPool<byte>.Shared.Return(sector);
            ArrayPool<byte>.Shared.Return(block);
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

        int[] hashTable = new int[Lz4.HashTableSize];
        byte[] sector = ArrayPool<byte>.Shared.Rent(CciFormat.SectorSize);
        byte[] block = ArrayPool<byte>.Shared.Rent(CciFormat.SectorSize);
        List<(string Temp, string Final)> files = new();
        bool success = false;
        Span<byte> placeholder = stackalloc byte[CciFormat.HeaderSize];
        Span<byte> word = stackalloc byte[4];
        Span<byte> headerBytes = stackalloc byte[CciFormat.HeaderSize];

        try
        {
            long sectorsDone = 0;
            long bytesOut = 0;
            bool endOfInput = false;
            int partIndex = 0;
            int acceleration = Acceleration(options.Level);
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

                        if (split && partSectors > 0)
                        {
                            long estimated = position + ((partSectors + 2) * 4L) + CciFormat.SectorSize;
                            if (estimated > options.SplitBytes)
                            {
                                break;
                            }
                        }

                        int read = ReadFull(source, sector, CciFormat.SectorSize);
                        if (read == 0)
                        {
                            endOfInput = true;
                            break;
                        }

                        if (read < CciFormat.SectorSize)
                        {
                            Array.Clear(sector, read, CciFormat.SectorSize - read);
                        }

                        CciError encode = CciSectorCodec.TryEncode(
                            sector.AsSpan(0, CciFormat.SectorSize),
                            block,
                            acceleration,
                            hashTable,
                            out int written,
                            out bool compressed);

                        if (encode != CciError.None)
                        {
                            return encode;
                        }

                        if (position > CciFormat.MaxRepresentablePosition)
                        {
                            return CciError.Range;
                        }

                        entries.Add((uint)(position >> CciFormat.IndexAlignment) |
                                    (compressed ? 0x80000000u : 0u));
                        output.Write(block, 0, written);

                        position += written;
                        partSectors++;
                        sectorsDone++;
                        bytesOut += written;

                        if (sectorsDone % reportInterval == 0)
                        {
                            options.Progress?.Report(new CciProgress(
                                sectorsDone,
                                totalSectors,
                                Math.Min(inputLength, sectorsDone * CciFormat.SectorSize),
                                bytesOut,
                                partIndex));
                        }
                    }

                    if (partSectors == 0)
                    {
                        break;
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

            foreach ((string temp, string final) in files)
            {
                File.Move(temp, final, options.Overwrite);
            }

            success = true;
            options.Progress?.Report(new CciProgress(
                sectorsDone,
                totalSectors,
                Math.Min(inputLength, sectorsDone * CciFormat.SectorSize),
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
        catch (IOException)
        {
            return CciError.Io;
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(sector);
            ArrayPool<byte>.Shared.Return(block);

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

    private static int Acceleration(int level) => level == 0 ? 0 : Math.Clamp(10 - level, 1, 10);

    private static CciError ValidateOptions(CciWriteOptions options)
    {
        if (options.Level is < 0 or > 9)
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

    private static int ReadFull(Stream stream, byte[] buffer, int count)
    {
        int total = 0;
        while (total < count)
        {
            int read = stream.Read(buffer, total, count - total);
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
