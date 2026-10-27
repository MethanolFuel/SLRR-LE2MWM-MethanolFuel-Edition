using System.Numerics;

namespace Slrr.Audio;

/// <summary>Listener pose for positional voices. Left-handed, Y up (the game's D3D convention; handedness GUESSED).</summary>
public sealed class Listener
{
    public Vector3 Position { get; set; }

    public Vector3 Forward { get; set; } = Vector3.UnitZ;

    public Vector3 Up { get; set; } = Vector3.UnitY;

    /// <summary>Unit vector pointing to the listener's right (left-handed: <c>up x forward</c>).</summary>
    public Vector3 Right
    {
        get
        {
            var r = Vector3.Cross(Up, Forward);
            return r.LengthSquared() < 1e-12f ? Vector3.UnitX : Vector3.Normalize(r);
        }
    }
}

/// <summary>Mixer channel group: the three volume channels of <c>java.sound.Sound</c> (MEASURED names/ids, java.sound.md).</summary>
public enum SoundChannel
{
    Effects = 0,
    Music = 1,
    Engine = 2,
}

/// <summary>How a voice is started.</summary>
public sealed class PlayParams
{
    public float Gain { get; set; } = 1f;

    /// <summary>Playback-rate multiplier (1 = native pitch).</summary>
    public float Pitch { get; set; } = 1f;

    public bool Loop { get; set; }

    /// <summary>Loop region in frames; default is the clip's first <c>smpl</c> loop, else the whole clip.</summary>
    public (int Start, int End)? LoopRegion { get; set; }

    /// <summary>Null = non-positional (UI) sound, centred.</summary>
    public Vector3? Position { get; set; }

    public float MinDistance { get; set; } = 1f;

    /// <summary>0 = unlimited (inverse-distance only).</summary>
    public float MaxDistance { get; set; }

    public float Rolloff { get; set; } = 1f;

    /// <summary>Balance for non-positional voices, -1 (left) .. 1 (right).</summary>
    public float Pan { get; set; }

    public SoundChannel Channel { get; set; } = SoundChannel.Effects;

    /// <summary>The scripts' last <c>nplay</c> argument: the owning object's id (0 = none).</summary>
    public int OwnerId { get; set; }

    /// <summary>Instance-limit group (e.g. a resource key) and the cap (the sound entry's <c>instances</c>); the oldest voice of the group is stolen.</summary>
    public string? Group { get; set; }

    public int MaxInstances { get; set; }

    /// <summary>Linear fade-in time in seconds.</summary>
    public float FadeIn { get; set; }
}

/// <summary>One playing sound. Mutate Gain/Pitch/Position from the game thread; the mixer reads them at the next Render.</summary>
public sealed class Voice
{
    internal readonly float[][] Data;
    internal double Cursor;
    internal float PrevGainL;
    internal float PrevGainR;
    internal float PrevRate;
    internal bool Started;
    internal float FadeLevel;
    internal float FadeTarget;
    internal float FadeStep; // per output frame, always >= 0
    internal bool StopAfterFade;
    internal long Serial;

    internal Voice(int id, WavClip clip, PlayParams p, int outRate)
    {
        Id = id;
        Clip = clip;
        Positional = p.Position.HasValue;
        Data = Positional ? [clip.ToMono().Channels[0]] : clip.Channels;
        Gain = p.Gain;
        Pitch = p.Pitch;
        Loop = p.Loop;
        Position = p.Position ?? Vector3.Zero;
        MinDistance = Math.Max(p.MinDistance, 1e-3f);
        MaxDistance = p.MaxDistance;
        Rolloff = p.Rolloff;
        Pan = p.Pan;
        Channel = p.Channel;
        OwnerId = p.OwnerId;
        Group = p.Group;
        OutRate = outRate;
        var region = p.LoopRegion ?? (clip.Loops.Count > 0 && clip.Loops[0].EndFrame > clip.Loops[0].StartFrame
            ? (clip.Loops[0].StartFrame, clip.Loops[0].EndFrame + 1)
            : (0, clip.Frames));
        LoopStart = Math.Clamp(region.Item1, 0, Math.Max(clip.Frames - 1, 0));
        LoopEnd = Math.Clamp(region.Item2, LoopStart + 1, Math.Max(clip.Frames, 1));
        FadeLevel = p.FadeIn > 0 ? 0f : 1f;
        FadeTarget = 1f;
        FadeStep = p.FadeIn > 0 ? 1f / (p.FadeIn * outRate) : 0f;
    }

    public int Id { get; }

    public WavClip Clip { get; }

    public bool Positional { get; }

    public float Gain { get; set; }

    public float Pitch { get; set; }

    public bool Loop { get; set; }

    public Vector3 Position { get; set; }

    public float MinDistance { get; set; }

    public float MaxDistance { get; set; }

    public float Rolloff { get; set; }

    public float Pan { get; set; }

    public SoundChannel Channel { get; }

    public int OwnerId { get; }

    public string? Group { get; }

    public int LoopStart { get; }

    public int LoopEnd { get; }

    public int OutRate { get; }

    public bool Finished { get; internal set; }

    /// <summary>Playback position in source frames.</summary>
    public double Cursor0 => Cursor;

    /// <summary>Fade to silence over <paramref name="seconds"/>, then free the voice (0 = immediately at the next render).</summary>
    public void Stop(float seconds = 0f)
    {
        FadeTarget = 0f;
        FadeStep = seconds <= 0 ? 1f : Math.Max(1f / (seconds * OutRate), 1e-9f);
        StopAfterFade = true;
    }

    /// <summary>Linear fade of the voice's fade envelope to <paramref name="target"/> (0..1) over <paramref name="seconds"/>.</summary>
    public void FadeTo(float target, float seconds)
    {
        FadeTarget = Math.Clamp(target, 0f, 1f);
        FadeStep = seconds <= 0 ? 1f : Math.Max(Math.Abs(FadeTarget - FadeLevel) / (seconds * OutRate), 1e-9f);
        StopAfterFade = false;
    }
}

/// <summary>
/// Software mixer: N voices, pitch shifting with 4-point Hermite interpolation, per-voice gain (ramped across each block to
/// avoid zipper noise), 3D pan and distance attenuation without Doppler, looping and fades. Output is interleaved stereo float.
/// The distance/pan model is GUESSED (the original runs on DirectSound3D/EAX through <c>eax.dll</c>; no measurement is possible
/// without the exe running): inverse-distance clamped between <c>MinDistance</c> and <c>MaxDistance</c>, equal-power pan on the
/// sine of the azimuth, and a linear fade over the last 20 % of <c>MaxDistance</c>.
/// </summary>
public sealed class AudioMixer
{
    private readonly object _lock = new();
    private readonly List<Voice> _voices = [];
    private readonly float[] _channelVolume = [1f, 1f, 1f];
    private int _nextId = 1;
    private long _serial;

    public AudioMixer(int sampleRate = 44100, int maxVoices = 64)
    {
        SampleRate = sampleRate;
        MaxVoices = maxVoices;
    }

    public int SampleRate { get; }

    public int MaxVoices { get; }

    public Listener Listener { get; } = new();

    public float MasterVolume { get; set; } = 1f;

    public int ActiveVoices
    {
        get
        {
            lock (_lock)
            {
                return _voices.Count;
            }
        }
    }

    public float GetChannelVolume(SoundChannel c) => _channelVolume[(int)c];

    public void SetChannelVolume(SoundChannel c, float v) => _channelVolume[(int)c] = Math.Clamp(v, 0f, 1f);

    /// <summary>Starts a voice. Returns null if the voice cap is reached and nothing quieter could be stolen.</summary>
    public Voice? Play(WavClip clip, PlayParams? p = null)
    {
        p ??= new PlayParams();
        lock (_lock)
        {
            if (p.Group is not null && p.MaxInstances > 0)
            {
                var same = _voices.Where(v => v.Group == p.Group).OrderBy(v => v.Serial).ToList();
                while (same.Count >= p.MaxInstances)
                {
                    _voices.Remove(same[0]);
                    same.RemoveAt(0);
                }
            }

            if (_voices.Count >= MaxVoices)
            {
                var quiet = _voices.OrderBy(v => v.Gain * v.FadeLevel).First();
                if (quiet.Gain * quiet.FadeLevel >= p.Gain)
                {
                    return null;
                }

                _voices.Remove(quiet);
            }

            var voice = new Voice(_nextId++, clip, p, SampleRate) { Serial = _serial++ };
            _voices.Add(voice);
            return voice;
        }
    }

    public Voice? Find(int id)
    {
        lock (_lock)
        {
            return _voices.FirstOrDefault(v => v.Id == id);
        }
    }

    public bool Stop(int id, float fadeSeconds = 0f)
    {
        var v = Find(id);
        v?.Stop(fadeSeconds);
        return v is not null;
    }

    public void StopAll()
    {
        lock (_lock)
        {
            _voices.Clear();
        }
    }

    /// <summary>The gain a positional voice gets from the listener distance (before pan). Public for tests and adapters.</summary>
    public static float DistanceGain(float distance, float min, float max, float rolloff)
    {
        if (max > 0 && distance >= max)
        {
            return 0f;
        }

        var d = Math.Max(distance, min);
        var g = min / (min + (rolloff * (d - min)));
        if (max > 0 && distance > 0.8f * max)
        {
            g *= (max - distance) / (0.2f * max);
        }

        return g;
    }

    /// <summary>Equal-power left/right gains for a position relative to a listener (pan = sin of azimuth, 0 when coincident).</summary>
    public static (float L, float R) PanGains(Listener l, Vector3 pos)
    {
        var rel = pos - l.Position;
        var d = rel.Length();
        var pan = d < 1e-4f ? 0f : Math.Clamp(Vector3.Dot(rel / d, l.Right), -1f, 1f);
        var a = (pan + 1f) * MathF.PI / 4f;
        return (MathF.Cos(a), MathF.Sin(a));
    }

    /// <summary>Renders <c>buffer.Length / 2</c> interleaved stereo frames, overwriting the buffer.</summary>
    public void Render(Span<float> buffer)
    {
        buffer.Clear();
        var frames = buffer.Length / 2;
        if (frames == 0)
        {
            return;
        }

        lock (_lock)
        {
            for (var i = _voices.Count - 1; i >= 0; i--)
            {
                var v = _voices[i];
                RenderVoice(v, buffer, frames);
                if (v.Finished)
                {
                    _voices.RemoveAt(i);
                }
            }
        }

        var master = MasterVolume;
        for (var i = 0; i < buffer.Length; i++)
        {
            buffer[i] = Math.Clamp(buffer[i] * master, -1f, 1f);
        }
    }

    private static float Hermite(float[] d, int n, double pos, bool loop, int ls, int le)
    {
        var i1 = (int)pos;
        var t = (float)(pos - i1);
        float At(int i)
        {
            if (loop)
            {
                var len = le - ls;
                if (i >= le)
                {
                    i = ls + ((i - ls) % len);
                }
                else if (i < ls && i1 >= ls)
                {
                    i = le - Math.Min(ls - i, len);
                }
            }

            return i < 0 || i >= n ? 0f : d[i];
        }

        var y0 = At(i1 - 1);
        var y1 = At(i1);
        var y2 = At(i1 + 1);
        var y3 = At(i1 + 2);
        var c0 = y1;
        var c1 = 0.5f * (y2 - y0);
        var c2 = y0 - (2.5f * y1) + (2f * y2) - (0.5f * y3);
        var c3 = (0.5f * (y3 - y0)) + (1.5f * (y1 - y2));
        return (((((c3 * t) + c2) * t) + c1) * t) + c0;
    }

    private void RenderVoice(Voice v, Span<float> buffer, int frames)
    {
        // Target gains for this block.
        float gl, gr;
        var baseGain = v.Gain * _channelVolume[(int)v.Channel];
        if (v.Positional)
        {
            var dist = (v.Position - Listener.Position).Length();
            baseGain *= DistanceGain(dist, v.MinDistance, v.MaxDistance, v.Rolloff);
            (gl, gr) = PanGains(Listener, v.Position);
        }
        else
        {
            // Non-positional: unity centre, constant-power only for explicit balance.
            var bal = Math.Clamp(v.Pan, -1f, 1f);
            gl = bal > 0 ? 1f - bal : 1f;
            gr = bal < 0 ? 1f + bal : 1f;
        }

        var tgtL = baseGain * gl;
        var tgtR = baseGain * gr;
        var rateTarget = (float)(Math.Max(v.Pitch, 0f) * v.Clip.SampleRate / v.OutRate);
        if (!v.Started)
        {
            v.PrevGainL = tgtL;
            v.PrevGainR = tgtR;
            v.PrevRate = rateTarget;
            v.Started = true;
        }

        var data = v.Data;
        var n = data[0].Length;
        var inv = 1f / frames;
        var silent = tgtL < 1e-6f && tgtR < 1e-6f && v.PrevGainL < 1e-6f && v.PrevGainR < 1e-6f && v.FadeStep == 0f;
        for (var f = 0; f < frames; f++)
        {
            var a = (f + 1) * inv;
            var rate = v.PrevRate + ((rateTarget - v.PrevRate) * a);
            if (v.FadeLevel != v.FadeTarget && v.FadeStep > 0)
            {
                v.FadeLevel = v.FadeLevel < v.FadeTarget
                    ? Math.Min(v.FadeLevel + v.FadeStep, v.FadeTarget)
                    : Math.Max(v.FadeLevel - v.FadeStep, v.FadeTarget);
            }

            if (!silent)
            {
                var l = v.PrevGainL + ((tgtL - v.PrevGainL) * a);
                var r = v.PrevGainR + ((tgtR - v.PrevGainR) * a);
                var pos = v.Cursor;
                if (data.Length == 1)
                {
                    var s = Hermite(data[0], n, pos, v.Loop, v.LoopStart, v.LoopEnd) * v.FadeLevel;
                    buffer[2 * f] += s * l;
                    buffer[(2 * f) + 1] += s * r;
                }
                else
                {
                    buffer[2 * f] += Hermite(data[0], n, pos, v.Loop, v.LoopStart, v.LoopEnd) * v.FadeLevel * l;
                    buffer[(2 * f) + 1] += Hermite(data[1], n, pos, v.Loop, v.LoopStart, v.LoopEnd) * v.FadeLevel * r;
                }
            }

            v.Cursor += rate;
            if (v.Loop)
            {
                if (v.Cursor >= v.LoopEnd)
                {
                    v.Cursor = v.LoopStart + ((v.Cursor - v.LoopStart) % (v.LoopEnd - v.LoopStart));
                }
            }
            else if (v.Cursor >= n)
            {
                v.Finished = true;
                break;
            }

            if (v.StopAfterFade && v.FadeLevel <= 0f)
            {
                v.Finished = true;
                break;
            }
        }

        v.PrevGainL = tgtL;
        v.PrevGainR = tgtR;
        v.PrevRate = rateTarget;
    }
}
