# java.game

Script root: `sl\Scripts\game\*.class` (50 files) plus the mod's additions that compile into the same package
(`MW_Mod\Scripts\*`, `DestructionTools\Scripts\*`, `maps\Addon_Maps\*\Scripts\*`, car model scripts
`cars\racers\*_data\scripts\*` under `java.game.cars`). 97 classes in `java.game` itself, 439 in `java.game.cars`.
Only 7 natives live here (GameLogic 1, Vehicle 3, Painter 3, Navigator 1); the rest is the game logic proper, which
runs on the VM. For the C# engine this page is therefore mostly a map of *who calls what*.

## Game flow in terms of API calls

1. `Init.<init>(int)` (see entry-points.md): `Sound.init()`, `Frontend.init()`, loading screen on, `Input.initControllers`,
   `Input.enablePointingDevice()`, then `new GameLogic()`.
2. `GameLogic()` (decompiled): `createNativeInstance()`; fills `CLUBNAMES`, `CARCOLORS[0..20]`
   (cars.rpk colour resources 0x2A.., 0x42..0x50, 0x65..0x68); `Math.randomize()`; caches 6 `SfxRef`s; mounts content with
   `System.rpkScan("parts\\engines\\")`, `"parts\\"`, `"cars\\racers\\"`, `"cars\\fake_racers\\"`, `"maps\\"` and
   `System.openLib("MW_Mod.rpk")`; `initVehicleTypes()` (create one `VehicleType` script object per child of `cars:0x1000`),
   `MWM_Items_Scan()` (chassis children of `cars:0x0101`, map children of `MW_Mod:0x0ABC` after `rpkScan("maps\\Addon_Maps\\")`,
   driver skins via `FindFile` over `humans/drivers/Default_Driver/*.*`); runs an anti-tamper self test
   (`MainMenu.Check(random*100) != sqrt(key)+3.75` shows 'PROTECTION ERROR' and exits - a **float equality** test, so Math.sqrt
   and float arithmetic must be bit-compatible with the original); `preCacheGametypes` for `humans:0x1` and `cars:0x4`;
   controller join loop; `Garage`, five global `Hotkey`s; `Thread timeRefresher` (static `run()`); `setEventMask(EVENT_HOTKEY|EVENT_TIME)`;
   `addTimer(timeRefreshRate=10, 0)`; `Frontend.loadingScreen.hide()`; `changeActiveSection(new SplashScreen(frontend.rpk:0x5))`.
3. Game clock: `time` in seconds, `day`, `timeFactor` 4.0 (game time runs 4x). Every 10 seconds of timer time (`addTimer` seconds; real vs simulated clock unverified) the timer event
   `handleEvent(obj, EVENT_TIME, 0)` calls `spendTime(10*4)`, which wraps at 24*3600, increments `day`, notifies the text thread and
   pushes `System.syncGameTime(time)` to the engine.
4. Screens are `GameState`s switched with `GameLogic.changeActiveSection(state)` (exit old with the new as argument, enter new
   with the old). A null state ends the game via `System.exit()`.
5. Driving screens (`Track` subclasses) are found through the resource tree: every map archive has an entry `Script`
   (id 0xDC1) below `MW_Mod:0x0ABC`/`0x10ABC`/`0x20ABC` whose text is `script maps\\Addon_Maps\\X\\Scripts\\XTrack.class`; `MWM_Items_Scan`
   instantiates it once with `create(null, new GameRef(mapNode), "0,0,0,0,0,0", "load Track script")` and keeps the instance in `MapBox`.

## Vehicles

Creation paths (H): (a) `Vehicle(parent, typeId, colorIdx, power, optical, wear, tear)` -> `create(parent, GameRef(typeId),
"0,0,0,0,0,0", "Vehicle")` returns the `Chassis` script object, then `chassis.suspend_update = 1; chassis.addStockParts(new
Descriptor(CARCOLORS[color], ...)); chassis.suspend_update = 0; chassis.forceUpdate()`; (b) `Vehicle.load(path, parent)`
reads a vehicle file whose first record is a `Part.createFromFile` chain; (c) `Bot.createCar` from a `VehicleDescriptor`.
A Vehicle object is itself a `GameRef`: `set(chassis)` rebinds it to the chassis node, so `vehicle.setMatrix`, `vehicle.id()`,
`vehicle.queueEvent(...)` all act on the car.

### java.game.GameLogic

`extends java.lang.GameType` implements java.lang.Runnable. Compiled in `sl/Scripts/game/GameLogic.class`.

(H) The game controller singleton. Created by java.game.Init's constructor (`gameLogic = new GameLogic()`), runs from there on: `createNativeInstance()` in its own constructor, then rpkScan/openLib of the content archives, VehicleType/Chassis/Map scans, controller join loop, `Hotkey`s for music/screenshot, a clock Thread, and finally `changeActiveSection(new SplashScreen(frontend.rpk:0x5))`. It is a GameType AND a Runnable whose run() is STATIC.

<details><summary>75 static fields with initial values</summary>

`VEHICLETYPE_ROOT = new java.util.resource.ResourceRef(cars.rpk:0x1000r)`, `CHASSIS_ROOT = new java.util.resource.ResourceRef(cars.rpk:0x101r)`, `MAPS_ROOT = new java.util.resource.ResourceRef(MW_Mod.rpk:0xABCr)`, `INITIAL_PLAYER_MONEY = 20000`, `INITIAL_PLAYER_PRESTIGE = 0.3f`, `INITIAL_OPPONENT_PRESTIGE_MIN = 0.3f`, `INITIAL_OPPONENT_PRESTIGE_MAX = 0.9f`, `INITIAL_OPPONENT_PRESTIGE_RND = 0.03f`, `INITIAL_OPPONENT_AI_MIN = 0.51f`, `INITIAL_OPPONENT_AI_MAX = 1.0f`, `INITIAL_OPPONENT_AI_RND = 0.0f`, `INITIAL_OPPONENT_VHC_MIN = 0.0f`, `INITIAL_OPPONENT_VHC_MAX = 0.9f`, `INITIAL_OPPONENT_VHC_RND = 0.3f`, `INITIAL_OPPONENT_NIGHT_VHC_MIN = 0.0f`, `INITIAL_OPPONENT_NIGHT_VHC_MAX = 1.2f`, `INITIAL_OPPONENT_NIGHT_VHC_RND = 0.1f`, `SAVEFILEID_MAIN = -2023406815`, `SAVEFILEVERSION_MAIN = 21`, `SAVEFILEVERSION_MAIN_LOAD = 20`, `SFX_ENTERGARAGE = frontend.rpk:0x7Dr`, `SFX_LEAVEGARAGE = frontend.rpk:0x7Cr`, `SFX_WRENCH = frontend.rpk:0x7Br`, `SFX_HORN = sound.rpk:0x15r`, `SFX_DECAL = frontend.rpk:0x9Er`, `SFX_SPRAY = frontend.rpk:0x9Cr`, `SFX_DRAGDROP = frontend.rpk:0x9Dr`, `HUMAN_SMILEY = humans.rpk:0x9r`, `HUMAN_OPPONENT = humans.rpk:0x7r`, `HUMAN_OPPONENT2 = humans.rpk:0x8r`, `HUMAN_PLAYER = humans.rpk:0xCr`, `HUMAN_POLICEMAN = humans.rpk:0x18r`, `RID_CARCOLOR_Baiern_Devils_eye_red = 0`, `RID_CARCOLOR_Baiern_Spring_yellow = 1`, `RID_CARCOLOR_Einvagen_Zucker = 2`, `RID_CARCOLOR_Einvagen_Tornado_rot = 3`, `RID_CARCOLOR_Einvagen_Nacht = 4`, `RID_CARCOLOR_Einvagen_Smaragd = 5`, `RID_CARCOLOR_Einvagen_Black_mage = 6`, `RID_CARCOLOR_Einvagen_Hamvas_Grun = 7`, `RID_CARCOLOR_Einvagen_Indigo = 8`, `RID_CARCOLOR_Einvagen_Jazz = 9`, `RID_CARCOLOR_Einvagen_Antracit = 10`, `RID_CARCOLOR_Einvagen_Mercator_Blau = 11`, `RID_CARCOLOR_Einvagen_Murano = 12`, `RID_CARCOLOR_Einvagen_Champagner = 13`, `RID_CARCOLOR_Einvagen_Ozean = 14`, `RID_CARCOLOR_Einvagen_Reflex = 15`, `RID_CARCOLOR_Einvagen_Saratoga = 16`, `RID_CARCOLOR_Used_Rusty_Cherry = 17`, `RID_CARCOLOR_Used_Rusty_Smaragd = 18`, `RID_CARCOLOR_Used_Rusty_Nacht = 19`, `RID_CARCOLOR_Used_Rusty_Zucker = 20`, `CARCOLORS = new I[21]`, `carrerSaveDir = "save/carrer/"`, `tempSaveDir = "save/temp/"`, `carSaveDir = "save/cars/"`, `skinSaveDir = "save/skins/"`, `controlSaveDir = "save/controls/"`, `ROC_ENTRYFEE = 100000`, `CLUBS = 3`, `CLUBMEMBERS = 20`, `CLUBNAMES = new java.lang.String[CLUBS]`, `GM_INVALID = 0`, `GM_CARREER = 1`, `GM_FREERIDE = 2`, `GM_QUICKRACE = 3`, `GM_SINGLECAR = 4`, `GM_DEMO = 5`, `GM_MULTIPLAYER = 6`, `numplayers = 2`, `speedymen = new java.game.Racer[(CLUBS * CLUBMEMBERS)]`, `timeFactor = 4.0f`, `timeRefreshRate = 10`, `MotionB = 0`

</details>

<details><summary>38 instance/uninitialised fields</summary>

`ControlSet controlSet2`, `String activeControlFile`, `int gameMode`, `int timeout`, `int klampiPatch`, `int played`, `int saved`, `int carrerInProgress`, `String[] kismajom`, `VehicleType[] vehicleTypes`, `VehicleDescriptor[] carDescriptors_New`, `VehicleDescriptor[] carDescriptors_Used`, `float dealerVisitTimeStamp_New`, `float dealerVisitTimeStamp_Used`, `Hotkey hotkey0`, `Hotkey hotkey1`, `Hotkey hotkey2`, `Hotkey hotkey3`, `Hotkey hotkey4`, `GameState actualState`, `Garage garage`, `RaceSetup racesetup`, `GameRef[] controllers`, `Player player`, `Player player2`, `int day`, `float time`, `Text timeTxt`, `Text dayTxt`, `Thread timeRefresher`, `int cheats`, `ResourceRef[] ChassisRoot`, `ResourceRef[] MapRoot`, `Vector MapBox`, `Vector ChassisBox`, `Vector DefaultSkinBox`, `Vector PoliceManSkinBox`, `int[] ChassisResNoumber`

</details>

| Method | Decl / Reg | Typed sites (callers) | Name-only sites | Arguments seen | Semantics and evidence |
|---|---|---|---|---|---|
| `static int kismajomCheck(String[])` | native/R | 0 | 1 (1) |  | (H) `GameLogic.kismajomCheck(String[] codes)`: Garage calls it after Input.flushKeys() with the 5 cheat words stored Caesar-shifted by +1 ("mfunfspd" = letmeroc, "cfhgpsnpofz" = begformoney, "gbtuqspnpujpo" = fastpromotion, "jmjlfdifbujoh" = ilikecheating, "ujnfnbdijof" = timemachine). Returns the index of the code the player typed since the last flush, or -1. Effects (Garage.osdCommand CMD_TUNE): 0 ROC start, 1 +100000 money, 2 club promotion, 3 GameLogic.cheats=1, 4 day=180. |

Decl/Reg: `native` = flagged 0x40 in the shipped .class, `script` = non-native in the .class but registered natively in an exe, `undeclared` = registered in an exe but missing from the shipped .class; `R` = registered in `StreetLegal_Redline.exe`, `G` = registered in `Slrr_GI.exe` (patched build), `-` = no registration found. Typed sites = call sites that name this method with its descriptor (distinct caller classes in brackets); name-only = sites that only name the method, counted over all classes (README conventions).

<details><summary>29 script-implemented members</summary>

`initVehicleTypes()`, `MWM_Items_Scan()`, `getVehicleDescriptor(int)`, `getVehicleDescriptor(int, float)`, `getVehicleType(int)`, `preCacheGametypes(ResourceRef)`, `setTimerText(Text, Text)`, `setTimerText(Text)`, `setTime(float)`, `getTime()`, `spendTime(float)`, `mechTime(Part, int)`, `run()`, `canChallenge(Racer, Racer)`, `challenge(int, int, int, int, int)`, `findRacer(Racer)`, `changeActiveSection(GameState)`, `autoSave()`, `autoSaveQuiet()`, `save(String)`, `fileCheck(String)`, `load(String)`, `loadUnsavedData()`, `loadDefaults()`, `erase()`, `<init>()`, `finalize()`, `handleEvent(GameRef, int, int)`, `handleEvent(Hotkey)`

</details>


### java.game.Vehicle

`extends java.lang.GameType`. Compiled in `sl/Scripts/game/Vehicle.class`.

(H) A car as a whole: wraps `chassis` (the root Part) and is itself a GameType/GameRef of the native car object (cfg kind `car`). Constructed either from an existing chassis (`Vehicle(Part)`, `set(chassis)`) or by spawning a new one (`Vehicle(GameRef parent, int typeId, int colorIdx, float power, float optical, ...)` which calls `create(parent, new GameRef(typeId), "0,0,0,0,0,0", "Vehicle")`). Settings reach the native car only through EVENT_COMMAND strings: `transmission N`, `steerhelp F`, `asr F`, `abs F`, `difflock F`, `cruise N`, `damage_multiplier F`, `wakeup`.

Static fields: `SAVEFILEID_VHC = -2006563806`, `SAVEFILEVERSION_VHC = 5`, `SAVEFILEID_SKIN = -2108385177`, `SAVEFILEVERSION_SKIN = 0`, `TRANSMISSION_MANUAL = 0`, `TRANSMISSION_AUTO = 1`, `TRANSMISSION_SEMIAUTO = 5`, `TRANSMISSION_MANUALCLUTCH = 2`

Fields: `float maxTestTrackSpeedSq`, `float bestTestTrackAcc`, `float bestTestTrackAcc120`, `float bestTestTrackTime0`, `float bestTestTrackTime0_speedSq`, `float bestTestTrackTime1`, `float bestTestTrackTime2`, `float bestTestTrackTime2_speedSq`, `int races_won`, `int races_lost`, `float bestNightQM`, `int cruiseControl`, `Chassis chassis`, `Vector iteratorStack`

| Method | Decl / Reg | Typed sites (callers) | Name-only sites | Arguments seen | Semantics and evidence |
|---|---|---|---|---|---|
| `float getSpeedSquare()` | native/R | 14 (5) | 1 (1) | (no args) | (H) Squared speed of the car (Valocity treats < 0.25 as stopped; Track displays `sqrt(v)*0.08`). Units not confirmed. e.g. `TopGearTrack`: `player.car.getSpeedSquare()` |
| `int getHorn()` | native/R | 1 (1) | 0 | (no args) | (M) Non-zero while the horn is sounding (City reads it to trigger pedestrian/police reactions). e.g. `City`: `player.car.getHorn()` |
| `float hasCrime()` | native/R | 1 (1) | 0 | (no args) | (M) Float 'crime' level of the car (City multiplies by 1.1 for the police pursuit trigger). e.g. `City`: `player.car.hasCrime()` |


<details><summary>35 script-implemented members</summary>

`load(String, GameRef)`, `fileCheck(String)`, `fileCheck_Skin(String)`, `<init>(Part)`, `calcPoliceFine(float)`, `<init>(GameRef, int, int, float, float, float, float)`, `getInfo(int)`, `iteratePartsInit()`, `iterateParts()`, `wakeUp()`, `addStockParts(float, float, float)`, `setDefaultTransmission()`, `setTransmission(int)`, `setDefaultSteeringHelp()`, `setSteeringHelp(float)`, `setDefaultASR()`, `setASR(float)`, `setDefaultABS()`, `setABS(float)`, `setDifflock(float)`, `setCruiseControl(int)`, `getCruiseControl()`, `setDamageMultiplier(float)`, `toString()`, `canTakeSeat()`, `isDriveable()`, `isTestable()`, `isDriveable2()`, `getTotalPrice()`, `repair()`, `getPrestigeMultiplier()`, `getPrestige()`, `save(String)`, `loadSkin(String)`, `saveSkin(String)`

</details>


### java.game.Painter

`extends java.lang.GameType` implements java.game.GameState. Compiled in `sl/Scripts/game/Painter.class`.

(H) Paint/decal tool state machine (GameState + GameType). Calls the three natives with the clicked part and brush parameters; registers a per-frame `control(float)` callback to paint under the cursor.

<details><summary>39 static fields with initial values</summary>

`RID_PAINT_SPRAY = frontend.rpk:0x130r`, `RID_PAINT_PART = frontend.rpk:0x131r`, `RID_PAINT_DECAL = frontend.rpk:0x132r`, `RID_PAINT_LOAD = frontend.rpk:0x11r`, `RID_PAINT_SAVE = frontend.rpk:0x72r`, `RID_BACKGROUND = frontend.rpk:0xC0r`, `BACKGROUND_HEIGHT = 0.4f`, `PARTS_VP_TOP = 0.815f`, `PARTS_VP_LEFT = 0.226f`, `PARTS_VP_WIDTH = 0.696f`, `PARTS_VP_HEIGHT = 0.178f`, `CMD_SCROLL_UP = 0`, `CMD_SCROLL_DOWN = 1`, `CMD_SPRAY = 2`, `CMD_PART = 3`, `CMD_DECALS = 4`, `PAINTBRUSH = new java.util.resource.ResourceRef(misc\garage.rpk:0x102r)`, `SfxSPRAY = new java.util.resource.SfxRef(GameLogic.SFX_SPRAY)`, `SfxDECAL = new java.util.resource.SfxRef(GameLogic.SFX_DECAL)`, `SfxWARN = new java.util.resource.SfxRef(frontend.rpk:0x81r)`, `RID_button_decal = misc\garage.rpk:0x8Cr`, `CMD_DECALBUTTONS = 64`, `CMD_NONE = 100`, `CMD_BRUSHINC = 102`, `CMD_BRUSHDEC = 103`, `CMD_BRUSHRCW = 104`, `CMD_BRUSHRCCW = 105`, `CMD_BRUSHFLIP = 106`, `CMD_LOADSKIN = 107`, `CMD_SAVESKIN = 108`, `CMD_COPYSKIN = 109`, `CMD_C_COPY = 110`, `CMD_C_PAINT = 111`, `MODE_NONE = 0`, `MODE_PAINTCOLOR = 1`, `MODE_PAINTPART = 2`, `MODE_PAINTDECAL = 3`, `MODE_COPY = 4`, `PF_REDUCED_FUNCTIONALITY = 1`

</details>

<details><summary>39 instance/uninitialised fields</summary>

`ControlSetState css`, `PaintInventory paintCans`, `int overVehicle`, `int mode`, `int lastPaintMode`, `GameRef paintCursor`, `ResourceRef paintBrush`, `int brushColor`, `float decalRotation`, `float decalSize`, `int decalFlip`, `GameRef[] decalButtons`, `RenderRef[] decalButtonTypes`, `RenderRef[] decalButtonInstances`, `ResourceRef[] decalButtonTextures`, `int showDecals`, `int spraySfxID`, `int spraySfxOn`, `int handleControls`, `Osd osd`, `int copyGroup`, `int paintGroup`, `int colorsGroup`, `int decalsGroup`, `int brushGroup`, `int sprayGroup`, `int osdGroup`, `Text moneyTxt`, `Text infoline`, `Text invLineTxt`, `Player player`, `Gadget red0`, `Gadget red1`, `int flags`, `Rectangle copy_prev`, `int copy_texture`, `int copy_status`, `Button copy_button`, `Button paint_button`

</details>

Engine-read field candidates (the exact field name is pushed as a string constant by exe code; first code xref in `StreetLegal_Redline.exe` after the `@`; short names can be coincidental): `mode@0x461dda`, `osd@0x42e091`, `flags@0x487d06`

| Method | Decl / Reg | Typed sites (callers) | Name-only sites | Arguments seen | Semantics and evidence |
|---|---|---|---|---|---|
| `void doPaint(GameRef, int, int, int, float, float, int)` | native/R | 0 | 3 (1) |  | (H) `doPaint(GameRef targetPart, int colorARGB, int brushResId, int commit, float rotation, float size, int flip)`: decal/stroke at the cursor pick. `commit`=0 preview (the 'cursor' ghost is passed as target when previewing), 1 apply. Colors are ARGB ints with alpha (`paintColor \| 1711276032` = alpha 0x66). |
| `void paintPart(GameRef, int)` | native/R | 0 | 1 (1) |  | (H) `paintPart(GameRef part, int colorARGB)`: flood-paints a part (alpha forced to 0xFF with `\| -16777216`). |
| `void xPaint(GameRef)` | native/R | 0 | 1 (1) |  | (H) `xPaint(GameRef car)`: Painter calls it with player.car (undo/clear paint; exact effect unknown). |


Script members: `<init>(Player, Osd, Text, Text, int)`, `show()`, `hide()`, `changeMode(int)`, `beginPaint(int)`, `endPaint()`, `setDecalButtons()`, `clearDecalButtons()`, `handleEvent(GameRef, int, String)`, `control(float)`, `osdCommand(int)`


### java.game.Navigator

`extends java.lang.Object`. Compiled in `sl/Scripts/game/Navigator.class`.

(H) Mini-map/GPS. Script class with a Viewport and Camera; the only native is updateNavigator.

Static fields: `DEF_ZOOM = 4.5f`

<details><summary>25 instance/uninitialised fields</summary>

`Viewport vp`, `Dummy localroot`, `Camera cam`, `RenderRef[] tiles`, `RenderRef[] tiletypes`, `RenderRef route`, `ResourceRef[] meshes`, `GameRef button`, `RenderRef rmCamera`, `GameRef gmCamera`, `Marker dmCamera`, `Vector marker`, `Vector dynamarker`, `float vp_l`, `float vp_t`, `float vp_w`, `float vp_h`, `float zoom`, `float offsetX`, `float offsetZ`, `float top`, `float left`, `float size`, `int mode`, `int lda`

</details>

Engine-read field candidates (the exact field name is pushed as a string constant by exe code; first code xref in `StreetLegal_Redline.exe` after the `@`; short names can be coincidental): `cam@0x482d60`, `marker@0x48325e`, `dynamarker@0x4830cf`, `zoom@0x43add1`, `offsetX@0x482e0a`, `offsetZ@0x482df7`, `top@0x482e48`, `left@0x482e5d`, `size@0x482e76`, `mode@0x461dda`

| Method | Decl / Reg | Typed sites (callers) | Name-only sites | Arguments seen | Semantics and evidence |
|---|---|---|---|---|---|
| `void updateNavigator(GameRef, int)` | native/R | 1 (1) | 10 (2) | 0: p0; 1: mode | (H) `updateNavigator(GameRef target, int mode)`: redraws the map for the tracked car; the script overload updateNavigator(target) passes the current `mode`. e.g. `Navigator`: `updateNavigator(p0, mode)` |


Script members: `<init>(float, float, float, int, int, int, int, int, int)`, `finalize()`, `setInteractive()`, `show()`, `ViewMap()`, `hide()`, `changeMode(int)`, `updateNavigator(GameRef)`, `changeSize(float, float, float, float)`, `changeZoom(float)`, `addMarker(Racer)`, `addMarker(RenderRef, GameRef)`, `addMarker(RenderRef, Vector3, int)`, `remMarker(Marker)`


### java.game.Init

`extends java.lang.GameType`. Compiled in `sl/Scripts/game/Init.class`.

(H) The first game script class (sl.rpk:0x40 `script java.game.Init`). `<init>(int)`: Sound.init(); Frontend.init(); Frontend.loadingScreen.show(); Input.initControllers(this, "save/controls/active_control_set"); Input.enablePointingDevice(); gameLogic = new GameLogic(). finalize() reverses it.

Fields: `GameLogic gameLogic`

Script members: `<init>(int)`, `finalize()`


## Catalogue of java.game script classes

| Class | Extends | Methods | Fields | Natives | Role |
|---|---|---|---|---|---|
| `java.game.Animater` | `java.lang.Object` | 0 | 0 | 0 | One-line empty Runnable stub. |
| `java.game.Bot` | `java.game.Racer` | 32 | 20 | 0 | AI opponent: creates a Vehicle from a VehicleDescriptor and drives it through the `bot` native kind (sl.rpk:0x6E botbrain); controlled with brain command strings such as "AI_BeginRace 1.0", "osd N". |
| `java.game.BuyCatalogItemDialog` | `java.render.osd.dialog.YesNoDialog` | 2 | 0 | 0 |  |
| `java.game.CarInfo` | `java.lang.GameType` | 12 | 33 | 0 | Car information screens (engine, financial, records) reading WheelRef/DynoData/Chassis data. |
| `java.game.CarLot` | `java.lang.GameType` | 28 | 22 | 0 | Used-car lot screen (carlot.rpk map, 10 parking spots x 10 floors). |
| `java.game.CarLotData` | `java.lang.Object` | 1 | 4 | 0 | Camera/dummy car positions for the used-car lot (10 spots). |
| `java.game.CarMarket` | `java.game.Scene` | 19 | 30 | 0 | Car dealer screen (dealer.rpk/dealer2.rpk maps). |
| `java.game.CarSelect` | `java.game.Scene` | 13 | 102 | 0 | Scene state for picking a car (like CarMarket without trading). |
| `java.game.Car_Dealer_Options` | `java.render.osd.dialog.Dialog` | 2 | 8 | 0 |  |
| `java.game.CarrerFileReqDialog` | `java.render.osd.dialog.FileRequesterDialog` | 2 | 0 | 0 |  |
| `java.game.Catalog` | `java.lang.GameType` | 22 | 303 | 0 | Parts catalogue / shop (3,160 decompiled lines): enumerates `parts` gametypes with isScripted("java.game.parts.Part"), creates temp rims/tyres with create(..., "temp rim"). |
| `java.game.CatalogInventory` | `java.game.VisualInventory` | 9 | 3 | 0 |  |
| `java.game.CatalogInventoryPanel` | `java.game.InventoryPanel` | 3 | 5 | 0 |  |
| `java.game.CaynonTrack` | `java.game.Track` | 4 | 6 | 0 |  |
| `java.game.City` | `java.game.Track` | 32 | 142 | 0 | The open-world map (Track subclass, 2,489 decompiled lines): traffic, police, races, night races. |
| `java.game.ClubInfo` | `java.lang.GameType` | 5 | 11 | 0 | Club standings screen. |
| `java.game.ControlsFileReqDialog` | `java.render.osd.dialog.FileRequesterDialog` | 2 | 0 | 0 |  |
| `java.game.DMarker` | `java.game.Marker` | 1 | 1 | 0 | Destination marker (Marker subclass). |
| `java.game.DeadIslandTrack` | `java.game.Track` | 4 | 6 | 0 |  |
| `java.game.DealerData` | `java.lang.Object` | 0 | 2 | 0 | Car placement arrays of a dealer map (subclasses DealerNewData, DealerSHData). |
| `java.game.DealerNewData` | `java.game.DealerData` | 1 | 0 | 0 |  |
| `java.game.DealerSHData` | `java.game.DealerData` | 1 | 0 | 0 |  |
| `java.game.DealerTrack` | `java.game.Track` | 6 | 19 | 0 |  |
| `java.game.Decal` | `java.util.resource.ResourceRef` | 2 | 1 | 0 | ResourceRef subclass for decal textures. |
| `java.game.Drag_Track` | `java.game.Track` | 4 | 5 | 0 |  |
| `java.game.DriverSelect` | `java.game.Scene` | 11 | 70 | 0 | Scene state for choosing the driver skin (uses ResourceRef.makeTexture/changeResource on the humans skin textures). |
| `java.game.FineDialog` | `java.render.osd.dialog.CsDialog` | 1 | 0 | 0 |  |
| `java.game.GE_F_mask` | `java.game.parts.Part` | 1 | 0 | 0 | Mod class: Miran's Destruction Tools front mask (DestructionTools.rpk gametype with `script DestructionTools\Scripts\GE_F_Mask.class`), a plain Part. |
| `java.game.GE_Grill` | `java.game.parts.Part` | 1 | 0 | 0 |  |
| `java.game.GE_L_Taillights` | `java.game.parts.bodypart.Taillights` | 1 | 0 | 0 |  |
| `java.game.GE_R_Taillights` | `java.game.parts.bodypart.Taillights` | 1 | 0 | 0 |  |
| `java.game.GE_hood` | `java.game.parts.bodypart.Hood` | 1 | 0 | 0 |  |
| `java.game.GameLogic` | `java.lang.GameType` | 30 | 113 | 1 | The game controller singleton. Created by java.game.Init's constructor (`gameLogic = new GameLogic()`), runs from there on: `createNativeInstance()` in its own constructor, then rpkScan/openLib of the content archives, VehicleType/Chassis/Map scans, controller join loop, `Hotkey`s for music/screenshot, a clock Thread, and finally `changeActiveSection(new SplashScreen(frontend.rpk:0x5))`. It is a GameType AND a Runnable whose run() is STATIC. |
| `java.game.GameState` | `None` | 0 | 0 | 0 | Interface with enter(GameState previous) / exit(GameState next); implemented by every screen: MainMenu, Garage, CarMarket, CarLot, Catalog, Track (and its map subclasses), TrackSelect, RaceSetup, CarInfo, ClubInfo, RocInfo, SplashScreen, Painter. GameLogic.changeActiveSection(state) calls exit(new) on the old and enter(old) on the new, and System.exit() when the new state is null. |
| `java.game.Garage` | `java.game.Scene` | 19 | 124 | 0 | Garage screen (1,571 lines): install/remove parts by drag and drop using EVENT_CURSOR events, `install_OK`, `install`/`remove` commands, cheats via kismajomCheck. |
| `java.game.IngameMenu` | `java.game.OptionsDialog` | 4 | 16 | 0 | In-race pause menu (extends OptionsDialog). |
| `java.game.Init` | `java.lang.GameType` | 2 | 1 | 0 | The first game script class (sl.rpk:0x40 `script java.game.Init`). `<init>(int)`: Sound.init(); Frontend.init(); Frontend.loadingScreen.show(); Input.initControllers(this, "save/controls/active_control_set"); Input.enablePointingDevice(); gameLogic = new GameLogic(). finalize() reverses it. |
| `java.game.Inventory` | `java.lang.GameType` | 21 | 2 | 0 | Player part inventory (VisualInventory/CatalogInventory/PaintInventory subclasses show it as panels). |
| `java.game.InventoryItem` | `java.lang.Object` | 2 | 2 | 0 | Base of inventory entries (Part, Folder, Paint) with getName/getPrice/getDescription/installToCar/copyToInventory. |
| `java.game.InventoryItem_Folder` | `java.game.InventoryItem` | 15 | 3 | 0 |  |
| `java.game.InventoryItem_Paint` | `java.game.InventoryItem` | 5 | 2 | 0 |  |
| `java.game.InventoryItem_Part` | `java.game.InventoryItem` | 15 | 3 | 0 |  |
| `java.game.InventoryPanel` | `java.lang.Object` | 10 | 17 | 0 | OSD panel showing inventory items; CatalogInventoryPanel/VisualInventory build on it. |
| `java.game.LoadingText` | `java.lang.Thread` | 3 | 2 | 0 | Thread subclass animating the 'loading' text. |
| `java.game.MP1_Drift_Track` | `java.game.Track` | 4 | 5 | 0 |  |
| `java.game.MainMenu` | `java.lang.Object` | 3 | 1 | 0 |  |
| `java.game.MainMenuDialog` | `java.game.OptionsDialog` | 9 | 92 | 0 | Main menu dialog with the intro videos and the About/Options entries (extends OptionsDialog). |
| `java.game.Marker` | `java.lang.Object` | 1 | 12 | 0 | Navigator marker render instances (frontend.rpk resources). |
| `java.game.Mechanic` | `java.lang.GameType` | 9 | 46 | 0 | Part-inventory/mechanic UI used by the garage; sends commands to the car ("suspend", "filter N M", "flap_toggle"). |
| `java.game.Navigator` | `java.lang.Object` | 15 | 26 | 1 | Mini-map/GPS. Script class with a Viewport and Camera; the only native is updateNavigator. |
| `java.game.Nevada_Track` | `java.game.Track` | 7 | 13 | 0 |  |
| `java.game.NightracesData` | `java.lang.Object` | 1 | 4 | 0 | Static start/finish positions and orientations of the night races (18 each). |
| `java.game.NrOpponentDialog` | `java.render.osd.dialog.Dialog` | 2 | 1 | 0 |  |
| `java.game.OptionsDialog` | `java.render.osd.dialog.Dialog` | 13 | 145 | 0 | Options screen (video, sound, controls, game) built from Osd gadgets; calls System.getConfigOptions, System.setMeasure, GfxEngine.changeVideoMode, System.compileAll. |
| `java.game.PaintCan` | `java.lang.Object` | 1 | 2 | 0 | Paint can item: colour + capacity. |
| `java.game.PaintInventory` | `java.game.VisualInventory` | 5 | 2 | 0 |  |
| `java.game.Painter` | `java.lang.GameType` | 14 | 78 | 3 | Paint/decal tool state machine (GameState + GameType). Calls the three natives with the clicked part and brush parameters; registers a per-frame `control(float)` callback to paint under the cursor. |
| `java.game.ParkingCar` | `java.lang.Object` | 3 | 3 | 0 | Parked-car record (position, orientation) used by CarLot/City. |
| `java.game.Physic_Lab_Options` | `java.render.osd.dialog.Dialog` | 2 | 9 | 0 |  |
| `java.game.Physic_Lab_Track` | `java.game.Track` | 6 | 8 | 0 |  |
| `java.game.Player` | `java.game.Racer` | 7 | 15 | 0 | The human racer (controller, money, club, owned car, decals). |
| `java.game.PlayerSetupDialog` | `java.render.osd.dialog.Dialog` | 4 | 9 | 0 |  |
| `java.game.Pneumatic_SA` | `java.game.parts.rgearpart.Pneumatic_ShockAbsorber` | 1 | 0 | 0 | Mod class: pneumatic shock absorber (MW_Mod.rpk:0x83), extends Pneumatic_ShockAbsorber; Chassis.Hsuspension drives it. |
| `java.game.PoliceScout` | `java.lang.Object` | 0 | 4 | 0 | Record of a police bot chasing the player. |
| `java.game.ROCEntryDialog` | `java.render.osd.dialog.Dialog` | 4 | 9 | 0 |  |
| `java.game.ROCTrack` | `java.game.Track` | 17 | 41 | 0 |  |
| `java.game.RaceDialog` | `java.render.osd.dialog.CsDialog` | 2 | 0 | 0 |  |
| `java.game.RaceSetup` | `java.lang.GameType` | 9 | 27 | 0 | Race configuration screen (prize, route preview with RenderRef.plotRoute). |
| `java.game.Racer` | `java.lang.GameType` | 8 | 16 | 0 | Base of Player and Bot: name, car (Vehicle), prestige, club. Base of Player and Bot: name, car (Vehicle), prestige, club; has a save/load pair. |
| `java.game.RacerTalkDialog` | `java.render.osd.dialog.Dialog` | 4 | 19 | 0 |  |
| `java.game.RepairAllDialog` | `java.render.osd.dialog.NoYesDialog` | 1 | 0 | 0 |  |
| `java.game.RepairDialog` | `java.render.osd.dialog.NoYesDialog` | 1 | 0 | 0 |  |
| `java.game.Roc` | `java.lang.Object` | 10 | 12 | 0 | Race of Champions tournament (16 competitors, 4 rounds). |
| `java.game.RocInfo` | `java.lang.GameType` | 6 | 8 | 0 | ROC tournament table screen. |
| `java.game.Round` | `java.lang.Object` | 11 | 11 | 0 | One round of the ROC tournament. |
| `java.game.SMarker` | `java.game.Marker` | 1 | 1 | 0 | Start marker (Marker subclass). |
| `java.game.Scene` | `java.lang.GameType` | 6 | 13 | 0 | GameType base for 3D screens: sky dome, sun light (RenderRef.setLight/setFlare), fog (GroundRef.setFog) chosen from the game clock via time2Config(float). |
| `java.game.ScrapDialog` | `java.render.osd.dialog.YesNoDialog` | 1 | 0 | 0 |  |
| `java.game.SplashScreen` | `java.lang.GameType` | 5 | 2 | 0 |  |
| `java.game.TestTrack` | `java.game.Track` | 16 | 131 | 0 |  |
| `java.game.TopGearTrack` | `java.game.Track` | 9 | 20 | 0 |  |
| `java.game.Track` | `java.game.Scene` | 32 | 106 | 0 | Scene + GameState for a drivable map: cameras, triggers, HUD. Each addon map script (DealerTrack, Nevada_Track, Wichur_Drift_Track, TopGearTrack, Physic_Lab_Track...) extends it; maps are found as children of MW_Mod:0x0ABC whose entry says `script ...Track.class`. |
| `java.game.TrackSelect` | `java.game.Scene` | 10 | 56 | 0 | Map/track chooser screen with video preview (GfxEngine.openVideo). |
| `java.game.TrafficTracker` | `java.lang.Object` | 0 | 5 | 0 | Record linking a traffic car, its traffic id and its marker (City). |
| `java.game.Trigger` | `java.lang.Object` | 4 | 4 | 0 | Script wrapper around a native trigger (system.rpk:0x34 `native trigger`) plus a Navigator marker. |
| `java.game.UR_Dialog` | `java.render.osd.dialog.Dialog` | 3 | 18 | 0 |  |
| `java.game.ValoCity_Track` | `java.game.Track` | 1 | 5 | 0 |  |
| `java.game.Valocity` | `java.game.City` | 8 | 0 | 0 |  |
| `java.game.Vehicle` | `java.lang.GameType` | 38 | 22 | 3 | A car as a whole: wraps `chassis` (the root Part) and is itself a GameType/GameRef of the native car object (cfg kind `car`). Constructed either from an existing chassis (`Vehicle(Part)`, `set(chassis)`) or by spawning a new one (`Vehicle(GameRef parent, int typeId, int colorIdx, float power, float optical, ...)` which calls `create(parent, new GameRef(typeId), "0,0,0,0,0,0", "Vehicle")`). Settings reach the native car only through EVENT_COMMAND strings: `transmission N`, `steerhelp F`, `asr F`, `abs F`, `difflock F`, `cruise N`, `damage_multiplier F`, `wakeup`. |
| `java.game.VehicleDescriptor` | `java.lang.Object` | 4 | 11 | 0 | A concrete car to spawn: model id, colour index, power, optical, tear, wear. |
| `java.game.VehicleFileReqDialog` | `java.render.osd.dialog.FileRequesterDialog` | 2 | 0 | 0 |  |
| `java.game.VehicleModel` | `java.lang.Object` | 2 | 18 | 0 | One sellable model: cars.rpk id, set mask, prevalence, power/optical ranges, prestige, name. |
| `java.game.VehicleSkinFileReqDialog` | `java.render.osd.dialog.FileRequesterDialog` | 2 | 0 | 0 |  |
| `java.game.VehicleType` | `java.lang.GameType` | 4 | 58 | 0 | Per-make catalogue record; the per-make *_VT scripts (Baiern_VT...) are gametype entries below cars:0x1000 that fill `vtdarr` with VehicleModel records (resource id, VS_* set mask, prevalence, min/max power and optical tuning, prestige, qm figures, name). |
| `java.game.VisualInventory` | `java.game.Inventory` | 21 | 8 | 0 |  |
| `java.game.Wichur_Damage_Track` | `java.game.Track` | 4 | 5 | 0 |  |
| `java.game.Wichur_Drift_Track` | `java.game.Track` | 4 | 5 | 0 |  |


## Car model scripts (java.game.cars)

439 classes in `cars\racers\<Make>_data\scripts\` and `cars\fake_racers\` (measured by superclass): 9 `<Make>_models extends Chassis`
(physics constants and the model table), 24 `<Make>_<Model> extends <Make>_models` (constructor + `addStockParts(Descriptor)`), 9 `<Make>_VT extends
VehicleType` (sales records) and 397 part classes packaged with the car (364 `bodypart`, 20 `enginepart`, 11 direct `Part`
subclasses, 1 `airfueldeliverysystem`, 1 `rgearpart`: windows, wings, lights, exhausts, steering wheels, radiators) whose gametype entries sit in
the make's `.rpk`.
