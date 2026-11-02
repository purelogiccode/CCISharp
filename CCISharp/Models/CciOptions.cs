namespace CCISharp;

/// <summary>Options for opening a CCI image.</summary>
public sealed record CciReaderOptions
{
    /// <summary>File sharing mode used for the part files. Defaults to <see cref="FileShare.Read"/>.</summary>
    public FileShare Share { get; init; } = FileShare.Read;

    /// <summary>
    /// Validate index bounds and monotonicity while opening. Defaults to <c>true</c>.
    /// Per-sector bounds are still checked on every read when this is <c>false</c>.
    /// </summary>
    public bool ValidateIndex { get; init; } = true;

    /// <summary>
    /// Largest accepted byte span for one sector blob. Bounds allocations from
    /// corrupt indexes. Defaults to 1 MiB (a valid blob is at most 2048 bytes).
    /// </summary>
    public int MaxSectorSpan { get; init; } = 1 << 20;

    /// <summary>
    /// Hard cap on index bytes read from one part. Internal in 1.0.1: the default
    /// (<see cref="CciFormat.MaxIndexBytes"/>, 64 MiB) is deliberately far above
    /// any valid part, so it needs no public knob yet. Tests lower it to exercise
    /// the rejection path without multi-gigabyte files.
    /// </summary>
    internal long MaxIndexBytes { get; init; } = CciFormat.MaxIndexBytes;
}

/// <summary>Options for creating a CCI image.</summary>
public sealed record CciWriteOptions
{
    /// <summary>
    /// Compression preset: <c>0</c> stores every sector raw; <c>1..9</c> map to
    /// the LZ4 block encoder's acceleration (<c>10 - level</c>, 9 = smallest).
    /// </summary>
    public int Level { get; init; } = 9;

    /// <summary>
    /// Split target in bytes. <c>0</c> writes a single part. Defaults to
    /// <see cref="CciFormat.DefaultSplitBytes"/> (~4 GiB).
    /// </summary>
    public long SplitBytes { get; init; } = CciFormat.DefaultSplitBytes;

    /// <summary>Buffer size for the source/output streams. Defaults to 1 MiB.</summary>
    public int BufferSize { get; init; } = 1 << 20;

    /// <summary>Overwrite existing output files. Defaults to <c>false</c>.</summary>
    public bool Overwrite { get; init; }

    /// <summary>Optional progress sink.</summary>
    public IProgress<CciProgress>? Progress { get; init; }

    /// <summary>Cancellation token.</summary>
    public CancellationToken CancellationToken { get; init; }
}

/// <summary>Options for extracting a CCI image to a raw ISO.</summary>
public sealed record CciExtractOptions
{
    /// <summary>Overwrite an existing output file. Defaults to <c>false</c>.</summary>
    public bool Overwrite { get; init; }

    /// <summary>Pre-size the output file before writing. Defaults to <c>true</c>.</summary>
    public bool Preallocate { get; init; } = true;

    /// <summary>Optional progress sink.</summary>
    public IProgress<CciProgress>? Progress { get; init; }

    /// <summary>Cancellation token.</summary>
    public CancellationToken CancellationToken { get; init; }
}
