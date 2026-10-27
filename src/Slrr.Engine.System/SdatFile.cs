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

/// <summary>A runtime resource saved with an SDAT file (trailer instance table): kind 5 or 7, optional inline data.</summary>
public sealed record SdatInstanceRef(int Seq, int Kind, int DataLength);

/// <summary>
/// The resource-reference trailer the exe appends on <c>File.close()</c> of a written file and reads back on
/// <c>open(MODE_READ)</c> (MEASURED, StreetLegal_Redline.exe 0x485870 close / 0x485440 open / 0x485c60 write(ResourceRef) /
/// 0x485ea0 readResID, header byte 6 = 2): after the values,
/// <c>int instanceCount, instanceCount × (int seq, int kind, int len, byte[len]), int archiveCount,
/// archiveCount × (int seq, int nameLen incl. NUL, name), int trailerSize</c> where trailerSize counts the bytes before
/// it. <c>write(ResourceRef)</c> stores <c>(seq &lt;&lt; 16) | (id &amp; 0xFFFF)</c> with seq = the file's 1-based number of
/// the resource's archive; readResID maps seq back through the archive name (find-or-register by name, 0x538380).
/// Run-time resources (high word 0xFFFF; kinds 5/7 are saved as side files "name.seq" or inline data) are listed in the
/// instance table. Confirmed on the data: save/controls/active_control_set ends in the empty trailer (0, 0, 8) that was
/// documented as "12 unexplained trailing bytes"; the shipped ROC cars (cars/racers/ROC_cars) carry 7 archive names.
/// </summary>
public sealed class SdatTrailer
{
    public List<SdatInstanceRef> Instances { get; } = [];

    /// <summary>Archive names by their file-local sequence number (1-based).</summary>
    public Dictionary<int, string> Archives { get; } = [];

    /// <summary>Offset where the trailer starts (= end of the values).</summary>
    public int Start { get; init; }

    /// <summary>Parses the trailer of a complete image; null when the image has none (e.g. written by an older build of this engine).</summary>
    public static SdatTrailer? TryParse(byte[] data)
    {
        if (data.Length < SdatFormat.HeaderSize + 12 || data[6] != 2)
        {
            return null;
        }

        var size = BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(data.Length - 4));
        var start = data.Length - 4 - size;
        if (size < 8 || start < SdatFormat.HeaderSize)
        {
            return null;
        }

        var t = new SdatTrailer { Start = start };
        var pos = start;
        int Int()
        {
            if (pos + 4 > data.Length - 4)
            {
                throw new FormatException("trailer overrun");
            }

            var v = BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(pos));
            pos += 4;
            return v;
        }

        try
        {
            var n = Int();
            if (n < 0 || n > 100_000)
            {
                return null;
            }

            for (var i = 0; i < n; i++)
            {
                int seq = Int(), kind = Int(), len = Int();
                if (len < 0 || pos + len > data.Length - 4)
                {
                    return null;
                }

                pos += len;
                t.Instances.Add(new SdatInstanceRef(seq, kind, len));
            }

            var a = Int();
            if (a < 0 || a > 0xFFFF)
            {
                return null;
            }

            for (var i = 0; i < a; i++)
            {
                int seq = Int(), len = Int();
                if (len <= 0 || pos + len > data.Length - 4)
                {
                    return null;
                }

                t.Archives[seq] = SdatFormat.TextEncoding.GetString(data, pos, len).TrimEnd('\0');
                pos += len;
            }
        }
        catch (FormatException)
        {
            return null;
        }

        return pos == data.Length - 4 ? t : null;
    }

    /// <summary>Writes a trailer with no instances and the given archive names (what <see cref="SdatWriter.WriteTrailer"/> uses).</summary>
    public static byte[] Build(IReadOnlyList<(int Seq, string Name)> archives)
    {
        var ms = new MemoryStream();
        void Int(int v)
        {
            Span<byte> b = stackalloc byte[4];
            BinaryPrimitives.WriteInt32LittleEndian(b, v);
            ms.Write(b);
        }

        Int(0);
        Int(archives.Count);
        foreach (var (seq, name) in archives)
        {
            var bytes = SdatFormat.TextEncoding.GetBytes(name);
            Int(seq);
            Int(bytes.Length + 1);
            ms.Write(bytes);
            ms.WriteByte(0);
        }

        var size = (int)ms.Length;
        Int(size);
        return ms.ToArray();
    }
}

/// <summary>Sequential reader over a complete SDAT file image.</summary>
public sealed class SdatReader
{
    private readonly byte[] _data;
    private readonly int _end;
    private int _pos;

    public SdatReader(byte[] data)
    {
        _data = data;
        Valid = SdatFormat.HasHeader(data);
        _pos = Valid ? SdatFormat.HeaderSize : data.Length;
        Trailer = Valid ? SdatTrailer.TryParse(data) : null;
        _end = Trailer?.Start ?? data.Length;
    }

    /// <summary>The resource-reference trailer, when the file has one (see <see cref="SdatTrailer"/>).</summary>
    public SdatTrailer? Trailer { get; }

    /// <summary>False if the 8-byte header is missing or wrong (the engine then fails <c>open(MODE_READ)</c>).</summary>
    public bool Valid { get; }

    /// <summary>A read ran past the end of the data.</summary>
    public bool Truncated { get; private set; }

    public int Position => _pos;

    public int Remaining => _end - _pos;

    public int ReadInt()
    {
        if (Remaining < 4)
        {
            _pos = _end;
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
            _pos = _end;
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

    /// <summary>Appends the resource-reference trailer the exe writes on close (see <see cref="SdatTrailer"/>).</summary>
    public void WriteTrailer(IReadOnlyList<(int Seq, string Name)> archives) => _stream.Write(SdatTrailer.Build(archives));

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
