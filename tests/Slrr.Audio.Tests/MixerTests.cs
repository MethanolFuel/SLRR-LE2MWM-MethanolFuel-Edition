using static Slrr.Audio.Tests.Check;
using System.Numerics;
using Xunit;

namespace Slrr.Audio.Tests;

public class MixerTests
{
    private const int Rate = 44100;

    private static WavClip Render(AudioMixer m, double seconds)
    {
        using var sink = new WavFileSink(m.SampleRate);
        AudioPump.Run(m, sink, seconds);
        return sink.ToClip();
    }

    private static double PeakHz(WavClip c, int from = 0) => Spectrum.PeakFrequency(c.Channels[0], from, 16384, c.SampleRate, 50, 5000);

    [Theory]
    [InlineData(1.0, 1000.0)]
    [InlineData(1.5, 1500.0)]
    [InlineData(0.5, 500.0)]
    [InlineData(2.0, 2000.0)]
    public void PitchScalesTheSpectrumPeak(double pitch, double expectedHz)
    {
        var m = new AudioMixer(Rate);
        m.Play(Synth.Sine(1000, 1.0, 22050), new PlayParams { Pitch = (float)pitch, Loop = true });
        var c = Render(m, 1.0);
        Assert.InRange(PeakHz(c, 2048), expectedHz * 0.995, expectedHz * 1.005);
    }

    [Fact]
    public void OneShotEndsAndFreesItsVoice()
    {
        var m = new AudioMixer(Rate);
        m.Play(Synth.Sine(440, 0.5, 22050));
        Assert.Equal(1, m.ActiveVoices);
        var c = Render(m, 1.0);
        Assert.Equal(0, m.ActiveVoices);
        // audible for ~0.5 s, silent afterwards
        Assert.True(c.Channels[0][(int)(0.25 * Rate)] != 0 || c.Channels[0][(int)(0.25 * Rate) + 1] != 0);
        Assert.All(c.Channels[0].Skip((int)(0.52 * Rate)), s => Assert.Equal(0f, s));
    }

    [Fact]
    public void HalfPitchDoublesTheDuration()
    {
        var m = new AudioMixer(Rate);
        m.Play(Synth.Sine(440, 0.5, 22050), new PlayParams { Pitch = 0.5f });
        var c = Render(m, 2.0);
        var last = Array.FindLastIndex(c.Channels[0], s => Math.Abs(s) > 1e-4f);
        Assert.InRange(last / (double)Rate, 0.97, 1.01);
    }

    [Fact]
    public void LoopingVoiceOutlivesItsClipWithoutClicks()
    {
        var m = new AudioMixer(Rate);
        // 441 Hz at 22050 Hz = exactly 50 samples per cycle; 2205 frames = 44.1 cycles is not whole, so use 2200 = 44 cycles.
        var clip = Synth.Sine(441, 2200.0 / 22050, 22050);
        m.Play(clip, new PlayParams { Loop = true });
        var c = Render(m, 1.0);
        Assert.Equal(1, m.ActiveVoices);
        var d = c.Channels[0];
        var maxStep = 0f;
        for (var i = 1; i < d.Length; i++)
        {
            maxStep = Math.Max(maxStep, Math.Abs(d[i] - d[i - 1]));
        }

        // A 441 Hz sine at amplitude 0.8 changes by at most 2*pi*441/44100*0.8 = 0.05 per sample; a seam click would exceed that.
        Assert.True(maxStep < 0.06f, $"max step {maxStep}");
    }

    [Fact]
    public void SmplLoopRegionIsHonoured()
    {
        // 100 frames of ramp 0..1; loop 20..40 inclusive => once past the end the output stays inside the ramp values of the region.
        var ramp = Enumerable.Range(0, 100).Select(i => i / 100f).ToArray();
        var clip = new WavClip(22050, [ramp]) { Loops = [new WavLoop(20, 39, 0)] };
        var m = new AudioMixer(22050);
        m.Play(clip, new PlayParams { Loop = true });
        var buf = new float[2 * 22050];
        m.Render(buf);
        var tail = buf.Where((_, i) => i % 2 == 0).Skip(2000).ToArray();
        Assert.InRange(tail.Min(), 0.19f, 0.2f);
        Assert.InRange(tail.Max(), 0.38f, 0.41f);
    }

    [Fact]
    public void GainScalesLinearlyAndChannelVolumeApplies()
    {
        var m = new AudioMixer(Rate);
        m.Play(Synth.Sine(440, 1.0, 22050, 0.8f), new PlayParams { Gain = 0.5f, Loop = true });
        var a = Render(m, 0.5).Peak();
        Assert.InRange(a, 0.39f, 0.41f);
        m.SetChannelVolume(SoundChannel.Effects, 0.5f);
        Render(m, 0.05); // the change is ramped over one block; let it settle
        var b = Render(m, 0.5).Peak();
        Assert.InRange(b, 0.19f, 0.21f);
    }

    [Fact]
    public void PositionalPanFollowsTheSide()
    {
        float Lr(Vector3 pos)
        {
            var m = new AudioMixer(Rate);
            m.Play(Synth.Sine(440, 1.0, 22050), new PlayParams { Loop = true, Position = pos, MinDistance = 100 });
            var c = Render(m, 0.3);
            return c.Channels[0].Max() / c.Channels[1].Max();
        }

        Assert.True(Lr(new Vector3(-5, 0, 0)) > 20f, "source on the left is louder on L");
        Assert.True(Lr(new Vector3(5, 0, 0)) < 0.05f, "source on the right is louder on R");
        Assert.InRange(Lr(new Vector3(0, 0, 5)), 0.97f, 1.03f); // ahead: centred
        Assert.InRange(Lr(Vector3.Zero), 0.97f, 1.03f); // coincident: centred
    }

    [Fact]
    public void PanGainsAreEqualPower()
    {
        var l = new Listener();
        foreach (var x in new[] { -10f, -3f, 0f, 2f, 9f })
        {
            var (gl, gr) = AudioMixer.PanGains(l, new Vector3(x, 0, 4));
            Near(1f, (gl * gl) + (gr * gr), 1e-4);
        }
    }

    [Fact]
    public void ListenerOrientationRotatesThePan()
    {
        var l = new Listener { Forward = Vector3.UnitX }; // facing +X: +Z is now on the left in a left-handed system
        var (gl, gr) = AudioMixer.PanGains(l, new Vector3(0, 0, 5));
        Assert.True(gl > gr);
    }

    [Fact]
    public void DistanceModelIsMonotoneAndClamped()
    {
        Assert.Equal(1f, AudioMixer.DistanceGain(0.5f, 2f, 0f, 1f));
        Assert.Equal(1f, AudioMixer.DistanceGain(2f, 2f, 0f, 1f));
        Near(0.5f, AudioMixer.DistanceGain(4f, 2f, 0f, 1f), 1e-5);
        Near(0.25f, AudioMixer.DistanceGain(8f, 2f, 0f, 1f), 1e-2);
        var prev = 2f;
        for (var d = 0f; d < 600f; d += 7f)
        {
            var g = AudioMixer.DistanceGain(d, 20f, 500f, 1f);
            Assert.True(g <= prev + 1e-6f && g >= 0f);
            prev = g;
        }

        Assert.Equal(0f, AudioMixer.DistanceGain(500f, 20f, 500f, 1f));
        Assert.Equal(0f, AudioMixer.DistanceGain(900f, 20f, 500f, 1f));
    }

    [Fact]
    public void DistanceAttenuatesTheRenderedLevel()
    {
        float Level(float dist)
        {
            var m = new AudioMixer(Rate);
            m.Play(Synth.Sine(440, 1.0, 22050), new PlayParams { Loop = true, Position = new Vector3(0, 0, dist), MinDistance = 2 });
            return Render(m, 0.3).Rms();
        }

        var near = Level(2);
        var far = Level(8);
        Assert.InRange(far / near, 0.24f, 0.26f); // inverse distance: 2/8
    }

    [Fact]
    public void FadeOutRampsDownThenFreesTheVoice()
    {
        var m = new AudioMixer(Rate);
        var v = m.Play(Synth.Sine(440, 1.0, 22050), new PlayParams { Loop = true })!;
        Render(m, 0.1);
        v.Stop(0.2f);
        var c = Render(m, 0.5);
        Assert.Equal(0, m.ActiveVoices);
        var d = c.Channels[0];
        float Env(int from) => d.Skip(from).Take(441).Max(Math.Abs);
        Assert.True(Env(0) > Env(4410) && Env(4410) > Env(8000), "monotone fade");
        Assert.Equal(0f, d[(int)(0.25 * Rate)..].Max(Math.Abs));
    }

    [Fact]
    public void FadeInStartsSilent()
    {
        var m = new AudioMixer(Rate);
        m.Play(Synth.Sine(440, 1.0, 22050), new PlayParams { Loop = true, FadeIn = 0.2f });
        var d = Render(m, 0.4).Channels[0];
        Assert.True(d.Take(50).Max(Math.Abs) < 0.02f);
        Assert.True(d.Skip(Rate / 4).Take(400).Max(Math.Abs) > 0.75f);
    }

    [Fact]
    public void InstanceGroupCapStealsTheOldestVoice()
    {
        var m = new AudioMixer(Rate);
        var ids = new List<int>();
        for (var i = 0; i < 6; i++)
        {
            ids.Add(m.Play(Synth.Sine(300 + i, 1.0, 22050), new PlayParams { Group = "x", MaxInstances = 4 })!.Id);
        }

        Assert.Equal(4, m.ActiveVoices);
        Assert.Null(m.Find(ids[0]));
        Assert.Null(m.Find(ids[1]));
        Assert.NotNull(m.Find(ids[5]));
    }

    [Fact]
    public void VoiceCapKeepsTheLoudest()
    {
        var m = new AudioMixer(Rate, maxVoices: 3);
        var clip = Synth.Sine(440, 1.0, 22050);
        m.Play(clip, new PlayParams { Gain = 0.9f });
        m.Play(clip, new PlayParams { Gain = 0.1f });
        m.Play(clip, new PlayParams { Gain = 0.8f });
        Assert.Null(m.Play(clip, new PlayParams { Gain = 0.05f })); // quieter than everything: rejected
        Assert.NotNull(m.Play(clip, new PlayParams { Gain = 0.7f })); // steals the 0.1 voice
        Assert.Equal(3, m.ActiveVoices);
    }

    [Fact]
    public void MonoAndStereoClipsAreHandled()
    {
        var left = Synth.Sine(440, 0.5, 22050).Channels[0];
        var right = new float[left.Length];
        var stereo = new WavClip(22050, [left, right]);
        var m = new AudioMixer(Rate);
        m.Play(stereo, new PlayParams { Loop = true });
        var c = Render(m, 0.2);
        Assert.True(c.Channels[0].Max() > 0.7f);
        Assert.Equal(0f, c.Channels[1].Max(Math.Abs));
    }

    [Fact]
    public void OutputNeverExceedsFullScale()
    {
        var m = new AudioMixer(Rate);
        for (var i = 0; i < 8; i++)
        {
            m.Play(Synth.Sine(200 + i, 1.0, 22050), new PlayParams { Loop = true });
        }

        Assert.True(Render(m, 0.3).Peak() <= 1f);
    }

    [Fact]
    public void RenderIsDeterministic()
    {
        WavClip Once()
        {
            var m = new AudioMixer(Rate);
            m.Play(Synth.Noise(1.0), new PlayParams { Loop = true, Pitch = 1.37f, Position = new Vector3(3, 0, 4) });
            return Render(m, 0.5);
        }

        Assert.Equal(Once().Channels[1], Once().Channels[1]);
    }
}
