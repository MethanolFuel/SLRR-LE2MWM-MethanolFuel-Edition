using System.Numerics;

namespace Slrr.Audio;

/// <summary>Flag bits of <c>SfxRef.nplay</c> (MEASURED, java.util.md).</summary>
public static class SfxFlags
{
    public const int None = 0;
    public const int Loop = 1;
    public const int NoAutoStop = unchecked((int)0x80000000);
}

/// <summary>
/// What the <c>java.sound.Sound</c> and <c>java.util.resource.SfxRef</c> natives need from a real sound engine. The Ui area's
/// <c>SoundState</c> is a headless model (three volumes, music set, track, mode flags); an adapter in the integration layer
/// forwards each native to this interface:
/// <list type="bullet">
/// <item><c>Sound.setVolume(ch, v)/getVolume(ch)</c> -> <see cref="SetVolume"/>/<see cref="GetVolume"/> (channels 0 effects, 1 music, 2 engine)</item>
/// <item><c>Sound.changeMusicSet/nextTrack/prevTrack</c> -> the music members (no music files ship in this install; GUESSED to be streamed mp3/ogg in the original)</item>
/// <item><c>Sound.getMode/setMode/has3DHardware/hasMixHardware</c> -> <see cref="Mode"/>, <see cref="Has3DHardware"/>, <see cref="HasMixHardware"/></item>
/// <item><c>SfxRef.nplay(pos, a, pitch, vol, flags, ownerId)</c> -> <see cref="Play"/>; <c>stop(id)</c> -> <see cref="Stop"/>; <c>cache()</c> -> <see cref="Precache"/></item>
/// </list>
/// </summary>
public interface IUiSoundBackend
{
    float GetVolume(int channel);

    void SetVolume(int channel, float volume);

    void ChangeMusicSet(int set);

    void NextTrack();

    void PrevTrack();

    int Mode { get; set; }

    bool Has3DHardware { get; }

    bool HasMixHardware { get; }

    /// <summary>
    /// Starts a sound; returns the play id handed back to scripts (0 = failed). <paramref name="position"/> null = UI sound.
    /// Arguments from the script call sites: <c>a</c> is always 0 in shipped scripts except the unused overloads (role unknown),
    /// <c>pitch</c> is a rate multiplier (the lift passes <c>System.timeWarp - 0.5</c>), <c>volume</c> a gain (1.0 everywhere).
    /// A sound that is not looping stops itself unless <see cref="SfxFlags.NoAutoStop"/> is set.
    /// </summary>
    int Play(SoundKey sound, Vector3? position, float a, float pitch, float volume, int flags, int ownerId);

    void Stop(int playId);

    void Precache(SoundKey sound);
}

/// <summary>Reference implementation of <see cref="IUiSoundBackend"/> over <see cref="AudioMixer"/>.</summary>
public sealed class MixerUiSoundBackend(AudioMixer mixer, IClipProvider clips) : IUiSoundBackend
{
    private readonly Dictionary<int, Voice> _plays = [];

    public int Mode { get; set; }

    public bool Has3DHardware => false;

    public bool HasMixHardware => false;

    public int MusicSet { get; private set; } = -1;

    public int TrackOffset { get; private set; }

    public float GetVolume(int channel) => channel is >= 0 and <= 2 ? mixer.GetChannelVolume((SoundChannel)channel) : 0f;

    public void SetVolume(int channel, float volume)
    {
        if (channel is >= 0 and <= 2)
        {
            mixer.SetChannelVolume((SoundChannel)channel, volume);
        }
    }

    public void ChangeMusicSet(int set)
    {
        MusicSet = set;
        TrackOffset = 0;
    }

    public void NextTrack() => TrackOffset++;

    public void PrevTrack() => TrackOffset--;

    public int Play(SoundKey sound, Vector3? position, float a, float pitch, float volume, int flags, int ownerId)
    {
        var clip = clips.GetClip(sound);
        if (clip is null)
        {
            return 0;
        }

        var def = clips.GetDef(sound);
        var loop = (flags & SfxFlags.Loop) != 0 || (def?.Loops ?? false);
        var voice = mixer.Play(clip, new PlayParams
        {
            Gain = volume * (def?.Volume ?? 1f),
            Pitch = pitch <= 0 ? 1f : pitch,
            Loop = loop,
            Position = position,
            MinDistance = def?.MinDistance ?? 1f,
            MaxDistance = def?.MaxDistance ?? 0f,
            OwnerId = ownerId,
            Group = sound.ToString(),
            MaxInstances = def?.Instances ?? 0,
            Channel = SoundChannel.Effects,
        });
        if (voice is null)
        {
            return 0;
        }

        _plays[voice.Id] = voice;
        return voice.Id;
    }

    public void Stop(int playId)
    {
        if (_plays.Remove(playId, out var v))
        {
            v.Stop(0.02f);
        }
    }

    public void Precache(SoundKey sound) => clips.GetClip(sound);
}
