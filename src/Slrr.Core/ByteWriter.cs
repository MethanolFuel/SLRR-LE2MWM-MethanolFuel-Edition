using System.Buffers.Binary;

namespace Slrr.Core;

/// <summary>Little-endian growable writer, the counterpart of <see cref="ByteReader"/>.</summary>
public sealed class ByteWriter
{
    private readonly MemoryStream _stream = new();

    public int Position => (int)_stream.Position;

    public void U8(byte v) => _stream.WriteByte(v);

    public void U16(ushort v)
    {
        Span<byte> b = stackalloc byte[2];
        BinaryPrimitives.WriteUInt16LittleEndian(b, v);
        _stream.Write(b);
    }

    public void U32(uint v)
    {
        Span<byte> b = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(b, v);
        _stream.Write(b);
    }

    public void I32(int v) => U32(unchecked((uint)v));

    public void F32(float v)
    {
        Span<byte> b = stackalloc byte[4];
        BinaryPrimitives.WriteSingleLittleEndian(b, v);
        _stream.Write(b);
    }

    public void Bytes(ReadOnlySpan<byte> bytes) => _stream.Write(bytes);

    public byte[] ToArray() => _stream.ToArray();
}
