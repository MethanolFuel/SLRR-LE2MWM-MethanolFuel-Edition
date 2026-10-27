# RPAK instance / spatial trees (TypeOfEntry 1, `ins_game`)

Code: `src/Slrr.Formats/Instances/` (reader, chunk codecs, relayout writer, coverage),
CLI: `tools/Slrr.Cli/CmdInstances.cs` (`dump-instances`), tests: `tests/Slrr.Formats.Tests/Instances/`.
See `rpak.md` for the archive container. All integers little-endian.

## Measured facts (all 87 shipped .rpk, 17 contain TypeOfEntry 1 rows)

- `system.rpk` names type id 0x4 (TypeOfEntry 1/1) `ins_game`; map roots have `superId 0x00010004`
  (= reference slot 1 `system.rpk`, id 0x4).
- **Node layout**, at a record's `offset`, `size` bytes long:
  ```
  u32 header                 0 in all 156,325 nodes
  u32 childCount
  childCount x child record  exactly the RPAK entry-table row format (23 bytes + alias + 48 extra),
                             with TypeOfEntry 1 in all 156,305 child records
  chunks until node end:     char[4] tag | u32 length | payload
  ```
  Every child record's `superId` equals its parent's `id` (156,325/156,325 incl. roots).
  Child `offset` is an **absolute file offset** to another node; `size` is that node's byte length.
- Starting from the table's TypeOfEntry 1 rows (20 roots), the trees cover the data region with no holes and no
  overlap: 116,557,434 gap bytes in 16 map/garage archives = 116,557,434 tree bytes inside gaps (100%); total tree
  bytes 116,563,437 (the 20 root nodes, 6,003 bytes, lie inside their table entry's own range).
- No node is referenced twice; maximum depth 22.
- Chunk tags and bytes: `POLY` 2,883 chunks / 70,080,480 B; `PHYS` 66,353 / 30,252,144 B; `RSD\0` 31,599 / 3,268,184 B;
  `EXTP` 1 / 12 B (city.rpk root, payload u32 3). Node headers + child records: 12,962,617 B.
- **48-byte extra** (also on the archive's own table rows): 6 f32 = box **center xyz + half extents xyz**
  (every PHYS and POLY vertex lies inside center ± half, 66,353/66,353 PHYS and 4,214/4,214 POLY sub-meshes;
  156,289 of 156,305 child boxes lie inside their parent box with 5 cm tolerance), then 24 bytes, zero in all 156,325.
- `additionalType` on tree records: 0x01 spatial node (127,279), 0x21 placed object (29,026), 0x11 map root with
  RSD (18, of which 1 also has EXTP). `parentCompatible` f32: 0.0 (127,280), 1.0 (29,040), 2.0 (5).
- Node shapes (additionalType, has children, chunks): (0x01, leaf, PHYS) 65,981; (0x01, inner, none) 55,846;
  (0x21, leaf, RSD) 29,026; (0x01, leaf, RSD with a single NUL) 2,554; (0x01, inner, POLY) 2,511;
  (0x01, leaf, POLY+PHYS) 372.
- **PHYS** = collision mesh: `u32 triCount | triCount x {u32 surface, u32 a, b, c, f32 normal[3]} | u32 vertCount | vec3[vertCount]`.
  Parses exactly (no trailing bytes, indices in range) in 66,353/66,353 chunks; normal == normalize((b-a)x(c-a)) for
  693,433/693,433 non-degenerate triangles. `surface` takes 108 values 0..107.
- **POLY** = render geometry: `u32 unknown | u32 submeshCount | submeshes`, submesh =
  `u32 material | u32 vertCount | u32 triCount | vertCount x 56-byte vertex | u32 index[3*triCount]`.
  Vertex = pos f32[3], normal f32[3], u32 color0, u32 color1, f32[6] (three UV pairs). Parses exactly in 2,883/2,883
  chunks; 1,100,565 vertices, 696,012 triangles; normals unit length in 4,202/4,214 sub-meshes. `unknown` = 1 in 2,857
  chunks (others: 122, 6, 199, 47). `material` takes 108 values 0..107.
- **RSD\0** = CRLF text `gametype 0xSSSSIIII` + `params ...` (+ `flags 1.000` once in city2.rpk).
  `gametype` high 16 bits = 1-based slot in the archive's reference table (0 = the archive itself), low 16 bits = entry
  id there: e.g. city.rpk `0x0002018E` -> slot 2 `objects.rpk` id 0x18E `lamp_base`; map roots use `0x00010035` ->
  `system.rpk` 0x35 `map`. Map roots: `params 0xFLAGS[,0,0,0,0,0,0,mesh.scm[,ai.trc]]`.
  Object instances: `params x,y,z,a,b,c` — 6 numbers in all 29,026 objects in city/city2/test_track/ROC/DamageTrack;
  b is non-zero in at least 96 objects (city 44, ROC_track 52; count from a text grep, lower bound), zero elsewhere.
  The record box is usually a few metres around (x,y,z) but not centred on it (e.g. lamp boxes sit above the base point).
- Script cross-reference: `maps/Addon_Maps/Drag_Track/Scripts/src/Drag_Track.java` loads
  `new GroundRef(Maps.Addon_Maps.Drag_Track:0x00000001r)`, i.e. the root `ins_game` id 0x1 (`Ground`, params
  `...Meshes\Ground.SCM`); start positions use `Vector3` + `Ypr`.

## Round-trip / relocation results (`dump-instances --coverage .`)

- Parse -> relayout (`InstanceLayout.Write`) with no changes: **17/17 byte-identical**.
- Adding one 64-byte reference slot (moves every absolute offset): **17/17** archives re-read with identical tree
  fingerprints (records, boxes, chunk payloads), identical ordinary entry data and identical gap totals.
- Decoded share of tree bytes (node structure + PHYS/POLY that re-serialize identically + RSD text):
  116,563,425 / 116,563,437 (the 12 opaque bytes are the EXTP chunk).
- Per archive (gap bytes = tree bytes in gaps, decoded/tree = 100.000% except city.rpk 32,186,648/32,186,660):
  Caynon 1,272,079; Dead_Island 342,011; Drag_Track 5,530,010; Physic_Lab 1,420,310; Top_Gear_Track 1,923,612;
  DamageTrack 726,695; DriftTrack 8,347,606; MP1 766,154; ROC_track 7,518,722; city 32,186,319; city2 37,975,044;
  test_track 9,809,421; carlot 7,908,305; dealer 73,601; dealer2 119,000; garage 638,545 (4 roots).
- Unit tests cover a synthetic archive (build from memory, rewrite identical, grow a chunk + alias + header and re-read),
  codec round trips, malformed input (cyclic reference, oversized child count) and three shipped archives.

## Hypotheses (not verified)

- Spatial nodes form a bounding-volume tree over the static map: unnamed inner nodes split 2-3 ways, nodes named
  `<level digit><A-D>` (e.g. `0D`, `1A`) look like quadtree cells; POLY sits on cells, PHYS on leaves.
- POLY `material` and PHYS `surface` (both 0..107) index the map mesh's material list (`.scm`) — same range, unproven.
- Object params `a,b,c` = yaw, pitch, roll in radians (the script API's `Ypr`); observed `a` values lie roughly in -3..3.
- additionalType bits: 0x20 = placed object, 0x10 = map root. `parentCompatible` 1.0 marks game objects.
- color0/color1 = diffuse/specular or baked vertex lighting; UV1 = lightmap UVs (values in 0..1).
- EXTP payload (u32 3, city.rpk only) meaning unknown.

## Open questions

- Whether the engine requires the original block order (we keep it; relayout never reorders shipped blocks).
- How the engine uses the 24 always-zero bytes of the extra block and the node header u32.
- Relation of POLY geometry to the `.scm`/`.scx` meshes referenced in the root params (duplicate or replacement?).
