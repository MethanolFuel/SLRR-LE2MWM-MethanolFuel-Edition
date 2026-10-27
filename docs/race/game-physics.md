# Race wave area: game physics adapter (`src/Slrr.Game.Physics`)

The host's `IPhysicsWorld` (src/Slrr.Game) on top of Slrr.Physics' `Vehicle` and Slrr.World's ground/collision world:
binds chassis parts to simulated cars, executes the car command verbs, steps a fixed 120 Hz simulation from the host
frame (accumulator), maps the human controller's virtual axes to the player car, collides chassis boxes with the map
and with each other, and writes pose/velocity/telemetry back where the scripts read it. **Nothing here was compared
with the original game.** Labels: **M** = measured (shipped scripts/cfgs/data, or docs of other areas that measured
it, cited), **G** = guessed.

Build/test in isolation: `dotnet build src/Slrr.Game.Physics/Slrr.Game.Physics.sln -warnaserror` and
`dotnet test src/Slrr.Game.Physics/Slrr.Game.Physics.sln` (17 tests; corpus/host tests pass vacuously without game data).

## Files

| File | Contents |
|---|---|
| `GamePhysicsWorld.cs` | `IPhysicsWorld` implementation: binding, verbs, script placement, rebuild on `forceUpdate`, write-back; `IMapSource`, `FixedMapSource`, `GamePhysicsOptions` |
| `PhysicsScene.cs` | Host-independent core: cars, accumulator, fixed step, collisions, telemetry (`ICarTelemetrySource`) |
| `SimCar.cs` | `SimCar` base (run state, controls, impulses, box), `FullCar` (a `Vehicle` + `Drivetrain`), `SimpleCar` (kinematic traffic/AI body), `SwitchableGround` |
| `Controls.cs` | `CarControls`, `IVehicleInputSource`, `VirtualAxisInput` (Input.AXIS_*), `ScriptedInput`, `Drivetrain` (gearbox, clutch, steering rate) |
| `Collision.cs` | `IStaticCollision` + `CollisionWorldShapes`, `CollisionSolver` (box/triangle SAT, car/car spheres, impulses) |
| `Interop.cs` | Vec3/Quat <-> System.Numerics, Ypr conversion, `SurfaceMapping`, `WorldGround` (Slrr.Physics ground over Slrr.World) |
| `Telemetry.cs` | `CarTelemetry`, `WheelTelemetry`, `ImpactEvent`, `ICarTelemetrySource` |
| `PhysicsNatives.cs` | `Vehicle.getSpeedSquare/getHorn/hasCrime` (the three Vehicle natives nobody registers) |
| `HeadlessDrive.cs` | Shipped car on a shipped map with a scripted input segment (no host, no game scripts) |
| `tools/Slrr.Cli/CmdCarSim.cs` | `carsim` CLI (inside `#if SLRR_GAME_PHYSICS`) |

## Integration (lead)

1. `Slrr.sln`: add `src/Slrr.Game.Physics/Slrr.Game.Physics.csproj` and `tests/Slrr.Game.Physics.Tests/Slrr.Game.Physics.Tests.csproj`.
2. `tools/Slrr.Cli/Slrr.Cli.csproj`: `<ProjectReference Include="..\..\src\Slrr.Game.Physics\Slrr.Game.Physics.csproj" />` and add
   `SLRR_GAME_PHYSICS` to `DefineConstants`; `Commands.cs`: `"carsim" => CmdCarSim.Run(args[1..], o, e),` plus a usage line
   `carsim [car] [--map m] [--cars n] ...   shipped car on a shipped map, scripted drive, step cost`.
3. Host wiring (wherever a `GameHost` is built for driving, e.g. `PlayCommand`/`CmdPlay`):
   ```csharp
   var physics = new GamePhysicsWorld(new GamePhysicsOptions { Maps = mapSource });   // mapSource: the map area's IMapSource
   new GameHostOptions { ..., Physics = physics, ExtraModules = [new PhysicsNatives(physics)], Dyno = new ExeDynoCalculator() }
   ```
   Renderer/audio read `physics` as `ICarTelemetrySource` (or `physics.Scene`).
4. Map area: implement `IMapSource.Current` (the `StaticWorld` of the loaded `GroundRef` map, null in menus/garage). Without it
   cars are bound and their verbs work, but they stay where the scripts put them (`Held`, no ground).
5. AI area: `physics.SetInput(carId, source)` with an `IVehicleInputSource` for each bot car (the `controllable` verb to a non-human
   controller is logged and left to the AI owner). `physics.Scene.Impact` (or `Impacts` per frame) is the hook for
   `EVENT_COLLISION`/damage; the event id and payload the scripts expect are not known here.

Verified here: the smoke play path (50 steps, `play --smoke` script) passes with this adapter plugged in and the three
Vehicle natives are no longer missing (test `SmokePathStillPassesWithThePhysicsAdapterAndBindsTheDealerCars`; 4 dealer cars bound,
78 car verbs consumed, 4 rebuilds after `forceUpdate`).

## Cross-area interfaces (defined here)

* `IMapSource { StaticWorld? Current }` - map owner -> physics.
* `ICarTelemetrySource { Cars, Get(id), Alpha, Impacts }` - physics -> render/audio. `CarTelemetry`: chassis origin pose
  (`Position`, `Orientation`, `Ypr`), velocities, speed, rpm/idle/limit, gear, pedals, steer, engine torque, g-forces,
  odometer, impact total, per wheel: world and chassis-frame centre, radius, spin, rolling angle, steer angle, travel,
  load, slip ratio/angle, skid measure, grounded.
* `IVehicleInputSource.Read(time, CarControls)` - player/AI/replay -> physics.
* `IStaticCollision` - any triangle source for chassis collisions (`CollisionWorldShapes` wraps Slrr.World).

## Behaviour

| Behaviour | Label |
|---|---|
| Car identity = chassis instance id: `Vehicle(Part)` calls `set(chassis)`, so `car.command(..)` and `car.id()` carry the chassis id (Vehicle.class) | M (script) |
| Bind on the first car verb to a node whose script is a Part with chassis state (`PartsWorld.Find`); spec from `SpecBuilder.Build` with a private `ExeDynoCalculator`; < 3 wheels -> `SimpleCar` | G |
| Rebuild the simulated car when `PartsWorld.ChassisUpdated` fires for a bound chassis (keeps pose/state/input) | G |
| Car verbs consumed: start, stop, idle, reset, brake, suspend, wakeup, setsteer, transmission, steerhelp, sethorn, setvel (+ abs, asr, esp, difflock, cruise, filter, damage_multiplier, corpse, reload, pickup, team, addroll, setpitch, ai_cheat stored only) | names M (entry-points.md §5), effects G |
| `start` = engine on + simulated + accepts input; `stop` = engine off, held, motion zeroed; `idle` = engine on; `reset` = at rest, upright at the current yaw, snapped to ground, run state kept (integration change: `Track.lockCar` ends with `reset; reload` and no `start` follows, see docs/race/integration.md); `brake` = throttle 0 / brake 1 until start/reset; `suspend`/`wakeup` freeze/unfreeze | G, from their use: race grids send `reset; setMatrix; stop; idle` then `start` (City), showrooms `reset; idle; setsteer -0.6` (CarSelect) |
| `transmission n`: 0 manual, 1 auto, 2 manual+clutch, 5 semi-auto | values M (Vehicle constants), behaviour G |
| `setvel` = 3 linear then 3 angular components | format M (`%f,%f,%f,%f,%f,%f`), order G |
| Script `setPos/setMatrix/setState` since the last write-back teleports the car; it is snapped to the ground found below (y + 2 m) | G |
| Write-back each frame: node Pos = chassis ORIGIN (not CoM), Ori = Ypr, Vel, AngVel | storage M (ResourceNode), origin vs CoM G |
| Ypr <-> orientation = `Matrix4x4.CreateFromYawPitchRoll` | M for placed map objects (world.md), G for cars |
| Car forward is -Z, left is -X in the chassis frame | M (DevilSport cfg: steering wheels at z = -1.48, FL at x = -0.709) |
| `Vehicle.getSpeedSquare` in m²/s² | M (CarInfo prints sqrt * 2.24 as MPH; TestTrack thresholds 126.5/193.6 = 40.5/50.1 km/h) |
| `getHorn` from AXIS_HORN / `sethorn`; `hasCrime` = 0 | G |
| Chassis telemetry cells (`getIndexedData`): RPM, GearNumber, Throttle/Brake/Clutch/Nitro strength, Handbrake, CurrentSteering1-3, ExhaustActivity, GearStrength, CarRuntime; `Chassis.Mileage` += metres driven | offsets/names M (parts.md), values/units G |
| Input axes: steer AXIS_TURN_LEFTRIGHT (1) +1 = left, throttle 28, brake 29, nitro 31, handbrake 48, clutch 49 (+1 = pedal pressed), gear AXIS_GEAR_UPDOWN 50 (+1 up = A, -1 down = Z), horn 54 | M (ControlSet.class `define(...)`) |
| Human controller = the `controllable <car>` target is a registered `Controller` in `UiEngine.Controllers` | G |
| Fixed step 1/120 s, accumulator, max 12 steps per frame (rest dropped and counted in `DroppedTime`) | G (exe rate not measured) |
| Automatic box: shift on the rpm the driven wheels imply (up > 95 % of RPM_limit, down < 45 %), 0.6 s between shifts, 0.25 s shift with the clutch open, launch slip keeping the engine > 1.5x idle, brake-to-reverse at standstill; steering rate 4/s; `steerhelp` divides steer by 1 + k·v/20 | G |
| Engine off: clutch open and engine speed held at 0 (Slrr.Physics' idle governor would restart it) | G |
| Surface: friction = `.scm` `friction` (neutral 1.0), rolling resistance added = (rollres - 1) x 0.01 for materials with explicit properties | role M (physics.md 0x456292/0x45629b), mapping G |
| Suspension rays: downward rays use Slrr.World's ground grid (triangle under the ray, plane intersection, re-query); others its BVH | G (performance choice) |
| Chassis box: width track + 0.25 m, length 1.65 x wheelbase, from 5 cm to 1.0 m above the axle line | G (the exe's body collision shapes were not decoded) |
| Box vs map: SAT on the triangles inside the box's bounding sphere, minimum-penetration axis (face axes preferred), up to 4 deepest contacts, position correction minus 5 mm slop, 2 impulse passes, restitution 0.2, friction 0.5 | G |
| Car vs car: two spheres per car, mass-weighted correction, same impulse | G |
| Impact event when the normal impulse / mass >= 1 m/s; `ImpactTotal` accumulates it (damage hook) | G |
| Cars below the map's lowest point - 50 m are held (`Settings["fell"]`) | G |
| `SimpleCar`: bicycle model, 3.5 m/s² full throttle, 8 m/s² brake, 45 m/s max, 8 m/s² lateral grip, two ground probes for height/pitch | G |

## Measured results (repository data; 4 vCPUs shared with other agents, timings vary)

* **Headless drive** (`HeadlessDrive`, test `DevilSportDrivesTheTestTrackSegmentDeterministically`, CLI `carsim`): Baiern
  DevilSport (1479 kg, built by its own part scripts through `CarLoader`) on `maps/test_track.rpk` at TestTrack's own start
  `posExit (189.2, -3.8, -1417.0)`, `oriExit` yaw 2.88 (M, TestTrack.class; the map's ground there is -3.81, the car rests on
  all four wheels at gV 1.00). Scripted segment (1 s idle, full throttle up the exit ramp with a small weave, brake at 6.5 s):
  89.2 km/h at 6.5 s in 2nd gear (1st to 57.6 km/h at 6877 rpm), max rpm 6892 (RPM_limit 7250), braking -1.31..-1.39 g,
  stop after 99.6 m, all wheels grounded 99.2 % of the steps, 0 impacts. Two runs and the CLI in another process give the
  same state hash `C1C3333CF4CD154F` (bit-identical). These numbers are the model's, not the game's.
* Earlier segments with stronger steering crashed into the ramp walls / an object at about (156, -1295): impacts were
  reported with the wall normals (e.g. dv 32.5 m/s head-on at 103 km/h, the car flipped), the box never tunnelled.
* **Step cost** (test `StepCostForOneAndTwentyCars`, 6 s of the segment, best of 3 after a warm-up, ms per 120 Hz step for
  all cars incl. collisions and telemetry): 1 full car 0.04-0.13; 20 full cars 0.50-1.4 serial, 0.34-0.45 with
  `Parallel`; 1 full + 19 `SimpleCar`s 0.15-0.19. Car loading (part scripts + dyno) 0.64 s, test_track collision world 0.21 s (no render scene).
* Host: the smoke play path passes 50/50 with the adapter; the dealer's 4 cars are bound, their verbs
  (`difflock`, `transmission 1`, `steerhelp 0.666`, `asr`, `abs`, `reset`, `setsteer`, `stop`) consumed, and the car is rebuilt
  after each `forceUpdate`.
* Synthetic tests: Ypr round trip vs `Matrix4x4.CreateFromYawPitchRoll`; accumulator; flat-ground determinism; a wall
  stops the box (no corner past it, impact normal +Z, surface/source ids carried); manual gear edges; verbs; held cars
  idle without moving; `SimpleCar` climbs a ramp; head-on simple cars collide instead of passing through.

## Open questions

1. Physics rate and frame order of the exe (P0 #3); 120 Hz is this project's choice.
2. Semantics of `stop`, `idle`, `start`, `brake`, `suspend`, `wakeup`, `filter`, `corpse`, `reload` (traced only by usage).
3. GearNumber encoding and the units of the other telemetry cells; whether `Mileage` is metres.
4. Is the scripts' `getPos/setMatrix` of a car the chassis origin (assumed) or the centre of mass?
5. Chassis collision shape (mesh/bodies of the car) and the exe's response/damage model; the EVENT_COLLISION payload.
6. The surface `rollres` scale and defaults (shared with world.md open question 1).
7. DevilSport lifts two wheels at ~1 g lateral in Slrr.Physics (seen with steer 0.06 at 96 km/h): roll stiffness/CoM height
   of SpecBuilder are worth a look by the physics owner.
