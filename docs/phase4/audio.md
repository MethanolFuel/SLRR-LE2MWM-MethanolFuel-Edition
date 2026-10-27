# Audio area: software sound engine (`src/Slrr.Audio`)

Phase 4. A managed WAV loader, a software mixer, the RPM/load engine-sound model, one-shot SFX and an output abstraction.
References only `Slrr.Core` and `Slrr.Formats` (rpk reader); the tests also use `Slrr.Decompiler` to scrape engine tables.
Labels: **M** = measured from the shipped data or from running our code, **G** = guessed (no evidence beyond names/values).

Build/test in isolation: `dotnet test src/Slrr.Audio/Audio.sln -warnaserror` (57 tests; corpus tests pass vacuously without game data).

## Files

| File | Contents |
|---|---|
| `WavFile.cs` | `WavClip` (float, de-interleaved) + RIFF reader (PCM 8/16/24/32, float 32/64, EXTENSIBLE, `smpl` loops, `cue `, padded/unknown chunks, truncated data) + 16-bit writer. |
| `SoundCatalog.cs` | `SoundDef` (the rpk sound entry: `sourcefile`, `volume`, `instances`, `mindist`, `maxdist`, `flags`) and `SoundCatalog.Scan` over every `.rpk`. |
| `Mixer.cs` | `AudioMixer`, `Voice`, `PlayParams`, `Listener`, `SoundChannel`. |
| `EngineSound.cs` | `SoundKey`, `IClipProvider` (+ `CatalogClipProvider`, `MapClipProvider`), `EngineSfxItem/Setup`, `EngineSoundModel`, `SfxTableScraper`. |
| `OneShots.cs` | `SkidSound`, `ImpactSound`, `GearWhine`. |
| `Sinks.cs` | `IAudioSink`, `WavFileSink`, `NullSink`, `AudioPump`. |
| `UiSoundBackend.cs` | `IUiSoundBackend` (the adapter surface for the Ui area's `Sound`/`SfxRef` natives) + `MixerUiSoundBackend`. |
| `Spectrum.cs` | FFT, `PeakFrequency` (used by tests and `audio inspect`), `Synth` (sine, noise, whine fallback). |
| `../Slrr.Audio.OpenAL` | `OpenAlSink` (Silk.NET.OpenAL 2.22.0). **Builds, untested: no audio device in the build environment.** `OpenAlSink.TryCreate` returns null without a device. Separate project so the core has no native dependency. |
| `tools/Slrr.Cli/CmdAudio.cs` | `audio list | inspect | engines | rev | demo`. Needs `ProjectReference` to `src/Slrr.Audio` in `Slrr.Cli.csproj` and `"audio" => CmdAudio.Run(args[1..], o, e)` in `Commands.cs` (left to the lead). |

## What the data says

* **165 .wav files** found under the install root (147 outside `MW_Mod/` and `DestructionTools/`; the task brief said 156), all integer PCM, all decode (M).
  Formats: 22050 Hz mono 16-bit x 80, 44100 Hz mono x 34, 44100 Hz stereo x 27, 11025 Hz mono x 13, 32000 Hz x 4, and 7 eight-bit files (M).
  Extra chunks: `PAD `, `JUNK`, `LIST`, `fact`, `cue `, `DISP`, `smpl`.
* **Loop points:** 7 files carry an `smpl` chunk but only `sound\wav\surface\Slip.wav` declares a loop (5799..9683); the other six have
  zero loops (base-note metadata only). Looping is therefore decided by the caller (`SFX_LOOP`, entry `flags`), not by the files (M).
* **Sound entries:** `sound.rpk` has 43 entries, 33 are sounds (kind 6, text `key value` lines, same format as texture entries); 155 sound
  entries exist over all archives (engines' own rpks, `frontend.rpk`, `objects.rpk`, car rpks, mod rpks). Keys seen: `sourcefile`
  155, `volume` 133, `instances` 133 (values 4 or 10), `mindist` 79 (1..20), `maxdist` 73 (only `police_siren`: 500), `flags` 35
  (value 1 on the Police_* entries) (M). `flags 1` = loop is G.
* **11 entries point at files that are not shipped:** `sound.rpk` `transmission_fwd/rev` (`engine\SIKIT.WAV`) and `roll`
  (`_old\Data\Sound\DUGATTYU.WAV`), the three `ignition.wav` of the engine rpks, `Nonus` 1000/2200/3300RPM, `frontend camera`,
  `MW_Mod Jack_Move` (M). `GearWhine` therefore has a synthesised fallback (`Synth.Whine`).
* **Engine sound ids:** in an engine rpk (e.g. `Baiern_Emer.rpk`) ids `0xA0..0xAA` = `engine_IDL\{0900..6000}RPM.wav`,
  `0xAB..0xB5` = `exhaust_WOT\{0900..6000}RPM.wav`, `0xB6` = `FX\N2O_trigger.wav`, `0x1000` = ignition (M). Scripts name them
  `parts.engines.Baiern_Emer:0x00A2r`; `SfxTableScraper` maps that to `("baiern_emer.rpk", 0xA2)` (G for non-engine paths).
* **SfxTable rows** (`addItem(sound, rpm, rpmMin, rpmMax, volMin, volMax)`): 14 engine-part classes call it, 79 rows in total
  (the "552 sites" in `java.sound.md` count unrelated `Menu.addItem`). Every one clears table 1 and fills table 0 (engine) and table 2
  (exhaust, accompanied by `setSfxExhaustMinVol(0.6|0.9)`) (M). Row values: rpm 900..6200, `rpmMin` 500..4000, `rpmMax` 1350..18000,
  volumes 0.2..1.0; 9 rows have a reference rpm (`900*2`, `2500*2`, `1500*2`) above `rpmMax` (M).
* **Sample content vs file label (M):** FFT of the 3000/3500/5000/5500 RPM files peaks at about `label/31` Hz, so the labels are real recording
  rpm. The table's `rpm` argument is NOT the label (id `0xA2` = 2000RPM.wav is registered with rpm 3500; `0xAA` = 6000RPM.wav with 5000), so the
  playback rate cannot be derived from the file name; it must come from the row (open question below).
* `Chassis` defaults (M): `rpm_trans_fwd/rev = 1400`, `sfx_starter_rpm = 200`, `SFX_trans_fwd/rev/ignition = sound:0x7/0x9/0xA`.
* `SfxRef.nplay(pos, a, pitch, vol, flags, ownerId)` call sites: `a` is always 0, `pitch` is passed as `System.timeWarp - 0.5` by the garage
  lift, `vol` is 1.0 everywhere, flags `SFX_LOOP=1`, `SFX_NOAUTOSTOP=0x80000000` (M for values, G for roles of `a`/`pitch`/`vol`).

## Models

| Behaviour | Implementation | Label |
|---|---|---|
| Playback | 4-point Hermite interpolation, per-block linear ramps of left/right gain and of rate (no zipper noise), voice cap 64 (quietest stolen), per-resource `instances` cap (oldest stolen) | cap values M, algorithms n/a |
| Fades | linear fade-in/out per voice; `Stop(seconds)` frees the voice at silence | n/a |
| Channels | Effects 0 / Music 1 / Engine 2 volumes (`Sound.setVolume`), master | ids M |
| 3D | listener pose, inverse-distance clamped between `mindist` and `maxdist` (extra linear fade over the last 20 % of `maxdist`), equal-power pan on the sine of the azimuth, no Doppler, left-handed right-vector | **G** (original uses DirectSound3D/EAX) |
| Engine rate | `rate = rpm / refRpm`, clamped 0.25..4 | **G** |
| Engine gain | `lerp(volMin, volMax, (rpm-min)/(max-min))` times quarter-sine rise `min..ref` and quarter-cosine fall `ref..max` (neighbours crossfade), 0 outside `min..max` | **G**; the ranges/overlaps are M |
| Engine load | table 0 x `0.7+0.3*load`; table 2 (exhaust) x `lerp(exhaustMinVol, 1, load)` | **G** (names `engine_IDL`/`exhaust_WOT`, `setSfxExhaustMinVol`) |
| Skid | gain = entry volume x smoothstep(slip 0.15..0.8) x 0.5; pitch 0.85..1.25 with speed 0..40 m/s | parameters M (volume 2, mindist 20, instances 4), curves **G** |
| Impact | gain = entry volume x sqrt(closing speed / 20 m/s), pitch down to 0.85, instance cap from the entry | **G** curves |
| Gear whine | pitch = `|driveRpm| / rpm_trans` (1400), gain grows to full at 2x ref and with load | **G** (sample missing) |

## Measured by the tests

* Pitch: a 1000 Hz sine at pitch 0.5/1/1.5/2 shows a spectrum peak within 0.5 % of 500/1000/1500/2000 Hz.
* Engine model with three crossfading layers (native pitch `ref/10` Hz): at 800, 1200, 1800, 2600, 3500, 4800, 6200, 7000 rpm the peak is
  within 1.5 % of `rpm/10` Hz; a 30 s continuous sweep 800..7000 stays within 5 % at every probe.
* Real data: `Baiern_Emer\exhaust_WOT\3000RPM.wav` (`0xAF`, 95 Hz) pitched through the model at 2250/3000/4500/6000 rpm scales the spectrum peak by
  0.75/1/1.5/2 within 3 %.
* Shipped engine: Baiern 6SFi block (5 rows) sweep 800 to 7000 rpm for 12 s at 44.1 kHz: exactly 12.000 s, peak 0.61 (no clipping), rms 0.19;
  MC Prime SuperDuty (8 rows): peak 0.93, rms 0.25.
* Mixer: one-shot duration, half pitch doubles the length, loop seams without clicks, `smpl` region honoured, linear gain, pan sign/equal power,
  inverse-distance ratio 2 m vs 8 m = 0.25, fade monotone, voice and instance caps, determinism.

## Interfaces for other areas

* **Ui area (`Sound`/`SfxRef` natives):** implement/forward to `IUiSoundBackend`. `SoundState` volumes <-> `GetVolume/SetVolume`; `changeMusicSet`, `nextTrack`,
  `prevTrack`; `getMode/setMode/has3DHardware/hasMixHardware`; `nplay(pos, a, pitch, vol, flags, ownerId)` -> `Play(SoundKey, ...)` (returns the play id, 0 if
  the sound is unavailable); `stop(id)` -> `Stop`; `cache()` -> `Precache`. A `ResourceRef` to a sound resolves to `SoundKey(<rpk file name lower-case>, id)`;
  `MixerUiSoundBackend` is a ready implementation. No music files ship in this install, so music calls only record state.
* **Parts area:** `SfxTableState.Items` (`SfxItem(Sound, A, B, C, D, E)`) maps 1:1 onto `EngineSfxItem(SoundKey, RefRpm=A, RpmMin=B, RpmMax=C, VolMin=D, VolMax=E)`;
  `ChassisState.SfxExhaustMinVolume` -> `EngineSfxSetup.ExhaustMinVolume`. The adapter resolves `SfxItem.Sound` (a ResourceRef VmObject) to a `SoundKey`.
* **Physics/vehicle area:** per frame call `EngineSoundModel.Update(rpm, load, position)`, `SkidSound.Update(slip, speed, position)`,
  `ImpactSound.Play(mixer, clip, def, closingSpeed, position)`, `GearWhine.Update(driveRpm, load)`; set `mixer.Listener` from the camera and pump
  `mixer.Render` into an `IAudioSink` (WAV for headless, `OpenAlSink` on a device).

## CLI

```
audio list [root]                    165 wavs with format/duration/peak/chunks/loops + all rpk sound entries (MISSING FILE flags)
audio inspect <wav|archive.rpk:0xID> format, loops, cues, dominant frequency
audio engines [root]                 the 14 engine blocks' tables (79 rows)
audio rev out.wav [--engine Baiern_Emer] [--from 800] [--to 7000] [--seconds 12] [--rate 44100]
audio demo out.wav                   impacts at 5/12/20 m/s from left to right over a panning tyre skid, 6 s
```

## Open questions

1. True meaning of the table's `rpm` argument (the file labels disagree with it, see above) and whether the original ramps volume at the range edges the way the
   quarter-sine crossfade does. Needs a trace of the exe's sound update (xrefs near 0x58c8ce..) or a recording of the original.
2. `Sound_3D_HW`/`Sound_Mix_HW` behaviour, DirectSound3D rolloff and EAX use (`eax.dll` ships) are not reproduced.
3. Role of the three floats of `nplay` and of `flags 1` in the entries; the music source (playlists) is not in the shipped data.
4. `SIKIT.WAV` (gear whine) and `ignition.wav` are referenced but not shipped; `sfx_starter_rpm` (200) use is unmodelled beyond the constant.
5. Real-device OpenAL output has never been run.
