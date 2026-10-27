using System.Globalization;
using Slrr.Audio;
using Slrr.Core;
using Slrr.Decompiler;
using Slrr.Formats.Tufa;
using Slrr.GameAudio;

namespace Slrr.Cli;

/// <summary>
/// <c>gameaudio</c>: in-game audio adapter (src/Slrr.Game.Audio).
/// Needs a ProjectReference from Slrr.Cli.csproj to src/Slrr.Game.Audio and a
/// <c>"gameaudio" =&gt; CmdGameAudio.Run(args[1..], o, e)</c> line in Commands.cs (left to the lead).
/// </summary>
public static class CmdGameAudio
{
    public const string Usage = """
        gameaudio drive <out.wav> [--engine Baiern_Emer] [--rate 44100] [gameRoot]
                                                      render the scripted 60 s drive (starter, idle, launch, shifts, braking, skid, impact) through the sink
        gameaudio formula [--engine Baiern_Emer] [--rpm 3500] [--throttle 1] [gameRoot]
                                                      print the exe's table evaluation (pitch, gain per row) at an engine rpm
        """;

    public static int Run(string[] args, TextWriter o, TextWriter e)
    {
        try
        {
            if (args.Length == 0)
            {
                e.WriteLine(Usage);
                return 2;
            }

            var rest = args[1..];
            return args[0] switch
            {
                "drive" => Drive(rest, o, e),
                "formula" => Formula(rest, o, e),
                _ => Fail(e),
            };
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or InvalidOperationException or FormatException or SlrrFormatException)
        {
            e.WriteLine("error: " + ex.Message);
            return 1;
        }
    }

    private static int Fail(TextWriter e)
    {
        e.WriteLine(Usage);
        return 2;
    }

    private static string Opt(string[] a, string name, string def)
    {
        var i = Array.IndexOf(a, name);
        return i >= 0 && i + 1 < a.Length ? a[i + 1] : def;
    }

    private static string Root(string[] a)
    {
        for (var i = 0; i < a.Length; i++)
        {
            if (a[i].StartsWith("--", StringComparison.Ordinal))
            {
                i++;
                continue;
            }

            if (Directory.Exists(a[i]))
            {
                return a[i];
            }
        }

        return Directory.GetCurrentDirectory();
    }

    private static string F(double v, string fmt = "F2") => v.ToString(fmt, CultureInfo.InvariantCulture);

    private static EngineSfxSetup LoadSetup(string root, string engine)
    {
        var dir = Path.Combine(root, "parts", "engines", engine, "scripts");
        var cls = Directory.Exists(dir)
            ? Directory.EnumerateFiles(dir, "*block.class").OrderBy(x => x, StringComparer.Ordinal)
                .FirstOrDefault(f => File.ReadAllText(f, System.Text.Encoding.Latin1).Contains("getSfxTable", StringComparison.Ordinal))
            : null;
        return cls is null
            ? throw new InvalidOperationException($"no engine block with sound tables under parts/engines/{engine}/scripts")
            : SfxTableScraper.Parse(TufaDecompiler.DecompileFile(TufaFile.Read(cls), out _));
    }

    private static int Drive(string[] a, TextWriter o, TextWriter e)
    {
        if (a.Length == 0)
        {
            return Fail(e);
        }

        var root = Root(a[1..]);
        var engine = Opt(a, "--engine", "Baiern_Emer");
        var rate = int.Parse(Opt(a, "--rate", "44100"), CultureInfo.InvariantCulture);
        var setup = LoadSetup(root, engine);
        var cat = SoundCatalog.Scan(new GameFileSystem(root));
        var clips = new CatalogClipProvider(cat);
        var assets = VehicleAudioAssets.FromCatalog(cat, clips);
        var r = HeadlessDrive.Render(setup, clips, assets, rate, a[0]);
        o.WriteLine($"{engine}: {setup.Tables.Sum(t => t.Count)} table rows, skid {(assets.Skid is null ? "missing" : "ok")}, impact {(assets.Impact is null ? "missing" : "ok")}");
        o.WriteLine($"wrote {a[0]}: {F(r.Duration, "F3")} s, peak {F(r.Mix.Peak(), "F3")}, rms {F(r.Mix.Rms(), "F3")}");
        return 0;
    }

    private static int Formula(string[] a, TextWriter o, TextWriter e)
    {
        var root = Root(a);
        var setup = LoadSetup(root, Opt(a, "--engine", "Baiern_Emer"));
        var rpm = float.Parse(Opt(a, "--rpm", "3500"), CultureInfo.InvariantCulture);
        var thr = float.Parse(Opt(a, "--throttle", "1"), CultureInfo.InvariantCulture);
        var tableRpm = rpm + 1f;
        var res = new MeasuredSfxTable.RowResult[MeasuredSfxTable.MaxRows];
        for (var t = 0; t < 3; t++)
        {
            var rows = setup.Tables[t];
            if (rows.Count == 0)
            {
                continue;
            }

            MeasuredSfxTable.Evaluate(rows, tableRpm, res);
            var vol = t == 2 ? MeasuredSfxTable.ExhaustVolume(thr, setup.ExhaustMinVolume) : MeasuredSfxTable.EngineTableVolumes(thr, setup.Tables[1].Count > 0).Item1;
            o.WriteLine($"table {t} (volume {F(vol)}):");
            for (var i = 0; i < rows.Count && i < MeasuredSfxTable.MaxRows; i++)
            {
                var it = rows[i];
                o.WriteLine($"  {it.Sound}  A={F(it.RefRpm, "F0")} B={F(it.RpmMin, "F0")} C={F(it.RpmMax, "F0")}  pitch {F(res[i].Pitch, "F3")} gain {F(res[i].Gain, "F3")}");
            }
        }

        return 0;
    }
}
