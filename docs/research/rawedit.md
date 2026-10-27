# `java.lang.RawEdit`: catalog, address map and typed replacement API

Phase 3 static research. Inputs: all 1,692 `.class` files, the 138 `.java` sources, `StreetLegal_Redline.exe`,
`Slrr_GI.exe` and the strings of `SlrrExhaustiveSettingsEditor.exe` (read as data). **Nothing was executed.**
Reproduce with `python3 -I tools/analysis/rawedit_catalog.py` (needs `pefile`, `capstone`). Machine-readable
output: [`rawedit-addresses.tsv`](rawedit-addresses.tsv), [`rawedit-callsites.tsv`](rawedit-callsites.tsv),
[`rawedit-chassis-offsets.tsv`](rawedit-chassis-offsets.tsv), [`rawedit.json`](rawedit.json).

Confidence: **H** = verified from two independent sources in the binary (e.g. config loader key -> address, or a native
reading and writing the same field). **M** = one solid source (editor tooltip or source comment) plus code references.
**L** = name only. Facts and hypotheses are labelled.

## 1. What RawEdit is (facts)

* `system/Scripts/lang/RawEdit.class` (source `src/RawEdit.java`) has five natives, registered only by
  `Slrr_GI.exe`: `getI(I)I`, `getF(I)F`, `setI(II)I`, `setF(IF)I`, `CentCur(I)I`, plus four scripted wrappers
  (`GetIntFromRawAddress`, `GetFloatFromRawAddress`, `SetIntOnRawAddress`, `SetFloatOnRawAddress`) and 69 static
  `IntValueAddress` / `FloatValueAddress` objects holding absolute addresses (class and source agree for all 69).
* `getI`/`getF` (Slrr_GI VA `0x475953`/`0x47596f`) parse one int and do `mov eax,[eax]` / `fld [eax]`; `setI`/`setF`
  (`0x47598b`) do `mov [eax],ecx` and return the address. **No range or protection check.** `CentCur` centres the
  mouse cursor (source comment), registered from `0x457e1a`.
* Slrr_GI makes 371 calls to the native registrar `0x416b00` (Redline 360). The Phase 2 table found 10 extra; this
  analysis found the 11th: **`Chassis.getIndexedAddr(I)I`** is registered from the `.code` cave (stub `0x7c04a1`,
  string `"getIndexedAddr"` at `0x618714`). It returns `index + [obj+0x4c]` for the looked-up native object, i.e. the
  address of a field in the native chassis data; scripts then pass it to `RawEdit.getI/setI`.
* The 69 addresses are not an arbitrary memory model: they are **named global tuning variables of the exe**. 30 are
  vanilla globals (25 are stored by the config loader at `0x427bf0`, which maps each `GetConfigData` key to one of them;
  3 are resource counters and 2 are frame/game clocks updated by vanilla code). 39 are **mod-created variables that live in repurposed
  `.data` string space**: Slrr_GI overwrote rarely used error/diagnostic format strings
  (`"Could not get level Description of D3DX texture!"`, `"Error in IndexBufferSlots::...release count!=0"`,
  `"Using offscreen surfaces and StretchCopy()"`, `"Not an RoQ file."`, `"DX8SDK Samples Path"`, ...) with floats and
  ints (16 `.data` ranges, 575 bytes differ between the two exes) and added code (125 `.text` ranges, 2,128 bytes, plus
  1,372 non-zero bytes in the new 64 KB `.code` section, used up to `0x7c0660`) that multiplies vanilla values by
  these globals. Verified detail: Redline code still pushes those string addresses (`Redline refs` column), so in
  Slrr_GI the corresponding error paths would print float bytes; *hypothesis*: those paths never run in normal play.
  Example: `0x618710` was the 4-byte `"rb"` mode string of an `fopen` in the texture/RoQ loader (`0x4fa13b`); the mod now
  stores `WheelFrictionMult` (0.8f) there.
* `SlrrExhaustiveSettingsEditor.exe` (a .NET editor, not run) patches these defaults **into `Slrr_GI.exe` itself**
  (it asks for the exe path and says "Non valid exe will not save"). Its UTF-16 tooltips are the author's own
  descriptions and were mapped to variables by control order (the `M` rows). It also knows three settings absent from
  `RawEdit.java`: `FreeMemInGarage`, `AutoShiftDeadTimeShiftingUp`, `AutoShiftDeadTimeShiftingDown` (addresses not
  recovered here; see section 6).

## 2. Use catalog (facts)

| Where | What |
|---|---|
| Shipped `.class` | exactly **2 files** mention RawEdit: `RawEdit.class` itself and `parts/scripts/bodypart/Chassis.class`. Chassis has **8 call sites** (bodies 134 and 172, lines 559/566/577/584/1245/1252/1263/1270), all `RawEdit.setF(whl.ptr + 200, FrontMaxSteer | RearMaxSteer)`, compiled as a by-name call (`MNAME setF`) on `CLASS RawEdit`. |
| `.java` sources | 37 matching lines in 5 files: `Chassis.java` (the same 8 `setF` calls, `getIndexedData`/`getIndexedAddr` declarations, 21 `IndexedData_*` offset constants, one `getIndexedData(IndexedData_RPM)` use at line 1449), `HeadlightController.java` (line 110 `File.exists("system\\Scripts\\lang\\RawEdit.class")` used as feature flag `hasExhaustiveBits`, line 239 `getIndexedData(IndexedData_RPM) > 200`), `SfxTable.java` (5 commented lines: `AddItemPtr` result `+0x10..+0x20`), `src/Catalog.java` and `sl/Scripts/game/src/Catalog.java` (8 commented lines each: pin `mem_vertex_max`/`mem_texture_max` to 0 on entering the catalog and restore on exit; they also name `RawEdit.engine_help_[Un]ForceMinAllMemLimits`, which no longer exists in `RawEdit.class`). |
| Not found | no script reads or writes any `Address_*` constant outside RawEdit.class. The 69-address table is infrastructure for the external settings editor and for future scripts. |
| Source vs class | the `.java` files are newer than the shipped `.class` (`HeadlightController` has no shipped `.class` at all; `getIndexedData` callers exist only in source). Both views are listed in `rawedit-callsites.tsv`. |

Object-relative accesses (not absolute addresses), proposed replacements in section 4:

| Expression | Meaning | Evidence | Conf |
|---|---|---|---|
| `whl.ptr + 0xC8` (float) | per-wheel steering value | `WheelRef.getSteer` (Redline `0x440e50`) reads `[obj+0xC8]` and `WheelRef.setSteer` (`0x440e90`) writes `[obj+0xC8]` (the same object lookup `0x45d910`/`0x42ab50`). The script writes `FrontMaxSteer` (default 1.48 rad) / `RearMaxSteer` (default 0.0) there. No script calls `WheelRef.setSteer`, so the field is used as the wheel's steering limit/angle (*hypothesis*: engine treats it as max angle). | H field identity, M role |
| `chassis.getIndexedAddr(k)` | address of float/int field at `chassisData + k` | table of 21 offsets in `rawedit-chassis-offsets.tsv` (Mileage `0x20FC`, RPM `0x1F4C`, GearNumber `0x1DF8` (int), pedals, steering, NOS level, engine wear, 4 dashboard LEDs at `0x3AC..0x3DC`, Flags `0x70`, SpeedLimit `0x2104`) | M (names/offsets from source) |
| `soundBase = SfxTable.AddItemPtr(...)`, `+0x10..+0x20` | fields of the new sound item (`+0x10` pitch, others unknown) | only in comments | L |

## 3. Address map (69 addresses)

Columns: Redline file value (or the printable text found at/next to the address when the mod overwrote strings), GI
default (the value patched into the file), config key (the `GetConfigData` key whose loader stores into the address in
Redline, verified), references in Redline / in GI (of which in the `.code` cave). For mod variables the "Redline refs"
are pushes of the *old string*, not uses of the variable. Where several Redline functions touch an address the TSV
lists them in `redline_sites` / `gi_sites` (`rawedit.json`). Cave sites are labelled with the vanilla function they return
into, e.g. collision multipliers return into `0x496050` (collision response), wheel multipliers into `0x44fa70`
(large vehicle step), `0x43ba80`, `0x44e2d0`, spring variables into `0x490dc0` / `0x491d50`, AI and auto-shift knobs sit
in the patched vehicle-control function `0x474b00`.

| Name | Addr | Type | Redline value or nearby text | GI default | Config key | Redline / GI refs (cave) | Conf | Proposed property |
|---|---|---|---|---|---|---|---|---|
| I_SoftwareFrustrumCullReturnOverwrite | 6185bc | int | `Using offscreen surfaces and StretchCopy()` (text) | 4 | - | 0 / 2 (0) | M | Graphics.ObjectsBehindCameraUnloadMode |
| I_DisableFlop | 6185d0 | int | `Could not get level Description of D3DX texture!` (text) | 1 | - | 0 / 3 (0) | M | Damage.DoorsOpenOnCollision |
| I_cfg_mem_instance_current | 765f60 | int | BSS | BSS | - | 8 / 8 (0) | H | ResourceBudget.InstancesInUse |
| I_cfg_mem_instance_max | 618d5c | int | 2048 | 1000 | mem_instance_max | 2 / 1 (0) | H | ResourceBudget.InstanceMax |
| I_cfg_mem_instance_min | 618d60 | int | 1024 | 0 | mem_instance_min | 2 / 1 (0) | H | ResourceBudget.InstanceMin |
| I_cfg_mem_sound_max | 618d64 | int | 8388608 | 1048576000 | mem_sound_max | 2 / 1 (0) | H | ResourceBudget.SoundMax |
| I_cfg_mem_sound_min | 618d68 | int | 6291456 | 0 | mem_sound_min | 2 / 1 (0) | H | ResourceBudget.SoundMin |
| I_cfg_mem_texture_current | 65c234 | int | BSS | BSS | - | 11 / 11 (0) | H | ResourceBudget.TextureBytesInUse |
| I_cfg_mem_texture_max | 618d54 | int | 134217728 | 1048576000 | mem_texture_max | 2 / 4 (0) | H | ResourceBudget.TextureMax |
| I_cfg_mem_texture_min | 618d58 | int | 8388608 | 0 | mem_texture_min | 2 / 1 (0) | H | ResourceBudget.TextureMin |
| I_cfg_mem_vertex_current | 65bd44 | int | BSS | BSS | - | 13 / 13 (0) | H | ResourceBudget.VertexBytesInUse |
| I_cfg_mem_vertex_max | 618d4c | int | 131072 | 1048576000 | mem_vertex_max | 2 / 4 (0) | H | ResourceBudget.VertexMax |
| I_cfg_mem_vertex_min | 618d50 | int | 32768 | 0 | mem_vertex_min | 2 / 1 (0) | H | ResourceBudget.VertexMin |
| I_cfg_skidmarks_max | 618bf4 | int | 256 | 8200 | skidmark_max | 2 / 1 (0) | H | ResourceBudget.SkidmarksMax |
| I_resource_loadrate | 618d6c | int | 16 | 20 | resource_loadrate | 3 / 2 (0) | H | ResourceBudget.LoadsPerFrame |
| I_AISlideIgnoreMod | 6185e8 | int | `Could not get level Description of D3DX texture!` (text) | 172 | - | 0 / 1 (0) | M | Ai.SlideIgnoreMod |
| I_DisableCruiseSteerOverWrite | 618038 | int | `Error in ~IndexBuffer:  release count!=0` (text) | 0 | - | 0 / 1 (1) | L | Drivetrain.CruiseSteerOverwriteDisabled |
| F_ClutchOnInAutoShiftDeadTime | 618610 | float | `Could not get level Description of D3DX texture!` (text) | 0.5 | - | 1 / 2 (0) | M | Drivetrain.AutoShiftClutchDuringShift |
| F_ThrottleOnInAutoShiftDeadTime | 618614 | float | `Could not get level Description of D3DX texture!` (text) | 0.15 | - | 0 / 1 (0) | M | Drivetrain.AutoShiftThrottleDuringShift |
| F_CarDeformationOnCollisionMult | 6185c4 | float | `Using Dynamic Textures` (text) | 0.8 | - | 0 / 2 (0) | M | Damage.BodyDeformationMult |
| F_CarDeformationOnCollisionMultMinus | 6185c0 | float | `Using offscreen surfaces and StretchCopy()` (text) | -0.8 | - | 0 / 2 (0) | M | (derived: -BodyDeformationMult) |
| F_CarPartDetachmentMult | 6185c8 | float | `Using Dynamic Textures` (text) | 0.3 | - | 0 / 2 (0) | M | Damage.PartDetachmentMult |
| F_CarChassisDeformationOnCollisionMult | 6185cc | float | `Could not get level Description of D3DX texture!` (text) | 0.4 | - | 0 / 1 (0) | M | Damage.ChassisSlotDeformationMult |
| F_WearMultEngine | 6185d4 | float | `Could not get level Description of D3DX texture!` (text) | 5 | - | 1 / 2 (0) | M | Wear.EngineWearMult |
| F_WearMultTyre | 6185e0 | float | `Could not get level Description of D3DX texture!` (text) | 0.6 | - | 0 / 2 (0) | M | Wear.TyreWearMult |
| F_TrafficFlatBonus | 6185dc | float | `Could not get level Description of D3DX texture!` (text) | -0.13 | - | 0 / 1 (0) | M | Ai.TrafficHeightOffset |
| F_AIOverSteering | 6185ec | float | `Could not get level Description of D3DX texture!` (text) | 1 | - | 0 / 1 (0) | M | Ai.OverSteering |
| F_AIOverBreaking | 6185f0 | float | `Could not get level Description of D3DX texture!` (text) | 1 | - | 0 / 1 (0) | M | Ai.OverBraking |
| F_AIOverThrotlling | 6185f4 | float | `Could not get level Description of D3DX texture!` (text) | 1.1 | - | 0 / 1 (0) | M | Ai.OverThrottling |
| F_cfg_deformation | 611360 | float | 1 | 1.5 | deformation | 5 / 4 (0) | H | Damage.OverallDeformation |
| F_cfg_engine_inertia_factor | 60e028 | float | 4 | 5 | engine_inertia_factor | 2 / 1 (0) | H | Drivetrain.EngineInertiaFactor |
| F_cfg_external_damage | 611358 | float | 1 | 0.7 | external_damage | 3 / 2 (0) | H | Damage.ExternalDamageScale |
| F_cfg_internal_damage | 61135c | float | 1 | 0.3 | internal_damage | 10 / 7 (0) | H | Damage.InternalDamageScale |
| F_cfg_particle_density | 618948 | float | 1 | 1 | particle_density | 7 / 6 (0) | H | Graphics.ParticleDensity |
| I_bDrawHeadlightRays | 6187bc | int | 1 | 1 | headlight_rays | 2 / 2 (0) | H | Graphics.DrawHeadlightRays |
| I_bDrawHeadlightFlares | 6187c0 | int | 1 | 1 | flares | 2 / 2 (0) | H | Graphics.DrawHeadlightFlares |
| I_ShadowSize | 6187b8 | int | 256 | 256 | shadow_size | 6 / 6 (0) | H | Graphics.ShadowSize |
| I_Shadows | 6187b4 | int | 2 | 2 | shadows | 5 / 5 (0) | H | Graphics.ShadowMode |
| I_TextureSize | 6188b4 | int | 4 | 4 | texture_size | 3 / 3 (0) | H | Graphics.TextureSize |
| F_cfg_ShadowDetail | 65aed0 | float | BSS | BSS | shadow_detail | 2 / 2 (0) | H | Graphics.ShadowDetail |
| F_cfg_ObjectDetail | 65c424 | float | BSS | BSS | object_detail | 6 / 6 (0) | H | Graphics.ObjectDetail |
| F_cfg_VideoGamma | 617728 | float | 1.3 | 1.3 | video_gamma | 4 / 4 (0) | H | Graphics.VideoGamma |
| F_WheelGroundFeedbackFactor | 60e020 | float | 2.8 | 11 | wheel_gndfeedback_factor | 2 / 2 (0) | H | Physics.WheelGroundFeedbackFactor |
| F_WheelBreakeFactor | 60e024 | float | 4 | 2.3 | wheel_brake_factor | 2 / 2 (0) | H | Physics.WheelBrakeFactor |
| F_LastFrameDrawTime | 63c528 | float | BSS | BSS | - | 5 / 5 (0) | M | Clock.LastFrameSeconds |
| F_GameTimeSeconds | 63c530 | float | BSS | BSS | - | 3 / 3 (0) | M | Clock.GameSeconds |
| F_Gravity | 6186e4 | float | `DX8SDK Samples Path` (text) | -9.8067 | - | 0 / 2 (0) | M | Physics.Gravity |
| F_CollisionForceMultMapObjects_1 | 6186ec | float | `DX8SDK Samples Path` (text) | 0.8 | - | 0 / 3 (3) | M | Physics.CollisionForceMapObjects[0] |
| F_CollisionForceMultMapObjects_2 | 6186f0 | float | `DX8SDK Samples Path` (text) | 0.8 | - | 1 / 4 (3) | M | Physics.CollisionForceMapObjects[1] |
| F_CollisionForceMultVehicle_1 | 6186f4 | float | `DX8SDK Samples Path` (text) | 1.2 | - | 0 / 3 (3) | M | Physics.CollisionForceVehicles[0] |
| F_CollisionForceMultVehicle_2 | 6186f8 | float | `Not an RoQ file.` (text) | 1.2 | - | 0 / 3 (3) | M | Physics.CollisionForceVehicles[1] |
| F_FrictionOnCollision_1 | 6186fc | float | `Not an RoQ file.` (text) | 1 | - | 1 / 4 (3) | M | Physics.CollisionFriction[0] |
| F_FrictionOnCollision_2 | 618700 | float | `Not an RoQ file.` (text) | 1 | - | 0 / 3 (3) | M | Physics.CollisionFriction[1] |
| F_WheelFrictionMult | 618710 | float | `Not an RoQ file.` (text) | 0.8 | - | 1 / 2 (1) | M | Physics.WheelFrictionMult |
| F_WheelFriction_XMult | 617fc4 | float | `Error in IndexBufferSlots::Add : release count!=` (text) | 1 | - | 2 / 3 (1) | M | Physics.WheelFrictionLateralMult |
| F_WheelSilictionMult | 617fc8 | float | `Error in IndexBufferSlots::Add : release count!=` (text) | 1 | - | 0 / 2 (2) | L | Physics.WheelSlipMult |
| F_WheelStiffnessMult | 618000 | float | `Error in IndexBufferSlots::ReleaseLast: release ` (text) | 0.7 | - | 0 / 2 (2) | M | Physics.WheelStiffnessMult |
| F_WheelRollResMult | 618004 | float | `Error in IndexBufferSlots::ReleaseLast: release ` (text) | 1 | - | 0 / 1 (1) | M | Physics.WheelRollResistanceMult |
| F_WheelBearingMult | 618008 | float | `Error in IndexBufferSlots::ReleaseLast: release ` (text) | 1 | - | 0 / 2 (2) | M | Physics.WheelBearingMult |
| F_WheelMaxLoadMult | 61800c | float | `Error in IndexBufferSlots::ReleaseLast: release ` (text) | 0.8 | - | 0 / 2 (2) | M | Physics.WheelMaxLoadMult |
| F_WheelLoadSmoothMult | 618010 | float | `Error in IndexBufferSlots::ReleaseLast: release ` (text) | 1 | - | 0 / 1 (1) | M | Physics.WheelLoadSmoothMult |
| F_NativeSpring_Var1 | 618014 | float | `Error in IndexBufferSlots::ReleaseLast: release ` (text) | 1 | - | 0 / 1 (1) | M | Physics.Spring.Hardness |
| F_NativeSpring_Var2 | 618018 | float | `Error in IndexBufferSlots::ReleaseLast: release ` (text) | 1 | - | 0 / 1 (1) | M | Physics.Spring.InverseRebound |
| F_NativeSpring_Var3 | 61801c | float | `Error in IndexBufferSlots::ReleaseLast: release ` (text) | 1 | - | 0 / 1 (1) | M | Physics.Spring.InboundInverseSoftness |
| F_NativeSpring_Var4 | 618020 | float | `Error in IndexBufferSlots::ReleaseLast: release ` (text) | 1 | - | 0 / 1 (1) | M | Physics.Spring.LengthAndBumpiness |
| F_NativeSpring_Var5 | 618024 | float | `Error in IndexBufferSlots::ReleaseLast: release ` (text) | 1 | - | 0 / 1 (1) | M | Physics.Spring.MaxInverseContraction |
| F_NativeSpring_Var6 | 618028 | float | `Error in IndexBufferSlots::ReleaseLast: release ` (text) | 3.86 | - | 0 / 1 (1) | M | Physics.Spring.CollisionRelated |
| F_NativeSpring_Var8 | 61802c | float | `Error in IndexBufferSlots::ReleaseLast: release ` (text) | 1 | - | 0 / 7 (7) | M | Physics.Spring.Length |
| F_CruiseBreakWhenOverLimit | 618034 | float | `Error in IndexBufferSlots::ReleaseLast: release ` (text) | 0.5 | - | 3 / 4 (1) | L | Drivetrain.CruiseBrakeWhenOverLimit |

Group totals: ResourceBudget 13, Graphics 10, Physics 24, Damage 8, Drivetrain 5, Ai 5, Wear 2, Clock 2.
Confidence: H 28, M 38, L 3. Mod defaults worth knowing (GI file values): Gravity -9.8067, vertex/texture/sound maxima 1,000 MiB, instance max 1000
(Redline: vertex 128 KB, texture 128 MB, instance 2048), `engine_inertia_factor` 5 (vanilla 4), `wheel_gndfeedback_factor` 11
(vanilla 2.8), `wheel_brake_factor` 2.3 (vanilla 4), `external_damage` 0.7, `internal_damage` 0.3, `deformation` 1.5,
`skidmark_max` 8200, `resource_loadrate` 20.

## 4. Proposed typed replacement (design, not implemented)

Principle (from `script-vm-natives.md`): a raw memory model cannot be supported and is not needed, because every address
is a named scalar. Implement the five natives as a *shim over a typed settings object*:

```csharp
// Engine/Settings: one immutable-by-default record per group, one mutable live instance owned by the engine.
public sealed class EngineSettings {
    public GraphicsSettings Graphics { get; }          // ShadowMode, ShadowSize, TextureSize, ShadowDetail, ObjectDetail,
                                                       // VideoGamma, ParticleDensity, DrawHeadlightRays/Flares, ObjectsBehindCameraUnloadMode
    public ResourceBudget Budget { get; }              // Vertex/Texture/Instance/Sound Min/Max, LoadsPerFrame, SkidmarksMax
                                                       // + read-only InstancesInUse, TextureBytesInUse, VertexBytesInUse
    public DamageSettings Damage { get; }              // External/Internal scale, OverallDeformation, BodyDeformationMult,
                                                       // PartDetachmentMult, ChassisSlotDeformationMult, DoorsOpenOnCollision
    public WearSettings Wear { get; }                  // EngineWearMult, TyreWearMult
    public AiSettings Ai { get; }                      // SlideIgnoreMod, OverSteering/Braking/Throttling, TrafficHeightOffset
    public PhysicsSettings Physics { get; }            // Gravity, CollisionForce{MapObjects,Vehicles}[2], CollisionFriction[2],
                                                       // Wheel* multipliers, Spring.{Hardness..Length}, WheelGroundFeedbackFactor,
                                                       // WheelBrakeFactor
    public DrivetrainSettings Drivetrain { get; }      // EngineInertiaFactor, AutoShift{Clutch,Throttle}DuringShift, CruiseBrakeWhenOverLimit
    public ClockView Clock { get; }                    // LastFrameSeconds, GameSeconds (read-only)
}
```

Rules:

1. **Single registry** `RawAddressMap`: `uint address -> (PropertyAccessor, type)` built from `rawedit-addresses.tsv`. `RawEdit.getI/F`
   and `setI/F` look the address up; type mismatch (`getI` on a float property) reinterprets the 32-bit pattern exactly as the
   exe would, so the existing scripts keep working. Unknown addresses: log once and return 0 / ignore the write
   (the original would crash or corrupt).
2. `Address_F_CarDeformationOnCollisionMultMinus` is derived (`-BodyDeformationMult`); make it a computed property and
   ignore direct writes (the source comment asks callers to keep both in sync).
3. **Defaults**: the engine ships two presets: `Vanilla` (Redline file values) and `MethanolFuel` (the GI values in the
   table). Scripts that previously relied on the patched exe get the second one. The config keys in the table
   (`mem_vertex_max`, `deformation`, ...) are the user-config names the original `GetConfigData` loader reads, so the same
   settings file should provide both; runtime `RawEdit` writes are session-only unless saved explicitly (the old editor patched the exe).
4. **Object-relative accesses** get named properties instead of offsets: `WheelRef.ptr + 0xC8` -> `WheelRef.Steer`
   (same field as `setSteer`/`getSteer`; so `RawEdit.setF(whl.ptr+0xC8, v)` can be routed to `WheelRef.setSteer(v)`),
   `getIndexedAddr(k)` -> `ChassisTelemetry` with the 21 named members of `rawedit-chassis-offsets.tsv` (`RPM`, `Mileage`,
   `GearNumber`, `Throttle`, ... read-only except `GearNumber`, `Flags`, `SpeedLimit`). Keep `getIndexedAddr` returning an opaque handle
   `(chassisId << 16) | offset` that the shim decodes when it later appears in `RawEdit.getI/setI`; `SfxTable.AddItemPtr` offsets are
   unused (comments) and need no support.
5. `File.exists("system\\Scripts\\lang\\RawEdit.class")` (HeadlightController feature flag) must keep returning true in the
   C# engine if the shim is present, otherwise `hasExhaustiveBits` flips and the headlight code takes its fallback path.
6. `RawEdit.CentCur(I)I`: centre the OS cursor on the client window and return the new x (source comment); trivial in the host window layer.
7. Add the settings to the Phase 4/5 consumers rather than the VM: damage/wear/AI/physics multipliers become parameters of those
   systems; only the shim knows addresses.

Suggested priority: (1) memory-budget group and Graphics (config-backed, H, already semantically plain);
(2) Damage, Wear, `engine_inertia_factor`, `wheel_*_factor`, Gravity (needed for Phase 4 parity numbers);
(3) collision/wheel/spring multipliers (M; only meaningful once the vanilla routines they scale are reimplemented);
(4) ChassisTelemetry and the steer field (needed by `Chassis.setupWheels` and HeadlightController).

## 5. Facts vs hypotheses

Facts: address list, class/source agreement, native semantics (unchecked loads/stores), the 8 shipped call sites,
config-key -> address pairs from the loader, GI vs Redline byte differences, `0xC8` identity with `getSteer`/`setSteer`,
`getIndexedAddr` registration, defaults.
Hypotheses: that overwritten error strings are never hit; roles of the physics multipliers (taken from names and the vanilla
functions they hook, not from reading the physics code); control-order mapping of editor tooltips to variables; that `0xC8` acts as max
steer angle; the opaque-handle approach for `getIndexedAddr`.

## 6. Gaps

* Addresses for `FreeMemInGarage`, `AutoShiftDeadTimeShiftingUp/Down` (editor-only) were not located; they are probably among
  the unlisted `.data` differences (`0x618d4e..0x618d6c` region and `0x60e020..0x60e02a` are mapped; `0x61abd8`, `0x61ac03` are
  banner strings).
* Cave functions were only labelled by the vanilla function they return into; their exact arithmetic (e.g. which collision
  component is scaled) was not decoded.
* Function boundaries come from direct-call targets (approximate); registered natives called via pointer are matched by start address only.
