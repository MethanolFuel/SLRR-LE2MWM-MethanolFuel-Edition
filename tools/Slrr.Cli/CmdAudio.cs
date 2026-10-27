using System.Globalization;
using System.Numerics;
using Slrr.Audio;
using Slrr.Core;
using Slrr.Decompiler;
using Slrr.Formats.Tufa;

namespace Slrr.Cli;

/// <summary>
/// <c>audio</c>: the software sound engine (src/Slrr.Audio).
/// Needs a ProjectReference from Slrr.Cli.csproj to src/Slrr.Audio and a <c>"audio" =&gt; CmdAudio.Run(args[1..], o, e)</c> line
/// in Commands.cs (added at integration).
/// </summary>
public static class CmdAudio
{
    public const string Usage = """
        audio list [gameRoot]                         every .wav (format, duration, peak, chunks, loops) and the rpk sound entries
        audio inspect <file.wav|archive.rpk:0xID> [gameRoot]   one sound: format, loops, dominant frequency
        audio engines [gameRoot]                      engine sound tables (SfxTable.addItem rows) of every shipped engine block
        audio rev <out.wav> [--engine Baiern_Emer] [--from 800] [--to 7000] [--seconds 12] [--rate 44100] [gameRoot]
                                                      render an engine rev sweep (rpm ramp, throttle ramp 0.2..1)
        audio demo <out.wav> [gameRoot]               impacts at several intensities/positions plus a tyre skid
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
                "list" => List(rest, o),
                "inspect" => Inspect(rest, o, e),
                "engines" => Engines(rest, o),
                "rev" => Rev(rest, o, e),
                "demo" => Demo(rest, o, e),
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

    private static int List(string[] a, TextWriter o)
    {
        var fs = new GameFileSystem(Root(a));
        var cat = SoundCatalog.Scan(fs);
        var wavs = cat.WavFiles().ToList();
        long ok = 0;
        var formats = new Dictionary<string, int>();
        foreach (var w in wavs)
        {
            var rel = Path.GetRelativePath(fs.Root, w);
            try
            {
                var c = WavFile.Load(w);
                ok++;
                var fmt = $"{c.SampleRate} Hz {c.ChannelCount}ch {c.BitsPerSample}-bit";
                formats[fmt] = formats.GetValueOrDefault(fmt) + 1;
                var extra = string.Join(",", c.ChunkIds.Where(x => x is not ("fmt " or "data")));
                o.WriteLine($"{rel}  {fmt}  {F(c.Duration)} s  peak {F(c.Peak())}  [{extra}]" + (c.Loops.Count > 0 ? $" loop {c.Loops[0].StartFrame}-{c.Loops[0].EndFrame}" : string.Empty));
            }
            catch (InvalidDataException ex)
            {
                o.WriteLine($"{rel}  ERROR {ex.Message}");
            }
        }

        o.WriteLine($"{wavs.Count} wav files, {ok} decoded");
        foreach (var kv in formats.OrderByDescending(k => k.Value))
        {
            o.WriteLine($"  {kv.Value,4}  {kv.Key}");
        }

        o.WriteLine($"{cat.Defs.Count} sound entries in rpk archives");
        foreach (var d in cat.Defs)
        {
            var missing = fs.Exists(d.SourceFile) ? string.Empty : "  MISSING FILE";
            o.WriteLine($"  {d.Archive}:0x{d.Id:X} {d.Alias} -> {d.SourceFile} vol {F(d.Volume, "0.###")} inst {d.Instances} min {F(d.MinDistance, "0.###")} max {F(d.MaxDistance, "0.###")} flags {d.Flags}{missing}");
        }

        return 0;
    }

    private static int Inspect(string[] a, TextWriter o, TextWriter e)
    {
        if (a.Length == 0)
        {
            return Fail(e);
        }

        var root = Root(a[1..]);
        var fs = new GameFileSystem(root);
        string path;
        SoundDef? def = null;
        var colon = a[0].IndexOf(".rpk:", StringComparison.OrdinalIgnoreCase);
        if (colon > 0)
        {
            var cat = SoundCatalog.Scan(fs);
            var id = Convert.ToUInt32(a[0][(colon + 5)..].Replace("0x", string.Empty, StringComparison.OrdinalIgnoreCase), 16);
            def = cat.Find(a[0][..(colon + 4)], id) ?? throw new InvalidOperationException("no such sound entry");
            path = fs.Resolve(def.SourceFile) ?? throw new InvalidOperationException("source file missing: " + def.SourceFile);
            o.WriteLine($"{def.Archive}:0x{def.Id:X} {def.Alias} -> {def.SourceFile}");
        }
        else
        {
            path = File.Exists(a[0]) ? a[0] : fs.Resolve(a[0]) ?? throw new InvalidOperationException("not found: " + a[0]);
        }

        var c = WavFile.Load(path);
        o.WriteLine($"{c.SampleRate} Hz, {c.ChannelCount} ch, {c.BitsPerSample}-bit (format {c.FormatTag}), {c.Frames} frames, {F(c.Duration, "F3")} s");
        o.WriteLine($"peak {F(c.Peak(), "F3")}  rms {F(c.Rms(), "F3")}  chunks {string.Join(" ", c.ChunkIds.Select(x => $"'{x}'"))}");
        foreach (var l in c.Loops)
        {
            o.WriteLine($"  smpl loop {l.StartFrame}..{l.EndFrame} count {l.PlayCount}");
        }

        foreach (var q in c.Cues)
        {
            o.WriteLine($"  cue {q.Id} @ {q.SampleOffset}");
        }

        var m = c.ToMono().Channels[0];
        if (m.Length >= 4096)
        {
            var mid = Math.Max(0, (m.Length / 2) - 2048);
            o.WriteLine($"dominant frequency (4096-pt window at middle) {F(Spectrum.PeakFrequency(m, mid, 4096, c.SampleRate), "F1")} Hz");
        }

        return 0;
    }

    private static string? FindBlockClass(string root, string engine)
    {
        var dir = Path.Combine(root, "parts", "engines", engine, "scripts");
        return Directory.Exists(dir)
            ? Directory.EnumerateFiles(dir, "*block.class").OrderBy(x => x, StringComparer.Ordinal).FirstOrDefault(f => HasSfx(f))
            : null;
    }

    private static bool HasSfx(string classFile) => File.ReadAllText(classFile, System.Text.Encoding.Latin1).Contains("getSfxTable", StringComparison.Ordinal);

    private static EngineSfxSetup Scrape(string classFile) =>
        SfxTableScraper.Parse(TufaDecompiler.DecompileFile(TufaFile.Read(classFile), out _));

    private static int Engines(string[] a, TextWriter o)
    {
        var root = Root(a);
        var items = 0;
        var blocks = 0;
        foreach (var f in Directory.EnumerateFiles(Path.Combine(root, "parts", "engines"), "*.class", SearchOption.AllDirectories).OrderBy(x => x, StringComparer.Ordinal))
        {
            if (!HasSfx(f))
            {
                continue;
            }

            var s = Scrape(f);
            var n = s.Tables.Sum(t => t.Count);
            if (n == 0)
            {
                continue;
            }

            blocks++;
            items += n;
            o.WriteLine($"{Path.GetRelativePath(root, f)}  exhaustMinVol {F(s.ExhaustMinVolume)}");
            for (var t = 0; t < 3; t++)
            {
                foreach (var it in s.Tables[t])
                {
                    o.WriteLine($"  table {t}: {it.Sound} ref {F(it.RefRpm, "F0")} rpm {F(it.RpmMin, "F0")}..{F(it.RpmMax, "F0")} vol {F(it.VolMin)}..{F(it.VolMax)}");
                }
            }
        }

        o.WriteLine($"{blocks} classes with sound tables, {items} items");
        return 0;
    }

    private static int Rev(string[] a, TextWriter o, TextWriter e)
    {
        if (a.Length == 0)
        {
            return Fail(e);
        }

        var root = Root(a[1..]);
        var engine = Opt(a, "--engine", "Baiern_Emer");
        var from = float.Parse(Opt(a, "--from", "800"), CultureInfo.InvariantCulture);
        var to = float.Parse(Opt(a, "--to", "7000"), CultureInfo.InvariantCulture);
        var seconds = double.Parse(Opt(a, "--seconds", "12"), CultureInfo.InvariantCulture);
        var rate = int.Parse(Opt(a, "--rate", "44100"), CultureInfo.InvariantCulture);
        var cls = FindBlockClass(root, engine);
        if (cls is null)
        {
            e.WriteLine($"error: no engine block with sound tables under parts/engines/{engine}/scripts");
            return 1;
        }

        var setup = Scrape(cls);
        var cat = SoundCatalog.Scan(new GameFileSystem(root));
        var mixer = new AudioMixer(rate);
        using var model = new EngineSoundModel(mixer, new CatalogClipProvider(cat), setup, 0.5f);
        o.WriteLine($"{Path.GetFileName(cls)}: {model.VoiceCount} voices ({setup.Tables.Sum(t => t.Count)} table items)");
        using var sink = new WavFileSink(rate, a[0]);
        AudioPump.Run(mixer, sink, seconds, t =>
        {
            var k = (float)(t / seconds);
            model.Update(from + ((to - from) * k), 0.2f + (0.8f * k));
        });
        var clip = sink.ToClip();
        o.WriteLine($"wrote {a[0]}: {F(clip.Duration)} s, peak {F(clip.Peak(), "F3")}, rms {F(clip.Rms(), "F3")}");
        return 0;
    }

    private static int Demo(string[] a, TextWriter o, TextWriter e)
    {
        if (a.Length == 0)
        {
            return Fail(e);
        }

        var root = Root(a[1..]);
        var cat = SoundCatalog.Scan(new GameFileSystem(root));
        var clips = new CatalogClipProvider(cat);
        var collision = cat.FindByAlias("sound.rpk", "collision") ?? throw new InvalidOperationException("sound.rpk collision entry missing");
        var slide = cat.FindByAlias("sound.rpk", "slide") ?? throw new InvalidOperationException("sound.rpk slide entry missing");
        var hit = clips.GetClip(new SoundKey(collision.Archive, collision.Id)) ?? throw new InvalidOperationException("collision wav missing");
        var slideClip = clips.GetClip(new SoundKey(slide.Archive, slide.Id)) ?? throw new InvalidOperationException("slide wav missing");
        const int Rate = 44100;
        var mixer = new AudioMixer(Rate);
        mixer.Listener.Position = Vector3.Zero;
        using var sink = new WavFileSink(Rate, a[0]);
        using var skid = new SkidSound(mixer, slideClip, slide, new Vector3(0, 0, 8));
        var impacts = new (double T, float Speed, float X)[] { (0.5, 5, -6), (1.5, 12, 0), (2.5, 20, 6), (3.5, 20, 0.5f), (3.7, 8, -2) };
        var next = 0;
        AudioPump.Run(mixer, sink, 6.0, t =>
        {
            // lateral position sweeps left to right while the car slides between 1 s and 5 s
            var slip = t is > 1.0 and < 5.0 ? 0.9f : 0.05f;
            skid.Update(slip, 25f, new Vector3((float)((t - 3) * 3), 0f, 8f));
            while (next < impacts.Length && t >= impacts[next].T)
            {
                var im = impacts[next++];
                ImpactSound.Play(mixer, hit, collision, im.Speed, new Vector3(im.X, 0, 5));
            }
        });
        var clip = sink.ToClip();
        o.WriteLine($"wrote {a[0]}: {F(clip.Duration)} s, peak {F(clip.Peak(), "F3")}, rms {F(clip.Rms(), "F3")}");
        return 0;
    }
}
