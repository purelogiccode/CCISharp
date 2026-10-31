namespace CCISharp;

/// <summary>
/// Seekable, read-only <see cref="Stream"/> over the decoded image of a
/// <see cref="CciFile"/>. One sector is cached for sequential reads.
/// Instances are not thread-safe; use <see cref="CciFile.ReadSector"/> for
/// concurrent access.
/// </summary>
public sealed class CciStream : Stream
{
    private readonly CciFile _file;
    private readonly byte[] _sector = new byte[CciFormat.SectorSize];
    private long _position;
    private long _cachedSector = -1;
    private bool _disposed;

    internal CciStream(CciFile file)
    {
        _file = file;
    }

    /// <inheritdoc />
    public override bool CanRead => !_disposed;

    /// <inheritdoc />
    public override bool CanSeek => !_disposed;

    /// <inheritdoc />
    public override bool CanWrite => false;

    /// <inheritdoc />
    public override long Length
    {
        get
        {
            ThrowIfDisposed();
            return _file.Length;
        }
    }

    /// <inheritdoc />
    public override long Position
    {
        get => _position;
        set => Seek(value, SeekOrigin.Begin);
    }

    /// <inheritdoc />
    public override int Read(Span<byte> buffer)
    {
        ThrowIfDisposed();

        if (buffer.Length == 0 || _position >= _file.Length)
        {
            return 0;
        }

        int total = 0;
        while (total < buffer.Length && _position < _file.Length)
        {
            long sectorIndex = _position / CciFormat.SectorSize;
            int offsetInSector = (int)(_position % CciFormat.SectorSize);

            if (_cachedSector != sectorIndex)
            {
                CciError error = _file.ReadSector((ulong)sectorIndex, _sector);
                if (error != CciError.None)
                {
                    throw new IOException($"CCI read failed at sector {sectorIndex}: {error.ToMessage()}");
                }

                _cachedSector = sectorIndex;
            }

            long remainingInImage = _file.Length - _position;
            int copy = (int)Math.Min(
                CciFormat.SectorSize - offsetInSector,
                Math.Min(buffer.Length - total, remainingInImage));

            _sector.AsSpan(offsetInSector, copy).CopyTo(buffer[total..]);
            _position += copy;
            total += copy;
        }

        return total;
    }

    /// <inheritdoc />
    public override int Read(byte[] buffer, int offset, int count)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        if (offset < 0 || count < 0 || offset > buffer.Length - count)
        {
            throw new ArgumentOutOfRangeException(nameof(offset));
        }

        return Read(buffer.AsSpan(offset, count));
    }

    /// <inheritdoc />
    public override long Seek(long offset, SeekOrigin origin)
    {
        ThrowIfDisposed();

        long newPosition = origin switch
        {
            SeekOrigin.Begin => offset,
            SeekOrigin.Current => _position + offset,
            SeekOrigin.End => _file.Length + offset,
            _ => throw new ArgumentOutOfRangeException(nameof(origin)),
        };

        if (newPosition < 0)
        {
            throw new IOException("Cannot seek before the beginning of the stream.");
        }

        _position = newPosition;
        return _position;
    }

    /// <inheritdoc />
    public override void Flush()
    {
        // Read-only stream.
    }

    /// <inheritdoc />
    public override void SetLength(long value) => throw new NotSupportedException("CCI streams are read-only.");

    /// <inheritdoc />
    public override void Write(byte[] buffer, int offset, int count) =>
        throw new NotSupportedException("CCI streams are read-only.");

    /// <inheritdoc />
    public override void Write(ReadOnlySpan<byte> buffer) =>
        throw new NotSupportedException("CCI streams are read-only.");

    /// <inheritdoc />
    protected override void Dispose(bool disposing)
    {
        _disposed = true;
        base.Dispose(disposing);
    }

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);
}
