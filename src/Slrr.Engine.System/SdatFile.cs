using System.Buffers.Binary;
using System.Text;

namespace Slrr.Engine.SystemLib;

/// <summary>
/// The <c>java.io.File</c> container ("SDAT"). Layout (measured on <c>save/controls/active_control_set</c>, see
/// docs/engine-api/java.io.md): 8-byte header <c>"SDAT" 00 01 02 00</c>, then the values with no type tags:
/// int = 4 bytes LE, float = 4 bytes IEEE-754 LE, String = u32 length INCLUDING the terminating NUL, the characters, NUL.
/// Guessed (not observable in the sample): a null String is written as length 0 and read back as null; a resource
/// reference is a plain int id; reads past the end return 0 / 0.0 / null and set <see cref="SdatReader.Truncated"/>.
/// </summary>
public static class SdatFormat
{
    public static ReadOnlySpan<byte> Header => [(byte)'S', (byte)'D', (byte)'A', (byte)'T', 0x00, 0x01, 0x02, 0x00];

    public const int HeaderSize = 8;

    /// <summary>Strings are single-byte text (the game is a 2003 Windows ANSI program; Latin-1 is lossless for bytes).</summary>
    public static Encoding TextEncoding => Encoding.Latin1;

    public static bool HasHeader(ReadOnlySpan<byte> data) => data.Length >= HeaderSize && data[..HeaderSize].SequenceEqual(Header);
}

/// <summary>Sequential reader over a complete SDAT file image.</summary>
public sealed class SdatReader
{
    private readonly byte[] _data;
    private int _pos;

    public SdatReader(byte[] data)
    {
        _data = data;
        Valid = SdatFormat.HasHeader(data);
        _pos = Valid ? SdatFormat.HeaderSize : data.Length;
    }

    /// <summary>False if the 8-byte header is missing or wrong (the engine then fails <c>open(MODE_READ)</c>).</summary>
    public bool Valid { get; }

    /// <summary>A read ran past the end of the data.</summary>
    public bool Truncated { get; private set; }

    public int Position => _pos;

    public int Remaining => _data.Length - _pos;

    public int ReadInt()
    {
        if (Remaining < 4)
        {
            _pos = _data.Length;
            Truncated = true;
            return 0;
        }

        var v = BinaryPrimitives.ReadInt32LittleEndian(_data.AsSpan(_pos));
        _pos += 4;
        return v;
    }

    public float ReadFloat() => BitConverter.Int32BitsToSingle(ReadInt());

    public string? ReadString()
    {
        var len = ReadInt();
        if (Truncated || len <= 0)
        {
            return null;
        }

        if (len > Remaining)
        {
            Truncated = true;
            len = Remaining;
            var cut = SdatFormat.TextEncoding.GetString(_data, _pos, len);
            _pos = _data.Length;
            return cut.TrimEnd('\0');
        }

        var s = SdatFormat.TextEncoding.GetString(_data, _pos, len - 1);
        _pos += len;
        return s;
    }
}

/// <summary>Sequential writer producing an SDAT image on a stream.</summary>
public sealed class SdatWriter : IDisposable
{
    private readonly Stream _stream;
    private readonly bool _leaveOpen;

    public SdatWriter(Stream stream, bool leaveOpen = false)
    {
        _stream = stream;
        _leaveOpen = leaveOpen;
        _stream.Write(SdatFormat.Header);
    }

    public void WriteInt(int v)
    {
        Span<byte> b = stackalloc byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(b, v);
        _stream.Write(b);
    }

    public void WriteFloat(float v) => WriteInt(BitConverter.SingleToInt32Bits(v));

    public void WriteString(string? s)
    {
        if (s is null)
        {
            WriteInt(0);
            return;
        }

        var bytes = SdatFormat.TextEncoding.GetBytes(s);
        WriteInt(bytes.Length + 1);
        _stream.Write(bytes);
        _stream.WriteByte(0);
    }

    public void Flush() => _stream.Flush();

    public void Dispose()
    {
        _stream.Flush();
        if (!_leaveOpen)
        {
            _stream.Dispose();
        }
    }

    /// <summary>Builds a complete SDAT image in memory.</summary>
    public static byte[] Build(Action<SdatWriter> fill)
    {
        var ms = new MemoryStream();
        using (var w = new SdatWriter(ms, leaveOpen: true))
        {
            fill(w);
        }

        return ms.ToArray();
    }
}
