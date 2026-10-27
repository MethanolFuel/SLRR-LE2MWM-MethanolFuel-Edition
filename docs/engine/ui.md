# UI area: headless java.render / java.sound / input (`src/Slrr.Engine.Ui`)

Phase 3 wave 2. Headless engine side of the script UI: the natives of `java.render` (Camera, GfxEngine, Text,
Viewport), `java.sound.Sound` and the input classes `java.io.Input`, `Controller`, `MouseCursor`, plus a driver
that pumps frames and simulates keys and mouse clicks. Nothing is drawn or played. Everything above the natives
(Osd, Group, Menu, Gadget/Button/Slider/MultiChoice, Dialog and its subclasses, Frontend, LoadingScreen,
HotkeyWatcher) is the shipped script code running in `Slrr.Vm`.

Build/test in isolation: `dotnet test src/Slrr.Engine.Ui/Ui.sln` (12 tests; corpus tests pass vacuously without game data).

## Files

| File | Contents |
|---|---|
| `UiNatives.cs` | `INativeModule` with the 64 natives (Camera 3, GfxEngine 11, Text 9, Viewport 11, Sound 10, Input 11, Controller 7, MouseCursor 2) = every exe registration of these classes (test `RegistersExactlyTheExeNativesOfTheUiClasses`). |
| `UiEngine.cs`, `UiModels.cs`, `InputState.cs` | Per-VM state: viewports, cameras, text instances, hotkeys, controllers' virtual-axis tables, video, sound, input queues; hotkey dispatch. |
| `UiDriver.cs` | Frame pump, front-end boot, simulated keys/axes/clicks, scene views (visible texts, gadgets, top Osd). |
| `IUiWorld.cs` | The minimal interface to other areas (ids, subscriptions, event masks), default `ScriptUiWorld`. |
| `Script.cs` | Field/static/call/Vector helpers for C# code that inspects script objects. |
| `UiSurvey.cs`, `tools/Slrr.Cli/CmdUi.cs` | Before/after runnable-class counts (`ui --survey [root] [--dynamic]`, `ui --natives`). |

Wiring: `registry.Add(new UiNatives(engine))` and `new UiDriver(vm, engine)`, or `UiDriver.Create(classPath, configure)`.
**Integration note:** `CmdUi.cs` needs a `ProjectReference` from `Slrr.Cli.csproj` to `src/Slrr.Engine.Ui` and a
`"ui" => CmdUi.Run(...)` line in `Commands.cs` (not edited here).

## Interfaces needed from other areas

`IUiWorld` (default implementation in brackets):

* `IdOf(vm, obj)`: runtime id of a ResourceRef (`ref.id()`). Used to build cursor-event parameter strings. [calls the script's `id()`.]
* `Subscribers(vm, source, event)`: targets + handler names registered by `GameType.addNotification`. Used for icon buttons
  (`Osd.createButton(int rid, ...)` → `addHook`). [unknown → falls back to `Osd.event_handler` when the icon is in `osd.button[]`.]
* `AcceptsEvent(vm, target, event)`: event mask of `setEventMask`. [unknown → `Group.active` decides, which the script
  keeps in step with `setEventMask(EVENT_CURSOR)`/`clearEventMask` in `Group.activate/deactivate`.]

Offered to other areas: `UiEngine.AxisInfo(controller, vaxis)` = what a Controller should answer to
`getInfo(GII_AXIS, vaxis)` (asked by `Input.getInput`); `UiEngine.VirtualAxis`; `UiDriver.BeforeFrame` for areas that
run `animate`/`control` callbacks (`enableAnimateHook` is a GameType native, not implemented here, so `Osd.animate`
(tooltips, slider drag, StringInput typing) does not run yet).

## Event protocol used (evidence)

* **Frame wake (measured, exe 0x428db9):** every frame the engine reads the static `java.render.Frontend.render` and wakes
  all threads waiting on it. `UiDriver.Frame(dt)`: global hotkeys, `notifyAll(Frontend.render)`, then all script threads
  run until they block or the frame's virtual time (default 1/30 s) is spent; the clock always advances by `dt`.
* **Hotkeys (scripts H, dispatch M):** `Input.createHotkey(key, flags, hotkey, handler, osd, filter)` registers;
  `Hotkey.activate` passes the Osd as handler. The shipped `HotkeyWatcher` thread calls `Input.checkHotkeys(controller,
  Frontend.inputQueue.lastElement())` every 50 ms. `checkHotkeys` consumes pending input edges until one matches an
  active hotkey of that Osd (VIRTUAL flag 1 = `Input.AXIS_*`, KEY flag 2 = DIK code; filter `F_KEY_PRESS` 1 /
  `F_KEY_RELEASE` 2), sets `hotkey.state` (1 press, 0 release) and starts `handler.handleEvent(Hotkey)` **on a new script
  thread** (handlers open dialogs that `wait()`), at most one event per call so `state` is not overwritten before it is
  read. Global hotkeys (osd null, `Input.createGlobalHotkey`) are delivered by the frame pump. Queueing/threading of the
  original are guesses (open question 18).
* **Mouse (scripts H, token layout M):** a gadget is a physics box whose parent is a `Group` with mask `EVENT_CURSOR`; the
  engine sends cursor events to that Group: `handleEvent(GameRef, EVENT_CURSOR, "EC params")`. `UiDriver.Click` sends
  `EC_LDOWN`, `EC_LUP`, `EC_LCLICK` in that order. Token positions follow the reader (`Group.handleEvent`): LDOWN/LUP
  token 3 = physics id, LCLICK token 2 = physics id, HOVER token 3 = id + token 4 = 1; the other tokens are unknown and
  sent as 0. A click hits only gadgets of an active Group in an Osd whose viewport is active. Sliders: the hit box is a
  `Button` whose `parent` is the Slider; `Slider.click` reads `MouseCursor.getPos()` (set `Input.CursorX/Y`).
* **Input edges:** `KeyDown(dik, ch)` sets physical (0, dik) = 1, queues `(ch<<16)|dik` for `Input.lastKey` (Osd.animate
  uses `key >> 16` as the character: M), a raw-key edge, and an edge for every virtual axis whose mapped value crosses
  0.5. `SetVirtualAxis` / `TapAxis` inject virtual axes directly (no control file needed).

## Natives: measured vs guessed

| Native(s) | Behaviour | Status |
|---|---|---|
| Viewport.create/resize/get* | store/return (layer, a, b, w, h); `getLeft` = a, `getTop` = b | storage H; a=left/b=top **guessed** (Osd.rebuild passes `(vpTop, vpLeft)`, the 7-arg ctor `(x+.., y+..)`) |
| Viewport.getAspect | `w·video_x / (h·video_y)` | **M** (matches Osd.screenAspect formula and the letterbox ctor) |
| Viewport.activate/deactivate/destroy | flags, active, destroyed | H (names/flags from scripts) |
| Viewport.unproject | returns a copy of the input | **guessed** (no 3D) |
| Camera.create/destroy/setFog | store parameters | H storage, semantics of lod/culling unknown (open q. 13) |
| Text.create/update/changeColor/changeAlign/setPos/getPos | store; `update` copies the script's `text` field | H (Text.changeText = `text = s; update()`) |
| Text.getWidthPixels | `len × 0.5 × Text.getFontSize(font)` | **guessed** metric (fonts not read) |
| Text.get/setDefaultColor | stored per instance, default -1 | **guessed** (registered, never called) |
| GfxEngine.changeVideoMode/currDisplayMode/numDisplayModes/displayModeName | mode state from Config.video_*; fixed mode list | state H, mode list **guessed** |
| GfxEngine.openVideo | returns **0 = success** | **M** (GfxEngine.playVideo loops only when `== 0`; MainMenuDialog/TrackSelect build a 3D background when non-zero) |
| GfxEngine.isPlayingVideo/closeVideo | plays `VideoLength` s of virtual time (default 0) | guessed |
| GfxEngine.flush/forceRendering/printScreen/setGlobalEnvmap | counters / recorded | H names, no rendering |
| Sound.* | three volumes (initial 1.0 guessed), music set + track, mode/hardware flags; `enableDebugDump` = 0 | H API, values guessed |
| Input.getAxis(d, a) | physical value; `a < 0` sets mouse sensitivity `(-1-a)/100` | value H; negative-axis setter **M** (one site: OptionsDialog before `cursor.config()`) |
| Input.activeAxis(d) | first axis with \|v\| > 0.5, else -1 | -1 = none **H** (OptionsDialog tests `>= 0`), threshold guessed |
| Input.axisName/getDeviceName | DIK key-name table; devices "Keyboard", "SysMouse" | names guessed; "SysMouse" H (Input.initControllers) |
| Input.lastKey/flushHotkeys/createHotkey/deleteHotkey/checkHotkeys | see protocol above | M/guessed |
| Input.mapAxis/setAxisSmooth | recorded | guessed (no script site evaluated) |
| Controller.user_Add/Del/Reset/GetAxisVal/SetAxis* | virtual-axis table; linear map from [from_min, from_max] to [to_min, to_max] clamped | table H (Controller.add/setcontrol), mapping shape guessed |
| MouseCursor.getPos/getPickedPos | host-set OSD coordinates (-1..1) / 3D point | M (Osd.animate places tooltips at getPos in OSD coords) |

## Demonstrations (tests/Slrr.Engine.Ui.Tests/HeadlessDialogTests.cs)

Front end booted like `Init.<init>` (Sound.init, Frontend.init with its LoadingScreen and HotkeyWatcher threads,
Input.initControllers, enablePointingDevice), a `Player` with `controllers[0]`:

* **OptionsDialog**: `display()` blocks on the dialog; Osd on top of `Frontend.inputQueue`, viewport layer 20 active;
  `osdCommand(CMD_OPTIONS)` (what MainMenuDialog does) shows the options page; click "sound options"; click the
  effects slider at 25 % → `Sound.setVolume(0, 0.25)`; click "mixing" → `Config.Sound_Mix_HW = 0`, label "SOFTWARE (SLOW!)";
  ESC (AXIS_CANCEL hotkey) → options page; DOWN + SELECT keys → sound page; "back", "game options", "metric system" →
  `Config.metricSystem` toggled.
* **Catalog**: `enter()` builds 12 pages, front page visible with "$50000"; ENGINE → V8 ENGINE → ESC → ESC → DECALS →
  manufacturers (page numbers "D-I/1", "D-I/2"); `exit()` deactivates the viewport and hides the pointer.
* **MenuDialog** (Menu-based): DOWN + SELECT returns item 2 from `display()`; **YesNoDialog**: click "NO" returns 1.

Stand-ins used only by the tests (`TestWorld.cs`, not shipped): unique ResourceRef ids, a GameRef parent/child tree,
stored PhysicsRef/GameRef positions, every other foreign native a no-op (`NativeRegistry.DefaultStub`), and
`Thread.sleep` in milliseconds (see below).

## Survey (measured, `ui --survey . --dynamic`)

* Static (tier-B closure of all methods, `native-usage-classes.tsv`): 546/1,685 classes runnable with the java.lang
  natives alone, **548** with the UI natives (+2: `java.io.Hotkey`, `java.render.FlashText`). 318 classes have a UI native
  in their closure; nearly all also need ResourceRef/GameRef/GameType natives, so they unlock when those areas land.
* VM survey without stubs (static init + every no-argument constructor/static method, fresh VM each): static init
  1,618 → 1,618; no-arg methods completed 27/105 → **39/105**; classes with all of them completed 1,557 → **1,559**. The
  remaining blocker is `ResourceRef.newNative` (124 runs).
* With the test stand-ins for foreign natives, OptionsDialog, Catalog, MenuDialog and YesNoDialog run interactively (above).

## Findings for other areas

* **Thread.sleep is in milliseconds** (H): `SoftTimer` sleeps `waitTime * 1000`, `HotkeyWatcher` `sleep(50)` "every 50 ms",
  `FlashText` 600, `LoadingScreen` 300, `playVideo` 100. `LangNatives` sleeps that many *seconds*, so the HotkeyWatcher
  polls every 50 s. The tests override it.
* **`!` on a float** (`OptionsDialog.osdCommand(CMD_REFRESH_TEXT)`: `if (!aspectX)` with `static float aspectX`) raises
  "non-integral operand" in `Slrr.Vm`; the video-options page therefore stops with a script error when opened. The game
  runs this path, so the engine evidently accepts it (`!f` = `f == 0`).
* `ResourceRef.getChildNodes()` (script) returns null for a node without children; scripts such as
  `MouseCursor.cleanup` rely on the engine always giving the cursor node children.
