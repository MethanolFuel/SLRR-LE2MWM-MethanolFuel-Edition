# java.util and java.util.resource

Script roots: `system\Scripts\util\*.class` (7 classes) and `system\Scripts\util\resource\*.class` (9 classes).
`java.util.resource` is the bridge between scripts and the engine's resource/instance tree; it carries 107 of the
360 registered natives.

## The reference classes

```
java.lang.Native            (int ptr)
  ResourceRef               any node of the resource tree (rpk entry or live instance)
    GameRef                 game-object instance (script + native twin)      -> java.lang.GameType extends it
      GroundRef             native kind `ground` (the map root)
    RenderRef               render instance (mesh object, light, camera, text, particle)
      Camera, ParticleSystem (+ java.render.Text owns a RenderRef)
    PhysicsRef              physics body instance
    SfxRef                  sound resource (play helpers are script)
    java.render.Viewport    viewport resource
  Animation                 (Native, not a ResourceRef) skeleton animation player
```

Facts (H):

* A ref object is a **handle**: `new ResourceRef(id)` runs `newNative()` then `set(id)`; the script GC calls
  `finalize()` -> `deleteNative()`. Handles are cheap value-like wrappers; two handles to the same node are different
  script objects, equality is by `id()`.
* `ResourceRef.type()` and the `RESTYPE_*`/`INSTANCE_*`/`RESOURCE_*` constants (0..22) equal the **TypeOfEntry** codes of
  `system.rpk` (`ins_game`=1, `res_mesh`=5, `res_texture`=7, `typ_game_object`=8, `typ_render_object`=14,
  `res_viewport`=18, `res_animation`=19...). Gametype entries have type 8 and `GameRef.create` rejects other types
  (`create: Wrong GameType! %s:0x%08X`, exe 0x53a62c).
* The tree: an entry's `superId` is its parent (docs/formats/rpak.md). `getFirstChild/getNextChild/getParent` walk it
  across archives; GameLogic enumerates `cars:0x1000` (vehicle types), `cars:0x0101` (chassis), `MW_Mod:0x0ABC` (maps),
  `humans:0x1` and `cars:0x4` (precache), `parts` (catalogue sets).
* World-tree flags: `GameRef.WORLDTREEROOT = 16`, `WORLDTREELEAF = 64`; `setParent` places an instance below another
  (cars below the map). Parts are created as siblings of the car (`Part.addPart` creates under
  `new GameRef(car).getParentID()`) and are then linked to their parent part by the `install` command, not by tree parenthood.
* Events: `GameRef.EVENT_*` bit values are COLLISION 1, DELETE 2, HIT 4, DAMAGE 8, COMMAND 16, TRIGGER_ON 32, TRIGGER_OFF 64,
  TIME 128, DESTROY 256, WATER 512, INFO 1024, BODYCOLLISION 8192, GNDCOLLISION 16384, NETEVENT 32768, CURSOR 65536,
  HOTKEY 1048576, ANY 0x0FFFFFFF, CUSTOMINFO 0x10000000, SAME 0, NONE 0. `GameType.EC_*` (cursor codes, 1..16) and `GII_*`
  (info keys, 0..30) are in the GameType constants (java.lang.md).

{{CLASS java.util.resource.ResourceRef}}

{{CLASS java.util.resource.GameRef}}

{{CLASS java.util.resource.RenderRef}}

{{CLASS java.util.resource.PhysicsRef}}

{{CLASS java.util.resource.GroundRef}}

{{CLASS java.util.resource.Animation}}

{{CLASS java.util.resource.ParticleSystem}}

{{CLASS java.util.resource.SfxRef}}

{{CLASS java.util.resource.Dummy}}

## Pure-script classes of java.util

{{CLASSLIST java.util.}}

### Config

`java.util.Config` (system.rpk:0x47, `script java.util.Config`) is both a GameType and the engine's settings record:
111 fields, mostly statics with defaults (video 800x600x32, camera tuning, damage multipliers, memory budgets,
controller dead zones...). The exe reads/writes them **by name**: the start-up function (0x58c6f0) reads
`acceptBuild`, `version`, `FILD`, `video_x/y/depth/windowed`, `shadows`, `resource_refreshrate`, `Sound_3D_HW`,
`Sound_Mix_HW` and `init` (see entry-points.md), and later code reads about 70 more (see the candidate list below).
`Config(int)` loads `save/game/options` (`loadConfig`), `finalize()` saves it (`saveConfig`, magic -19088744 =
0xFEDCBA98, version 12), and falls back to volumes 1.0/0.5/1.0 when no file exists.

{{CLASS java.util.Config}}
