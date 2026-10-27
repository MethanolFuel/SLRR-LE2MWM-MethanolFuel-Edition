# java.lang

Script roots: `system\Scripts\lang\*.class` (16 files). All of `java.lang` is script code except the natives below.
The VM itself (opcodes, `Thread::run`) is specified in `docs/research/script-vm-opcodes.md`; this page covers the
classes the VM and the content assume.

## Threading, ordering and cooperation model

Facts (H unless noted):

* A `Thread` is a cooperative VM thread. `Thread::run(float budget)` gives each thread a time slice per engine tick
  and checks the suspend flags after every instruction (docs/research/script-vm-opcodes.md section 1). There is no
  preemption inside an instruction and no exception mechanism: a script error prints `!Script error` /
  `Thread: %s` and stops THAT thread (docs section 5).
* Threads are named. The names the engine uses itself (strings in the exe): `THRD-CLASSVAR-INI`,
  `THRD-INSTVAR-INI` (field initialisers), `THRD-CREATE` (the constructor call made by `GameRef.create`),
  `THRD-RUNVMI` (event/callback invocations), `THRD-FIN` (finalizers). Script `new Thread(target, "name")` adds user threads.
* `Object.wait()` / `notify()` / `notifyAll()` work on any object and are the only synchronisation primitive. Seen
  patterns: (1) worker loop `while(1){ obj.wait(); work; }` woken by `obj.notify()` (GameLogic clock text, GameType async
  message port, Mechanic/CarLot workers); (2) `Signal.set(v)`/`waitFor(v)`; (3) `LoadingScreen` waits on
  `Frontend.render`, which the engine notifies once per frame (H: the per-frame code at 0x428db9 loads class
  `java.render.Frontend`, looks up its field `render` and calls 0x408ce0, which is exactly the routine the native
  `Object.notifyAll` (0x47c970) tail-jumps to; script side: `LoadingScreen.run` is
  `while(1){ Frontend.render.wait(); ... }`). A C# VM therefore has to run `notifyAll(Frontend.render)` every frame.
* Timing: `Thread.sleep(ms)` takes milliseconds (float). `GameType.addTimer(seconds,id)` takes seconds and fires
  `handleEvent(self, EVENT_TIME, id)` once.
* `GameLogic implements Runnable` with a **static** `run()` and hands `this` to `new Thread(this, ...)`: the VM must resolve
  `run` on the target by name including static methods (M).
* Async dispatch: a `GameType` that called `enterAsyncMode()` has `asyncMode=1`. The exe reads that field by name when it
  delivers an event (xref at 0x425f12) and, hypothesis, uses `handleEventAsync(...)` which only does
  `mp.putMessage(...)` into the object's `MessagePort`; the object's own thread (`GameType.run`) drains the port and calls
  `handleMessage(Message)` / `handleMessage(Event)`. 44 part classes define `handleMessage(Event)` (tuning dialogs).
* There is no `synchronized`, `volatile`, `try` or `throw` in any shipped script (the in-engine compiler rejects them).

### java.lang.Object

`extends None`. Compiled in `system/Scripts/lang/Object.class`.

(H) Root of every script class (no superclass). Monitor methods work on ANY object, scripts use them as condition variables (see Thread notes).

| Method | Decl / Reg | Typed sites (callers) | Name-only sites | Arguments seen | Semantics and evidence |
|---|---|---|---|---|---|
| `void wait()` | native/R | 9 (5) | 0 | (no args) | (H) Blocks the calling script thread until another thread calls notify/notifyAll on the SAME object. Pattern: `while(1){ obj.wait(); ... }` (GameLogic.run waits on `timeRefresher`, GameType.run waits on `mp`, LoadingScreen.run waits on `Frontend.render`). No timeout form exists. There is no matching lock/unlock: scripts never use `synchronized` (the in-engine compiler rejects it); mutual exclusion comes from the cooperative scheduler. e.g. `GameLogic`: `timeRefresher.wait()` |
| `void notify()` | native/R | 13 (9) | 0 | (no args) | (H) Wakes one thread waiting on this object. 13 typed sites; used by GameLogic.spendTime/setTimerText (wake the clock-text thread), MessagePort.putMessage (wake the async-event thread), SoftTimer (wake a dialog after a delay). e.g. `RacerTalkDialog`: `notify()` |
| `void notifyAll()` | native/R | 2 (2) | 0 | (no args) | (H) Wakes every waiter (Signal.set, LoadingScreen.hide wakes `termSig`). e.g. `Signal`: `notifyAll()` |
| `String toString()` | native/R | 1 (1) | 119 (23) | (no args) | (L) Native default text. Scripts mostly override it (Vehicle.toString returns chassis.vehicleName, Vector3/Ypr.toString). GameType.enterAsyncMode_Script uses it as the thread name. e.g. `GameType`: `toString()` |
| `int hashCode()` | native/R | 0 | 0 |  | (L) Registered, no site in the shipped scripts. |
| `void enableGC()` | native/R | 0 | 0 |  | (L) Registered, no site in the shipped scripts. Pairs with disableGC; hypothesis: toggles the VM garbage collector around critical sections. |
| `void disableGC()` | native/R | 0 | 0 |  | (L) See enableGC. |

Decl/Reg: `native` = flagged 0x40 in the shipped .class, `script` = non-native in the .class but registered natively in an exe, `undeclared` = registered in an exe but missing from the shipped .class; `R` = registered in `StreetLegal_Redline.exe`, `G` = registered in `Slrr_GI.exe` (patched build), `-` = no registration found. Typed sites = call sites that name this method with its descriptor (distinct caller classes in brackets); name-only = sites that only name the method, counted over all classes (README conventions).

Script members: `<init>()`, `finalize()`, `equals(Object)`


### java.lang.Native

`extends java.lang.Object`. Compiled in `system/Scripts/lang/Native.class`.

(H) Marker base class whose only field is `int ptr`. Engine-backed classes: ResourceRef family, Thread, String, File, FindFile, Animation, DynoData, SfxTable, WheelRef. The engine stores the C++ object pointer in `ptr` (docs/research/script-vm-natives.md).

Fields: `int ptr`

Engine-read field candidates (the exact field name is pushed as a string constant by exe code; first code xref in `StreetLegal_Redline.exe` after the `@`; short names can be coincidental): `ptr@0x408311`


### java.lang.Runnable

`extends None`. Compiled in `system/Scripts/lang/Runnable.class`.

(H) Interface with `run()`. Implementors: Thread, GameType (run() drains its message port), GameLogic, FlashText, SoftTimer, HotkeyWatcher.

Script members: `run()`


### java.lang.Thread

`extends java.lang.Native` implements java.lang.Runnable. Compiled in `system/Scripts/lang/Thread.class`.

(H) Cooperative script thread. The VM runs threads in time slices (Thread::run(float budget), docs/research/script-vm-opcodes.md), there is no preemption inside a slice and no exceptions. A Thread is a java.lang.Native: `ptr` holds the engine thread object.

Static fields: `MAX_PRIORITY = 10`, `MIN_PRIORITY = -10`, `NORM_PRIORITY = 0`

Fields: `Runnable target`

Engine-read field candidates (the exact field name is pushed as a string constant by exe code; first code xref in `StreetLegal_Redline.exe` after the `@`; short names can be coincidental): `target@0x47c5bd`

| Method | Decl / Reg | Typed sites (callers) | Name-only sites | Arguments seen | Semantics and evidence |
|---|---|---|---|---|---|
| `static Thread getCurrentThread()` | native/- | 0 | 0 |  | (L) Declared native in Thread.class, NOT registered in StreetLegal_Redline.exe and not called. |
| `static void sleep(float)` | native/R | 43 (13) | 15 (2) | 0: 1000 x8, 4000 x3 | (H) Static. Argument is MILLISECONDS as float: `Thread.sleep(waitTime * 1000)` (SoftTimer, waitTime in seconds), `sleep(600)` (FlashText blink), `sleep(50)` (HotkeyWatcher poll), `sleep(300)` (LoadingScreen). Values seen: 1000 x8, 4000, 20, 3000, 7000. e.g. `DealerTrack`: `Thread.sleep(1000)` |
| `int getPriority()` | native/R | 0 | 0 |  | (L) Registered, no site in the shipped scripts (setPriority is used once). |
| `void setPriority(int)` | native/R | 0 | 3 (2) |  | (M) Only call: `setPriority(Thread.MAX_PRIORITY)` in CarLot (MIN -10, NORM 0, MAX 10 are script constants). |
| `void setDaemon(int)` | native/R | 0 | 0 |  | (L) Registered, no site in the shipped scripts. |
| `void init(String)` | native/R | 1 (1) | 2 (2) | 0: p1 | (H) protected native called by the constructor `Thread(Runnable target, String name)` with the NAME only; the target is stored in the script field `target`. Order inside the constructor: `super(); init(name); this.target = target`. `Thread()` and `Thread(String)` use `this` as target. e.g. `Thread`: `init(p1)` |
| `void start()` | native/R | 31 (29) | 1 (1) | (no args) | (H) Schedules the thread to run `target.run()`. The target may be a STATIC `run()` method (GameLogic declares `static void run()` and passes `this`), so the engine's lookup of `run` must accept static methods. e.g. `Bumper`: `v1.start()` |
| `void stop()` | native/R | 13 (9) | 31 (8) | (no args) | (H) Kills the thread (no unwinding, no finally). Used for `timeRefresher.stop()` in GameLogic.finalize, GameType.leaveAsyncMode_Script_Real, Mechanic/CarLot worker threads. e.g. `CarLot`: `loadTextThread.stop()` |
| `int isAlive()` | native/R | 0 | 0 |  | (L) Registered, no site in the shipped scripts. |
| `void suspend()` | native/R | 0 | 0 |  | (L) Registered, no site in the shipped scripts. |
| `void resume()` | native/R | 0 | 0 |  | (L) Registered, no site in the shipped scripts. |


Script members: `<init>()`, `<init>(String)`, `<init>(Runnable)`, `<init>(Runnable, String)`, `run()`


### java.lang.Signal

`extends java.lang.Object`. Compiled in `system/Scripts/lang/Signal.class`.

(H) Pure script class (no natives). `set(v)` stores `value` and notifyAll(); `waitFor(v)` loops `while(value != v) wait()`. Both methods have flags 0x81 (public + 0x80; 0x80 is probably 'synchronized/atomic' in TUFA, never used by the compiler for anything else in the corpus).

Fields: `int value`

Script members: `set(int)`, `waitFor(int)`


## Strings and numbers

### java.lang.String

`extends java.lang.Native`. Compiled in `system/Scripts/lang/String.class`.

(H) Strings are VM objects (`java.lang.Native` subclass). `+` concatenates and formats int/float operands (operator 0x10, `unsupported type (+)` otherwise). The float-to-text format of `+` is NOT known (open question 4); `Float.toString(f, fmt)` is the explicit formatter.

Static fields: `TCF_NONE = 0`, `TCF_NOSECONDS = 1`, `TCF_NOHOURS = 2`, `TCF_NOMINUTES = 4`, `TCF_SMART = 8`, `TCF_VERBOSE = 16`, `TCF_US = 32`, `TCF_SIGN = 64`

| Method | Decl / Reg | Typed sites (callers) | Name-only sites | Arguments seen | Semantics and evidence |
|---|---|---|---|---|---|
| `void finalize()` | native/R | 0 | 9 (9) |  | (L) Frees the native string buffer. 9 by-name sites are other classes' finalize. |
| `String token(int, String)` | native/R | 1 (1) | 49 (6) | 0: p0; 1: " \t\n" | (H) `s.token(i)` = i-th token, 0-based, default delimiters " \t\n" (script overload token(I) calls token(i, " \t\n")). Core of every text protocol: command strings, EVENT_CURSOR parameters (`p2.token(0).intValue()`), save-free key/value text. Pattern `s.token(++k)` with k starting at -1 walks the tokens. e.g. `String`: `token(p0, " \t\n")` |
| `int intValue()` | native/R | 0 | 66 (18) |  | (H) Parses decimal int from the string (used on tokens). Behaviour on malformed input unknown. |
| `float floatValue()` | native/R | 0 | 15 (3) |  | (H) Parses float from the string (used on tokens). |
| `String append(int)` | native/R | 1 (1) | 0 | 0: p0 | (M) `inputString = inputString.append(charCode)` in StringInput: appends the character with that code and returns the (new) string. e.g. `StringInput`: `inputString.append(p0)` |
| `int length()` | native/R | 9 (4) | 2 (2) | (no args) | (H) Number of characters. e.g. `Bot`: `name.length()` |


Script members: `timeToString(float)`, `timeToString(float, int)`, `timeToAlphaString(float, int)`, `<init>()`, `token(int)`


### java.lang.Integer

`extends java.lang.Object`. Compiled in `system/Scripts/lang/Integer.class`.

(H) Boxed int with script fields; only the two static helpers are native.

Static fields: `MIN_VALUE = -2147483648`, `MAX_VALUE = 2147483647`

Fields: `int number`

| Method | Decl / Reg | Typed sites (callers) | Name-only sites | Arguments seen | Semantics and evidence |
|---|---|---|---|---|---|
| `static String toString(int)` | native/R | 4 (1) | 119 (23) | 0: p0 x4 | (H) Static. Decimal text of the int. Also reached by-name from the `+` operator machinery in 119 sites (shared name with Float.toString). e.g. `Integer`: `toString(p0)` |
| `static String toHexString(int)` | native/R | 0 | 1 (1) |  | (M) Static. Hex text of the int. One by-name site (City debug id). |


Script members: `toOrdinal(int)`, `<init>(int)`, `setValue(int)`, `intValue()`


### java.lang.Float

`extends java.lang.Object`. Compiled in `system/Scripts/lang/Float.class`.

(H) Boxed float; only toString is native.

Static fields: `MAX_VALUE = 2147483648.0f`, `MIN_VALUE = -2147483648.0f`

Fields: `float number`

| Method | Decl / Reg | Typed sites (callers) | Name-only sites | Arguments seen | Semantics and evidence |
|---|---|---|---|---|---|
| `static String toString(float, String)` | native/R | 453 (32) | 119 (23) | 0: posX x63, posY x63; 1: "   %1.0f degrees" x193, "   %1.3f" x189 | (H) Static `Float.toString(float v, String fmt)`: printf-style format. 453 typed sites, formats seen: "   %1.0f degrees" x193, "   %1.3f" x189, "   %1.5f", "Frequency: %1.1f s", "%.0f". Used for every numeric text box. e.g. `MrSir_Spring`: `Float.toString(force, "%1.0f N/m")` |


Script members: `<init>(float)`, `<init>(String)`


### java.lang.Math

`extends java.lang.Object`. Compiled in `system/Scripts/lang/Math.class`.

(H) Only four natives; there is no sin/cos/abs/min/max natively. Scripts implement trigonometry through their own code or through Vector3/Ypr natives (rotate, length, normalize).

| Method | Decl / Reg | Typed sites (callers) | Name-only sites | Arguments seen | Semantics and evidence |
|---|---|---|---|---|---|
| `static float random()` | native/R | 181 (24) | 0 | (no args) | (H) Static, float in [0,1). 181 typed sites. Seeded by `Math.randomize()` (GameLogic ctor) or `Math.setrandseed(int)` (no site). e.g. `DealerTrack`: `Math.random()` |
| `static float sqrt(float)` | native/R | 28 (17) | 17 (7) | 0: v1 x9, v2 x8 | (H) Static float sqrt. e.g. `DealerTrack`: `Math.sqrt(v2)` |
| `static void randomize()` | native/R | 1 (1) | 22 (22) | (no args) | (H) Static; reseeds from the clock. One typed site (GameLogic ctor), 22 by-name. e.g. `GameLogic`: `Math.randomize()` |
| `static void setrandseed(int)` | native/R | 0 | 0 |  | (L) Registered, no site in the shipped scripts. |



Not available natively: sin, cos, tan, atan2, abs, min, max, floor, pow. The shipped scripts that need trigonometry use
`Vector3.rotate`/`Ypr` or hand-written polynomials; whatever C# implements must not add semantics beyond the four
natives (extra natives would only be dead code).

## System

### java.lang.System

`extends java.lang.Object`. Compiled in `system/Scripts/lang/System.class`.

(H) All members static. See entry-points.md for startup use (System.openLib, rpkScan).

Static fields: `LD_NORM = 0`, `LD_HIGH = 1`

| Method | Decl / Reg | Typed sites (callers) | Name-only sites | Arguments seen | Semantics and evidence |
|---|---|---|---|---|---|
| `static void exit()` | native/R | 1 (1) | 17 (16) | (no args) | (H) `System.exit()` terminates the game (GameLogic.changeActiveSection(null) calls it when the new state is null). `exit(String msg)` is the fatal-error form (Bot.createCar "VehicleDescriptor null"). e.g. `GameLogic`: `System.exit()` |
| `static void exit(String)` | native/R | 1 (1) | 17 (16) | 0: "Bot.createCar(): VehicleDescriptor null" | (H) `System.exit()` terminates the game (GameLogic.changeActiveSection(null) calls it when the new state is null). `exit(String msg)` is the fatal-error form (Bot.createCar "VehicleDescriptor null"). e.g. `Bot`: `System.exit("Bot.createCar(): VehicleDescriptor null")` |
| `static void log(String)` | native/R | 1 (1) | 0 | 0: "private constructor access (Dialog)" | (M) Writes a line to the script log (string 'Script log' in the exe). Used once (Dialog 'private constructor access'). e.g. `Dialog`: `System.log("private constructor access (Dialog)")` |
| `static int buildNumber()` | native/R | 1 (1) | 0 | (no args) | (M) Returns the engine build; MainMenuDialog compares it with Config.acceptBuild (601). The PE identifies as SL2 build 606. e.g. `MainMenuDialog`: `System.buildNumber()` |
| `static String info(int)` | native/R | 0 | 0 |  | (L) Registered/declared, no site. Hypothesis: engine information string by index. |
| `static int gc()` | native/R | 0 | 0 |  | (L) No site in the shipped scripts. |
| `static int runFinalization()` | native/R | 0 | 0 |  | (L) No site in the shipped scripts. |
| `static int compileAll(String)` | native/R | 2 (2) | 0 | 0: "." x2 | (H) `System.compileAll(".")` from MainMenuDialog/OptionsDialog: recompiles every .java under the given root whose .class is out of date (the compiler lives in the exe: JVM::compileSource). e.g. `MainMenuDialog`: `System.compileAll(".")` |
| `static void analyze(Object)` | native/R | 0 | 0 |  | (L) Debug helper (three overloads: Object, float, int); no site. |
| `static void analyze(float)` | native/R | 0 | 0 |  | (L) Debug helper (three overloads: Object, float, int); no site. |
| `static void analyze(int)` | native/R | 0 | 0 |  | (L) Debug helper (three overloads: Object, float, int); no site. |
| `static void arraycopy(Object, int, Object, int, int)` | native/R | 2 (1) | 2 (1) | 0: v2, v3; 1: 0 x2; 2: elementData x2; 3: 0 x2; 4: elementCount x2 | (H) `System.arraycopy(src, srcPos, dst, dstPos, len)`, Vector.ensureCapacity/insert use it with element arrays. e.g. `Vector`: `System.arraycopy(v2, 0, elementData, 0, elementCount)` |
| `static float currentTime()` | native/R | 22 (8) | 0 | (no args) | (H) Real (wall) time in SECONDS as float. `TimeTextWait = currentTime() + 2` = two seconds. 22 sites. e.g. `CarSelect`: `System.currentTime()` |
| `static float simTime()` | native/R | 56 (6) | 0 | (no args) | (H) Simulation clock in SECONDS (race timers: `botTime = simTime() - startTime`). Advances by dt*timeWarp. 56 sites. e.g. `Nevada_Track`: `System.simTime()` |
| `static float timeWarp(float)` | native/R | 29 (11) | 44 (3) | 0: 1.0f x14, 0.0f x7 | (H) Sets the simulation speed factor and RETURNS THE PREVIOUS ONE; `timeWarp(-1)` is the query form (Track: `float v = System.timeWarp(-1.0f)`). 1.0 normal, 0.0 pause, 2x steps for the debug time-acceleration keys. e.g. `Pneumatic_ShockAbsorber`: `System.timeWarp(1.0f)` |
| `static void syncGameTime(float)` | native/R | 2 (1) | 0 | 0: time x2 | (H) `System.syncGameTime(time)`: GameLogic pushes the in-game clock (seconds since midnight, float) to the engine (sky/sun position). Called from setTime and spendTime. e.g. `GameLogic`: `System.syncGameTime(time)` |
| `static void setMeasure(float)` | native/R | 4 (2) | 0 | 0: 1600 x2, 1000 x2 | (M) `setMeasure(1000)` or `1600` depending on Config.metricSystem: distance unit in metres (km or ~mile) used by the native speed/distance displays. e.g. `OptionsDialog`: `System.setMeasure(1600)` |
| `static void getConfigOptions()` | native/R | 5 (1) | 0 | (no args) | (M) Called after OptionsDialog changes video/sound options; hypothesis: refreshes script `Config.*` statics from engine-side state. e.g. `OptionsDialog`: `System.getConfigOptions()` |
| `static int netHost()` | native/- | 0 | 0 |  | (L) Declared native, not registered in the Redline exe, no site (network play removed). |
| `static int netJoin()` | native/- | 0 | 0 |  | (L) Same as netHost. |
| `static int netLeave()` | native/- | 0 | 0 |  | (L) Same as netHost. |
| `static int openLib(String)` | native/R | 2 (2) | 1 (1) | 0: "parts.rpk", "MW_Mod.rpk" | (H) `System.openLib("MW_Mod.rpk")`: mounts an .rpk archive (path relative to the game root, backslash or slash) and makes its resource tree available. `rpkScan(dir)` is a SCRIPT static that calls it for every `dir\*.rpk` found via FindFile and returns the count. e.g. `Catalog`: `System.openLib("parts.rpk")` |
| `static void isLoadingReset()` | native/R | 1 (1) | 0 | (no args) | (H) Used by LoadingScreen.track(): resets the engine's 'is loading' state before showing the loading dialog. e.g. `LoadingScreen`: `System.isLoadingReset()` |
| `static int isLoading()` | native/R | 1 (1) | 0 | (no args) | (H) LoadingScreen.run polls it: while the resource streamer is busy the loading dialog stays; when it returns 0 the dialog is hidden (or waits for the user). e.g. `LoadingScreen`: `System.isLoading()` |
| `static void setLdPriority(int)` | native/R | 0 | 3 (1) |  | (M) `setLdPriority(System.LD_HIGH)` while a loading dialog is shown, `LD_NORM` otherwise: resource loader thread priority. |


Script members: `rpkScan(String)`


## Vectors

### java.lang.Vector3

`extends java.lang.Object`. Compiled in `system/Scripts/lang/Vector3.class`.

(H) Script class with float x,y,z; math helpers (add, sub, dot, mul, diff) are script code. Natives: length, distance, normalize, rotate(Ypr), rotate(Vector3,F) and the ctor from Ypr.

Fields: `float x`, `float y`, `float z`

| Method | Decl / Reg | Typed sites (callers) | Name-only sites | Arguments seen | Semantics and evidence |
|---|---|---|---|---|---|
| `Vector3(Ypr)` | native/R | 0 | 0 |  | (L) `Vector3(Ypr)` native constructor (direction vector from yaw/pitch/roll); no typed site. The other constructors are script code. |
| `float length()` | native/R | 13 (5) | 2 (2) | (no args) | (H) Euclidean length. e.g. `City`: `v3.length()` |
| `float distance(Vector3)` | native/R | 4 (1) | 0 | 0: v2 x2, lastCrossPos | (H) Distance to another vector. e.g. `Track`: `v4.distance(lastCrossPos)` |
| `void normalize()` | native/R | 2 (2) | 4 (1) | (no args) | (H) In place, void. e.g. `City`: `v3.normalize()` |
| `Vector3 rotate(Ypr)` | native/R | 7 (3) | 33 (5) | 0: ElevatorPlatformYpr x4, CamYpr | (H) rotate(Ypr) returns this vector rotated by yaw/pitch/roll; rotate(Vector3 axis,float angle) rotates around an axis (angle in radians, by-name sites pass `new Ypr(a+1.57f,0,0)` forms). e.g. `CarSelect`: `CamVector.rotate(CamYpr)` |
| `Vector3 rotate(Vector3, float)` | native/R | 0 | 33 (5) |  | (H) rotate(Ypr) returns this vector rotated by yaw/pitch/roll; rotate(Vector3 axis,float angle) rotates around an axis (angle in radians, by-name sites pass `new Ypr(a+1.57f,0,0)` forms). |


Script members: `<init>()`, `<init>(float, float, float)`, `<init>(Vector3)`, `add(Vector3)`, `sub(Vector3)`, `diff(Vector3, Vector3)`, `mul(Vector3)`, `mul(float)`, `dot(Vector3)`, `equals(Vector3)`, `toString()`, `save(File)`, `load(File)`


### java.lang.Ypr

`extends java.lang.Object`. Compiled in `system/Scripts/lang/Ypr.class`.

(H) Yaw/pitch/roll in RADIANS (Ypr(0, 1.57f...) forms; Chassis.deg2rad exists for degree inputs). Fields y, p, r.

Fields: `float y`, `float p`, `float r`

| Method | Decl / Reg | Typed sites (callers) | Name-only sites | Arguments seen | Semantics and evidence |
|---|---|---|---|---|---|
| `Ypr(Vector3)` | native/R | 0 | 0 |  | (L) `Ypr(Vector3)` native constructor (yaw/pitch/roll from a direction vector); no typed site. |
| `void mul(Ypr)` | native/R | 0 | 8 (3) |  | (M) In place scalar-or-Ypr multiply; by-name sites multiply by a float. |


Script members: `<init>()`, `<init>(float, float, float)`, `<init>(Ypr)`, `<init>(Vector3, Vector3)`, `add(Ypr)`, `sub(Ypr)`, `diff(Ypr, Ypr)`, `mul(float)`, `equals(Ypr)`, `toString()`


`Pori` (position+orientation pair) is script only (add/diff/mul). `Class` is an empty marker class.

## GameType

### java.lang.GameType

`extends java.util.resource.GameRef` implements java.lang.Runnable. Compiled in `system/Scripts/lang/GameType.class`.

(H) Base of every scripted game object (Part, Vehicle, Osd, GameLogic, Init, Config, Controller...). Extends GameRef, so a GameType IS a handle to its own world-tree node. The engine instantiates subclasses; see entry-points.md for how and for the callbacks the engine invokes (handleEvent, handleEventAsync, control, drive, animate, getInfo, finalize).

<details><summary>52 static fields with initial values</summary>

`GII_NULL = 0`, `GII_BONE = 1`, `GII_POS = 2`, `GII_VEL = 3`, `GII_DIR = 4`, `GII_ID = 5`, `GII_TYPE = 6`, `GII_CATEGORY = 7`, `GII_CONTROL = 8`, `GII_DRIVE = 9`, `GII_CONTROLLABLE = 10`, `GII_SIZE = 11`, `GII_DAMAGE = 15`, `GII_RENDERINSTANCE = 23`, `GII_OWNER = 24`, `GII_AXIS = 25`, `GII_ANIMATE = 28`, `GII_CAMERA = 34`, `GII_INVITEM_CONTENTS = 37`, `GII_INVITEM_HOOK = 38`, `GII_GETOUT_OK = 41`, `GII_REMOVE_OK = 41`, `GII_MODE = 42`, `GII_PART_OWNER = 43`, `GII_RENDER = 48`, `GII_CAR_DRIVETYPE = 52`, `GII_PART_CATEGORY = 55`, `GII_CAR_TRAFFICPTR = 56`, `GII_INSTALL_OK = 71`, `GII_COMPATIBLE = 72`, `GII_MAX = 72`, `GIR_CAT_VEHICLE = 5`, `GIR_CAT_PART = 9`, `GIR_CAT_BUTTON = 10`, `GIR_CAT_INVENTORYITEM = 11`, `GIR_CAT_BOLT = 12`, `EC_LDOWN = 1`, `EC_LUP = 2`, `EC_RDOWN = 3`, `EC_RUP = 4`, `EC_LCLICK = 5`, `EC_RCLICK = 6`, `EC_DBLCLICK = 7`, `EC_DBRCLICK = 8`, `EC_LDRAGBEGIN = 9`, `EC_LDRAGEND = 10`, `EC_LDROP = 11`, `EC_RDRAGBEGIN = 12`, `EC_RDRAGEND = 13`, `EC_RDROP = 14`, `EC_HOVER = 15`, `EC_LEAVE = 16`

</details>

Fields: `int asyncMode`, `Thread t`, `MessagePort mp`

Engine-read field candidates (the exact field name is pushed as a string constant by exe code; first code xref in `StreetLegal_Redline.exe` after the `@`; short names can be coincidental): `asyncMode@0x425f13`

| Method | Decl / Reg | Typed sites (callers) | Name-only sites | Arguments seen | Semantics and evidence |
|---|---|---|---|---|---|
| `void remNotification(GameRef, int)` | native/R | 1 (1) | 3 (3) | 0: car; 1: GameRef.EVENT_COMMAND | (H) Undoes addNotification(source, mask). e.g. `Bot`: `remNotification(car, GameRef.EVENT_COMMAND)` |
| `void addNotification(GameRef, int, int, String)` | native/R | 1 (1) | 21 (9) | 0: map; 1: GameRef.EVENT_COMMAND; 2: GameRef.EVENT_SAME; 3: null | (H) Subscriber API: `subscriber.addNotification(GameRef source, int eventMask, int deliverAs, String filter [, String handlerName])`. When `source` raises an event in eventMask, the engine calls `handlerName(GameRef source, int event, String params)` on the subscriber (default name `handleEvent`). deliverAs is always GameRef.EVENT_SAME (0), filter is always null in the corpus. Handler names seen: "event_handlerRocFence", "event_handlerRaceFinish", "event_handlerClick", "event_handler"+suffix (Osd.addHook, used for EVENT_CURSOR). e.g. `Valocity`: `addNotification(map, GameRef.EVENT_COMMAND, GameRef.EVENT_SAME, null)` |
| `void addNotification(GameRef, int, int, String, String)` | native/R | 0 | 21 (9) |  | (H) Subscriber API: `subscriber.addNotification(GameRef source, int eventMask, int deliverAs, String filter [, String handlerName])`. When `source` raises an event in eventMask, the engine calls `handlerName(GameRef source, int event, String params)` on the subscriber (default name `handleEvent`). deliverAs is always GameRef.EVENT_SAME (0), filter is always null in the corpus. Handler names seen: "event_handlerRocFence", "event_handlerRaceFinish", "event_handlerClick", "event_handler"+suffix (Osd.addHook, used for EVENT_CURSOR). |
| `void addTimer(float, int)` | native/R | 23 (7) | 2 (2) | 0: 1 x8, 5 x4; 1: 2 x4, 0 x3 | (H) `addTimer(float seconds, int id)`: ONE-SHOT timer. After `seconds` the engine calls `handleEvent(GameRef self, EVENT_TIME, id)` (int overload). GameLogic re-arms itself every time: `addTimer(timeRefreshRate, 0)` inside the handler. Values seen: seconds 1,5,3,10,2; ids 2,4,0,7,3. e.g. `Bot`: `addTimer(3.0f, 0)` |
| `void removeAllTimers()` | native/R | 12 (12) | 0 | (no args) | (H) Cancels pending timers of this object (called in finalize of Chassis and tracks). e.g. `Nevada_Track`: `removeAllTimers()` |
| `void setEventMask(int)` | native/R | 24 (17) | 6 (6) | 0: GameRef.EVENT_CURSOR x13, GameRef.EVENT_TIME x7 | (H) Sets (replaces) the events THIS object accepts: EVENT_CURSOR x13, EVENT_TIME x7, EVENT_COLLISION, EVENT_COMMAND. Without EVENT_TIME in the mask addTimer events are not delivered (GameLogic calls setEventMask(EVENT_HOTKEY\|EVENT_TIME)). e.g. `TopGearTrack`: `setEventMask(GameRef.EVENT_COLLISION)` |
| `void clearEventMask(int)` | native/R | 22 (22) | 0 | 0: GameRef.EVENT_ANY x19, GameRef.EVENT_TIME | (H) `clearEventMask(EVENT_ANY)` in finalize/destructors (19 of 22 sites). e.g. `Chassis`: `clearEventMask(GameRef.EVENT_ANY)` |
| `void registerCallback(int)` | native/R | 2 (1) | 0 | 0: GII_CONTROL, GII_ANIMATE | (H) `registerCallback(GII_CONTROL)` / `registerCallback(GII_ANIMATE)`: asks the engine to call the script's `control(float dt)` / `animate()` every tick. enableControlHook/enableAnimateHook wrap it. Other GII_* ids (GII_DRIVE=9) probably select `drive()`. e.g. `GameType`: `registerCallback(GII_CONTROL)` |
| `void unregisterCallback(int)` | native/R | 2 (1) | 0 | 0: GII_CONTROL, GII_ANIMATE | (H) Counterpart of registerCallback. e.g. `GameType`: `unregisterCallback(GII_CONTROL)` |
| `void unregisterCallbacks()` | native/R | 1 (1) | 0 | (no args) | (H) Drops all registered per-tick callbacks (Chassis.finalize). e.g. `Chassis`: `unregisterCallbacks()` |
| `void createNativeInstance(GameRef, int, String, String)` | native/R | 4 (3) | 0 | 0: p0 x3, null; 1: 0 x2, RESOURCEID; 2: null x3, p1; 3: null x2, "controller" | (H) Creates the engine-side (C++) twin of a script object that was made by `new` (not by GameRef.create). Args: (GameRef parent, int resourceId, String params, String name). `Dummy(parent)` passes RID_DUMMY and "dummy"; GameLogic() passes (null, 0, null, null) and so gets no world-tree node data. Controller passes "controller". e.g. `Controller`: `createNativeInstance(p0, RESOURCEID, p1, "controller")` |


<details><summary>24 script-implemented members</summary>

`<init>()`, `<init>(int)`, `finalize()`, `enterAsyncMode_Script()`, `leaveAsyncMode_Script()`, `leaveAsyncMode_Script_Real()`, `enterAsyncMode()`, `leaveAsyncMode()`, `handleEventAsync(GameRef, int, int)`, `handleEventAsync(GameRef, int, String)`, `run()`, `handleMessage(Message)`, `enableControlHook()`, `disableControlHook()`, `enableAnimateHook()`, `disableAnimateHook()`, `getInfo(int)`, `createNativeInstance()`, `createNativeInstance(GameRef)`, `handleEvent(GameRef, int, int)`, `handleEvent(GameRef, int, String)`, `control(float)`, `drive()`, `animate()`

</details>


The lifecycle of a GameType, the callbacks the engine invokes on it and the notification/timer protocol are in
[entry-points.md](entry-points.md).

## RawEdit (Slrr_GI.exe only)

### java.lang.RawEdit

`extends java.lang.Object`. Compiled in `system/Scripts/lang/RawEdit.class`.

(H) Slrr_GI.exe only (the Redline exe registers none of its natives). Static class; the script wrappers GetIntFromRawAddress/GetFloatFromRawAddress/SetIntOnRawAddress/SetFloatOnRawAddress take address holder objects (IntValueAddress/FloatValueAddress).

<details><summary>69 static fields with initial values</summary>

`Address_I_SoftwareFrustrumCullReturnOverwrite = new IntValueAddress(6391228)`, `Address_I_DisableFlop = new IntValueAddress(6391248)`, `Address_I_cfg_mem_instance_current = new IntValueAddress(7757664)`, `Address_I_cfg_mem_instance_max = new IntValueAddress(6393180)`, `Address_I_cfg_mem_instance_min = new IntValueAddress(6393184)`, `Address_I_cfg_mem_sound_max = new IntValueAddress(6393188)`, `Address_I_cfg_mem_sound_min = new IntValueAddress(6393192)`, `Address_I_cfg_mem_texture_current = new IntValueAddress(6668852)`, `Address_I_cfg_mem_texture_max = new IntValueAddress(6393172)`, `Address_I_cfg_mem_texture_min = new IntValueAddress(6393176)`, `Address_I_cfg_mem_vertex_current = new IntValueAddress(6667588)`, `Address_I_cfg_mem_vertex_max = new IntValueAddress(6393164)`, `Address_I_cfg_mem_vertex_min = new IntValueAddress(6393168)`, `Address_I_cfg_skidmarks_max = new IntValueAddress(6392820)`, `Address_I_resource_loadrate = new IntValueAddress(6393196)`, `Address_I_AISlideIgnoreMod = new IntValueAddress(6391272)`, `Address_I_DisableCruiseSteerOverWrite = new IntValueAddress(6389816)`, `Address_F_ClutchOnInAutoShiftDeadTime = new FloatValueAddress(6391312)`, `Address_F_ThrottleOnInAutoShiftDeadTime = new FloatValueAddress(6391316)`, `Address_F_CarDeformationOnCollisionMult = new FloatValueAddress(6391236)`, `Address_F_CarDeformationOnCollisionMultMinus = new FloatValueAddress(6391232)`, `Address_F_CarPartDetachmentMult = new FloatValueAddress(6391240)`, `Address_F_CarChassisDeformationOnCollisionMult = new FloatValueAddress(6391244)`, `Address_F_WearMultEngine = new FloatValueAddress(6391252)`, `Address_F_WearMultTyre = new FloatValueAddress(6391264)`, `Address_F_TrafficFlatBonus = new FloatValueAddress(6391260)`, `Address_F_AIOverSteering = new FloatValueAddress(6391276)`, `Address_F_AIOverBreaking = new FloatValueAddress(6391280)`, `Address_F_AIOverThrotlling = new FloatValueAddress(6391284)`, `Address_F_cfg_deformation = new FloatValueAddress(6361952)`, `Address_F_cfg_engine_inertia_factor = new FloatValueAddress(6348840)`, `Address_F_cfg_external_damage = new FloatValueAddress(6361944)`, `Address_F_cfg_internal_damage = new FloatValueAddress(6361948)`, `Address_F_cfg_particle_density = new FloatValueAddress(6392136)`, `Address_I_bDrawHeadlightRays = new IntValueAddress(6391740)`, `Address_I_bDrawHeadlightFlares = new IntValueAddress(6391744)`, `Address_I_ShadowSize = new IntValueAddress(6391736)`, `Address_I_Shadows = new IntValueAddress(6391732)`, `Address_I_TextureSize = new IntValueAddress(6391988)`, `Address_F_cfg_ShadowDetail = new FloatValueAddress(6663888)`, `Address_F_cfg_ObjectDetail = new FloatValueAddress(6669348)`, `Address_F_cfg_VideoGamma = new FloatValueAddress(6387496)`, `Address_F_WheelGroundFeedbackFactor = new FloatValueAddress(6348832)`, `Address_F_WheelBreakeFactor = new FloatValueAddress(6348836)`, `Address_F_LastFrameDrawTime = new FloatValueAddress(6538536)`, `Address_F_GameTimeSeconds = new FloatValueAddress(6538544)`, `Address_F_Gravity = new FloatValueAddress(6391524)`, `Address_F_CollisionForceMultMapObjects_1 = new FloatValueAddress(6391532)`, `Address_F_CollisionForceMultMapObjects_2 = new FloatValueAddress(6391536)`, `Address_F_CollisionForceMultVehicle_1 = new FloatValueAddress(6391540)`, `Address_F_CollisionForceMultVehicle_2 = new FloatValueAddress(6391544)`, `Address_F_FrictionOnCollision_1 = new FloatValueAddress(6391548)`, `Address_F_FrictionOnCollision_2 = new FloatValueAddress(6391552)`, `Address_F_WheelFrictionMult = new FloatValueAddress(6391568)`, `Address_F_WheelFriction_XMult = new FloatValueAddress(6389700)`, `Address_F_WheelSilictionMult = new FloatValueAddress(6389704)`, `Address_F_WheelStiffnessMult = new FloatValueAddress(6389760)`, `Address_F_WheelRollResMult = new FloatValueAddress(6389764)`, `Address_F_WheelBearingMult = new FloatValueAddress(6389768)`, `Address_F_WheelMaxLoadMult = new FloatValueAddress(6389772)`, `Address_F_WheelLoadSmoothMult = new FloatValueAddress(6389776)`, `Address_F_NativeSpring_Var1 = new FloatValueAddress(6389780)`, `Address_F_NativeSpring_Var2 = new FloatValueAddress(6389784)`, `Address_F_NativeSpring_Var3 = new FloatValueAddress(6389788)`, `Address_F_NativeSpring_Var4 = new FloatValueAddress(6389792)`, `Address_F_NativeSpring_Var5 = new FloatValueAddress(6389796)`, `Address_F_NativeSpring_Var6 = new FloatValueAddress(6389800)`, `Address_F_NativeSpring_Var8 = new FloatValueAddress(6389804)`, `Address_F_CruiseBreakWhenOverLimit = new FloatValueAddress(6389812)`

</details>

| Method | Decl / Reg | Typed sites (callers) | Name-only sites | Arguments seen | Semantics and evidence |
|---|---|---|---|---|---|
| `static int getI(int)` | native/G | 0 | 1 (1) |  | (H) `getI(int address)` reads the 32-bit int at an ABSOLUTE address of the running Slrr_GI.exe process (source comment). |
| `static float getF(int)` | native/G | 0 | 1 (1) |  | (H) `getF(int address)` reads a 32-bit float at an absolute address. |
| `static int setF(int, float)` | native/G | 0 | 9 (2) |  | (H) `setF(int address, float value)` writes; returns the address overwritten. |
| `static int setI(int, int)` | native/G | 0 | 1 (1) |  | (H) `setI(int address, int value)` writes; returns the address overwritten (source comment). |
| `static int CentCur(int)` | native/G | 0 | 0 |  | (H) Source comment: centres the cursor on the client window; the parameter is unused, the return value is the new x position. |


Script members: `GetIntFromRawAddress(IntValueAddress)`, `GetFloatFromRawAddress(FloatValueAddress)`, `SetFloatOnRawAddress(FloatValueAddress, float)`, `SetIntOnRawAddress(IntValueAddress, int)`


`RawEdit` is the mod's backdoor into the **patched** executable `Slrr_GI.exe`: five extra natives peek/poke absolute
process addresses of that exe. A raw memory model cannot be supported, but the address list below is a precise
catalogue of engine-internal tuning variables (physics multipliers, AI aggressiveness, memory budgets, video toggles)
that the C# engine has to expose as typed settings if the mod's scripts are to keep working. The table is parsed from
`system/Scripts/lang/src/RawEdit.java`:

69 named engine variables.

| Address | Type | Name (RawEdit.java) | Author comment |
|---|---|---|---|
| `0x60E020` | float | `WheelGroundFeedbackFactor` |  |
| `0x60E024` | float | `WheelBreakeFactor` |  |
| `0x60E028` | float | `cfg_engine_inertia_factor` |  |
| `0x611358` | float | `cfg_external_damage` |  |
| `0x61135C` | float | `cfg_internal_damage` |  |
| `0x611360` | float | `cfg_deformation` |  |
| `0x617728` | float | `cfg_VideoGamma` |  |
| `0x617FC4` | float | `WheelFriction_XMult` |  |
| `0x617FC8` | float | `WheelSilictionMult` |  |
| `0x618000` | float | `WheelStiffnessMult` |  |
| `0x618004` | float | `WheelRollResMult` |  |
| `0x618008` | float | `WheelBearingMult` |  |
| `0x61800C` | float | `WheelMaxLoadMult` |  |
| `0x618010` | float | `WheelLoadSmoothMult` |  |
| `0x618014` | float | `NativeSpring_Var1` | (hardness) |
| `0x618018` | float | `NativeSpring_Var2` | (inverse-rebound) |
| `0x61801C` | float | `NativeSpring_Var3` | (inbound inverse-softness) |
| `0x618020` | float | `NativeSpring_Var4` | (length and bumpiness) |
| `0x618024` | float | `NativeSpring_Var5` | (max inverse-contraction) |
| `0x618028` | float | `NativeSpring_Var6` | (collision realted) |
| `0x61802C` | float | `NativeSpring_Var8` | (spring length) |
| `0x618034` | float | `CruiseBreakWhenOverLimit` |  |
| `0x618038` | int | `DisableCruiseSteerOverWrite` |  |
| `0x6185BC` | int | `SoftwareFrustrumCullReturnOverwrite` |  |
| `0x6185C0` | float | `CarDeformationOnCollisionMultMinus` | this should be set at the same time as the above one to CarDeformationOnCollisionMult*-1 |
| `0x6185C4` | float | `CarDeformationOnCollisionMult` |  |
| `0x6185C8` | float | `CarPartDetachmentMult` |  |
| `0x6185CC` | float | `CarChassisDeformationOnCollisionMult` |  |
| `0x6185D0` | int | `DisableFlop` |  |
| `0x6185D4` | float | `WearMultEngine` |  |
| `0x6185DC` | float | `TrafficFlatBonus` |  |
| `0x6185E0` | float | `WearMultTyre` |  |
| `0x6185E8` | int | `AISlideIgnoreMod` |  |
| `0x6185EC` | float | `AIOverSteering` |  |
| `0x6185F0` | float | `AIOverBreaking` |  |
| `0x6185F4` | float | `AIOverThrotlling` |  |
| `0x618610` | float | `ClutchOnInAutoShiftDeadTime` |  |
| `0x618614` | float | `ThrottleOnInAutoShiftDeadTime` |  |
| `0x6186E4` | float | `Gravity` |  |
| `0x6186EC` | float | `CollisionForceMultMapObjects_1` |  |
| `0x6186F0` | float | `CollisionForceMultMapObjects_2` |  |
| `0x6186F4` | float | `CollisionForceMultVehicle_1` |  |
| `0x6186F8` | float | `CollisionForceMultVehicle_2` |  |
| `0x6186FC` | float | `FrictionOnCollision_1` |  |
| `0x618700` | float | `FrictionOnCollision_2` |  |
| `0x618710` | float | `WheelFrictionMult` |  |
| `0x6187B4` | int | `Shadows` |  |
| `0x6187B8` | int | `ShadowSize` |  |
| `0x6187BC` | int | `bDrawHeadlightRays` |  |
| `0x6187C0` | int | `bDrawHeadlightFlares` |  |
| `0x6188B4` | int | `TextureSize` |  |
| `0x618948` | float | `cfg_particle_density` |  |
| `0x618BF4` | int | `cfg_skidmarks_max` |  |
| `0x618D4C` | int | `cfg_mem_vertex_max` |  |
| `0x618D50` | int | `cfg_mem_vertex_min` |  |
| `0x618D54` | int | `cfg_mem_texture_max` |  |
| `0x618D58` | int | `cfg_mem_texture_min` |  |
| `0x618D5C` | int | `cfg_mem_instance_max` |  |
| `0x618D60` | int | `cfg_mem_instance_min` |  |
| `0x618D64` | int | `cfg_mem_sound_max` |  |
| `0x618D68` | int | `cfg_mem_sound_min` |  |
| `0x618D6C` | int | `resource_loadrate` |  |
| `0x63C528` | float | `LastFrameDrawTime` |  |
| `0x63C530` | float | `GameTimeSeconds` |  |
| `0x65AED0` | float | `cfg_ShadowDetail` |  |
| `0x65BD44` | int | `cfg_mem_vertex_current` |  |
| `0x65C234` | int | `cfg_mem_texture_current` |  |
| `0x65C424` | float | `cfg_ObjectDetail` |  |
| `0x765F60` | int | `cfg_mem_instance_current` |  |

