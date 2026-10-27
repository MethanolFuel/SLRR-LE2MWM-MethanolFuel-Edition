using System.Text;
using Slrr.Render.Gl;
using Xunit;
using Xunit.Abstractions;

namespace Slrr.Race.Tests;

/// <summary>Game data root ($SLRR_GAME_ROOT or the repository checkout); corpus tests pass vacuously without it.</summary>
internal static class GameRoot
{
    private static readonly Lazy<string?> s_root = new(() =>
    {
        var env = Environment.GetEnvironmentVariable("SLRR_GAME_ROOT");
        if (!string.IsNullOrEmpty(env) && File.Exists(Path.Combine(env, "sl.rpk")))
        {
            return env;
        }

        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "Slrr.sln")) && File.Exists(Path.Combine(dir.FullName, "sl.rpk")))
            {
                return dir.FullName;
            }
        }

        return null;
    });

    public static string? Root => s_root.Value;
}

/// <summary>
/// The race-wave adapters composed in one host (<see cref="RaceHost"/>) and driven by the shipped scripts: new career, used
/// car, Garage "Choose Track", a map, TAKE IT, throttle through the input layer. Every assertion reads VM-visible state
/// (script fields, instance nodes, natives evaluated in the VM) or the physics telemetry.
/// </summary>
public sealed class CorpusTests(ITestOutputHelper output)
{
    private void Dump(RaceScenarioResult r)
    {
        foreach (var s in r.Steps)
        {
            output.WriteLine(s);
        }

        foreach (var e in r.ThreadErrors)
        {
            output.WriteLine("thread error: " + e);
        }
    }

    [Fact]
    public void TheBoughtUsedCarDrivesOnDragTrackThroughTheInputLayer()
    {
        if (GameRoot.Root is not { } root)
        {
            return;
        }

        using var race = RaceHost.Create(root);
        var r = new RaceScenario(race, new RaceScenarioOptions { Tracks = ["Drag Track"], DriveSeconds = 8, AccelerationRound = false }).Run();
        Dump(r);

        Assert.True(r.BoughtCar, "career + used car purchase");
        Assert.Equal("java.game.Vehicle", r.CarClass);
        Assert.Contains("Compression", r.EngineBefore ?? string.Empty, StringComparison.Ordinal);
        Assert.Null(r.DriveableAnswer); // Garage "Choose Track" passed Vehicle.isDriveable
        Assert.True(r.EnteredTrack);
        Assert.Equal("java.game.Drag_Track", r.TrackClass);
        Assert.True(r.CarBound);

        // the car left its start, is moving, sits on its wheels; the scripts see it (instance node, Vehicle.getSpeedSquare in the VM)
        Assert.True(r.Distance > 30f, $"moved {r.Distance} m");
        Assert.True(r.FinalSpeed > 5f, $"speed {r.FinalSpeed} m/s");
        Assert.True(r.GroundedFraction > 0.95f, $"grounded {r.GroundedFraction}");
        Assert.True(r.ScriptSpeedSquare > 25f, $"getSpeedSquare {r.ScriptSpeedSquare}");
        Assert.True(Vector(r.ScriptPosition - r.Start) > 30f, "script-visible position moved");
        Assert.True(r.MaxGear >= 2, "the automatic box shifted");
        Assert.Empty(r.ThreadErrors);
        Assert.Empty(r.Missing);
    }

    [Fact]
    public void TestTrackAccelerationRoundIsTimedByTheScriptsOnTheSimulatedCar()
    {
        if (GameRoot.Root is not { } root)
        {
            return;
        }

        using var race = RaceHost.Create(root);
        var r = new RaceScenario(race, new RaceScenarioOptions { Tracks = ["Test Track"], DriveSeconds = 2, AccelerationRound = true }).Run();
        Dump(r);

        Assert.Equal("java.game.TestTrack", r.TrackClass);
        Assert.NotNull(r.Acceleration);
        var (quarter, mile, zeroTo100, _, maxKmh) = r.Acceleration!.Value;

        // plausible for a ~1.2 t, ~130 N m used car; these are the scripts' own fields of player.car (not the game's numbers)
        Assert.InRange(quarter, 12f, 25f);
        Assert.InRange(mile, 30f, 70f);
        Assert.InRange(zeroTo100, 6f, 20f);
        Assert.InRange(maxKmh, 120f, 260f);
        Assert.True(quarter < mile);
        Assert.Contains(r.AccelTriggerEvents, e => e.Contains("On = True", StringComparison.Ordinal));
        Assert.Empty(r.ThreadErrors);

        // the ghost dealer cars were taken out of the race map's physics scene
        Assert.Single(race.Physics.Scene.AllCars);
    }

    [Fact]
    public void RenderAndAudioCaptureTheRunUnderSoftwareGl()
    {
        if (GameRoot.Root is not { } root)
        {
            return;
        }

        using (var probe = GlHost.TryCreate(out _))
        {
            if (probe is null)
            {
                return; // no OpenGL 3.3 context (run under xvfb-run with LIBGL_ALWAYS_SOFTWARE=1)
            }
        }

        var dir = Path.Combine(Path.GetTempPath(), "slrr-race-test-" + Guid.NewGuid().ToString("N")[..8]);
        var wav = Path.Combine(dir, "run.wav");
        try
        {
            RaceScenarioResult r;
            using (var race = RaceHost.Create(root, new RaceHostOptions
                   {
                       Render = true, Audio = true, WavPath = wav, CaptureDir = dir, CaptureEvery = 60, Width = 320, Height = 240,
                   }))
            {
                r = new RaceScenario(race, new RaceScenarioOptions { Tracks = ["Drag Track"], DriveSeconds = 4, AccelerationRound = false }).Run();
                Dump(r);
                Assert.True(r.Moved);
                var onTrack = race.Renderer!.Frames.Where(f => f.CapturePath is not null && f.Time >= r.Samples[0].Time).ToList();
                Assert.NotEmpty(onTrack);
                Assert.All(onTrack, f => Assert.True(f.Stats.DrawCalls > 20, $"{f.Stats}"));
                Assert.True(r.AudioEngineVoices > 0, "engine voices of the player car");
                Assert.True(r.AudioTableRpmMax > 3000f, $"table rpm {r.AudioTableRpmMax}");
            }

            // WAV written on dispose: non-silent while driving
            var bytes = File.ReadAllBytes(wav);
            Assert.Equal("RIFF", Encoding.ASCII.GetString(bytes, 0, 4));
            Assert.True(bytes.Length > 44100 * 4 * 10, $"wav {bytes.Length} bytes");
            var tail = bytes.AsSpan(bytes.Length - (44100 * 4 * 2));
            long energy = 0;
            for (var i = 0; i + 1 < tail.Length; i += 2)
            {
                var s = (short)(tail[i] | (tail[i + 1] << 8));
                energy += Math.Abs((int)s);
            }

            Assert.True(energy > 0, "the last 2 s (driving) are silent");
        }
        finally
        {
            try
            {
                Directory.Delete(dir, true);
            }
            catch (IOException)
            {
            }
        }
    }

    private static float Vector(System.Numerics.Vector3 v) => v.Length();
}
