using System.Numerics;
using Slrr.Audio;
using Slrr.Engine.Parts;
using Slrr.Game;
using HostSink = Slrr.Game.IAudioSink;
using OutputSink = Slrr.Audio.IAudioSink;

namespace Slrr.GameAudio;

/// <summary>Camera pose used as the listener.</summary>
public readonly record struct ListenerPose(Vector3 Position, Vector3 Forward, Vector3 Up);

/// <summary>
/// The host's <see cref="HostSink"/> over the software mixer: script <c>SfxRef.nplay/stop</c> (menu clicks, garage jack loop), the
/// <c>Sound.*</c> volumes/mode mirrored from the Ui area's <c>SoundState</c>, the listener from the camera, per-car sound from
/// <see cref="IVehicleAudioTelemetry"/>, and the mixed result written to an <see cref="OutputSink"/> (WAV file for headless runs,
/// <c>OpenAlSink</c> on a device). <see cref="Update"/> renders exactly <c>dt</c> seconds of audio per host frame.
/// </summary>
public sealed class GameAudioSink : HostSink, IDisposable
{
    private readonly AudioMixer _mixer;
    private readonly IClipProvider _clips;
    private readonly OutputSink _output;
    private readonly MixerUiSoundBackend _backend;
    private readonly Dictionary<object, VehicleSlot> _vehicles = [];
    private readonly float[] _block = new float[4096 * 2];
    private IGameWorld? _world;
    private SoundResolver? _resolver;
    private double _frameDebt;
    private readonly float[] _lastVolume = [float.NaN, float.NaN, float.NaN];

    public GameAudioSink(AudioMixer mixer, IClipProvider clips, OutputSink output, VehicleAudioAssets? assets = null)
    {
        _mixer = mixer;
        _clips = clips;
        _output = output;
        Assets = assets ?? VehicleAudioAssets.Synthetic();
        _backend = new MixerUiSoundBackend(mixer, clips);
    }

    public AudioMixer Mixer => _mixer;

    /// <summary>The UI sound backend (Sound.* natives and SfxRef.nplay land here).</summary>
    public IUiSoundBackend Backend => _backend;

    public VehicleAudioAssets Assets { get; set; }

    /// <summary><c>System.timeWarp</c>; set by <see cref="Attach"/> when the world is a <see cref="GameHost"/>.</summary>
    public Func<float> TimeWarp { get; set; } = () => 1f;

    /// <summary>Overrides the camera lookup (render/physics know the real active camera).</summary>
    public Func<ListenerPose?>? ListenerSource { get; set; }

    public SoundResolver? Resolver => _resolver;

    /// <summary>Calls routed to <see cref="Play"/> that started a voice / that could not (unknown id, missing wav).</summary>
    public int PlayedCount { get; private set; }

    public int FailedPlayCount { get; private set; }

    public double RenderedSeconds { get; private set; }

    public void Attach(IGameWorld world)
    {
        _world = world;
        _resolver = new SoundResolver(world.Resources.Archives);
        if (world is GameHost host)
        {
            TimeWarp = () => host.SystemArea.TimeWarp;
        }
    }

    /// <summary>
    /// <c>SfxRef.nplay(pos, a, pitch, vol, flags, owner)</c> routed by the host: the resource id resolves through the archive table, the
    /// call forwards to <see cref="IUiSoundBackend.Play"/>. Returns the voice id (0 = nothing started, as the scripts expect).
    /// </summary>
    public int Play(int soundId, Vector3? position, float a, float b, float c, int flags)
    {
        var key = _resolver?.Resolve(soundId);
        var id = key is { } k ? _backend.Play(k, position, a, b, c, flags, 0) : 0;
        if (id == 0)
        {
            FailedPlayCount++;
        }
        else
        {
            PlayedCount++;
        }

        return id;
    }

    public void Stop(int handle) => _backend.Stop(handle);

    /// <summary>Registers a car. <paramref name="setup"/> is polled each frame and the engine voices rebuilt when its signature changes (parts re-fill the tables on <c>updatevariables()</c>).</summary>
    public void AttachVehicle(object key, IVehicleAudioTelemetry telemetry, Func<EngineSfxSetup> setup, Func<int>? signature = null, VehicleAudioOptions? options = null)
    {
        RemoveVehicle(key);
        _vehicles[key] = new VehicleSlot(telemetry, setup, signature, options ?? new VehicleAudioOptions());
    }

    /// <summary>Registers a car whose tables live in a Parts <see cref="ChassisState"/> (needs <see cref="Attach"/> first).</summary>
    public void AttachChassis(ChassisState chassis, IVehicleAudioTelemetry telemetry, VehicleAudioOptions? options = null)
    {
        var resolver = _resolver ?? throw new InvalidOperationException("Attach(world) first");
        AttachVehicle(chassis, telemetry, () => resolver.SetupOf(chassis), () => SoundResolver.Signature(chassis), options);
    }

    public void RemoveVehicle(object key)
    {
        if (_vehicles.Remove(key, out var s))
        {
            s.Audio?.Dispose();
        }
    }

    public VehicleAudio? VehicleOf(object key) => _vehicles.TryGetValue(key, out var s) ? s.Audio : null;

    public void Update(float dt)
    {
        SyncUi();
        UpdateListener();
        var warp = TimeWarp();
        foreach (var s in _vehicles.Values)
        {
            var sig = s.Signature?.Invoke() ?? 0;
            if (s.Audio is null || sig != s.LastSignature)
            {
                s.Audio?.Dispose();
                s.Audio = new VehicleAudio(_mixer, _clips, s.Setup(), Assets, s.Options);
                s.LastSignature = sig;
            }

            s.Audio.Update(s.Telemetry, warp);
        }

        Render(dt);
    }

    /// <summary>Renders <paramref name="seconds"/> of audio into the output without touching any model (flush at the end of a run).</summary>
    public void Render(double seconds)
    {
        _frameDebt += seconds * _mixer.SampleRate;
        var frames = (long)_frameDebt;
        _frameDebt -= frames;
        RenderedSeconds += (double)frames / _mixer.SampleRate;
        while (frames > 0)
        {
            var n = (int)Math.Min(frames, _block.Length / 2);
            _mixer.Render(_block.AsSpan(0, n * 2));
            _output.Write(_block.AsSpan(0, n * 2));
            frames -= n;
        }
    }

    private void SyncUi()
    {
        var snd = _world?.Ui.Sound;
        if (snd is null)
        {
            return;
        }

        for (var ch = 0; ch < 3; ch++)
        {
            var v = snd.Volumes[ch];
            if (!v.Equals(_lastVolume[ch]))
            {
                _lastVolume[ch] = v;
                _backend.SetVolume(ch, v);
            }
        }

        _backend.Mode = snd.Mode;
        if (snd.MusicSet != _backend.MusicSet)
        {
            _backend.ChangeMusicSet(snd.MusicSet);
        }
    }

    private void UpdateListener()
    {
        var pose = ListenerSource?.Invoke() ?? CameraPose();
        if (pose is { } p)
        {
            _mixer.Listener.Position = p.Position;
            _mixer.Listener.Forward = p.Forward;
            _mixer.Listener.Up = p.Up;
        }
    }

    /// <summary>
    /// First live camera that has a placed node: position from the node (<c>Pos</c>), orientation from yaw/pitch (<c>Ori</c>; radians and
    /// the axis convention are GUESSED, left-handed Y up: forward = (sin yaw cos pitch, sin pitch, cos yaw cos pitch)).
    /// </summary>
    private ListenerPose? CameraPose()
    {
        if (_world is null)
        {
            return null;
        }

        foreach (var cam in _world.Ui.Cameras)
        {
            if (cam.Destroyed)
            {
                continue;
            }

            var node = _world.Resources.NodeOf(cam.Parent ?? cam.Object) ?? _world.Resources.NodeOf(cam.Object);
            if (node is null)
            {
                continue;
            }

            var yaw = node.Ori[0];
            var pitch = node.Ori[1];
            var fwd = new Vector3(MathF.Sin(yaw) * MathF.Cos(pitch), MathF.Sin(pitch), MathF.Cos(yaw) * MathF.Cos(pitch));
            return new ListenerPose(new Vector3(node.Pos[0], node.Pos[1], node.Pos[2]), fwd, Vector3.UnitY);
        }

        return null;
    }

    public void Dispose()
    {
        foreach (var s in _vehicles.Values)
        {
            s.Audio?.Dispose();
        }

        _vehicles.Clear();
        _output.Dispose();
    }

    private sealed class VehicleSlot(IVehicleAudioTelemetry telemetry, Func<EngineSfxSetup> setup, Func<int>? signature, VehicleAudioOptions options)
    {
        public IVehicleAudioTelemetry Telemetry { get; } = telemetry;

        public Func<EngineSfxSetup> Setup { get; } = setup;

        public Func<int>? Signature { get; } = signature;

        public VehicleAudioOptions Options { get; } = options;

        public VehicleAudio? Audio { get; set; }

        public int LastSignature { get; set; }
    }
}
