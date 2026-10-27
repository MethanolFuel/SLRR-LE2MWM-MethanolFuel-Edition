# Gaps wave: career race flow (`src/Slrr.Career`)

How the player gets into a race and back, driven headlessly through the shipped scripts on the `Slrr.Race` host:
garage menus, entry rules, race start, HUD and script events, finish, results, payout, garage, save; plus the
collision/damage path from the physics impacts to the scripts and the garage repair screen. Labels: **MEASURED** =
read from StreetLegal_Redline.exe / the shipped classes and data, or observed by running our code on them;
**GUESSED** = chosen so the scripts work. Nothing is compared with the original game; no parity is claimed.

```
dotnet test src/Slrr.Career/Slrr.Career.sln -warnaserror      # 6 unit + 5 corpus tests (corpus pass vacuously without data)
slrr career roc|day|night|damage|all [--root .] [--roc-entry cheat|test|none] [--shipped-roc-cars] [--promotions n]
```

Every run prints the transcript (steps, every screen reached, `MILESTONE`, `BLOCKED`, `STAND-IN`, `TEST-ONLY`, `CHEAT`
lines), the milestones, the first unmet dependency, the numbers, thread errors and missing natives.

## Files

| File | Contents |
|---|---|
| `CareerSession.cs` | `RaceHost` + career natives + collision bridge; transcript; menu helpers (`Click`, `Dismiss`, `Call`); career views (money, prestige, club, rank, clock); `CareerTestState` (TEST-ONLY setters); `StandInDriver` (player-side stand-in: place the car, pure-pursuit through the input layer's virtual axes) |
| `CareerNatives.cs` | `GameLogic.kismajomCheck` (the shipped cheat-code check) and its 16-character typed ring |
| `CollisionDamage.cs` | `CollisionDamageBridge`: physics impacts → `EVENT_COLLISION` + part tear |
| `CareerFlows.cs` | boot/buy prefix, ROC flow, save check, `FlowResult` |
| `CityFlows.cs` | Valo City entry, day race, night race, return to the garage |
| `DamageFlow.cs` | repair quote → crash in the city → repair quote |
| `CareerCommand.cs`, `tools/Slrr.Cli/CmdCareer.cs` | `career` CLI (`#if SLRR_CAREER`, compiled by the test project until wired) |

## How the player reaches each race (MEASURED from the decompiled classes)

* **Valo City** (`Valocity extends City`) is a TrackSelect entry: Garage "Choose Track" (needs `Vehicle.isDriveable`,
  spends 30 min, autosaves) → TrackSelect "Valo City" → TAKE IT. The MWM garage has no city button (its
  `CMD_HITTHESTREET` is "Change driver skin/model"). The car starts at the club's garage exit `posGarage[club]`.
  Day (04-22 h): ~80 % of the 60 racers get a `dummycar` registered as traffic (`addTrafficCar`); the city learns of
  them from `car_add id trafficId` posted to the map. Night: a drag race spot per day (`NightracesData`, index
  `random·5 + club·6`), PARTICIPATE / WATCH RACE.
* **Day race**: within 40 m the HUD shows the racer; within 20 m the AI may honk (it challenges when
  `canChallenge(bot, player)`); within 10 m the player's horn (`Vehicle.getHorn`) opens `RacerTalkDialog` (or accepts
  the AI's challenge) → `RaceSetup` (route from `getNearestCross(pos)` to `getNearestCross(start, 500..800)`, prize
  "Prestige only"/$100-$1000 club 0, $250-$2000 club 1, $500-$4000 club 2, the higher-prestige racer chooses) →
  START RACE → `City.startRace` (both cars `reset/stop/idle`, finish trigger radius 20, `AI_BeginRace`) → timers 9-12
  "3","2","1","GO!" → `startRace2` (`start`, `AI_race`) → `event_handlerRaceFinish` (time warp 0.1, RaceDialog
  DAY_WIN/LOOSE, money ± prize, races won/lost, `GameLogic.challenge` prestige/ranking) → timer 14 →
  `loadingScreen.userWait` → back to driving.
* **Night race**: start trigger radius 13 → PARTICIPATE → `NrOpponentDialog` (up to 3 club neighbours, prize
  $1000/$2000/$4000 × (rank/20 + 1) or pink slips from club 1 on) → RaceDialog → NR_3..NR_START every timer tick →
  `AI_NightRace` → finish trigger radius 8 at `pF` → the slower side gets 1000 s 10 s after the first → RaceDialog →
  money ± prize, `autoSaveQuiet`.
* **Race of Champions**: Garage "Go to the Race Of Champions" → `ROCEntryDialog` with 4 requirements: money ≥ $100000
  (bit 1), rank in the last 5 of all 60 (bit 2), car prestige ≥ 7.5 (shown as 750, bit 4), parts-bin weight (bit 8,
  always met: the uninitialised local is 0). ENTER only with all four (or the cheat button) → Garage in ROC mode →
  "Go on the next race" → `ROCTrack` (map `maps\ROC_track.rpk`, opponent `Bot.createCar(map, roc.getCurrentCarName())`
  = a saved car file, `AI_params`, `AI_spline`) → RaceDialog "Run n of 3" (10 s) → countdown → start trigger
  (5.4, 0, -6) r 30 → finish trigger (-25, 0.8, -57) r 8 → RaceDialog → garage. 4 rounds, 2 won runs advance; the
  **$100000 fee is charged only when the ROC ends** (final won or a round lost; `finishRocRace`), winning the final
  sets prestige 1.0 and gives the "Muscle" car.

### Earning the money/prestige legitimately (MEASURED rules, GUESSED effort)

A fresh career has $3999 and prestige 0.30 at rank 0 (last of club 0). Money comes only from races (day prize up to
$1000 in club 0, night $1000-$4000 × tier, pink slips) and selling cars; prestige from `Racer.calcPrestige` after each
`GameLogic.challenge`. Measured runs below: one day race won +$200 (+0.06 prestige, rank unchanged), one club-2 night
race won +$3000 (rank 40 → 42). Entering the ROC legitimately therefore needs on the order of 30+ night wins to reach
$100000 plus climbing to the top 5 of club 2 and a 750-prestige car — not scripted here. Instead:

* the game's **own cheat codes** (`GameLogic.kismajom`, shifted by +1, decoded MEASURED): `letmeroc` (enter the ROC),
  `begformoney` (+$100000), `fastpromotion` (next club), `ilikecheating` (cheats on: "[CHEAT] ALWAYS ENTER ROC"),
  `timemachine` (day 180). Typed, then "Fine tune specific parts" (Garage `CMD_TUNE` calls `kismajomCheck`).
  Transcripts mark them `CHEAT`.
* `CareerTestState` (TEST-ONLY): money, prestige, clock written into the script fields.

## Measured runs (headless, repository data, `career <flow>`)

| Flow | Result | Key numbers |
|---|---|---|
| ROC (`letmeroc`, 2 runs) | **completed**: entry dialog (reqs 8 = only bit 8), ROC garage, ROCTrack ×2, GO, finish, RaceDialog "won this run" / "qualified to the qualifying rounds (Run 3 is skipped)", garage, `roc.actRound` 0 → 1, save | player time 11.066 s both runs (59.4 m start→finish, stand-in driver, max 27 km/h); money $3999 unchanged (fee due at the end) |
| ROC with the shipped opponent cars | **stops** in `ROCTrack.enter` | `Vehicle.load("cars/racers/ROC_cars/roc_*")` misreads: this edition's `OilPan.load` reads a save version + 6 floats the files do not have → type 0x100 → `null.load()` |
| City day race (club 0) | **completed**: city, racer in traffic (stand-in), HUD, RacerTalkDialog, RaceSetup, START RACE, "3 2 1 GO!", finish, RaceDialog "The bet is: $200", payout, IngameMenu "TOW CAR TO GARAGE FOR $57", TrackSelect, BACK, garage, save | prize $200 (2 prize steps), money 3999 → 4199, races won 1, prestige 0.30 → 0.36 |
| City night race (club 2 via `fastpromotion` ×2) | **completed**: 00:30 via "Advance time 6 hours" ×2, city at night, spot trigger, PARTICIPATE, NrOpponentDialog ("18th - … - $3000 - … Baiern DevilSport"), NR countdown, finish, RaceDialog "has won … The prize is: $3000", garage (tow $16), save | script nrTime2 19.17 s over 401 m (stand-in driver, max 109 km/h); money 3999 → 6999; rank 40 → 42 |
| Damage | **completed**: repair quote, city, crash, `City.collision` = 1, garage, repair quote | impact dv 7.98 m/s at 26 km/h, 26 body parts lose 1.524 tear in total, chassis `getCondition()` 0.0020 → 0.0001, RepairAllDialog **$354 → $418** |

Cost (MEASURED, 4 shared vCPUs, other agents running): each corpus test 6-13 s wall for 45-115 s of game time
(5 corpus tests 47 s). Script thread errors 0 and missing natives 0 in all completed runs (after the fixes below). Bot names differ between
runs (the scripts call `Math.randomize()`; not investigated).

## First unmet dependency per race type

| Race type | First unmet dependency (owner) | Stand-in used to continue |
|---|---|---|
| City (all) | no ground at club 0's garage exit (-278.5, 9.8, 1033.0): `maps\city.rpk`'s collision covers z ≤ 328.5 only, the car falls (y -35 after 2 s) — **Slrr.Roads** (City ground) | car placed at the script's own free-ride start (-83.8, 12.9, -558.7) |
| City day | traffic: `addTrafficCar` is recorded only, no car is spawned or `car_add` posted — **Slrr.AI/Roads** | racer's `dummycar` placed 7 m ahead, `car_add` posted to the map |
| City day | `getNearestCross(start, 500..800)` returns the start (no road graph): finish = start — **Slrr.Roads** | `RaceSetup.pFinish` moved 120 m along the player's heading (TEST-ONLY write) |
| City day / night / ROC | opponents do not drive: `AI_race`, `AI_NightRace`, `AI_spline` not simulated — **Slrr.AI** | opponent stands still; the scripts' own timeout (night: 1000 s after 10 s) or the player's win ends the race |
| City night (club 0) | club 0's night spots (`NightracesData` 0..4, z 663..1448) have no ground — **Slrr.Roads** | `fastpromotion` ×2 (club 2 spots 12..16 mostly have ground); car placed 6 m behind the start |
| ROC | entry requirements (money, top-5 rank, car prestige) — career progress | `letmeroc` (or TEST-ONLY money/prestige + `ilikecheating`) |
| ROC | shipped opponent car files do not load (content vs. this edition's part scripts) | opponent = the player's car saved by `Vehicle.save` to `save/standin/` (TEST-ONLY `Roc.carSaveDir`/`carNames`) |
| all | driving the player from A to B | `StandInDriver` (pursuit through AXIS_TURN_LEFTRIGHT/THROTTLE/BRAKE overrides; body frame -Z forward) |

## Collision / damage (`CollisionDamageBridge`)

* **Event id and payload, MEASURED**: the exe posts code 1 (`EVENT_COLLISION`) with a contact record to both bodies
  of a contact and their root objects (0x426800 callers 0x4a36a4/0x4a36c1/0x4a371e/0x4a3739, gated by the impulse at
  record+0xA0 vs the body threshold at body+0x18 or flag 0x80); the event translator 0x425c60 (switch on code-1, case
  0 at 0x425d6b) turns it into the text `"%d"` (0x60c9c8) = instance id of the **other** body's root. For comparison
  (same function): EVENT_HIT (4) is `"%d %f"` from `"%f,%x"`, EVENT_TRIGGER_ON/OFF (32/64) `"%d"` of the other body,
  EVENT_TIME the int overload. No caller posts EVENT_DAMAGE (8): damage is native part state read by
  `getWear/getTear/getCondition/repairCost`.
* **Implemented**: per physics step, impacts (≥ 1 m/s, `ImpactEvent`) become `queueEvent(car, 0, 1, "<other id>")`,
  at most one per (car, other) pair per 0.1 s (GUESSED), other = the other car or the map instance (GUESSED, the exe
  would name the hit map object). Delivered by the resource layer to the car's script and every
  `addNotification(car, EVENT_COLLISION)` subscriber (City, Garage).
* **Damage, GUESSED model**: dv above 2 m/s removes `Config.external_damage (0.25) × damage_multiplier × (dv − 2) / 20`
  of tear from every body part (`java.game.parts.BodyPart` subclasses), half of it from the chassis; the part's
  `updateDamage()` callback runs. Visible through the scripts' own `getCondition()` / `repairCost()` and the
  Mechanic's RepairAllDialog (table above).

## Fixes outside `Slrr.Career` (minimal, each needed by a flow)

1. `Slrr.Engine.Resources/ResourceNatives.cs` — `setEventMask` **ORs** into the mask (MEASURED 0x4819f0 → 0x48d3d0
   `mask |= m`; clear is `&= ~m`). Replacing it killed City's timers (Track.enter sets EVENT_TIME, City.enter then
   `setEventMask(EVENT_COLLISION)`), so no racer, police or night race logic ever ran.
2. `Slrr.Vm/LangNatives.cs` — `String.token` returns the interned String (GUESSED): City/Bot compare tokens with
   literals by reference (`token(0) == "car_add"`); without it `car_add`/`car_rem` were never recognised.
   `Slrr.Engine.System/EngineSystem.cs` `Intern` now uses the VM's literal pool (one pool for literals, file strings
   and native strings).
3. `Slrr.Vm/Vm.cs` — overload selection accepts a number for a `String` parameter (score −50; the frame entry already
   converts): ROCTrack's countdown `setMessage(testCountDown--)` threw "unknown method" every second.
4. `Slrr.Engine.System/SdatFile.cs`, `EngineSystem.cs` + hooks in `Slrr.Game/GameHost.cs` — **SDAT resource-reference
   trailer, MEASURED** (0x485c60 write(ResourceRef), 0x485ea0 readResID, 0x485870 close, 0x485440 open; header byte 6 =
   2): ids are stored as `(file-local archive number << 16) | local id`; on close the file gets `int n, n × (seq, kind,
   len, data[len])` (run-time textures/meshes, side files `name.seq`), `int m, m × (seq, len, archive name)`,
   `int trailerSize`. Confirmed on the data: the "12 unexplained trailing bytes" of `save/controls/active_control_set`
   are the empty trailer (0, 0, 8); `cars/racers/ROC_cars/ROC_DS_1` lists 7 archives (`cars\racers\Baiern.rpk`,
   `parts\wheels.rpk`, …) and 14 saved instances. Readers map numbers back by name (find-or-register), saved run-time
   instances read as 0 (counted in `UnresolvedSavedInstances`), files without a trailer read as before. Writers emit the
   trailer when the hooks are set (GameHost sets them).
5. `Slrr.Race/RaceHost.cs` — `RaceHostOptions.ExtraModuleFactories` (registered after the command natives, before the
   render hooks).

## Interfaces for the other gaps agents

* `CollisionDamageBridge(GameHost, GamePhysicsWorld, Func<int> mapId, CollisionDamageOptions)`: needs only
  `PhysicsScene.Impact`/`Stepped`; a richer impact (hit map object id, part hit) can be added to `ImpactEvent` by
  PhysicsQA and used here.
* Traffic (Roads/AI): announce a traffic car with `Resources.QueueEvent(mapId, mapId, 16, $"car_add {carId} {trafficId}")`
  and `car_rem` likewise — City and the opponent logic then work as shown by the stand-in.
* AI: the scripts' race verbs reaching the brains are `AI_BeginRace f`, `AI_race x,y,z oppId`, `AI_NightRace`,
  `AI_params a b`, `AI_spline f file playerId` (ROC spline files `cars/racers/ROC_cars/*.spl`).
* App: `CareerNatives.Typed.Type(text)` must be fed from the window's character input for the cheat codes to work.

## Integration steps for the lead

1. `Slrr.sln`: add `src/Slrr.Career/Slrr.Career.csproj` and `tests/Slrr.Career.Tests/Slrr.Career.Tests.csproj`.
2. `tools/Slrr.Cli/Slrr.Cli.csproj`: `<ProjectReference Include="..\..\src\Slrr.Career\Slrr.Career.csproj" />` and add
   `SLRR_CAREER` to `DefineConstants`; `Commands.cs`: `"career" => CmdCareer.Run(args[1..], o, e),` and the usage line
   `career <roc|day|night|damage|all> [...]   career race flows (CmdCareer.Usage)`. Then drop the `<Compile Include>`
   of CmdCareer.cs from the test project (or keep it with the define).
3. Run `dotnet test tests/Slrr.Vm.Tests tests/Slrr.Engine.System.Tests tests/Slrr.Engine.Resources.Tests
   tests/Slrr.Game.Tests tests/Slrr.Race.Tests` after merging (fixes 1-4 touch them).

## Open questions

1. The exe's per-body collision threshold (body+0x18) and what it names as "other" for map objects.
2. The exe's damage model (deformation, `Config.deformation/external_damage/internal_damage`, slot damage strings).
3. Whether the original intern pool covers every native string (token, File.readString, getDeviceName).
4. How the original handles saved run-time instances (kind 5/7) in readResID (we read 0).
5. Whether the shipped ROC car files also fail on the original exe with this edition's scripts (they must, if the
   scripts and files are the ones shipped — reads are deterministic — but nobody ran it).
