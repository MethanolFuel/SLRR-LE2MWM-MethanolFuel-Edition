using System.Buffers.Binary;
using System.Text;

namespace Slrr.Core;

/// <summary>Thrown when binary game data does not match the expected layout.</summary>
public sealed class SlrrFormatException(string message) : Exception(message);

/// <summary>
/// Bounds-checked little-endian cursor over a byte buffer. Every read validates the
/// remaining length and throws <see cref="SlrrFormatException"/> instead of reading past the end,
/// so parsers can be run on untrusted or truncated files.
/// </summary>
public sealed class ByteReader
{
    private readonly ReadOnlyMemory<byte> _data;

    public ByteReader(ReadOnlyMemory<byte> data, int position = 0)
    {
        _data = data;
        Position = position;
    }

    public int Position { get; set; }
    public int Length => _data.Length;
    public int Remaining => _data.Length - Position;
    public ReadOnlyMemory<byte> Data => _data;

    private ReadOnlySpan<byte> Take(int count)
    {
        if (count < 0 || Position < 0 || count > Remaining)
        {
            throw new SlrrFormatException($"read of {count} bytes at offset {Position} exceeds buffer length {Length}");
        }

        var span = _data.Span.Slice(Position, count);
        Position += count;
        return span;
    }

    public byte U8() => Take(1)[0];
    public ushort U16() => BinaryPrimitives.ReadUInt16LittleEndian(Take(2));
    public short I16() => BinaryPrimitives.ReadInt16LittleEndian(Take(2));
    public uint U32() => BinaryPrimitives.ReadUInt32LittleEndian(Take(4));
    public int I32() => BinaryPrimitives.ReadInt32LittleEndian(Take(4));
    public float F32() => BinaryPrimitives.ReadSingleLittleEndian(Take(4));

    public byte[] Bytes(int count) => Take(count).ToArray();

    public ReadOnlyMemory<byte> Slice(int count)
    {
        var start = Position;
        Take(count);
        return _data.Slice(start, count);
    }

    /// <summary>Reads <paramref name="count"/> bytes and returns them as Latin-1 text (lossless for any byte).</summary>
    public string Latin1(int count) => Encoding.Latin1.GetString(Take(count));

    public void Expect(ReadOnlySpan<byte> magic, string what)
    {
        var got = Take(magic.Length);
        if (!got.SequenceEqual(magic))
        {
            throw new SlrrFormatException($"bad {what} magic at offset {Position - magic.Length}: expected '{Encoding.Latin1.GetString(magic)}', got '{Encoding.Latin1.GetString(got)}'");
        }
    }

    /// <summary>Validates a count read from the file against the bytes that remain, to avoid huge allocations.</summary>
    public int CheckedCount(uint count, int minElementSize, string what)
    {
        if (minElementSize > 0 && count > (uint)Math.Max(0, Remaining) / (uint)minElementSize)
        {
            throw new SlrrFormatException($"{what} count {count} at offset {Position} cannot fit in the remaining {Remaining} bytes");
        }

        return (int)count;
    }
}
