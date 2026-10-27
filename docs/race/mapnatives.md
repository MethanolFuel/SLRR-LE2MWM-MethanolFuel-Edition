# Race wave area: map natives (`src/Slrr.Engine.MapNatives`)

The natives that let the script layer load and query a race map: `GroundRef` (21 declared + 5 registered-only),
`Animation` (9), `ParticleSystem` (9) and the 7 `Vector3`/`Ypr` math natives `Track.enter`/City/ROCTrack call that no
area registered. Backed by Slrr.World (`StaticWorld`, `CollisionWorld`/`IGroundQuery`, placed objects) and the
Resources tree. Labels: **MEASURED** (shipped class files/data, or observed running the shipped scripts in our VM),
**GUESSED** (chosen so the scripts work; not traced in the exe). Nothing here claims parity with the original game.

Isolated build/test: `dotnet test src/Slrr.Engine.MapNatives/Slrr.Engine.MapNatives.sln -warnaserror` (11 tests;
`SLRR_ALL_MAPS=1` adds the 12-map run with and without the module, ~40 s; corpus tests pass vacuously without game data).

| File | Contents |
|---|---|
| `MapWorlds.cs` | `MapWorlds` (ground id → `MapState`, lazy `StaticWorld` build, unload, placed-object instances), `MapState`, `IMapWorldListener`, `IMapGroundProvider`, `FogSetting`, `WaterPlane`, `TrafficEntry` |
| `GroundNatives.cs` | the GroundRef natives |
| `AnimationNatives.cs`, `ParticleNatives.cs` | Animation (state in `VmObject.Native`), ParticleSystem (render instance + state in `ResourceHandle.Extension`) |
| `MathNatives.cs` | `Vector3.<init>(Ypr)`, `length`, `distance`, `normalize`, `rotate(Ypr)`, `rotate(Vector3,F)`, `Ypr.<init>(Vector3)`, `Ypr.mul` (registered only where missing) |
| `MapNativesModule.cs` | the `INativeModule` (replace semantics for GroundRef/Animation/ParticleSystem; wraps `ResourceRef.unload()V`) |
| `tools/Slrr.Cli/CmdMaps.cs` | `maps` CLI + `TrackLoadHarness` (compiled by the test project until wired) |

## Interfaces for other areas

* **Physics / vehicles:** `IMapGroundProvider.GroundFor(instanceId)` walks the instance's parents to a loaded ground
  and returns its `IGroundQuery` (the car is `setParent(map)` by `Track.lockCar`, MEASURED in our run: parent = ground
  for all 12 maps). `MapState.World.DynamicBodies` lists movable object bodies.
* **Renderer:** `IMapWorldListener { MapLoaded, MapUnloaded, FogChanged }` (add to `MapWorlds.Listeners`); `MapState`
  carries `World.Render`, `Fog`, `Water`, `WaterLimits`. ParticleSystem render instances carry `ParticleState` in
  `node.Properties["particles"]`; Animation clocks are `AnimationState` (`getPos` = time × speed; no skeleton posed).
* **Game host:** `MapNativeOptions.FogSink` keeps `HostNatives.Fog` in sync (this module replaces the host's `setFog`).

## Behaviour

| Native(s) | Behaviour | Label |
|---|---|---|
| all signatures | from system/Scripts/util/resource/{GroundRef,Animation,ParticleSystem}.class and lang/{Vector3,Ypr}.class | MEASURED |
| world load | first GroundRef native call on a ground builds `WorldLoader.Load(ground's archive)`; every Track.enter calls `setFog` (Scene.addSceneElements) and most `setWater` before the car is placed | trigger GUESSED, call order MEASURED |
| `ResourceRef.unload()` on a ground | original registration runs, then the world and its placed-object instances are dropped (`Track.exit` ends with `map.unload()`) | call site MEASURED, effect GUESSED |
| placed objects | one type-1 instance per RSD object record under the ground (gametype mapped through the archive's reference table, pos/ypr from the record); no object script runs | GUESSED |
| `setFog(IFF)`, `setWater(V,V,F,F)`, `setWater(FFF)`, `addWaterLimit` | stored | roles GUESSED |
| `alignToRoad(p)` | `[ (p.x, ground y, p.z), (0,0,1) ]`; ground = first surface at or below p.y+2 m, else top-most | GUESSED (no `.trc` road graph) |
| `getNearestCross(p, d)` | p projected onto the ground | GUESSED |
| `getStartDirection(a,b)` | unit horizontal a→b | GUESSED |
| `getRouteLength`, `findRoute`, `getRouteDist`, `getRoutePos` | straight segment | GUESSED |
| traffic (`addTrafficCar/N/P`, `rem`, `notify`, `delTraffic`, `setTrafficCarBehaviour`, `haltTraffic*`) | recorded; ids from 1; spawners return 0 | GUESSED |
| pedestrians (`setPedestrianDensityN`, `add/rem/removePedestrianType`, `pedestrianDistance` = -1) | recorded | GUESSED |
| `Animation.<init>(RenderRef,ResourceRef)` | clock state; stores the RenderRef in field `obj` | field MEASURED, rest GUESSED |
| `ParticleSystem.init(cfg,parent,name)` | type-3 render instance under parent, TypeId = cfg; other 8 natives store emitter state | GUESSED |
| Vector3/Ypr math | yaw about +Y, pitch about X, roll about Z = `Quaternion.CreateFromYawPitchRoll`, yaw 0 faces +Z — the same convention as the host's `Vector3.rotate` and Slrr.World's MEASURED placement fit | GUESSED for scripts |

## Measured: the 12 track-selector maps through the scripts

Path (each map on a fresh `GameHost`, VM seed 12345): boot → MainMenu → `SELECT TRACK` (demo car) → TrackSelect →
`CMD_MAP0+n` (page `CMD_DOWN` for n ≥ 10) → `TAKE IT` → `MapBox[n].StartMap()` (options dialog → OK for Car Dealer and
Physic Lab) → `GameLogic.changeActiveSection(new XTrack())` → `Track.enter` → 3 s of frames → back with
`changeActiveSection(parentState)` (`Track.exit`, `map.unload()`). "Entered" = the Track is the active section and its
`enableAnimateHook()` (near the end of `Track.enter`) registered GII_ANIMATE.

**12/12 enter and exit back to TrackSelect, 0 script thread errors, every world built, every `unload` drops it.**
Ground y under the script's `posStart`, via the script-visible `map.alignToRoad(posStart)[0].y` and
`map.getNearestCross(posStart,0).y` run in the VM, equals Slrr.World's `TryGetGround` on all 12 (same code path, so
this checks the plumbing, not the original's road snapping). Wall times on a shared 4-vCPU box (±30 %):

| Map (class) | ground archive | tris | objects | build ms | enter ms | posStart.y | ground y | surface |
|---|---|---|---|---|---|---|---|---|
| Car Dealer (DealerTrack) | misc\dealer.rpk | 366 | 0 | 38 | 275 | 0 | 0 | material 1 |
| Caynon | Addon_Maps\Caynon | 5,735 | 0 | 60 | 164 | 8.7 | 8.55 | Groung |
| Damage Track | maps\DamageTrack | 3,075 | 1 | 27 | 139 | -2.12 | -2.126 | material 2 |
| Dead Island | Addon_Maps\Dead_Island | 1,634 | 0 | 8 | 96 | 2.55 | 2.632 | Sand2 |
| Drag Track | Addon_Maps\Drag_Track | 23,664 | 0 | 57–96 | 123–197 | -0.5 | -0.338 | asfalt |
| Drift Track | maps\DriftTrack | 34,500 | 0 | 85 | 176 | 10.4 | 10.365 | material 1 |
| MP1 Drift Track | maps\MP1 | 3,247 | 0 | 8 | 74 | -0.36 | -0.217 | _p_asfalt |
| Nevada Desert | maps\ROC_track | 60,776 | 1,791 | 252 | 340 | 0 | 0.01 | material 29 |
| Physic Lab | Addon_Maps\Physic_Lab | 5,993 | 0 | 24 | 132 | 445.38 | 445.381 | Road |
| Test Track | maps\test_track | 81,397 | 1,413 | 195 | 288 | -3.8 | -3.81 | material 4 |
| Top Gear Track | Addon_Maps\Top_Gear_Track | 7,825 | 0 | 20 | 107 | 0 | 0.001 | 1 - Default |
| Valo City (Valocity) | maps\city | 297,664 | 10,168 | 902 | 998 | 12.9 | 12.89 | Cross |

posStart is within 0.17 m of the ground everywhere; on Drag Track, Dead Island and MP1 it is 0.08–0.16 m **below**
the PHYS surface, so the exe must lift/settle the car itself (`reset`/`reload` commands; physics area).
GroundRef calls seen: `setFog` (all), `setWater` (Drag, Dead Island, Valocity), `addWaterLimit` (Damage, Drift, Valocity),
Valocity also `addTrafficN` ×9, `addPedestrianType` ×6, `setPedestrianDensityN`. No map calls Animation or ParticleSystem
during enter (City's starter lady / ROCTrack's crowd only appear in races).

**Without this module** the same 12 maps also "enter" (the host stubs missing natives as no-ops), but no world exists,
the scripts' GroundRef calls (`setWater`, `addWaterLimit`, `delTraffic`, `setPedestrianDensityN`, traffic, pedestrians)
and `Vector3.length` / `Ypr(Vector3)` (Track.makeCamExternal) are reported missing.

**Still missing after this module (other areas):** `Vehicle.getSpeedSquare()F` and `Navigator.updateNavigator(GameRef,I)V`,
91 calls each in 3 s on Nevada, Test Track, Top Gear and Valocity (`Track.animate`). They do not block entering.

## Integration (lead)

1. `Slrr.sln`: add `src/Slrr.Engine.MapNatives` and `tests/Slrr.Engine.MapNatives.Tests`.
2. `tools/Slrr.Cli/Slrr.Cli.csproj`: `<ProjectReference Include="..\..\src\Slrr.Engine.MapNatives\Slrr.Engine.MapNatives.csproj" />`;
   `Commands.cs`: `"maps" => CmdMaps.Run(args[1..], o, e),` (+ usage line). `CmdMaps.cs` does not compile in Slrr.Cli before that.
3. `GameHost` (Slrr.Game references the new project): after `Resources` exists,
   `MapNatives = new MapNativesModule(Resources, new MapWorldOptions { GameRoot = Root }, new MapNativeOptions { FogSink = (id, c, n, f) => Natives.Fog[id] = (c, n, f) });`
   and `registry.Add(MapNatives)` **after** `HostNatives` (replace semantics take over `setFog`). Expose `MapNatives.Maps`
   as `IMapGroundProvider` to `IPhysicsWorld` and register the renderer as an `IMapWorldListener`.
   (Without editing GameHost: pass it in `ExtraModules` with `resources: null` and call `Attach(host.Resources)` — what the harness does.)

## Open questions

1. When the exe builds a map (on `new GroundRef`, on first reference, on `setParent(map)`), and whether `unload()` frees it.
2. Whether RSD object records become script-visible instances (`getFirstChild` of the ground) and whether scripted object gametypes run.
3. The `.trc` road graph (city.trc, 843 KB binary, unparsed) behind `alignToRoad`/`getNearestCross`/`getStartDirection`/routes; `alignToRoad`'s second element.
4. Argument roles of `setWater` (300, 50), `addTraffic*` floats, `pedestrianDistance`, ParticleSystem source actions.
5. The Vector3/Ypr angle convention for scripts (yaw sign, `Ypr.mul` order) — needs one trace of `new Ypr(new Vector3(1,0,0))`.
6. Why several start positions sit below the PHYS ground (what `reset`/`reload` do to the car's height).
