using System.Text;

namespace Slrr.Audio;

/// <summary>A sustain loop from a RIFF <c>smpl</c> chunk (sample frames, end inclusive as stored).</summary>
public readonly record struct WavLoop(int StartFrame, int EndFrame, int PlayCount);

/// <summary>A cue point from a RIFF <c>cue </c> chunk.</summary>
public readonly record struct WavCue(int Id, int SampleOffset);

/// <summary>
/// Decoded PCM audio, de-interleaved to float [-1, 1]. Channel 0 is left (or the only channel).
/// </summary>
public sealed class WavClip
{
    public WavClip(int sampleRate, float[][] channels)
    {
        if (channels.Length == 0)
        {
            throw new ArgumentException("at least one channel", nameof(channels));
        }

        SampleRate = sampleRate;
        Channels = channels;
    }

    public int SampleRate { get; }

    public float[][] Channels { get; }

    public int ChannelCount => Channels.Length;

    public int Frames => Channels[0].Length;

    public double Duration => (double)Frames / SampleRate;

    /// <summary>Source bit depth and format tag (1 = PCM, 3 = float), for inspection.</summary>
    public int BitsPerSample { get; init; } = 16;

    public int FormatTag { get; init; } = 1;

    /// <summary>Chunk ids in file order, e.g. <c>fmt </c>, <c>data</c>, <c>smpl</c>, <c>LIST</c>.</summary>
    public IReadOnlyList<string> ChunkIds { get; init; } = [];

    public IReadOnlyList<WavLoop> Loops { get; init; } = [];

    public IReadOnlyList<WavCue> Cues { get; init; } = [];

    public float Peak()
    {
        var p = 0f;
        foreach (var ch in Channels)
        {
            foreach (var s in ch)
            {
                p = Math.Max(p, Math.Abs(s));
            }
        }

        return p;
    }

    public float Rms()
    {
        double sum = 0;
        long n = 0;
        foreach (var ch in Channels)
        {
            foreach (var s in ch)
            {
                sum += (double)s * s;
                n++;
            }
        }

        return n == 0 ? 0 : (float)Math.Sqrt(sum / n);
    }

    public WavClip ToMono()
    {
        if (ChannelCount == 1)
        {
            return this;
        }

        var m = new float[Frames];
        for (var i = 0; i < m.Length; i++)
        {
            float s = 0;
            foreach (var ch in Channels)
            {
                s += ch[i];
            }

            m[i] = s / ChannelCount;
        }

        return new WavClip(SampleRate, [m]) { BitsPerSample = BitsPerSample, FormatTag = FormatTag, ChunkIds = ChunkIds, Loops = Loops, Cues = Cues };
    }
}

/// <summary>Managed RIFF/WAVE reader and 16-bit PCM writer. Reads PCM 8/16/24/32, IEEE float 32/64 and WAVE_FORMAT_EXTENSIBLE.</summary>
public static class WavFile
{
    public static WavClip Load(string path) => Read(File.ReadAllBytes(path));

    public static WavClip Read(ReadOnlySpan<byte> b)
    {
        if (b.Length < 12 || !b[..4].SequenceEqual("RIFF"u8) || !b.Slice(8, 4).SequenceEqual("WAVE"u8))
        {
            throw new InvalidDataException("not a RIFF/WAVE file");
        }

        var ids = new List<string>();
        var loops = new List<WavLoop>();
        var cues = new List<WavCue>();
        int tag = 0, channels = 0, rate = 0, bits = 0, blockAlign = 0;
        var haveFmt = false;
        ReadOnlySpan<byte> data = default;
        var haveData = false;
        var pos = 12;
        while (pos + 8 <= b.Length)
        {
            var id = Encoding.Latin1.GetString(b.Slice(pos, 4));
            long size = BitConverter.ToUInt32(b.Slice(pos + 4, 4));
            var body = pos + 8;
            var avail = (int)Math.Min(size, b.Length - body); // tolerate a truncated final chunk
            var chunk = b.Slice(body, avail);
            ids.Add(id);
            switch (id)
            {
                case "fmt " when avail >= 16:
                    tag = BitConverter.ToUInt16(chunk[..2]);
                    channels = BitConverter.ToUInt16(chunk.Slice(2, 2));
                    rate = BitConverter.ToInt32(chunk.Slice(4, 4));
                    blockAlign = BitConverter.ToUInt16(chunk.Slice(12, 2));
                    bits = BitConverter.ToUInt16(chunk.Slice(14, 2));
                    if (tag == 0xFFFE && avail >= 26)
                    {
                        tag = BitConverter.ToUInt16(chunk.Slice(24, 2)); // sub-format GUID starts with the format tag
                    }

                    haveFmt = true;
                    break;
                case "data":
                    data = chunk;
                    haveData = true;
                    break;
                case "smpl" when avail >= 36:
                {
                    var n = BitConverter.ToInt32(chunk.Slice(28, 4));
                    for (var i = 0; i < n && 36 + ((i + 1) * 24) <= avail; i++)
                    {
                        var l = chunk[(36 + (i * 24))..];
                        loops.Add(new WavLoop(BitConverter.ToInt32(l.Slice(8, 4)), BitConverter.ToInt32(l.Slice(12, 4)), BitConverter.ToInt32(l.Slice(20, 4))));
                    }

                    break;
                }

                case "cue " when avail >= 4:
                {
                    var n = BitConverter.ToInt32(chunk[..4]);
                    for (var i = 0; i < n && 4 + ((i + 1) * 24) <= avail; i++)
                    {
                        var c = chunk[(4 + (i * 24))..];
                        cues.Add(new WavCue(BitConverter.ToInt32(c[..4]), BitConverter.ToInt32(c.Slice(20, 4))));
                    }

                    break;
                }
            }

            pos = body + (int)Math.Min(size + (size & 1), int.MaxValue - body);
        }

        if (!haveFmt || !haveData)
        {
            throw new InvalidDataException("missing fmt or data chunk");
        }

        if (channels < 1 || rate <= 0 || bits < 8 || blockAlign < 1)
        {
            throw new InvalidDataException("invalid fmt chunk");
        }

        var bytes = bits / 8;
        var frames = data.Length / (bytes * channels);
        var pcm = new float[channels][];
        for (var c = 0; c < channels; c++)
        {
            pcm[c] = new float[frames];
        }

        for (var f = 0; f < frames; f++)
        {
            for (var c = 0; c < channels; c++)
            {
                var s = data[(((f * channels) + c) * bytes)..];
                pcm[c][f] = (tag, bits) switch
                {
                    (1, 8) => (s[0] - 128) / 128f,
                    (1, 16) => BitConverter.ToInt16(s) / 32768f,
                    (1, 24) => (((s[0] | (s[1] << 8) | (s[2] << 16)) << 8) >> 8) / 8388608f,
                    (1, 32) => BitConverter.ToInt32(s) / 2147483648f,
                    (3, 32) => BitConverter.ToSingle(s),
                    (3, 64) => (float)BitConverter.ToDouble(s),
                    _ => throw new InvalidDataException($"unsupported format tag {tag} / {bits} bit"),
                };
            }
        }

        return new WavClip(rate, pcm) { BitsPerSample = bits, FormatTag = tag, ChunkIds = ids, Loops = loops, Cues = cues };
    }

    /// <summary>Serialises as 16-bit PCM (samples clipped to [-1, 1]).</summary>
    public static byte[] Write(WavClip clip)
    {
        var ch = clip.ChannelCount;
        var dataLen = clip.Frames * ch * 2;
        using var ms = new MemoryStream(44 + dataLen);
        using var w = new BinaryWriter(ms);
        w.Write("RIFF"u8);
        w.Write(36 + dataLen);
        w.Write("WAVEfmt "u8);
        w.Write(16);
        w.Write((short)1);
        w.Write((short)ch);
        w.Write(clip.SampleRate);
        w.Write(clip.SampleRate * ch * 2);
        w.Write((short)(ch * 2));
        w.Write((short)16);
        w.Write("data"u8);
        w.Write(dataLen);
        for (var f = 0; f < clip.Frames; f++)
        {
            for (var c = 0; c < ch; c++)
            {
                w.Write((short)Math.Clamp(MathF.Round(clip.Channels[c][f] * 32767f), -32768f, 32767f));
            }
        }

        return ms.ToArray();
    }

    public static void Save(string path, WavClip clip) => File.WriteAllBytes(path, Write(clip));
}
