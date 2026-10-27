# Engine system layer (`src/Slrr.Engine.System`)

Phase 3 wave 2. Implements, on top of `Slrr.Vm`'s `INativeModule`, the natives of `java.lang.System` that the VM does
not have (14 of 25), `java.io.File` / `FindFile` (sandboxed, SDAT format), the timer natives of `GameType`, and a
corrected `Thread.sleep`. Local solution: `Slrr.Engine.System.sln` (`dotnet test Slrr.Engine.System.sln -warnaserror`).

```csharp
var sandbox = new GameSandbox(gameRoot, saveRoot);          // saveRoot must be writable, gameRoot is never written
var system  = new EngineSystem(sandbox);                    // INativeModule
var natives = NativeRegistry.CreateDefault().Add(system);   // after the java.lang module (sleep is replaced)
var vm      = new Vm(classPath, natives);
// per frame:  system.Frame(vm, dt);   (RunFrame + time warp + timers)
```

Legend: **M** measured (on shipped data or bytecode), **H** hypothesis / guess.

## Natives

| Class | Natives | Notes |
|---|---|---|
| `java.lang.System` | `buildNumber` 606 (M: PE is build 606; the script wants >= `Config.acceptBuild` 601), `info` (H), `compileAll` (returns 0: no in-engine compiler), `timeWarp` (M: sets, returns previous, negative = query), `syncGameTime`, `setMeasure`, `getConfigOptions`, `netHost/netJoin/netLeave` (0; declared in the class, not registered by the exe), `openLib`, `isLoadingReset`, `isLoading`, `setLdPriority` | state is exposed as properties (`TimeWarp`, `GameTime`, `Measure`, `LoadPriority`, `MountedLibs`) and hooks (`OpenLibHandler`, `IsLoading`, `LoadingReset`, `ConfigOptionsRefresh`, `InfoProvider`) for the resource/render areas |
| `java.io.File` | `open/close`, `write(int/float/String/ResourceRef)`, `readInt/readFloat/readString/readResID`, static `delete/copy/move/exists`, `TimeLow/TimeHigh` (Slrr_GI extras) | status values are 1 / 0 (H) |
| `java.io.FindFile` | `first(pattern, flags)`, `next`, `close` | bare names, sorted case-insensitively, no `.`/`..` (H) |
| `java.lang.GameType` | `addTimer(F,I)`, `removeAllTimers()` | `EngineSystem.RegisterTimerNatives = false` if the game-object area registers them |
| `java.lang.Thread` | `sleep(F)` re-registered with `replace: true`, argument in **milliseconds** | `Slrr.Vm` treats it as seconds; the scripts (`sleep(1000)`, `waitTime * 1000`) and docs/engine-api/java.lang.md say ms. Set `FixThreadSleep = false` once the VM is fixed |

## Sandbox (`GameSandbox`)

Script paths are game-relative, `/` or `\`, case-insensitive, `.\` prefix allowed. Rejected (the native returns 0/null and
the reason is appended to `EngineSystem.Rejections`): leading slash (absolute/UNC), any `:` (drive letters, streams),
`..` or dot/space-terminated segments, control characters and `< > " |`, wildcards (except in `FindFile` patterns),
symbolic links anywhere on the path.

* `save/...` is the **save root**: read-write. Reads fall back to `<gameRoot>/save/...` when the save root has no such
  file (the shipped `save/controls/active_control_set` loads); directory listings merge both, save root first.
* `screenshots/...` is the only other writable area (`ScreenshotRoot`, default `<saveRoot>/_screenshots`).
* Everything else is read-only. Writing, deleting or moving anything outside the two mounts is refused.
* `File.open(MODE_WRITE)` creates missing directories (H), `delete` removes a file or an empty directory,
  `copy` overwrites and creates the destination directory, `move` also renames an empty destination-free directory.

## SDAT file format (`SdatFile.cs`) - measured

8-byte header `53 44 41 54 00 01 02 00` (`"SDAT"`, `00 01 02 00`), then untagged values: int = 4 bytes LE, float = 4 bytes LE,
String = `u32` length including the NUL, characters (Latin-1, H), NUL. `open(MODE_READ)` fails (returns 0) if the header is
missing (M per java.io.md). Guessed because the sample cannot show them:

* a null `String` is written as length 0 and reads back as null; the empty string is length 1 + NUL;
* `write(ResourceRef)` stores one int, the ref's `id()` (hook `ResourceIdOf`); `readResID` returns that int through
  `ResolveResourceId` (identity by default). Whether original (2003) saves store more than an int is **open**: it decides
  whether shipped saves load. A career save written by this engine and read back by the shipped `GameLogic.load` works
  (test `GameLogicSaveWritesTheCareerLayoutAndLoadReadsItBack`), but only against this engine's own encoding;
* reads past the end return 0 / 0.0 / null (the original behaviour is unobserved).

## Save-game layout (`SaveFormats.cs`)

Magic ids and versions (from the script constants): career `0x87654321` v21 (loads >= 20), vehicle `0x88664422` v5,
skin `0x82549867` v0, options `0xFEDCBA98` v12, controls `'CTRL'` v16. `SaveFormats.Probe/KindOf/IsLoadableCareer` mirror
`GameLogic.fileCheck`. The career file written by `GameLogic.save()` (driven through the VM with one player, no bots and
no market cars) is, after magic and version: character resid, money, prestige (float), player index in `speedymen`,
flags, name, races_won, races_lost, hints, winPinkSlips, partsbin, decal count + (decal resid, stickies)..., per other
racer (fileid, seed, prestige, botVd, nightVd, racer data), market car counts and flags, current car, time (float), day,
7 reserved ints. (The order is the script's; M from running it.)

`ControlSetFile` parses/writes `save/controls/active_control_set` (M): 5 devices, 78 bindings x 9 values, 4 smoothing
records x 6 values; **12 unexplained trailing bytes** (`0, 0, 8`) follow the last record in the sample. `ControlSet.load`
ignores them; they are kept in `Trailer` and not written back. Is it a footer that the original `File.close()` appends?

## Timers (`TimerService`) and time

`addTimer(seconds, id)` is one-shot; when due, `handleEvent(GameRef self, EVENT_TIME, int id)` is started in a new VM
thread `THRD-RUNVMI` (M: the thread name is an exe string; the int overload is chosen by parameter types), so handlers may
`Thread.sleep`. Timers fire in (due time, creation) order on the **wall** clock `Vm.Time` (H; `UseSimTime` switches to the
warped clock). `Frame(vm, dt)` = `vm.RunFrame(dt)`, then `SimTime += dt * (TimeWarp - 1)` (the VM advances SimTime by dt;
M: `timeWarp(0)` pauses it, `timeWarp(-1)` queries), then due timers, then one zero-length frame so handlers start in
the same frame. `AcceptsTimeEvents` lets the object layer enforce `setEventMask(EVENT_TIME)`.
`Signal` is pure script (`wait/notifyAll`), nothing to implement.

## String identity (finding for the VM owner)

The VM compares objects by reference (`==` at 0x41c5c0: M per vm.md). `ControlSet.load` compares names read from the
file with `Input.getDeviceName(i)` using `==`; with fresh String objects it never matches and every device id comes out
as `-1 - n`. It works if native-produced strings are canonical: `EngineSystem.Intern(vm, text)` is a per-VM pool used
by `readString`/`FindFile` here (and by a test stub of `getDeviceName`). Other areas returning strings that scripts may
compare (device names, resource names) should use it; ideally the VM interns string literals in the same pool.
(H: the engine interns strings.)

## Not done / open

* Original `File.close()` footer (see trailing 12 bytes), `write(ResourceRef)` encoding, behaviour of reads past EOF,
  whether `FindFile` returns `.`/`..`, status values of `delete/copy/move`.
* `openLib` only records and delegates (`OpenLibHandler`); mounting the archive into a resource tree belongs to the
  resource area.
* Vehicle/skin save files (`Vehicle.save/load`, nested `Part.save`) are script code on the same File natives; not driven
  here (needs the parts/inventory classes).
