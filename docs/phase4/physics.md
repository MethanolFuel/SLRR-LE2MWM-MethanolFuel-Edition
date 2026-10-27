# Phase 4 area: vehicle physics (`src/Slrr.Physics`)

Deterministic fixed-step vehicle dynamics fed by the data the parts layer captures, plus a port of the exe's engine
dyno model. **Parity with the original game is UNVERIFIED**: nothing here was compared with a run of the game. Labels:
**M** = measured (static disassembly of `StreetLegal_Redline.exe`, address given, or shipped data/scripts),
**G** = guessed (chosen to make the model work or plausible). No repo binary was executed.

Build/test in isolation: `dotnet build Slrr.Physics.sln -warnaserror && dotnet test Slrr.Physics.sln` (20 tests;
corpus tests pass vacuously without the game data).

## Files

| File | Contents |
|---|---|
| `Dyno/EngineCycleModel.cs` | Port of the calcDyno engine simulation (0x4b6670 ctor, 0x4b69a0 geometry, 0x4b6a80 mixture, 0x4b6bf0 per-rpm setup, 0x4b68c0 cycle reset, 0x4b7020 step, 0x4b7740 cycle, 0x4b77f0 table builder) and the getTorque/getHP lookup (0x46b0f0/0x46b210). |
| `Dyno/ExeDynoCalculator.cs` | `IDynoCalculator` for `Slrr.Engine.Parts` (field reader/driver 0x46aa20). Plug in: `new PartsWorld(dyno: new ExeDynoCalculator())`. |
| `Dyno/DynoDataDefaults.cs` | Field initializers of `DynoData.class` (M). |
| `Tyre.cs` | `TyreParams` (the 17-float array at wheel+0x1E8), `TyreModel.MagicFormula` (0x43ba80), load smoothing, load-sensitive stiffness, damage factor. |
| `Ground.cs` | `IGroundQuery` (ray cast; cross-area interface) and `FlatGround`. |
| `VehicleSpec.cs` | Engine/drivetrain/wheel/rollbar/aero data the simulation consumes. |
| `SpecBuilder.cs` | `ChassisParams` + `WheelState` + rollbars + cfg wheel/body lines + dyno tables → `VehicleSpec`. |
| `Vehicle.cs` | The simulation: rigid body, suspension, tyres, wheel spin, clutch, gearbox, open diff, brakes, drag. |
| `Scenarios.cs` | 0-100 km/h, top speed, 100-0 km/h, skidpad (R 40 m). |
| `Loading/CarLoader.cs` | Builds a shipped car headless through `Slrr.Engine.Resources` + `Slrr.Engine.Parts` with its own scripts (`addStockParts`, `updatevariables`, `forceUpdate`). |
| `tools/Slrr.Cli/CmdDrive.cs` | `drive` command (inside `#if SLRR_PHYSICS`, see Integration). |

## Engine dyno (M: port of the exe)

`DynoData.calcDyno(steps)` re-runs the native constructor, reads 33 script fields (cylinders, bore/2, stroke/2, Vmin,
valve areas/times, spark map, burn time, RPM_limit, turbo window, mixture, flow limits; `P_turbo_waste` only if the field
exists else = `P_turbo_max`; optional `intercooling`), then simulates one cylinder over crank angle:

* slider-crank volume, rod length 0.15 m fixed (+0x0C), clearance `Vmin`; ~231 steps per 720° cycle (dt = cycle·0.004329);
* adiabatic P·V^1.3 / T·V^0.3; intake/exhaust valves are square waves (area `in/out_max` when open, `in/out_min` leak),
  flow = pressure relaxation `k = min(dt·area·1000, 1)` with mass/temperature/heat mixing;
* linear burn from `spark·cycle` over `time_burn·(1 - min(rpm·5e-5, 0.9))` releasing `mixture_H` per kg of charge
  (nitro: `mixture_H·nitro_H`, intake T `293.15·nitro_cooling`);
* wall heat loss `q = min(dt/95, 1)·d·|d|`, d = T − 353.15 K. **The script field `T_loss` is never read** (95 is the ctor constant);
* turbo: boost = P_turbo_max·(1 − (|rpm·mul − opt|)/range) clamped to [0, P_turbo_waste], intake P·(1+b), T and ρ·√(1+b);
* per cycle: `torque = W·cyl·0.125`, `power = W·cyl·rpm/60`, `net = power − 2e-8·rpm³` W, `hp = net·0.001341`.
  Note the exe's own torque and power are not consistent (T·ω ≠ P): the displayed maxHP is ~28 % above T·ω;
* table: rpm = i·step for i = 1 .. (int)maxRPM/step − 1 (entry 0 = 0), step = maxRPM/steps; each point runs up to 11
  cycles until |mep_prev/mep − 1| < 0.05; maxima: maxTorque at max mep, maxHP at max net power;
* `getTorque(rpm, boost)` / `getHP` interpolate table 0 (boost ignored); **getHP returns watts** (CarInfo multiplies by
  0.001·1.341). A second (nitro) table is built only when `nitro_H − 1 > 1e-4` or `nitro_cooling − 1 > 1e-4`, after
  maxHP etc. were written. `torque`/`torque2` are only overwritten (with 1.0) for a temporary native. The chassis
  field reader copies the native's tables 0 and 1 (or 0 twice) into the car as (rpm, torque) pairs (0x448779..0x44894d)
  and sets two engine cells (+0xD0/+0xD4) to 1.0, which it scales by the wear factor (0x44865b, 0x4486fb); the chassis
  field `engine_torque` has no exe xref (M), so the runtime torque is the table (role of the 1.0 cells G).

Default DynoData (1.901 L, CR 10.51): 171.5 N m @ 1750, maxHP 92.6 @ 4250 (test `DefaultDynoDataGivesAPlausibleCurve`).
Not reproduced: x87 80-bit temporaries (storage is float, temporaries double), and `+0x198/+0x19C` (last fuel/air flow,
heap garbage before the first cycle in the exe, 0 here; only matters with `max_*_consumption > 0`).

## Wheel / tyre data (M unless noted)

| Item | Evidence |
|---|---|
| `setPacejka(i, v)` accepts i = 0..16 into wheel+0x1E8 | 0x441237 `cmp eax,0x11; jae` |
| named setters write the same array: sliction→[0], friction→[2], stiffness→[4], maxload→[11], frictn_x→[12], loadsmooth→[14]; rollres +0x74, bearing +0xF0 | setter natives 0x440f90..0x441207 |
| tyre force formula (magic formula, clamp ≥ 0), see `TyreModel.MagicFormula` | 0x43ba80 (0x43bb30 = unclamped) |
| runtime: p2 = surface friction (+0x50) × effective friction; p13 = surface rolling res (+0x54) + tyre rollres | 0x45628c..0x45629e |
| load fed to the formula: `ls·maxL·(2/π)·atan(πL/(2maxL)) + (1−ls)·min(L, maxL)`; then a further `(x+1)/2` factor of an unidentified x (not reproduced) | 0x4562a4..0x456302 |
| p16 > 0: p4 ·= (p15 + p16)/(Fs + p16) | 0x456313..0x456343 |
| lateral component multiplied by p12 (frictn_x) | 0x4563a4 |
| damage d scales friction, brake and engine torque by 1 − 0.6 d² (d < 1) else 0.1 | 0x440fcc, 0x4415ae, 0x4486fb |
| `setDamping(d)`: bump = d, rebound = 0.7 d | 0x4412e0 |
| engine inertia is multiplied by `engine_inertia_factor` (4 Redline / 5 GI); brake torque by `wheel_brake_factor`; `wheel_gndfeedback_factor` in the tyre path | 0x4558de, 0x455c6f, 0x4563cb (formulas around them not traced) |
| `engine_rpm_idle` stored as rad/s; `engine_mass·8000` stored | 0x4486b6, 0x4486d0 |

GUESSED in `Vehicle`: slip definitions (κ = (ωR − vx)/max(|vx|, 1 m/s), tan α = vy/max(|vx|, 1)), combined slip
σ = √(κ² + tan²α) fed as the formula's x with a friction-circle split; ray-cast suspension along the body axis, spring
`K·(rest − length)` with the static length chosen so the static corner load sits at the cfg wheel position, bump stop
10·K, contact force along the ground normal; wheel spin integrated implicitly with the tyre slope; rolling resistance
torque p13·Fz·R; bearing 1e-3·bearing·ω; clutch = friction clutch of capacity `ClutchF` N m solved implicitly with the
open diff (drive fractions normalised); gearbox efficiency 0.9; ratio[7] = reverse; engine torque =
throttle·table − dyno friction − closed-throttle pumping (0.1·T(limit/2)·rpm/limit); hard rev cut at RPM_limit; idle
governor; engine inertia = `engine_inertia·0.025`; drag `0.5ρ·Cd·A·v²` with Cd = script `C_drag` only if in [0.1, 1.5]
(stock cars end with ~0.015 after `fully_stripped_drag − Σ drag_reduction`, so the field default 0.32 is used),
A = 0.85·(track + 0.25)·1.3 m; inertia tensor of a uniform box; mass = chassis cfg bodies + every installed part's cfg
bodies; no ABS/TCS, no wings/downforce (cfg `wing` lines not modelled), no tyre wear/temperature, no `setArm/setHub/
setInstantCenter/setCPatch` geometry.

Integration: `Vehicle.Step` = 1/120 s split into 8 equal sub-steps of the whole system (body and wheels), semi-implicit
Euler, plain double arithmetic in fixed order → bit-identical runs (test). Gravity 9.8067 (Slrr_GI F_Gravity, M).

## Results (flat ground; GUESSED driver: shift at 97 % RPM_limit, 0.25 s shifts, clutch slip at launch)

| Car (stock parts, script-built) | mass kg | dyno maxHP / T·ω peak | 0-100 s | top km/h | 100-0 m | skidpad g |
|---|---|---|---|---|---|---|
| Baiern DevilSport | 1479 | 351.5 hp / 274.6 hp | 5.39 | 276.8 | 31.2 | 0.98 |
| Einvagen 110 GT | 1324 | 169.7 / 132.2 | 11.25 | 225.5 | 41.0 | 0.70 |
| Duhen SunStrip 1.5 DVC | 1222 | 120.3 / 92.5 | 12.56 | 170.8 | 40.4 | 0.61 |
| Shimutshibu Focer RC 200 | 1475 | 178.5 / 138.5 | 9.40 | 222.2 | 36.1 | 0.76 |
| synthetic test car (default DynoData, 1200 kg) | 1200 | 92.6 / 74 | 13.8 | 161.5 | 31.5 | 1.20 |

All four shipped cars pass the CLI's plausibility checks (0-100 between the all-power bound and 4× it, top speed under
the drag limit, braking 0.4..2 g, skidpad 0.3..2.5 g). **These engine numbers are NOT the game's**: see the VM gap below.
V-engine cars (MC GT, Prime DLH 500, SuperDuty) currently produce no torque at all.

Tests: energy (undamped springs, frictionless tyres: spread 6.1 % of the oscillation energy, drift 0.3 % over 20 s,
bounded), damping/tyres only remove energy, determinism, static ride height, monotonic (more torque → faster, more mass →
slower, stronger brakes → shorter until tyre-limited, bigger bore → more torque, nitro/turbo raise the table), formula
checks of 0x43ba80 / lookup / damage curve.

## Findings for other areas (not fixed here: other projects are read-only for this area)

1. **Slrr.Vm (blocking):** `&&`/`||` must apply JVM::booleanConversion (0x417840: `F` → `f != 0`, `L`/`[` → non-null,
   then int). `VmThread.Binary` throws `non-integral operands (&&)` for floats, which stops every engine block's
   `updatevariables` (`Block_Inline` line 137 `if (v4 && v2)`, `Block_Vee` line 172) and therefore
   `Chassis.updatevariables` before `calcDyno`. Fix: accept `VmKind.Float` in the OrOr/AndAnd case of `Binary`
   (`IsTrue` already handles floats). Until then `CarLoader.CompleteEngine` replays the dyno tail of Chassis.java in C#
   and substitutes DynoData defaults for the unset valve fields (flagged in the output; numbers not the game's).
2. **Slrr.Engine.Parts:** `WheelState.PacejkaCount` is 15 but the exe accepts 0..16 (Tyre.java sets par_15/par_16 →
   load-sensitive stiffness, lost today); the named WheelRef setters share the Pacejka array in the exe; `setDamping(F)`
   sets rebound = 0.7 F; `DynoCurve` lacks `Displacement`/`Compression`, which calcDyno writes (0x46aefd, 0x46af10).
3. **Parts link rule:** tyres (`attach 0x...0200 2`) must link to a rim slot that declares `compatible 0x...0200 2`
   (every rim does); `PartsWorld.Match` refuses. `CarLoader` adds that rule as glue (G).
4. **Resources ↔ Parts:** `GameRef.create` must call `PartsWorld.Attach` before `<init>(int)`; EVENT_COMMAND must reach
   `PartsWorld.ExecuteCommand`; after an install the child's instance must be re-parented (Part.addPart only calls the
   child's `addStockParts` when `getParentID()` changed). `install` commands that name the car itself as parent need
   parentPart 0 (tree search). All four are done by `CarLoader` (G) until the real integration exists.
5. Script dependency (M): `TyreCalculations.getWeightOnWheel` reads `GameLogic.player.car.chassis`, so tyre numbers depend
   on the *player's* car; `CarLoader.BindPlayerCar` makes the loaded car the player's.

## Cross-area interfaces defined here

* `IGroundQuery.Raycast(origin, dir, maxDist, out GroundHit)`; `GroundHit` carries the surface friction (exe surface
  +0x50) and rolling resistance (+0x54). The map/collision area implements it; `FlatGround` is the stand-in.
* `ExeDynoCalculator : Slrr.Engine.Parts.IDynoCalculator`.
* `CarLoader.Load(root, CarType)` → `LoadedCar` (parts tree, world, VM, dyno, log); `SpecBuilder.Build(chassis, dyno)`.

## Integration (lead)

`tools/Slrr.Cli/CmdDrive.cs` is wrapped in `#if SLRR_PHYSICS` so `Slrr.Cli` keeps building. To enable: add
`<ProjectReference Include="..\..\src\Slrr.Physics\Slrr.Physics.csproj" />` and
`<DefineConstants>$(DefineConstants);SLRR_PHYSICS</DefineConstants>` to `Slrr.Cli.csproj`, then
`"drive" => CmdDrive.Run(args[1..], o, e)` in `Commands.cs`, and add `src/Slrr.Physics` + `tests/Slrr.Physics.Tests` to
`Slrr.sln`. Usage: `drive --list`, `drive <car> [--root dir] [--gi] [--spec] [--dyno] [--scenario all|0-100|top|brake|skidpad]`
(`--gi` applies the Slrr_GI tyre multipliers).

## Open questions

1. What the exe passes as the formula's x (slip ratio/angle scaling) and the unidentified `(x+1)/2` load factor (0x4562f6).
2. Units/role of `engine_inertia`, `engine_friction_fwd/rev`, `starter_torque`, `ClutchF`, brake torque × `wheel_brake_factor`.
3. What `C_drag`, `drag_center`, per-part `drag_own/drag_occluded` and the cfg `wing` lines mean to the exe's aero.
4. Suspension geometry (`setArm` 7, `setHub` 10, `setInstantCenter` 6, `setCPatch`), the cfg `spring a b c` line, and why
   the MWM Chassis script writes wheel positions as `(±WheelPosXMult, WheelPosYMult, 0)` (cfg positions used here).
5. The physics step rate of the exe (120 Hz is this area's choice; 1/120 appears only in the dyno's cycle time).
