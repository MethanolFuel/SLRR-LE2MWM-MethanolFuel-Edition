using Slrr.Audio;
using Slrr.Decompiler;
using Slrr.Formats.Tufa;
using Xunit;

namespace Slrr.GameAudio.Tests;

/// <summary>The scripted 60 s drive through telemetry -> sink -> vehicle audio -> mixer -> WAV sink.</summary>
public class DriveTests
{
    private const int Rate = 44100;

    private static HeadlessDriveResult Render(VehicleAudioAssets assets, Action<ScenarioFrame, GameAudioSink>? onFrame = null, bool exhaust = false)
    {
        var (setup, clips) = Rig.SineEngine(exhaust);
        return HeadlessDrive.Render(setup, clips, assets, Rate, null, onFrame);
    }

    private static VehicleAudioAssets Silent() => new();

    [Fact]
    public void ScenarioIsADriveWithFiveUpshiftsABrakingStopAndAnImpact()
    {
        var f = DriveScenario.Build();
        Assert.Equal(3600, f.Length);
        Assert.True(f[0].Starter);
        Assert.False(f[200].Starter);
        var shifts = 0;
        for (var i = 1; i < f.Length; i++)
        {
            if (f[i].Gear > f[i - 1].Gear && f[i - 1].Gear >= 1)
            {
                shifts++;
            }
        }

        Assert.True(shifts >= 4, $"upshifts {shifts}");
        Assert.True(f.Max(x => x.Speed) > 40f);
        Assert.True(f.Max(x => x.EngineRpm) >= 6000f && f.Max(x => x.EngineRpm) <= 7200f);
        Assert.Contains(f, x => x.Braking && x.WheelSlip > 0.8f);
        Assert.Contains(f, x => x.ImpactSpeed > 10f);
        Assert.True(f[^1].Speed < 0.01f);
    }

    [Fact]
    public void RendersSixtySecondsWithoutClipping()
    {
        var r = Render(VehicleAudioAssets.Synthetic());
        Assert.InRange(r.Duration, 59.99, 60.01);
        Assert.Equal(2, r.Mix.ChannelCount);
        var peak = r.Mix.Peak();
        Assert.InRange(peak, 0.15f, 0.999f);
        Assert.InRange(r.Mix.Rms(), 0.01f, 0.5f);
    }

    [Fact]
    public void VoicePitchEqualsTheExeFormulaOnEveryFrame()
    {
        var (setup, _) = Rig.SineEngine();
        var checkedFrames = 0;
        var maxErr = 0f;
        Render(Silent(), (f, sink) =>
        {
            var audio = sink.VehicleOf("player")!;
            var rpm = MeasuredSfxTable.TableRpm(ManualTelemetry.RpmToRad(f.EngineRpm));
            if (f.Starter)
            {
                for (var i = 0; i < setup.Tables[0].Count; i++)
                {
                    Assert.Equal(0f, audio.EngineVoice(0, i)!.Gain);
                }

                return;
            }

            for (var i = 0; i < setup.Tables[0].Count; i++)
            {
                var row = setup.Tables[0][i];
                var v = audio.EngineVoice(0, i)!;
                if (v.Gain > 0f)
                {
                    maxErr = Math.Max(maxErr, Math.Abs((v.Pitch * row.RefRpm) - rpm));
                    checkedFrames++;
                }
            }
        });
        Assert.True(checkedFrames > 3000, $"frames with an active row: {checkedFrames}");
        Console.WriteLine($"pitch check: {checkedFrames} active-row frames, max |pitch*A - rpm'| = {maxErr:F4}");
        Assert.True(maxErr < 0.5f, $"max |pitch*A - rpm'| = {maxErr}");
    }

    [Theory]
    [InlineData(4.5, 0.5)] // idle (900 rpm)
    [InlineData(5.4, 0.12)] // top of the first rev blip
    [InlineData(33.0, 0.3)] // 5th gear cruise
    [InlineData(59.0, 0.5)] // idle after the crash
    public void SpectrumPeakTracksTheEngineRpm(double t, double window)
    {
        var r = Render(Silent());
        var mono = Rig.Mono(r.Mix);
        var start = (int)(t * Rate);
        var size = 1;
        while (size * 2 <= window * Rate)
        {
            size *= 2;
        }

        var peak = Spectrum.PeakFrequency(mono, start, size, Rate, 40, 2000);
        var lo = (int)(t * DriveScenario.Hz);
        var hi = Math.Min((int)((t + ((double)size / Rate)) * DriveScenario.Hz), r.Frames.Length - 1);
        var expected = r.Frames[lo..(hi + 1)].Average(f => MeasuredSfxTable.TableRpm(ManualTelemetry.RpmToRad(f.EngineRpm))) / 10.0;
        Console.WriteLine($"probe t={t}: peak {peak:F1} Hz, expected {expected:F1} Hz, ratio {peak / expected:F4}");
        Assert.InRange(peak / expected, 0.96, 1.04);
    }

    [Fact]
    public void ExhaustTableAddsEnergyAndStaysUnderTheFullScaleCeiling()
    {
        var plain = Render(Silent());
        var withExhaust = Render(Silent(), null, exhaust: true);
        Assert.True(withExhaust.Mix.Rms() > plain.Mix.Rms());
        Assert.True(withExhaust.Mix.Peak() <= 1f);
    }

    [Fact]
    public void SkidAppearsOnlyWhileTheWheelsSlip()
    {
        var with = Rig.Mono(Render(VehicleAudioAssets.Synthetic()).Mix);
        var without = Rig.Mono(Render(new VehicleAudioAssets { Impact = VehicleAudioAssets.Synthetic().Impact }).Mix);
        double Diff(double from, double to)
        {
            double e = 0;
            for (var i = (int)(from * Rate); i < (int)(to * Rate); i++)
            {
                var d = with[i] - without[i];
                e += d * d;
            }

            return Math.Sqrt(e / ((to - from) * Rate));
        }

        Assert.True(Diff(37, 41) > 0.02, $"braking skid {Diff(37, 41)}");
        Assert.True(Diff(2.5, 7.5) < 1e-4, $"idle {Diff(2.5, 7.5)}");
        Assert.True(Diff(8.2, 9) > 0.005, $"launch wheelspin {Diff(8.2, 9)}");
    }

    [Fact]
    public void ImpactIsAHardOnsetAtTheCrash()
    {
        var with = Rig.Mono(Render(VehicleAudioAssets.Synthetic()).Mix);
        var without = Rig.Mono(Render(new VehicleAudioAssets { Skid = VehicleAudioAssets.Synthetic().Skid }).Mix);
        double Diff(double from, double to)
        {
            double e = 0;
            for (var i = (int)(from * Rate); i < (int)(to * Rate); i++)
            {
                var d = with[i] - without[i];
                e += d * d;
            }

            return Math.Sqrt(e / ((to - from) * Rate));
        }

        Assert.True(Diff(56.3, 56.39) < 1e-4);
        Assert.True(Diff(56.4, 56.6) > 0.03, $"impact {Diff(56.4, 56.6)}");
        Assert.True(Diff(58.5, 59.5) < 1e-4);
    }

    [Fact]
    public void DeterministicRender()
    {
        var a = Render(VehicleAudioAssets.Synthetic()).Mix.Channels[0];
        var b = Render(VehicleAudioAssets.Synthetic()).Mix.Channels[0];
        Assert.Equal(a.Length, b.Length);
        Assert.True(a.AsSpan().SequenceEqual(b));
    }

    [Fact]
    public void WavFileIsWrittenAndReadable()
    {
        var path = Path.Combine(Path.GetTempPath(), $"gameaudio-{Guid.NewGuid():N}.wav");
        try
        {
            var (setup, clips) = Rig.SineEngine();
            var r = HeadlessDrive.Render(setup, clips, VehicleAudioAssets.Synthetic(), 22050, path);
            var back = WavFile.Load(path);
            Assert.Equal(22050, back.SampleRate);
            Assert.Equal(2, back.ChannelCount);
            Assert.InRange(back.Duration, 59.99, 60.01);
            Assert.InRange(back.Peak(), r.Mix.Peak() - 0.01f, r.Mix.Peak() + 0.01f);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void ShippedEngineRendersTheDriveWithRealSamples()
    {
        if (GameRoot.Find() is not { } root)
        {
            return;
        }

        var cls = Path.Combine(root, "parts", "engines", "Baiern_Emer", "scripts", "Baiern_Devils_6SFi_3_6_block.class");
        var setup = SfxTableScraper.Parse(TufaDecompiler.DecompileFile(TufaFile.Read(cls), out _));
        var cat = SoundCatalog.Scan(new Slrr.Core.GameFileSystem(root));
        var clips = new CatalogClipProvider(cat);
        var assets = VehicleAudioAssets.FromCatalog(cat, clips);
        Assert.NotNull(assets.Skid);
        Assert.NotNull(assets.Impact);
        Assert.Null(assets.WhineForward); // SIKIT.WAV is not shipped
        var r = HeadlessDrive.Render(setup, clips, assets, Rate);
        Assert.InRange(r.Duration, 59.99, 60.01);
        Assert.InRange(r.Mix.Peak(), 0.2f, 0.95f);
        var mono = Rig.Mono(r.Mix);

        // idle is quieter than the launch; the spectrum at idle sits in the engine's low range
        double Rms(double a, double b)
        {
            double e = 0;
            for (var i = (int)(a * Rate); i < (int)(b * Rate); i++)
            {
                e += mono[i] * mono[i];
            }

            return Math.Sqrt(e / ((b - a) * Rate));
        }

        Assert.True(Rms(3.6, 4.6) > 0.001, "idle is audible");
        Assert.True(Rms(10, 14) > 0.0, "launch");
        for (var sec = 0; sec < 60; sec += 4)
        {
            var pk = 0f;
            for (var i = sec * Rate; i < (sec + 4) * Rate; i++)
            {
                pk = Math.Max(pk, Math.Abs(mono[i]));
            }

            Console.WriteLine($"  {sec,2}-{sec + 4,2} s peak {pk:F3}");
        }

        Console.WriteLine($"shipped Baiern drive: peak {r.Mix.Peak():F3} rms {r.Mix.Rms():F3} idle {Rms(3.6, 4.6):F4} launch {Rms(10, 14):F4} brake {Rms(37, 41):F4}");
    }

    [Fact]
    public void CliDriveWritesTheWavForAShippedEngine()
    {
        if (GameRoot.Find() is not { } root)
        {
            return;
        }

        var path = Path.Combine(Path.GetTempPath(), $"gameaudio-cli-{Guid.NewGuid():N}.wav");
        try
        {
            var o = new StringWriter();
            var e = new StringWriter();
            var code = Slrr.Cli.CmdGameAudio.Run(["drive", path, "--engine", "Baiern_Emer", "--rate", "22050", root], o, e);
            Assert.True(code == 0, o + e.ToString());
            Assert.Contains("wrote", o.ToString(), StringComparison.Ordinal);
            Assert.InRange(WavFile.Load(path).Duration, 59.99, 60.01);
            var f = new StringWriter();
            Assert.Equal(0, Slrr.Cli.CmdGameAudio.Run(["formula", "--rpm", "3500", root], f, e));
            Assert.Contains("table 0", f.ToString(), StringComparison.Ordinal);
            Console.WriteLine(o.ToString() + f);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
