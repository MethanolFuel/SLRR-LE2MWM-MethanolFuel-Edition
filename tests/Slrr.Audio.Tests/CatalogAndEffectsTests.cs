using static Slrr.Audio.Tests.Check;
using System.Numerics;
using Slrr.Core;
using Xunit;

namespace Slrr.Audio.Tests;

public class CatalogAndEffectsTests
{
    private const int Rate = 44100;

    private static WavClip Render(AudioMixer m, double seconds, Action<double>? before = null)
    {
        using var sink = new WavFileSink(m.SampleRate);
        AudioPump.Run(m, sink, seconds, before);
        return sink.ToClip();
    }

    [Fact]
    public void SoundEntriesOfSoundRpkCarryTheMeasuredParameters()
    {
        var root = GameRoot.Find();
        if (root is null)
        {
            return;
        }

        var fs = new GameFileSystem(root);
        var cat = SoundCatalog.Scan(fs, [Path.Combine(root, "sound.rpk")]);
        Assert.Equal(33, cat.Defs.Count); // 43 entries, 10 are folders
        var slide = cat.FindByAlias("sound.rpk", "slide")!;
        Assert.Equal(6u, slide.Id);
        Assert.Equal(@"sound\wav\surface\SLIP3.WAV", slide.SourceFile);
        Assert.Equal(2f, slide.Volume);
        Assert.Equal(20f, slide.MinDistance);
        Assert.Equal(4, slide.Instances);
        var siren = cat.FindByAlias("sound.rpk", "police_siren")!;
        Assert.Equal(500f, siren.MaxDistance);
        Assert.Equal(20f, siren.MinDistance);
        Assert.True(cat.Defs.Where(d => d.Alias == "police").All(d => d.Loops));
        Assert.Equal(0.5f, cat.FindByAlias("sound.rpk", "engine_idle")!.Volume);
        Assert.NotNull(cat.Find("SOUND.RPK", 0x7)); // transmission_fwd
        Assert.False(fs.Exists(cat.Find("sound.rpk", 0x7)!.SourceFile), "SIKIT.WAV is referenced but not shipped");
    }

    [Fact]
    public void EngineSampleIdsMapToTheRpmNamedFiles()
    {
        var root = GameRoot.Find();
        if (root is null)
        {
            return;
        }

        var cat = SoundCatalog.Scan(new GameFileSystem(root));
        Assert.EndsWith(@"engine_IDL\2000RPM.wav", cat.Find("baiern_emer.rpk", 0xA2)!.SourceFile, StringComparison.OrdinalIgnoreCase);
        Assert.EndsWith(@"exhaust_WOT\1500RPM.wav", cat.Find("baiern_emer.rpk", 0xAC)!.SourceFile, StringComparison.OrdinalIgnoreCase);
        Assert.NotNull(new CatalogClipProvider(cat).GetClip(new SoundKey("baiern_emer.rpk", 0xAA)));
        Assert.Null(new CatalogClipProvider(cat).GetClip(new SoundKey("baiern_emer.rpk", 0x7777)));
    }

    [Fact]
    public void ParsesTheEntryTextFormat()
    {
        var d = SoundDef.Parse("x.rpk", 5, "a", "sourcefile sound\\wav\\a.wav\r\nvolume 0.333\r\ninstances 4.000\r\nmindist 3.000\r\nflags 1.000\r\n"u8)!;
        Assert.Equal(0.333f, d.Volume);
        Assert.Equal(4, d.Instances);
        Assert.Equal(3f, d.MinDistance);
        Assert.True(d.Loops);
        Assert.Null(SoundDef.Parse("x.rpk", 5, "a", "sourcefile textures\\a.tga\r\n"u8)); // not a sound
        Assert.Null(SoundDef.Parse("x.rpk", 5, "a", "volume 1\r\n"u8));
    }

    [Fact]
    public void SkidGainRisesWithSlipAndPitchWithSpeed()
    {
        var def = new SoundDef("sound.rpk", 6, "slide", "x.wav") { Volume = 2f, MinDistance = 20f, Instances = 4 };
        var m = new AudioMixer(Rate);
        using var skid = new SkidSound(m, Synth.Noise(1.0), def);
        double Rms(float slip)
        {
            skid.Update(slip, 20f);
            Render(m, 0.05);
            return Render(m, 0.2).Rms();
        }

        var gripping = Rms(0.05f);
        var half = Rms(0.5f);
        var full = Rms(1.0f);
        Assert.True(gripping < 1e-4, "no squeal while gripping");
        Assert.True(half > gripping && full > half);
    }

    [Fact]
    public void SkidPitchTracksSpeed()
    {
        var m = new AudioMixer(Rate);
        var tone = Synth.Sine(1000, 1.0, 22050);
        using var skid = new SkidSound(m, tone);
        double Hz(float speed)
        {
            skid.Update(1f, speed);
            Render(m, 0.05);
            return Spectrum.PeakFrequency(Render(m, 0.5).Channels[0], 4096, 16384, Rate, 200, 3000);
        }

        var slow = Hz(0);
        var fast = Hz(40);
        Assert.InRange(slow, 850 * 0.99, 850 * 1.01);
        Assert.InRange(fast, 1250 * 0.99, 1250 * 1.01);
    }

    [Fact]
    public void ImpactLevelGrowsWithClosingSpeedAndPositionPans()
    {
        var def = new SoundDef("sound.rpk", 1, "collision", "x.wav") { Instances = 4 };
        var hit = Synth.Noise(0.3);
        float Peak(float speed, Vector3? pos)
        {
            var m = new AudioMixer(Rate);
            ImpactSound.Play(m, hit, def, speed, pos);
            return Render(m, 0.4).Peak();
        }

        Assert.True(Peak(5, null) < Peak(10, null) && Peak(10, null) < Peak(20, null));
        Assert.Equal(Peak(20, null), Peak(40, null)); // saturates at 20 m/s
        var m2 = new AudioMixer(Rate);
        ImpactSound.Play(m2, hit, def, 20, new Vector3(-4, 0, 1));
        var c = Render(m2, 0.4);
        Assert.True(c.Channels[0].Max(Math.Abs) > 3 * c.Channels[1].Max(Math.Abs));
        Assert.Null(ImpactSound.Play(new AudioMixer(Rate), hit, def, 0f));
    }

    [Fact]
    public void ImpactsAreLimitedToTheEntrysInstanceCount()
    {
        var def = new SoundDef("sound.rpk", 1, "collision", "x.wav") { Instances = 4 };
        var m = new AudioMixer(Rate);
        for (var i = 0; i < 10; i++)
        {
            ImpactSound.Play(m, Synth.Noise(1.0), def, 20);
        }

        Assert.Equal(4, m.ActiveVoices);
    }

    [Fact]
    public void GearWhinePitchIsDriveRpmOverReference()
    {
        var m = new AudioMixer(Rate);
        using var whine = new GearWhine(m, Synth.Sine(700, 1.0, 22050), 1400f);
        double Hz(float rpm)
        {
            whine.Update(rpm, 1f);
            Render(m, 0.05);
            return Spectrum.PeakFrequency(Render(m, 0.5).Channels[0], 4096, 16384, Rate, 100, 4000);
        }

        Assert.InRange(Hz(1400), 700 * 0.99, 700 * 1.01);
        Assert.InRange(Hz(2800), 1400 * 0.99, 1400 * 1.01);
        Assert.InRange(Hz(-2100), 1050 * 0.99, 1050 * 1.01); // reverse direction uses |rpm|
    }

    [Fact]
    public void WavFileSinkWritesAReadableFile()
    {
        var path = Path.Combine(Path.GetTempPath(), $"slrr-audio-{Guid.NewGuid():N}.wav");
        try
        {
            var m = new AudioMixer(Rate);
            m.Play(Synth.Sine(440, 1.0, 22050), new PlayParams { Loop = true });
            using (var sink = new WavFileSink(Rate, path))
            {
                AudioPump.Run(m, sink, 1.25);
            }

            var back = WavFile.Load(path);
            Assert.Equal(2, back.ChannelCount);
            Assert.Equal(Rate, back.SampleRate);
            Assert.Equal((int)(1.25 * Rate), back.Frames);
            Assert.InRange(back.Peak(), 0.7f, 0.81f);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void UiBackendMapsVolumesPlayAndStop()
    {
        var m = new AudioMixer(Rate);
        var key = new SoundKey("t.rpk", 1);
        var clips = new MapClipProvider().Add(key, Synth.Sine(440, 0.2, 22050), new SoundDef("t.rpk", 1, "t", "x.wav") { Volume = 0.5f, Instances = 2 });
        IUiSoundBackend ui = new MixerUiSoundBackend(m, clips);
        Assert.Equal(1f, ui.GetVolume(0));
        ui.SetVolume(1, 0.25f);
        ui.SetVolume(7, 0.9f); // unknown channel ignored
        Assert.Equal(0.25f, ui.GetVolume(1));
        Assert.Equal(0f, ui.GetVolume(7));

        Assert.Equal(0, ui.Play(new SoundKey("t.rpk", 2), null, 0, 1, 1, SfxFlags.None, 0)); // unknown sound: id 0
        var one = ui.Play(key, null, 0, 1f, 1f, SfxFlags.None, 0);
        Assert.NotEqual(0, one);
        Render(m, 0.5);
        Assert.Equal(0, m.ActiveVoices); // one-shot ended on its own

        var loop = ui.Play(key, new Vector3(1, 0, 1), 0, 1f, 1f, SfxFlags.Loop | SfxFlags.NoAutoStop, 42);
        Render(m, 0.5);
        Assert.Equal(1, m.ActiveVoices);
        Assert.Equal(42, m.Find(loop)!.OwnerId);
        ui.Stop(loop);
        Render(m, 0.1);
        Assert.Equal(0, m.ActiveVoices);

        // the entry's volume (0.5) multiplies the script volume
        var m2 = new AudioMixer(Rate);
        new MixerUiSoundBackend(m2, clips).Play(key, null, 0, 1f, 1f, SfxFlags.Loop, 0);
        Assert.InRange(Render(m2, 0.1).Peak(), 0.39f, 0.41f);
        Assert.Equal(unchecked((int)0x80000000), SfxFlags.NoAutoStop);
    }

    [Fact]
    public void DemoSceneRendersImpactsAndSkidWithMeasurableTimeline()
    {
        var root = GameRoot.Find();
        if (root is null)
        {
            return;
        }

        var cat = SoundCatalog.Scan(new GameFileSystem(root));
        var clips = new CatalogClipProvider(cat);
        var collision = cat.FindByAlias("sound.rpk", "collision")!;
        var slide = cat.FindByAlias("sound.rpk", "slide")!;
        var hit = clips.GetClip(new SoundKey(collision.Archive, collision.Id))!;
        var slip = clips.GetClip(new SoundKey(slide.Archive, slide.Id))!;
        WavClip Scene(bool withImpact)
        {
            var m = new AudioMixer(Rate);
            using var skid = new SkidSound(m, slip, slide, new Vector3(0, 0, 8));
            var fired = false;
            return Render(m, 4.0, t =>
            {
                skid.Update(t is > 1.0 and < 3.0 ? 0.9f : 0f, 25f);
                if (withImpact && !fired && t >= 2.0)
                {
                    fired = true;
                    ImpactSound.Play(m, hit, collision, 20f, new Vector3(0, 0, 3));
                }
            });
        }

        var c = Scene(false);
        var d = Scene(true);
        float Rms(WavClip x, double a, double b) => (float)Math.Sqrt(x.Channels[0].Skip((int)(a * Rate)).Take((int)((b - a) * Rate)).Average(v => v * v));
        Assert.True(Rms(c, 0.1, 0.9) < 1e-4f, "silent before the skid");
        Assert.True(Rms(c, 1.2, 1.9) > 0.01f, "skid audible");
        Assert.True(Rms(c, 3.6, 4.0) < 1e-3f, "skid ended");
        // the impact leaves everything before it untouched and adds energy right after it
        Assert.Equal(c.Channels[0][..(2 * Rate)], d.Channels[0][..(2 * Rate)]);
        var diff = Enumerable.Range(2 * Rate, Rate / 2).Select(i => d.Channels[0][i] - c.Channels[0][i]).ToArray();
        Assert.True(Math.Sqrt(diff.Average(v => v * v)) > 0.01, "impact audible on top of the skid");
    }
}
