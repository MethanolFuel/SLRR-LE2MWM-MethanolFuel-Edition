# java.sound

Script root: `system\Scripts\sound\Sound.class` (one class). 10 natives registered, of which 6 are declared native in the
shipped class and 4 are registered but undeclared (`getMode`, `setMode`, `has3DHardware`, `hasMixHardware`). The sound
surface used by the content is wider than this package, because sounds are `SfxRef`s and car sounds are tables owned by
the chassis:

| Where | Contract |
|---|---|
| `java.sound.Sound` | music playlists, three volume channels |
| `java.util.resource.SfxRef` (java.util.md) | `nplay(pos, f, f, f, flags, ownerId)` -> play id, `stop(playId)`, `cache()/precache()`; `SFX_LOOP`=1, `SFX_NOAUTOSTOP`=0x80000000 |
| `Chassis.getSfxTable(0..2)` -> `SfxTable.addItem(sound, rpm, rpmMin, rpmMax, volMin, volMax)` (java.game.parts.md) | rpm-driven engine / exhaust loops (552 `addItem` sites in the engine part scripts) |
| `Chassis.setHornSFX(sfx, pitch, index)`, `setNitroSFX`, fields `SFX_trans_fwd/rev`, `SFX_ignition`, `rpm_trans_fwd/rev`, `sfx_starter_rpm` | per-car one-shots and gear whine, read by the engine from the Chassis script fields |
| `Config.Sound_3D_HW`, `Sound_Mix_HW`, `mem_sound_max/min` | hardware capability and memory budget read by the exe at start-up (xrefs 0x58c8ce.., 0x60ccc0) |

Observed use of the 3D parameter `Vector3 pos`: `null` plays a non-positional (UI) sound; sounds attached to an object pass the
object's id as the last argument (Painter: `SfxSPRAY.play(pickedPos, 0, 1, 1, SFX_LOOP|SFX_NOAUTOSTOP, cursor.id())`).
Looping sounds are stopped by id, one-shots stop themselves unless `SFX_NOAUTOSTOP` is set.

### java.sound.Sound

`extends java.lang.Object`. Compiled in `system/Scripts/sound/Sound.class`.

(H) Static facade. Script statics: init() (creates a debug Viewport/Camera when enableDebugDump succeeds), increaseVolume/decreaseVolume (clamp 0..1 and call setVolume). Channels CHANNEL_EFFECTS=0, MUSIC=1, ENGINE=2; music sets NONE=-1, GARAGE=0, DRIVING=1, RACE=2, MENU=3. Config saves the three volumes in save/game/options.

Static fields: `MUSIC_SET_NONE = -1`, `MUSIC_SET_GARAGE = 0`, `MUSIC_SET_DRIVING = 1`, `MUSIC_SET_RACE = 2`, `MUSIC_SET_MENU = 3`, `CHANNEL_EFFECTS = 0`, `CHANNEL_MUSIC = 1`, `CHANNEL_ENGINE = 2`

Fields: `ResourceRef debugDisplay`, `Viewport vp`, `Camera camera`

Engine-read field candidates (the exact field name is pushed as a string constant by exe code; first code xref in `StreetLegal_Redline.exe` after the `@`; short names can be coincidental): `camera@0x42e1ae`

| Method | Decl / Reg | Typed sites (callers) | Name-only sites | Arguments seen | Semantics and evidence |
|---|---|---|---|---|---|
| `static void changeMusicSet(int)` | native/R | 0 | 13 (6) |  | (H) `changeMusicSet(MUSIC_SET_*)`: selects the playlist category (menu, garage, driving, race). |
| `static void nextTrack()` | native/R | 1 (1) | 0 | (no args) | (H) Hotkey AXIS_MUSIC_SELECT_NEXT (GameLogic.handleEvent(Hotkey)). e.g. `GameLogic`: `Sound.nextTrack()` |
| `static void prevTrack()` | native/R | 1 (1) | 0 | (no args) | (H) Hotkey AXIS_MUSIC_SELECT_PREV. e.g. `GameLogic`: `Sound.prevTrack()` |
| `static void setVolume(int, float)` | native/R | 2 (1) | 9 (2) | 0: p0 x2; 1: v2 x2 | (H) `setVolume(int channel, float 0..1)`. e.g. `Sound`: `setVolume(p0, v2)` |
| `static float getVolume(int)` | native/R | 2 (1) | 6 (2) | 0: p0 x2 | (H) `getVolume(int channel)`. e.g. `Sound`: `getVolume(p0)` |
| `static int enableDebugDump(ResourceRef)` | native/R | 1 (1) | 0 | 0: v0 | (L) `enableDebugDump(ResourceRef dummy)` returns non-zero if the engine can draw the sound debug display; Sound.init then builds a Viewport(127,..) and Camera. Debug only. e.g. `Sound`: `Sound.enableDebugDump(v0)` |
| `int getMode()` | undeclared/R | 0 | 0 |  | (L) Registered, not declared in the shipped class; Config.Sound_3D_HW / Sound_Mix_HW suggest hardware mode queries. |
| `int has3DHardware()` | undeclared/R | 0 | 0 |  | (L) Registered, not declared. |
| `int hasMixHardware()` | undeclared/R | 0 | 0 |  | (L) Registered, not declared. |
| `int setMode(int)` | undeclared/R | 0 | 0 |  | (L) Registered, not declared. |

Decl/Reg: `native` = flagged 0x40 in the shipped .class, `script` = non-native in the .class but registered natively in an exe, `undeclared` = registered in an exe but missing from the shipped .class; `R` = registered in `StreetLegal_Redline.exe`, `G` = registered in `Slrr_GI.exe` (patched build), `-` = no registration found. Typed sites = call sites that name this method with its descriptor (distinct caller classes in brackets); name-only = sites that only name the method, counted over all classes (README conventions).

Script members: `increaseVolume(int, float)`, `decreaseVolume(int, float)`, `init()`

