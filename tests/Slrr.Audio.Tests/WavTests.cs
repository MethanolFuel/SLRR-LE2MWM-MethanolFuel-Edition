using static Slrr.Audio.Tests.Check;
using Slrr.Core;
using Xunit;

namespace Slrr.Audio.Tests;

public class WavTests
{
    private static byte[] Riff(params (string Id, byte[] Body)[] chunks)
    {
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);
        w.Write("RIFF"u8);
        w.Write(0);
        w.Write("WAVE"u8);
        foreach (var (id, body) in chunks)
        {
            w.Write(System.Text.Encoding.ASCII.GetBytes(id));
            w.Write(body.Length);
            w.Write(body);
            if ((body.Length & 1) != 0)
            {
                w.Write((byte)0);
            }
        }

        return ms.ToArray();
    }

    private static byte[] Fmt(int tag, int ch, int rate, int bits)
    {
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);
        w.Write((short)tag);
        w.Write((short)ch);
        w.Write(rate);
        w.Write(rate * ch * bits / 8);
        w.Write((short)(ch * bits / 8));
        w.Write((short)bits);
        return ms.ToArray();
    }

    [Fact]
    public void RoundTripsThroughSixteenBitPcm()
    {
        var clip = Synth.Sine(440, 0.25, 22050, 0.5f);
        var back = WavFile.Read(WavFile.Write(clip));
        Assert.Equal(22050, back.SampleRate);
        Assert.Equal(1, back.ChannelCount);
        Assert.Equal(clip.Frames, back.Frames);
        Near(0.25, back.Duration, 1e-3);
        Assert.InRange(back.Peak(), 0.499f, 0.501f);
        for (var i = 0; i < clip.Frames; i += 97)
        {
            Near(clip.Channels[0][i], back.Channels[0][i], 1e-3);
        }
    }

    [Fact]
    public void Decodes8Bit24BitAndFloat()
    {
        var u8 = WavFile.Read(Riff(("fmt ", Fmt(1, 1, 11025, 8)), ("data", [128, 255, 0])));
        Assert.Equal([0f, 127f / 128f, -1f], u8.Channels[0]);

        var s24 = WavFile.Read(Riff(("fmt ", Fmt(1, 1, 8000, 24)), ("data", [0, 0, 0x40, 0xFF, 0xFF, 0xFF, 0, 0, 0x80])));
        Near(0.5f, s24.Channels[0][0], 1e-4);
        Near(-1f / 8388608f, s24.Channels[0][1], 1e-7);
        Near(-1f, s24.Channels[0][2], 1e-4);

        var f32 = WavFile.Read(Riff(("fmt ", Fmt(3, 2, 44100, 32)), ("data", [.. BitConverter.GetBytes(0.25f), .. BitConverter.GetBytes(-0.5f)])));
        Assert.Equal(2, f32.ChannelCount);
        Assert.Equal(0.25f, f32.Channels[0][0]);
        Assert.Equal(-0.5f, f32.Channels[1][0]);
    }

    [Fact]
    public void ParsesSmplLoopCueAndSkipsPaddedChunks()
    {
        var smpl = new byte[36 + 24];
        BitConverter.GetBytes(1).CopyTo(smpl, 28);
        BitConverter.GetBytes(10).CopyTo(smpl, 36 + 8);
        BitConverter.GetBytes(90).CopyTo(smpl, 36 + 12);
        var cue = new byte[4 + 24];
        BitConverter.GetBytes(1).CopyTo(cue, 0);
        BitConverter.GetBytes(7).CopyTo(cue, 4);
        BitConverter.GetBytes(33).CopyTo(cue, 4 + 20);
        var data = new byte[200];
        var clip = WavFile.Read(Riff(("fmt ", Fmt(1, 1, 22050, 16)), ("PAD ", [1, 2, 3]), ("data", data), ("smpl", smpl), ("cue ", cue), ("LIST", [1])));
        Assert.Equal(100, clip.Frames);
        Assert.Equal(new WavLoop(10, 90, 0), Assert.Single(clip.Loops));
        Assert.Equal(new WavCue(7, 33), Assert.Single(clip.Cues));
        Assert.Contains("PAD ", clip.ChunkIds);
    }

    [Fact]
    public void ToleratesTruncatedDataChunk()
    {
        var bytes = Riff(("fmt ", Fmt(1, 1, 22050, 16)), ("data", new byte[100]));
        BitConverter.GetBytes(10_000).CopyTo(bytes, bytes.Length - 100 - 4); // data size claims far more than present
        Assert.Equal(50, WavFile.Read(bytes).Frames);
    }

    [Fact]
    public void RejectsGarbage()
    {
        Assert.Throws<InvalidDataException>(() => WavFile.Read("not a wave file at all"u8.ToArray()));
        Assert.Throws<InvalidDataException>(() => WavFile.Read(Riff(("fmt ", Fmt(1, 1, 22050, 16)))));
    }

    [Fact]
    public void EveryShippedWavDecodes()
    {
        var root = GameRoot.Find();
        if (root is null)
        {
            return;
        }

        var wavs = new GameFileSystem(root).Enumerate(".wav").ToList();
        Assert.True(wavs.Count >= 156, $"found {wavs.Count} wav files");
        var rates = new HashSet<int>();
        foreach (var w in wavs)
        {
            var c = WavFile.Load(w);
            Assert.Equal(1, c.FormatTag); // MEASURED: all shipped files are integer PCM
            Assert.InRange(c.BitsPerSample, 8, 16);
            Assert.InRange(c.ChannelCount, 1, 2);
            Assert.True(c.Frames > 0, w);
            Assert.True(c.Peak() > 0f, w + " is silent");
            rates.Add(c.SampleRate);
        }

        // MEASURED sample rates of the install.
        Assert.Equal([11025, 22050, 32000, 44100], rates.Order());
    }

    [Fact]
    public void ShippedLoopChunksAreParsed()
    {
        var root = GameRoot.Find();
        if (root is null)
        {
            return;
        }

        var all = new GameFileSystem(root).Enumerate(".wav").Select(f => (f, c: WavFile.Load(f))).ToList();
        // MEASURED: 7 files carry an smpl chunk, but only surface\Slip.wav declares a loop (5799..9683); the others have 0 loops.
        Assert.Equal(7, all.Count(x => x.c.ChunkIds.Contains("smpl")));
        var loops = all.Where(x => x.c.Loops.Count > 0).ToList();
        var one = Assert.Single(loops);
        Assert.EndsWith("Slip.wav", one.f);
        Assert.Equal(new WavLoop(5799, 9683, 0), one.c.Loops[0]);
    }
}
