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

{{CLASS java.sound.Sound}}
