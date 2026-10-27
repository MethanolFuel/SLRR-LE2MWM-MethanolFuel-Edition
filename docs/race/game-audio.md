# Game audio adapter (`src/Slrr.Game.Audio`)

Race wave. The in-game side of sound: the host's `IAudioSink`, the Ui area's `Sound`/`SfxRef` natives, the engine-sound model driven by vehicle
telemetry, tyre/impact/whine one-shots, listener from the camera, and the mix to a WAV (headless) or any `Slrr.Audio.IAudioSink` (OpenAL).
Labels: **M** = measured (exe disassembly, shipped data, or our code run), **G** = guessed.

Build/test in isolation: `dotnet test src/Slrr.Game.Audio/Slrr.Game.Audio.sln -warnaserror` (32 tests; corpus tests pass vacuously without game data).
References only Core, Formats, Vm, Audio, Engine.Resources/Parts/Ui and Game. Edits no existing project.

## The open question is settled: what the table's `rpm` argument means (M, exe static disassembly)

`SfxTable.addItem(sound, A, B, C, D, E)` registers at `0x442aa4 -> 0x4423e0`; the per-frame engine sound routine is `0x4518c0` and the table update it
calls is `0x48f0e0` (found from the `getSfxTable` native at `0x442210`: the three tables live in the car's sound block at `+0xf8`, `+0x340`, `+0x588`,
each 0x248 bytes = 16 rows x 0x24 + count at `+0x240` + a float rpm scale at `+0x244`, initialised to 1.0 at `0x43e423/0x45dc56` and never written by a script).

* `addItem` stores `1/A` (not A), then B, C, D, E; a 17th row is refused (`count < 0x10`, `0x442438`).
* Engine rpm fed to the tables: `rpm' = (|omega_engine| x 9.5493 + 1) x scale` (`0x451bc0`, constant `0x5f0c14`).
* Per row `i` with `B <= rpm' <= C`: **pitch = rpm' x (1/A) x System.timeWarp** (global `0x636448`, read by the `System.timeWarp` native at `0x47bf9c`);
  **gain = lerp(D, E, (rpm'-B)/(C-B)) x fade**; the play call (`0x48cfb0`) multiplies by the sound entry's `volume` and drops anything `<= 0.01`.
  `fade` = 1, or linear `(rpm'-B)/(C[i-1]-B)` while `rpm' <= C[i-1]` (fade-in over the overlap with the previous row), or `1-(rpm'-B[i+1])/(C-B[i+1])`
  once `rpm' >= B[i+1]` (fade-out into the next row; wins over the fade-in). Row order = insertion order.
* So the sample **file label never enters**: `A` is the engine rpm at which the sample is heard at its native pitch. `engine_IDL\2000RPM.wav` registered at
  `A = 3500` plays at pitch 0.571 at 2000 rpm and unshifted at 3500. The earlier Slrr.Audio rate (`rpm/RefRpm`) was already right; its quarter-sine edge
  fade and the `0.7+0.3 load` table-0 gain were guesses and are replaced here (they stay in Slrr.Audio, unused by this area).
* Table selection by throttle (`0x451bd7..0x451d98`): `|thr| <= 0.1` -> table 1 (if it has rows, else table 0) at 1.0; `0.1 < thr <= 0.5` with table-1 rows ->
  table 1 x (1-v) + table 0 x v, `v = (thr-0.1) x 2.5`; else table 0 at 1.0. Every shipped engine leaves table 1 empty, so **table 0 is always at full volume**.
* Exhaust table 2: only while `rpm' > 1`; volume `m + (1-m) x |thr|`, `m` = `setSfxExhaustMinVol` (default 0.1 at `0x446ca1`; shipped 0.6/0.9).
* While the starter flag (`vehicle+0x1dc8`) is set the engine tables are not driven; the ignition sound (`sound.rpk:0xA`, not shipped) plays at pitch `rpm' x tw / 200`
  (`sfx_starter_rpm`).
* Transmission whine: `x = drive_omega x 9.5493 + 1`; silent for `|x| <= 1`; `|x| < 50` is lifted to +50; forward sample (`sound.rpk:7`) if `x > 0` else reverse (`:9`);
  pitch `|x| x tw / rpm_trans_fwd|rev` (1400); volume `min(|T| x 1e-4, 1)` with `T = vehicle+0x1e24`. Structure M, physical meaning of `+0x1e14/+0x1e24` G.
* Not modelled: the `vehicle+0x1fc0` "simplified" path (`0x451a1a`: pitch from body speed for cars without a full engine model), and the 3D attenuation of the backend
  `0x5505e0` (it compares a squared distance against the constants 16 and 62500 and the volume against 0.01; roles G). The mixer's inverse-distance curve stays G.

## Files

| File | Contents |
|---|---|
| `MeasuredSfxTable.cs` | The formulas above as pure functions: `TableRpm`, `Evaluate(rows, rpm)`, `EngineTableVolumes`, `ExhaustVolume`, `Whine`. |
| `VehicleTelemetry.cs` | `IVehicleAudioTelemetry` (what the physics adapter feeds), `ImpactEvent`, `ManualTelemetry` (plain data implementation). |
| `VehicleAudio.cs` | One car's sound: engine/exhaust voices from the tables, starter, whine (synth fallback, sample missing), skid, impacts; `VehicleAudioAssets.FromCatalog`. |
| `SoundResolver.cs` | Runtime resource id / `ResourceRef` -> `SoundKey` (archive slot name + entry id); `ChassisState.SfxTables` -> `EngineSfxSetup` (the missing Parts-to-`EngineSfxItem` adapter). |
| `GameAudioSink.cs` | `Slrr.Game.IAudioSink` implementation: `nplay/stop` -> `MixerUiSoundBackend`, `SoundState` volumes/mode mirrored each frame, listener from the first live camera node (or `ListenerSource`), per-car updates, renders exactly `dt` seconds into a `Slrr.Audio.IAudioSink` (fractional frames carried). |
| `DriveScenario.cs`, `HeadlessDrive.cs` | The scripted 60 s telemetry and its render through the whole pipeline. |
| `tools/Slrr.Cli/CmdGameAudio.cs` | `gameaudio drive <out.wav> [--engine X] [--rate N] [root]`, `gameaudio formula [--engine X] [--rpm N] [--throttle T] [root]`. |

## Interface the physics adapter implements

```csharp
public interface IVehicleAudioTelemetry {
    bool Active; Vector3 Position;
    float EngineAngularSpeed;   // rad/s, crankshaft (exe vehicle+0x1e1c)
    float Throttle;             // 0..1 pedal (+0x1db4)
    bool StarterEngaged;        // (+0x1dc8)
    float DriveShaftAngularSpeed; // rad/s signed (+0x1e14)
    float DriveShaftTorque;     // N m (+0x1e24)
    float Speed;                // m/s
    int WheelCount; float WheelSlip(int); Vector3 WheelPosition(int);   // slip 0..1
    void DrainImpacts(ICollection<ImpactEvent> into);                   // collisions since last call (closing speed m/s, point)
}
```

## Integration steps for the lead

1. `Slrr.sln`: add `src/Slrr.Game.Audio` and `tests/Slrr.Game.Audio.Tests`. `tools/Slrr.Cli/Slrr.Cli.csproj`: `ProjectReference` to `src/Slrr.Game.Audio`;
   `Commands.cs`: `"gameaudio" => CmdGameAudio.Run(args[1..], o, e),`. (Until then `CmdGameAudio.cs` does not compile in Slrr.Cli; the test project compiles it.)
2. Composition: `var cat = SoundCatalog.Scan(new GameFileSystem(root)); var clips = new CatalogClipProvider(cat);`
   `var sink = new GameAudioSink(new AudioMixer(44100), clips, output, VehicleAudioAssets.FromCatalog(cat, clips));` with `output` = `new WavFileSink(44100, path)`
   (headless) or `OpenAlSink.TryCreate(...) ?? new NullSink(44100)`; pass `Audio = sink` in `GameHostOptions`. `Attach` picks up `System.timeWarp` from the `GameHost`.
   Dispose the sink at the end of a run (the WAV file is written on dispose).
3. Per car, after the physics adapter has a chassis: `sink.AttachChassis(chassisState, telemetry)`; the sink re-reads the Parts tables every frame
   (signature check) and rebuilds the voices when `updatevariables()` re-fills them. Remove with `RemoveVehicle(chassisState)`.
4. Optional: `sink.ListenerSource = () => new ListenerPose(camPos, camForward, up)` from Render's active camera (the default reads the first live Ui camera's resource node;
   `Ori` as yaw/pitch radians is G). Non-positional cockpit sound: `VehicleAudioOptions.Positional = false`.

## Measured by the tests

* Voice pitch equals `rpm'/A` on all 4229 active-row frames of the 60 s drive: max |pitch x A - rpm'| = 0.0005 (float rounding).
* Spectrum peak vs the telemetry rpm (sine rows with native `A/10` Hz, so every row must sound at `rpm'/10` Hz): idle 90.1 Hz (expected 90.1), rev-blip top 496.4 (497.0),
  5th-gear cruise 608.1 (608.1), post-crash idle 90.1 (90.1): all within 0.12 %.
* 60 s drive: 60.000 s, synthetic engine peak < 1 (no clipping), shipped Baiern 6SFi rows + real "slide"/"collision" samples: peak 0.62, rms 0.13 at 44.1 kHz stereo.
* Skid difference energy is zero at idle and large during braking/launch wheelspin; the impact difference is a hard onset at 56.4 s and zero before and after.
* Host: a booted `GameHost` with `GameAudioSink` resolves `sound.rpk:0x6` from its runtime id, mirrors `Sound.setVolume` into the mixer, and `Play`/`Stop` drive a real voice.

## Guessed

Skid/impact curves and levels (`SkidLevel 0.5`, `ImpactLevel 0.6`), mixer distance rolloff, synthesised whine tone (700 Hz, level 0.35), camera orientation convention, `MasterGain 0.5`,
the scripted drive numbers. Needs a physics adapter to prove the telemetry field roles (`+0x1e14`, `+0x1e24`, `+0x1dc8`).

## Open questions

1. Whether the original loops each row's sample unconditionally (the call at `0x48cfb0` is made every frame; looping is decided in the backend) - we loop.
2. Backend 3D attenuation (`0x5505e0`), EAX, and the `+0x1fc0` simplified-engine path (see above).
3. The roles of `Part.setSfxLoopParams(FF)` (`0x46a1b0`, registered, never called by scripts) and of `Chassis.setHornSFX/setNitroSFX` (horn pitch/index) are not wired.
4. The real OpenAL device path is still untested (no device in the build environment).
