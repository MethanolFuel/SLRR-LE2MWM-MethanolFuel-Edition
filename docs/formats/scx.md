# SCX (`.scx`) meshes

Magic `INVO`, then u32 version. Two versions ship: v4 (2,682 files) and v3 (542 files).
All 3,224 files parse with 100% byte coverage (88,662,062 bytes), 5,569 meshes.

## v4: chunk directory

```
"INVO" u32 4  u32 chunkCount
chunkCount x { u32 type, u32 absoluteOffset }
chunks: u32 type (= directory type), u32 size (includes this 8-byte header), payload
```

Chunk types (counts over all files): 0: 4,561, 1: 4,382, 2: 277, 3: 55, 4: 4,714, 5: 4,714.
Per mesh the sequence is `0 1 4 5`, `0 2 4 5` or `0 3 4 5` (3 then repeats `3 4 5`); 115 files start `1 4 5` without a material chunk and 13 files have no chunks at all.

| Type | Payload | Status |
|---|---|---|
| 0 | material, 12..176 bytes | raw (hypothesis: colours, texture ids, name) |
| 1 | u32 | unknown |
| 2 | 12 bytes | unknown |
| 3 | variable | unknown (skinning / bone weights?) |
| 4 | u32 vertexCount, u32 flags, vertexCount x stride | stride = payload / count (always exact) |
| 5 | u32 indexCount, u16 indices | all indices are < vertexCount (measured); triangle list assumed |

Vertex flags behave like a D3D FVF mask. Sizes per bit, fitted to every flag/stride pair observed:
`0x1` position 12, `0x4` 4, `0x8` 8, `0x10` 12, `0x20` 4 (blend data), `0x40` normal 12,
`0x80` 4, `0x100` 4 (colours?), `0x200` uv0 8, `0x400` uv1 8, `0x800` uv2 8; `0x2` carries no data.
Normal offset was measured: unit-length normals at byte 16 for flags 0x245/0x263, at byte 20 for 0x265.
One flag value has bit 0x40000 set (0x40241); its meaning is unknown.

## v3: sequential meshes

```
"INVO" u32 3
repeat until EOF:
  u32 materialSize (includes itself), material bytes
  u32 vertexCount, vertexCount x stride   ; stride 44 if materialSize == 56, else 64
  u32 triangleCount, triangleCount x 3 x u32
optional u32 0 terminator (523 of 542 files)
```

44-byte vertex (from inspected samples): position 12, normal 12, uv 8, 8 bytes (uv2?), u32 colour (0xFFFFFFFF).
64-byte vertex: the same plus 20 bytes (unknown). Many v3 meshes are unindexed (vertexCount = 3 x triangles).
Material: 136 bytes typical, contains colours as floats and a name (`1 - Default`, `Concrete_Asphalt`).

## glTF export

`slrr dump-scx file.scx --gltf out.gltf` writes positions, normals and indices (one primitive per mesh),
spot-checked for v3 (MW_Mod/Meshes/Jack.SCX) and v4 (cars/racers/Nonus_data/meshes/chassis.scx).
UVs, materials and textures are not exported yet. Coordinate system / handedness not verified.

## Open questions

Material layout (both versions), chunk types 1/2/3, the extra 20 bytes in 64-byte v3 vertices,
whether index chunks are lists or strips (lists assumed), and the units/axes of positions.
