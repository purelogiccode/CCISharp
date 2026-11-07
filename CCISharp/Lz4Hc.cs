using System.Buffers.Binary;
using System.Numerics;

namespace CCISharp;

/// <summary>
/// Pure-managed LZ4 high-compression (HC) block encoder. The output is a
/// standard LZ4 block, interoperable with any conforming decoder; it is not
/// intended to be byte-identical to the reference <c>LZ4_compress_HC</c>
/// implementation.
/// </summary>
/// <remarks>
/// CCI's reference encoder uses LZ4-HC level 12. This encoder uses a bounded
/// hash-chain match finder with one-step lazy matching, which trades CPU time
/// for a smaller block than the fast encoder. Instances are reusable and keep
/// their hash tables between blocks, so a worker can compress a whole image
/// without allocating per sector.
/// </remarks>
internal sealed class Lz4HcEncoder
{
    private const int MinMatch = 4;
    private const int MfLimit = 12;
    private const int LastLiterals = 5;
    private const int MaxDistance = 0xFFFF;
    private const int HashBits = 14;
    private const int HashSize = 1 << HashBits;
    private const uint HashPrime = 2654435761u;

    private readonly int[] _head = new int[HashSize];
    private readonly int[] _stamp = new int[HashSize];
    private int[] _chain = new int[512];
    private int _generation = 1;

    /// <summary>Compresses one block at HC level 10, 11 or 12.</summary>
    public int Compress(ReadOnlySpan<byte> input, Span<byte> destination, int level)
    {
        if (level is < 10 or > 12)
        {
            throw new ArgumentOutOfRangeException(nameof(level), level, "LZ4-HC level must be 10..12.");
        }

        int required = Lz4.MaxCompressedOutputSize(input.Length);
        if (destination.Length < required)
        {
            throw new ArgumentException(
                $"Destination is too small ({destination.Length} bytes); at least {required} required",
                nameof(destination));
        }

        int outPos = 0;
        if (input.Length < MfLimit + 1)
        {
            Lz4.WriteLastLiterals(input, destination, ref outPos, 0);
            return outPos;
        }

        BeginBlock(input.Length);
        int attempts = level switch
        {
            12 => 128,
            11 => 64,
            _ => 32,
        };

        int literalStart = 0;
        int mfLimit = input.Length - MfLimit;
        int pos = 0;

        while (pos <= mfLimit)
        {
            int bestPos = FindBest(input, pos, attempts, out int bestLength);
            Insert(input, pos);

            if (bestLength < MinMatch)
            {
                pos++;
                continue;
            }

            // One-step lazy matching: if the next position has a strictly
            // longer match, emit the current byte as a literal instead.
            if (pos + 1 <= mfLimit)
            {
                _ = FindBest(input, pos + 1, attempts, out int nextLength);
                if (nextLength > bestLength)
                {
                    pos++;
                    continue;
                }
            }

            int literalLength = pos - literalStart;
            int matchLength = bestLength - MinMatch;
            int offset = pos - bestPos;

            byte token = (byte)((Math.Min(literalLength, 0xF) << 4) | Math.Min(matchLength, 0xF));
            destination[outPos++] = token;
            if (literalLength >= 0xF)
            {
                Lz4.WriteInteger(destination, ref outPos, literalLength - 0xF);
            }

            input.Slice(literalStart, literalLength).CopyTo(destination[outPos..]);
            outPos += literalLength;

            destination[outPos++] = (byte)offset;
            destination[outPos++] = (byte)(offset >> 8);

            if (matchLength >= 0xF)
            {
                Lz4.WriteInteger(destination, ref outPos, matchLength - 0xF);
            }

            int matchEnd = pos + bestLength;
            // Register the skipped positions so later blocks can find matches
            // that start inside this one. pos itself was registered above.
            for (int skipped = pos + 1; skipped < matchEnd; skipped++)
            {
                Insert(input, skipped);
            }

            pos = matchEnd;
            literalStart = pos;
        }

        Lz4.WriteLastLiterals(input, destination, ref outPos, literalStart);
        return outPos;
    }

    private void BeginBlock(int length)
    {
        if (_chain.Length < length)
        {
            _chain = new int[length];
        }

        _generation++;
        if (_generation == int.MaxValue)
        {
            // Wraparound is practically impossible, but reset deterministically.
            Array.Clear(_stamp);
            _generation = 1;
        }
    }

    private void Insert(ReadOnlySpan<byte> input, int pos)
    {
        if (pos + MinMatch > input.Length)
        {
            return;
        }

        int hash = Hash(input, pos);
        int previous = _stamp[hash] == _generation ? _head[hash] : -1;
        _chain[pos] = previous;
        _head[hash] = pos;
        _stamp[hash] = _generation;
    }

    private int FindBest(ReadOnlySpan<byte> input, int pos, int attempts, out int bestLength)
    {
        bestLength = 0;
        int bestPos = -1;
        int hash = Hash(input, pos);
        if (_stamp[hash] != _generation)
        {
            return -1;
        }

        int candidate = _head[hash];
        int maxLength = input.Length - LastLiterals - pos;

        while (candidate >= 0 && attempts-- > 0)
        {
            int distance = pos - candidate;
            if (distance > MaxDistance)
            {
                break;
            }

            if (bestLength == 0 || input[candidate + bestLength] == input[pos + bestLength])
            {
                int length = CountMatch(input, candidate, pos, maxLength);
                if (length > bestLength)
                {
                    bestLength = length;
                    bestPos = candidate;
                    if (length == maxLength)
                    {
                        break;
                    }
                }
            }

            candidate = _chain[candidate];
        }

        return bestPos;
    }

    private static int Hash(ReadOnlySpan<byte> input, int pos)
    {
        uint value = BinaryPrimitives.ReadUInt32LittleEndian(input.Slice(pos, MinMatch));
        return (int)((value * HashPrime) >> (32 - HashBits));
    }

    private static int CountMatch(ReadOnlySpan<byte> input, int candidate, int pos, int maxLength)
    {
        int length = 0;

        while (length + 8 <= maxLength)
        {
            ulong diff = BinaryPrimitives.ReadUInt64LittleEndian(input.Slice(candidate + length, 8)) ^
                         BinaryPrimitives.ReadUInt64LittleEndian(input.Slice(pos + length, 8));
            if (diff == 0)
            {
                length += 8;
            }
            else
            {
                return length + (BitOperations.TrailingZeroCount(diff) / 8);
            }
        }

        if (length + 4 <= maxLength)
        {
            uint diff = BinaryPrimitives.ReadUInt32LittleEndian(input.Slice(candidate + length, 4)) ^
                        BinaryPrimitives.ReadUInt32LittleEndian(input.Slice(pos + length, 4));
            if (diff == 0)
            {
                length += 4;
            }
            else
            {
                return length + (BitOperations.TrailingZeroCount(diff) / 8);
            }
        }

        while (length < maxLength && input[candidate + length] == input[pos + length])
        {
            length++;
        }

        return length;
    }
}

/// <summary>
/// Public entry point for the managed LZ4 high-compression (HC) block encoder.
/// Use <see cref="Lz4"/> for the fast encoder and decompression.
/// </summary>
public static class Lz4Hc
{
    /// <summary>
    /// Compresses <paramref name="input"/> as a raw LZ4 block using the managed
    /// high-compression encoder. Levels 10, 11 and 12 trade CPU time for ratio;
    /// level 12 matches the reference CCI encoder's <c>LZ4HC_CLEVEL_MAX</c>
    /// preset (it is not byte-identical to liblz4).
    /// </summary>
    /// <param name="input">Bytes to compress.</param>
    /// <param name="destination">Destination buffer of at least <see cref="Lz4.MaxCompressedOutputSize(int)"/> bytes.</param>
    /// <param name="level">HC level 10, 11 or 12.</param>
    /// <returns>The number of bytes written to <paramref name="destination"/>.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="level"/> is outside 10..12.</exception>
    /// <exception cref="ArgumentException"><paramref name="destination"/> is too small.</exception>
    public static int Compress(ReadOnlySpan<byte> input, Span<byte> destination, int level = 12) =>
        new Lz4HcEncoder().Compress(input, destination, level);
}
