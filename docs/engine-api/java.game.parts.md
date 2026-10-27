# java.game.parts

Script roots: `parts\scripts\*.class` (10 classes in `java.game.parts`, with sources in `parts/scripts/**/src/*.java`),
`parts\accessories\scripts`, and ~1,500 part classes spread over the car/engine/suspension archives. Natives:
Part 29, WheelRef 35, Chassis 23, SfxTable 3, DynoData 5. The `.java` sources of the base classes (Chassis, Tyre, Wheel,
Swaybar, ...) are in the repository and were used as ground truth for the notes.

## Part families

| Sub-package | Classes | Examples |
|---|---|---|
| `java.game.parts.` | 10 | `Audio`, `BodyPart`, `Descriptor`, `DynoData` |
| `java.game.parts.CMS_Brakes` | 28 | `Disc`, `brake_caliper_Brembo_4_L`, `brake_caliper_Brembo_6_L`, `brake_caliper_Endless_4` |
| `java.game.parts.MrSir_Running_Gear` | 48 | `AP_shock`, `Bilstein_B14_shock`, `GReddy_Type_R_shock`, `GReddy_Type_S_shock` |
| `java.game.parts.accessories` | 5 | `NOS_canister_12pds`, `NOS_canister_24pds`, `stock_Battery_black`, `stock_Battery_blue` |
| `java.game.parts.audioparts` | 6 | `AudioAccessories`, `AudioAmplifier`, `AudioBoxes`, `AudioHeadUnit` |
| `java.game.parts.bodypart` | 33 | `Bumper`, `Chassis`, `DecorativeBodyPart`, `ExtraLights` |
| `java.game.parts.enginepart` | 46 | `AirCooler`, `AirFuelDeliverySystem`, `Battery`, `Block` |
| `java.game.parts.engines` | 272 | `Baiern_88er_crankshaft`, `Baiern_90er_crankshaft`, `Baiern_Devils_6SFi_3_6_block`, `Baiern_Devils_6SFi_3_6_flywheel` |
| `java.game.parts.interior` | 25 | `seat_Pista`, `seat_Roadster_black`, `seat_Roadster_blue`, `seat_Speed_black` |
| `java.game.parts.mufflers` | 21 | `muffler_01`, `muffler_02`, `muffler_03`, `muffler_04` |
| `java.game.parts.neons` | 28 | `Ball`, `Botle`, `CD`, `Cubes` |
| `java.game.parts.rgearpart` | 23 | `Pneumatic_ShockAbsorber`, `ReciprocatingRGearPart`, `SetAgain`, `ShockAbsorber` |
| `java.game.parts.running_gear` | 342 | `brake_Baiern_CS_front`, `brake_Baiern_CS_rear`, `brake_Baiern_DS_front`, `brake_Baiern_DS_rear` |
| `java.game.parts.wheels` | 182 | `rim_Baiern_DTM_11_0_19_ET__25_LOD_CATALOG_GARAGE`, `rim_Baiern_DTM_13_0_19_ET__40_LOD_CATALOG_GARAGE`, `rim_Blossom_10_0_17_ET_0_LOD_CATALOG_GARAGE`, `rim_Blossom_10_0_19_ET_0_LOD_CATALOG_GARAGE` |


Base classes (script, `parts\scripts`):

| Class | Extends | Methods | Fields | Natives | Role |
|---|---|---|---|---|---|
| `java.game.parts.Audio` | `java.game.parts.Part` | 3 | 2 | 0 | Part subclass for audio parts (speakers). |
| `java.game.parts.BodyPart` | `java.game.parts.Part` | 3 | 2 | 0 | Part subclass for body panels, lights, glass, seats, wings; adds drag_reduction. |
| `java.game.parts.Descriptor` | `java.lang.Object` | 2 | 5 | 0 | Spawn description passed through addStockParts: wear, tear, color, optical, power (values >1 mean tuned stock parts). |
| `java.game.parts.DynoData` | `java.lang.Native` | 10 | 64 | 5 | Native dyno/engine-state record (Native subclass). Script-created: `<init>() { super(); newNative(); }`, finalize -> deleteNative. The ~64 script fields (cylinders, bore, stroke, valve timings, spark timing, turbo and nitro constants, mixture, flags) are filled by EnginePart.fillDynoData in the part tree and the whole object is run through calcDyno. |
| `java.game.parts.EnginePart` | `java.game.parts.Part` | 6 | 1 | 0 | Part subclass for engine internals; adds fillDynoData participation. |
| `java.game.parts.Part` | `java.lang.GameType` | 78 | 53 | 24 | Base of every installable part; a GameType created from a gametype entry whose cfg kind is `part` (1,502 of them). Its constructor is `Part(int id)` and chains `super(id)`. The cfg (`slot`/`attach`/`compatible`/`category`...) is parsed by the engine; the script holds the economics (name, value, wear limits, prestige), the install checks and the data the physics reads (see the field contract on the Chassis class). See entry-points.md for the slot protocol. |
| `java.game.parts.RGearPart` | `java.game.parts.Part` | 3 | 3 | 0 | Part subclass for 'running gear': wheels, tyres, brakes, springs, shocks, suspensions, sway bars. |
| `java.game.parts.Set` | `java.lang.GameType` | 2 | 3 | 0 | GameType that loads a 'part set' (Catalog calls create(null, parts, null, "set_loader")). |
| `java.game.parts.SfxTable` | `java.lang.Native` | 4 | 0 | 4 | Native RPM->sound mapping table for one car sound channel; obtained with `Chassis.getSfxTable(0..2)`. Engine blocks call `clear()` then `addItem(...)` in updatevariables() on tables 0 and 2 and clear table 1 (channel meaning unverified; table 2 is accompanied by setSfxExhaustMinVol, so it is probably the exhaust). |
| `java.game.parts.WheelRef` | `java.lang.Native` | 32 | 0 | 32 | Native wheel/suspension corner (java.lang.Native subclass, no constructor; the engine hands out instances via Chassis.getWheel(i), i = 0..3). The script part classes (Wheel, Tyre, Spring, ShockAbsorber, Suspension, Brake) push their computed constants into it from updatevariables(). Pacejka tyre model coefficients are indexed 0..14 (`setPacejka(int index, float value)`, Wheel.java sets all 15; Tyre.java comments say index 2 = friction, 4 = stiffness, 11 = max load, as set by the dedicated setters). |


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

### java.game.parts.Part

`extends java.lang.GameType`. Compiled in `parts/scripts/Part.class`.

(H) Base of every installable part; a GameType created from a gametype entry whose cfg kind is `part` (1,502 of them). Its constructor is `Part(int id)` and chains `super(id)`. The cfg (`slot`/`attach`/`compatible`/`category`...) is parsed by the engine; the script holds the economics (name, value, wear limits, prestige), the install checks and the data the physics reads (see the field contract on the Chassis class). See entry-points.md for the slot protocol.

<details><summary>26 static fields with initial values</summary>

`COMMON = 0`, `PACKAGE = 1`, `PRESTIGE_DOWNGRADE_MUL = 1.08f`, `PRESTIGE_VALUE_MUL = 0.012187f`, `MF_STOCK = 0`, `MF_HOLLEY = parts.rpk:0x35r`, `MF_HOLLEY2 = parts.rpk:0x35r`, `MF_AUTHORITY = parts.rpk:0x36r`, `MF_NEUSPEED = 0`, `MF_BILLSTEIN = parts.rpk:0x33r`, `MF_OZ = 0`, `MF_BAER = parts.rpk:0x37r`, `MF_BFGOODRICH = 0`, `MF_BFGOODRICH2 = 0`, `MF_BFGOODRICH3 = 0`, `MF_BORLA = parts.rpk:0x3Ar`, `MF_NOS = parts.rpk:0x3Br`, `MF_HR = parts.rpk:0x3Cr`, `MF_JSP = parts.rpk:0x3Dr`, `MF_JSP2 = parts.rpk:0x3Dr`, `MF_MICHELIN = 0`, `MF_MICHELIN2 = 0`, `MF_PROCHARGER = parts.rpk:0x42r`, `MF_PROCHARGER2 = parts.rpk:0x42r`, `MF_KN = parts.rpk:0x44r`, `SAVEFILEVERSION_PART = 1`

</details>

<details><summary>27 instance/uninitialised fields</summary>

`String name`, `String description`, `float value`, `float police_check_fine_value`, `float thoroughness_limit`, `float brand_new_prestige_value`, `float prestige_calc_weight`, `float price_catalog`, `float price_shop`, `float price_used`, `float price_repair`, `Ypr catalog_view_ypr`, `float repair_min_wear`, `float repair_min_tear`, `float repair_max_wear`, `float repair_max_tear`, `int carCategory`, `int manufacturer`, `int need_update`, `float drag_occluded`, `float drag_own`, `Vector3 drag_own_center`, `float C_drag`, `Vector3 drag_center`, `Vector attachedParts`, `float prev_prestige`, `float seed`

</details>

Engine-read field candidates (the exact field name is pushed as a string constant by exe code; first code xref in `StreetLegal_Redline.exe` after the `@`; short names can be coincidental): `name@0x485465`, `description@0x5469e6`, `C_drag@0x448cd6`, `drag_center@0x448c93`

| Method | Decl / Reg | Typed sites (callers) | Name-only sites | Arguments seen | Semantics and evidence |
|---|---|---|---|---|---|
| `float getWear()` | native/R | 24 (8) | 0 | (no args) | (H) Float condition factor in [0,1]; 1.0 = new. Repair raises it to repair_max_wear (0.9). 24 sites. e.g. `Part`: `getWear()` |
| `float setWear(float)` | native/R | 2 (2) | 3 (1) | 0: repair_max_wear x2 | (H) Sets wear; returns float (previous value probably). Part.repair: `if (getWear() < repair_max_wear) setWear(repair_max_wear)`. e.g. `Part`: `setWear(repair_max_wear)` |
| `void setMaxWear(float)` | native/R | 4 (4) | 452 (452) | 0: 100000.0f x2, 500000.0f | (H) Lifespan: `setMaxWear(kmToMaxWear(210000))` where kmToMaxWear(km) = km*1000 (metres). Wear accumulates natively with distance driven; 452 name-only sites (every engine/tyre/brake constructor). e.g. `MC_hood_scoop`: `setMaxWear(500000.0f)` |
| `float getTear()` | native/R | 17 (2) | 0 | (no args) | (H) Second condition factor (damage/tear), same convention. Part.getConditionNoAttach = wear * tear. e.g. `Part`: `getTear()` |
| `float setTear(float)` | native/R | 1 (1) | 3 (1) | 0: repair_max_tear | (H) Sets tear. e.g. `Part`: `setTear(repair_max_tear)` |
| `int getTexture()` | native/R | 7 (4) | 2 (1) | (no args) | (H) Resource id of the paint texture. Vehicle saves/restores it. e.g. `Part`: `getTexture()` |
| `int setTexture(int)` | native/R | 3 (3) | 3 (2) | 0: v2 x2, v7 | (H) Sets the paint texture (Descriptor.color chosen from GameLogic.CARCOLORS). e.g. `Part`: `setTexture(v2)` |
| `int getMesh()` | native/R | 5 (3) | 0 | (no args) | (H) Resource id of the mesh currently shown (Part.save writes it). e.g. `Part`: `getMesh()` |
| `int setMesh(int)` | native/R | 0 | 5 (3) |  | (H) Swaps the mesh (DecorativeBodyPart builds a scaled temp mesh and assigns its id). |
| `int getRenderType()` | native/R | 2 (2) | 0 | (no args) | (M) Index of the render variant (LOD/appearance) the engine uses; MrSir suspension scripts read it. e.g. `MrSir_ShockAbsorber`: `getRenderType()` |
| `int setRenderType(int)` | native/R | 1 (1) | 242 (75) | 0: v3 | (M) Sets it (242 name-only sites; Brake sets 3 values). e.g. `Brake`: `setRenderType(v3)` |
| `int[] install_OK(GameRef, int, GameRef, int, Vector3)` | native/R | 1 (1) | 1 (1) | 0: p0; 1: 0; 2: v5; 3: 0; 4: p1 | (H) `install_OK(GameRef car, int slot, GameRef outTarget, int ?, Vector3 dropPos)` returns int[] (null/empty when the part cannot be installed there). Garage then calls the script `installCheck(Part target, int[] match)` for a String reason. The int[] is the matched (slot, targetSlot) pair; exact layout unverified. e.g. `InventoryItem_Part`: `v4.install_OK(p0, 0, v5, 0, p1)` |
| `int flap(int)` | native/R | 2 (1) | 0 | 0: 0, 1 | (M) `flap(0)` queries, `flap(1)` toggles a flap/door state (Part.getFlap/toggleFlap); Mechanic sends command "flap_toggle" to the car. e.g. `Part`: `flap(0)` |
| `int getLogo()` | script/R | 0 | 3 (3) |  | (H) Declared as script method returning `manufacturer`; the exe also registers a native getLogo (undeclared natively). |
| `int getCar()` | native/R | 1 (1) | 2 (1) | (no args) | (H) Id of the Chassis/car the part is installed into (0 when loose). addPart uses `getCar()` and falls back to this.id(). e.g. `Part`: `getCar()` |
| `int getWheelID()` | native/R | 205 (159) | 0 | (no args) | (H) Index (0..3 = FL, FR, RL, RR) of the wheel position a suspension/brake/spring part belongs to, derived from the slot it is plugged into. 205 typed sites in the MrSir/CMS suspension scripts. e.g. `Disc`: `getWheelID()` |
| `void disableSlot(int, int)` | native/R | 10 (3) | 30 (1) | 0: crankshaft_slot_ID x4, exhaust_camshaft_slot_ID x2; 1: 1 x5, 0 x5 | (H) `disableSlot(int slotId, int disabled)`: 1 greys out/blocks a slot so nothing can be installed or removed there (Chassis.updatevariables enforces an outside-in order this way: while the wheel is fitted only the wheel slot stays enabled, then brake, then spring/shock, then suspension; engine blocks disable crankshaft/camshaft slots). e.g. `Block`: `disableSlot(crankshaft_slot_ID, 1)` |
| `String getSlotDamage(int)` | native/R | 1 (1) | 0 | 0: v7 | (H) Per-slot damage state as a String (opaque engine text, saved verbatim by Part.save). e.g. `Part`: `getSlotDamage(v7)` |
| `void setSlotDamage(int, String)` | native/R | 1 (1) | 0 | 0: v3; 1: v2 | (H) Restores it in Part.load. e.g. `Part`: `setSlotDamage(v3, v2)` |
| `void setSlotPos(int, Vector3, Ypr)` | native/R | 0 | 328 (137) |  | (H) `setSlotPos(slotId, Vector3 pos, Ypr ori)`: moves a slot's mount point at runtime (suspension scripts reposition wheel/brake/shock slots: `(101+i)`, `(111+i)`; 328 name-only sites). |
| `int getSlots()` | native/R | 4 (2) | 20 (20) | (no args) | (H) Number of slots this part has (cfg `slot` lines). e.g. `Part`: `getSlots()` |
| `int getSlotID(int)` | native/R | 5 (2) | 61 (21) | 0: v3 x3, v7 | (H) `getSlotID(index)` -> the slot id declared in the cfg `slot ... <id>` line at that index. `getSlotID(-1)` is used by Part.save as the id of the slot that connects to the parent (it is skipped when saving children). e.g. `Part`: `getSlotID(v3)` |
| `Part partOnSlot(int)` | native/R | 150 (100) | 52 (22) | 0: 1 x29, 4 x7 | (H) `partOnSlot(slotId)` -> the child Part script object linked at that slot (null when empty), seen from the part that owns the slot. 150 typed sites. On a child's own mounting slot it yields the parent. e.g. `Baiern_Devils_6SFi_3_6_block`: `partOnSlot(32)` |
| `int slotIDOnSlot(int)` | native/R | 9 (5) | 0 | 0: 1 x3, v8 | (H) `slotIDOnSlot(slotId)` -> the slot id on the OPPOSITE side of the link that occupies this part's slot `slotId` (0 = empty): Part.save stores it per child; Vehicle.isTestable checks `seat.slotIDOnSlot(1) == 901` (the seat's own mounting slot 1 is plugged into chassis slot 901). e.g. `Part`: `slotIDOnSlot(v8)` |
| `Part getCarRef()` | native/R | 6 (6) | 0 | (no args) | (H) The Chassis script object of the car (Part), used by BodyPart/RGearPart `getCar_LocalVersion()`. e.g. `Audio`: `getCarRef()` |
| `float getMass()` | undeclared/R | 0 | 0 |  | (L) Registered natively, not declared in the shipped Part.class. |
| `int getSlotIndex(int)` | undeclared/R | 0 | 0 |  | (L) Registered natively, not declared. |
| `int isSlotDisabled(int)` | undeclared/R | 0 | 0 |  | (L) Registered natively, not declared. |
| `int setSfxLoopParams(float, float)` | undeclared/R | 0 | 0 |  | (L) Registered natively, not declared. |

Decl/Reg: `native` = flagged 0x40 in the shipped .class, `script` = non-native in the .class but registered natively in an exe, `undeclared` = registered in an exe but missing from the shipped .class; `R` = registered in `StreetLegal_Redline.exe`, `G` = registered in `Slrr_GI.exe` (patched build), `-` = no registration found. Typed sites = call sites that name this method with its descriptor (distinct caller classes in brackets); name-only = sites that only name the method, counted over all classes (README conventions).

<details><summary>53 script-implemented members</summary>

`createFromFile(File, GameRef)`, `randomize(float)`, `random()`, `<init>(int)`, `installCheck(Part, int[])`, `isDynoable()`, `isDriveable()`, `getInfo(int)`, `isTuneable()`, `load(File)`, `save(File)`, `addpart(GameRef)`, `rempart(GameRef)`, `isStreetLegal()`, `isComplex()`, `calcPoliceFine(float)`, `calcPrestige(float, float)`, `currentPrestige()`, `currentPrestigeNoAttach()`, `calcPrice(float, float)`, `getConditionNoAttach()`, `getCondition()`, `currentPrice()`, `currentPriceNoAttach()`, `isRepairable()`, `repairCost()`, `getRealCondition(float, float, float, float, String)`, `clampTo(float, float, float)`, `rollTo(float, float, float)`, `HUF2USD(float)`, `tHUF2USD(float)`, `mHUF2USD(float)`, `Inch2Meter(float)`, `kmToMaxWear(float)`, `repair()`, `updatevariables()`, `updateDamage()`, `install()`, `remove()`, `falloff()`, `getFlap()`, `toggleFlap()`, `getWheel()`, `reCreate()`, `addStockParts()`, `addStockParts(int, float, float)`, `addStockParts(Descriptor)`, `addPart(int, String)`, `addPart(int, String, int, float, float)`, `addPart(int, String, Descriptor)`, `fillDynoData(DynoData, int)`, `getDynoData(DynoData, int)`, `check4warnings()`

</details>


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

### java.game.parts.bodypart.Chassis

`extends java.game.parts.BodyPart`. Compiled in `parts/scripts/bodypart/Chassis.class`.

(H) Root part of a car (cfg kind `car`, script class `cars...Baiern_CoupeSport` etc. extend it through the model classes). Its script FIELDS are the physics input: the engine reads the 23 field names listed below (torque, inertia, gear ratios, nitro...). It also gets the setters below and reads `dynodata`.

<details><summary>35 static fields with initial values</summary>

`MAKE_CUSTOM = 0`, `MAKE_BAIERN = 1`, `MAKE_DUHEN = 2`, `MAKE_EINVAGEN = 3`, `MAKE_ISHIMA = 4`, `MAKE_SHIMUTSHIBU = 5`, `MAKE_MC = 6`, `MAKE_EMER = 7`, `MAKE_HAULER_S_HEAVEN = 8`, `MAKE_PRIME = 9`, `MODEL_UNDEFINED = -1`, `DT_FWD = 1`, `DT_RWD = 2`, `IndexedData_Mileage = 8444`, `IndexedData_RPM = (8444 - 432)`, `IndexedData_GearStrength = (8444 - 804)`, `IndexedData_CarRuntime = (8444 - 720)`, `IndexedData_ExhaustActivity = (8444 - 724)`, `IndexedData_CurrentSteering1 = (8444 - 848)`, `IndexedData_CurrentSteering2 = (8444 - 844)`, `IndexedData_CurrentSteering3 = (8444 - 492)`, `IndexedData_Handbreak = (8444 - 828)`, `IndexedData_BreakStrength = (8444 - 836)`, `IndexedData_ThrottleStrength = (8444 - 840)`, `IndexedData_ClutchStrength = (8444 - 832)`, `IndexedData_NitroStrength = (8444 - 824)`, `IndexedData_NOSLevel = (8444 - 516)`, `IndexedData_EngineWear = (8444 - 708)`, `IndexedData_HandbreakLED = (8444 - 7472)`, `IndexedData_OverDriveLED = (8444 - 7456)`, `IndexedData_ASRLED = (8444 - 7488)`, `IndexedData_ABSLED = (8444 - 7504)`, `IndexedData_GearNumber = (8444 - 772)`, `IndexedData_Flags = 112`, `IndexedData_SpeedLimit = 8452`

</details>

<details><summary>160 instance/uninitialised fields</summary>

`float max_steer`, `float engine_torque`, `float engine_torque2`, `float engine_inertia`, `float maxRPM`, `float RPM_limit`, `float engine_friction_fwd`, `float engine_friction_rev`, `float engine_mass`, `float engine_rpm_idle`, `float starter_torque`, `float fully_stripped_drag`, `int make`, `int model`, `int drive_type`, `float brake_balance`, `int brake_balance_can_be_set`, `Vector exhaustSlotIDList`, `float diff_lock`, `int gears`, `float[] ratio`, `float rearend_ratio`, `float ClutchF`, `float tank_nitro`, `float consumption_nitro`, `int wheels`, `String vehicleName`, `String makerName`, `Vector3 wing_dir`, `Vector3 wing_F`, `int SFX_trans_fwd`, `int SFX_trans_rev`, `int SFX_ignition`, `float rpm_trans_fwd`, `float rpm_trans_rev`, `float sfx_starter_rpm`, `int suspend_update`, `int[] stock_parts_list_E`, `int[] stg_1_parts_list_E`, `int[] stg_2_parts_list_E`, `int[] parts_list_E`, `float stg_1_engne_kit_limit`, `float stg_2_engne_kit_limit`, `int[] stock_parts_list_FL`, `int[] stock_parts_list_FR`, `int[] stock_parts_list_RL`, `int[] stock_parts_list_RR`, `int[] stock_parts_list_F`, `int[] stock_parts_list_Rr`, `int[] stock_parts_list_L`, `int[] stock_parts_list_R`, `int[] stock_parts_list_T`, `int[] stg_1_parts_list_FL`, `int[] stg_1_parts_list_FR`, `int[] stg_1_parts_list_RL`, `int[] stg_1_parts_list_RR`, `int[] stg_1_parts_list_F`, `int[] stg_1_parts_list_Rr`, `int[] stg_1_parts_list_L`, `int[] stg_1_parts_list_R`, `int[] stg_1_parts_list_T`, `int[] stg_2_parts_list_FL`, `int[] stg_2_parts_list_FR`, `int[] stg_2_parts_list_RL`, `int[] stg_2_parts_list_RR`, `int[] stg_2_parts_list_F`, `int[] stg_2_parts_list_Rr`, `int[] stg_2_parts_list_L`, `int[] stg_2_parts_list_R`, `int[] stg_2_parts_list_T`, `int[] parts_list_FL`, `int[] parts_list_FR`, `int[] parts_list_RL`, `int[] parts_list_RR`, `int[] parts_list_F`, `int[] parts_list_Rr`, `int[] parts_list_L`, `int[] parts_list_R`, `int[] parts_list_T`, `float stg_1_body_kit_limit`, `float stg_2_body_kit_limit`, `int[] stock_parts_list_RGear_suspensions`, `int[] stock_parts_list_RGear_shocks`, `int[] stock_parts_list_RGear_springs`, `int[] stock_parts_list_RGear_brakes`, `int[] stock_parts_list_RGear_sways`, `int[] stock_parts_list_RGear_wheels`, `int[] stock_parts_list_RGear_tyres`, `int[] stock_parts_list_RGear_others`, `int[] stg_1_parts_list_RGear_suspensions`, `int[] stg_1_parts_list_RGear_shocks`, `int[] stg_1_parts_list_RGear_springs`, `int[] stg_1_parts_list_RGear_brakes`, `int[] stg_1_parts_list_RGear_sways`, `int[] stg_1_parts_list_RGear_wheels`, `int[] stg_1_parts_list_RGear_tyres`, `int[] stg_1_parts_list_RGear_others`, `int[] stg_2_parts_list_RGear_suspensions`, `int[] stg_2_parts_list_RGear_shocks`, `int[] stg_2_parts_list_RGear_springs`, `int[] stg_2_parts_list_RGear_brakes`, `int[] stg_2_parts_list_RGear_sways`, `int[] stg_2_parts_list_RGear_wheels`, `int[] stg_2_parts_list_RGear_tyres`, `int[] stg_2_parts_list_RGear_others`, `int[] parts_list_RGear_suspensions`, `int[] parts_list_RGear_shocks`, `int[] parts_list_RGear_springs`, `int[] parts_list_RGear_brakes`, `int[] parts_list_RGear_sways`, `int[] parts_list_RGear_wheels`, `int[] parts_list_RGear_tyres`, `int[] parts_list_RGear_others`, `float stg_1_rgear_kit_limit`, `float stg_2_rgear_kit_limit`, `int[] parts_list`, `Vector PneumaticSA`, `float maxHP`, `DynoData dynodata`, `int buildOnlyStockSuspensions`, `float old_brake_balance`, `float oldWheelXMultF`, `float oldWheelYMultF`, `float oldWheelXMultR`, `float oldWheelYMultR`, `float oldWheelRotYF`, `float oldWheelRotYR`, `float oldRimXMultF`, `float oldRimXMultR`, `float wheelPosMult`, `float rimPosMult`, `float blockPosX`, `float blockPosY`, `float blockPosZ`, `float blockRotY`, `float oldBlockPosX`, `float oldBlockPosY`, `float oldBlockPosZ`, `float oldBlockRotY`, `int fallBackEngineSound`, `int oldfallBackEngineSound`, `float oldFrontMaxSteer`, `float oldRearMaxSteer`, `float FrontMaxSteer`, `float RearMaxSteer`, `float WheelPosXMultF`, `float WheelPosYMultF`, `float WheelPosXMultR`, `float WheelPosYMultR`, `float WheelRotYF`, `float WheelRotYR`, `float RimPosXMultF`, `float RimPosXMultFMinOffset`, `float RimPosXMultR`, `float RimPosXMultRMinOffset`, `int SoundOverWriteCount`, `int oldForceFrontWheelDrive`, `float oldForcedDriveDistribution`, `int ForceFrontWheelDrive`, `float ForcedDriveDistribution`

</details>

Engine-read field candidates (the exact field name is pushed as a string constant by exe code; first code xref in `StreetLegal_Redline.exe` after the `@`; short names can be coincidental): `engine_inertia@0x44864b`, `maxRPM@0x4489f7`, `RPM_limit@0x448a08`, `engine_friction_fwd@0x448684`, `engine_friction_rev@0x448698`, `engine_mass@0x4486bd`, `engine_rpm_idle@0x4486ac`, `starter_torque@0x448670`, `gears@0x4489d5`, `ratio@0x44895a`, `rearend_ratio@0x4489e6`, `ClutchF@0x448a19`, `tank_nitro@0x448ae2`, `consumption_nitro@0x448af6`, `SFX_trans_fwd@0x448a2d`, `SFX_trans_rev@0x448a3d`, `SFX_ignition@0x448a4b`, `rpm_trans_fwd@0x448a60`, `rpm_trans_rev@0x448a78`, `sfx_starter_rpm@0x448a8c`, `suspend_update@0x4484c4`, `maxHP@0x46af53`, `dynodata@0x447d54`

| Method | Decl / Reg | Typed sites (callers) | Name-only sites | Arguments seen | Semantics and evidence |
|---|---|---|---|---|---|
| `float getTorque(float, float)` | native/R | 0 | 0 |  | (L) No script site. Native engine torque lookup `getTorque(float rpm, float boost)`; scripts compute torque through DynoData instead. |
| `float getMass()` | native/R | 9 (4) | 0 | (no args) | (H) Total car mass in kg from the cfg bodies. e.g. `TyreCalculations`: `GameLogic.player.car.chassis.getMass()` |
| `Vector3 getCM()` | native/R | 2 (2) | 0 | (no args) | (H) Centre of mass (Vector3). e.g. `TyreCalculations`: `GameLogic.player.car.chassis.getCM()` |
| `Vector3 getMin()` | native/R | 2 (2) | 0 | (no args) | (H) Bounding box minimum corner. e.g. `CarInfo`: `v13.getMin()` |
| `Vector3 getMax()` | native/R | 2 (2) | 0 | (no args) | (H) Bounding box maximum corner. e.g. `CarInfo`: `v13.getMax()` |
| `Vector3 getWheelPos(int)` | native/R | 1 (1) | 0 | 0: v1 | (M) Wheel position by index. e.g. `Chassis`: `getWheelPos(v1)` |
| `void forceUpdate()` | native/R | 49 (44) | 0 | (no args) | (H) Forces the engine to re-read the script fields after parts changed (49 typed sites: called at the end of every updatevariables, and by Vehicle after addStockParts with suspend_update). e.g. `Disc`: `the_car.forceUpdate()` |
| `String getWheelDamage(int)` | native/R | 1 (1) | 0 | 0: v3 | (H) Per-wheel damage String (saved in Chassis.save). e.g. `Chassis`: `getWheelDamage(v3)` |
| `void setWheelDamage(int, String)` | native/R | 0 | 1 (1) |  | (H) Restores it. |
| `void setCooling(float, float, float)` | native/R | 0 | 0 |  | (L) `setCooling(min, max, spd)`; source comment: defaults 10, 50, 0.01, spd is squared. No site. |
| `SfxTable getSfxTable(int)` | native/R | 42 (14) | 0 | 0: 0 x14, 1 x14 | (H) `getSfxTable(int channel)` returns the SfxTable (0..2). e.g. `Baiern_Devils_6SFi_3_6_block`: `the_car.getSfxTable(0)` |
| `void setSfxExhaustMinVol(float)` | native/R | 14 (14) | 0 | 0: 0.9f x10, 0.6f x4 | (H) Minimum exhaust volume (0.9 or 0.6). e.g. `Baiern_Devils_6SFi_3_6_block`: `the_car.setSfxExhaustMinVol(0.6f)` |
| `void setSteerWheelRadius(float)` | native/R | 0 | 0 |  | (L) No site. |
| `void setSteerWheel(float, float)` | native/R | 0 | 1 (1) |  | (H) `setSteerWheel(radius_m, z_offset_m)` from SteeringWheel: `((diameter-66)/2)/1000`, `hub_depth/100`. |
| `void setHornSFX(ResourceRef, float, int)` | native/R | 27 (9) | 0 | 0: v1 x27; 1: 1.0f x27; 2: 1 x9, 3 x9 | (H) `setHornSFX(ResourceRef sfx, float pitch, int index)`; each car model script calls it three times with index 1..3 (27 sites). e.g. `Baiern_models`: `setHornSFX(v1, 1.0f, 1)` |
| `void setNitroSFX(ResourceRef, float)` | native/R | 0 | 0 |  | (L) No site. |
| `float getMileage()` | native/R | 2 (2) | 0 | (no args) | (H) Odometer (float). e.g. `Chassis`: `getMileage()` |
| `void setMileage(float)` | native/R | 1 (1) | 2 (1) | 0: v3 | (H) Restored from the save file. e.g. `Chassis`: `setMileage(v3)` |
| `void setBuck(int, int, float, float, float, float)` | native/- | 0 | 0 |  | (L) `setBuck(partID, buckid, freq, prob, rpmdep, amp)` engine bucking effect; no site. Registered as returning int, declared void. |
| `int getWheels()` | native/R | 2 (2) | 0 | (no args) | (H) Number of wheels (4). e.g. `TyreCalculations`: `GameLogic.player.car.chassis.getWheels()` |
| `WheelRef getWheel(int)` | native/R | 119 (113) | 1 (1) | 0: v1 x109, v2 | (H) `getWheel(i)` -> WheelRef for corner i (0..3 = FL, FR, RL, RR). 119 typed sites. e.g. `MrSir_FL_strut_1`: `the_car.getWheel(v1)` |
| `float getIndexedData(int)` | native/G | 1 (1) | 0 | 0: IndexedData_RPM | (H) Slrr_GI.exe extra. Reads a float from the native vehicle struct by INDEX where index = 0x20FC - offset of the field; the constants IndexedData_* in Chassis.java name them: Mileage 0x20FC, RPM 0x20FC-0x1B0, GearStrength, CarRuntime, ExhaustActivity, CurrentSteering1..3, Handbreak, BreakStrength, ThrottleStrength, ClutchStrength, NitroStrength, NOSLevel, EngineWear, LED flags, GearNumber (int), Flags 0x70, SpeedLimit 0x2104. e.g. `Chassis`: `getIndexedData(IndexedData_RPM)` |
| `int getIndexedAddr(int)` | native/- | 0 | 0 |  | (L) Slrr_GI.exe extra: address form used with RawEdit.getI for int fields (declared, no site). |
| `void setAckermann(float)` | undeclared/R | 0 | 0 |  | (L) Registered natively, not declared. |
| `int setBuck(int, int, float, float, float, float)` | undeclared/R | 0 | 0 |  | (L) `setBuck(partID, buckid, freq, prob, rpmdep, amp)` engine bucking effect; no site. Registered as returning int, declared void. |
| `void setTorque(float)` | undeclared/R | 0 | 0 |  | (L) Registered natively, not declared. |


<details><summary>31 script-implemented members</summary>

`Hsuspension(int, int)`, `<init>(int)`, `finalize()`, `save(File)`, `load(File)`, `calcPoliceFine(float)`, `updatevariables()`, `addStockParts()`, `addStockParts(Descriptor)`, `updateDifflock()`, `isTuneable()`, `deg2rad(float)`, `buildTuningMenu(Menu)`, `endTuningSession(int)`, `handleMessage(Event)`, `ShockAbsorberForBigfoot(int)`, `PlaceRollPreventionBoxes()`, `UpdateOnlyWheels()`, `UpdateCamber(int)`, `UpdateOnlyBlock()`, `FallbackEngineSoundIndexToName(int)`, `ForceWheelDistribution(int)`, `SetFrontWheelDrive()`, `repair()`, `SetSFX_trans_fwd(int)`, `SetSFX_trans_rev(int)`, `SetSFX_ignition(int)`, `Setrpm_trans_fwd(float)`, `Setrpm_trans_rev(float)`, `Setsfx_starter_rpm(float)`, `GetCurrentTurboValue()`

</details>


`Chassis.IndexedData_*` constants (Chassis.java lines 1410..1436) map names to the native vehicle struct:
`index = 0x20FC - fieldOffset` (Mileage 0x20FC, RPM 0x1F4C = 0x20FC-0x1B0, CarRuntime -0x2D0, ExhaustActivity -0x2D4, CurrentSteering1..3
-0x350/-0x34C/-0x1EC, Handbreak -0x33C, BreakStrength -0x344, ThrottleStrength -0x348, ClutchStrength -0x340, NitroStrength -0x338,
NOSLevel -0x204, EngineWear -0x2C4, GearStrength -0x324, GearNumber -0x304 (int), indicator LEDs -0x1D30/-0x1D20/-0x1D40/-0x1D50,
Flags 0x70, SpeedLimit 0x2104). The C# vehicle state must expose the same named quantities to `getIndexedData`.

## Wheels, tyres, suspensions

### java.game.parts.WheelRef

`extends java.lang.Native`. Compiled in `parts/scripts/WheelRef.class`.

(H) Native wheel/suspension corner (java.lang.Native subclass, no constructor; the engine hands out instances via Chassis.getWheel(i), i = 0..3). The script part classes (Wheel, Tyre, Spring, ShockAbsorber, Suspension, Brake) push their computed constants into it from updatevariables(). Pacejka tyre model coefficients are indexed 0..14 (`setPacejka(int index, float value)`, Wheel.java sets all 15; Tyre.java comments say index 2 = friction, 4 = stiffness, 11 = max load, as set by the dedicated setters).

| Method | Decl / Reg | Typed sites (callers) | Name-only sites | Arguments seen | Semantics and evidence |
|---|---|---|---|---|---|
| `Vector3 getPos()` | native/R | 5 (3) | 2 (2) | (no args) | (H) Wheel centre relative to the chassis (CarInfo, Chassis). e.g. `Chassis`: `v8.getPos()` |
| `Ypr getYpr()` | native/R | 0 | 0 |  | (L) No site. |
| `float getDrive()` | native/R | 0 | 0 |  | (L) No site. |
| `float getSteer()` | native/R | 0 | 0 |  | (L) No site. |
| `float getRadius()` | native/R | 1 (1) | 4 (1) | (no args) | (H) Tyre radius in metres (CarInfo). e.g. `CarInfo`: `v21.getRadius()` |
| `void setPos(Vector3)` | native/R | 2 (1) | 1 (1) | 0: v11, v4 | (H) Moves the wheel position (Chassis.UpdateOnlyWheels applies the WheelPos*Mult tuning). e.g. `Chassis`: `v8.setPos(v11)` |
| `void setYpr(Ypr)` | native/R | 0 | 1 (1) |  | (M) Sets toe/caster/camber as Ypr in radians (Suspension: `new Ypr(side*toe*DEG2RAD, caster*DEG2RAD, side*camber*DEG2RAD)`). |
| `void setDrive(float)` | native/R | 3 (2) | 2 (1) | 0: drive_front x2, 0.0f | (H) Fraction (0..1) of engine torque sent to this wheel: Chassis zeroes all four in updatevariables, Transmission sets `drive_front` on front wheels and the rest on rear. e.g. `Chassis`: `v1.setDrive(0.0f)` |
| `void setSteer(float)` | native/R | 0 | 0 |  | (L) No site (steering is native). |
| `void setRadius(float)` | native/R | 1 (1) | 2 (2) | 0: wheel_radius | (H) Wheel.updatevariables passes wheel_radius. e.g. `Wheel`: `v2.setRadius(wheel_radius)` |
| `void setWidth(float)` | native/R | 1 (1) | 1 (1) | 0: rim_width | (H) Rim width. e.g. `Wheel`: `v2.setWidth(rim_width)` |
| `void setCPatch(float, float, float)` | native/R | 0 | 1 (1) |  | (M) `setCPatch(width, maxAngle, offset)` contact patch: Wheel passes `cpatch_width/1000` (mm -> m), `CPatchMaximumAngle`, `offset*something`. |
| `void setFriction(float)` | native/R | 2 (2) | 0 | 0: insider_friction, 1.0f | (H) Static friction coefficient (Tyre: insider_friction; Wheel resets to 1.0). e.g. `Tyre`: `v3.setFriction(insider_friction)` |
| `void setFrictn_x(float)` | native/R | 2 (2) | 0 | 0: insider_frictn_x, 1.0f | (H) Lateral friction factor (marked 'unused now' in Tyre.java). e.g. `Tyre`: `v3.setFrictn_x(insider_frictn_x)` |
| `void setSliction(float)` | native/R | 2 (2) | 0 | 0: insider_sliction, 1.0f | (H) Ratio dynamic/static friction. e.g. `Tyre`: `v3.setSliction(insider_sliction)` |
| `void setStiffness(float)` | native/R | 2 (2) | 0 | 0: stiffness, 100.0f | (H) Tyre stiffness (Wheel default 100, Tyre value). e.g. `Tyre`: `v3.setStiffness(stiffness)` |
| `void setRollRes(float)` | native/R | 2 (2) | 0 | 0: rollres, 0.001f | (H) Rolling resistance. e.g. `Tyre`: `v3.setRollRes(rollres)` |
| `void setBearing(float)` | native/R | 2 (2) | 0 | 0: 20.0f x2 | (H) Bearing friction (always 20.0 in the corpus). e.g. `Tyre`: `v3.setBearing(20.0f)` |
| `void setMaxLoad(float)` | native/R | 2 (2) | 0 | 0: loadcap, 1000.0f | (H) Load capacity in newtons (`loadcap`). e.g. `Tyre`: `v3.setMaxLoad(loadcap)` |
| `void setLoadSmooth(float)` | native/R | 1 (1) | 1 (1) | 0: 0.0f | (M) Load smoothing factor (Wheel sets 0.0, Tyre.java default 0.5). e.g. `Wheel`: `v2.setLoadSmooth(0.0f)` |
| `void setPacejka(int, float)` | native/R | 29 (2) | 3 (2) | 0: 0 x2, 1 x2; 1: 0.0f x19, 1.0f x2 | (H) See class note. 29 typed sites in Wheel.java/Tyre.java. e.g. `Tyre`: `v3.setPacejka(0, 0.0f)` |
| `void setForce(float)` | native/R | 0 | 1 (1) |  | (M) Spring force (Spring: `force * force_mul`). |
| `void setDamping(float)` | native/R | 0 | 3 (2) |  | (H) `setDamping(damping)` or `setDamping(bump, rebound)` (Spring passes both). |
| `void setDamping(float, float)` | native/R | 1 (1) | 3 (2) | 0: damping; 1: damping | (H) `setDamping(damping)` or `setDamping(bump, rebound)` (Spring passes both). e.g. `Spring`: `v1.setDamping(damping, damping)` |
| `void setRestLen(float)` | native/R | 2 (1) | 1 (1) | 0: Cur_length x2 | (H) Spring rest length (Pneumatic_ShockAbsorber.Cur_length). e.g. `Pneumatic_ShockAbsorber`: `whl.setRestLen(Cur_length)` |
| `void setMinLen(float)` | native/R | 0 | 3 (2) |  | (M) Travel limit `Cur_length - 0.05`. |
| `void setMaxLen(float)` | native/R | 0 | 4 (2) |  | (M) Travel limit `Cur_length + 0.05`. |
| `void setInstantCenter(float, float, float, float, float, float)` | native/R | 1 (1) | 1 (1) | 0: 10000.0f; 1: 10000.0f; 2: 10000.0f; 3: 10000.0f; 4: 10000.0f; 5: 10000.0f | (H) Six floats (two 3-vectors) describing the suspension instant centres; 10000,... means 'none/infinite'. Chassis.updatevariables resets to 10000 before suspensions overwrite. e.g. `Chassis`: `v1.setInstantCenter(10000.0f, 10000.0f, 10000.0f, 10000.0f, 10000.0f, 10000.0f)` |
| `void setBrake(float)` | native/R | 0 | 2 (1) |  | (H) Service brake torque for this wheel (Brake multiplies by the car's brake_balance for front/rear). |
| `void setHBrake(float)` | native/R | 1 (1) | 0 | 0: torque | (H) Handbrake torque. e.g. `Brake`: `v1.setHBrake(torque)` |
| `void setArm(float, float, float, float, float, float, float)` | native/R | 29 (29) | 80 (80) | 0: 0.296f x27, 0.244f x2; 1: 0.294f x27, 0.344f x2; 2: 0.03f x27, 0.054f x2; 3: 0.0f x27, 0 x2; 4: 0.0f x27, 0 x2; 5: 0.0f x27, 0 x2; 6: 1.0f x27, ... | (H) Seven floats describing a suspension arm (29 sites in MrSir strut scripts: 0.296, 0.294, 0.03, 0, 0, 0, 1): pivot position (3), direction/offset (3) and a scale. Roles unverified. e.g. `MrSir_FL_strut_1`: `v2.setArm(0.296f, 0.294f, 0.03f, 0.0f, 0.0f, 0.0f, 1.0f)` |
| `void setHub(float, float, float, float, float, float, float, float, float, float)` | native/R | 0 | 109 (109) |  | (M) Ten floats describing the hub geometry (109 name-only sites; first two are -1.0 and -0.121 in MrSir trailing arms). Roles unverified. |
| `float getBrake()` | undeclared/R | 0 | 0 |  | (L) Registered natively, not declared. |
| `float getHBrake()` | undeclared/R | 0 | 0 |  | (L) Registered natively, not declared. |
| `void setOppWheel(int)` | undeclared/R | 0 | 0 |  | (L) Registered natively, not declared. |



## Sound table and dyno

### java.game.parts.SfxTable

`extends java.lang.Native`. Compiled in `parts/scripts/SfxTable.class`.

(H) Native RPM->sound mapping table for one car sound channel; obtained with `Chassis.getSfxTable(0..2)`. Engine blocks call `clear()` then `addItem(...)` in updatevariables() on tables 0 and 2 and clear table 1 (channel meaning unverified; table 2 is accompanied by setSfxExhaustMinVol, so it is probably the exhaust).

| Method | Decl / Reg | Typed sites (callers) | Name-only sites | Arguments seen | Semantics and evidence |
|---|---|---|---|---|---|
| `int getItems()` | native/R | 0 | 0 |  | (L) No site. |
| `void clear()` | native/R | 42 (14) | 0 | (no args) | (H) Empties the table (42 typed sites, one per engine block per channel). e.g. `Baiern_Devils_6SFi_3_6_block`: `v1.clear()` |
| `void addItem(ResourceRef, float, float, float, float, float)` | native/RG | 0 | 552 (79) |  | (H) `addItem(ResourceRef sound, float rpm, float rpmMin, float rpmMax, float volMin, float volMax)`: e.g. (…0xA2r, 1500, 500, 6500, 0.42, 0.52) = a loop sample centred on 1500 rpm audible between 500 and 6500 with volume 0.42..0.52. Argument roles other than the ResourceRef are unverified. 552 name-only sites. |
| `int AddItemPtr(ResourceRef, float, float, float, float, float)` | native/G | 0 | 0 |  | (L) Slrr_GI.exe extra (same function body as addItem, returns int). |



### java.game.parts.DynoData

`extends java.lang.Native`. Compiled in `parts/scripts/DynoData.class`.

(H) Native dyno/engine-state record (Native subclass). Script-created: `<init>() { super(); newNative(); }`, finalize -> deleteNative. The ~64 script fields (cylinders, bore, stroke, valve timings, spark timing, turbo and nitro constants, mixture, flags) are filled by EnginePart.fillDynoData in the part tree and the whole object is run through calcDyno.

Static fields: `flagsXX_inoperable = -2147483648`, `flagsXX_has_flywheel = 1073741824`, `flagsXX_has_port_nos = 536870912`, `flagsXX_has_wet_nos = 268435456`, `flagsXX_has_dry_nos = 134217728`, `flagsI4_has_SC_intake_tube = 1`, `flagsI4_has_turbo_intake_tube = 2`, `flagsI4_has_stock_intake_tube = 4`, `flagsI4_has_air_filter_for_SC_intake = 8`, `flagsI4_has_air_filter_for_turbo_intake = 16`, `flagsI4_has_air_filter_for_stock_intake = 32`, `flagsV6_has_SC_intake_tube = 1`, `flagsV6_has_L_turbo_intake_tube = 2`, `flagsV6_has_R_turbo_intake_tube = 4`, `flagsV6_has_biturbo_intake_tube = 8`, `flagsV6_has_stock_intake_tube = 16`, `flagsV6_has_air_filter_for_SC_intake = 32`, `flagsV6_has_air_filter_for_L_turbo_intake = 64`, `flagsV6_has_air_filter_for_R_turbo_intake = 128`, `flagsV6_has_air_filter_for_stock_intake = 256`

<details><summary>44 instance/uninitialised fields</summary>

`float cylinders`, `float bore`, `float stroke`, `float Vmin`, `float in_min`, `float out_min`, `float in_max`, `float out_max`, `float time_in_open`, `float time_in_close`, `float time_out_open`, `float time_out_close`, `float time_spark_min`, `float time_spark_inc`, `float time_spark_RPM0`, `float time_spark_RPM1`, `float time_burn`, `float RPM_limit`, `float T_loss`, `float rpm_turbo_mul`, `float rpm_turbo_opt`, `float rpm_turbo_range`, `float P_turbo_max`, `float P_turbo_waste`, `float nitro_H`, `float nitro_cooling`, `float nitro_consumption`, `float mixture_ratio`, `float mixture_H`, `float max_fuel_consumption`, `float max_air_consumption`, `float torque`, `float torque2`, `float maxRPM`, `float Displacement`, `float Compression`, `float minCompression`, `float maxCompression`, `String fuelType`, `float maxTorque`, `float maxHP`, `float RPM_maxTorque`, `float RPM_maxHP`, `int flags`

</details>

Engine-read field candidates (the exact field name is pushed as a string constant by exe code; first code xref in `StreetLegal_Redline.exe` after the `@`; short names can be coincidental): `cylinders@0x46aaa5`, `bore@0x46aab7`, `stroke@0x46aac8`, `Vmin@0x46aae0`, `in_min@0x46aaf8`, `out_min@0x46ab1c`, `in_max@0x46ab0a`, `out_max@0x46ab2e`, `time_in_open@0x46ab43`, `time_in_close@0x46ab58`, `time_out_open@0x46ab6d`, `time_out_close@0x46ab82`, `time_spark_min@0x46abf1`, `time_spark_inc@0x46ac06`, `time_spark_RPM0@0x46ac1e`, `time_spark_RPM1@0x46ac33`, `time_burn@0x46abbd`, `RPM_limit@0x448a08`, `rpm_turbo_mul@0x46ac48`, `rpm_turbo_opt@0x46ac5d`, `rpm_turbo_range@0x46ac72`, `P_turbo_max@0x46ac8d`, `P_turbo_waste@0x46aca3`, `nitro_H@0x46af82`, `nitro_cooling@0x46af95`, `mixture_ratio@0x46acf1`, `mixture_H@0x46acdf`, `max_fuel_consumption@0x46ad1b`, `max_air_consumption@0x46ad06`, `torque@0x43ad49`, `torque2@0x46b039`, `maxRPM@0x4489f7`, `Displacement@0x46aefe`, `Compression@0x46af11`, `maxTorque@0x46af27`, `maxHP@0x46af53`, `RPM_maxTorque@0x46af3d`, `RPM_maxHP@0x46af69`, `flags@0x487d06`

| Method | Decl / Reg | Typed sites (callers) | Name-only sites | Arguments seen | Semantics and evidence |
|---|---|---|---|---|---|
| `void newNative()` | native/R | 1 (1) | 0 | (no args) | (H) Allocates the native twin (constructor). e.g. `DynoData`: `newNative()` |
| `void deleteNative()` | native/R | 1 (1) | 0 | (no args) | (H) Frees it (finalize). e.g. `DynoData`: `deleteNative()` |
| `float calcDyno(float)` | native/R | 1 (1) | 0 | 0: v13 | (H) `calcDyno(int steps)`: Chassis passes `maxRPM/250` (RPM step 250); computes the torque curve and fills `torque`, `torque2`, `maxRPM`, `maxHP`, `maxTorque`, `RPM_maxTorque`, `RPM_maxHP`. Returns float (unused). e.g. `Chassis`: `v11.calcDyno(v13)` |
| `float getTorque(float, float)` | native/R | 2 (2) | 0 | 0: rpm_idle, v33; 1: 0.0f x2 | (H) `getTorque(float rpm, float boost)`; CarInfo and Block use rpm_idle and boost 0. e.g. `Block`: `dynodata.getTorque(rpm_idle, 0.0f)` |
| `float getHP(float, float)` | native/R | 1 (1) | 0 | 0: v33; 1: 0 | (H) `getHP(float rpm, float boost)`. e.g. `CarInfo`: `v30.getHP(v33, 0)` |


Script members: `<init>()`, `finalize()`, `clear()`, `beAverageOf(DynoData, DynoData)`, `clone()`

