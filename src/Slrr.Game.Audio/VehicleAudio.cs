using System.Numerics;
using Slrr.Audio;

namespace Slrr.GameAudio;

/// <summary>Samples a car needs besides its engine tables. Any may be null (sound not shipped); whine falls back to a synthesised tone.</summary>
public sealed class VehicleAudioAssets
{
    public WavClip? Skid { get; init; }

    public SoundDef? SkidDef { get; init; }

    public WavClip? Impact { get; init; }

    public SoundDef? ImpactDef { get; init; }

    public WavClip? WhineForward { get; init; }

    public WavClip? WhineReverse { get; init; }

    public WavClip? Starter { get; init; }

    /// <summary><c>rpm_trans_fwd</c>: drive rpm at which the forward whine sample is at native pitch (Chassis default 1400, MEASURED).</summary>
    public float RpmTransForward { get; init; } = 1400f;

    public float RpmTransReverse { get; init; } = 1400f;

    /// <summary><c>sfx_starter_rpm</c> (Chassis default 200, MEASURED; the exe divides rpm' by 0x80c = 200).</summary>
    public float StarterRpm { get; init; } = 200f;

    /// <summary>Resolves the generic car samples from the sound.rpk aliases/ids (MEASURED names): "slide", "collision", ids 7/9 (SIKIT.WAV, not shipped) and 0xA (ignition, not shipped).</summary>
    public static VehicleAudioAssets FromCatalog(SoundCatalog catalog, IClipProvider clips)
    {
        var slide = catalog.FindByAlias("sound.rpk", "slide");
        var collision = catalog.FindByAlias("sound.rpk", "collision");
        return new VehicleAudioAssets
        {
            Skid = slide is null ? null : clips.GetClip(new SoundKey(slide.Archive, slide.Id)),
            SkidDef = slide,
            Impact = collision is null ? null : clips.GetClip(new SoundKey(collision.Archive, collision.Id)),
            ImpactDef = collision,
            WhineForward = clips.GetClip(new SoundKey("sound.rpk", 7)),
            WhineReverse = clips.GetClip(new SoundKey("sound.rpk", 9)),
            Starter = clips.GetClip(new SoundKey("sound.rpk", 0xA)),
        };
    }

    /// <summary>Synthetic stand-ins (tests, headless demos without game data).</summary>
    public static VehicleAudioAssets Synthetic() => new()
    {
        Skid = Synth.Noise(1.0, 22050, 7, 0.5f),
        Impact = Synth.Noise(0.4, 22050, 11, 0.8f),
    };
}

/// <summary>Tunables that are not part of the measured exe formula.</summary>
public sealed class VehicleAudioOptions
{
    /// <summary>False = non-positional (cockpit view): the car's own voices ignore the listener distance.</summary>
    public bool Positional { get; set; } = true;

    /// <summary>Mix gain of the engine/exhaust voices (the exe feeds the 'engine' channel; level is GUESSED).</summary>
    public float MasterGain { get; set; } = 0.5f;

    /// <summary>Extra mix level of the tyre skid loop (GUESSED; keeps the sum of engine + skid + impact under full scale).</summary>
    public float SkidLevel { get; set; } = 0.5f;

    /// <summary>Extra mix level of collision one-shots (GUESSED).</summary>
    public float ImpactLevel { get; set; } = 0.6f;

    /// <summary>Level of the synthesised whine when the sample is not shipped (GUESSED).</summary>
    public float SynthWhineGain { get; set; } = 0.35f;

    /// <summary>Base frequency of the synthesised whine at its native pitch (GUESSED).</summary>
    public double SynthWhineHz { get; set; } = 700.0;
}

/// <summary>
/// All the sound of one car: engine block (table 0/1), exhaust (table 2), starter, transmission whine, tyre skid and impacts.
/// The engine part is the measured exe formula (<see cref="MeasuredSfxTable"/>); skid, impact and the whine sample are the
/// Slrr.Audio effects with GUESSED curves. Call <see cref="Update"/> once per frame.
/// </summary>
public sealed class VehicleAudio : IDisposable
{
    private readonly AudioMixer _mixer;
    private readonly EngineSfxSetup _setup;
    private readonly VehicleAudioAssets _assets;
    private readonly VehicleAudioOptions _options;
    private readonly Voice?[][] _voices;
    private readonly float[][] _defGain;
    private readonly MeasuredSfxTable.RowResult[] _scratch = new MeasuredSfxTable.RowResult[MeasuredSfxTable.MaxRows];
    private readonly Voice? _starter;
    private readonly Voice? _whineFwd;
    private readonly Voice? _whineRev;
    private readonly float _whineScale;
    private readonly Voice? _skid;
    private readonly float _skidVolume;
    private readonly List<ImpactEvent> _impacts = [];
    private bool _disposed;

    public VehicleAudio(AudioMixer mixer, IClipProvider clips, EngineSfxSetup setup, VehicleAudioAssets assets, VehicleAudioOptions? options = null)
    {
        _mixer = mixer;
        _setup = setup;
        _assets = assets;
        _options = options ?? new VehicleAudioOptions();
        _voices = new Voice?[setup.Tables.Length][];
        _defGain = new float[setup.Tables.Length][];
        for (var t = 0; t < setup.Tables.Length; t++)
        {
            var rows = setup.Tables[t];
            var n = Math.Min(rows.Count, MeasuredSfxTable.MaxRows);
            _voices[t] = new Voice?[n];
            _defGain[t] = new float[n];
            for (var i = 0; i < n; i++)
            {
                var clip = clips.GetClip(rows[i].Sound);
                _defGain[t][i] = clips.GetDef(rows[i].Sound)?.Volume ?? 1f;
                if (clip is not null)
                {
                    _voices[t][i] = StartLoop(clip, SoundChannel.Engine);
                }
            }
        }

        if (assets.Starter is { } st)
        {
            _starter = StartLoop(st, SoundChannel.Engine);
        }

        var synth = assets.WhineForward is null || assets.WhineReverse is null ? Synth.Whine(_options.SynthWhineHz, 1.0) : null;
        _whineFwd = StartLoop(assets.WhineForward ?? synth!, SoundChannel.Engine);
        _whineRev = StartLoop(assets.WhineReverse ?? synth!, SoundChannel.Engine);
        _whineScale = synth is null ? 1f : _options.SynthWhineGain;
        if (assets.Skid is { } skid)
        {
            // same curves as Slrr.Audio.SkidSound (entry volume x smoothstep(slip) x 0.5, pitch with speed) with an extra mix level
            _skidVolume = assets.SkidDef?.Volume ?? 1f;
            _skid = mixer.Play(skid, new PlayParams
            {
                Gain = 0f,
                Loop = true,
                Position = _options.Positional ? Vector3.Zero : null,
                MinDistance = assets.SkidDef?.MinDistance ?? 20f,
                MaxDistance = assets.SkidDef?.MaxDistance ?? 0f,
                Channel = SoundChannel.Effects,
            });
        }
    }

    /// <summary>Number of engine/exhaust voices that exist (rows whose sample was found).</summary>
    public int EngineVoiceCount => _voices.Sum(v => v.Count(x => x is not null));

    public float MasterGain
    {
        get => _options.MasterGain;
        set => _options.MasterGain = value;
    }

    /// <summary>Last table rpm fed to the tables (|omega| x 9.5493 + 1), for diagnostics.</summary>
    public float LastTableRpm { get; private set; }

    /// <summary>The live voice of table <paramref name="table"/> row <paramref name="row"/> (null = sample not available).</summary>
    public Voice? EngineVoice(int table, int row) => table < _voices.Length && row < _voices[table].Length ? _voices[table][row] : null;

    private Voice? StartLoop(WavClip clip, SoundChannel ch) => _mixer.Play(clip, new PlayParams
    {
        Gain = 0f,
        Loop = true,
        Channel = ch,
        Position = _options.Positional ? Vector3.Zero : null,
        MinDistance = 5f,
    });

    /// <summary>Applies one frame of telemetry. <paramref name="timeWarp"/> is <c>System.timeWarp</c> (0 pauses all engine sound, as in the exe).</summary>
    public void Update(IVehicleAudioTelemetry t, float timeWarp = 1f)
    {
        if (_disposed)
        {
            return;
        }

        if (!t.Active || timeWarp <= 0f)
        {
            Silence();
            return;
        }

        var pos = t.Position;
        var rpm = MeasuredSfxTable.TableRpm(t.EngineAngularSpeed);
        LastTableRpm = rpm;
        var thr = MathF.Abs(t.Throttle);
        var (v0, v1) = MeasuredSfxTable.EngineTableVolumes(thr, _setup.Tables[1].Count > 0);
        if (t.StarterEngaged)
        {
            v0 = 0f;
            v1 = 0f;
        }

        ApplyTable(0, rpm, v0, timeWarp, pos);
        ApplyTable(1, rpm, v1, timeWarp, pos);
        ApplyTable(2, rpm, rpm > 1f ? MeasuredSfxTable.ExhaustVolume(thr, _setup.ExhaustMinVolume) : 0f, timeWarp, pos);

        if (_starter is not null)
        {
            Set(_starter, t.StarterEngaged ? 1f : 0f, rpm * timeWarp / MathF.Max(_assets.StarterRpm, 1f), pos);
        }

        var whine = MeasuredSfxTable.Whine(t.DriveShaftAngularSpeed, t.DriveShaftTorque, _assets.RpmTransForward, _assets.RpmTransReverse, timeWarp);
        if (_whineFwd is not null && _whineRev is not null)
        {
            Set(_whineFwd, whine is { Forward: true } w1 ? w1.Gain * _whineScale : 0f, whine?.Pitch ?? 1f, pos);
            Set(_whineRev, whine is { Forward: false } w2 ? w2.Gain * _whineScale : 0f, whine?.Pitch ?? 1f, pos);
        }

        if (_skid is not null)
        {
            var slip = 0f;
            var at = pos;
            for (var i = 0; i < t.WheelCount; i++)
            {
                var s = t.WheelSlip(i);
                if (s > slip)
                {
                    slip = s;
                    at = t.WheelPosition(i);
                }
            }

            var k = Math.Clamp((slip - 0.15f) / 0.65f, 0f, 1f);
            _skid.Gain = _skidVolume * k * k * (3f - (2f * k)) * 0.5f * _options.SkidLevel;
            _skid.Pitch = 0.85f + (0.4f * Math.Clamp(t.Speed / 40f, 0f, 1f));
            if (_skid.Positional)
            {
                _skid.Position = at;
            }
        }

        _impacts.Clear();
        t.DrainImpacts(_impacts);
        if (_assets.Impact is { } clip)
        {
            foreach (var e in _impacts)
            {
                var hit = ImpactSound.Play(_mixer, clip, _assets.ImpactDef, e.ClosingSpeed, _options.Positional ? e.Position : null);
                if (hit is not null)
                {
                    hit.Gain *= _options.ImpactLevel;
                }
            }
        }
    }

    private void ApplyTable(int table, float rpm, float tableVolume, float timeWarp, Vector3 pos)
    {
        var rows = _setup.Tables[table];
        var n = _voices[table].Length;
        MeasuredSfxTable.Evaluate(rows, rpm, _scratch);
        for (var i = 0; i < n; i++)
        {
            var v = _voices[table][i];
            if (v is null)
            {
                continue;
            }

            var r = _scratch[i];
            var effective = r.Gain * tableVolume * _defGain[table][i];
            if (effective <= MeasuredSfxTable.AudibleThreshold)
            {
                effective = 0f;
            }

            Set(v, effective * _options.MasterGain, Math.Clamp(r.Pitch * timeWarp, 0f, 8f), pos);
        }
    }

    private void Set(Voice v, float gain, float pitch, Vector3 pos)
    {
        v.Gain = gain;
        v.Pitch = pitch;
        if (v.Positional)
        {
            v.Position = pos;
        }
    }

    private void Silence()
    {
        foreach (var table in _voices)
        {
            foreach (var v in table)
            {
                if (v is not null)
                {
                    v.Gain = 0f;
                }
            }
        }

        foreach (var v in new[] { _starter, _whineFwd, _whineRev })
        {
            if (v is not null)
            {
                v.Gain = 0f;
            }
        }

        if (_skid is not null)
        {
            _skid.Gain = 0f;
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        foreach (var table in _voices)
        {
            foreach (var v in table)
            {
                v?.Stop(0.02f);
            }
        }

        _starter?.Stop(0.02f);
        _whineFwd?.Stop(0.02f);
        _whineRev?.Stop(0.02f);
        _skid?.Stop(0.05f);
    }
}
