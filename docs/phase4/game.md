# Game host (`src/Slrr.Game`)

Phase 4, area **Game**: the composition root that boots the C# engine like `engine-boot` (Resources + System + Parts +
Ui native modules on one `Slrr.Vm`), runs a frame loop, injects input and drives the shipped script layer past the
splash screen. It also closes, **without editing the Engine.\* or Vm projects**, the gaps that blocked the next screen
(replacement/extension natives registered with `NativeRegistry` replace semantics, plus two in-memory script shims), and
defines the interfaces the Physics/Render/Audio areas implement.

Labels: **M** = measured (exe strings/registrations, shipped class/cfg data, or observed by running the shipped scripts
in our VM), **G** = guessed (chosen so the shipped scripts work; needs a trace of the original).

Build/test in isolation: `dotnet test src/Slrr.Game/Slrr.Game.sln -warnaserror` (11 tests; corpus tests pass
vacuously without game data).

## Files

| File | Contents |
|---|---|
| `GameHost.cs` | Composition root (`GameHostOptions`), original boot order, frame loop, missing-native recorder, save/load helpers |
| `EngineInterfaces.cs` | `IGameWorld`, `IPhysicsWorld`, `IRenderer`, `IAudioSink` + `NullPhysicsWorld`, `NullRenderer`, `RecordingAudioSink` |
| `HostNatives.cs` | The gap-closing natives (`HostGap` list) and the per-frame `animate`/`control` callbacks |
| `ScriptShims.cs` | In-memory class patches for VM behaviours the scripts rely on |
| `PlaceholderDyno.cs` | G stand-in `IDynoCalculator` until Slrr.Physics provides the engine model |
| `InputInjector.cs` | Keyboard, virtual axes, gamepad axes, mouse position, gadget clicks, drag-and-drop |
| `ScreenTracker.cs` | Transcript of screens (active GameState + top Osd owner + header text) |
| `GameView.cs` | VM-backed state views (money, car, installed parts, parts bin) and the play-script path evaluator |
| `PlayScript.cs`, `PlayCommand.cs` | Play-script parser/runner and the `play` CLI logic; `Scripts/smoke-garage.play` (embedded) |
| `tools/Slrr.Cli/CmdPlay.cs` | CLI wrapper (compiled by the test project until the lead wires it) |

**Lead integration:** add `<ProjectReference Include="..\..\src\Slrr.Game\Slrr.Game.csproj" />` to `tools/Slrr.Cli/Slrr.Cli.csproj`
and `"play" => CmdPlay.Run(args[1..], o, e),` to `Commands.cs` (CmdPlay.cs does not compile in Slrr.Cli before that), and
add `src/Slrr.Game` + `tests/Slrr.Game.Tests` to `Slrr.sln`.

## Composition and frame loop

`new GameHost(options)` builds `NativeRegistry.CreateDefault()` + `ResourceNatives` + `ArchiveNatives` + `EngineSystem`
(openLib/timers off, `Thread.sleep` from the VM, the same switches as `engine-boot`) + `PartsNatives` (with a
`HostPartHost`: part ids are GameRef handle ids, a Vehicle stands for its chassis) + `UiNatives` + `HostNatives`.
`Boot()` replays the measured start-up (system.rpk, VM bootstrap refs, `create system.rpk:0x47 "Config"`, Config
contract, `create Config.init "GameInit"` → `java.game.Init` → `GameLogic` → `SplashScreen`).

`RunFrame(dt)` (order G; the exe's order between event delivery, physics and rendering is not traced):
scheduled input → `UiDriver.Frame` (global hotkeys, `Frontend.render` notifyAll (M, 0x428db9), script threads for dt) →
`animate()`/`control(dt)` of objects that `registerCallback(GII_ANIMATE 28 / GII_CONTROL 8)` (names M, rate G) →
`ResourceEngine.Pump` (timers, queued events) → `IPhysicsWorld.Step(dt·timeWarp)` → `IAudioSink.Update` →
`IRenderer.Render` → screen tracking.

## Interfaces for Slrr.Physics / Render / Audio

* `IGameWorld` — what the host hands them: `Vm` (clocks), `Resources` (instance tree with positions, render/physics
  nodes, event plumbing), `Parts` (chassis contract snapshots, wheels, RawEdit settings), `Ui` (viewports, cameras,
  texts, sound volumes, input), `Frame`.
* `IPhysicsWorld { Attach(world); bool Command(targetId, text); Step(dt); }` — every `EVENT_COMMAND` to a non-part object
  (car/camera/bot verbs, entry-points.md section 5) is offered to `Command` first (true = consumed), else queued as before.
* `IRenderer { Attach(world); Render(time); }` — read-only. `HostNatives.Fog` holds `GroundRef.setFog` values.
* `IAudioSink { Attach; int Play(soundId, Vector3? pos, a, b, c, flags); Stop(handle); Update(dt); }` — `SfxRef.nplay/stop`.
* Engine model: pass an `IDynoCalculator` as `GameHostOptions.Dyno` (Parts' interface) to replace the placeholder.

## Input injection and play scripts

`InputInjector`: `KeyDown/KeyUp/TapKey/Type` (DIK scan codes + typed char), `SetAxis/TapAxis` (`Input.AXIS_*`),
`Gamepad(device, axis, value)` (edges for every mapped virtual axis crossing 0.5), `MoveMouse`, `Click(label|tooltip)`,
`ClickCommand(cmd)`, `OsdCommand`, `DropOnto(gadget, targetId)` (an `EC_LDROP` cursor event queued on the mouse cursor's
GameRef so every `MouseCursor.addHandler` subscriber gets it; token layout M from `Garage.handleEvent`, token 1 G).
Clicks search the input-queue Osds top-first, then every other active Osd (inventory/catalog panels are separate Osds).

`play [script.play | --smoke] [--root dir] [--save dir] [--strict] [--stop-on-fail] [--no-host-natives]` runs a script
(`wait`, `frames`, `until section|screen|text`, `click`, `click~`, `clickcmd`, `osd`, `key`, `type`, `axis`, `pad`,
`mouse`, `drop`, `save`, `load`, `expect`, `print`; see `PlayScript.cs`) and prints every step, the screens reached,
missing natives (first screen + script frame), script thread errors, host gaps and counters. Exit 0 = all steps ok.

## Measured results (repository game data, MWM MethanolFuel edition)

* **Smoke path** (`play --smoke`, test `EmbeddedSmokeScriptPassesThroughThePlayRunner`): **50/50 steps**, 0 missing natives.
  Screens: SplashScreen → MainMenu (3.03 s) → StringRequesterDialog → PlayerSetupDialog → Garage + WELCOME! →
  CarMarket (used dealer, "Duhen SunStrip 1.8 DVC $16326") → NoYesDialog BUY CAR → Garage → Catalog → RUNNING GEAR/OTHERS →
  BuyCatalogItemDialog ("installable right away to your car") → Garage, mechanic mode, drop → MainMenu.
  Money 20000 → 3674 (car) → 3381 (part, the catalog's $293); car parts 72 → 73 with
  `java.game.parts.running_gear.sways.swaybar_Street_front` on chassis slot 701; 22.8 s virtual, 697 frames,
  ~3.0 M instructions, 3,353 instances, ~3 s wall. Deterministic (VM RNG seed 12345).
* **Step-by-step VM assertions** (test `MainMenuNewCareerGarageBuyPartInstallLeaveAndReload`): name, money at every
  step, car class, part count, bought part type id, parts-bin size, the `install 0 …` command on the part.
* **Baseline without the host natives** (`--no-host-natives`): 25/50, stuck in `Garage > SimpleLoadingDialog`
  (`InventoryPanel.attachItem` → `Button.setToolTip(Void)`), `SfxRef.nplay` and `Vector3.rotate` missing.
* **Parts linking** during the dealer's 6 stock cars: 297 part commands, 19 linked only by the car-tree fallback,
  55 only by the reverse rule, 2 unlinked (a Baiern clutch on an Einvagen crankshaft, a SunStrip rear brake).
* **Save/load round trip**: leaving the garage autosaves `save/carrer/Bob-1/{main,PlayerCar0}`; a second host loads it
  through MainMenu → LOAD CAREER → "Bob-1": name and money (3381) come back; the car does not (VM gap 1 below, asserted).
* **Race selection**: MainMenu → SELECT TRACK reaches `TrackSelect` with the 12 maps (Car Dealer, Caynon, Damage Track,
  Dead Island, Drag Track, …); TAKE IT stops in `TrackSelect.exit` (VM gap 2). Garage → Choose Track answers
  "the engine is not in working condition" (VM gap 1 prevents `calcDyno`).
* **Native coverage**: 358 distinct natives declared in the shipped classes; 298 registered with the host; 60 not:
  GroundRef 20, Animation 9, ParticleSystem 9, Vector3 5, PhysicsRef 4 (declared, not in the exe), Painter 3,
  Vehicle 3 (`getSpeedSquare`, `getHorn`, `hasCrime`), Ypr 2, Camera 2, `GameLogic.kismajomCheck`, `Navigator` 1, `RenderRef.getPos`.

## Host gaps closed (natives and shims)

| Key | Kind | What blocked / what it does | Label |
|---|---|---|---|
| `GameRef.create(GameRef,GameRef,String,String)` | replace | Parts never got their cfg definition (Resources runs `<init>(int)` immediately), so slot tables were empty and no install linked. Same create, then `PartsWorld.Attach(obj, cfg of the gametype's native part/car line, typeId)` before `<init>`; cfg type ids mapped through the gametype archive's reference table | create order M; cfg mapping M (same rule as map records) |
| `GameRef.queueEvent(ResourceRef,int,String)` | replace | `EVENT_COMMAND` to parts only reached the event queue. Part verbs → `PartsWorld.ExecuteCommand` synchronously; then (G) car-tree search when the named parent has no slot (`Part.addPart` always names `this`); then (G) the reverse rule child `attach` × parent `compatible` (every tyre `attach 0x00020200 2` vs rim `compatible 0x00020200 2`, M from cfgs); then the instance node is re-parented under the parent part (M: `Part.addPart` destroys the new part unless `getParentID()` changed; parent part vs car root G); `remove f mapId` → child of mapId. Other verbs → `IPhysicsWorld.Command`, else the original queue | see text |
| `GameRef.getInfo(II)I`, `(I String)I` | replace | Parts' native answers (`GII_PART_OWNER`, `GII_INSTALL_OK`, `GII_COMPATIBLE`, `GII_REMOVE_OK`, `GII_PART_CATEGORY`) were unreachable; the String overload carries the other id as text (`p.getInfo(GII_COMPATIBLE, car.id() + "")`, M) | keys M, answers G |
| `GameRef.isScripted(String)I` | replace | Category folders (e.g. parts.rpk:0xF23E Tyres) inherit `script java.game.parts.Part`, so `Catalog.collectObjectsStep` took every folder for a part (no tyres, one `<unnamed>` item). An entry that only inherits its script and has no `native` line answers 0 | behaviour M (Catalog), rule G |
| `Chassis.forceUpdate()V` | replace | Nobody called the script `Chassis.updatevariables()`, so `calcDyno` never ran. Now forceUpdate runs it first (not re-entrant; script errors caught and listed) | exe strings M, placement G |
| `SfxRef.nplay(Vector3,FFFII)I`, `SfxRef.stop(I)V` | new | every menu click; → `IAudioSink` | signature M, handle G |
| `Vector3.rotate(Ypr)` | new | every inventory panel camera, MainMenu car camera; rotates in place and returns this | in-place M (call sites), roll→pitch→yaw order G |
| `GroundRef.setFog(IFF)V` | new | garage scene; stored in `HostNatives.Fog` | roles G |
| `animate()` / `control(dt)` callbacks | new | `registerCallback(GII_ANIMATE/GII_CONTROL)` was stored but never invoked (Osd tooltips/text input, MainMenu camera) | names M, once per frame G |
| shim: empty non-void object methods return null | in-memory | `InventoryItem.getInfo()` has an empty body; the VM returns Void, `button.setToolTip(Void)` fails and Garage.enter dies in its loading screen. 2 methods patched with the compiled `return null;` sequence copied from `Vehicle.iteratePartsInit` | sequence M, original behaviour G |
| `PlaceholderDynoCalculator` | option | NullDynoCalculator leaves `maxTorque` 0 → every car "not in working condition". 1.0 MPa BMEP × displacement, peak at 60 % of the rev limit; inoperable flag → 0 | G, replace with Slrr.Physics |

## VM gaps found (for the Slrr.Vm owner; not fixable from natives)

1. **`&&` / `||` with float operands** throw "non-integral operands" (`VmThread.Ops.cs` Binary, `AndAnd/OrOr` accept only
   Int/Object). `Block_Inline.updatevariables` line 137 / `Block_Vee` line 172 (`if (f4 && f2)`) → no dyno → no car is
   driveable (Garage track selection, test track, ROC) and a saved car does not load (`Vehicle.load` → updatevariables).
   Fix: treat floats like `!f` already does (non-zero = true). **Top blocker for racing.**
2. **`instanceof` on a non-object** throws "object expected, found 0": `TrackSelect.enter/exit` evaluate
   `!gameState1 instanceof Garage` (bytecode order: NOT then instanceof). The exit stops, so TAKE IT never enters the track.
   Fix (G): instanceof of a number = 0.
3. **int → String assignment**: `GameLogic.run` (clock thread) `dayTxt.changeText(day)` with an int `day` →
   "cannot assign 1 to java.lang.String"; the garage DAY/TIME texts stay empty. Fix (G): convert with `"%d"` like string concatenation.
4. **String literals are not interned**: `Tyre.SetupTyre` tests `name == "<unnamed>"` (reference compare, M at 0x41c5c0),
   never true, so catalog tyres are listed as `<unnamed>` (cosmetic). Fix: one canonical String per literal text per VM
   (`EngineSystem.Intern` is the natives' pool).
5. **Falling off the end of a non-void method** pushes Void (handled here by `ScriptShims`; the VM should push the default value of the return type).

## Minimum engine surface still needed to reach an actual race

In the order the scripts hit it after `TrackSelect → TAKE IT` (or Garage → Choose Track):
1. VM gaps 1 and 2 (then 3).
2. An engine model (`IDynoCalculator`, Slrr.Physics) so `Block.dynodata.maxTorque > 0` and `Vehicle.isDriveable()` passes
   (the placeholder already satisfies the check once gap 1 is fixed).
3. Map loading for `Track`/`TestTrack`/`City`: `GroundRef` (20 unregistered natives: water, traffic, pedestrians, road
   queries such as `getNearestCross`, `alignToRoad`), placement of the map archive's RSD instance records, `Animation` (9),
   `ParticleSystem` (9), `Navigator`, `RenderRef.getPos`, `Camera` (2 left), `Vector3`/`Ypr` math (5 + 2 left).
4. Car simulation behind `IPhysicsWorld`: the car verbs (`start`, `idle`, `stop`, `reset`, `brake`, `setsteer`,
   `filter`, `transmission`, `wakeup`, `suspend` …), chassis telemetry (`Chassis.getIndexedData`), `Vehicle.getSpeedSquare`,
   collisions/triggers as `EVENT_COLLISION`/`EVENT_TRIGGER_ON` (race start/finish lines), the `controller`/`camera` verbs
   (`controllable`, `camera`, `look`, `dist`, `zoom`, `render`) and bot brains (`AI_*`, open question P1 #9).
5. Race bookkeeping is script code once 1–4 run (Racer/Bot/Round/RaceDialog).

## Open questions

1. Frame order (input, events, physics, render) and the rate of `animate`/`control` callbacks (P0 #3).
2. Does the exe call `updatevariables` from `forceUpdate`, after every link, or on `wakeup`? Which instance parent does an
   installed part get (parent part vs car root)?
3. Are inherited-only gametype folders really "not scripted" for `isScripted`, or does the exe's tree walk skip them otherwise?
4. Link rules (P0 #5): is the reverse attach/compatible rule real, and does an explicit parent part fall back to the car tree?
5. `EC_LDROP` token 1, and `SfxRef.nplay`'s floats and return value (P1 #10).
6. Save compatibility of `write(ResourceRef)` ids across sessions (P1 #12): the round trip here only uses one session's ids.
