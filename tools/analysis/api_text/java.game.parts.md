# java.game.parts

Script roots: `parts\scripts\*.class` (10 classes in `java.game.parts`, with sources in `parts/scripts/**/src/*.java`),
`parts\accessories\scripts`, and ~1,500 part classes spread over the car/engine/suspension archives. Natives:
Part 29, WheelRef 35, Chassis 23, SfxTable 3, DynoData 5. The `.java` sources of the base classes (Chassis, Tyre, Wheel,
Swaybar, ...) are in the repository and were used as ground truth for the notes.

## Part families

{{PACKAGES java.game.parts.}}

Base classes (script, `parts\scripts`):

{{CLASSLIST java.game.parts.}}

## The part / slot / attach protocol

Facts about the **cfg side** (docs/formats/cfg.md, measured over the 1,182 shipped cfgs; 3,753 `slot`, 2,753 `attach`,
1,265 `compatible`, 813 `slottype`, 680 `slotdmgmode`, 675 `stockpart` lines):

```
slot      x y z  yaw pitch roll  ID      ; mounting point in the OWNING part's frame; ID is a small int that scripts hard-code
attach    GAMETYPE_ID  SLOT_ID            ; follows the slot it refers to
compatible GAMETYPE_ID  SLOT_ID           ; part cfgs only (1,244 lines have 3 fields)
slottype  N     slotdmgmode 0xMASK        ; group / damage mode of the preceding slot
stockpart GAMETYPE_ID                     ; car cfgs: parts that exist on a fresh car (no slot given)
category  N                               ; 1 engine, 2 body, 3 running gear (observed with Part subclasses)
```

* In a **car** cfg `attach 0x00040200 1` after `slot ... 101 ; FL wheel` means: stock part 0x40200 plugs into the car's slot
  101 with ITS slot 1 (`parts/scripts/Wheel.cfg` declares `slot ... 1 ; towards wheel hub`). The 40 shipped `_main_*.cfg` use
  115 distinct slot ids; the common ones are tabulated below.
* In a **part** cfg the comments in the shipped files read e.g. `attach 0x00000041 33 ; Baiern_Kraftwerk_2_5_block->engine_block_crankshaft_IN`
  after `slot ... 34 ; crankshaft_OUT`: the part lists the parents it can be plugged into together with the parent's slot id,
  i.e. the same relation as `compatible` seen from the other side (H for the comment text, M for the interpretation).
* Slot ids are a script-visible contract: `Block` declares `crankshaft_slot_ID = 40`, `transmission_slot_ID = 1`, `oil_pan_slot_ID = 37`,
  `cylinder_head_slot_ID = 47`; `Chassis.updatevariables` does `partOnSlot(101+i)` for wheels, `111+i` brakes, `301+i` shocks,
  `311+i` springs, `121+i` suspensions; the engine slot 999 is the steering wheel, 500..509 nitro tanks.

Run-time protocol (script side, H unless noted):

1. **Creation.** `ref.create(parent, GameRef(partTypeId), "", name)` (`Part.addPart`) creates the part object. The engine
   reads the gametype entry of the type (`script X` + `native part some.cfg`), builds the native part with the cfg's slots and
   calls the script class' `<init>(int id)`. Parts are created next to the car (`parent = new GameRef(car).getParentID()`).
2. **Install.** The link is made by a text command sent to the CHILD: `child.command("install f car childSlot parentPart parentSlot [x y z]")`
   (exe parser at 0x475f6d, format `%*s %d %d %d %d %d %f %f %f`). Measured token roles: `car` = instance id of the car/target (stored in the part),
   `parentPart` = id of the part to plug into, `parentSlot` = slot id on it (0 = let the engine choose), `[x y z]` = drop position; `f` and
   `childSlot` are parsed but unused by that handler (`childSlot` is filled by `Part.load` with the child's own mounting slot id, M). Script forms:
   `Part.addPart`: `"install 0 " + carId + " 0 " + parent.id() + " 0"`; `Part.load`: `"install 0 " + id + " " + childSlotId + " " + id + " " + parentSlotId`;
   `Garage` drag and drop: `"install 0 " + carId + " 0 0 0 " + x + " " + y + " " + z`; `InventoryItem_Part`: `"install 0 " + carId`. Removal:
   `child.command("remove 0 " + mapId)` (the part goes back to the map instance). The matching rule applied when `parentSlot`/`parentPart` are 0 is open (question 5).
3. **Checking.** Before issuing the command Garage calls the native `child.install_OK(car, 0, outRef, 0, dropPos)` which returns the
   matching slot pair as `int[]` (null = impossible) and then the SCRIPT `parent.installCheck(Part, int[])` which returns a refusal
   String or null. Also script-side: `isDriveable()` / `isDynoable()` (48 overrides) return a reason String when a car or engine is
   incomplete.
4. **Engine -> script notifications (M: defined by many scripts, called by none).** On a successful link the engine calls
   `parent.addpart(GameRef child)` (Part keeps `attachedParts` and resets `prev_prestige`) and on removal `parent.rempart(child)`;
   the child gets `install()` (22 overrides, all begin with `super.install()`), `remove()`, `falloff()` (2), and damage events
   `updateDamage()`; `reCreate()` after a mesh/texture change. Evidence: these names appear in the exe string table next to
   the callbacks `handleEvent`, `getInfo`, `drive`, `animate`, `control`, `update` (0x60cfac..0x60cfd8) and no shipped script ever calls them.
5. **Recalculation.** Scripts call `updatevariables()` (259 definitions) top-down: `Chassis.updatevariables` calls the children in a
   fixed order (brakes 111+, wheels 101+, springs 311+, shocks 301+, suspensions 121+, engine block, steering wheel 999, nitro tanks),
   each pushes its numbers into the native objects (`WheelRef.set*`, `SfxTable`, `DynoData`) and into its own fields, and
   `chassis.forceUpdate()` tells the engine to re-read the Chassis fields. `suspend_update=1` suppresses engine updates during bulk changes.
6. **Persistence.** `Part.save` writes: gametype id (write(GameRef)), version, texture, mesh, wear, tear, then for every slot with damage or a
   child: slot index, damage string, child's slot id, and recursively the child (terminated by -1). `Part.load` reverses it with
   `createFromFile` and re-issues the `install` command.

### Slot ids declared by the car cfgs (`tools/analysis/cfg_slot_census.py`, 40 cfgs)

Every id declared by at least 6 cars. `slottype` is the group number of the following `slottype` line.

| Slot id | Cars | Typical comment | slottype | stock `attach` |
|---|---|---|---|---|
| 3,4,5,7..40 | 6-25 | body panels: F_bumper (3), F_windshield (4), FL_door (5), L_headlights (7), trunk (14,15), quarterpanels (24..29), radiator (30..32), targa_top (35) | - | - |
| 101..104 | 24 | FL/FR/RL/RR wheel | 101..104 | 0x00040200:1 |
| 111..114 | 24 | brakes | 201..204 | 0x00040024:1 |
| 121..124 | 24 | hubs (suspension) | 201..204 | 0x0004022B..E:1 |
| 131..134 | 24 | 'hub up' | 201..204 | - |
| 301..304 | 22-24 | shock absorbers | 201/202/303/304 | 0x00041000:1 |
| 311..314 | 24 | springs | 201,501/202,302/303/304 | 0x00040051:1 |
| 351..354 | 24 | lower arms | 301..304 | - |
| 401, 403 | 24 | 401 has no comment (stock attach 0x000700DF:99 / 0x000700F9:99); 403 battery | - | 0x00045300:1 (403) |
| 500..508 | 6-24 | N2O canisters and extra seats (500, 505, 507: canisters; 506, 508: FR seat) | - | 0x00043200:1 / 0x0004D339:1 |
| 501..504 | 24 | `;upper arms on chassis` in the Duhen cfg (no comment elsewhere) | - | - |
| 523/524, 533/534, 543/544, 553/554, 563/564 | 24 | unlabelled rear-wheel (3, 4) pairs; slottype 203/204 on the first two | - | - |
| 572..577 | 10-24 | neons: front 572, left 573, right 574, engine 575/577, cubes 576 | - | 0x0004D267:1 |
| 601 | 25 | hood | - | - |
| 701, 702 | 10, 7 | front/rear swaybar | - | 0x00044000:1 |
| 901, 903 | 24, 17 | FL seat, rear seats | - | 0x0004007D:1 |
| 999 | 24 | steering wheel | 11 | 0x00040999:999 |

{{CLASS java.game.parts.Part}}

## Physics input: the Chassis script object

The native car reads its parameters from the SCRIPT fields of the chassis instance, by name, every time
`forceUpdate()` is called (the exe contains the exact field-name strings and pushes them to the VM's field accessors at
0x4484c4..0x448cd6; the same for `DynoData` at 0x46abdf..0x46af53). `Chassis.updatevariables()` is the producer: it sums
masses/drag from body parts (`C_drag -= p.drag_reduction`), selects the Block, runs `dyno.calcDyno(maxRPM/250)` and copies
`engine_torque`, `engine_torque2`, `maxRPM`, `RPM_limit`, `engine_inertia`, friction, idle rpm, `ratio[0..7]`, `rearend_ratio`,
`gears`, `ClutchF`, `tank_nitro`, `consumption_nitro`, `maxHP`, plus sound hooks. If no engine is installed it falls back to
`engine_torque=0, maxRPM=7000, ratio={0,3.5,2.7,1.9,1.3,0,0,-4}`. The C# physics therefore needs a typed `ChassisParams` record
that these fields feed, not the script object itself. The list below (and in the other class sections) is generated by matching
all field names against the exe string table with a code xref; short names can be coincidences (`name`, `value`, `init`).

{{CLASS java.game.parts.bodypart.Chassis}}

`Chassis.IndexedData_*` constants (Chassis.java lines 1410..1436) map names to the native vehicle struct:
`index = 0x20FC - fieldOffset` (Mileage 0x20FC, RPM 0x1F4C = 0x20FC-0x1B0, CarRuntime -0x2D0, ExhaustActivity -0x2D4, CurrentSteering1..3
-0x350/-0x34C/-0x1EC, Handbreak -0x33C, BreakStrength -0x344, ThrottleStrength -0x348, ClutchStrength -0x340, NitroStrength -0x338,
NOSLevel -0x204, EngineWear -0x2C4, GearStrength -0x324, GearNumber -0x304 (int), indicator LEDs -0x1D30/-0x1D20/-0x1D40/-0x1D50,
Flags 0x70, SpeedLimit 0x2104). The C# vehicle state must expose the same named quantities to `getIndexedData`.

## Wheels, tyres, suspensions

{{CLASS java.game.parts.WheelRef}}

## Sound table and dyno

{{CLASS java.game.parts.SfxTable}}

{{CLASS java.game.parts.DynoData}}
