# Engine area: resources, gametypes and boot (`src/Slrr.Engine.Resources`)

Phase 3 wave 2 of `docs/CSHARP_ENGINE_REWRITE_PLAN.md`. Natives of the `ResourceRef` / `GameRef` / `RenderRef` /
`PhysicsRef` / `GameType` family, the resource-literal id mapping, the gametype registry, gametype creation and the
original boot sequence, run headless on the script VM (`src/Slrr.Vm`). Every behaviour below is tagged
**measured** (read from `StreetLegal_Redline.exe` disassembly/strings, or from shipped data/scripts; nothing was
executed) or **guess** (headless stand-in, to be checked against a trace).

Build/test in isolation: `dotnet build Slrr.Engine.Resources.sln -warnaserror`, `dotnet test tests/Slrr.Engine.Resources.Tests`.

| File | Contents |
|---|---|
| `ArchiveTable.cs` | the engine's archive table and the literal → int mapping (P0 open question 1) |
| `ResourceIds.cs` | id layout helpers, `RESTYPE_*` codes |
| `ResourceTree.cs` | merged resource tree (rpk entries + run-time instances), entry text, instance id allocator |
| `GametypeRegistry.cs` | `script` / `native` lines, script inheritance through `superId`, map-record `gametype` ids, corpus survey |
| `ResourceEngine.cs` | handles, `GameRef.create`, `createNativeInstance`, event masks / timers / notifications / queued events |
| `ResourceNatives.cs` | the 71 natives (`INativeModule`) + `ArchiveNatives` (`System.openLib`) |
| `CommandParser.cs` | `sscanf` subset and the part `install` / `remove` parsers |
| `Boot.cs` | the start-up sequence, `BootReport`, `BootCommand` (CLI) |
| `tools/Slrr.Cli/CmdBoot.cs` | `boot` command wrapper (not yet wired into `Commands.cs`; `Slrr.Cli.csproj` references this project) |

## 1. Resource-literal ids (answers P0 open question 1) — measured

Opcode 0E pushes constant-pool type 3 `(archive string, id)`. The exe path is
`0x406660` → `0x4122b0(name, id)` → resource manager `0x538440(name, id)`:

1. find the archive by name with `_stricmp` (`0x5d7190`: case-insensitive, **no** path normalisation, `\` ≠ `/`),
   otherwise append a new slot (`0x543eb0`); the slot number is the **global archive index**;
2. load the archive once (`0x544170`): header, then the reference table, registering every referenced name with
   `0x538380` (find-or-register, no load) in slot order; record 0 of the table is the archive's own index;
3. map the id (`0x544590`): `local = id & 0xFFFF`, `slot = id >> 16`; local 0 → 0; `slot >= archiveCount` → 0;
   slot 0 → `(ownIndex << 16) | local`; `slot <= refCount` → `(indexOfReference[slot] << 16) | local`; else 0;
4. a missing archive file leaves the slot registered but unloaded and the literal evaluates to **0**.

So **runtime id = (global archive index << 16) | local id**; run-time instances are `0xFFFF0000 | n` (`0x536ed0`).
The table starts empty (`0x535e70`; a separate runtime archive has index 0xFFFF) and the start-up function registers
`system.rpk` first (`0x58c76a`), so system.rpk is archive 0 and `system.rpk:0x47r` = `0x00000047`. Other indices
depend on the order of first mention at run time (literals, reference tables, `System.openLib`); the C# table
reproduces the mechanism, so the same order of mentions gives the same numbers. Independent confirmation in shipped
source (`sl/Scripts/game/src/Catalog.java`): `partsPackId = System.openLib("parts.rpk")` (returns the index, measured
at `0x487be0`) is compared with `parts.id() >> 16` and with `car.getInfo(GII_TYPE) >> 16`.

Corpus check (`EveryShippedResourceLiteralResolves`): 1,769 distinct literals in 69 archive spellings; none uses a
reference slot (high 16 bits always 0); 1 is `:0x0r` (= 0 by definition); 1 names an archive that does not exist
(`cars\Misc\RC_Truck.rpk`) and is 0; 15 point at ids that no entry defines (6 in `parts\engines\Baiern_Emer.rpk`,
6 in `MW_Mod.rpk`, `system.rpk:0x2F` = `GameRef.RID_FRONTEND`, `particles.rpk:0x24`, `cars\racers.rpk:0x201E`) —
they still get a non-zero id, as in the exe. `cars\racers\prime.rpk` / `Prime.rpk` share one slot (stricmp).

Save files (`File.write(ResourceRef)`, P1 question 12) will contain these session-dependent numbers; how the exe
makes them stable across sessions is still open.

## 2. Boot sequence — measured unless noted

| Step | exe | C# |
|---|---|---|
| register `system.rpk` (`Data is missing. (distr. %s is needed)` if absent) | 0x58c765 | `Boot` step *mount system.rpk* |
| VM bootstrap: refs system.rpk:0x1 (type 8), 0x4 (type 1, world root, flags \|= 0x10 WORLDTREEROOT), 0x3 (14), 0xC (12), 0xB (13); natives registered | 0x427980 | *VM bootstrap* |
| **Config is created as a gametype**: `create(root, system.rpk:0x47, params 0x63c55c, "Config")` → `java.util.Config.<init>(int)` (loads `save/game/options`) — this happens **before** the exe reads the Config fields, answering part of P0 question 2 | 0x427ad8..0x427b1b | *create Config* |
| reads `acceptBuild` (> 606 → `Incorrect executable build number`), `version`, `FILD`, `video_*`, `shadows`, `resource_refreshrate`, `Sound_3D_HW/Mix_HW` | 0x58c7ae.. | *Config contract* (reports values) |
| `Config.init` (read by name with 0x42a430) wrapped as type 8, `create(root, init, params, "GameInit")` | 0x4289a0..0x4289e5 | *GameInit* |
| `java.game.Init.<init>` → `Sound.init`, `Frontend.init`, `Input.initControllers`, `new GameLogic()` → `changeActiveSection(new SplashScreen(...))` | scripts | *new GameLogic*, *SplashScreen* |
| per frame: notifyAll on `java.render.Frontend.render` | 0x428db9 | *frame loop* (+ `ResourceEngine.Pump`) |

Not reproduced: the `params` buffers (0x63c55c / 0x63c560, bss) are passed as null; the intro movies; video/sound init.

## 3. Gametype creation — measured unless noted

* `GameRef.create(parent, type, params, name)` (0x47d7b0 → 0x53a5e0): a null parent becomes the world root
  (system.rpk:0x4); a type node that is not TypeOfEntry 8 prints `create: Wrong GameType! %s:0x%08X` and creation
  **continues**; the instance id comes from 0x536ed0 (LIFO free list refilled with 128 ascending numbers, `| 0xFFFF0000`;
  counter start value 0 and reuse of destroyed numbers: guess); default name `_gameinst`; the script constructor
  `<init>(int)` receives the **new instance id**, in a thread named `THRD-CREATE`; missing constructor →
  `create: Undefined GameType Constructor! %08X (%s)`; the receiver handle is rebound to the new instance; returns the
  script object (null for native-only gametypes).
* The script class comes from the gametype's `script` line or, failing that, the nearest ancestor's (superId chain,
  across archives; guess for the exe's walk, but it reproduces the scripted part classes).
* `params` `x,y,z,yaw,pitch,roll` initialise position/orientation (guess (M), same text as map RSD records).
* Boot-time creates run the constructor in a spawned THRD-CREATE thread pumped until it ends; creates from script code
  run the constructor nested on the calling thread (guess; blocking natives are then rejected by the VM).
* `createNativeInstance(parent, typeId, params, name)` binds an existing script object to a new instance (M). If the
  object is already bound to its own instance by `create` (e.g. `Dummy(int)` reached through `create`), that instance
  is reused (guess).
* `newNative` allocates a handle only when none exists (measured, 0x47cea0), so the binding made by `create` survives
  constructors that call `super()` instead of `super(id)` (`java.game.Init` does).
* Refs handed out by the engine (`getFirstChild`, `getNextChild`, `getParent`, `getWTRoot`) are of class GameRef for
  TypeOfEntry 1 and 8, RenderRef for 3 and 14, else ResourceRef (measured switch at 0x47d300, table 0x47d3c4).
  `GameLogic.MWM_Items_Scan` relies on it (by-name `isScripted()` on chassis children).

## 4. Gametype registry — measured counts

`GametypeRegistry.Survey` (all 87 archives, using the engine's own reference resolution, no basename fallback; 7
referenced archive names have no file): 1,616 text entries with `script`/`native`; 1,574 `native` entries
(part 1,502, car 45, ped 6, road 6, button 3, sun 3, walk 2, camera/player/ground/trigger/cursor/invitem/bot 1);
own script 1,504, inherited 13, none 57 — identical to `tools/analysis/rpk_gametype_resolve.py`.
Map placement records: all 31,599 RSD `gametype 0xSSSSIIII` values of the 16 instance-tree archives resolve through
the archive's reference table to an existing entry; 31,598 are TypeOfEntry 8, one
(`maps\DamageTrack.rpk`, record gametype `0x00000004`) names a render object (type 14).

## 5. Native table (71 registrations + `System.openLib`)

All 71 exe registrations of these classes are implemented (natives-redline.tsv); the 5 declared-but-unregistered
ones (`RenderRef.getPos`, `PhysicsRef.getVel` ×2 / `getAngVel` / `setStatic`) have no call site and no exe
implementation and are left out.

| Native | Behaviour | Tag |
|---|---|---|
| `ResourceRef.newNative/deleteNative` | allocate handle if none / drop it | measured |
| `set(I)`, `set(ResourceRef)`, `id()` | handle id (+8) | measured |
| `type()` | node TypeOfEntry, 0 if unresolved | measured |
| `getParentID/getParent/getFirstChild/getNextChild/getWTRoot` | tree walk; ref class by node type | measured (class), guess (child order = table order, archives appended as loaded) |
| `load/unload/cache/precache` | counters only | guess |
| `destroy` | destroys an instance and its instance children, timers, subscriptions | guess (details) |
| `duplicate`, `makeTexture`, `makeSound`, `scaleMesh` | create/record a run-time resource node | guess |
| `GameRef.create/create_native` | section 3 | measured (+ guesses listed there) |
| `get/set/clearFlags` | node flags | M (0x427ac5 ORs 0x10 into the root's flags) |
| `getPos/getOri/getVel/setPos/setMatrix/setState` | stored transform, null keeps | guess (storage), M (null keeps) |
| `setParent` | re-parents an instance | M |
| `isEmpty` | 1 when the ref resolves to nothing | guess |
| `isScripted(String)` | instance: script object of (sub)class; gametype entry: registry script class of (sub)class | M |
| `getScriptInstance` | the script object | M |
| `getInfo(II)/(IString)` | GII_ID → id, GII_TYPE → gametype id; other keys → script `getInfo(int)` override, else 0 | guess (P0 question 6) |
| `queueEvent` | queued; delivered by `Pump` | guess (open question 3) |
| `setActiveCollision`, `getDetail` | no-op / 1.0 | guess |
| `RenderRef.create` | render instance (type 3) below parent, TypeId = resource | guess |
| other RenderRef setters, `getTypeID`, `getBoneId` (-1), `lineCreate/plotRoute` (0) | stored / constants | guess |
| `PhysicsRef.create/createBox/createSphere/setMatrix/getPos/getOri` | physics instance (type 2), stored values | guess |
| `GameType.createNativeInstance` | section 3 | M |
| `set/clearEventMask`, `addTimer` (one-shot), `removeAllTimers`, `add/remNotification`, `(un)registerCallback(s)` | stored; `Pump` fires due timers as `handleEvent(GameRef, EVENT_TIME, id)` when the mask has EVENT_TIME, delivers queued events as `handleEvent(GameRef, int, String)` when the mask accepts them and to `addNotification` subscribers (named handler if given); EVENT_COMMAND to a script-less object is recorded in `ResourceEngine.Commands` | guess (delivery rules) |
| `System.openLib(String)` | register + load, returns the archive index | measured |

## 6. Commands

`CScanf.Scan` implements the `sscanf` subset of the command formats (`%d %f %s %Ns %*`, literals, whitespace).
`PartCommands.ParseInstall` uses the measured part format `%*s %d %d %d %d %d %f %f %f` (car: `%*s %d %d %d %d %d`):
i2 = car, i4 = target part, i5 = slot (0 = choose), i1/i3 parsed but unused, position only with all 8 fields;
`ParseRemove` = `%*s %d %d`. The fewer-than-five-ints rejection is a guess. The `fog` format typo (`&f`) stops after
the int, as the original would.

## 7. Interfaces for other areas

* A `ResourceRef` (or subclass, e.g. `Viewport`, `Camera`, `Text`'s refs, `SfxRef`, `GroundRef`) keeps a
  `ResourceHandle` in `VmObject.Native`. Other areas must not replace it; put area state in
  `ResourceHandle.Extension` or `ResourceNode.Properties`, get the node with `ResourceEngine.NodeOf(obj)`.
* `ResourceEngine.Tree.CreateInstance(type, parent, name, typeId)` creates instance nodes (e.g. a render or physics
  area that owns `Viewport.create`/`Camera.create`).
* `ResourceEngine.Commands` / `QueueEvent` / `Pump` are where a car/camera/part area hooks command handling
  (`PartCommands` for `install`/`remove`).
* `BootOptions.Modules` takes the other areas' `INativeModule`s; `NativeRegistry.Fallback` is used by the boot to
  record missing natives (an integrating caller must not set its own Fallback on that registry).
* Not implemented here (left to the owning areas): `GroundRef` (25), `Animation` (9), `ParticleSystem` (9),
  `SfxRef` (2), `System.rpkScan` is script (needs `java.io.FindFile`).

## 8. `boot` CLI and results (repository game data)

```
slrr boot [gameRoot] [--stub] [--frames n] [--verbose] [--log] [--gametypes]
```

* Without `--stub`: system.rpk, VM bootstrap OK; **stops in `create Config` at `java.io.File.open(I)I`**
  (`Config.loadConfig` line 276) — the next native needed is in the io area.
* `--stub`: Config created, contract OK (acceptBuild 601, version v2.2.1, 800x600x32), `Config.init` = sl.rpk:0x40 →
  `java.game.Init` runs; 24 missing natives hit (File, Sound, Viewport, Text, Camera, Input, FindFile, System.timeWarp
  / setMeasure / setLdPriority, GfxEngine.forceRendering); stops in `GameLogic.initVehicleTypes` (`null.length`)
  because the stubbed `FindFile` makes `System.rpkScan` mount nothing.
* With a test `FindFile` stand-in (tests only, `TestFindFile`) and stubs: the whole sequence completes —
  `new GameLogic()` (vehicle types, chassis and map scans over 60+ archives, the MWM protection check passes),
  `GameLogic.actualState = java.game.SplashScreen`, 30 frames with 3 live script threads; 28 missing natives.

Static closure (docs/research native-usage-classes.tsv, tier B, all methods): lang natives only 546 runnable classes,
with this area 579 (+33: the `*_VT` vehicle types, body kits, `Dummy`, `GameRef`, `RenderRef`, `ResourceRef`,
`osd.Button`, `osd.Style`, markers, ...). The next single blockers by class count are `Part.setMaxWear` (495),
then `SfxRef.nplay`, `Part.partOnSlot`, `Text.update` (2 each).

## 9. Open questions (this area)

1. Child order of `getFirstChild/getNextChild` (entry-table order vs id order vs mount order) — guess: table order.
2. Exact moment archives load when a reference table names them (the exe loads lazily on first id resolution — modelled).
3. Event delivery rules (masks for EVENT_COMMAND, threads, order) — open question 3 of engine-api.
4. `getInfo` keys answered natively (open question 6); `GII_COMPATIBLE`/`INSTALL_OK` currently return 0 unless a script answers.
5. Instance id counter start value and recycling; whether `destroy()` clears the handle's id.
6. Whether `createNativeInstance` on an object already bound by `create` makes a second instance.
7. How ids written to saves survive a different archive registration order.
