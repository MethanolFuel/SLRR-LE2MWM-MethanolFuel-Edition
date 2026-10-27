# java.io

Script roots: `system\Scripts\io\*.class` (10 classes). Natives: File 14, FindFile 3, Input 11, Controller 7,
MouseCursor 2 (+2 Slrr_GI extras on File).

## Files and the save-game idiom

All persistent state (career, cars, skins, controls, options) goes through `java.io.File`'s typed read/write calls
(see the class table). There is no seek, no length, no text mode: a file is a magic int, a version int and a flat
sequence of ints, floats, strings and resource references, read back in the same order. Observed formats and magics:

| File | Magic / version | Written by |
|---|---|---|
| `save/carrer/...` main save | `0x87654321`, version 21 (loader accepts >= 20) | `GameLogic.save/load` |
| vehicle file `save/cars/...` | `-2006563806` (0x88664422), version 5 | `Vehicle.save/load`, nested `Part.save` recursion |
| skin file `save/skins/...` | `-2108385177` (0x82549867), version 0 | Vehicle skin save |
| `save/game/options` | `-19088744` (0xFEDCBA98), version 12 | `Config.saveConfig` |
| `save/controls/active_control_set` | `0x4C525443` ('CTRL'), version 16 | `ControlSet.load/save` (the only sample in the repo) |

(The magics are the signed ints found in the bytecode; the hex forms are the same 32 bits.)
Directories used: `save/carrer/`, `save/temp/`, `save/cars/`, `save/skins/`, `save/controls/`, `screenshots\`.

**On-disk layout (measured on `save/controls/active_control_set`, 3,079 bytes, decoded by
`tools/analysis/sdat_probe.py`).** 8-byte file header: ASCII `SDAT` + bytes `00 01 02 00` (written by `open(MODE_WRITE)`,
checked by `open(MODE_READ)`; not visible to scripts), then the values exactly as written, with no type tags:
`write(int)` = 4 bytes little endian, `write(float)` = 4 bytes IEEE-754 LE, `write(String)` = u32 length INCLUDING the
terminating NUL, the characters, a NUL. The control file decodes completely under `ControlSet.load`: magic `CTRL`, version 16,
5 device names ("SysKeyboard", "SysMouse", "Wireless Controller", ...), 78 binding records of 9 x 4 bytes (group, virtual
axis, device index, device axis/key, from_min, from_max, to_min, to_max, dead_zone; e.g. `1, 28 (AXIS_THROTTLE), 0, 200 (UP)`),
then 4 smoothing records. The encoding of `write(ResourceRef)` / `readResID()` is NOT observable from this sample (open
question 12); whether original saves must load decides if the layout has to be reproduced byte for byte.

### java.io.File

`extends java.lang.Native`. Compiled in `system/Scripts/io/File.class`.

(H) Binary file handle (Native). Idiom everywhere: `File f = new File(path); if (f.open(File.MODE_WRITE)) { f.write(...); f.close(); }`, paths are relative to the game root with forward slashes (`save/temp/`, `save/cars/`, `save/controls/`). Binary format with an 8-byte `SDAT` header, raw little-endian ints/floats and u32-length-prefixed NUL-terminated strings (measured, see java.io.md). Save-game format = sequence of write(int/float/String/ResourceRef) calls, read back with readInt/readFloat/readString/readResID in the same order; every file starts with a magic int and a version int (GameLogic SAVEFILEID_MAIN 0x87654321, version 21 (loads >= 20)). finalize() closes the file.

Static fields: `MODE_READ = 0`, `MODE_WRITE = 1`

Fields: `String name`

Engine-read field candidates (the exact field name is pushed as a string constant by exe code; first code xref in `StreetLegal_Redline.exe` after the `@`; short names can be coincidental): `name@0x485465`

| Method | Decl / Reg | Typed sites (callers) | Name-only sites | Arguments seen | Semantics and evidence |
|---|---|---|---|---|---|
| `static int delete(String)` | native/R | 10 (6) | 6 (4) | 0: "save/temp/CopyCar" x3, "save/temp/CopyCar.*" x3 | (H) Static, deletes one path. `delete(dir, pattern)` (script overload) iterates FindFile, so wildcard patterns like "save/temp/CopyCar.*" work through FindFile. e.g. `Physic_Lab_Track`: `File.delete("save/temp/CopyCar")` |
| `static int copy(String, String)` | native/R | 0 | 2 (2) |  | (H) Static `copy(src,dst)`; the 3-arg script overload copies a directory's files via FindFile(FILES_ONLY). |
| `static int move(String, String)` | native/R | 0 | 1 (1) |  | (H) Static `move(src,dst)`. |
| `static int exists(String)` | native/R | 6 (6) | 2 (2) | 0: "save/temp/CopyCar" x3, v3 x2 | (H) Static, int 0/1. e.g. `Physic_Lab_Track`: `File.exists("save/temp/CopyCar")` |
| `static int TimeLow(String)` | native/G | 0 | 0 |  | (L) Slrr_GI.exe extra, low dword of the file time (Windows FILETIME) - no site in the shipped scripts. |
| `static int TimeHigh(String)` | native/G | 0 | 0 |  | (L) Slrr_GI.exe extra, high dword of the file time. |
| `int open(int)` | native/R | 0 | 14 (4) |  | (H) `open(File.MODE_READ=0 or MODE_WRITE=1)` returns non-zero on success; used as an `if` condition. |
| `void close()` | native/R | 15 (5) | 0 | (no args) | (H) Closes; safe to call twice (finalize also closes). e.g. `GameLogic`: `v1.close()` |
| `int write(int)` | native/R | 108 (52) | 40 (8) | 0: v2 x45, v4 x7 | (H) Overloads int, float, String, ResourceRef (returns int status, never checked). write(ResourceRef) stores a resource reference (Part.save writes `new GameRef(getInfo(GII_TYPE))`); read back with readResID(). e.g. `Disc`: `p0.write(v2)` |
| `int write(float)` | native/R | 250 (50) | 40 (8) | 0: posX x21, posY x21 | (H) Overloads int, float, String, ResourceRef (returns int status, never checked). write(ResourceRef) stores a resource reference (Part.save writes `new GameRef(getInfo(GII_TYPE))`); read back with readResID(). e.g. `Disc`: `p0.write(c_angle)` |
| `int write(ResourceRef)` | native/R | 4 (3) | 40 (8) | 0: v5 x3, v3 | (H) Overloads int, float, String, ResourceRef (returns int status, never checked). write(ResourceRef) stores a resource reference (Part.save writes `new GameRef(getInfo(GII_TYPE))`); read back with readResID(). e.g. `Part`: `p0.write(v3)` |
| `int write(String)` | native/R | 4 (3) | 40 (8) | 0: v12, name | (H) Overloads int, float, String, ResourceRef (returns int status, never checked). write(ResourceRef) stores a resource reference (Part.save writes `new GameRef(getInfo(GII_TYPE))`); read back with readResID(). e.g. `Part`: `p0.write(v12)` |
| `int readInt()` | native/R | 132 (52) | 0 | (no args) | (H) Reads one 32-bit int. e.g. `Disc`: `p0.readInt()` |
| `float readFloat()` | native/R | 270 (53) | 0 | (no args) | (H) Reads one 32-bit float. e.g. `Disc`: `p0.readFloat()` |
| `int readResID()` | native/R | 8 (4) | 0 | (no args) | (H) Reads a resource reference written by write(ResourceRef) and returns the resolved resource id (int); Part.createFromFile treats 0 as 'no part'. e.g. `Part`: `p0.readResID()` |
| `String readString()` | native/R | 7 (6) | 0 | (no args) | (H) Reads a string written by write(String). Behaviour at end of file is not observed in the scripts. e.g. `Part`: `p0.readString()` |

Decl/Reg: `native` = flagged 0x40 in the shipped .class, `script` = non-native in the .class but registered natively in an exe, `undeclared` = registered in an exe but missing from the shipped .class; `R` = registered in `StreetLegal_Redline.exe`, `G` = registered in `Slrr_GI.exe` (patched build), `-` = no registration found. Typed sites = call sites that name this method with its descriptor (distinct caller classes in brackets); name-only = sites that only name the method, counted over all classes (README conventions).

Script members: `delete(String, String)`, `copy(String, String, String)`, `move(String, String, String)`, `<init>()`, `<init>(String)`, `finalize()`


### java.io.FindFile

`extends java.lang.Native`. Compiled in `system/Scripts/io/FindFile.class`.

(H) Directory iterator (Native, fields `handle`, `flags`). Idiom: `FindFile ff = new FindFile(); for (n = ff.first(pattern[,flags]); n; n = ff.next()) {...} ff.close();`

Static fields: `FILES_DIRS = 0`, `FILES_ONLY = 1`, `DIRS_ONLY = 2`

Fields: `int handle`, `int flags`

Engine-read field candidates (the exact field name is pushed as a string constant by exe code; first code xref in `StreetLegal_Redline.exe` after the `@`; short names can be coincidental): `handle@0x487cf2`, `flags@0x487d06`

| Method | Decl / Reg | Typed sites (callers) | Name-only sites | Arguments seen | Semantics and evidence |
|---|---|---|---|---|---|
| `String first(String, int)` | native/R | 1 (1) | 9 (5) | 0: p0; 1: FILES_DIRS | (H) `first(String pattern, int flags)`: pattern with wildcards, e.g. "humans/drivers/Default_Driver/*.*", "parts\\engines\\*.rpk"; flags FILES_DIRS=0 (default), FILES_ONLY=1, DIRS_ONLY=2. Returns the BARE file name (callers prepend the directory) or null. e.g. `FindFile`: `first(p0, FILES_DIRS)` |
| `String next()` | native/R | 9 (5) | 0 | (no args) | (H) Next match or null. e.g. `Catalog`: `v2.next()` |
| `void close()` | native/R | 10 (6) | 0 | (no args) | (H) Releases the find handle. e.g. `Catalog`: `v2.close()` |


Script members: `<init>()`, `finalize()`, `first(String)`


## Input and controllers

Model (H): `Input.controllers[MAXPLAYERS]` (MAXPLAYERS = 1 in this build) are `Controller` GameTypes created by
`Input.initControllers(parent, "save/controls/active_control_set")`: for every DirectInput device except `"SysMouse"`
one `Controller(parent, "0 0 <idx>")`, then `queueEvent(null, EVENT_COMMAND, "device <i>")`; without devices a single
controller plus command `"setcontrol"`. The control file is parsed by the SCRIPT class `ControlSet`
(groups DEFAULTSET/MENUSET/DRIVERSET...) and translated into native `user_Add` mappings of *virtual axes* (`Input.AXIS_*`).
Game code only ever reads virtual axes through `controller.getInfo(GII_AXIS, axis)` or `Input.getInput(axis[,player])`
(which also timestamps `player_input[p] = System.currentTime()`, used by GameLogic to choose which controller joins).
The pointing device is separate: `Input.enablePointingDevice()` creates `Input.cursor` (MouseCursor, native kind `cursor`
frontend.rpk:0x2A), whose `EVENT_CURSOR` events (EC_* codes in GameType) are routed by `Osd.event_handler`.

### java.io.Input

`extends java.lang.Object`. Compiled in `system/Scripts/io/Input.class`.

(H) Static facade over the input layer. Axis ids AXIS_* (0..75), keyboard scan codes RCDIK_* (DirectInput DIK values: ESCAPE=1, A=30, ENTER=28, UP=200...), MAXPLAYERS = 1 in this build (so controllers[] has one element), VIRTUAL=1/KEY=2 hotkey kinds. Script helpers: initControllers, getInput(axis[,player]), isPlayerActive, enablePointingDevice, createGlobalHotkey.

<details><summary>155 static fields with initial values</summary>

`AXIS_NULL = 0`, `AXIS_TURN_LEFTRIGHT = 1`, `AXIS_TURN_UPDOWN = 2`, `AXIS_TURN_CCWCW = 3`, `AXIS_MOVE_LEFTRIGHT = 4`, `AXIS_MOVE_UPDOWN = 5`, `AXIS_MOVE_FWDBACK = 6`, `AXIS_LOOK_LEFTRIGHT = 7`, `AXIS_LOOK_UPDOWN = 8`, `AXIS_LOOK_CCWCW = 9`, `AXIS_LOOK_FWDBACK = 10`, `AXIS_LOOK_ZOOM = 11`, `AXIS_LOOK_PREVNEXT = 12`, `AXIS_LOOK_REAR = 13`, `AXIS_MENU_LEFTRIGHT = 14`, `AXIS_MENU_UPDOWN = 15`, `AXIS_MENU_PREVNEXT = 16`, `AXIS_FIRE_PRI = 17`, `AXIS_FIRE_SEC = 18`, `AXIS_USE = 19`, `AXIS_PRI_PREVNEXT = 20`, `AXIS_SEC_PREVNEXT = 21`, `AXIS_TARGET_PREVNEXT = 22`, `AXIS_TARGET_LEFTRIGHT = 23`, `AXIS_TARGET_UPDOWN = 24`, `AXIS_CONTROL_PREVNEXT = 25`, `AXIS_CONTROL_LEFTRIGHT = 26`, `AXIS_CONTROL_UPDOWN = 27`, `AXIS_THROTTLE = 28`, `AXIS_BRAKE = 29`, `AXIS_SHIFT_UPDOWN = 30`, `AXIS_NITRO = 31`, `AXIS_START = 32`, `AXIS_PAUSE = 33`, `AXIS_SELECT = 34`, `AXIS_CANCEL = 35`, `AXIS_MENU = 36`, `AXIS_MAP = 37`, `AXIS_HELP = 38`, `AXIS_CHEAT_1 = 39`, `AXIS_CHEAT_2 = 40`, `AXIS_SHIFT_LOOK = 41`, `AXIS_CURSOR_X = 42`, `AXIS_CURSOR_Y = 43`, `AXIS_CURSOR_Z = 44`, `AXIS_CURSOR_BUTTON1 = 45`, `AXIS_CURSOR_BUTTON2 = 46`, `AXIS_CURSOR_BUTTON3 = 47`, `AXIS_HANDBRAKE = 48`, `AXIS_CLUTCH = 49`, `AXIS_GEAR_UPDOWN = 50`, `AXIS_GEAR_SET = 51`, `AXIS_MUSIC_VOLUME = 52`, `AXIS_MUSIC_SELECT = 53`, `AXIS_HORN = 54`, `AXIS_MENU_UP = 55`, `AXIS_MENU_DOWN = 56`, `AXIS_MENU_LEFT = 57`, `AXIS_MENU_RIGHT = 58`, `AXIS_MUSIC_VOLUME_UP = 59`, `AXIS_MUSIC_VOLUME_DOWN = 60`, `AXIS_MUSIC_SELECT_NEXT = 61`, `AXIS_MUSIC_SELECT_PREV = 62`, `AXIS_PRINTSCREEN = 63`, `AXIS_SPECIAL_1 = 68`, `AXIS_SPECIAL_2 = 69`, `AXIS_SPECIAL_3 = 70`, `AXIS_SPECIAL_4 = 71`, `AXIS_SPECIAL_5 = 72`, `AXIS_SPECIAL_6 = 73`, `AXIS_SPECIAL_7 = 74`, `AXIS_SPECIAL_8 = 75`, `RCDIK_ESCAPE = 1`, `RCDIK_1 = 2`, `RCDIK_2 = 3`, `RCDIK_3 = 4`, `RCDIK_4 = 5`, `RCDIK_5 = 6`, `RCDIK_6 = 7`, `RCDIK_7 = 8`, `RCDIK_8 = 9`, `RCDIK_9 = 10`, `RCDIK_0 = 11`, `RCDIK_BACK = 14`, `RCDIK_TAB = 15`, `RCDIK_Q = 16`, `RCDIK_W = 17`, `RCDIK_E = 18`, `RCDIK_R = 19`, `RCDIK_T = 20`, `RCDIK_Y = 21`, `RCDIK_U = 22`, `RCDIK_I = 23`, `RCDIK_O = 24`, `RCDIK_P = 25`, `RCDIK_ENTER = 28`, `RCDIK_A = 30`, `RCDIK_S = 31`, `RCDIK_D = 32`, `RCDIK_F = 33`, `RCDIK_G = 34`, `RCDIK_H = 35`, `RCDIK_J = 36`, `RCDIK_K = 37`, `RCDIK_L = 38`, `RCDIK_Z = 44`, `RCDIK_X = 45`, `RCDIK_C = 46`, `RCDIK_V = 47`, `RCDIK_B = 48`, `RCDIK_N = 49`, `RCDIK_M = 50`, `RCDIK_SPACE = 57`, `RCDIK_F1 = 59`, `RCDIK_F2 = 60`, `RCDIK_F3 = 61`, `RCDIK_F4 = 62`, `RCDIK_F5 = 63`, `RCDIK_F6 = 64`, `RCDIK_F7 = 65`, `RCDIK_F8 = 66`, `RCDIK_F9 = 67`, `RCDIK_F10 = 68`, `RCDIK_F11 = 87`, `RCDIK_F12 = 88`, `RCDIK_UP = 200`, `RCDIK_LEFT = 203`, `RCDIK_RIGHT = 205`, `RCDIK_DOWN = 208`, `RCDIK_COMMA = 51`, `RCDIK_PERIOD = 52`, `RCDIK_NUMLOCK = 69`, `RCDIK_NUMPADSLASH = 181`, `RCDIK_NUMPADSTAR = 55`, `RCDIK_NUMPADMINUS = 74`, `RCDIK_NUMPADPLUS = 78`, `RCDIK_NUMPADENTER = 156`, `RCDIK_NUMPADDECIMAL = 83`, `RCDIK_NUMPAD0 = 82`, `RCDIK_NUMPAD1 = 79`, `RCDIK_NUMPAD2 = 80`, `RCDIK_NUMPAD3 = 81`, `RCDIK_NUMPAD4 = 75`, `RCDIK_NUMPAD5 = 76`, `RCDIK_NUMPAD6 = 77`, `RCDIK_NUMPAD7 = 71`, `RCDIK_NUMPAD8 = 72`, `RCDIK_NUMPAD9 = 73`, `RCDIK_PGUP = 201`, `RCDIK_PGDN = 209`, `MAXPLAYERS = 1`, `controllers = new java.io.Controller[MAXPLAYERS]`, `player_input = new F[MAXPLAYERS]`, `VIRTUAL = 1`, `KEY = 2`

</details>

Fields: `MouseCursor cursor`

Engine-read field candidates (the exact field name is pushed as a string constant by exe code; first code xref in `StreetLegal_Redline.exe` after the `@`; short names can be coincidental): `cursor@0x461d53`

| Method | Decl / Reg | Typed sites (callers) | Name-only sites | Arguments seen | Semantics and evidence |
|---|---|---|---|---|---|
| `static float getAxis(int, int)` | native/R | 1 (1) | 7 (6) | 0: v1; 1: v2 | (H) `getAxis(int axis, int player)` returns float value of a virtual axis (OptionsDialog calibration screen). e.g. `OptionsDialog`: `Input.getAxis(v1, v2)` |
| `static String getDeviceName(int)` | native/R | 4 (2) | 0 | 0: nDevices, v1 | (H) `getDeviceName(i)` returns the i-th DirectInput device name, null after the last. Input.initControllers loops until null and skips the name "SysMouse" (the mouse is handled by the cursor, not as a game controller). e.g. `ControlSet`: `Input.getDeviceName(nDevices)` |
| `static void mapAxis(GameRef, int, int, int, float, float, float, float)` | native/R | 0 | 0 |  | (L) Declared native, no script site (superseded by Controller.user_Add). |
| `static void setAxisSmooth(GameRef, int, float, float, float, float)` | native/R | 0 | 0 |  | (L) Declared native, no script site (superseded by Controller.user_SetAxisSmooth). |
| `static int activeAxis(int)` | native/R | 1 (1) | 0 | 0: v1 | (M) `activeAxis(deviceId)` returns the device axis/key currently active (OptionsDialog 'press a key to bind'). e.g. `OptionsDialog`: `Input.activeAxis(v1)` |
| `static String axisName(int, int)` | native/R | 1 (1) | 1 (1) | 0: v1; 1: v2 | (M) `axisName(device, axis)` human readable axis name for the control screens. e.g. `OptionsDialog`: `Input.axisName(v1, v2)` |
| `static int lastKey()` | native/R | 2 (2) | 0 | (no args) | (M) Last key pressed (RCDIK_*), used by Osd text entry and StringInput. e.g. `Input`: `lastKey()` |
| `static void createHotkey(int, int, Hotkey, GameRef, Osd, int)` | native/R | 1 (1) | 138 (30) | 0: key; 1: flags; 2: this; 3: osd; 4: osd; 5: eventFilter | (H) `createHotkey(int key, int flags, Hotkey hk, GameRef handler, Osd osd, int eventFilter)`; called from Hotkey.activate() (`Input.createHotkey(key, flags, this, osd, osd, eventFilter)`). On a matching key the engine calls `handler.handleEvent(Hotkey hk)` (GameLogic.handleEvent(Hotkey) switches on hk.command). Global hotkeys (no osd) are created by Hotkey's 4-arg ctor via Input.createGlobalHotkey. Per-OSD hotkeys (Hotkey.activate) pass the OSD itself as handler: Osd.handleEvent(Hotkey) then handles menu navigation for commands carrying the PRIVATEEVENT bit and otherwise calls hk.handler.osdCommand(hk.command) (or posts Event(command,state) to its alienMp port); Hotkey.state is non-zero on key down. e.g. `Hotkey`: `Input.createHotkey(key, flags, this, osd, osd, eventFilter)` |
| `static void deleteHotkey(Hotkey)` | native/R | 1 (1) | 0 | 0: this | (H) Removes the registration (Hotkey.inactivate). e.g. `Hotkey`: `Input.deleteHotkey(this)` |
| `static void checkHotkeys(GameRef, Osd)` | native/R | 0 | 1 (1) |  | (H) `checkHotkeys(Controller c, Osd topOsd)`: polled by HotkeyWatcher every 50 ms with the top-most Osd of Frontend.inputQueue; fires matching hotkeys whose osd is on top. |
| `static void flushHotkeys()` | native/R | 1 (1) | 0 | (no args) | (H) Drops queued hotkey presses (Input.flushKeys). e.g. `Input`: `flushHotkeys()` |


Script members: `createGlobalHotkey(int, int, Hotkey, GameRef)`, `flushKeys()`, `initControllers(GameRef, String)`, `addController(Controller)`, `getActivePlayers()`, `isPlayerActive(int)`, `destroyControllers()`, `getInput(int)`, `getInput(int, int)`, `enablePointingDevice()`, `disablePointingDevice()`


### java.io.Controller

`extends java.lang.GameType`. Compiled in `system/Scripts/io/Controller.class`.

(H) One per player. `new Controller(GameRef parent, "0 0 <playerIndex>")` calls createNativeInstance(parent, system.rpk:0x32, params, "controller"); Input.initControllers then sends `queueEvent(null, EVENT_COMMAND, "device <i>")` per DirectInput device, `"setcontrol"` for the fallback. getInfo(GII_AXIS, axis) returns the axis value. Key bindings are plain text files (save/controls/active_control_set) read by ControlSet.load and pushed to the native layer with user_Add.

Static fields: `RESOURCEID = system.rpk:0x32r`

Fields: `String name`, `ControlSetState css`, `String controlFile`

Engine-read field candidates (the exact field name is pushed as a string constant by exe code; first code xref in `StreetLegal_Redline.exe` after the `@`; short names can be coincidental): `name@0x485465`

| Method | Decl / Reg | Typed sites (callers) | Name-only sites | Arguments seen | Semantics and evidence |
|---|---|---|---|---|---|
| `int user_Add(int, int, int, float, float, float, float)` | native/R | 0 | 19 (6) |  | (H) `user_Add(int virtualAxis, int deviceId, int deviceAxisOrKey, float fromMin, float fromMax, float toMin, float toMax)` maps a device axis/key range onto a virtual axis (AXIS_*). Controller.add computes fromMin as `min + (max-min)*deadzone`. Returns int status (never checked). |
| `int user_Del(int, int, int)` | native/R | 0 | 15 (6) |  | (H) `user_Del(virtualAxis, deviceId, deviceAxisOrKey)` removes one mapping (Garage swaps cursor and look axes this way). |
| `int user_SetAxisSmooth(int, float, float, float, float, float)` | native/R | 0 | 1 (1) |  | (H) `(virtualAxis, centerRange, factorCenter, factorOpposite, factorSame, power)` from ControlSet.vasp (VirtualAxisSmoothProperties). |
| `void user_SetAxisSpeed(int, float)` | native/R | 0 | 0 |  | (L) No site. |
| `float user_GetAxisVal(int)` | native/R | 0 | 0 |  | (L) No site. |
| `void user_SetAxisForce(int, float, float)` | native/R | 0 | 0 |  | (L) Force-feedback force for an axis; no site. |
| `void user_Reset()` | native/R | 2 (1) | 0 | (no args) | (H) Clears all mappings (called by setcontrol/reset/activateState before re-adding). e.g. `Controller`: `user_Reset()` |


Script members: `<init>(GameRef, String)`, `add(ControlSet, int)`, `del(ControlSet, int)`, `smooth(VirtualAxisSmoothProperties)`, `setcontrol(ControlSet)`, `setcontrol()`, `reset(ControlSetState)`, `reset()`, `activateState(int, int)`, `activateState(int)`


### java.io.MouseCursor

`extends java.lang.GameType`. Compiled in `system/Scripts/io/MouseCursor.class`.

(H) The on-screen pointer (GameType created from frontend.rpk:0x2A `native cursor`), fields cursor (GameRef), vp, camera, controller. It subscribes to its own EVENT_CURSOR (addNotification) and routes EC_* codes to the Osd under it.

Fields: `GameRef cursor`, `Viewport vp`, `Camera camera`, `String pointer`, `int visible`, `Controller controller`, `GameRef controlledcam`

Engine-read field candidates (the exact field name is pushed as a string constant by exe code; first code xref in `StreetLegal_Redline.exe` after the `@`; short names can be coincidental): `cursor@0x461d53`, `camera@0x42e1ae`

| Method | Decl / Reg | Typed sites (callers) | Name-only sites | Arguments seen | Semantics and evidence |
|---|---|---|---|---|---|
| `Vector3 getPos()` | native/R | 4 (3) | 2 (2) | (no args) | (H) Cursor position as Vector3 (screen space, Osd coordinates). e.g. `RaceSetup`: `Input.cursor.getPos()` |
| `Vector3 getPickedPos()` | native/R | 3 (2) | 0 | (no args) | (H) World position under the cursor (picking ray hit) - used by Painter (spray target) and RaceSetup (place markers). e.g. `Painter`: `Input.cursor.getPickedPos()` |


Script members: `<init>()`, `finalize()`, `enable(int)`, `config()`, `setPointer(ResourceRef, String)`, `setController(Controller)`, `enableCameraControl(GameRef)`, `disableCameraControl()`, `addHandler(GameType)`, `remHandler(GameType)`, `cleanup()`


### java.io.ControlSet

`extends java.lang.Object`. Compiled in `system/Scripts/io/ControlSet.class`.

(H) Script class that parses a control file: groups of bindings (`group[i]`, `deviceID`, `axisID`, `vaxisID`, `from_min/max`, `to_min/max`, `dead_zone`). Groups are DEFAULTSET/MENUSET/DRIVERSET states switched by Controller.activateState.

Static fields: `CTRLFILEID = 1280463939`, `CTRLFILEVERSION = 16`, `NCONTROLS = 78`, `KEYBOARD = 0`, `MOUSE = 1`, `DEFAULTSET = 0`, `DRIVERSET = 1`, `MENUSET = 2`, `CAMTURNSET = 3`, `CAMMOVESET = 4`, `DEBUGCAMSET = 5`

Fields: `int nDevices`, `String[] deviceName`, `int[] group`, `int[] vaxisID`, `int[] deviceID`, `int[] axisID`, `float[] from_min`, `float[] from_max`, `float[] to_min`, `float[] to_max`, `float[] dead_zone`, `Vector vasp`

Script members: `fileCheck(String)`, `<init>()`, `nitems()`, `load(String)`, `save(String)`, `change(int, int, int)`, `define(int, int, int, int, int, float, float, float, float, float)`, `defaults()`, `ps2emu()`


## Remaining script classes

| Class | Extends | Methods | Fields | Natives | Role |
|---|---|---|---|---|---|
| `java.io.ControlSet` | `java.lang.Object` | 9 | 23 | 0 | Script class that parses a control file: groups of bindings (`group[i]`, `deviceID`, `axisID`, `vaxisID`, `from_min/max`, `to_min/max`, `dead_zone`). Groups are DEFAULTSET/MENUSET/DRIVERSET states switched by Controller.activateState. |
| `java.io.ControlSetState` | `java.lang.Object` | 4 | 2 | 0 |  |
| `java.io.Controller` | `java.lang.GameType` | 17 | 4 | 7 | One per player. `new Controller(GameRef parent, "0 0 <playerIndex>")` calls createNativeInstance(parent, system.rpk:0x32, params, "controller"); Input.initControllers then sends `queueEvent(null, EVENT_COMMAND, "device <i>")` per DirectInput device, `"setcontrol"` for the fallback. getInfo(GII_AXIS, axis) returns the axis value. Key bindings are plain text files (save/controls/active_control_set) read by ControlSet.load and pushed to the native layer with user_Add. |
| `java.io.File` | `java.lang.Native` | 22 | 3 | 16 | Binary file handle (Native). Idiom everywhere: `File f = new File(path); if (f.open(File.MODE_WRITE)) { f.write(...); f.close(); }`, paths are relative to the game root with forward slashes (`save/temp/`, `save/cars/`, `save/controls/`). Binary format with an 8-byte `SDAT` header, raw little-endian ints/floats and u32-length-prefixed NUL-terminated strings (measured, see java.io.md). Save-game format = sequence of write(int/float/String/ResourceRef) calls, read back with readInt/readFloat/readString/readResID in the same order; every file starts with a magic int and a version int (GameLogic SAVEFILEID_MAIN 0x87654321, version 21 (loads >= 20)). finalize() closes the file. |
| `java.io.FindFile` | `java.lang.Native` | 6 | 5 | 3 | Directory iterator (Native, fields `handle`, `flags`). Idiom: `FindFile ff = new FindFile(); for (n = ff.first(pattern[,flags]); n; n = ff.next()) {...} ff.close();` |
| `java.io.Hotkey` | `java.lang.Object` | 4 | 7 | 0 |  |
| `java.io.Input` | `java.lang.Object` | 22 | 156 | 11 | Static facade over the input layer. Axis ids AXIS_* (0..75), keyboard scan codes RCDIK_* (DirectInput DIK values: ESCAPE=1, A=30, ENTER=28, UP=200...), MAXPLAYERS = 1 in this build (so controllers[] has one element), VIRTUAL=1/KEY=2 hotkey kinds. Script helpers: initControllers, getInput(axis[,player]), isPlayerActive, enablePointingDevice, createGlobalHotkey. |
| `java.io.MouseCursor` | `java.lang.GameType` | 13 | 7 | 2 | The on-screen pointer (GameType created from frontend.rpk:0x2A `native cursor`), fields cursor (GameRef), vp, camera, controller. It subscribes to its own EVENT_CURSOR (addNotification) and routes EC_* codes to the Osd under it. |
| `java.io.VirtualAxisSmoothProperties` | `java.lang.Object` | 4 | 6 | 0 |  |
| `java.io.controlconfig` | `java.io.Controller` | 0 | 1 | 0 |  |

