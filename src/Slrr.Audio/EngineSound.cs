using System.Globalization;
using System.Numerics;
using System.Text.RegularExpressions;

namespace Slrr.Audio;

/// <summary>Identifies a sound resource: the rpk it lives in (lower-case file name, e.g. <c>baiern_emer.rpk</c>) and its id.</summary>
public readonly record struct SoundKey(string Archive, uint Id)
{
    public override string ToString() => $"{Archive}:0x{Id:X}";
}

/// <summary>Resolves sound keys to decoded clips and their definition entry. Implemented over the game files by <see cref="CatalogClipProvider"/>.</summary>
public interface IClipProvider
{
    /// <summary>The decoded clip, or null when the resource or its .wav is not available.</summary>
    WavClip? GetClip(SoundKey key);

    /// <summary>The rpk definition (volume, instances, distances) if known.</summary>
    SoundDef? GetDef(SoundKey key);
}

public sealed class CatalogClipProvider(SoundCatalog catalog) : IClipProvider
{
    private readonly Dictionary<SoundKey, WavClip?> _clips = [];

    public WavClip? GetClip(SoundKey key)
    {
        if (_clips.TryGetValue(key, out var c))
        {
            return c;
        }

        var def = GetDef(key);
        WavClip? clip = null;
        if (def is not null)
        {
            try
            {
                clip = catalog.LoadWav(def.SourceFile);
            }
            catch (InvalidDataException)
            {
                clip = null;
            }
        }

        _clips[key] = clip;
        return clip;
    }

    public SoundDef? GetDef(SoundKey key) => catalog.Find(key.Archive, key.Id);
}

/// <summary>Dictionary-backed provider for tests and tools.</summary>
public sealed class MapClipProvider : IClipProvider
{
    private readonly Dictionary<SoundKey, (WavClip Clip, SoundDef? Def)> _map = [];

    public MapClipProvider Add(SoundKey key, WavClip clip, SoundDef? def = null)
    {
        _map[key] = (clip, def);
        return this;
    }

    public WavClip? GetClip(SoundKey key) => _map.TryGetValue(key, out var v) ? v.Clip : null;

    public SoundDef? GetDef(SoundKey key) => _map.TryGetValue(key, out var v) ? v.Def : null;
}

/// <summary>
/// One <c>SfxTable.addItem(sound, rpm, rpmMin, rpmMax, volMin, volMax)</c> row as the Parts layer stores it
/// (<c>SfxItem(Sound, A, B, C, D, E)</c> in Slrr.Engine.Parts). Argument roles: the sample plays at its native pitch at
/// <see cref="RefRpm"/>, is audible between <see cref="RpmMin"/> and <see cref="RpmMax"/>, and its volume runs from
/// <see cref="VolMin"/> at RpmMin to <see cref="VolMax"/> at RpmMax. The ranges are MEASURED from the 79 shipped engine-part
/// call sites; the roles are GUESSED from the decompiled values (e.g. (3500, 500..6500, 0.42..0.52) and
/// (6200, 4000..18000, 0.7..0.2)) and the shipped file names (<c>0900RPM.wav</c>, ...).
/// </summary>
public readonly record struct EngineSfxItem(SoundKey Sound, float RefRpm, float RpmMin, float RpmMax, float VolMin, float VolMax);

/// <summary>The three sound tables of a chassis (<c>Chassis.getSfxTable(0..2)</c>) plus <c>setSfxExhaustMinVol</c>.</summary>
public sealed class EngineSfxSetup
{
    /// <summary>Table 0 = engine block loops, table 1 = unused in every shipped engine (cleared), table 2 = exhaust loops (MEASURED: only 0 and 2 get items, 2 comes with setSfxExhaustMinVol).</summary>
    public List<EngineSfxItem>[] Tables { get; } = [[], [], []];

    /// <summary>Exhaust level at zero load (shipped values 0.6 and 0.9; role GUESSED).</summary>
    public float ExhaustMinVolume { get; set; } = 1f;
}

/// <summary>
/// Crossfaded RPM/load engine sound. Every table item is one looping voice; its gain and playback rate follow the rpm:
/// <list type="bullet">
/// <item>rate = rpm / RefRpm (clamped 0.25..4) [GUESSED; the table calls RefRpm the "rpm" of the item and the sample files are named after an rpm]</item>
/// <item>gain = lerp(VolMin, VolMax, (rpm-RpmMin)/(RpmMax-RpmMin)) [GUESSED] x edge fade [GUESSED]: quarter-sine rise from RpmMin to RefRpm, quarter-cosine fall from RefRpm to RpmMax, so overlapping neighbours crossfade</item>
/// <item>load: table 0 x (0.7 + 0.3 load) [GUESSED]; table 2 (exhaust) x lerp(ExhaustMinVolume, 1, load) [GUESSED role of setSfxExhaustMinVol]</item>
/// </list>
/// </summary>
public sealed class EngineSoundModel : IDisposable
{
    private readonly AudioMixer _mixer;
    private readonly EngineSfxSetup _setup;
    private readonly List<(int Table, EngineSfxItem Item, Voice Voice)> _voices = [];

    public EngineSoundModel(AudioMixer mixer, IClipProvider clips, EngineSfxSetup setup, float masterGain = 1f, Vector3? position = null, float minDistance = 5f)
    {
        _mixer = mixer;
        _setup = setup;
        MasterGain = masterGain;
        for (var t = 0; t < setup.Tables.Length; t++)
        {
            foreach (var item in setup.Tables[t])
            {
                var clip = clips.GetClip(item.Sound);
                if (clip is null)
                {
                    continue;
                }

                var def = clips.GetDef(item.Sound);
                var v = mixer.Play(clip, new PlayParams
                {
                    Gain = 0f,
                    Loop = true,
                    Channel = SoundChannel.Engine,
                    Pitch = 1f,
                    Position = position,
                    MinDistance = minDistance,
                });
                if (v is not null)
                {
                    _defGain[v] = def?.Volume ?? 1f;
                    _voices.Add((t, item, v));
                }
            }
        }
    }

    private readonly Dictionary<Voice, float> _defGain = [];

    public float MasterGain { get; set; }

    public int VoiceCount => _voices.Count;

    /// <summary>Gain and rate of one item at an rpm (before load and master gain). Gain 0 outside [RpmMin, RpmMax].</summary>
    public static (float Gain, float Rate) Evaluate(EngineSfxItem it, float rpm)
    {
        var rate = Math.Clamp(it.RefRpm > 0 ? rpm / it.RefRpm : 1f, 0.25f, 4f);
        if (rpm <= it.RpmMin || rpm >= it.RpmMax)
        {
            return (0f, rate);
        }

        var span = it.RpmMax - it.RpmMin;
        var vol = it.VolMin + ((it.VolMax - it.VolMin) * ((rpm - it.RpmMin) / span));
        float edge;
        if (rpm < it.RefRpm)
        {
            var w = it.RefRpm - it.RpmMin;
            edge = w <= 0 ? 1f : MathF.Sin(MathF.PI / 2f * ((rpm - it.RpmMin) / w));
        }
        else
        {
            var w = it.RpmMax - it.RefRpm;
            edge = w <= 0 ? 1f : MathF.Cos(MathF.PI / 2f * ((rpm - it.RefRpm) / w));
        }

        return (vol * edge, rate);
    }

    /// <summary>Applies an engine state: rpm and throttle load 0..1; <paramref name="position"/> null = non-positional (cockpit view).</summary>
    public void Update(float rpm, float load, Vector3? position = null)
    {
        load = Math.Clamp(load, 0f, 1f);
        foreach (var (table, item, voice) in _voices)
        {
            var (g, rate) = Evaluate(item, rpm);
            var loadGain = table == 2 ? _setup.ExhaustMinVolume + ((1f - _setup.ExhaustMinVolume) * load) : 0.7f + (0.3f * load);
            voice.Gain = g * loadGain * MasterGain * _defGain[voice];
            voice.Pitch = rate;
            if (position.HasValue)
            {
                voice.Position = position.Value;
            }
        }

    }

    public void Dispose()
    {
        foreach (var (_, _, v) in _voices)
        {
            v.Stop();
        }

        _voices.Clear();
    }
}

/// <summary>
/// Extracts the engine sound tables from decompiled engine-part script text (<c>slrr decompile</c> output of a
/// <c>*_block.class</c>): <c>getSfxTable(n)</c>, <c>clear()</c>, <c>addItem(new ResourceRef(parts.engines.X:0xAAr), a, b, c, d, e)</c>
/// and <c>setSfxExhaustMinVol(v)</c>. The runtime path is the Parts layer's <c>SfxTableState</c>; this scraper exists so tables
/// can be inspected and rendered without running the VM.
/// </summary>
public static partial class SfxTableScraper
{
    [GeneratedRegex(@"getSfxTable\((\d+)\)")]
    private static partial Regex TableRx();

    [GeneratedRegex(@"addItem\(new ResourceRef\(([\w.]+):0x([0-9A-Fa-f]+)r\),\s*(.+)\);")]
    private static partial Regex ItemRx();

    [GeneratedRegex(@"setSfxExhaustMinVol\(([-0-9.]+)f?\)")]
    private static partial Regex MinVolRx();

    public static EngineSfxSetup Parse(string decompiledSource)
    {
        var setup = new EngineSfxSetup();
        var current = -1;
        foreach (var line in decompiledSource.Split('\n'))
        {
            if (TableRx().Match(line) is { Success: true } t)
            {
                current = int.Parse(t.Groups[1].Value, CultureInfo.InvariantCulture);
                if (current is < 0 or > 2)
                {
                    current = -1;
                }

                continue;
            }

            if (current >= 0 && line.Contains(".clear()", StringComparison.Ordinal))
            {
                setup.Tables[current].Clear();
                continue;
            }

            if (current >= 0 && ItemRx().Match(line) is { Success: true } m)
            {
                var args = SplitArgs(m.Groups[3].Value.TrimEnd(')', ' ', '\r'));
                if (args.Count == 5)
                {
                    var v = args.Select(Eval).ToArray();
                    var archive = m.Groups[1].Value[(m.Groups[1].Value.LastIndexOf('.') + 1)..].ToLowerInvariant() + ".rpk";
                    setup.Tables[current].Add(new EngineSfxItem(new SoundKey(archive, Convert.ToUInt32(m.Groups[2].Value, 16)), v[0], v[1], v[2], v[3], v[4]));
                }

                continue;
            }

            if (MinVolRx().Match(line) is { Success: true } mv)
            {
                setup.ExhaustMinVolume = float.Parse(mv.Groups[1].Value, CultureInfo.InvariantCulture);
            }
        }

        return setup;
    }

    private static List<string> SplitArgs(string s) => s.Split(',').Select(x => x.Trim()).ToList();

    /// <summary>Evaluates "900.0 * 2", "0.72000003", "1.5f" style constant expressions (+ - * /).</summary>
    public static float Eval(string expr)
    {
        var tokens = Regex.Matches(expr.Replace("f", string.Empty, StringComparison.Ordinal), @"[-+]?\d+(\.\d+)?|[*/]")
            .Select(x => x.Value).ToList();
        if (tokens.Count == 0)
        {
            throw new FormatException(expr);
        }

        double acc = double.Parse(tokens[0], CultureInfo.InvariantCulture);
        for (var i = 1; i + 1 < tokens.Count; i += 2)
        {
            var rhs = double.Parse(tokens[i + 1], CultureInfo.InvariantCulture);
            acc = tokens[i] == "*" ? acc * rhs : acc / rhs;
        }

        return (float)acc;
    }
}
