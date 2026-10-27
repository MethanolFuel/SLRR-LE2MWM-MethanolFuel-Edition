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

### java.util.resource.ResourceRef

`extends java.lang.Native`. Compiled in `system/Scripts/util/resource/ResourceRef.class`.

(H) Handle to a node of the resource tree (the .rpk entries merged into one tree). `ResourceRef` is a Native: the constructor runs newNative() then set(id); finalize runs deleteNative(). Equality of refs is by id(); null/0 means 'no resource'.

Static fields: `RESTYPE_INVALID = 0`, `INSTANCE_GAME = 1`, `INSTANCE_PHYSICS = 2`, `INSTANCE_RENDER = 3`, `RESOURCE_FORCEFX = 4`, `RESOURCE_MESH = 5`, `RESOURCE_SOUND = 6`, `RESOURCE_TEXTURE = 7`, `RESTYPE_GAME = 8`, `RESTYPE_PHYSICS_BODY = 9`, `RESTYPE_PHYSICS_CONSTRAINT = 10`, `RESTYPE_PHYSICS_PARTICLE = 11`, `RESTYPE_RENDER_CAMERA = 12`, `RESTYPE_RENDER_LIGHT = 13`, `RESTYPE_RENDER_OBJECT = 14`, `INSTANCE_RENDER_SPRITE = 15`, `INSTANCE_RENDER_TEXT = 16`, `INSTANCE_RENDER_HORIZON = 17`, `RESOURCE_VIEWPORT = 18`, `RESOURCE_ANIMATION = 19`, `RESTYPE_RENDER_MICROPHONE = 20`, `RESOURCE_SOUNDMIXER = 21`, `RESTYPE_MAX = 22`

| Method | Decl / Reg | Typed sites (callers) | Name-only sites | Arguments seen | Semantics and evidence |
|---|---|---|---|---|---|
| `void newNative()` | native/R | 1 (1) | 0 | (no args) | (H) Called by the constructor (`<init>() { super(); newNative(); }`). Allocates the C++ handle in `ptr`. Always paired with deleteNative() in finalize(). e.g. `ResourceRef`: `newNative()` |
| `void deleteNative()` | native/R | 1 (1) | 0 | (no args) | (H) Called from finalize() (script GC). Releases the handle. e.g. `ResourceRef`: `deleteNative()` |
| `void load()` | native/R | 0 | 55 (51) |  | (M) Synchronous load of the resource (mesh/texture/sound) or, on a save-game string, (by-name sites `Vehicle.load(path,...)`, `GameLogic.load` are unrelated script statics). |
| `void unload()` | native/R | 4 (4) | 2 (2) | (no args) | (H) Releases the resource data (CarMarket/Garage unload the map ref on exit). e.g. `CarMarket`: `map.unload()` |
| `void cache()` | native/R | 9 (5) | 19 (4) | (no args) | (H) Preloads into the resource cache (GameLogic ctor caches SfxRefs and, via preCacheGametypes, every child of humans:0x1 and cars:0x4). e.g. `GameLogic`: `p0.cache()` |
| `void precache()` | native/R | 0 | 6 (3) |  | (M) Like cache but non-blocking/queued (Track precaches sounds). |
| `void destroy()` | native/R | 144 (35) | 14 (7) | (no args) | (H) Destroys the INSTANCE the ref points to (a GameRef to a car/part/map object, a RenderRef, a Viewport). 144 typed sites. After destroy the script object is garbage. e.g. `DealerTrack`: `stuff.destroy()` |
| `void set(int)` | native/R | 10 (10) | 0 | 0: 0 x9, p0 | (H) set(int id) / set(ResourceRef) rebinds the handle; `set(null)` or `set(0)` clears (release()). id is the global resource id; literals like `parts:0xAB5Cr` compile to constants of this id space. e.g. `Baiern_models`: `v1.set(0)` |
| `void set(ResourceRef)` | native/R | 3 (2) | 0 | 0: p0 x2, null | (H) set(int id) / set(ResourceRef) rebinds the handle; `set(null)` or `set(0)` clears (release()). id is the global resource id; literals like `parts:0xAB5Cr` compile to constants of this id space. e.g. `Vehicle`: `set(p0)` |
| `int type()` | native/R | 0 | 1 (1) |  | (M) Returns the entry type code. The RESTYPE_*/INSTANCE_*/RESOURCE_* constants equal the rpk TypeOfEntry values of system.rpk (INSTANCE_GAME=1 ins_game, RESOURCE_MESH=5, RESTYPE_RENDER_OBJECT=14, ...). |
| `int id()` | native/R | 228 (35) | 52 (20) | (no args) | (H) Returns the global resource/instance id (int). 228 typed sites; ids are passed in text commands (`"leave " + car.id()`, `"look " + id`), stored in save files via File.write(ResourceRef) and compared with RID_* constants. e.g. `DealerTrack`: `player.car.id()` |
| `int getParentID()` | native/R | 2 (1) | 0 | (no args) | (H) Id of the tree parent (Part.addPart uses it to place a new part next to the car: `new GameRef(car).getParentID()` = the map/world node). e.g. `Part`: `v6.getParentID()` |
| `ResourceRef getParent()` | native/R | 2 (1) | 1 (1) | (no args) | (M) Same as getParentID but returns a ResourceRef. e.g. `Part`: `getParent()` |
| `ResourceRef getFirstChild()` | native/R | 4 (3) | 0 | (no args) | (H) Iteration: `for (r = root.getFirstChild(); r; r = r.getNextChild())` (GameLogic.preCacheGametypes, Catalog). countChildNodes/getChildNodes are script wrappers built on it. Returns null at the end. e.g. `Catalog`: `parts.getFirstChild()` |
| `ResourceRef getNextChild()` | native/R | 4 (3) | 0 | (no args) | (H) See getFirstChild. e.g. `Catalog`: `parts.getNextChild()` |
| `ResourceRef getWTRoot()` | native/R | 0 | 0 |  | (L) World-tree root; no site. |
| `void makeTexture(ResourceRef, String)` | native/R | 0 | 4 (2) |  | (M) `makeTexture(ResourceRef dest, String path)`: creates a texture resource in `dest` from an image file path (DriverSelect uses it with 'humans/drivers/.../*.dds' skins and system.rpk:0x8 = res_texture). |
| `void makeSound(ResourceRef, String)` | native/R | 0 | 0 |  | (L) Same idea for sounds; no site. |
| `void scaleMesh(float, float, float)` | native/R | 7 (3) | 6 (2) | 0: 1.01f x2, 0.99f x2; 1: 1.01f x2, 0.99f x2; 2: 1.01f x2, 0.99f x2 | (M) `scaleMesh(sx, sy, sz)`: scales the mesh of a TEMP copy made with duplicate() (DecorativeBodyPart/Wing scale 1.01/0.99 for the 'inflated' catalogue ghost). e.g. `DecorativeBodyPart`: `temp_meshRes.scaleMesh(1.01f, 1.01f, 1.01f)` |
| `void duplicate(ResourceRef)` | native/R | 8 (4) | 9 (6) | 0: temp_meshRes x4, TempRender x2 | (M) Copies another resource into this (temp) ref; used before scaleMesh/changeResource. e.g. `DecorativeBodyPart`: `temp_meshRes.duplicate(temp_meshRes)` |

Decl/Reg: `native` = flagged 0x40 in the shipped .class, `script` = non-native in the .class but registered natively in an exe, `undeclared` = registered in an exe but missing from the shipped .class; `R` = registered in `StreetLegal_Redline.exe`, `G` = registered in `Slrr_GI.exe` (patched build), `-` = no registration found. Typed sites = call sites that name this method with its descriptor (distinct caller classes in brackets); name-only = sites that only name the method, counted over all classes (README conventions).

Script members: `<init>()`, `<init>(int)`, `<init>(ResourceRef)`, `finalize()`, `release()`, `countChildNodes()`, `getChildNodes()`


### java.util.resource.GameRef

`extends java.util.resource.ResourceRef`. Compiled in `system/Scripts/util/resource/GameRef.class`.

(H) Handle to a game-object INSTANCE (world tree node with script and/or native twin). Subclass of ResourceRef, base of GameType. Constants: EVENT_* bit flags, RID_* system ids, WORLDTREEROOT=16/WORLDTREELEAF=64 flags.

<details><summary>31 static fields with initial values</summary>

`WORLDTREEROOT = 16`, `WORLDTREELEAF = 64`, `RID_DUMMY = system.rpk:0x1Br`, `RID_FRONTEND = system.rpk:0x2Fr`, `RID_CAMERA = system.rpk:0x33r`, `RID_TRIGGER = system.rpk:0x34r`, `RID_CURSOR = frontend.rpk:0x2Ar`, `RID_BUTTON = frontend.rpk:0x24r`, `RID_INVITEM = frontend.rpk:0x2Br`, `EVENT_COLLISION = 1`, `EVENT_DELETE = 2`, `EVENT_HIT = 4`, `EVENT_DAMAGE = 8`, `EVENT_COMMAND = 16`, `EVENT_TRIGGER_ON = 32`, `EVENT_TRIGGER_OFF = 64`, `EVENT_TIME = 128`, `EVENT_DESTROY = 256`, `EVENT_WATER = 512`, `EVENT_INFO = 1024`, `EVENT_BODYCOLLISION = 8192`, `EVENT_GNDCOLLISION = 16384`, `EVENT_NETEVENT = 32768`, `EVENT_CURSOR = 65536`, `EVENT_HOTKEY = 1048576`, `EVENT_ANY = 268435455`, `EVENT_NONE = 0`, `EVENT_CUSTOMINFO = 268435456`, `EVENT_SAME = 0`, `TC_ACTIVE = 1`, `TC_PASSIVE = 2`

</details>

| Method | Decl / Reg | Typed sites (callers) | Name-only sites | Arguments seen | Semantics and evidence |
|---|---|---|---|---|---|
| `GameType create(GameRef, GameRef, String, String)` | native/R | 2 (2) | 17 (10) | 0: null, p0; 1: parts, p1; 2: null, p2; 3: "set_loader", p3 | (H) `parent.create(parent, typeRef, params, name)` is the central factory: instantiates the gametype `typeRef` (a gametype entry: script class + native cfg, see entry-points.md) under `parent` (null = no parent), places it with `params` = "x,y,z,yaw,pitch,roll" (comma separated, e.g. "0,0,0,0,0,0", "100,0,0,0,0,0" for loaded parts), `name` is a debug label ("chassis", "Vehicle", "loaded carpart", "VehicleType", "load Track script", "set_loader"). Returns the SCRIPT object (GameType subclass) or null; also rebinds `this` ref to the new instance. The new script object's `<init>(int id)` is called (1,569 classes define it). e.g. `Catalog`: `parts.create(null, parts, null, "set_loader")` |
| `void create_native(GameRef, GameRef, String, String)` | native/R | 0 | 1 (1) |  | (M) Variant that creates the engine object without a script constructor call (Valocity dummycar `create_native(map, typeRef, "0,-10000,0,0,0,0", "dummycar")`, parked far below the map). |
| `int getFlags()` | native/R | 0 | 0 |  | (L) No site. |
| `void setFlags(int)` | native/R | 4 (4) | 0 | 0: GameRef.WORLDTREEROOT x2, GameRef.WORLDTREELEAF | (M) WORLDTREEROOT (16) / WORLDTREELEAF (64) flags: Osd/Dummy/Group mark nodes as roots or leaves of the render world tree. e.g. `MouseCursor`: `setFlags(GameRef.WORLDTREEROOT)` |
| `void clearFlags(int)` | native/R | 1 (1) | 0 | 0: GameRef.WORLDTREELEAF | (M) See setFlags. e.g. `Group`: `clearFlags(GameRef.WORLDTREELEAF)` |
| `Ypr getOri()` | native/R | 23 (6) | 0 | (no args) | (H) Returns Ypr. 23 typed sites (camera/car orientation). e.g. `Nevada_Track`: `player.car.getOri()` |
| `Vector3 getPos()` | native/R | 71 (12) | 2 (2) | (no args) | (H) Returns Vector3 in world metres. 71 typed sites. e.g. `Nevada_Track`: `player.car.getPos()` |
| `Vector3 getVel()` | native/R | 2 (2) | 0 | (no args) | (H) Velocity Vector3 (Track.cameraTarget, City.player.car). e.g. `City`: `player.car.getVel()` |
| `void setPos(Vector3)` | native/R | 0 | 1 (1) |  | (L) Only a by-name site. |
| `void setMatrix(Vector3, Ypr)` | native/R | 30 (7) | 164 (21) | 0: null x7, v7 x5; 1: null x7, oriS x6 | (H) `setMatrix(Vector3 pos, Ypr ori)`; `null` means 'keep that part' (Vehicle ctor `setMatrix(null,null)`, ROCTrack passes pos with ori from startOri). 30 typed sites, 164 by-name (RenderRef/PhysicsRef share the name). e.g. `CarSelect`: `player.car.setMatrix(null, null)` |
| `void setParent(ResourceRef)` | native/R | 31 (9) | 2 (2) | 0: map x19, player x8 | (H) Reparents in the world tree. Typical: `car.setParent(map)` after creating a car (19 of 31 sites), `setParent(player)`. e.g. `CarLot`: `player.car.setParent(player)` |
| `void setState(Vector3, Ypr, Vector3, Vector3)` | native/R | 0 | 0 |  | (L) setState(pos, ori, vel, angvel); no site. |
| `int isEmpty()` | native/R | 0 | 0 |  | (L) No site. |
| `int isScripted(String)` | native/R | 3 (2) | 1 (1) | 0: "java.game.parts.Part", "java.game.parts.Set" | (H) `isScripted()` / `isScripted("java.game.parts.Part")`: true when the node has a script instance, optionally an instance of the named class. Catalog filters parts with it; MWM_Items_Scan keeps only scripted chassis nodes. e.g. `Catalog`: `parts.isScripted("java.game.parts.Part")` |
| `Object getScriptInstance()` | native/R | 17 (7) | 1 (1) | (no args) | (H) Returns the script object (Object, cast by caller) behind the ref. 17 typed sites (`(Part) ref.getScriptInstance()`), the engine-to-script back-pointer. e.g. `Part`: `p0.getScriptInstance()` |
| `int getInfo(int, int)` | native/R | 3 (3) | 26 (10) | 0: p0 x3; 1: 0 x3 | (H) `getInfo(int key[, int/String arg])`: generic info query on a game object; who answers which key (native object vs the script's getInfo override) is open (question 6). Keys seen: GII_TYPE x13 (resource id of the gametype), GII_OWNER x9, GII_CATEGORY x7 (GIR_CAT_* result), GII_BONE x6, GII_GETOUT_OK x3, GII_PART_CATEGORY, GII_INSTALL_OK, GII_COMPATIBLE (take a String like car id), GII_AXIS, GII_REMOVE_OK, GII_RENDER, GII_CAR_TRAFFICPTR, GII_SIZE, 52 (= GII_CAR_DRIVETYPE). e.g. `Part`: `getInfo(p0, 0)` |
| `int getInfo(int, String)` | native/R | 0 | 26 (10) |  | (H) `getInfo(int key[, int/String arg])`: generic info query on a game object; who answers which key (native object vs the script's getInfo override) is open (question 6). Keys seen: GII_TYPE x13 (resource id of the gametype), GII_OWNER x9, GII_CATEGORY x7 (GIR_CAT_* result), GII_BONE x6, GII_GETOUT_OK x3, GII_PART_CATEGORY, GII_INSTALL_OK, GII_COMPATIBLE (take a String like car id), GII_AXIS, GII_REMOVE_OK, GII_RENDER, GII_CAR_TRAFFICPTR, GII_SIZE, 52 (= GII_CAR_DRIVETYPE). |
| `void queueEvent(ResourceRef, int, String)` | native/R | 7 (4) | 55 (12) | 0: null x7; 1: GameRef.EVENT_COMMAND x6, EVENT_COMMAND; 2: "AI_params 50.0 50.35", "reset" | (H) `queueEvent(ResourceRef source, int eventCode, String params)`: posts an event to this object's queue; `command(s)` is `queueEvent(null, EVENT_COMMAND, s)`. Delivered later as `handleEvent(GameRef, EVENT_COMMAND, s)` to a scripted target or consumed by the native object (see entry-points.md command table). e.g. `TopGearTrack`: `raceBot.brain.queueEvent(null, GameRef.EVENT_COMMAND, "AI_params 50.0 50.35")` |
| `void setActiveCollision()` | native/R | 0 | 0 |  | (L) No site. |
| `float getDetail()` | undeclared/R | 0 | 0 |  | (L) Registered in the exe, not declared in the shipped GameRef.class. |


Script members: `<init>(GameRef)`, `<init>(GameRef, GameRef, String, String)`, `<init>(GameRef, int, String, String)`, `<init>(int)`, `<init>(int, int, String, String)`, `isScripted()`, `getInfo(int)`, `command(String)`


### java.util.resource.RenderRef

`extends java.util.resource.ResourceRef`. Compiled in `system/Scripts/util/resource/RenderRef.class`.

(H) Handle to a render INSTANCE (mesh object, light, camera, text, particle system). Ids are tree nodes below a parent RenderRef.

Static fields: `RID_MENUBACK = frontend.rpk:0x1Ar`, `RID_MENULIGHT = frontend.rpk:0x1r`

| Method | Decl / Reg | Typed sites (callers) | Name-only sites | Arguments seen | Semantics and evidence |
|---|---|---|---|---|---|
| `void create(ResourceRef, RenderRef, String)` | native/R | 1 (1) | 17 (10) | 0: p0; 1: p1; 2: p2 | (H) `create(ResourceRef res, RenderRef parent, String name)` (native). The script overload create(res, int flags, name) wraps it with a ResourceRef(flags)? (flags/parent id). Constructors `RenderRef(res, parent, name)`. e.g. `RenderRef`: `create(p0, p1, p2)` |
| `int getBoneId(String)` | native/R | 0 | 0 |  | (L) No site. |
| `void setMatrix(int, ResourceRef, Vector3, Ypr)` | native/R | 0 | 164 (21) |  | (H) `setMatrix(Vector3 pos, Ypr ori)` or with a bone/resource index first (`setMatrix(int, ResourceRef, Vector3, Ypr)`, DealerTrack). null = unchanged. Pori overload is script. |
| `void setMatrix(Vector3, Ypr)` | native/R | 22 (7) | 164 (21) | 0: pos x4, Light1Pos x3; 1: null x13, ori x4 | (H) `setMatrix(Vector3 pos, Ypr ori)` or with a bone/resource index first (`setMatrix(int, ResourceRef, Vector3, Ypr)`, DealerTrack). null = unchanged. Pori overload is script. e.g. `CarSelect`: `Cam.setMatrix(CamVector, CamYpr)` |
| `Vector3 getPos()` | native/- | 0 | 2 (2) |  | (L) Declared native, not registered; by-name sites are other classes. |
| `void changeResource(ResourceRef, ResourceRef)` | native/R | 3 (2) | 4 (3) | 0: OrginalTextureRes, v5; 1: CurrentTextureRes, p2 | (H) `changeResource(ResourceRef old, ResourceRef new)`: swaps a mesh/texture on a live instance (DriverSelect skin preview). e.g. `DriverSelect`: `TempRender.changeResource(OrginalTextureRes, CurrentTextureRes)` |
| `void setColor(int)` | native/R | 1 (1) | 1 (1) | 0: -65536 | (M) ARGB int (-65536 = 0xFFFF0000 red). e.g. `InventoryItem_Folder`: `canObject.setColor(-65536)` |
| `void setLight(int, int, int)` | native/R | 19 (5) | 0 | 0: 14443620 x3, 14538925 x2; 1: 6579300 x3, 4939375 x2; 2: 13158600 x3, 14538925 x2 | (H) `setLight(int c0, int c1, int c2)` 24-bit RGB ints on a sun/light instance (19 typed sites). Pattern: c0 == c2 and c1 is the dark value (night: (4612741, 463390, 4612741) where 463390 is also the night fog colour; day: (14538925, 4939375, 14538925)), so the order is probably (diffuse, ambient, specular). e.g. `DealerTrack`: `suntype.setLight(14538925, 4939375, 14538925)` |
| `void setFlare(ResourceRef, int, float, float, int, int)` | native/R | 0 | 5 (1) |  | (M) `setFlare(ResourceRef tex, int colorARGB, float a, float b, int c, int d)`: lens flare of a sun light (Scene passes skydome.rpk:0x100, -454761217, 1.0, 10.0, 15, 8). Roles of a..d unknown. |
| `void setType(ResourceRef)` | native/R | 0 | 0 |  | (L) No site. |
| `int getTypeID()` | native/R | 1 (1) | 0 | (no args) | (M) Used by MouseCursor to read the cursor sprite type. e.g. `MouseCursor`: `v3.getTypeID()` |
| `int lineCreate(ResourceRef, ResourceRef)` | native/R | 0 | 0 |  | (L) Debug line renderer; no site. |
| `void lineAdd(Vector3, Vector3, int, float)` | native/R | 0 | 0 |  | (L) Debug line renderer; no site. |
| `int plotRoute(ResourceRef, ResourceRef, int, float, Vector3)` | native/R | 0 | 2 (1) |  | (M) `plotRoute(ResourceRef a, ResourceRef b, int color, float width, Vector3 offset)`: draws the navigator route line (RaceSetup: color -65536, width 10). |
| `float getDetail()` | undeclared/R | 0 | 0 |  | (L) Registered in the exe, not declared in the shipped RenderRef.class. |


Script members: `<init>()`, `<init>(ResourceRef, int, String)`, `<init>(ResourceRef, RenderRef, String)`, `<init>(int)`, `<init>(int, int, String)`, `create(ResourceRef, int, String)`, `setMatrix(Pori)`


### java.util.resource.PhysicsRef

`extends java.util.resource.ResourceRef`. Compiled in `system/Scripts/util/resource/PhysicsRef.class`.

(H) Handle to a physics body INSTANCE below a parent GameRef.

| Method | Decl / Reg | Typed sites (callers) | Name-only sites | Arguments seen | Semantics and evidence |
|---|---|---|---|---|---|
| `void create(ResourceRef, int, String)` | native/R | 1 (1) | 17 (10) | 0: p0; 1: p1; 2: p2 | (H) create(parent, int type, name) native. e.g. `PhysicsRef`: `create(p0, p1, p2)` |
| `void createBox(ResourceRef, float, float, float, String)` | native/R | 27 (3) | 4 (1) | 0: map x27; 1: 0.2f x18, 3.986f x4; 2: 0.01f x21, 4.0f x3; 3: 9.85f x22, 8.4f x2; 4: "Platform Rotate physic" x18, "Left Window Physic" x2 | (H) `createBox(GameRef parent, float sx, float sy, float sz, String name)`: static box collider. 27 typed sites in DealerTrack/Garage (jack, walls, platform) with names like "Platform Rotate physic". Sizes are half-extents or full extents: unknown. e.g. `DealerTrack`: `RightWall.createBox(map, 0.005f, 4.0f, 8.4f, "Right Window Physic")` |
| `void createSphere(ResourceRef, float, String)` | native/R | 0 | 0 |  | (L) No site. |
| `void setMatrix(Vector3, Ypr)` | native/R | 4 (2) | 164 (21) | 0: v10, v11; 1: null x4 | (H) Pos+ori (null allowed). e.g. `Osd`: `v12.setMatrix(v10, null)` |
| `void setStatic(int)` | native/- | 0 | 0 |  | (L) Declared native, not registered, no site. |
| `Vector3 getPos()` | native/R | 1 (1) | 2 (2) | (no args) | (H) Vector3 position (Gadget.phy). e.g. `Gadget`: `phy.getPos()` |
| `Vector3 getVel()` | native/- | 0 | 0 |  | (L) Declared native, not registered, no site. |
| `Vector3 getVel(Vector3)` | native/- | 0 | 0 |  | (L) Declared native, not registered, no site. |
| `Vector3 getAngVel()` | native/- | 0 | 0 |  | (L) Declared native, not registered, no site. |
| `Ypr getOri()` | native/R | 0 | 0 |  | (L) No site. |


Script members: `<init>()`, `<init>(int)`, `<init>(ResourceRef, int, String)`, `setMatrix(Pori)`, `finalize()`


### java.util.resource.GroundRef

`extends java.util.resource.GameRef`. Compiled in `system/Scripts/util/resource/GroundRef.class`.

(H) GameRef subclass for the 'ground' native kind (system.rpk:0x35 `map`): the world root of a track. Provides water, fog, AI traffic, pedestrians, road routing. Track scripts keep it in `map`.

| Method | Decl / Reg | Typed sites (callers) | Name-only sites | Arguments seen | Semantics and evidence |
|---|---|---|---|---|---|
| `void setWater(float, float, float)` | native/R | 0 | 4 (4) |  | (M) setWater(Vector3 point, Vector3 normal, float a, float b) (Track: point (0,-1e6,-1500) = effectively no water; Valocity: (0,-8,-1500), normal (0,1,0), 300, 50). Compact form (FFF) declared natively too. |
| `void setWater(Vector3, Vector3, float, float)` | native/R | 0 | 4 (4) |  | (M) setWater(Vector3 point, Vector3 normal, float a, float b) (Track: point (0,-1e6,-1500) = effectively no water; Valocity: (0,-8,-1500), normal (0,1,0), 300, 50). Compact form (FFF) declared natively too. |
| `void addWaterLimit(Vector3, Vector3)` | native/R | 0 | 3 (3) |  | (M) (Vector3 point, Vector3 normal): clip plane limiting the water area (Drift/Damage tracks). |
| `void setFog(int, float, float)` | native/R | 17 (2) | 0 | 0: 10530992, 657946; 1: 70.0f x5, 60.0f x4; 2: 400.0f x5, 200.0f x4 | (H) `setFog(int rgb, float near, float far)`: 24-bit RGB (463390 = 0x07121E), near 0..70, far 150..1000. 17 typed sites. e.g. `Drag_Track`: `map.setFog(10530992, 0.0f, 1000.0f)` |
| `int addTrafficCar(GameRef, Vector3)` | native/R | 0 | 2 (2) |  | (M) Registers a car as AI traffic; returns traffic id (stored in `traffic_id`). |
| `void remTrafficCar(int)` | native/R | 0 | 3 (3) |  | (M) Removes by traffic id. |
| `int notifyTrafficCar(int, int)` | native/R | 0 | 1 (1) |  | (L) (trafficId, flag). |
| `int addTrafficN(GameRef, int, float, float, float)` | native/R | 0 | 1 (1) |  | (M) Spawn N traffic cars of a type: (GameRef type, int count*density, float, float, float). GroundRef.addTraffic script wrappers multiply by Config.trafficDensity. |
| `int addTrafficP(GameRef, Vector3, int, float, float, float)` | native/R | 1 (1) | 0 | 0: GRT_POLICECAR; 1: v2; 2: 1; 3: 2; 4: 5; 5: 2 | (M) Spawn at a position: City uses (GRT_POLICECAR, pos, 1, 2, 5, 2) for the police. e.g. `City`: `map.addTrafficP(GRT_POLICECAR, v2, 1, 2, 5, 2)` |
| `void delTraffic()` | native/R | 1 (1) | 0 | (no args) | (H) Removes all traffic (Track exit). e.g. `Track`: `map.delTraffic()` |
| `void setTrafficCarBehaviour(int, int)` | native/R | 0 | 1 (1) |  | (L) (trafficId, mode). |
| `void haltTrafficCross(Vector3, float)` | native/R | 1 (1) | 0 | 0: raceStart; 1: 15.0f | (M) City stops cross traffic at the race start: (pos, radius 15). e.g. `City`: `map.haltTrafficCross(raceStart, 15.0f)` |
| `void haltTrafficPath(Vector3, Vector3)` | native/R | 1 (1) | 0 | 0: pS; 1: pF | (M) (from, to) stops traffic on a path segment. e.g. `City`: `map.haltTrafficPath(pS, pF)` |
| `Vector3 getNearestCross(Vector3, float)` | native/R | 4 (3) | 4 (2) | 0: v1 x2, pS; 1: 0 x2, 150 | (H) `getNearestCross(Vector3 p, float maxDist)` returns the nearest road junction (0 = unlimited?). e.g. `City`: `map.getNearestCross(pS, 150)` |
| `Vector3 getStartDirection(Vector3, Vector3)` | native/R | 6 (3) | 0 | 0: pStart x2, pS; 1: pF, pS | (H) (start, finish) returns the road direction vector at the start for placing grid cars. e.g. `City`: `map.getStartDirection(pS, pF)` |
| `float getRouteLength(Vector3, Vector3)` | native/R | 2 (1) | 2 (1) | 0: pStart x2; 1: pFinish x2 | (H) (from, to) float length of the route along roads (RaceSetup). A (Vector3-less) overload is registered natively but not declared. e.g. `RaceSetup`: `track.map.getRouteLength(pStart, pFinish)` |
| `Vector3[] alignToRoad(Vector3)` | native/R | 7 (1) | 2 (1) | 0: pS x3, pF x2 | (H) `alignToRoad(Vector3 p)` returns Vector3[] {snapped position, direction?}. e.g. `City`: `map.alignToRoad(pS)` |
| `void setPedestrianDensityN(float)` | native/R | 0 | 1 (1) |  | (M) Density (script wrapper multiplies by Config.pedestrianDensity). |
| `void addPedestrianType(GameRef)` | native/R | 0 | 6 (1) |  | (M) Registers a ped gametype (humans.rpk:0x57..0x5b are the `ped` natives). |
| `void remPedestrianType(GameRef)` | native/- | 0 | 0 |  | (L) Declared as remPedestrianType but registered as removePedestrianType (name skew). |
| `float pedestrianDistance(Vector3, int)` | native/R | 0 | 0 |  | (L) No site. |
| `float findRoute(Vector3, Vector3)` | undeclared/R | 0 | 0 |  | (L) Registered, not declared: `float findRoute(Vector3 from, Vector3 to)` route search handle/length. |
| `float getRouteDist(Vector3)` | undeclared/R | 0 | 0 |  | (L) Registered, not declared. |
| `float getRouteLength()` | undeclared/R | 0 | 2 (1) |  | (H) (from, to) float length of the route along roads (RaceSetup). A (Vector3-less) overload is registered natively but not declared. |
| `Vector3 getRoutePos(float)` | undeclared/R | 0 | 0 |  | (L) Registered, not declared: `Vector3 getRoutePos(float distance)` position along a found route. |
| `void removePedestrianType(GameRef)` | undeclared/R | 0 | 0 |  | (L) Registered under this name; the shipped class declares `remPedestrianType` (name skew, neither is called). |


Script members: `addTraffic(GameRef, int)`, `addTraffic(GameRef, int, float, float, float)`, `getNearestCross(Vector3)`, `setPedestrianDensity(float)`


### java.util.resource.Animation

`extends java.lang.Native`. Compiled in `system/Scripts/util/resource/Animation.class`.

(H) Native animation player bound to a RenderRef skeleton: `new Animation(RenderRef obj, ResourceRef anim)` (ctor is native).

Fields: `RenderRef obj`

Engine-read field candidates (the exact field name is pushed as a string constant by exe code; first code xref in `StreetLegal_Redline.exe` after the `@`; short names can be coincidental): `obj@0x47e9da`

| Method | Decl / Reg | Typed sites (callers) | Name-only sites | Arguments seen | Semantics and evidence |
|---|---|---|---|---|---|
| `Animation(RenderRef, ResourceRef)` | native/R | 0 | 0 |  | (H) Native constructor `Animation(RenderRef obj, ResourceRef anim)`; the script field `obj` keeps the RenderRef alive. |
| `void finalize()` | native/R | 0 | 9 (9) |  | (H) Native finalizer: releases the animation player (9 name-only 'finalize' sites are other classes). |
| `void setSpeed(float)` | native/R | 1 (1) | 6 (2) | 0: 2.0f | (M) Playback speed factor (2.0 for the starter lady). e.g. `City`: `nrStarterLadyAnim.setSpeed(2.0f)` |
| `void setFade(float)` | native/R | 0 | 0 |  | (L) No site. |
| `void play()` | native/R | 1 (1) | 110 (27) | (no args) | (H) Plays once (City nrStarterLady). 110 by-name sites (names shared with Thread/others). e.g. `City`: `nrStarterLadyAnim.play()` |
| `void loopPlay()` | native/R | 0 | 6 (2) |  | (M) Loops (ROCTrack rocMen). |
| `void pause()` | native/R | 0 | 0 |  | (L) No site. |
| `void seek(float)` | native/R | 0 | 0 |  | (L) No site. |
| `float getPos()` | native/R | 0 | 2 (2) |  | (L) No site. |



### java.util.resource.ParticleSystem

`extends java.util.resource.RenderRef`. Compiled in `system/Scripts/util/resource/ParticleSystem.class`.

(H) RenderRef subclass; created with `new ParticleSystem(ResourceRef cfg, RenderRef parent[, String name])` (init native).

| Method | Decl / Reg | Typed sites (callers) | Name-only sites | Arguments seen | Semantics and evidence |
|---|---|---|---|---|---|
| `void init(ResourceRef, RenderRef, String)` | native/R | 1 (1) | 2 (2) | 0: p0; 1: p1; 2: p2 | (H) Called by the constructors `ParticleSystem(ResourceRef cfg, RenderRef parent[, String name])`; creates the emitter from a particle cfg (docs/formats: `[STATE]/[ACTION]` sections). e.g. `ParticleSystem`: `init(p0, p1, p2)` |
| `void stop()` | native/R | 0 | 31 (8) |  | (L) Stops emission; by-name sites are mostly SfxRef/Thread stop. |
| `void setFreq(float)` | native/R | 0 | 0 |  | (L) No site. |
| `void setDirectSource(String, Vector3, float, float, Vector3, float, float, float, String)` | native/R | 0 | 0 |  | (L) No script site. Emitter action control: (action name, Vector3, 2 floats, Vector3, 3 floats, String). |
| `void setSource(String, Vector3, float, float, Vector3, float, float, float, String)` | native/R | 0 | 0 |  | (L) Same signature as setDirectSource; no site. |
| `void setCounter(String, Vector3, float)` | native/R | 0 | 0 |  | (L) (action name, Vector3, float); no site. |
| `int getCounter(String)` | native/R | 0 | 0 |  | (L) (action name) -> int; no site. |
| `void delAction(String)` | native/R | 0 | 0 |  | (L) (action name); no site. |
| `void modePermanent(int)` | native/R | 0 | 0 |  | (L) (int flag); no site. |


Script members: `<init>(ResourceRef, RenderRef, String)`, `<init>(ResourceRef, RenderRef)`, `finalize()`


### java.util.resource.SfxRef

`extends java.util.resource.ResourceRef`. Compiled in `system/Scripts/util/resource/SfxRef.class`.

(H) Sound resource handle. Script wrappers: play() (3 overloads) call nplay. Flags SFX_NONE=0, SFX_LOOP=1, SFX_NOAUTOSTOP=0x80000000.

Static fields: `SFX_NONE = 0`, `SFX_LOOP = 1`, `SFX_NOAUTOSTOP = -2147483648`

| Method | Decl / Reg | Typed sites (callers) | Name-only sites | Arguments seen | Semantics and evidence |
|---|---|---|---|---|---|
| `int nplay(Vector3, float, float, float, int, int)` | native/R | 4 (1) | 0 | 0: null x2, p0 x2; 1: 0.0f x2, p1 x2; 2: p2 x2, 1.0f; 3: p3 x2, 1.0f; 4: p4 x2, SFX_NONE; 5: 0 x3, p5 | (H) `nplay(Vector3 pos, float vol, float pitch, float x, int flags, int ?)`: all script play() overloads end here. Returns a play id (int) that must be passed to stop(id) for looping sounds. (arg order in the descriptor is (Vector3;FFFII)I.) Protected native (flags 0x44). e.g. `SfxRef`: `nplay(null, 0.0f, 1.0f, 1.0f, SFX_NONE, 0)` |
| `void stop(int)` | native/R | 6 (3) | 31 (8) | 0: SFX_Loop_ID x4, applauseSfxID | (H) `stop(int playId)`: stops a looping/long sound (Garage Jack_Move_SFX, Painter spray, ROCTrack applause). e.g. `City`: `applause1.stop(applauseSfxID)` |


Script members: `<init>()`, `<init>(int)`, `play()`, `play(float, float, int)`, `play(Vector3, float, float, float, int)`, `play(Vector3, float, float, float, int, int)`


### java.util.resource.Dummy

`extends java.lang.GameType`. Compiled in `system/Scripts/util/resource/Dummy.class`.

Script members: `<init>()`, `<init>(int)`, `<init>(GameRef)`, `<init>(GameRef, int)`


## Pure-script classes of java.util

| Class | Extends | Methods | Fields | Natives | Role |
|---|---|---|---|---|---|
| `java.util.Config` | `java.lang.GameType` | 4 | 111 | 0 | See the text above the table. |
| `java.util.Event` | `java.util.Message` | 2 | 5 | 0 | Message subclass with flags F_KEY_PRESS 1 / F_KEY_RELEASE 2 / F_GAD_CHANGE 4 and the Gadget that raised it. |
| `java.util.Message` | `java.lang.Object` | 3 | 7 | 0 | type (MT_SYSTEM 0, MT_GAME 1, MT_EVENT 2), cmd, data. MC_SYS_TERMINATE=0 ends a GameType async thread. |
| `java.util.MessagePort` | `java.lang.Object` | 2 | 1 | 0 | FIFO + notify: putMessage appends and notifies, getMessage pops or returns null. |
| `java.util.StringTokenizer` | `java.lang.Object` | 5 | 3 | 0 | Script tokenizer (3 fields). |
| `java.util.Vector` | `java.lang.Object` | 26 | 3 | 0 | Growable object array (elementData/elementCount), 26 script methods, uses System.arraycopy. |
| `java.util.VideoMode` | `java.lang.Object` | 1 | 4 | 0 | Record of a display mode. |


### Config

`java.util.Config` (system.rpk:0x47, `script java.util.Config`) is both a GameType and the engine's settings record:
111 fields, mostly statics with defaults (video 800x600x32, camera tuning, damage multipliers, memory budgets,
controller dead zones...). The exe reads/writes them **by name**: the start-up function (0x58c6f0) reads
`acceptBuild`, `version`, `FILD`, `video_x/y/depth/windowed`, `shadows`, `resource_refreshrate`, `Sound_3D_HW`,
`Sound_Mix_HW` and `init` (see entry-points.md), and later code reads about 70 more (see the candidate list below).
`Config(int)` loads `save/game/options` (`loadConfig`), `finalize()` saves it (`saveConfig`, magic -19088744 =
0xFEDCBA98, version 12), and falls back to volumes 1.0/0.5/1.0 when no file exists.

### java.util.Config

`extends java.lang.GameType`. Compiled in `system/Scripts/util/Config.class`.

(H) See the text above the table.

<details><summary>109 static fields with initial values</summary>

`SHIGH = 3`, `HIGH = 2`, `MID = 1`, `LOW = 0`, `OFF = -1`, `version = "v2.2.1"`, `acceptBuild = 601`, `video_windowed = 0`, `video_x = 800`, `video_y = 600`, `video_depth = 32`, `video_gamma = 1.0f`, `SysCursor = 1`, `Sound_3D_HW = 2`, `Sound_Mix_HW = 2`, `loadConfigEnabled = 1`, `majomParade = 0`, `init = sl.rpk:0x40r`, `texture_size = 1`, `texture_save_quality = 1.0f`, `texture_format = 3`, `shadow_size = 256`, `shadow_detail = 0.25f`, `shadows = 1`, `mirror = 0`, `flares = 1`, `headlight_rays = 1`, `BgScene = 1`, `object_detail = 0.03f`, `object_detail_amp = 13.0f`, `particle_density = 0.5f`, `skidmark_max = 2000`, `trafficDensity = 0.2f`, `pedestrianDensity = 0.1f`, `cameras_viewrange = 400.0f`, `camera_int_clipplane = 0.1f`, `camera_int_viewrange = 400.0f`, `camera_ext_clipplane = 0.2f`, `camera_ext_viewrange = 400.0f`, `camera_mirror_viewrange = 50`, `mirror_object_detail = 0.03f`, `mirror_object_detail_amp = 13.0f`, `camera_AOV = 15.0f`, `camera_torque = 0.02f`, `camera_force = 2.0f`, `camera_damping = 1.0f`, `camera_perpdamping = 0.7f`, `camera_max_force = 20.0f`, `camera_look_ahead = 0.0f`, `camera_go_behind = 0.0f`, `camera_go_above = 0.0f`, `camera_distance = 5.0f`, `camera_FOV = (1.25f * 0.5f)`, `head_move_steer = 1.0f`, `head_move_vel = 1.0f`, `head_move_acc = 1.0f`, `camera_render = 0`, `camera_nocollision = 0`, `metricSystem = 1`, `player_transmission = 1`, `player_steeringhelp = 0.666f`, `player_abs = 0.0f`, `player_asr = 0.0f`, `player_esp = 0.0f`, `deformation = 1.6f`, `external_damage = 0.25f`, `internal_damage = 1.0f`, `player_damage_multiplier = 0.8f`, `player_change_distance = 10.0f`, `player_change_velocity = 2.0f`, `player_display = 0`, `MouseHelp = 0`, `wheel_gndfeedback_factor = 7.0f`, `wheel_brake_factor = 6.0f`, `engine_inertia_factor = 6.0f`, `target_angle_weight = 0.5f`, `controller_deadzone = 0.0f`, `mouseSensitivity = 0.8f`, `ForceFeedBack = 1`, `FFB_strength = 1.0f`, `FFB_strength_emulated = 0.0333f`, `gravity = 9.81f`, `ground_friction = 1.0f`, `ground_precache = 1`, `resource_refreshrate = 2`, `resource_loadrate = 10`, `mem_vertex_max = ((80 * 1024) * 1024)`, `mem_vertex_min = ((16 * 1024) * 1024)`, `mem_texture_max = ((80 * 1024) * 1024)`, `mem_texture_min = ((40 * 1024) * 1024)`, `mem_instance_max = 5500`, `mem_instance_min = 4000`, `mem_sound_max = ((66 * 1024) * 1024)`, `mem_sound_min = ((12 * 1024) * 1024)`, `mp_arrows_on = 1`, `deadZoneAccelerate = 0.0f`, `deadZoneBrake = 0.0f`, `deadZoneSteering = 0.0f`, `deadZoneClutch = 0.0f`, `throttleSpeed = 0.02f`, `brakeSpeed = 0.02f`, `steerSpeed = 0.01f`, `steerhelp_turn = 1.0f`, `throttlePower = 2.5f`, `brakePower = 2.5f`, `steeringPower = 1.5f`, `clutchPower = 2.5f`, `SAVEFILEID = -19088744`, `SAVEFILEVERSION = 12`

</details>

Fields: `int FILD`, `int gpsMode`

Engine-read field candidates (the exact field name is pushed as a string constant by exe code; first code xref in `StreetLegal_Redline.exe` after the `@`; short names can be coincidental): `version@0x58c7dd`, `acceptBuild@0x58c7af`, `video_windowed@0x58c856`, `video_x@0x58c834`, `video_y@0x58c83e`, `video_depth@0x58c84a`, `video_gamma@0x427f25`, `SysCursor@0x460012`, `Sound_3D_HW@0x58c8cf`, `Sound_Mix_HW@0x58c8f3`, `init@0x4289a6`, `texture_size@0x427d92`, `texture_save_quality@0x427f6a`, `texture_format@0x427ed8`, `shadow_size@0x427caa`, `shadow_detail@0x427d46`, `shadows@0x427cf4`, `flares@0x427c60`, `headlight_rays@0x427c16`, `object_detail@0x427de4`, `object_detail_amp@0x427e8c`, `particle_density@0x427f50`, `skidmark_max@0x427f60`, `trafficDensity@0x484115`, `camera_int_clipplane@0x48b989`, `camera_int_viewrange@0x458dd6`, `camera_ext_clipplane@0x437d1b`, `camera_ext_viewrange@0x437d2b`, `camera_AOV@0x437cdb`, `camera_torque@0x437ce8`, `camera_force@0x437cf5`, `camera_damping@0x437d02`, `camera_perpdamping@0x437d0f`, `camera_max_force@0x437d38`, `camera_look_ahead@0x442703`, `camera_go_behind@0x442713`, `camera_go_above@0x442723`, `camera_distance@0x442733`, `camera_FOV@0x437d55`, `head_move_steer@0x428220`, `head_move_vel@0x428241`, `head_move_acc@0x428251`, `camera_render@0x437e0b`, `camera_nocollision@0x437d45`, `deformation@0x427fa9`, `external_damage@0x427f84`, `internal_damage@0x427f99`, `player_damage_multiplier@0x47999e`, `player_change_distance@0x4799d2`, `player_change_velocity@0x4799e2`, `player_display@0x47995f`, `MouseHelp@0x428105`, `wheel_gndfeedback_factor@0x4280b9`, `wheel_brake_factor@0x4280df`, `engine_inertia_factor@0x42808d`, `target_angle_weight@0x442772`, `controller_deadzone@0x478494`, `mouseSensitivity@0x45ff30`, `ForceFeedBack@0x428051`, `FFB_strength@0x428060`, `FFB_strength_emulated@0x428075`, `gravity@0x463322`, `ground_friction@0x46338b`, `ground_precache@0x47d0cb`, `resource_refreshrate@0x58c87e`, `resource_loadrate@0x428047`, `mem_vertex_max@0x427fb9`, `mem_vertex_min@0x427fc3`, `mem_texture_max@0x427fd2`, `mem_texture_min@0x427fe1`, `mem_instance_max@0x427ff0`, `mem_instance_min@0x427fff`, `mem_sound_max@0x428010`, `mem_sound_min@0x428030`, `steerhelp_turn@0x42812d`, `FILD@0x58c7e7`

Script members: `<init>(int)`, `finalize()`, `saveConfig(String)`, `loadConfig(String)`

