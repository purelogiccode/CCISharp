namespace CCISharp;

/// <summary>
/// Result codes returned by CCISharp APIs.
/// </summary>
public enum CciError
{
    /// <summary>Success.</summary>
    None = 0,

    /// <summary>An argument was null, empty or otherwise invalid.</summary>
    InvalidArgument,

    /// <summary>The requested file or part was not found.</summary>
    NotFound,

    /// <summary>The file could not be opened because access was denied.</summary>
    AccessDenied,

    /// <summary>An I/O operation failed.</summary>
    Io,

    /// <summary>The magic value is not <c>CCIM</c>.</summary>
    InvalidHeader,

    /// <summary>The header size is not 32.</summary>
    UnsupportedHeaderSize,

    /// <summary>The block size is not 2048.</summary>
    UnsupportedBlockSize,

    /// <summary>The format version is not 1.</summary>
    UnsupportedVersion,

    /// <summary>The index alignment is not 2.</summary>
    UnsupportedIndexAlignment,

    /// <summary>The uncompressed size is zero or not sector-aligned.</summary>
    InvalidSize,

    /// <summary>The index is truncated, out of bounds, or not monotonic.</summary>
    InvalidIndex,

    /// <summary>A sector blob is malformed or failed to decode.</summary>
    CorruptSector,

    /// <summary>The file ended before the expected data.</summary>
    Truncated,

    /// <summary>A requested offset or sector is outside the image.</summary>
    Range,

    /// <summary>LZ4 compression or decompression failed.</summary>
    Lz4,

    /// <summary>The output file already exists and overwrite was not requested.</summary>
    AlreadyExists,

    /// <summary>Splitting would produce more than nine parts.</summary>
    TooManyParts,

    /// <summary>The operation was cancelled.</summary>
    Cancelled,
}

/// <summary>Human-readable messages for <see cref="CciError"/>.</summary>
public static class CciErrorExtensions
{
    /// <summary>Returns a stable, human-readable message for an error code.</summary>
    public static string ToMessage(this CciError error) => error switch
    {
        CciError.None => "success",
        CciError.InvalidArgument => "invalid argument",
        CciError.NotFound => "file or part not found",
        CciError.AccessDenied => "access denied",
        CciError.Io => "I/O error",
        CciError.InvalidHeader => "invalid CCI header magic",
        CciError.UnsupportedHeaderSize => "unsupported CCI header size",
        CciError.UnsupportedBlockSize => "unsupported CCI block size",
        CciError.UnsupportedVersion => "unsupported CCI version",
        CciError.UnsupportedIndexAlignment => "unsupported CCI index alignment",
        CciError.InvalidSize => "invalid CCI uncompressed size",
        CciError.InvalidIndex => "invalid CCI index",
        CciError.CorruptSector => "corrupt CCI sector",
        CciError.Truncated => "truncated CCI file",
        CciError.Range => "offset or sector outside the image",
        CciError.Lz4 => "LZ4 compression/decompression failed",
        CciError.AlreadyExists => "output file already exists",
        CciError.TooManyParts => "split would produce more than nine parts",
        CciError.Cancelled => "operation cancelled",
        _ => "unknown CCI error",
    };
}

/// <summary>Exception thrown by the throwing convenience APIs.</summary>
public sealed class CciException : Exception
{
    /// <summary>Creates an exception without a specific error code.</summary>
    public CciException()
    {
    }

    /// <summary>Creates an exception with a message.</summary>
    public CciException(string message)
        : base(message)
    {
    }

    /// <summary>Creates an exception with a message and inner exception.</summary>
    public CciException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    /// <summary>Creates an exception for an error code.</summary>
    public CciException(CciError error, string? message = null)
        : base(message ?? error.ToMessage())
    {
        Error = error;
    }

    /// <summary>Creates an exception for an error code and inner exception.</summary>
    public CciException(CciError error, Exception innerException, string? message = null)
        : base(message ?? error.ToMessage(), innerException)
    {
        Error = error;
    }

    /// <summary>The CCI error code.</summary>
    public CciError Error { get; }
}
