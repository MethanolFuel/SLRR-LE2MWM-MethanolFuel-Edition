# Open questions that need dynamic traces of the original game

Everything below could NOT be settled from the shipped scripts, the cfg/rpk data or the static disassembly. For each item:
why it matters for the C# engine, what static evidence exists, and the smallest trace that would settle it. Hook points are
virtual addresses in `StreetLegal_Redline.exe` (identified statically, never executed): `0x42a430` read int field of a VM object by name,
`0x426170`/`0x426270` read Config static int/string, `0x53a5e0` gametype create, `0x47d7b0` native `GameRef.create`, `0x481a70`
`createNativeInstance`, `0x416b00` `registerNative`, `0x420ff0` `Thread::run`, `0x41fbc0` script/native dispatch, `0x408ce0` wake-all (notifyAll),
`0x551240` sscanf used by every command parser, `0x475f6d` part command parser (`install`, `remove`, `repair`...), `0x473b10` install check, `0x474600` attach.
A single logging session (start game, enter garage, install/remove a part, start a test drive, open an OSD dialog, save) with hooks on these would answer most items.

Priorities: **P0** blocks running any script end to end, **P1** blocks the gameplay slice (garage + test drive), **P2** polish/compatibility.

## P0

1. **Integer encoding of resource ids.** `RES` literals (`parts:0xAB5Cr`) evaluate to an int; `id()`, `getParentID()`, `getInfo(GII_TYPE)`, `readResID()` and the ids concatenated
   into command strings are ints too. Scripts compare them (`==`), add to them (`Racer.RID_FEJ + i + diff`), keep them in `int[]` and store them in saves.
   Static: rpk instance records use `0xSSSSIIII` (16-bit reference-table slot, 16-bit id) but a script-level id must be global across archives. Trace: log the
   int returned for 20 known literals (one per archive), `id()` of a live car/part, and the bytes `File.write(ResourceRef)` produces.
2. **Start-up ordering and the Config contract.** When is `java.util.Config` instantiated (its constructor loads `save/game/options`), relative to the exe's
   reads of `Config.video_*`/`acceptBuild` at 0x58c7ae..? How does the exe find class `Config` (package search)? Are the missing `Config.esp_*` fields
   (`esp_steer_scale`, `esp_angvel_scale`, `esp_yaw_allowed`, `esp_brake_level`, `esp_power_level`) fatal (`vm_get_float_field: unknown field`) or defaulted? What is in the
   `params` buffer (0x63c560) passed to the `GameInit` create? Trace: log VM field reads from process start to the first `Init.<init>` call.
3. **Event delivery.** Exact parameter strings for every `EVENT_*` (COLLISION, HIT, DAMAGE, TRIGGER_ON/OFF, WATER, INFO, BODYCOLLISION, GNDCOLLISION, CURSOR sub-codes
   `EC_*`), when `handleEvent` runs relative to threads and callbacks, how many events per tick, whether `addNotification` copies or filters (`deliverAs`, `filter`
   arguments are always `EVENT_SAME`/`null` in the corpus), what `asyncMode` changes at 0x425f12. Trace: log (target class, event, param string) per call for a Garage + Track session.
4. **VM semantics that scripts depend on numerically.** Time-slice length of `Thread::run(budget)`, whether `Thread.start()`/`notify()` run the other thread immediately or at the
   next tick, `Thread.sleep` clock (real vs sim time), float formatting of `String + float` and `Float.toString(f, "%1.3f")` (printf flavour), `String.floatValue/intValue`
   on malformed text, integer division/modulo of negatives, float to int conversion, `sqrt` bit-exactness (GameLogic's anti-tamper test is a float equality
   `MainMenu.Check(x) != sqrt(x)+3.75`, so the C# `Math.sqrt` and float arithmetic must reproduce the original bit for bit or every start shows 'PROTECTION ERROR').
   Trace: run a tiny probe script through the original (a mod class) and dump results, or log `Thread::run` entries with timestamps.
5. **Part linking.** The roles of `install`'s ints i1 and i3 (parsed but unused by the handler we read), the exact rule that picks the slot pair when i5 = 0 (`compatible`/`attach`/`slottype`
   match), what `install_OK` returns in its `int[]`, how `disableSlot` is enforced, and which script callbacks (`install`, `addpart`, `updatevariables`, `updateDamage`) the engine
   issues, in what order, for one install and one remove. Trace: hooks on 0x475f6d/0x473b10/0x474600 plus the VM call log during a garage drag-and-drop.
6. **`getInfo` key semantics.** Which `GII_*` keys the engine answers natively and which it asks the script for (`getInfo` string in the exe), return meaning for `GII_TYPE, OWNER, CATEGORY,
   BONE, GETOUT_OK, REMOVE_OK, INSTALL_OK, COMPATIBLE, PART_CATEGORY, AXIS, RENDER, CAR_TRAFFICPTR, SIZE, CAR_DRIVETYPE`. Trace: log `GameRef.getInfo(II)` arguments and results.

## P1

7. **Chassis / DynoData field contract.** 23 Chassis field names and 39 DynoData names have exact string xrefs (listed in java.game.parts.md); we do not know which are read vs
   written, when (`forceUpdate` vs every tick), units and axis/rotation conventions, the indexing of `ratio[0..7]` (reverse at 7?), or how `engine_torque`, `torquetable` and the
   DynoData curve are combined. Trace: log every field read/write (0x42a430 and its set counterparts) with the value during engine start, a 0-100 run and a gear change.
8. **Wheel/suspension natives.** Float layouts of `setArm` (7), `setHub` (10), `setInstantCenter` (6), `setCPatch`, `setDamping(FF)`, the 15 Pacejka coefficient roles, `setLoadSmooth`,
   `PhysicsRef.createBox` extents (half vs full), `Part.setSlotPos` frame, `Part.flap`. Trace: call each native with known values on a test car and observe forces/positions, or hook the
   registered functions (addresses in natives-redline.tsv: e.g. `Chassis.getTorque` 0x43c7f0) and dump their arguments for the stock cars.
9. **Command handlers.** Effect, units and ranges of every verb in the command table of entry-points.md (`look`, `move`, `dist`, `smooth`, `force`, `torque`, `angle`, `zoom`, `roll`,
   `render`, `simulate`, `setsteer`, `filter a b`, `corpse`, `osd`, `AI_*`, `cursor`, `mode`, ...). Trace: hook 0x551240 call sites to log (verb, parsed values) while the scripts run; the
   semantics then follow from the camera/car behaviour.
10. **Sound tables.** Roles of `SfxTable.addItem`'s five floats, `SfxRef.nplay`'s three floats, `Chassis.setHornSFX` index 1..3, `setSfxExhaustMinVol`, music set selection, channel gains.
    Trace: dump the table after an engine start and play with a known rpm sweep.
11. **GroundRef / traffic / pedestrian natives.** `setWater` arguments (point, normal, 300, 50), `addTrafficN/P` argument roles (count, speed, spawn radii), `getNearestCross(p, f)` second
    argument, `alignToRoad` return layout, `haltTraffic*`, `pedestrianDistance`, `notifyTrafficCar`, `setTrafficCarBehaviour` modes. Trace: call-argument logs while the City map runs.
12. **Save compatibility.** Encoding of `write(ResourceRef)`/`readResID()` (file format of everything except strings/ints/floats is now known, see java.io.md), and whether original
    career/vehicle saves must load. Trace: one real career save file (not in the repo) plus the matching in-memory ids.
13. **Rendering parameters.** `Camera.create` last four args (lodBias, lodAmp, culling, planeTest), `Viewport` layer numbers and `RENDERFLAG` effect, `RenderRef.setLight` argument order
    (diffuse, ambient, specular is inferred), `setFlare` arguments, `Text.create` parent semantics and font metrics, `GfxEngine.flush` and `openVideo(path, a, b)` flags.
14. **Garbage collection / finalizer timing.** When `finalize()` runs (`THRD-FIN`) and in which order relative to `destroy()`; `File.finalize` closes files that scripts forget to close;
    `ResourceRef.deleteNative`. Needed to avoid leaks or premature release in the C# VM. Trace: log finalizer calls with object class and frame number.

## P2

15. **Slrr_GI.exe extras.** Meaning of every `RawEdit` address (70 named variables, java.lang.md), the `getIndexedData` offsets (names are in Chassis.java), and whether the Mod
    behaviour the community relies on requires them. Trace: change one value at a time in a running game.
16. **Native kinds without script.** What `native road/walk/sun/ped/trlight/object/building/bot` entries do (57 gametypes have no script), the cfg-less `sun 0x19F` parameter, and
    whether an engine default script class applies. Static analysis of the object factory around 0x53a5e0 may be enough.
17. **Class loading rules.** Package-root search order (`system\scripts\lang`, `...\io`, `...\util`, `...\render`, `...\sound`, `sl\scripts\game`, `parts\scripts`, `parts\accessories\scripts`, then
    rpk `script` paths), name resolution when the `.class` path and the declared class name differ (`GE_Hood.class` -> `java.game.GE_hood`), source recompilation (`.java` newer than `.class`) and
    the `java.unnamed` fallback. Mostly static, but the case-insensitivity rules need a probe.
18. **Input details.** `Hotkey` flags/modifier semantics, `eventFilter` handling, `Input.activeAxis`/`lastKey` return conventions, force feedback (`user_SetAxisForce`).
19. **Network.** `System.netHost/netJoin/netLeave` are declared but not registered; `EVENT_NETEVENT` exists. Confirm that multiplayer is dead code.
20. **Cheat/obfuscation checks.** `MainMenu.Check`, `Track.Check`, `Garage.Check` (flags 0x29) and `GameLogic.kismajomCheck`'s keystroke buffer: reproduce exactly or patch out knowingly.
