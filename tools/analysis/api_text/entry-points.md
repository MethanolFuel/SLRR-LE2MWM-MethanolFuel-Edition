# Scripting entry points

How the engine reaches scripts and how scripts reach engine objects. Every row says where the fact comes from:
`exe 0xADDR` = disassembly/strings of `StreetLegal_Redline.exe` (the patched `Slrr_GI.exe` has the same layout, docs/research),
`class` = decompiled shipped script, `rpk` = text entry inside an `.rpk` (read with `tools/analysis/rpk_script_bindings.py`).

## 1. Boot sequence

| Step | What happens | Evidence |
|---|---|---|
| 1 | Start-up function prints `SL2 build %d (%s %s), distr. %s` (606, "Feb  7 2005", "v2.2.1"), checks the disc, mounts `system.rpk` (`Data is missing. (distr. %s is needed)` if absent) | exe 0x58c700..0x58c76f |
| 2 | VM bootstrap (0x427980, M): builds `ResourceRef`s for seven fixed `system.rpk` entries (strings at 0x60caec..0x60cb34), calls the three native-registration thunks (0x487f20, 0x46b530, 0x4427e0 contain the 360 `registerNative` sites of docs/research/script-vm-natives.md) and looks up the script class named **`Config`** (string 0x60cb40 at 0x427b08). The classes the engine itself touches by name are `java.lang.String/Object/Vector3/Ypr`, `java.util.resource.GameRef/ResourceRef`, `java.game.parts.WheelRef/SfxTable/DynoData`, `...bodypart.Chassis`, `java.game.Vehicle` (strings 0x60e4c0..0x60f424) | exe 0x427980..0x427b0d |
| 3 | The exe reads the STATIC fields of `java.util.Config` by name: `acceptBuild` (> 606 -> `Incorrect executable build number`), `version` (must equal `"v2.2.1"`), `FILD`, `video_x/y/depth/windowed`, `shadows`, `resource_refreshrate`, `Sound_3D_HW`, `Sound_Mix_HW`, then initialises video (0x4fc1b0) and sound with them (M) | exe 0x58c7ae..0x58c92c; Config class |
| 4 | Intro movies `Data\FMV\Activision.avi`, `Invictus.avi`, `StreetLegal.avi` | exe 0x58c934..0x58c944 |
| 5 | Game init (0x428960): reads the static int `Config.init` (= `sl.rpk:0x40`, the gametype entry `Init`) with `GetConfigData`, wraps it in a `ResourceRef` of type 8 and calls the engine's create routine **0x53a5e0 with (root 0x636460, type, params buffer 0x63c560, name `"GameInit"`)** | exe 0x4289a0..0x4289e5; Config.init initialiser `sl.rpk:0x40r` |
| 6 | The create routine checks that the node is a gametype (`create: Wrong GameType! %s:0x%08X`), builds the instance (`_gameinst`), and calls the script constructor in a VM thread named `THRD-CREATE` (`create: Undefined GameType Constructor! %08X (%s)` otherwise). The same routine is what the native `GameRef.create` (0x47d7b0) calls | exe 0x53a5e0..0x53a9e0 |
| 7 | `sl.rpk:0x40` carries the text `script java.game.Init`, so `java.game.Init.<init>(int)` runs: `Sound.init(); Frontend.init(); Frontend.loadingScreen.show(); Input.initControllers(this, "save/controls/active_control_set"); Input.enablePointingDevice(); gameLogic = new GameLogic();` | rpk sl.rpk; class java.game.Init |
| 8 | `new GameLogic()`: `createNativeInstance()`, mounts content (`System.rpkScan`, `openLib`), scans vehicle types/chassis/maps, creates `Garage`, hotkeys and the clock thread, hides the loading screen and calls `changeActiveSection(new SplashScreen(frontend.rpk:0x5))`. From here on the game is script driven; the exe only runs the frame loop, physics, rendering and event delivery | class java.game.GameLogic |
| frame | Per frame the exe loads class `java.render.Frontend`, reads its static field `render` and runs the same wake routine as `Object.notifyAll` on it (see java.lang.md); delivers events/timers/callbacks to scripts; steps physics; renders | exe 0x428db9..0x428dda |

Not yet known: how `Config` is instantiated and when `save/game/options` is applied relative to step 3 (open question 2),
what the 'params' buffer at 0x63c560 holds for the root (bss, filled at run time), and the content of `Config.esp_*`
fields the exe expects (strings `esp_steer_scale`, `esp_angvel_scale`, `esp_yaw_allowed`, `esp_brake_level`,
`esp_power_level` exist in the exe but not in the shipped `Config.class`; `vm_get_float_field: unknown field` is the
engine's reaction, whether fatal is unknown).

## 2. Gametype entries: how a cfg/rpk id becomes a script object

A **gametype** is an `.rpk` entry with `TypeOfEntry = 8` (`RESTYPE_GAME`, `typ_game_object` in `system.rpk`). Its data is CRLF text
(docs/formats/rpak.md). Lines relevant here (measured over all 87 archives with `tools/analysis/rpk_script_bindings.py`:
1,616 text entries carry `script` and/or `native`):

```
script  java.game.parts.Part                      ; dotted class name ...
script  cars\racers\Baiern_data\scripts\X.class   ; ... or a path to the .class file (the class name INSIDE the file decides, e.g. GE_Hood.class declares java.game.GE_hood)
native  part  parts\scripts\RGearPart.cfg         ; native object kind + its cfg file
native  car   cars\racers\Baiern_data\scripts\_main_CoupeSport.cfg
native  sun   0x0000019F                          ; some kinds take an id instead of a cfg
```

* `native` kinds seen (1,574 entries): `part` 1,502, `car` 45, `ped` 6, `road` 6, `button` 3, `sun` 3, `walk` 2, and one each of
  `bot`, `camera`, `player`, `ground`, `trigger`, `cursor`, `invitem`. The exe's kind vocabulary additionally contains `object`, `trlight`,
  `building` (strings at 0x60c8e8..0x60c94c).
* The script class is found by walking the entry's `superId` chain (high 16 bits = slot in the archive's reference table, 0 = same
  archive) until an entry has a `script` line: of the 1,574 `native` entries 1,504 carry their own `script`, 13 inherit
  `java.game.parts.Part` from the `part` root node of `parts.rpk` (id 0x3), and 57 have none (pure native objects such as `camera`, `map`, `trigger`,
  `ped`, `cursor`, `botbrain`; hypothesis: an engine default script such as `java.lang.GameType` applies, unconfirmed).
  `tools/analysis/rpk_gametype_resolve.py` reproduces these counts.
* Gametype roots used by scripts (all measured): `sl.rpk:0x40` Init (parent `system.rpk:0x1` = `typ_game_object`); `system.rpk` children of 0x1:
  `Config` 0x47 (`java.util.Config`), `Dummy` 0x1B (`java.util.resource.Dummy`), `game` 0x4C, `native` 0x15 with children `camera` 0x33, `Controller` 0x32
  (`system\Scripts\io\Controller.class` + `native player`), `map` 0x35 (`native ground`), `trigger` 0x34; `cars.rpk:0x1000` = `VEHICLETYPE_ROOT`
  (children = the per-make `*_VT` script gametypes); `cars.rpk:0x0101` = `CHASSIS_ROOT` (the chassis gametypes, `GameRef.isScripted()` selects those with scripts);
  `MW_Mod.rpk:0x0ABC` = `MAPS_ROOT` (children `Script` 0xDC1 of each map archive, text `script ...Track.class`); `parts.rpk:0x3` = root of all parts
  (`script java.game.parts.Part`).
* Placed objects in map archives are instance-tree records (`RSD\0` text `gametype 0xSSSSIIII` + `params x,y,z,a,b,c`, docs/formats/instances.md,
  29,026 objects): the high 16 bits of the id select a slot of the archive's reference table, the low 16 bits the entry. The exe parses
  `gametype %d` and `params %2048s` (0x619b40/0x619b50) and the script-visible `create(parent, type, params, name)` takes the **same
  params text** (`"0,0,0,0,0,0"`, `"100,0,0,0,0,0"`, `"0,-10000,0,0,0,0"`): hypothesis (M) that both go through the same parser; the six numbers are x, y, z,
  yaw, pitch, roll.

### GameRef.create

```
GameType  parent.create(GameRef parent, GameRef type, String params, String name)    // native, 0x47d7b0 -> 0x53a5e0
```

* `this` is rebound to the new node, the return value is the script instance (can be null on failure).
* The script constructor is `<init>(int id)`: 1,569 of the shipped classes declare it and chain `super(id)` up to `GameRef(I)` -> `ResourceRef(I)` -> `set(id)`,
  so `id` is the new instance's resource id (M: inferred from the constructor chain, the exe call into the constructor not read).
* Constructors run on a VM thread (`THRD-CREATE`), i.e. they may block (`wait`, `sleep`), and other threads run meanwhile.
* Destruction: `ref.destroy()` (144 typed sites) or parent destruction; the script's `finalize()` is called by the VM (`THRD-FIN`);
  the exe warns `Gametype destructor (ID %08X): still has %d instances` when types outlive their instances.

### createNativeInstance (script-first creation)

`new GameLogic()`, `new Osd(...)`, `new Controller(...)`, `new MouseCursor()`, `new Dummy(...)` create the script object FIRST (plain `new`) and
then call `createNativeInstance(GameRef parent, int typeResourceId, String params, String name)` (native 0x481a70) to create the engine twin.
`GameType.createNativeInstance()` = `(null, 0, null, null)`; `Dummy` passes `GameRef.RID_DUMMY` and `"dummy"`; `Controller` passes
`Controller.RESOURCEID` (system.rpk:0x32), its param string and `"controller"`. Scripts that do not call it (e.g. `Init`) are plain script objects whose
`GameRef` part is bound by the `create` path.

### Handles

`ResourceRef`, `GameRef`, `RenderRef`, ... subclasses of `java.lang.Native` are cheap handles: constructor -> `newNative()`, `finalize()` ->
`deleteNative()`; `DynoData` has its own `newNative`/`deleteNative`. `Thread`, `String`, `File`, `FindFile`, `Animation`, `WheelRef`, `SfxTable` are `Native`
as well (`ptr` field). A `WheelRef`/`SfxTable` is never `new`ed by scripts: the engine hands out instances (`Chassis.getWheel(i)`, `getSfxTable(i)`).

## 3. Callbacks: what the engine calls on scripts

Method names found as strings in the exe or defined-but-never-called in the scripts. "Defs" = number of shipped classes that define it.

| Callback | Signature | Armed by | Evidence | Defs |
|---|---|---|---|---|
| constructor | `<init>(int id)` | `GameRef.create` | exe 0x53a5e0 messages; 1,569 classes | 1,569 |
| `handleEvent` | `(GameRef obj, int event, int param)` and `(GameRef obj, int event, String params)` | `setEventMask`, `addNotification`, `addTimer` (EVENT_TIME, `param` = timer id) | exe strings `handleEvent` x3 (0x60c9d0, 0x60ca08, 0x60ca14); `asyncMode` read at 0x425f12; GameLogic, Garage, Track | 10 + 10 |
| `handleEvent` | `(Hotkey hk)` | `Input.createHotkey` / global hotkey | GameLogic, Osd | 2 |
| `handleEventAsync` | same two overloads | object has `asyncMode == 1` | exe strings x2; implemented only in GameType (`mp.putMessage(...)`) | 2 |
| `event_handler<Name>` | `(GameRef obj, int event, String params)` | `addNotification(src, mask, EVENT_SAME, null, "event_handler<Name>")` | Osd.addHook, Nevada/ROC/City tracks (`event_handlerRocFence`, `event_handlerRaceFinish`, `event_handlerClick`) | 21 |
| `control` | `(float dt)` | `registerCallback(GII_CONTROL)` / `enableControlHook()` | exe strings `control` x3; Painter overrides | 2 |
| `animate` | `()` | `registerCallback(GII_ANIMATE)` / `enableAnimateHook()` (Osd.enableControl) | exe string; 8 map/menu classes | 9 |
| `drive` | `()` | probably `registerCallback(GII_DRIVE)` | exe string only; GameType has an empty body | 1 |
| `getInfo` | `(int key[, int arg])` | engine queries `GII_*` keys on the script | exe string `getInfo` x2; default `GameType.getInfo(I)I` returns 0 | 4 |
| `run` | `()` | `Thread.start()` | exe 0x612d18; target may be static | 33 |
| `finalize` | `()` | GC / destroy | exe `THRD-FIN` (docs/research) | 26 |
| `install` / `remove` / `falloff` | `()` | engine part-link commands (section 5) | exe strings 0x6124ac / 0x6124d0 / 0x612464; never called by scripts, all 22 `install` overrides call `super.install()` | 22 / 2 / 2 |
| `addpart` / `rempart` | `(GameRef child)` | a child part got linked / unlinked | exe strings 0x612328 / 0x612350 (each preceded by the `GameRef` + `ptr` argument construction); Part keeps `attachedParts` | 1 / 1 |
| `updatevariables` | `()` | engine after `add_*/rem_*` structure changes; scripts call it recursively | exe strings x4 (0x6100b8, 0x61078c, 0x6123cc, 0x61244c); 259 definitions | 259 |
| `updateDamage` | `()` | damage events of a part | exe string 0x60e4b0; Part (empty) | 1 |
| `reCreate` | `()` | after mesh/texture/skin rebuild | exe string 0x60cfb4; Part (empty) | 1 |
| `update` | `()` | exe string 0x60cfac | 4 inventory classes define it (exe use unconfirmed) | 4 |

Script-to-script conventions that are NOT engine callbacks (listed because they are 70% of the method names): `GameState.enter(GameState prev)` / `exit(GameState next)`
(GameLogic.changeActiveSection), `osdCommand(int)` (Osd buttons, 35 definitions), `handleMessage(Message|Event)` (async port, 4 + 44), `show()/hide()`, `refresh*`,
`save(File)/load(File)`, `isDriveable()/isDynoable()/isTuneable()`, `buildTuningMenu(Menu)`, `endTuningSession(int)`, `calcPoliceFine`, `check4warnings`.

**Event delivery contract (from the scripts).** `handleEvent(GameRef source, int event, String params)`; `params` is a space separated token list parsed with
`String.token(i)`. Known event kinds and parameter layouts as consumed by Garage/CarMarket/Track:

| Event | params tokens |
|---|---|
| `EVENT_COLLISION` | token0 = id of the colliding object (Garage compares with the jack vehicle id) |
| `EVENT_CURSOR` | token0 = `EC_*` code (1..16), then per code: `EC_LDROP`: dragged object id, target id, 3 floats drop position, button physics id; `EC_RCLICK`: object id, 3 floats hit position; click/hover: object id |
| `EVENT_TRIGGER_ON/OFF` | (handler `event_handlerRocFence` etc.) trigger node as `source` |
| `EVENT_COMMAND` | the command string itself (also used scriptside by Valocity on `map`) |
| `EVENT_TIME` | int overload: `param` = id passed to `addTimer` |

## 4. Event masks and timers

* `setEventMask(mask)` replaces the object's accepted events, `clearEventMask(EVENT_ANY)` is the standard teardown. Without `EVENT_TIME` in the mask timers are silent.
* `addTimer(seconds, id)` is one-shot; periodic behaviour re-arms in the handler (GameLogic every 10 s with id 0, Garage every 1 s with id 2).
* `addNotification(source, mask, EVENT_SAME, null[, "handlerName"])` subscribes THIS object to `source`'s events (the handler defaults to `handleEvent`).
* `queueEvent(source, code, params)` posts; `command(s)` = `queueEvent(null, EVENT_COMMAND, s)`.

## 5. Command strings (the `GameRef.command` / `EVENT_COMMAND` vocabulary)

Native objects are driven through text commands; this is the second large interface after the natives. The exe parses them with `sscanf` formats
(`%*s` skips the verb). The table merges (a) every verb the shipped scripts send (call-site count and receivers) with (b) the formats found
in the exe next to the verb word. Groups by exe address: Controller 0x60d848..0x60d90c (copy at 0x612b24), bot brain 0x60d930..0x60da38, camera 0x60dcbc..0x60e008,
car 0x61047c..0x6108fc, cursor/ground 0x610ce0..0x610fd8, part 0x6124b4..0x6125c4 (group membership by address adjacency is M).

{{COMMANDS}}
Verbs the scripts send without an exe format are (M) plain words matched by string compare: `idle`, `start`, `stop`, `reset`, `brake`, `suspend`,
`reload`, `wakeup`, `lock`, `unlock`, `flap_toggle`, `AI_stop`, `AI_suspend`, `AI_GoToTraffic`, `enable`, `disable`, `sens`, `setcontrol`.
Note the exe's own typo in the `fog` formats (`%*s %d &f &f`, 0x60de64 and 0x610fb0): the original parser would read a literal `&f` token, so
`fog` is probably unusable and no script sends it. Several formats are listed twice (Controller copy at 0x612b24, Part copy 0x6124b4..): the same verbs
are accepted by more than one native kind.

### Part commands in detail

`install` (exe 0x475f6d..): `sscanf(s, "%*s %d %d %d %d %d %f %f %f")` -> ints i1..i5 and floats x,y,z; with fewer than 8 fields the position is ignored.
Measured use of the ints: i2 -> reference to the car/target instance (stored at part+0x60), i4 -> reference to the target PART, i5 -> slot id
on that part (passed by pointer to the install check 0x473b10, 0 = choose), i1 and i3 are parsed but not used by this handler (scripts pass 0 for i1;
`Part.load` passes the child's own slot id in i3). If the check fails the command returns 0, otherwise the attach routine 0x474600 runs and the engine then
calls the script callbacks of section 3. `remove` (0x476091..): `"%*s %d %d"`, the second int is the instance the part is moved to (the map).

## 6. The cfg side: parts, slots and `attach`

See java.game.parts.md ("The part / slot / attach protocol"): the cfg declares geometry and ids (`slot`, `attach`, `compatible`, `slottype`, `slotdmgmode`,
`stockpart`), the script classes hard-code the same ids and compute the physics numbers, the engine matches both through `install` and reads the Chassis
fields back by name.
