using System.Globalization;
using System.Text;
using Slrr.Core;
using Slrr.Formats.Rpak;

namespace Slrr.Audio;

/// <summary>
/// A sound resource definition: the text entry stored in an rpk (<c>sourcefile</c> .wav path plus
/// <c>volume</c>, <c>instances</c>, <c>mindist</c>, <c>maxdist</c>, <c>flags</c>). MEASURED: field names and values from
/// <c>sound.rpk</c> (43 entries) and the sound entries of the other archives.
/// </summary>
public sealed record SoundDef(string Archive, uint Id, string Alias, string SourceFile)
{
    /// <summary>Gain multiplier, default 1 (MEASURED values 0.333, 0.5, 1, 2).</summary>
    public float Volume { get; init; } = 1f;

    /// <summary>Maximum simultaneously playing instances (MEASURED 4 or 10; role GUESSED from the name).</summary>
    public int Instances { get; init; } = 1;

    /// <summary>Distance (m) below which a positional sound is at full volume (MEASURED 1..20; role GUESSED).</summary>
    public float MinDistance { get; init; } = 1f;

    /// <summary>Distance (m) beyond which a positional sound is silent (only the police siren sets it: 500).</summary>
    public float MaxDistance { get; init; } = 0f;

    /// <summary><c>flags</c> value, 0 if absent. Police_1..6 carry 1 (GUESSED: looping).</summary>
    public int Flags { get; init; }

    public IReadOnlyDictionary<string, string> Raw { get; init; } = new Dictionary<string, string>();

    public bool Loops => (Flags & 1) != 0;

    public static SoundDef? Parse(string archive, uint id, string alias, ReadOnlySpan<byte> data)
    {
        var raw = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in Encoding.Latin1.GetString(data).Split('\n'))
        {
            var t = line.Trim();
            var semi = t.IndexOf(';', StringComparison.Ordinal);
            if (semi >= 0)
            {
                t = t[..semi].Trim();
            }

            var sp = t.IndexOfAny([' ', '\t']);
            if (sp > 0)
            {
                raw[t[..sp]] = t[(sp + 1)..].Trim();
            }
        }

        if (!raw.TryGetValue("sourcefile", out var src) || !src.EndsWith(".wav", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        float F(string k, float d) => raw.TryGetValue(k, out var v) && float.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out var x) ? x : d;
        return new SoundDef(archive, id, alias, src)
        {
            Volume = F("volume", 1f),
            Instances = (int)F("instances", 1f),
            MinDistance = F("mindist", 1f),
            MaxDistance = F("maxdist", 0f),
            Flags = (int)F("flags", 0f),
            Raw = raw,
        };
    }
}

/// <summary>Index of every sound resource in the shipped rpk archives, and of the shipped .wav files.</summary>
public sealed class SoundCatalog
{
    private readonly Dictionary<(string, uint), SoundDef> _byId = [];

    private SoundCatalog(GameFileSystem fs) => Files = fs;

    public GameFileSystem Files { get; }

    public IReadOnlyList<SoundDef> Defs { get; private set; } = [];

    /// <summary>Scan the given archives (default: every .rpk in the game root).</summary>
    public static SoundCatalog Scan(GameFileSystem fs, IEnumerable<string>? rpks = null)
    {
        var cat = new SoundCatalog(fs);
        var defs = new List<SoundDef>();
        foreach (var path in rpks ?? fs.Enumerate(".rpk"))
        {
            RpakArchive a;
            try
            {
                a = RpakArchive.Read(path);
            }
            catch (Exception ex) when (ex is InvalidDataException or IOException or Slrr.Core.SlrrFormatException)
            {
                continue;
            }

            var name = Path.GetFileName(path).ToLowerInvariant();
            foreach (var e in a.Entries)
            {
                // Sound/texture-like text entries only; skip folders and binary blocks without reading all data.
                if (e.Size == 0 || e.Size > 4096 || e.TypeOfEntry == RpakArchive.ExtraEntryType)
                {
                    continue;
                }

                var d = SoundDef.Parse(name, e.Id, e.Alias, a.GetData(e).Span);
                if (d is not null)
                {
                    defs.Add(d);
                    cat._byId[(name, e.Id)] = d;
                }
            }
        }

        cat.Defs = defs;
        return cat;
    }

    public SoundDef? Find(string archive, uint id) => _byId.GetValueOrDefault((archive.ToLowerInvariant(), id));

    /// <summary>First definition with this alias in an archive (e.g. <c>sound.rpk</c> "slide").</summary>
    public SoundDef? FindByAlias(string archive, string alias) =>
        Defs.FirstOrDefault(d => d.Archive.Equals(archive, StringComparison.OrdinalIgnoreCase) && d.Alias.Equals(alias, StringComparison.OrdinalIgnoreCase));

    public IEnumerable<string> WavFiles() => Files.Enumerate(".wav");

    /// <summary>Loads and decodes a game-relative .wav path (e.g. <c>sound\wav\engine\idle.wav</c>); null if missing.</summary>
    public WavClip? LoadWav(string gamePath) => Files.Resolve(gamePath) is { } p ? WavFile.Load(p) : null;
}
