# Script VM: native entry points and natively implemented `java.*` classes

Static analysis of `StreetLegal_Redline.exe` and the patched `Slrr_GI.exe`. Nothing was executed.

## Registration mechanism (measured)

Natives are registered at start-up by straight-line code, one call per method (__thiscall on the VM object
`[0x63641c]`):

```
mov  ecx, [0x63641c]
push <C function>        ; e.g. 0x43c7f0
push "(FF)F"             ; descriptor, dotted form
push "getTorque"
push "java.game.parts.bodypart.Chassis"
call 0x416b00            ; JVM::registerNative(class, name, desc, fn): 0x18-byte record, hashed by name
```

* `StreetLegal_Redline.exe`: **360** registrations, **37 classes**. Full list (call site, class, method,
  descriptor, C function VA): [`natives-redline.tsv`](natives-redline.tsv), produced by
  `scripts/native_table.py`. All 360 call sites of `0x416b00` are matched.
* A method is native when its MTHD flags include `0x40` (e.g. `SfxTable.getItems` flags `0x41`, slots 0). At
  call time the VM looks it up by `class.method` and reports
  `Thread::callmethod: native implementation missing for "%s.%s"` when nothing is registered.
  `CallInfo.parseParameters: not enough parameters passed to native function` is the arity check.
* Native-backed object lifetime: classes extending `java.lang.Native` (fields: only `ptr I`, the C++ object
  pointer) get `newNative()V` / `deleteNative()V` (`ResourceRef`, `DynoData`).
  `GameType.createNativeInstance(GameRef,I,String,String)V` and `GameRef.create_native(...)` instantiate engine
  objects for script types. Strings `Natives.cpp` (`D:\Invictus\StreetLegalRedline\!Engine\Game\Natives.cpp`)
  and `createNativeInstance` place these in the same module.

## Classes with native methods (Redline exe)

| Class | # | Methods |
|---|---|---|
| java.lang.Object | 7 | wait, notify, notifyAll, toString, disableGC, enableGC, hashCode |
| java.lang.String | 6 | finalize, token, intValue, floatValue, append, length |
| java.lang.System | 22 | exit ×2, log, gc, runFinalization, analyze ×3, arraycopy, currentTime, simTime, timeWarp, openLib, compileAll, info, buildNumber, setLdPriority, syncGameTime, setMeasure, getConfigOptions, isLoadingReset, isLoading |
| java.lang.Thread | 10 | init, start, stop, isAlive, getPriority, setPriority, sleep, suspend, resume, setDaemon |
| java.lang.Math | 4 | random, sqrt, randomize, setrandseed |
| java.lang.Integer / Float | 2 / 1 | toString, toHexString / toString |
| java.lang.Vector3 / Ypr | 6 / 2 | `<init>` ×n, rotate, length, distance, normalize / `<init>`, mul |
| java.lang.GameType | 11 | add/remNotification, addTimer, removeAllTimers, set/clearEventMask, (un)registerCallback(s), createNativeInstance |
| java.io.File / FindFile | 14 / 3 | open, close, delete, copy, move, exists, write ×n, readInt, readFloat, readResID, readString / first, next, close |
| java.io.Input / Controller / MouseCursor | 11 / 7 / 2 | axes, hotkeys / user_* force-feedback axes / getPickedPos, getPos |
| java.render.GfxEngine / Viewport / Text / Camera | 11 / 11 / 9 / 3 | video modes, flush, video playback / create, activate, unproject… / text OSD / create, destroy, setFog |
| java.sound.Sound | 10 | music set and track control, volume, hardware queries |
| java.util.resource.ResourceRef | 20 | newNative, deleteNative, set, load, unload, destroy, type, id, parent/child walk, makeTexture, makeSound, cache, precache, duplicate, scaleMesh |
| java.util.resource.GameRef | 20 | create, create_native, flags, pos/ori/vel, getInfo, queueEvent, setParent/Pos/Matrix/State, isScripted, getScriptInstance… |
| java.util.resource.RenderRef / PhysicsRef / GroundRef | 14 / 6 / 25 | render instances / physics bodies / water, fog, traffic, routes, pedestrians |
| java.util.resource.Animation / ParticleSystem / SfxRef | 9 / 9 / 2 | playback / emitters / nplay, stop |
| java.game.parts.Part / WheelRef / bodypart.Chassis | 29 / 35 / 23 | slots, wear, meshes / suspension and tyre parameters (incl. setPacejka) / torque, mass, CM, wheels, SFX |
| java.game.parts.SfxTable / DynoData | 3 / 5 | getItems, clear, addItem / newNative, deleteNative, calcDyno, getTorque, getHP |
| java.game.Vehicle / Painter / Navigator / GameLogic | 3 / 3 / 1 / 1 | getHorn, hasCrime, getSpeedSquare / paintPart, doPaint, xPaint / updateNavigator / kismajomCheck |

Everything else in `java.*` is script (`.class`) code that runs on the VM. The engine also calls back into
scripts by name: `handleEvent`, `handleEventAsync`, `getInfo`, `drive`, `animate`, `render`, `control`,
`update`, `reCreate`, `init`, `GameInit` (on `java.render.Frontend`), `updateDamage`, `finalize`
(`THRD-FIN`). Engine-known class names: `java.lang.String`, `java.lang.Object`, `java.lang.Vector3`,
`java.lang.Ypr`, `java.util.resource.GameRef`, `java.util.resource.ResourceRef`, `java.game.parts.WheelRef`,
`java.game.parts.SfxTable`, `java.game.parts.bodypart.Chassis`, `java.game.Vehicle`, `java.game.parts.DynoData`.
The engine also reads and writes script fields by name through `vm_get/set_int|float|instance_field`.

## Patched executable `Slrr_GI.exe` (measured + hypothesis)

Same build (timestamp `0x420765d4`, identical sections) plus a 64 KB `.code` section at `0x7c0000`. Its 370
registrations include **10 not in the Redline exe** ([`natives-slrr_gi-extra.tsv`](natives-slrr_gi-extra.tsv)):
`SfxTable.AddItemPtr` (and a re-registered `addItem`), `Chassis.getIndexedData(I)F`, `File.TimeLow/TimeHigh`,
and `java.lang.RawEdit.getI/getF/setI/setF/CentCur`.

The mod's scripts use `RawEdit` (`system/Scripts/lang/src/RawEdit.java`) to **peek and poke absolute addresses
of this exe** (config globals such as `0x618D5C` instance memory, damage and wear multipliers, AI tuning).
Hypothesis: the C# engine must replace `RawEdit` with a typed settings API, or emulate the documented addresses
as named variables. A raw memory model cannot be supported.

## Confidence

* The class/method/descriptor/function list is **measured** (exact pattern match, 360/360 call sites).
* The method groupings in the table are summaries. The TSV is authoritative.
* Callback names come from strings. Exactly when each callback is invoked was not traced (**hypothesis**).
