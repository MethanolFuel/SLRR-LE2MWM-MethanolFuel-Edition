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

{{CLASS java.game.GameLogic}}

{{CLASS java.game.Vehicle}}

{{CLASS java.game.Painter}}

{{CLASS java.game.Navigator}}

{{CLASS java.game.Init}}

## Catalogue of java.game script classes

{{CLASSLIST java.game.}}

## Car model scripts (java.game.cars)

439 classes in `cars\racers\<Make>_data\scripts\` and `cars\fake_racers\` (measured by superclass): 9 `<Make>_models extends Chassis`
(physics constants and the model table), 24 `<Make>_<Model> extends <Make>_models` (constructor + `addStockParts(Descriptor)`), 9 `<Make>_VT extends
VehicleType` (sales records) and 397 part classes packaged with the car (364 `bodypart`, 20 `enginepart`, 11 direct `Part`
subclasses, 1 `airfueldeliverysystem`, 1 `rgearpart`: windows, wings, lights, exhausts, steering wheels, radiators) whose gametype entries sit in
the make's `.rpk`.
