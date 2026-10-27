# Phase 4 area: vehicle physics (`src/Slrr.Physics`)

Deterministic fixed-step vehicle dynamics fed by the data the parts layer captures, plus a port of the exe's engine
dyno model. **Parity with the original game is UNVERIFIED**: nothing here was compared with a run of the game. Labels:
**M** = measured (static disassembly of `StreetLegal_Redline.exe`, address given, or shipped data/scripts),
**G** = guessed (chosen to make the model work or plausible). No repo binary was executed.

Build/test in isolation: `dotnet build src/Slrr.Physics -warnaserror && dotnet test tests/Slrr.Physics.Tests` (32 tests;
corpus tests pass vacuously without the game data). The gaps-wave changes are summarised in **Gaps wave** below.

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
| `Vehicle.cs` | The simulation: rigid body, suspension, tyres (exe slip definitions), wheel spin, clutch, gearbox, exe-style drive split + difflock coupling, predictive rev limiter, brakes, drag, damage, slope-aware placement. |
| `ChassisShape.cs` | Chassis collision boxes from the chassis cfg `body` lines and their `_phys_*.scx` meshes. |
| `Scenarios.cs` | 0-100 km/h, top speed, 100-0 km/h (stops on the speed magnitude, flags spins), skidpad (R 40 m); `AutoShifter`. |
| `Validation.cs` | `CarEstimates` (first-principles numbers from a car's own data) and the 8 regression checks with expected ranges. |
| `Loading/CarLoader.cs` | Builds a shipped car headless through `Slrr.Engine.Resources` + `Slrr.Engine.Parts` with its own scripts (`addStockParts`, `updatevariables`, `forceUpdate`). |
| `tools/Slrr.Cli/CmdDrive.cs` | `drive` command (inside `#if SLRR_PHYSICS`, see Integration). |
| `tools/Slrr.Cli/CmdPhysCheck.cs` | `physcheck` command: the regression checks for one or all shipped cars (inside `#if SLRR_PHYSICS`). |

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

GUESSED in `Vehicle` (the measured replacements of the gaps wave, slip/drive split/difflock/limiter/rolling resistance,
are in **Gaps wave**): ray-cast suspension along the body axis, spring `K·(rest − length)` with the static length chosen so
the static corner load sits at the cfg wheel position, bump stop 10·K, contact force along the ground normal; wheel spin
integrated implicitly with the tyre slope; bearing 1e-3·bearing·ω; clutch = friction clutch of capacity `ClutchF` N m
solved implicitly with the driveline; gearbox efficiency 0.9; ratio[7] = reverse; engine torque = throttle·table·limiter −
dyno friction − closed-throttle pumping (0.1·T(limit/2)·rpm/limit); engine inertia = `engine_inertia·0.025`; drag
`0.5ρ·Cd·A·v²` with Cd = script `C_drag` only if in [0.1, 1.5] (stock cars end with ~0.015 after `fully_stripped_drag −
Σ drag_reduction`, so the field default 0.32 is used), A = 0.85·(track + 0.25)·1.3 m; inertia tensor of a uniform box;
mass = chassis cfg bodies + every installed part's cfg bodies; CoM = chassis cfg bodies only; lateral tyre force at the
wheel centre; no ABS/TCS, no wings/downforce (cfg `wing` lines not modelled), no tyre wear/temperature, no
`setArm/setHub/setInstantCenter/setCPatch` geometry.

Integration: `Vehicle.Step` = 1/120 s split into 8 equal sub-steps of the whole system (body and wheels), semi-implicit
Euler, plain double arithmetic in fixed order → bit-identical runs (test). Gravity 9.8067 (Slrr_GI F_Gravity, M).

## Gaps wave (physics quality)

Everything below was checked by static disassembly of `StreetLegal_Redline.exe` (no binary executed) or on the shipped
data; **parity with the original game remains unverified** (nothing was compared with a run of the game).

### 1. Caynon: the car sat in place (root cause, reproduction, fix)

Reproduction (MEASURED with this model): `race-play --track Caynon` (dealer SunStrip 1.5, FWD) and the corpus test
`CaynonStartIsNoLongerStuck` (Slrr.Game.Physics.Tests; CaynonTrack `posStart (0, 8.7, -20)`, `oriStart Ypr(0,0,0)`).
The surface table was not the cause: Caynon's binary `Ground.SCM` has one material ("Groung"), every probe hit surface 0
with friction 1.0, no NaN, normals up (Y 0.97..1.0). The causes, in the order they act:

1. **Start pose in a twisted dip.** `world height` around the start: the ground falls 0.07 m behind and rises 8..24 %
   ahead, with a cross fall of 0.15..0.24 that differs between the axles (facet normals (0.23, 0.97, 0.12) under the
   front, (0.13, 0.99, -0.05) under the rear). The SunStrip's front corners have only 0.069 m of droop (its scripts set
   max length = rest length, M) and its chassis-body CoM is behind the middle (62 % rear), so the front-left wheel hangs
   at full droop (load 0..1000 N while the front-right carries 3300..4500 N).
2. **Open differential** (this area's earlier guess): the free wheel took the engine speed (80..190 rad/s) while the
   loaded one got the same tiny torque.
3. **Automatic box shifted on wheelspin**: the up-shift decision used the driven wheels' mean spin, so the box walked up to
   5th at 0..2 km/h and the engine had no torque left (the race survey's "5th gear at 0-2 km/h").
4. **Hard rev cut**: with the free wheel the engine sat on RPM_limit and the bang-bang cut removed the drive.

Fixes (MEASURED where marked): the exe's drive split and differential coupling (§2), the exe's predictive rev limiter
(§2), the exe's slip states (§3), an up-shift guard (`Drivetrain.UpshiftGroundFraction`: an up-shift also needs the rpm the
car's ground speed implies above 80 % of RPM_limit, GUESSED), and slope-aware placement (`Vehicle.PlaceOnGround`: body
tilted onto the plane fitted through the four wheels' ground points, yaw kept, raised until no wheel is below its static
stance, GUESSED; before, a car put on an 11° slope started with 53 kN in the uphill bump stops and flipped). Results
(MEASURED, `race-play --survey --seconds 5`): Caynon **0.1 m / 1.9 km/h → 20.1 m / 31.2 km/h**; the other maps still drive
(Drag 32.1 → 33.6 m, Test Track 21.4 → 23.1 m, Dead Island 25.1 → 29.9 m, Drift 35.5 → 39.9 m, Car Dealer 9.8 → 9.7 m);
11/12 maps drive (Valo City: no ground under the start, map area).

### 2. Drivetrain, differential, engine (MEASURED structure, vehicle step 0x454500..0x457848)

The vehicle update is virtual slot 12 (vtable 0x5f0f18 + 0x30), `update(world, dt)`, returning when dt < 1e-4.

* **Differential lock** (0x4554a0..0x455602): each driven wheel's spin is pulled toward the drive-weighted mean spin by
  the fraction `k = min(|Δ|·0.1·dt·50·L, 1)`, L = `difflock` (car +0x1fbc → +0xa24; the verb stores f, or 0.1 when f ≤ 0,
  0x4597ea; initial 0.1, 0x44717d; stock Chassis.java sends `difflock 0.0`). `DrivetrainSpec.DiffLock`, applied once per
  1/120 s step. Weights: `setDrive` writes +0xcc and +0xe0 (0x440e00); the wheel factor +0xc4 is copied from the car
  template (0x44aa28) and not identified (taken as 1).
* **Drive split** (0x455af2..0x455b55): the engine/shaft coupling changes the shaft speed and every driven wheel gets
  `ω_i += Δ·d_i/c_i`: equal spin increments, not equal torque. `DrivetrainSpec.EqualSpinDrive` (default true) solves the
  clutch with the driven wheels as one spool for the drive impulse. Split-friction test: > 1.5× the open differential's
  speed after 2 s (`ExeStyleDriveBeatsAnOpenDifferentialOnSplitFriction`).
* **Rev limiter** (0x454970..0x4549d5): predicted speed `w + (w − w_prev)`; drive torque × 1 below RPM_limit,
  × (limit − w)/(w_pred − w) when the next step would cross it, × 0 above and rising, × (limit − w_pred)/(w − w_pred) above
  but falling. `Vehicle.RevLimiterFactor` replaces the hard cut.
* **Idle governor** (0x454813..0x454864): below idle, throttle += 1.1·|friction|/table torque (capped at 1); here
  "friction" is this model's friction + pumping (the exe's own friction term differs, below).
* Found, NOT ported (step/unit questions open): engine friction `−ω·0.05·maxRPM·engine_friction_fwd` (ω ≥ 0, capped at
  `0.5·engine_inertia·ω`), `ω²·engine_friction_rev` (ω < 0) (0x454770..0x4547ef); starter torque added and throttle zeroed
  when throttle > 0.1 below 0.95·idle (0x45487d); **drive torque = 2 × table torque × throttle × limiter** (0x454a85
  `fadd st0,st0`); the engine speed changes by `net / engine_inertia` per call with no dt (0x455919); engine and shaft
  relax toward the rigid common speed by `0.999·(1 − clutch pedal)` per call; reflected inertias `4·engine_inertia`
  (engine_inertia_factor 0x60e028 = 4.0) and `[+0xe4]/G²` ([+0xe4] = cfg `enginepower` 6th value, default 1000, 0x446c43);
  ClutchF (+0xc8) is not used by that coupling. Because the engine increments carry no dt, the exe's effective engine
  inertia depends on its call rate, which was not found (open question 2).
* Field offsets (M, 0x448643..0x448a23): engine_inertia +0xe0, starter_torque +0xcc, engine_friction_fwd/rev
  +0xf0/+0xf4, engine_rpm_idle·π/30 +0xec, maxRPM +0x14, RPM_limit +0x18, ClutchF +0xc8, ratios +0x20.., rearend_ratio +0x40.

### 3. Tyre slip input of 0x43ba80 (MEASURED: 0x43bbd0, called at 0x456187)

`Vehicle.TyreForces` follows the exe: steady slip κ = (Rω − vx)/|vx|, α = atan(vy/|vx|) above 0.1 m/s; below 10.1 m/s
they are blended (b = clamp((|vx| − 0.1)·0.1, 0, 1)) with two stored first-order states
`m' = (Δv·dt + 0.3m)/(0.3(1 + 5dt))`, `t' = (0.5t + vy·dt)/(0.5(1 + 10dt))` (clamped ±1; i.e. dm/dt = (Δv − 1.5m)/0.3,
dt/dt = (vy − 5t)/0.5, Δv = Rω − vx); the formula's x = `sqrt(sin²t + (cos t · m · p12)²)`, Fx = F·cos t·m·p12/x,
Fy = −F·sin t/x. **p12 (`setFrictn_x`) scales the longitudinal slip** (0x45619c, 0x4563a4); the earlier model applied it
to the lateral force. The slip-angle sign and the `(x+1)/2` load factor (0x4562f6) are still open.

### 4. Surfaces (MEASURED: .scm handlers 0x4627f0..0x462c5e, Config.ground_friction read at 0x46338b)

Every material starts with friction = `ground_friction` (Config script default 1.0; ≤ 0 → 1.0), rollres = 0.001,
collcoeff = 0.2 (0x462914..0x462928); `friction f` stores f·ground_friction, `rollres r` and `collcoeff c` raw. The wheel
step sets p2 = surface friction × tyre friction and p13 = surface rollres + tyre rollres (0x456292/0x45629b) and uses
**0.01 × p13 × smoothed load** (0x456492..0x456498), `Vehicle.RollingResistanceScale`: rolling resistance is negligible on
ordinary ground and 0.03·load on Dead Island's Sand2 (rollres 3.0). A second built-in material (0x463428) starts with
rollres 0.01 (role unknown). `GroundHit` carries friction, rollres, collcoeff and the surface index (`SurfaceMapping` in
Slrr.Game.Physics); collcoeff's role in the collision response was not traced. Test: friction 0.6 lengthens 100-0 > 1.3×.

### 5. Lifting wheels at ~1 g (DevilSport) and rollovers

Statics: with the tyre forces at the contact patch the inner wheels unload at a = t/(2h) g, whatever the roll stiffness
(rollbars only move load between the axles; the stock DevilSport and SunStrip have none). The chassis-body CoM is high:
DevilSport h 0.65 m above the ground, t 1.418 m → 1.09 g; SunStrip h 0.73 m → 0.93 g; fleet 0.58..0.74 m. The regression
slalom rolled the SunStrip at 50 km/h. Structure in the exe (M): every wheel is its own physics object (wheel +0x30)
joined to the chassis by the spring constraint that setForce/setDamping/setRest/Min/MaxLen configure (0x491f80 calls,
0x441270..0x441540), and the tyre force is computed on that wheel object. `VehicleSpec.LateralForceAtHub` (default true;
GUESSED consequence of that structure) applies the lateral tyre force at the wheel centre (longitudinal force stays at
the contact patch): lift at t/(2(h − r)) (DevilSport ≈ 2.3 g); the SunStrip slalom now peaks at 0.65° sideslip, no roll.
The real CoM is still open: the parts' cfg bodies cannot be placed yet (`SpecOptions.PartsInCenterOfMass`, off: through
the slot frames the SunStrip's engine block lands at z −0.05 and its bumpers at the car centre); the MWM Chassis adds
"Rollprevention" parts (cars.racers 0x201E, missing from this install: "Wrong GameType!") and sets
`setInstantCenter(±0.616, −0.235, 0, 0, −0.15, 0.25)` (not modelled); the cfg keyword `center x y z` (0x444570, car
+0x1eac) exists but no shipped cfg uses it.

### 6. Chassis collision shape, damage (adapter side: docs/race/game-physics.md)

`ChassisShape.Read` (data M, use G): boxes from the chassis cfg `body` lines whose `_phys_*.scx` meshes are stored in
centimetres (DevilSport frame x ±78.1, y −25.6..22.6, z −154.7..154.9 cm around body (0, −0.34, 0.54)); 8..14 boxes per
racer. Damage effects use the MEASURED 1 − 0.6d² (0.1 from d = 1): `Vehicle.EngineDamage` (torque), `WheelSim.Damage`
(tyre peak friction p2 and brake torque).

### 7. Regression scenarios (`Validation.Run`, CLI `physcheck`)

Eight checks per car with ranges from the car's own data (`CarEstimates`: peak table power, tyre peak D·sin(Cπ/2),
driven static load share, CoM height, track, brake decel, drag- and gear-limited top speed). Tolerances GUESSED: 0-100 in
[0.9·max(power, traction bound), 3× that + 2 s]; top speed in [0.75, 1.05]× min(drag, gear limit); 100-0 in
[v²/(2g·1.1μ), v²/(2·0.7·min(0.8μg, brakes)) + 5 m] with a heading change ≤ 30°; skidpad R40 in [0.55, 1.15]·μ; slalom
18 m at 50 km/h: sideslip ≤ 12°, path error < 2.5 m, upright; brake in turn R50 at 60 % of the grip speed with the pedal
that gives 0.3 g: sideslip ≤ 25°, stops upright; drift entry at 60 km/h with full lock + handbrake 0.5 s: sideslip ≥ 8°,
upright; jump landing (dropped level from 1 m at 15 m/s): peak vertical g in [1 + v²/(2g(stroke + 0.35)), 25], settles
within 4 s, upright. Fleet run (MEASURED, 24 racers, `physcheck --all`, 3-4 s per car): **16 pass every check**; outliers:

| Car | Outlier | Cause found |
|---|---|---|
| Duhen Racing SunStrip 2.0 CDVC, Ishima Enula WRZ, Enula WR SuperTurizmo, Shimutshibu Focer RC 300, Focer WRC | no engine torque, 0 gears | Slrr.Vm: `getstatic` of the instance field `Block.chargersOnBlock` (TurboCharger.fillDynoData) throws "instance field access from static context"; Chassis.updatevariables stops before the gearbox/dyno. Turbo cars only; VM owner. |
| Duhen SunStrip 1.8 / 2.2 DVC | 100-0 spins (heading change 180°) | FR brake torque 0: stock parts linked into the wrong chassis slots (slot 112, FR brake, holds the front shock; slot 113 a front brake). Parts layer / loader linking, not physics. |
| Hauler's SuperDuty Extra 750 | 0-100 17.9 s (range ≤ 12.3 s), 100-0 heading change 42° | not investigated |
| 15 traffic / fake-racer gametypes | skipped | no Part chassis script (CarLoader) |

The V-engine cars (MC GT/GTB/GTLE, Prime, SuperDuty 500) have torque now (the VM `&&`/`||` fix of an earlier wave): MC GT
189 kW, Prime 262 kW, SuperDuty 500 201 kW peak table power, and they pass every check.

## Results (first revision; flat ground; GUESSED driver: shift at 97 % RPM_limit, 0.25 s shifts, clutch slip at launch)

| Car (stock parts, script-built) | mass kg | dyno maxHP / T·ω peak | 0-100 s | top km/h | 100-0 m | skidpad g |
|---|---|---|---|---|---|---|
| Baiern DevilSport | 1479 | 351.5 hp / 274.6 hp | 5.39 | 276.8 | 31.2 | 0.98 |
| Einvagen 110 GT | 1324 | 169.7 / 132.2 | 11.25 | 225.5 | 41.0 | 0.70 |
| Duhen SunStrip 1.5 DVC | 1222 | 120.3 / 92.5 | 12.56 | 170.8 | 40.4 | 0.61 |
| Shimutshibu Focer RC 200 | 1475 | 178.5 / 138.5 | 9.40 | 222.2 | 36.1 | 0.76 |
| synthetic test car (default DynoData, 1200 kg) | 1200 | 92.6 / 74 | 13.8 | 161.5 | 31.5 | 1.20 |

All four shipped cars pass the CLI's plausibility checks (0-100 between the all-power bound and 4× it, top speed under
the drag limit, braking 0.4..2 g, skidpad 0.3..2.5 g). **These engine numbers are NOT the game's**: see the VM gap below.
(Current numbers: `physcheck`, e.g. DevilSport 0-100 6.32 s, top 262 km/h, 100-0 31.1 m, skidpad 0.93 g; the engine data
changed in later waves. V-engine cars have torque now, see Gaps wave §7.)

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

Gaps wave: `tools/Slrr.Cli/CmdPhysCheck.cs` (same `#if SLRR_PHYSICS`): add `"physcheck" => CmdPhysCheck.Run(args[1..], o, e)`
and the usage line `CmdPhysCheck.Usage` to `Commands.cs`. Usage: `physcheck [car ...|--all] [--root dir] [--gi] [--quiet]`
(exit 1 when any car is an outlier). It is also compiled into tests/Slrr.Game.Physics.Tests (test `PhysCheckCommandRunsForOneCar`).

## Open questions

1. The `(x+1)/2` load factor (0x4562f6) and the slip-angle sign convention of 0x43bbd0's outputs.
2. The exe's physics call rate: the vehicle update takes dt (returns below 1e-4) but its engine increments carry no dt, so
   the engine's effective inertia and the 2× drive torque factor cannot be ported until the caller and its dt are found
   (the slot-12 call site was not identified; `FPS: %.1f (%.1f)` at 0x42de6c prints 1/frame time and 1/(time of
   0x4fca30, the world/render update)). 120 Hz is this area's choice.
3. Units/role of `starter_torque`, `ClutchF` (+0xc8, unused by the drivetrain coupling), the wheel template factor +0xc4,
   brake torque × `wheel_brake_factor`, `engine_mass·8000`.
4. What `C_drag`, `drag_center`, per-part `drag_own/drag_occluded` and the cfg `wing` lines mean to the exe's aero.
5. The real CoM (placement of the parts' bodies; `setArm` 7, `setHub` 10, `setInstantCenter` 6, `setCPatch`; the MWM
   Rollprevention part, missing here), and whether the exe's wheel constraint really feeds side forces in at the hub.
6. How the exe turns collisions into damage (the effect curve is measured; the accumulation in Slrr.Game.Physics is a guess)
   and the role of the surfaces' `collcoeff`.
