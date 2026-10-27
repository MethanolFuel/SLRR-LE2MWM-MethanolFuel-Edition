# SCX materials, UVs, textures and textured glTF (Phase 2)

Extends [scx.md](scx.md) and [ptx.md](ptx.md). Code: `src/Slrr.Formats/Graphics/`, CLI `export-gltf`
(`tools/Slrr.Cli/CmdGltf.cs`), tests `tests/Slrr.Formats.Tests/Graphics/`. All numbers below were measured on the
shipped content on 2026-10-27 (3,224 SCX files, 87 RPK archives) unless marked **hypothesis**.

## How a mesh finds its textures (measured)

SCX files do not name textures. The link lives in the RPK archives:

```
type 14 "render" entry      mesh 0x0000003A            -> type 5 entry: sourcefile cars\racers\Nonus_data\meshes\chassis.scx
                            texture 0x0000003D         -> type 7 entry: sourcefile ...\Nonus_dash.dds
                            texture 0x0000003C         -> ...\Nonus_interior.dds
                            texture 0x00020025         -> reference slot 2 of Nonus.rpk = cars.rpk, id 0x25
                                                          = sourcefile cars\textures\default_white.png
                            texture 0x0000003B         -> ...\Nonus_chassis.dds
```

- A texture stage in an SCX material stores an **index into the render entry's ordered `texture` list**.
  Checked on Nonus chassis: stage indices 0,1 -> `Nonus_dash`, `Nonus_interior` for materials named `dash`,
  `interior`; material `chassis` uses 2 (white, paint base) and 3 (`Nonus_chassis.dds`, livery layer).
- Ids with zero high 16 bits are looked up in the same archive; otherwise the high 16 bits are the 1-based
  reference slot (`RpakReference.Index`) and the low 16 bits the id inside that archive.
- `texture 0x00000000` lines exist (711 stage uses); 622 of them are stage slot 3 (see below).
- Entry text may carry `;` comments (`texture 0x00000012 ; Weight.dds`).
- The cfg files only reference render ids (`render 0x...`); no cfg-level texture lookup was needed.

Index: 87 archives, 3,017 render entries whose mesh has a `sourcefile`; 1,965 of the 3,224 SCX files are drawn by at
least one render entry (the others are physics `_phys_*` shapes, LOD/catalog variants referenced elsewhere, or
unreferenced). 598 mesh `sourcefile`s name `.scx` files that are not shipped (e.g. `maps\city2\meshes\*`).

## v4 material (chunk type 0) — measured

```
u32 flags        values 0,3,4,12,13,15,21,48,49,50,51,...  (meaning unknown)
u32 flags2       only top bits used: 0, 0x80000000, 0x40000000, 0x10000000, 0xA0000000, ...
u32 propertyCount
propertyCount x { u32 tag, payload }   payload size from the tag's high byte:
   0x00 colour, 4 bytes      0x00000000 (5,282x)  0x00000001 (1,728x)  0x00000002 (229x)
   0x01 f32                  0x01000000 (1,728x, e.g. 16.2)  0x01000001 (1,520x, e.g. 0.3)  0x01000002 (1x)
   0x05 u32                  0x05000000 (10x, value 25, objects/meshes/area_200_egyedi only)
   0x06 texture stage, 28 B  0x060000ss, ss = stage slot 0..5
   0x08 name, 32 B NUL-padded 0x08000000 (every material)
```

All 4,599 non-empty v4 materials parse to their exact length (115 meshes have no material chunk).
Texture stage payload: `u32 textureIndex, i32 uvSource, u32 unknown, f32 uScale, f32 vScale, f32 uOffset, f32 vOffset`.
`unknown` is 3 in every stage but one (0). uvSource values: 0 (most), 1, 2, -2.
Scale/offset are not always 1/0: e.g. (0.25, 0.33, 1.5, 1.0) atlas cells on number plates / gauges.

Stage use (render x material, v4): slot 0 3,208, slot 1 1,946, slot 3 1,520, slot 5 605, slot 4 156, slot 2 1.
Slot 3 holds all 705 uvSource -2 stages. Of 1,520 slot-3 stages only 281 point at a real texture; 622 point at
`texture 0x00000000` and 617 at an index one past the end of the list.

**Hypotheses:** tag 0x00000000 = diffuse colour, 0x00000001 = specular colour, 0x01000000 = specular power,
0x01000001 = reflection strength; colour bytes are B,G,R,A (D3DCOLOR; only grey values were inspected so the
order is unconfirmed). Slot 3 is the reflection/environment stage; uvSource -2 = generated (sphere/cube) coordinates;
when the render leaves slot 3 unassigned the engine supplies its own environment map. Slot 1 is a decal/livery layer
over slot 0 (chassis: white base x paint colour, then `*_chassis.dds` with alpha). uv offset is applied after scale.

## v3 material — partly measured

Record lengths (after the size u32): 132 (753x), 100 (124x), 52 (30x). 855/855 meet the expected length.

```
0   f32 x4  colour (e.g. 0.349 0.341 0.325 1)        hypothesis: diffuse
16  f32 x4  colour                                    hypothesis: specular
32  f32     e.g. 13.5, 18.9                           hypothesis: specular power
36  u32     (not surveyed)
40  u16 slots, 0xFFFF = none (40..~66); u16@56 is always 64
100 char[32] name                                     only in 132-byte records ("Muffler metal", "Concrete_Asphalt")
```

The u16 at byte 40 is used as the diffuse texture index: of the materials drawn by a render entry, 384 values are
inside the render's texture list, 22 outside, 36 are 0xFFFF. Other slots (46, 48, 52, 60) are mostly 0xFFFF;
their meaning (bump / reflection / ...) is not known.

## Vertices and UVs — measured

v4 flag bits in byte order: `0x1` position 12, blend data `0x4` 4 / `0x8` 8 / `0x10` 12 / `0x20` 4, `0x40` normal 12,
`0x80` colour 4, `0x100` colour 4, `0x200` uv0 8, `0x400` uv1 8, `0x800` uv2 8. Measured on all buffers of
flags 0x3C1 (115,274 vertices: bytes 24..31 are 0xFFFFFFFF colours, UVs at 32), 0x7C1 (uv0 32, uv1 40),
0x245 (byte 12 is a weight in 0..1, normal at 16, uv at 28), 0xA41 (uv0 24, uv2 32).
Flag 0x40241 (one buffer, stride 44): 12 extra bytes, position unknown; the exporter assumes they come last.

UV origin is top-left (no V flip): a UV-over-texture overlay of the Nonus interior mesh lines up with the atlas
islands, and 787/787 of its UVs lie in [0,1]; Jack.SCX (v3) 765/765, Caynon Foliage.SCX (v3) 7,044/7,044.

v3 44-byte vertex: position 0, normal 12, uv0 24, uv1 32, u32 colour 40.
**The extra 20 bytes of 64-byte v3 vertices** (516,965 vertices): 473,764 are all zero.
Bytes 44..51 are non-zero in 35,471 vertices, all in 29 files (mostly `parts\CMS_Brakes`), and look like
uninitialised exporter memory (random floats up to 3e38). Bytes 52..59 are a float pair with small ranges
(-11.2..1.1, -0.08..16.0; non-zero in 4,182 / 7,730 vertices) — **hypothesis:** a third UV set. Bytes 60..63 are
always zero.

## Chunk types 1, 2, 3 — measured payloads, meaning hypothesised

| Type | Count | Payload (measured) | Hypothesis |
|---|---|---|---|
| 1 | 4,382 | u32, always 0 | "static mesh / no bones" marker |
| 2 | 277 | `u32 0, u32 a, i32 b`: (0,1,0) 114, (0,0,1) 86, (0,1,-1) 60, (0,0,-1) 17 | rigid binding to a bone; files are shocks/springs with `.bon` files beside them |
| 3 | 55 | `u32 0, u32 n, n x u32` bone indices (e.g. 14 indices in a 64-byte payload) | bone palette of a skinned sub-mesh (humans, MW_Mod Driver.SCX) |

## Texture decoders — measured against Pillow 12.3 / libjpeg

Pure managed code, no NuGet packages: `DdsDecoder` (DXT1/3/5 + mask formats), `PngCodec` (all PNG colour types,
Adam7, plus an RGBA writer), `JpegDecoder` (baseline Huffman, any sampling, libjpeg-style "fancy" 2x upsampling),
`TgaDecoder` (types 1/2/3/9/10/11), `BmpDecoder` (8/24/32 bit), `TextureDecoder` (dispatch + PTX colour+alpha).

| Files | Decoded | Max channel difference vs Pillow |
|---|---|---|
| 1,560 DDS (1,156 DXT1, 252 DXT5, 150 DXT3, 1 RGB16, 1 ARGB32 cube) | 1,560 | 28 exact, 1,532 within 2 (DXT rounding) |
| 624 PNG | 624 | all exact |
| 22 TGA, 10 BMP | all | all exact |
| 26 JPG files | 26 | 1 exact, 1 within 2, 24 within 8 |
| 577 JPEGs inside the 127 PTX files (339 colour 4:2:0, 238 greyscale alpha) | 577 | 107 exact, 381 within 2, 86 within 8, 3 within 18 (4x4 images) |

JPEG differences come from the float IDCT vs libjpeg's integer IDCT and edge handling of the upsampler.
101 PTX frames have no alpha JPEG (alphaLen 0) and are treated as opaque.

## Resolution survey (`slrr export-gltf --survey .`)

```
SCX files 3224, parsed 3224, drawn by >=1 render entry 1965 (of which 39 have no texture stage)
stages checked 7927 (per render x mesh): resolved+decoded 6302, slot-3 unassigned (past list or id 0) 1239,
  other index past list 220, null id (other slots) 89, entry/file missing 77, decode failed 0
SCX with stages 1926: all textures resolved 1043; all resolved except unassigned slot-3 1675
SCX with stages 1926: >=1 render resolves every stage 1055; ... except unassigned slot-3 1687
```

So **1,043 of 1,926** textured SCX files resolve every texture stage through every render entry that draws them;
**1,675** do if unassigned slot-3 (reflection) stages are accepted as engine-supplied. Remaining failures:
220 stages index past the list (the v4 ones are 197 slot-0 and 1 slot-1 stages, e.g. headlight/taillight meshes reused by `cars\fake_racers\*.rpk`
renders with empty texture lists, and LOD/catalog rims), 89 non-slot-3 `0x00000000` ids, 77 texture entries whose
source file is not shipped (e.g. `maps\textures\metal_gerenda.png`).

## glTF export (`slrr export-gltf <file.scx> <out.gltf>`)

`TexturedGltfExporter` writes one self-contained `.gltf` (buffer and PNG images base64-embedded): positions,
normals, TEXCOORD_0 (the base stage's UV channel with its scale/offset baked in), TEXCOORD_1 (other channel when
present), one material per mesh (`baseColorFactor` = diffuse colour, `baseColorTexture` = lowest non-generated
stage, `alphaMode BLEND` when the texture has alpha) and every stage with texture names in `material.extras`.
Exported and checked with the Khronos glTF-Validator 2.0.0-dev.3.10 (0 errors, 0 warnings each):
Nonus chassis + FL_door, Duhen/Baiern/Focer/MC chassis, traffic Wreck `bmw.SCX`, map content
`maps\Addon_Maps\Caynon\Meshes\Foliage.SCX` (7,044 vertices, plant texture) and `maps\skydome\meshes\skydome.SCX`
(16 render entries, all resolved), city object `objects\meshes\12x6_ketszintteraszos`, MW_Mod `Jack.SCX`.
Map terrain itself is not in standalone SCX files (map RPKs keep it in instance trees, see rpak.md), so a whole map
cannot be exported yet. Axes/handedness are still unverified; layered stages (decal over paint) are not composited.

## Open questions

- Colour byte order and the meaning of material `flags`/`flags2`, tag 0x01000001, 0x05000000, stage `unknown` = 3.
- Slots 2, 4, 5 (slot 5 is used 605 times; slot 4 often with uvSource 1, i.e. a lightmap/detail layer?).
- v3 u16 slots other than byte 40; the 0x40000 vertex flag.
- Whether stage scale/offset is applied as uv*scale+offset (assumed) or about the centre.
- Engine source of slot-3 maps when unassigned; render `flags` (e.g. 8, 1152) and `shd_*` keys.
