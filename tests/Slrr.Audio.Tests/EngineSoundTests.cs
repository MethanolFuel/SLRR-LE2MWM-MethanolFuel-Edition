using static Slrr.Audio.Tests.Check;
using Slrr.Core;
using Slrr.Decompiler;
using Slrr.Formats.Tufa;
using Xunit;

namespace Slrr.Audio.Tests;

public class EngineSoundTests
{
    private const int Rate = 44100;

    private static SoundKey K(uint id) => new("test.rpk", id);

    /// <summary>Layers whose native pitch is refRpm/10 Hz, so any correctly pitched layer must show a spectrum peak at rpm/10 Hz.</summary>
    private static (EngineSfxSetup Setup, MapClipProvider Clips) SyntheticEngine()
    {
        var setup = new EngineSfxSetup();
        var clips = new MapClipProvider();
        foreach (var (id, refRpm, lo, hi) in new[] { (1u, 1000f, 500f, 2000f), (2u, 2500f, 1500f, 4000f), (3u, 5000f, 3000f, 9000f) })
        {
            clips.Add(K(id), Synth.Sine(refRpm / 10.0, 1.0, 22050));
            setup.Tables[0].Add(new EngineSfxItem(K(id), refRpm, lo, hi, 1f, 1f));
        }

        return (setup, clips);
    }

    private static WavClip RenderAt(EngineSoundModel model, AudioMixer mixer, float rpm, float load, double seconds)
    {
        using var sink = new WavFileSink(Rate);
        model.Update(rpm, load);
        AudioPump.Run(mixer, sink, seconds);
        return sink.ToClip();
    }

    [Theory]
    [InlineData(800)]
    [InlineData(1200)]
    [InlineData(1800)]
    [InlineData(2600)]
    [InlineData(3500)]
    [InlineData(4800)]
    [InlineData(6200)]
    [InlineData(7000)]
    public void SpectrumPeakTracksTheCommandedRpmAcrossCrossfades(int rpm)
    {
        var (setup, clips) = SyntheticEngine();
        var mixer = new AudioMixer(Rate);
        using var model = new EngineSoundModel(mixer, clips, setup);
        var c = RenderAt(model, mixer, rpm, 1f, 0.75);
        var hz = Spectrum.PeakFrequency(c.Channels[0], 8192, 16384, Rate, 30, 2000);
        Assert.InRange(hz, rpm / 10.0 * 0.985, rpm / 10.0 * 1.015);
    }

    [Fact]
    public void ContinuousSweepKeepsTrackingWithinFivePercent()
    {
        var (setup, clips) = SyntheticEngine();
        var mixer = new AudioMixer(Rate);
        using var model = new EngineSoundModel(mixer, clips, setup);
        const double Seconds = 30;
        using var sink = new WavFileSink(Rate);
        AudioPump.Run(mixer, sink, Seconds, t => model.Update((float)(800 + ((7000 - 800) * t / Seconds)), 1f));
        var d = sink.ToClip().Channels[0];
        Assert.Equal((int)(Seconds * Rate), d.Length);
        for (var t = 2.0; t < Seconds - 2; t += 3.0)
        {
            var mid = t + (16384.0 / 2 / Rate);
            var rpm = 800 + ((7000 - 800) * mid / Seconds);
            var hz = Spectrum.PeakFrequency(d, (int)(t * Rate), 16384, Rate, 30, 2000);
            Assert.InRange(hz, rpm / 10.0 * 0.95, rpm / 10.0 * 1.05);
        }
    }

    [Fact]
    public void EvaluateGivesZeroOutsideTheRangeAndInterpolatesVolume()
    {
        var it = new EngineSfxItem(K(1), 3000, 1000, 5000, 0.2f, 1.0f);
        Assert.Equal(0f, EngineSoundModel.Evaluate(it, 900).Gain);
        Assert.Equal(0f, EngineSoundModel.Evaluate(it, 5000).Gain);
        Assert.Equal(0f, EngineSoundModel.Evaluate(it, 7000).Gain);
        var (g3, r3) = EngineSoundModel.Evaluate(it, 3000);
        Near(0.6f, g3, 1e-4); // edge fade is 1 at the reference rpm, volume is the midpoint
        Near(1f, r3, 1e-5);
        Near(1.5f, EngineSoundModel.Evaluate(it, 4500).Rate, 1e-5);
        Assert.True(EngineSoundModel.Evaluate(it, 2000).Gain < g3);
    }

    [Fact]
    public void RateIsClampedToAFourFoldRange()
    {
        var it = new EngineSfxItem(K(1), 1000, 0, 100000, 1, 1);
        Assert.Equal(4f, EngineSoundModel.Evaluate(it, 50000).Rate);
        Assert.Equal(0.25f, EngineSoundModel.Evaluate(it, 10).Rate);
    }

    [Fact]
    public void NeighbouringLayersCrossfadeWithoutALevelHole()
    {
        // The shipped Baiern exhaust rows (table 2 of Baiern_Devils_6SFi_3_6_block): total gain stays above 0.25 from 1100 to 7000 rpm.
        EngineSfxItem[] rows =
        [
            new(K(1), 1500, 750, 2500, 1f, 1f),
            new(K(2), 3000, 1500, 6200, 1f, 1f),
            new(K(3), 6200, 4000, 18000, 1f, 0.7f),
        ];
        for (var rpm = 1100; rpm <= 7000; rpm += 50)
        {
            var sum = rows.Sum(r => EngineSoundModel.Evaluate(r, rpm).Gain);
            Assert.True(sum > 0.25f, $"hole at {rpm} rpm: {sum}");
        }
    }

    [Fact]
    public void ExhaustFollowsLoadBetweenTheMinimumVolumeAndFull()
    {
        var setup = new EngineSfxSetup { ExhaustMinVolume = 0.6f };
        var clips = new MapClipProvider().Add(K(1), Synth.Sine(250, 1.0, 22050));
        setup.Tables[2].Add(new EngineSfxItem(K(1), 2500, 1000, 5000, 1f, 1f));
        var mixer = new AudioMixer(Rate);
        using var model = new EngineSoundModel(mixer, clips, setup);
        RenderAt(model, mixer, 2500, 0f, 0.1);
        var lo = RenderAt(model, mixer, 2500, 0f, 0.3).Rms();
        RenderAt(model, mixer, 2500, 1f, 0.1);
        var hi = RenderAt(model, mixer, 2500, 1f, 0.3).Rms();
        Assert.InRange(lo / hi, 0.59f, 0.61f);
    }

    [Fact]
    public void MissingSamplesAreSkippedAndSilenceOutsideAllRanges()
    {
        var setup = new EngineSfxSetup();
        setup.Tables[0].Add(new EngineSfxItem(K(99), 1000, 0, 2000, 1, 1));
        var mixer = new AudioMixer(Rate);
        using var model = new EngineSoundModel(mixer, new MapClipProvider(), setup);
        Assert.Equal(0, model.VoiceCount);
        var (s, c) = SyntheticEngine();
        using var m2 = new EngineSoundModel(mixer, c, s);
        Near(0f, RenderAt(m2, mixer, 300, 1f, 0.3).Peak(), 1e-3);
    }

    [Fact]
    public void ScraperReadsDecompiledTablesAndEvaluatesExpressions()
    {
        const string Src = """
            SfxTable sfxTable1;
            if( sfxTable1 = the_car.getSfxTable(0) )
            {
                sfxTable1.clear();
                sfxTable1.addItem(new ResourceRef(parts.engines.Baiern_Emer:0x00A2r), 3500.0, 500.0, 6500.0, 0.42, 0.52);
                sfxTable1.addItem(new ResourceRef(parts.engines.Baiern_Emer:0x00AAr), 900.0 * 2, 3000.0, 18000.0, 0.62, 0.72000003);
            }
            if( sfxTable1 = the_car.getSfxTable(1) )
            {
                sfxTable1.clear();
            }
            if( sfxTable1 = the_car.getSfxTable(2) )
            {
                sfxTable1.clear();
                sfxTable1.addItem(new ResourceRef(parts.engines.Baiern_Emer:0x00ABr), 1500.0, 750.0, 2500.0, 1.0, 1.0);
                the_car.setSfxExhaustMinVol(0.6);
            }
            menu.addItem("Change filter", 1, "text");
            """;
        var s = SfxTableScraper.Parse(Src);
        Assert.Equal(2, s.Tables[0].Count);
        Assert.Empty(s.Tables[1]);
        Assert.Single(s.Tables[2]);
        Assert.Equal(new EngineSfxItem(new SoundKey("baiern_emer.rpk", 0xA2), 3500, 500, 6500, 0.42f, 0.52f), s.Tables[0][0]);
        Assert.Equal(1800f, s.Tables[0][1].RefRpm);
        Assert.Equal(0.72000003f, s.Tables[0][1].VolMax);
        Assert.Equal(0.6f, s.ExhaustMinVolume);
    }

    [Fact]
    public void ShippedEngineBlocksYieldTheMeasuredTableCounts()
    {
        var root = GameRoot.Find();
        if (root is null)
        {
            return;
        }

        var items = 0;
        var blocks = 0;
        foreach (var f in Directory.EnumerateFiles(Path.Combine(root, "parts", "engines"), "*.class", SearchOption.AllDirectories))
        {
            if (!File.ReadAllText(f, System.Text.Encoding.Latin1).Contains("getSfxTable", StringComparison.Ordinal))
            {
                continue;
            }

            var s = SfxTableScraper.Parse(TufaDecompiler.DecompileFile(TufaFile.Read(f), out _));
            var n = s.Tables.Sum(t => t.Count);
            if (n > 0)
            {
                blocks++;
                items += n;
                Assert.Empty(s.Tables[1]); // MEASURED: table 1 is only ever cleared
                Assert.True(s.Tables[0].Count > 0 && s.Tables[2].Count > 0);
                foreach (var it in s.Tables.SelectMany(t => t))
                {
                    Assert.True(it.RpmMin < it.RpmMax, f);
                    Assert.InRange(it.VolMin, 0f, 1f);
                    Assert.InRange(it.VolMax, 0f, 1f);
                }
            }
        }

        Assert.Equal(14, blocks);
        Assert.Equal(79, items);
    }

    [Fact]
    public void ShippedSampleKeepsItsSpectrumShapeWhenPitchedByTheModel()
    {
        var root = GameRoot.Find();
        if (root is null)
        {
            return;
        }

        var cat = SoundCatalog.Scan(new GameFileSystem(root));
        var key = new SoundKey("baiern_emer.rpk", 0xAF); // exhaust_WOT\3000RPM.wav, dominant 95 Hz (MEASURED with `audio inspect`)
        var def = cat.Find(key.Archive, key.Id);
        Assert.NotNull(def);
        var provider = new CatalogClipProvider(cat);
        var setup = new EngineSfxSetup();
        setup.Tables[2].Add(new EngineSfxItem(key, 3000, 500, 18000, 1f, 1f));
        var baseHz = 0.0;
        foreach (var (rpm, ratio) in new[] { (3000f, 1.0), (4500f, 1.5), (6000f, 2.0), (2250f, 0.75) })
        {
            var mixer = new AudioMixer(Rate);
            using var model = new EngineSoundModel(mixer, provider, setup);
            var c = RenderAt(model, mixer, rpm, 1f, 1.0);
            var hz = Spectrum.PeakFrequency(c.Channels[0], 8192, 16384, Rate, 30, 400);
            if (ratio == 1.0)
            {
                baseHz = hz;
                Assert.InRange(hz, 90, 101);
            }
            else
            {
                Assert.InRange(hz / baseHz, ratio * 0.97, ratio * 1.03);
            }
        }
    }

    [Fact]
    public void ShippedEngineRevSweepRendersTwelveCleanSeconds()
    {
        var root = GameRoot.Find();
        if (root is null)
        {
            return;
        }

        var cls = Path.Combine(root, "parts", "engines", "Baiern_Emer", "scripts", "Baiern_Devils_6SFi_3_6_block.class");
        var setup = SfxTableScraper.Parse(TufaDecompiler.DecompileFile(TufaFile.Read(cls), out _));
        var cat = SoundCatalog.Scan(new GameFileSystem(root));
        var mixer = new AudioMixer(Rate);
        using var model = new EngineSoundModel(mixer, new CatalogClipProvider(cat), setup, 0.5f);
        Assert.Equal(5, model.VoiceCount);
        using var sink = new WavFileSink(Rate);
        AudioPump.Run(mixer, sink, 12.0, t => model.Update((float)(800 + ((7000 - 800) * t / 12.0)), (float)(0.2 + (0.8 * t / 12.0))));
        var c = sink.ToClip();
        Near(12.0, c.Duration, 1e-3);
        Assert.InRange(c.Peak(), 0.2f, 0.999f); // loud but not clipped
        Assert.InRange(c.Rms(), 0.05f, 0.6f);
        Assert.All(c.Channels[0], s => Assert.False(float.IsNaN(s)));
        // louder at the top end of the sweep than at idle (exhaust opens with load)
        float Rms(double a, double b) => (float)Math.Sqrt(c.Channels[0].Skip((int)(a * Rate)).Take((int)((b - a) * Rate)).Average(x => x * x));
        Assert.True(Rms(10, 12) > Rms(0, 1));
    }
}
