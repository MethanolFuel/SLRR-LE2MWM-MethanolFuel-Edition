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

{{CLASS java.io.File}}

{{CLASS java.io.FindFile}}

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

{{CLASS java.io.Input}}

{{CLASS java.io.Controller}}

{{CLASS java.io.MouseCursor}}

{{CLASS java.io.ControlSet}}

## Remaining script classes

{{CLASSLIST java.io.}}
