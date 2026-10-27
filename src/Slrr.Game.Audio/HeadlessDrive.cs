using System.Numerics;
using Slrr.Audio;

namespace Slrr.GameAudio;

/// <summary>Result of a headless render: the stereo mix and per-frame bookkeeping.</summary>
public sealed record HeadlessDriveResult(WavClip Mix, ScenarioFrame[] Frames, int SampleRate)
{
    public double Duration => Mix.Duration;
}

/// <summary>
/// Renders the scripted drive (<see cref="DriveScenario"/>) through the real pipeline: telemetry -> <see cref="GameAudioSink"/> ->
/// <see cref="VehicleAudio"/> -> <see cref="AudioMixer"/> -> <see cref="WavFileSink"/>. Used by the tests and the <c>gameaudio drive</c> CLI.
/// </summary>
public static class HeadlessDrive
{
    public static HeadlessDriveResult Render(
        EngineSfxSetup setup,
        IClipProvider clips,
        VehicleAudioAssets assets,
        int sampleRate = 44100,
        string? wavPath = null,
        Action<ScenarioFrame, GameAudioSink>? onFrame = null,
        VehicleAudioOptions? options = null)
    {
        var frames = DriveScenario.Build();
        var mixer = new AudioMixer(sampleRate);
        var sink = new WavFileSink(sampleRate, wavPath);
        using var audio = new GameAudioSink(mixer, clips, sink, assets)
        {
            ListenerSource = () => new ListenerPose(new Vector3(0f, 1.2f, -4f), Vector3.UnitZ, Vector3.UnitY),
        };
        var telemetry = new ManualTelemetry();
        audio.AttachVehicle("player", telemetry, () => setup, null, options);
        foreach (var f in frames)
        {
            DriveScenario.Apply(f, telemetry);
            audio.Update(1f / DriveScenario.Hz);
            onFrame?.Invoke(f, audio);
        }

        return new HeadlessDriveResult(sink.ToClip(), frames, sampleRate);
    }
}
