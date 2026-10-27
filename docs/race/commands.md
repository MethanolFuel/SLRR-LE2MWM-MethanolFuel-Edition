# Engine commands (`src/Slrr.Engine.Commands`)

Race-wave area **Commands**: the `EVENT_COMMAND` verbs that scripts send to native objects (cameras, cars, controllers,
bot brains, cursor, map) via `GameRef.command(s)` = `queueEvent(null, EVENT_COMMAND, s)`. They run as a deterministic
state machine behind small interfaces, with pure-managed default implementations. Labels: **M** = measured (exe strings
and formats, shipped script code, or observed by running the shipped scripts in our VM), **G** = guessed. Nothing here
claims parity with the original game: no trace of the original was available.

Build/test in isolation: `dotnet test src/Slrr.Engine.Commands/Slrr.Engine.Commands.sln -warnaserror` (36 tests; the 5
corpus tests pass vacuously without game data). References: Slrr.Vm, Slrr.Engine.Resources (tests also use Slrr.Game).

## Call-site census and coverage (M)

`python3 -I tools/analysis/command_sites.py .` scans all 1,692 `.class` files (1,760 class definitions; duplicated class
definitions are counted each time): 528 `command`/`queueEvent(EVENT_COMMAND)` sites, **526 with a literal verb**, 2
non-literal (`ROCTrack` `roc.getCurrentAIParam()`, `GameRef.command(p0)`). The verbs were implemented in rank order.

| Status | Script call sites | Share | Verbs (sites) |
|---|---|---|---|
| Implemented (state that drives behaviour) | 438 | 83.3 % | setsteer 35, idle 34, start 33, reset 32, stop 31, look 23, angle 21, render 19, move 18, controllable 16, camera 16, simulate 15, leave 15, dist 15, brake 13, suspend 10, smooth 10, AI_BeginRace 9, zoom 8, torque 8, force 8, hide 6, sethorn 5, roll 5, wakeup 4, lock/unlock 4+4, obs_add 4, obs_rem 3, AI_suspend 2, AI_follow 2, height, enable, disable, autozoom, AI_stop, AI_race, AI_level, AI_NightRace, AI_GoToTraffic, AI_GoToTarget 1 each |
| Recorded (parsed + stored for physics/render/UI) | 78 | 14.8 % | filter 17, reload 10, renderinstance 8, viewport 6, osd 6, corpse 6, texture 5, difflock 2, activate 2, AI_params 2, transmission, steerhelp, asr, abs, cruise, damage_multiplier, setcontrol, sens, mode, cursor, device, deactivate, flap_toggle, AI_spline 1 each |
| Not this area (part structure → Slrr.Engine.Parts) | 10 | 1.9 % | install 5, remove 2, add_rollbar 2, repair 1 |

All ~130 verbs of the exe's command parsers are in `VerbCatalog` with their sscanf format and string address (M);
`race --coverage` prints the table. Verbs no script sends are parsed and stored too.

## Files

| File | Contents |
|---|---|
| `VerbCatalog.cs` | verb → group, exe format + VA, script sites, status; kind-specific formats (car `render %d %d %d`, cursor `move %f,%f`, ...) |
| `ParsedCommand.cs` | parse with the receiving kind's format (`CScanf` from Slrr.Engine.Resources) |
| `Interfaces.cs` | `IObjectDirectory`, `IScriptEventSink`, `IVehicleState`, `IVehicleDriver`/`DriveInput`, `ICameraRig`, `IRaceDirector`, `ITrafficDirector`, `ICarControl` |
| `CommandEngine.cs` | dispatcher by native kind, per-frame `Step`, `AnswerInfo` (GII_*), transcript/stats |
| `CameraRig.cs` | camera verbs → follow/look/move/orbit rigs, viewport bindings, car interior views |
| `RaceDirector.cs` | controller + brain verbs, AI driving, trigger volumes → EVENT_TRIGGER_ON/OFF, route/lap/wrong-way telemetry, crime level |
| `TrafficDirector.cs` | traffic registry, `car_add`/`car_rem` events, cruise driving |
| `CarControl.cs` | car verb state (`CarControlState`) + cursor/map verbs (`SceneCommands`) |
| `KinematicVehicles.cs` | stand-in `IVehicleState`/`IVehicleDriver` (point-mass bicycle) when no physics adapter exists |
| `ResourceObjectDirectory.cs` | the directory/event sink over `ResourceEngine` |
| `CommandNatives.cs` | `INativeModule`: getInfo wrapper, Vehicle natives, optional queueEvent routing, fill-only gaps |
| `tools/Slrr.Cli/CmdRace.cs` | `CommandsPhysicsWorld` (Slrr.Game `IPhysicsWorld` decorator), `RaceSession`, `race` CLI |

## Dispatch (M unless noted)

The target's native kind (rpk `native` line, `ResourceNode.NativeKind`) picks the parser, as in the exe where each kind
has its own format block: `camera` (GameRef.RID_CAMERA, system.rpk:0x33) → CameraRig; `car` → CarControl, and
`render`/`hide` → CameraRig interior views (car format `%*s %d %d %d` = viewport, owner, camera index, from
Track.changeCamInternal/Mirror); `player` (Controller) and `bot` (BOTBRAIN sl.rpk:0x6E; accepts the controller verbs too)
→ RaceDirector; `cursor` → SceneCommands; kindless map nodes + `obs_add/obs_rem/fog` → SceneCommands (G). Part-structure
verbs and other kinds are never consumed. Unknown verbs are not consumed.

Observed on the data: entering Test Track / Drag Track / Valo City sends 33–49 commands; **all are consumed**.

## Behaviour (all G unless stated)

* **Cameras**: focus = `look` target (object frame; a map/unknown id = world point) or, for the follow camera bound by
  `camera id` to a controller, the car that controller drives. Desired position = `move` anchor (second offset when
  given, `+height`), or a point behind the followed car at the current distance, `force.X` up. Exponential smoothing with
  τ = `move` stiffness or `smooth`; `dist min max` is a rope applied after smoothing; `angle a b c` orbits (b deg/s,
  elevation c); `zoom fov rate`, `autozoom`, `roll`, `pitch/maxpitch`, `simulate 0` = rigid. Poses are written back to
  the instance (scripts read `cam.getPos()`); a script `setMatrix` is adopted. `render vp …`/`hide vp` bind viewports.
* **GII answers** (wrapper over `GameRef.getInfo`, only when the generic path returns 0): GII_OWNER(24) of a car = the
  controller/brain that sent `controllable car`; GII_BONE(1) = the car; GII_RENDER(48) = 4 interior cameras;
  GII_CATEGORY(7) = GIR_CAT_VEHICLE(5). Needed so Track.changeCamFollow's `viewport`/`camera` reach the controller (M: those
  two commands went to id 0 before).
* **Cars**: `start` free, `stop` hold, `idle` hold (engine idle), `brake` full brake, `suspend/wakeup`, `reset` zero velocity,
  `setvel`, `setsteer`, `sethorn` (→ `Vehicle.getHorn`), assists/filters/corpse/texture stored in `CarControlState`.
* **Brains**: `AI_follow off id` follows at speed `target + 0.8·(d−8)`; `AI_race x,y,z opp` drives to the point and through
  it, after `AI_BeginRace f` (f = reaction delay); `AI_GoToTarget` stops within 5 m; `AI_stop` brakes; `AI_suspend` no input;
  top speed 22 + 4·AI_level m/s (≤ 70). `AI_GoToTraffic` hands the car to TrafficDirector and posts
  `ai_info_entertraffic 0x<traffic> 0x<car> 0x<brain>` to the car (format M 0x60dbc8; value order G, chosen so Bot reads
  token 1 as the traffic id).
* **Triggers**: instances of kind `trigger`; params `x,y,z,0,0,0,sphere,R` / `…,box,a,b,c` (M: java.game.Trigger and the
  exe's `%.2f,%.2f,%.2f,0,0,0,sphere,%.2f`). Each step, for cars that behave as cars (received a car verb, controlled, or
  in traffic — keeps car-kind props such as the Navigator's "Camera GPS Marker Base" out), inside/outside changes post
  EVENT_TRIGGER_ON(32)/OFF(64) on the trigger with text = car id (M: scripts compare `token(0).intValue()` with a car id),
  OFF before ON.
* **Traffic**: `car_add carId trafficId` / `car_rem …` posted to the map (format M 0x610394, roles M from City.handleEvent);
  traffic cars cruise straight on at 12 m/s (6 slow): no road graph.
* **Telemetry** (not an exe feature): `RaceDirector.TrackRoute` gives splits, laps, finished, wrong-way (> 2 m/s away
  from the next checkpoint for 2 s). `hasCrime` returns a host-set value (0).
* **KinematicVehicles**: v' = 5·throttle − 0.08·v − 9·brake (− 0.3 rolling when coasting), yaw rate 0.9·steer·min(1, v/8).

## Measured results (repository data, kinematic stand-in cars)

* Track entry (Test Track, Drag Track, Valo City): 0 not-consumed commands, viewport 0 shows the controller's follow
  camera, no script thread errors, `Vehicle.getSpeedSquare` / `Navigator.updateNavigator` / `Vector3.length` /
  `Ypr(Vector3)` no longer missing (before: getSpeedSquare and updateNavigator 580 stub calls each in the first ~13 s on
  Test Track; length/Ypr once per track entry).
* **TestTrack acceleration round** (`race --accel`, test `TestTrackAccelerationRoundIsTimedByTheScripts`): the player car
  is placed in round 0's start trigger facing `startdir 3.14`, the script's 1 s timer arms the round, full throttle. The
  scripts' own timing logic (triggers + `getSpeedSquare`) reports 1/4 mile **14.700 s**, 1 mile **37.233 s**, 0–100 km/h
  **7.233 s**; the closed form of the stand-in model gives 14.68 / 37.21 / 7.25 s (difference = frame quantisation). On
  the way the car crosses `Police_Trigger`; the script's `followCar` + `pressHorn` arrive as `AI_follow 0,0,0.001 car` and
  `sethorn 1`, and the police bot follows. Bit-identical across two runs.

## Integration steps for the lead

1. `Slrr.sln`: add `src/Slrr.Engine.Commands/Slrr.Engine.Commands.csproj` and `tests/Slrr.Engine.Commands.Tests/…csproj`.
2. CLI: in `tools/Slrr.Cli/Slrr.Cli.csproj` reference `src/Slrr.Game` and `src/Slrr.Engine.Commands`; in `Commands.cs` add
   `"race" => CmdRace.Run(args[1..], o, e),` (usage `CmdRace.Usage`). Then drop the `<Compile Include=…CmdRace.cs>` from the
   test project and reference Slrr.Cli instead (or keep it).
3. Host: either move `CommandsPhysicsWorld` (in CmdRace.cs) into Slrr.Game (it only needs `IPhysicsWorld`), or keep
   building hosts like `RaceSession`: `Physics = new CommandsPhysicsWorld(physicsAdapter)`,
   `ExtraModules = [new CommandNatives(() => physics.Engine)]` (register after HostNatives — it chains getInfo).
4. Physics adapter: pass `vehicles`/`driver` to `CommandEngine.ForResources(resources, vehicles, driver)` through the
   decorator's factory; read `engine.Cars.Get(carId)` (hold, steer, assists, reset/velocity serials) and AI `DriveInput`s.
   `PassCarVerbsThrough` is unnecessary with the decorator (it offers every command to both).
5. GroundRef traffic natives (another area): implement `addTrafficCar/remTrafficCar/setTrafficCarBehaviour/delTraffic`
   over `engine.Traffic.Add/Remove/SetBehaviour/RemoveAll`.

## Open questions (need traces of the original)

1. Camera body model: roles of `force` (3 floats), `torque`, `angle` (4 floats), `move`'s float, `autozoom`, `simulate`.
2. `AI_BeginRace f`, `AI_params a b`, `AI_level` scaling, `AI_spline`; how the exe's AI follows roads.
3. `ai_info_entertraffic` value order (Bot reads token 1 via `String.intValue` of a `0x..` token); `ai_info_rolled`.
4. Trigger participants (physics bodies?) and event order within a frame; trigger box extents (full vs half).
5. Car `stop`/`idle`/`brake` exact semantics; `filter a b` channels; GII_RENDER count per car (cfg?).
6. The main-menu QUICKRACE path did not finish within the run budget here (stalls before the race; not investigated).
