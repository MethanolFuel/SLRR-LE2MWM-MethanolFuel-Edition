using System.Numerics;

namespace Slrr.Audio;

/// <summary>
/// Tyre skid loop. Parameters come from the rpk sound entry (MEASURED <c>sound.rpk</c> "slide": volume 2.0, instances 4,
/// mindist 20, sourcefile <c>surface\SLIP3.WAV</c>); the slip-to-gain and speed-to-pitch curves are GUESSED.
/// </summary>
public sealed class SkidSound : IDisposable
{
    private readonly Voice _voice;
    private readonly float _volume;

    public SkidSound(AudioMixer mixer, WavClip clip, SoundDef? def = null, Vector3? position = null)
    {
        _volume = def?.Volume ?? 1f;
        _voice = mixer.Play(clip, new PlayParams
        {
            Gain = 0f,
            Loop = true,
            Position = position,
            MinDistance = def?.MinDistance ?? 20f,
            MaxDistance = def?.MaxDistance ?? 0f,
            Channel = SoundChannel.Effects,
        }) ?? throw new InvalidOperationException("no free voice");
    }

    /// <summary>Slip ratio 0..1 (0 = gripping), speed in m/s.</summary>
    public void Update(float slip, float speed, Vector3? position = null)
    {
        var s = Math.Clamp((slip - 0.15f) / 0.65f, 0f, 1f);
        _voice.Gain = _volume * s * s * (3f - (2f * s)) * 0.5f;
        _voice.Pitch = 0.85f + (0.4f * Math.Clamp(speed / 40f, 0f, 1f));
        if (position.HasValue)
        {
            _voice.Position = position.Value;
        }
    }

    public void Dispose() => _voice.Stop(0.05f);
}

/// <summary>Collision one-shots (<c>sound.rpk</c> "collision"/"damage"/"tire" entries; volume 1, instances 4).</summary>
public static class ImpactSound
{
    /// <summary>
    /// Plays an impact. <paramref name="impulse"/> is the closing speed in m/s; 20 m/s and up is full level
    /// (gain = sqrt curve, pitch drops up to 15 % for harder hits; both GUESSED).
    /// </summary>
    public static Voice? Play(AudioMixer mixer, WavClip clip, SoundDef? def, float impulse, Vector3? position = null)
    {
        var k = Math.Clamp(impulse / 20f, 0f, 1f);
        if (k <= 0.01f)
        {
            return null;
        }

        return mixer.Play(clip, new PlayParams
        {
            Gain = (def?.Volume ?? 1f) * MathF.Sqrt(k),
            Pitch = 1f - (0.15f * k),
            Position = position,
            MinDistance = def?.MinDistance ?? 3f,
            MaxDistance = def?.MaxDistance ?? 0f,
            Group = def is null ? "impact" : $"{def.Archive}:{def.Id}",
            MaxInstances = def?.Instances ?? 4,
            Channel = SoundChannel.Effects,
        });
    }
}

/// <summary>
/// Transmission whine for one direction. <c>rpm_trans_fwd/rev</c> = 1400 (MEASURED default of the Chassis script) is the
/// drive-shaft rpm at which the sample plays at native pitch (GUESSED role). The shipped entries (sound.rpk 7 and 9) name
/// <c>engine\SIKIT.WAV</c>, which is NOT in this install, so callers fall back to <see cref="Synth.Whine"/>.
/// </summary>
public sealed class GearWhine : IDisposable
{
    private readonly Voice _voice;

    public GearWhine(AudioMixer mixer, WavClip clip, float refRpm = 1400f, Vector3? position = null)
    {
        RefRpm = refRpm;
        _voice = mixer.Play(clip, new PlayParams { Gain = 0f, Loop = true, Position = position, MinDistance = 2f, Channel = SoundChannel.Engine })
            ?? throw new InvalidOperationException("no free voice");
    }

    public float RefRpm { get; }

    public void Update(float driveRpm, float load)
    {
        var r = Math.Abs(driveRpm);
        _voice.Pitch = Math.Clamp(r / RefRpm, 0.25f, 4f);
        _voice.Gain = Math.Clamp(r / (2f * RefRpm), 0f, 1f) * (0.3f + (0.7f * Math.Clamp(load, 0f, 1f))) * 0.5f;
    }

    public void Dispose() => _voice.Stop(0.05f);
}
