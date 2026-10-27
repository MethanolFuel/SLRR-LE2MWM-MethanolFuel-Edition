# Script VM: native usage, implementation order and class dependencies

Phase 3 static research over all 1,692 shipped `.class` files (1,760 class definitions, 1,685 distinct class names; 62
names are defined in more than one file, mostly copies of the same part script, and are merged). Reproduce with
`python3 -I tools/analysis/native_usage.py` (parser `tools/analysis/tufa_lib.py`). **Nothing was executed.** Machine-readable
output: [`native-usage-natives.tsv`](native-usage-natives.tsv), [`native-usage-classes.tsv`](native-usage-classes.tsv),
[`native-usage-curve.tsv`](native-usage-curve.tsv), [`native-usage-order.json`](native-usage-order.json),
[`native-usage-summary.json`](native-usage-summary.json), [`class-deps.tsv`](class-deps.tsv),
[`engine-dependents.tsv`](engine-dependents.tsv). RawEdit is in [`rawedit.md`](rawedit.md).

## 1. Method

* A *native* is a method with MTHD flag `0x40`. The universe is 383 distinct `(class, method, descriptor)`: 358 declared native in
  shipped classes, 360 registered by `StreetLegal_Redline.exe`, 10 more registered only by `Slrr_GI.exe` (the 9 of
  `natives-slrr_gi-extra.tsv` plus `Chassis.getIndexedAddr`, found in this phase: Slrr_GI has 371 registrar calls, 360 + 11).
* **Call sites** = instructions that resolve to a native: `1A METHOD` refs resolved up the superclass chain (3,680), plus by-name
  calls (`12 MNAME`, `26 SUPERCALL`) whose receiver type is known from the call path: field descriptor, `CLASS`, typed local,
  implicit `this` (2,264 sites; the compiler emits 5,622 `MNAME`s, 5,449 resolved this way). 173 `MNAME` plus 1,283 `CALLBYNAME` sites
  have no static receiver type; for these only name + arity candidates exist (25 sites could be natives, kept in a separate column).
  Of 13,682 `METHOD` refs only 18 unresolved targets remain overall (classes `java.game.MotionBlur`, `java.game.MultiPlayer` are
  referenced by scripts but not shipped).
* **Closure** per method = natives reachable through calls, constructors (`new X(..)` -> constructors of matching arity + instance
  initialisers up the chain), `this(..)`/`super(..)`, static initialisers of every touched class. Three tiers:
  **A** exact refs only, **B** = A + overrides of each target in subclasses (CHA), **C** = B + all name+arity matches for untyped by-name
  calls. Headline numbers use **B**; A and C are in the TSVs.
* A class is **runnable** when every native in the closure of *all* its methods (plus initialisers) is implemented
  ("inst" variant: constructors and initialisers only). Inherited methods count only where called. Engine callbacks
  (`handleEvent`, `render`, ...) are *not* entry points beyond the class's own methods.

## 2. Headline numbers (facts under the stated method)

| Item | Value |
|---|---|
| natives in union / reachable from some shipped class (B) / from none (also C) | 383 / 265 / 115 |
| natives by package: java.game 107 (82 reachable), java.util 111 (65), java.lang 80 (55), java.io 39 (32), java.render 36 (25), java.sound 10 (6) | |
| call sites: java.game 2,313, java.lang 1,315, java.io 1,085, java.util 1,022, java.render 174, java.sound 35 | 5,944 total |
| classes needing 0 natives (all methods) / (instantiation only) | 416 / 650 of 1,685 |
| natives needed to run every shipped class (B) | **265** (first N with all 1,685 classes: 265; instantiation only: 225) |
| declared native but registered by neither exe (by class+name) | 12: `System.netHost/netJoin/netLeave`, `Thread.getCurrentThread`, `Camera.activate/deactivate`, `GroundRef.remPedestrianType`, `PhysicsRef.getAngVel/getVel(x2)/setStatic`, `RenderRef.getPos`; **none has a call site** |
| registered but not declared native in shipped classes | 25 (e.g. `Part.getMass/getLogo/getSlotIndex/isSlotDisabled/setSfxLoopParams`, `WheelRef.getBrake/getHBrake/setOppWheel`, `Chassis.setTorque/setAckermann`, `Sound.getMode/setMode/has3DHardware/hasMixHardware`, `GroundRef` route natives, `Camera.setFog`, `Text.get/setDefaultColor`) |

Top natives by call sites: `Float.toString` 549, `Part.setMaxWear` 456 (one site in each of 456 part classes), `Part.setSlotPos` 328,
`File.write(F)` 289, `File.readFloat` 270, `Part.setRenderType` 243, `ResourceRef.id` 228, `Part.getWheelID` 205, `Part.partOnSlot` 182,
`Math.random` 181. By class: `Part` 1,580 sites, `File` 993, `Float` 549, `ResourceRef` 443, `WheelRef` 306, `GameRef` 302, `Chassis` 277,
`Math` 226, `System` 182, `Thread` 127, `GameType` 123, `SfxTable` 121.

## 3. Prioritised implementation order

Raw call-site counts are a poor guide: ordering by sites reaches 1,019 runnable classes after 10 natives where a closure-aware
order reaches 1,056, and after 100 natives 1,033 versus 1,483. The recommended order is the greedy one in
`native-usage-natives.tsv` (`rank`): repeatedly take the native that completes the most classes, weighting classes by `1/remaining^2`.

First 40 (R = registered in Redline; sites = exact + typed; reach = classes whose closure contains it):

| # | Native | Reg | Sites | Calling classes | Reach (B) |
|---|---|---|---|---|---|
| 1 | `game.parts.Part.setMaxWear(F)V` | R | 456 | 456 | 756 |
| 2 | `lang.Float.toString(FString)String` | R | 549 | 46 | 340 |
| 3 | `lang.System.arraycopy` | R | 4 | 2 | 349 |
| 4 | `Part.getCarRef` | R | 6 | 6 | 523 |
| 5 | `Part.getWheelID` | R | 205 | 159 | 375 |
| 6 | `Part.setRenderType` | R | 243 | 76 | 274 |
| 7 | `WheelRef.setDamping(FF)` | R | 4 | 3 | 171 |
| 8 | `Part.getRenderType` | R | 2 | 2 | 48 |
| 9 | `Part.setSlotPos` | R | 328 | 137 | 332 |
| 10 | `Chassis.getWheel` | R | 119 | 113 | 269 |
| 11-14 | `WheelRef.setArm / setHub / setInstantCenter / setYpr` | R | 109 / 109 / 2 / 1 | | 221-224 |
| 15-17 | `ResourceRef.newNative`, `set(I)`, `set(ResourceRef)` | R | 1 / 10 / 3 | | 380-384 |
| 18-22 | `Part.getWear`, `Part.partOnSlot`, `WheelRef.setRadius / setBrake / setHBrake` | R | | | 197-234 |
| 23-26 | `Math.sqrt`, `Part.disableSlot`, `Object.notify`, `DynoData.newNative` | R | | | 91-231 |
| 27-32 | `GameRef.queueEvent`, `String.token`, `Camera.destroy`, `Viewport.destroy`, `ResourceRef.destroy`, `GameRef.create` | R | | | 59-298 |
| 33-40 | `Part.setWear/getCar/setTear/setTexture`, `ResourceRef.getParentID/id`, `WheelRef.setForce/setMaxLen` | R | | | 148-260 |

The full 383-row ranking, with the descriptor, registration status, both site kinds, distinct calling classes and per-tier reach,
is in `native-usage-natives.tsv`; the order is also in `native-usage-order.json`.

### Cumulative curve (greedy order, tier B; `native-usage-curve.tsv` has every N)

| N natives | classes runnable (all methods) | classes instantiable | methods runnable (of 3,891 with bodies) | by call-site order | by popularity order |
|---|---|---|---|---|---|
| 0 | 416 (24.7%) | 650 | 1,062 | 416 | 416 |
| 1 | 886 | 1,210 | 1,631 | 536 | 886 |
| 2 | 1,016 | 1,340 | 1,793 | 1,016 | 886 |
| 5 | 1,033 | 1,379 | 1,854 | 1,017 | 891 |
| 10 | 1,056 | 1,425 | 1,927 | 1,019 | 1,047 |
| 20 | 1,175 | 1,454 | 2,294 | 1,020 | 1,047 |
| 30 | 1,257 | 1,544 | 2,458 | 1,020 | 1,286 |
| 50 | 1,431 | 1,579 | 2,776 | 1,025 | 1,390 |
| 100 | 1,483 | 1,605 | 3,074 | 1,033 | 1,446 |
| 150 | 1,565 | 1,671 | 3,451 | 1,071 | 1,540 |
| 200 | 1,632 | 1,678 | 3,692 | 1,098 | 1,633 |
| 250 | 1,678 | 1,685 | 3,876 | 1,298 | 1,673 |
| 265 | 1,685 (all) | 1,685 | 3,891 | | |

File level (1,692 files, tier B, all methods): 405 runnable with 0 natives, 1,070 with 5, 1,212 with 20, 1,466 with 50, 1,507 with 100, 1,647
with 200, all 1,692 with 265. Tiers A and C bracket B (largest gap at N=200: A 1,645, B 1,632, C 1,586; A is optimistic, C pessimistic).

Reading: two natives (`Part.setMaxWear`, `Float.toString`) take the runnable set from 416 to 1,016 because 456 part classes call
`setMaxWear` (their closure includes it whenever that call is reachable from a method or the constructor); the next ~60 natives are the part/wheel/resource setters that add 400 more; the remaining 200 are a
long tail (UI, input, sound, route finding). Most of the head are **property setters** (`Part`, `WheelRef`, `Chassis`) that a headless
engine can implement as stored fields; real behaviour is only needed for `Math`, `String`, `Float`, `Integer`, `Vector3`, `File`, `Thread`.

### Phase 3 exit criteria (GameLogic, Catalog, OptionsDialog run headless)

Natives in the closure (union of the three classes): instantiating them needs **66** (tier B; 63 tier A; mostly `ResourceRef` 10,
`Viewport` 9, `File` 5, `Input` 5, `GameType` 5, `System` 5, `Controller` 4, `Text` 4, `GameRef` 4, `RenderRef` 4, `FindFile` 3, `Math` 3);
running all their methods needs **194** (tier B; 130 tier A; 236 tier C). Per class (B, all methods): GameLogic 153, Catalog 188,
OptionsDialog 182. The lists are in `native-usage-summary.json` (`exit_criteria`). Suggested order: first the 66 instantiation natives
(a stub engine can implement them as no-ops/stored fields), then the greedy ranking, which covers most of the other 128 within its first 100 entries.

## 4. Registry discrepancies (facts)

* 12 natives are declared in classes but registered by neither exe, 25 are registered but not declared native (section 2).
  `Chassis.setBuck` is registered as `(IIFFFF)I` but declared `(IIFFFF)V`, `GroundRef.removePedestrianType` (exe) vs `remPedestrianType` (class).
  The VM looks natives up by `class.method` (Phase 2 finding), so descriptor differences may be harmless; name differences are not.
* None of the 12 unregistered declarations is called by a shipped class, so they can be ignored until a script needs them.

## 5. Dependency graph

`class-deps.tsv` has 7,284 edges between 1,685 classes (`ref` = class constant or TYPE operand, `super`, `iface`, `sig` = only seen in
descriptors). Edges to classes not shipped: `java.game.MotionBlur` (2), `java.game.MultiPlayer` (1).
Distinct classes with at least one dependency on: `java.render.*` 138, `java.sound.*` 10, `java.util.resource.*` 90, `java.io.*` 104,
`java.game.*` 1,584 (1,517 on `java.game.parts.*`). Only 64 classes depend on nothing but `java.lang/util/io`. 139 classes touch
`java.render` or `java.sound`; the rest of the corpus is engine-independent once natives are stubbed.

Direct dependents (non-signature edges) of the engine classes; full list of 140 classes with `native_methods`, `inheritance_descendants`
and file counts in `engine-dependents.tsv`:

| Engine class | natives | direct dependents | files |
|---|---|---|---|
| render.Osd | 0 | 97 | 85 |
| render.osd.Menu | 0 | 70 | 67 |
| render.Frontend | 0 | 59 | 49 |
| render.Text | 7 | 50 | 42 |
| render.osd.Gadget / Slider / Button | 0 | 49 / 46 / 12 | 48 / 46 / 12 |
| render.osd.dialog.Dialog (32 subclasses) | 0 | 39 | 32 |
| render.GfxEngine | 11 | 27 | 27 |
| render.LoadingScreen | 0 | 26 | 26 |
| render.Viewport / Camera | 11 / 4 | 12 / 9 | 12 / 9 |
| sound.Sound (the only `java.sound` class) | 6 | 10 | 10 |
| game.parts.Part (1,484 descendants) | 24 | 575 | 568 |
| game.parts.bodypart.Chassis (33) | 23 | 249 | 249 |
| game.parts.WheelRef | 32 | 119 | 119 |
| game.GameLogic | 1 | 79 | 73 |
| game.Player / Vehicle | 0 / 3 | 47 / 44 | 40 / 41 |
| game.VehicleType / Track / Inventory | 0 | 18 / 17 / 16 | |
| game.parts.DynoData / SfxTable | 5 / 4 | 15 / 14 | |

Transitive dependents are not informative (every class reaches ~1,600 others through `Object`/`Native`/`Part`); use direct counts and
`inheritance_descendants`.

## 6. Caveats (hypotheses and limits)

* Reachability is static and over-approximate (virtual dispatch, dead methods); "runnable" counts therefore under-state what a
  stub engine can run when dead branches are never taken. Conversely, engine-initiated callbacks and by-name calls with unknown receivers
  can add natives (tier C).
* Duplicate class names (62) are merged by union, so a native used by only one of two variants counts for both.
* `Native`-derived classes (`ResourceRef`, `GameRef`, `DynoData`, ...) create engine objects through `newNative`/`createNativeInstance`;
  those are in the ranking but their C++ side effects are not modelled here.
* Source/class skew (Phase 1): all numbers describe the shipped `.class` files, not the `.java` files.
