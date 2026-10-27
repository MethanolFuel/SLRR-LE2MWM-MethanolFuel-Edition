# Gaps wave: AI opponents, race director, traffic (`src/Slrr.AI`)

AI drivers for simulated cars, the race director behind the commands layer's `IRaceDirector`, managed headless races
(grid, countdown, checkpoints, laps, positions, finish, DNF, results) and lane-following traffic. Labels: **MEASURED** =
static disassembly of `StreetLegal_Redline.exe` (address given), the shipped decompiled scripts or shipped data, or observed
by running our code (numbers below); **GUESSED** = chosen to make it work. **Nothing here was compared with the original
game; no parity is claimed.** No repo binary was executed.

```
dotnet build src/Slrr.AI/Slrr.AI.sln -warnaserror
dotnet test  src/Slrr.AI/Slrr.AI.sln             # 21 tests, ~4 min (two 8+30-car races); corpus tests pass vacuously without data
slrr ai-race [--cars 8] [--traffic 30] [--laps 2] [--rubber] [--no-measure] [--finish-timeout s]
slrr ai-race --spl cars/racers/ROC_cars/roc_DS.spl --root . --cut 2400 --cars 2   # shipped spline on maps/ROC_track
```

## Files

| File | Contents |
|---|---|
| `Roads/IRoadSource.cs` | **cross-area interface** `IRoadSource`, `IRoadPath`, `PathSample`, `RoadPoint`, `GridSlot`, `Checkpoint`; `RaceLayout` (grid, checkpoints) |
| `Roads/SampledPath.cs` | Hermite/Catmull-Rom centreline resampled to a uniform 1 m arc-length table (O(1) `Sample`, windowed `Project`, signed curvature); `SubPath` |
| `Roads/PathSetRoadSource.cs` | graphless `IRoadSource` over a set of routes/lanes (route = along one path) |
| `Roads/SplFile.cs` | AI spline files (`*.spl`) |
| `Roads/SyntheticRoads.cs` | turtle-built 1.59 km race circuit + 2.06 km ring road with 4 directed lanes |
| `Driving/PathFollowingDriver.cs` | the AI driver (`IVehicleInputSource`), `DriverSettings`, `DriverMode`, recovery |
| `Driving/SpeedProfile.cs` | curvature/braking speed profile of a path |
| `Driving/VehicleCaps.cs` | car limits (geometry, grip, braking, roll limit); `AiLevel` (exe transform, params) |
| `Race/AiRaceDirector.cs` | `IRaceDirector` over Commands' `RaceDirector`: brain verbs → drivers on physics cars; `Gate`; `GameFiles` |
| `Race/RaceSession.cs` | managed race: grid, countdown, checkpoints/laps, standings/gaps, finish, DNF, rubber-banding (off) |
| `Race/RaceResults.cs` | results rows; `RaceDialog` arguments (1000.0 = "--:--:--") |
| `Race/HeadlessRace.cs`, `Race/MapRace.cs`, `Race/SyntheticCars.cs` | the headless proof harness; a route fitted from a shipped spline + map |
| `Traffic/AiTrafficDirector.cs` | `ITrafficDirector`: spawn/despawn SimpleCars around a focus, lane following, gap keeping, `car_add`/`car_rem` |
| `tools/Slrr.Cli/CmdAi.cs` | `ai-race` CLI (`#if SLRR_AI`, compiled by the test project) |

## What the scripts and the exe show (MEASURED)

* **Brain verb handler** (exe 0x42cec0, strings 0x60d914..0x60da2c). Mode field +0x259c: `AI_stop` 0 (and brake input
  +0x2250 = 127, i.e. full), `AI_suspend` 1, `AI_race` 2, `AI_spline` 3, `AI_follow` 4, `AI_GoToTrafficSlow` 5,
  `AI_GoToTraffic` 6, `AI_GoToTarget` 7, `AI_BeginRace` 8.
  * `AI_BeginRace %f`: default 1.0, clamped to [0, 1], stored at +0x25dc. Scripts: City/ROCTrack send 0.5 on the grid
    (ROC at "3") and 1.0 at "1" while the car is held (`stop; idle`).
  * `AI_race %f,%f,%f %d` (0x433670): appends the point to a target list (+0x260c, count +0x2684), queries a global road
    object (0x798a54) for the car's and the target's position, sends the car `transmission 0`, keeps the opponent, takes one
    of 8 race slots (table 0x60d6e4..0x60d704).
  * `AI_spline %f %s %d` (0x4337e0): loads the file with `"%lf %lf %lf %lf %lf %lf %lf %lf %d %lf %lf %d"` per line (≥ 6
    values, defaults 1.0 / 10000 / 10000 stored before the scan, 7th value < 0 replaced by the verb's float), one spline
    segment per consecutive pair (0x57de20 gets both positions and directions), mode 3, `transmission 0`.
  * `AI_level %f`: stored = 0.5 + (clamp(v, 0, 1) − 0.5)·0.5 (0.25..0.75), default 0.5; `AI_NightRace [%f=100]` resets it to 0.5.
  * `AI_params %f %f`: two floats stored twice; shipped ROC values "8.1 0.36", "4.0 0.4", "5.4 0.36", "7.5 0.4", "7.5 0.42",
    "8.0 0.4" (Roc.aiParams), TestTrack police "50.0 50.35". `AI_horn` stores 127/0. `ai_info_rolled` exists (0x60dac8).
* **Scripts** (decompiled City, ROCTrack, Bot, Roc, RaceDialog): Bot.enterCar sends `AI_level`, `controllable`, `AI_suspend`;
  City day race grid = 2 cars 1.75 m left/right of the start facing `getStartDirection(start, finish)`, countdown timers
  9→12 one second apart ("3", "2", "1" + `AI_BeginRace 1.0`, "GO!" + `start` + `AI_race finish opp`), finish = one trigger,
  the first car id in it wins, the bot is stopped with `AI_stop`; night race DNF timer 10 s after the first finisher;
  ROC = `AI_params` + `AI_spline 0.5 cars/racers/ROC_cars/roc_XX.spl playerCarId` (lowercase name, file is uppercase);
  results go to `RaceDialog(…, winnerName, loserName, winnerTime, loserTime, prize, mode, …)`, time 1000.0 prints "--:--:--".
* **Spline data**: 7 ROC car splines (57 lines × 12 columns, 4.2 km), `maps/ROC_track/race.spl` (52 × 9); directions
  |d| 0.988..1.022; column 11 = previous column 10 on 45..47 of 56 transitions; columns 10/11 hold 10000, 95..300 or 2000..3000.

## Behaviour (GUESSED unless marked)

* **Driver**: pure pursuit from the rear axle, look-ahead 4 + 0.42·v m (6..40); speed target from `SpeedProfile`
  (v = sqrt(aLat·GripUse/|κ|), κ capped at 1/8 m, backward braking pass at BrakeDecel·BrakeUse, previewed 0.5 s);
  steering clamped to the lateral limit at the current speed (never below 1.3× what the road needs), shrinking with braking;
  P throttle/brake, traction control and ABS on FullCars; **the driver shifts a FullCar itself in manual mode** (the exe
  sends `transmission 0`, MEASURED; our automatic box downshifted to 1st on locked wheels and spun the car).
  AI level (stored 0.25..0.75) → GripUse 0.80..0.95, BrakeUse 0.75..0.90. `AI_params a b` → lateral limit a m/s², braking b g.
* **Car limits** (`VehicleCaps.Measure`): skidpad and 100-0 of Slrr.Physics' scenarios, then lateral ≤ 0.6·g·track/(2·h_CoM)
  (**roll limit**: Slrr.Physics cars tip over before they slide, MEASURED: the synthetic car rolled at ~0.7 g with a 0.96 g
  skidpad), braking ≤ 0.8 g.
* **Avoidance**: cars ahead within 12 + 1.6·v m and 3 m of the own line: pass on the side with room (racers) or keep
  8 m + 1 s (traffic, and racers without room); lateral offset changes ≤ 2 m/s.
* **Recovery**: body roll > 18° → lift; upside down/on its side ≥ 2 s (accumulated) → respawn upright 10 m back on the path;
  wanting speed but < 1 m/s for 2.5 s → reverse 2 s (twice), then respawn; > half width + 20 m off the path for 5 s → respawn.
* **Brain verbs** in `AiRaceDirector`: `AI_BeginRace f` → brake held, revs at f; `AI_race` → `IRoadSource.Route(car, target)`
  (straight line if none), drive through the target; `AI_spline` → the file (case-insensitive lookup), tangent scale 0.33
  (FITTED, below); `AI_stop` → full brake; `AI_follow`/`AI_GoToTarget`/`AI_suspend`/traffic stay with Commands' director.
* **Race session**: grid per City (columns ±1.75 m, MEASURED) in rows 8 m apart; 3 s countdown (MEASURED cadence), launch
  revs 0.5 → 1.0 at "1"; checkpoints every 150 m, the last is the line; laps; standings (finished by time, then progress);
  gaps at 10 m marks; DNF after `FinishTimeout` past the first finisher (10 s MEASURED for City's 2-car night race, the
  8-car test uses 120 s), 60 s without progress, or 900 s; finished cars brake (open routes, as the scripts do) or do a
  12 m/s cool-down lap (closed). Rubber-banding off by default (±5..8 % speed vs the player or the field mean).
* **Traffic**: SimpleCars spawned 60..300 m from the focus on random lane points with 18 m clearance, rolling at 0.8 × cruise,
  removed beyond 380 m; cruise 12 m/s ±15 % (slow 6, as the commands layer had), lateral 2.5 m/s², gap keeping, successor lane
  at lane ends; `car_add`/`car_rem` to the map (format MEASURED). Seeded xorshift; spawned ids from 0x71000000.

## Measured results (our code on this machine, 4 shared vCPUs; no comparison with the game)

| Run | Result |
|---|---|
| 8 AI full cars (synthetic 1200 kg car, torque ×0.9..1.25, AI_level 0..1) + 30 traffic, 2 laps × 1587 m | **all 8 finish**, 2:36.73 … 2:52.33, best lap 1:13.53; 0 % of steps outside the 7 m half width for 4 cars, ≤ 1.6 % for the others (respawns); 4 roll respawns in the field; 10 car/car contacts |
| Determinism | two runs → identical state hash (e.g. `8844B14F1BB0E374`) and identical results |
| Step cost, 39 cars (8 FullCar + 30 SimpleCar + focus) | **1.64-1.71 ms per 1/120 s step** (physics + AI); AI drivers 0.13 ms of it; traffic director 0.005-0.01 ms per frame; 8 FullCars alone 0.82 ms/step. 31 SimpleCars cost 0.84 ms/step of physics without any AI (Slrr.Game.Physics, O(n²) car/car pass) |
| Traffic | peak 30, 64 spawned / 34 despawned in 175 s, min same-lane gap 6.5 m, 0 traffic contacts after the steering fix |
| Rubber-banding (4 cars, 1 lap) | finishing spread 10.16 s → 6.48 s |
| `AI_race` via the director (synthetic circuit, 600 m) | arrives after 31.4 s, max lateral error 0.51 m |
| ROC: `roc_DS.spl` on `maps/ROC_track.rpk` | tangent scale fitted: 1.0 → 16 route pieces cross map geometry, 0.5 → 12, **0.33 → 10**; 92 % of the route has ground within 3 m. 2 AI cars race the first 2400 m: 2:02.78 and 2:15.79, max lateral 3.1 m, 0 recoveries |

## Integration steps for the lead

1. `Slrr.sln`: add `src/Slrr.AI/Slrr.AI.csproj` and `tests/Slrr.AI.Tests/Slrr.AI.Tests.csproj` (drop `src/Slrr.AI/Slrr.AI.sln`).
2. CLI: `tools/Slrr.Cli/Slrr.Cli.csproj` → `<ProjectReference Include="..\..\src\Slrr.AI\Slrr.AI.csproj" />` and
   `SLRR_AI` in `DefineConstants`; `Commands.cs` → `"ai-race" => CmdAi.Run(args[1..], o, e),` + usage line `CmdAi.Usage`.
3. Host (`Slrr.Race.RaceHost`), no change in Commands needed:
   ```csharp
   AiRaceDirector? ai = null;
   // in the CommandsPhysicsWorld factory: the gate drops the old straight-line AI inputs for the cars `ai` drives
   var engine = CommandEngine.ForResources(res, bridge, AiRaceDirector.Gate(bridge, () => ai));
   ai = new AiRaceDirector(engine.Race, physics.Scene, roads /* Roads area's IRoadSource, may be null */, GameFiles.Resolver(root));
   // every frame after CommandsPhysicsWorld stepped the engine:
   ai.Sync();
   ```
4. Traffic: `engine.Race.Traffic = new AiTrafficDirector(physics.Scene, roads, engine.Events) { Focus = () => playerPos, MapId = map }`
   and call its `Step(dt)` per frame; implement the GroundRef natives `addTrafficCar/remTrafficCar/setTrafficCarBehaviour/delTraffic`
   on it (`Add/Remove/SetBehaviour/RemoveAll`). Spawned SimpleCars have no resource instance: the render area must draw them
   from `ICarTelemetrySource` (or the lead maps them to traffic gametypes).
5. Roads area: implement `IRoadSource` (nearest point, `Route(from, to)`, directed lanes + `Next`, optional checkpoints/grid);
   `PathSetRoadSource` is the stand-in.

## Open questions / findings for other areas

1. The exe's AI follow logic, how `AI_race` routes on the road object at 0x798a54, the meaning of AI_spline columns 7-12 and
   of `AI_params`, the spline tangent scale (0x57de20), what `ai_info_rolled` triggers.
2. **Slrr.Physics** (not fixed here): cars roll over below their skidpad grip (static rollover ~1.07 g, rolls seen at ~0.7 g
   in transients and after 2-4 m/s car/car contacts); a tumbling car accelerates (13 → 34 m/s with zero throttle in 3 s):
   energy is added while upside down.
3. **Map/physics**: on ROC_track both shipped ROC splines lead the cars at x 61..88, z 205..213 (s ≈ 2450 m of roc_DS) into
   geometry they cannot pass (wheels spin at ~6700 rpm at 2.5 m/s, friction 1.0, also without object collision); cause not found.
4. Traffic id/instance mapping for spawned cars, and whether the exe's traffic cars avoid the player (here: yes, gap keeping).
