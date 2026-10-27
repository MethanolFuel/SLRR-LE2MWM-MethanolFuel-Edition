using Slrr.Audio;

namespace Slrr.GameAudio.Tests;

internal static class GameRoot
{
    public static string? Find()
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
    }
}

internal static class Rig
{
    public const string Archive = "synth.rpk";

    /// <summary>
    /// Five crossfading rows whose sample is a sine at <c>A/10</c> Hz, so after the exe formula (pitch = rpm'/A) every row sounds at
    /// exactly <c>rpm'/10</c> Hz whatever the crossfade: the spectrum peak must follow the table rpm.
    /// </summary>
    public static (EngineSfxSetup Setup, MapClipProvider Clips) SineEngine(bool exhaust = false)
    {
        var rows = new (uint Id, float A, float B, float C, float D, float E)[]
        {
            (1, 1000, 400, 2400, 0.5f, 1.0f),
            (2, 2000, 1800, 3600, 0.6f, 1.0f),
            (3, 3500, 3000, 5200, 0.7f, 1.0f),
            (4, 5000, 4600, 7500, 0.8f, 1.0f),
            (5, 6500, 7000, 18000, 0.9f, 0.6f),
        };
        var setup = new EngineSfxSetup { ExhaustMinVolume = 0.6f };
        var clips = new MapClipProvider();
        foreach (var r in rows)
        {
            var key = new SoundKey(Archive, r.Id);
            clips.Add(key, Synth.Sine(r.A / 10.0, 1.0, 22050, 0.8f));
            setup.Tables[0].Add(new EngineSfxItem(key, r.A, r.B, r.C, r.D, r.E));
            if (exhaust)
            {
                var xk = new SoundKey(Archive, r.Id + 100);
                clips.Add(xk, Synth.Sine(r.A / 10.0, 1.0, 22050, 0.8f));
                setup.Tables[2].Add(new EngineSfxItem(xk, r.A, r.B, r.C, r.D, r.E));
            }
        }

        return (setup, clips);
    }

    public static float[] Mono(WavClip c) => c.ToMono().Channels[0];
}
