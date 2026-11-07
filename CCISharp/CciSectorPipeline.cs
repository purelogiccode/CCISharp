using System.Buffers;
using System.Collections.Concurrent;

namespace CCISharp;

/// <summary>
/// Ordered, bounded-parallel sector encoder. It reads sectors in batches,
/// encodes them with up to <c>jobs</c> workers and yields the encoded blocks in
/// sector order. Parallelism never changes the bytes written, so a caller that
/// writes blocks sequentially produces an image identical to the
/// single-threaded encoder.
/// </summary>
internal sealed class CciSectorPipeline : IDisposable
{
    private const int SectorSize = CciFormat.SectorSize;

    private readonly Stream _source;
    private readonly int _level;
    private readonly int _jobs;
    private readonly CancellationToken _ct;
    private readonly int _batchSectors;
    private readonly byte[] _input;
    private readonly byte[] _output;
    private readonly int[] _lengths;
    private readonly bool[] _compressed;
    private readonly ConcurrentBag<CciSectorEncoder> _encoderPool = new();
    private CciSectorEncoder? _sequentialEncoder;
    private int _batchCount;
    private int _batchIndex;
    private int _errorCode;
    private bool _endOfInput;
    private bool _disposed;

    public CciSectorPipeline(Stream source, int level, int jobs, CancellationToken ct)
    {
        _source = source;
        _level = level;
        _jobs = jobs;
        _ct = ct;
        _batchSectors = Math.Clamp(jobs * 16, 64, 1024);

        _input = ArrayPool<byte>.Shared.Rent(_batchSectors * SectorSize);
        _output = ArrayPool<byte>.Shared.Rent(_batchSectors * SectorSize);
        _lengths = new int[_batchSectors];
        _compressed = new bool[_batchSectors];
    }

    /// <summary>First encoding error seen by a worker, or <see cref="CciError.None"/>.</summary>
    public CciError Error => (CciError)Volatile.Read(ref _errorCode);

    /// <summary>Number of decoded source bytes consumed so far, including a partial final sector.</summary>
    public long BytesRead { get; private set; }

    /// <summary>
    /// Yields the next encoded block in sector order. Returns <c>false</c> at
    /// end of input or after <see cref="Error"/> is set.
    /// </summary>
    public bool TryNext(out ReadOnlySpan<byte> block, out bool compressed)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        block = default;
        compressed = false;
        if (Error != CciError.None)
        {
            return false;
        }

        if (_batchIndex >= _batchCount)
        {
            if (_endOfInput)
            {
                return false;
            }

            FillBatch();
            if (Error != CciError.None || _batchCount == 0)
            {
                return false;
            }
        }

        int index = _batchIndex++;
        block = _output.AsSpan(index * SectorSize, _lengths[index]);
        compressed = _compressed[index];
        return true;
    }

    private void FillBatch()
    {
        _batchIndex = 0;
        _batchCount = 0;
        int inputBytes = 0;

        while (_batchCount < _batchSectors)
        {
            _ct.ThrowIfCancellationRequested();

            int read = CciWriter.ReadFull(_source, _input, inputBytes, SectorSize);
            if (read == 0)
            {
                _endOfInput = true;
                break;
            }

            if (read < SectorSize)
            {
                Array.Clear(_input, inputBytes + read, SectorSize - read);
            }

            inputBytes += read;
            BytesRead += read;
            _batchCount++;
        }

        if (_batchCount == 0)
        {
            return;
        }

        if (_jobs <= 1)
        {
            _sequentialEncoder ??= new CciSectorEncoder(_level);
            for (int index = 0; index < _batchCount; index++)
            {
                EncodeOne(_sequentialEncoder, index);
            }

            return;
        }

        Parallel.For(
            0,
            _batchCount,
            new ParallelOptions { MaxDegreeOfParallelism = _jobs, CancellationToken = _ct },
            index =>
            {
                if (!_encoderPool.TryTake(out CciSectorEncoder? encoder))
                {
                    encoder = new CciSectorEncoder(_level);
                }

                try
                {
                    EncodeOne(encoder, index);
                }
                finally
                {
                    _encoderPool.Add(encoder);
                }
            });
    }

    private void EncodeOne(CciSectorEncoder encoder, int index)
    {
        if (Error != CciError.None)
        {
            return;
        }

        CciError error = encoder.Encode(
            _input.AsSpan(index * SectorSize, SectorSize),
            _output.AsSpan(index * SectorSize, SectorSize),
            out int written,
            out bool compressed);

        if (error != CciError.None)
        {
            Interlocked.Exchange(ref _errorCode, (int)error);
            return;
        }

        _lengths[index] = written;
        _compressed[index] = compressed;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        ArrayPool<byte>.Shared.Return(_input);
        ArrayPool<byte>.Shared.Return(_output);
        _sequentialEncoder?.Dispose();
        while (_encoderPool.TryTake(out CciSectorEncoder? encoder))
        {
            encoder.Dispose();
        }
    }
}
