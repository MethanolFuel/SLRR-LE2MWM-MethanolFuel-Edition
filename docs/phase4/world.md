# Phase 4 area: static world (`src/Slrr.World`)

Loads a map archive's instance forest (`Slrr.Formats.Instances`) and builds what driving needs: a collision world with
spatial indices, a render-side scene description, the surface table, and the placed-object list. Every behaviour is
tagged **MEASURED** (checked against shipped data by `world verify|angles|bench` or a test) or **GUESSED**.

Isolated build/test: `dotnet build Slrr.World.sln -warnaserror`, `dotnet test tests/Slrr.World.Tests`
(corpus tests pass vacuously without game data). CLI host: `tools/Slrr.World.Host` (`slrr-world <cmd>`), which compiles
`tools/Slrr.Cli/CmdWorld.cs`. **Integration for the lead:** add `<ProjectReference Include="..\..\src\Slrr.World\Slrr.World.csproj" />`
to `Slrr.Cli.csproj` and `"world" => CmdWorld.Run(args[1..], o, e)` to `Commands.cs`. Until the reference exists, `Slrr.Cli`
does not compile because `CmdWorld.cs` is already in its folder.

| File | Contents |
|---|---|
| `IGroundQuery.cs` | `IGroundQuery` (physics-facing interface), `SurfaceHit`, `Contact` |
| `CollisionWorld.cs` | `TriangleSoupBuilder`, `CollisionWorld`: BVH + ground grid, ground/ray/sphere/capsule queries |
| `GroundGrid.cs` | uniform XZ grid used by `TryGetGround` |
| `Geometry.cs` | closest-point and ray-triangle helpers |
| `StaticWorld.cs` | `WorldLoader.Load(rpk, options)`, `StaticWorld`, `PlacedObject`, `DynamicBody`, `WorldStats`, box/sphere shape triangulation |
| `RenderScene.cs` | `RenderScene` / `RenderChunk` / `RenderSubmesh` (zero-copy 56-byte vertex buffers) / `RenderMaterial` |
| `Surfaces.cs` | `SurfaceTable`, `SurfaceInfo`, `SurfaceClass`, text and binary `.scm` parsers |
| `ObjectCatalog.cs` | object-type ICFG parser (`object bounds`, `body`, `render` lines), archive/path resolution |
| `Placement.cs`, `PlacementAnalysis.cs` | angle convention and the fitting code that selected it |
| `HeightMap.cs` | top-down sampling, PNG and SVG writers |

## Interface for the physics engine

```csharp
public interface IGroundQuery {
    bool TryGetGround(float x, float z, float fromY, out SurfaceHit hit); // ray from above at (x,z)
    bool Raycast(Vector3 origin, Vector3 direction, float maxDistance, out SurfaceHit hit);
    SurfaceTable Surfaces { get; }
}
public readonly record struct SurfaceHit(Vector3 Position, Vector3 Normal, int Surface, float Distance, int Triangle, int Source);
```

`CollisionWorld` implements it and adds `OverlapSphere`, `OverlapCapsule` (penetration `Contact`s with push direction and depth),
`HeightAt`, `LineOfSight`. `StaticWorld.Ground` exposes the interface; vehicle code needs nothing else. All queries are
read-only and thread safe. Y is up (MEASURED: placed object Y equals ground Y, see below). Pass `float.PositiveInfinity` as
`fromY` for the top-most surface, or the wheel's height plus a margin to get the surface under a bridge.
`TryGetGround` only considers triangles whose stored normal has Y > 0.05 (GUESSED threshold); ceilings and walls are ignored.
`Surface` is an index into `Surfaces` (`SurfaceTable.ObjectSurfaceId` = 0xFFFF for placed-object shapes).

## Collision world

* **Source (MEASURED):** every PHYS chunk of every spatial node, already in world coordinates (no node transform exists).
  city.rpk: 23,976 PHYS chunks, 197,286 triangles; 0 bad indices; 30 degenerate (zero-area) triangles dropped over all
  maps; the stored normal equals the geometric normal for every kept triangle (0 mismatches).
  Normal orientation: city 159,623 up (Y > 0.05), 2,430 down, 35,233 walls; ROC_track 48,541 / 3,534 / 2,965.
* **Index:** BVH (binned SAH, 16 bins, leaf <= 4 triangles, parallel top-level build; triangles with a bbox edge > 64 m go into a
  second tree so terrain sheets do not inflate leaf boxes) for ray/overlap queries, plus a uniform XZ grid (6 m cells, widened
  above 4M cells; exact scan-line coverage) over the upward triangles for ground queries. Grid and BVH ground results agree on
  199,999 of 200,000 probes (the one difference is a point within 1e-5 of a triangle edge, where the two use different boxes).
* **Placed objects (MEASURED parse, semantics partly GUESSED):** `RSD gametype -> objects.rpk entry -> ICFG text`. The tree has
  no geometry for objects, so collision comes from the object type's `body px py pz ax ay az mass shape` lines (450 in
  objects.rpk): `box hx hy hz` (half extents, GUESSED from the lamp pole: body y 4.0, half-height 4.0, bounds Y 0..8.02),
  `sphere r`, or an `.scx` collision mesh. Mass 0 = static (added to the collision world as 12-triangle boxes, 80-triangle
  icospheres (GUESSED tessellation) or the mesh); mass > 0 = movable, reported in `StaticWorld.DynamicBodies` for the physics
  engine and not added. **Collision `.scx` meshes are stored x100 (MEASURED):** body y equals half of the mesh's Y extent times
  0.01 for hotel_013 (1075.02 -> 10.748), kuka_d (49.11 -> 0.506), villanyoszlop (344.0 -> 3.44); `WorldLoader.PhysMeshScale`.
  Without the scale the city bounds were +-4,900 x 1,087; with it they are x -1,471..1,485, y -114..211, z -1,929..329.
  city.rpk: 10,168 objects, 8,944 with bodies, 8,054 static + 2,439 dynamic bodies, 100,378 object-shape triangles.
  Open: 6 objects (area_41_egyedi, bridge4_egyedi, ...) have collision meshes with 112 triangles > 200 m (plausible for
  roads/areas, not verified); box bodies lie inside the type's `object bounds` for 6,878 of 8,064 in city but 0 of 318 in
  ROC_track (e.g. kaktusz: box at the origin with half height 3 against bounds Y -0.5..4.7), so box body semantics are not
  fully understood.

### Placed-object angle convention (answers the "verify against data" request) - MEASURED

`params x,y,z,a,b,c`: Y is up; `a` = yaw about +Y, `b` = pitch about X, `c` = roll about Z, composed as
`Rz(c) * Rx(b) * Ry(a)` with row vectors, i.e. exactly `Matrix4x4.CreateFromYawPitchRoll(a, b, c)` followed by a translation
(`Placement.Transform`). Method (`world angles`): transform every object type's local `object bounds` box by each of 288
candidate conventions (6 axis permutations x 8 sign choices x 6 multiplication orders) and compare the world AABB with the 48-byte box stored
in the object's instance record. The default convention reproduces the record box to within 5 cm for **9,708 / 9,708** objects
in city.rpk and **1,791 / 1,791** in ROC_track (median error 0, so the box is derived from bounds and this transform).
Mirrored yaw fits clearly fewer of the yawed objects (symmetric objects fit both). Objects with non-zero `b`: 2 in city, 71 in
ROC_track; non-zero `c`: 7 in ROC_track; all fit. Orders 0 and 5 (and some sign flips of `c`) tie on the data; order 0 is used.
Ground check (`world verify`, object collision excluded): the ground triangle found within 1 m above each object has
`object.y - ground.y` median 0.000, p25 -0.000, p75 0.001; 91.7% of 9,299 city objects and 94.7% of ROC_track's within 0.5 m
(the rest stand on roofs/bridges or have no PHYS ground below).

## Surface table - what the surface index means

* **PHYS `surface` and POLY `material` are the same index space, the material list of the map's mesh file (MEASURED).**
  Pearson r between PHYS triangles per surface and POLY triangles per material is 1.00 on city, city2, test_track, ROC_track,
  MP1 and DriftTrack (`world verify`); on MP1 the per-index counts are 1091/1092, 452/453, 790/790, 204/208, 8/8, ...
  (`world surfaces`). A map root's `params 0xFLAGS,0,0,0,0,0,0,mesh.scm[,ai.trc]` names the file (game-relative, Windows path).
* **Text `.scm`** (city 108 materials, city2 75, Drag_Track 18, Dead_Island 5, Top_Gear 7): `materials N`, then
  `material <idx> <name>` lines, each optionally followed by property lines, keys read by the exe next to `ground_friction`:
  `friction`, `rollres`, `collcoeff`, `skid_SM` (skid-mark resource), `skid_SMcolor` (ARGB), `skid_PS` (smoke particle
  resource), `skid_SFX` (key exists in the exe, never set in shipped files). Only two map files carry values at all:
  Dead_Island (road/rock friction 0.8; Sand2 friction 0.6, rollres 3.0, collcoeff 15.0, skid mark 0x00020047 "Grass Skid" in
  MW_Mod.rpk, colour 0xFF737373, smoke 0x00030104 "Sand smoke") and Top_Gear (Material #38: friction 0.8, rollres 2.0, same
  skid). city/city2 give names only. Everything without a property line uses neutral defaults (GUESSED: friction 1.0,
  rollres 1.0, collcoeff 1.0; resource id 0 = "Any"). The tyre script (`Tyre.java`) multiplies its own friction; how the
  exe combines tyre and surface friction is not derived here.
* **Binary `.scm`** (MP1.SCM, Caynon Ground.SCM): `u32 count | u32 36 | count x { u32 tag, char name[32] }`, `count*36+8 ==
  file size` in both. Names only (`_p_beton`, `_p_traFa`, `_p_asfalt`, `_p_start`, "Material #288"); the `_p_` prefix looks
  like "physics material" (GUESSED). The tag low byte varies, upper bytes 02 03 00 (unknown).
* **Classes (GUESSED from names):** Asphalt (asfalt, RBegin/RCenter, Cross, autopalyaszele), Concrete (sidwalk, patka, beton,
  labazat), Grass (trawa, traFa, GBegin/GCenter, Area, fu), Sand, Rock (szikla), Metal (szalagkorlat, drotkerites, racs),
  Glass (ablak, kirakat), Wall (fal, teto, csatorna), Wood (ajto), Foliage (tree/alp). Rendered by `world map --surface`
  (the city image shows roads, kerbs, grass and building footprints as expected). Maps without a `.scm` file in the repo
  (test_track, ROC_track, DriftTrack, DamageTrack) get anonymous `material N` entries.

## Render scene

`RenderScene.Chunks` = one entry per POLY chunk with the owning node's box (for culling) and `Submeshes`
(material, raw 56-byte vertex buffer slice, triangle-list indices). city.rpk: 624 chunks, 624 submeshes, 257,560 vertices,
198,579 triangles; every submesh material is inside the material table (0 out of range on all measured maps).
`Materials` = name + `SurfaceInfo`. Not resolved here: texture binding (material name -> texture; the `.scm` has none),
winding/handedness of the index lists, and the object render models (`ObjectType.Renders` lists the `render 0xID x y z rx ry rz`
resources and offsets; `PlacedObject.Transform` is the instance matrix). The renderer must draw those from the objects archive.

## `world` CLI

`world stats|surfaces|height|ray|map|bench|verify|angles <map.rpk> [args] [--root gameRoot]`. `map` writes a top-down
PNG or SVG (`--cell`, `--surface` colours by class, `--bounds x0 z0 x1 z1`; +Z up). Example: `world map maps/city.rpk city.png --cell 3`
gives a 986x753 image in 0.14 s that clearly shows the road network, the harbour/hill ring and building footprints (black =
no PHYS ground, i.e. building interiors).

## Measured performance (4 vCPU shared by 5 agents, so figures vary +-30%; best of 3 runs)

| map | triangles | build total | notes |
|---|---|---|---|
| city.rpk (10,168 objects) | 297,664 | 0.83-0.88 s | forest 100 ms, PHYS 72 ms, objects 150 ms, BVH+grid 400-450 ms, render 46 ms |
| city2.rpk | 410,148 | ~1.0 s | |
| test_track / ROC_track | 81,397 / 60,776 | 0.31 / 0.29 s | |
| DriftTrack / Drag_Track / MP1 / DamageTrack | 34,500 / 23,664 / 3,247 / 3,075 | 0.06-0.14 s | |

city.rpk queries (`world bench maps/city.rpk 1000000`): ground, random points on up-facing triangles, one thread
**1.29-1.42 M/s** (0.70-0.77 us); four threads 3.0-5.6 M/s; coherent path (0.1 m steps, what wheels see) **10-10.7 M/s**;
bounded-start ground (height lookup + bounded query pair) ~0.9 M pairs/s; horizontal ray from 1.5 m, 500 m max: ~210 k rays/s;
sphere overlap r 0.5 m: ~240 k/s. Targets (10k+ objects in a few seconds, 1e6 ground queries/s) are met.
Memory (not measured precisely): triangle arrays are 52 B per triangle; the grid is 4 B per cell + 4 B per triangle-cell pair.

## Open questions

1. How the exe combines tyre friction with `SurfaceInfo.Friction`/`rollres`/`collcoeff`, and the real defaults for materials
   without property lines (here neutral 1.0); `skid_*` ids are kept raw (not resolved to resources).
2. Box body semantics (half sizes confirmed for lamp poles; ROC_track boxes unexplained), and whether mass 0 really means static.
3. Whether object collision meshes use the x100 scale universally (3 types verified by hand; after scaling, only 6 objects keep triangles > 200 m).
4. Index-list winding, texture binding and the use of the three UV sets / two vertex colours in POLY.
5. Whether PHYS normals/ceilings (2,430 down-facing in city) should block upward ground probes under bridges the way `fromY` does here.
6. Test-host note: on the shared 16 GB box the xunit host was killed twice right after a build (looks like OOM from other agents); reruns pass.
