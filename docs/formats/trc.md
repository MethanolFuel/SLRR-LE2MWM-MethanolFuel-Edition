# `.trc` AI road graph (maps/city/ai/city.trc)

Code: `src/Slrr.Roads/TrcFile.cs` (reader + writer), CLI `roads verify|stats|dump`. All integers little-endian.
**MEASURED** = exe disassembly (StreetLegal_Redline.exe, static only) and/or a check over the whole shipped file;
**GUESSED** = meaning inferred, not traced.

## Where it comes from (MEASURED)

* The only `.trc` in the repository is `maps/city/ai/city.trc` (843,301 bytes); its siblings `city.rest` (54,818 B) and
  `city.walk` (990,441 B) are text (pedestrian/parking data, not parsed here).
* city.rpk's map root RSD: `params 0x00000010,0,0,0,0,0,0,maps\city\meshes\city.scm,maps\city\ai\city.trc`. No other shipped
  archive names a `.trc` (city2.rpk, ROC_track, test_track, the addon maps: none).
* Loader: `0x580910` (called with a pointer to the whole file in memory) memcpy's the version, then reads four counts into four
  object pools: 0x60-byte crossings (`0x57f360`), 0x78-byte crossings (`0x57fa70`), 0x78-byte roads (`0x57b060`), 0xd0-byte lanes
  (`0x579750`), builds an id table (`0x580a4d`: slot 0 = null, then the four pools in order) and calls each object's loader with
  the read cursor and the id table: crossings `0x57ba20`, roads `0x57b220`, lanes `0x579960`, curve lists `0x57f1a0`,
  curve pieces `0x57d970`.

## Layout (MEASURED: field order from the loaders, sizes from the data; the file parses to its last byte and re-serialises byte-identically)

```
u32 version (1) | u32 crossingsA (53) | u32 crossingsB (218) | u32 roads (484) | u32 lanes (1572)
crossing (A then B), exe 0x57ba20:
  u32 n | n x { u32 laneId, u8 starts }           -> +0x14 array of (ptr, u8)
  u32 m | m x turn path (69 B)                    -> +0x1c array of 0x60
  f32 pos[3] -> +0x24 | u32 tail -> +0x30
turn path: u8 outLink (+0), u8 inLink (+1), u16 code (+2), u32 0 (+0x58), i32 turn (+0x5c), piece (+4)
road (exe 0x57b220): u32 firstLane (+4) | u32 endNode (+8) | u32 startNode (+0xc) | f32 end[3] (+0x10) | f32 start[3] (+0x1c)
                     | u32 flags (+0x64) | curve list (+0x28: u32 k, k x piece; total length kept at +0x60)
lane (exe 0x579960): u32 road (+0x10) | u32 nextLane (+0x14) | u8 reverse (+0x18) | f32 end[3] (+0x1c) | f32 start[3] (+0x28)
                     | u32 endLink (+0x34) | u32 startLink (+0x38) | f32 xmin (+0x78) | f32 runXmax (+0x7c) | f32 xmax (+0x80)
                     | u32 laneIndex (+0x84) | u32 ref88, ref8c, left (+0x90), right (+0x94) | f32 speed (+0x98) | f32 (+0x9c)
                     | i32 (+0xa0) | curve list (+0x3c)
piece (57 B, exe 0x57d970): f32 p0[3], p1[3], t0[3], t1[3], f32 length (+0x30), f32 length2 (+0x34), u8 flag (+0x38)
```

Ids: one space, 0 = none. city: crossings 1..271 (A 1..53, B 54..271), roads 272..755, lanes 756..2327.

## Checks on city.trc (`roads verify maps/city/ai/city.trc`, all MEASURED)

| Fact | Count |
|---|---|
| byte-identical rewrite | yes |
| every lane id is in exactly two crossings' link lists, once with `starts` = 1, once 0 | 1572/1572 |
| lane `endLink` / `startLink` = its index in the end / start crossing's list | 1572/1572 |
| turn path runs from the end of `links[inLink]` to the start of `links[outLink]` (< 1 cm) | 3854/3854 |
| road / lane curve starts at the second vector and ends at the first | 484/484, 1572/1572 |
| road `endNode` is the crossing nearer the curve end | 484/484 |
| lane `reverse` = 1 exactly when it runs against its road | 1572/1572 (768 reversed) |
| road `firstLane` is its lowest lane id; `nextLane` chains the road's lanes (0 = last) | 484/484; 1088/1088 |
| `left` / `right` are mutual neighbours in the same direction, 3.50 m apart | 644 lanes |
| piece `length == length2` | 10,765/10,765 |
| Hermite arc length of `p0 + t0 ... p1 + t1` vs `length` | max error 0.359 m (10,760 within 0.25 m) |
| `laneIndex` (+0x84) is a permutation of 0..1571; entry i sorted by `xmin` | yes |

Other values: lanes per road 1:20, 2:192, 3:4, 4:226, 6:42; links per crossing 4..24; speed value 13.889 (1473 lanes) /
27.778 (99); +0x9c = 0.9 and +0xa0 = -1 everywhere; road flags 0 everywhere; crossing `tail` 0 except 3 crossings (two look
like floats, one is 328); turn-path code 11/12/13 (A and B), 51..53, 101..103, 201..203, 301..303; turn -1/0/1.
Totals: roads 62.6 km, lanes 221.3 km, turn paths 112.6 km; bounds x -1427..1380, z -1808..1553, y -2.1..19.2.

## Semantics

| Item | Label |
|---|---|
| piece = cubic Hermite, distance s maps to u = s / length, tangent normalised (0x57cda0); negative s wraps by the total length (0x57e960) | MEASURED |
| the x-sorted table in +0x78..+0x84 is what the nearest-lane search bisects (0x581bf0) | MEASURED use |
| speed value = speed limit in m/s (50 and 100 km/h exactly) | GUESSED |
| crossing tables A/B = plain vs signalled | GUESSED |
| turn path code: last digit pairs with turn (x1 ↔ +1, x2 ↔ -1, x3 ↔ 0 in 3,587/3,854), hundreds = signal phase group | GUESSED |
| road flags > 0 skip the road in the route builder (0x5827a9 branch) | branch MEASURED, meaning GUESSED |

## Open questions

1. The piece byte `flag` (always 1): 0x57f1a0 stores the list's end direction as the last piece's t1 when it is set and as -t0
   when it is 0 (MEASURED branch); 0x57cda0 takes another path for flag 0 (not traced). Meaning unknown.
2. Meaning of `ref88`/`ref8c` (always 0), crossing `tail`, the second crossing class, the +0x9c factor (0.9) and +0xa0 (-1).
3. The margin of the stored x extents (4-6 m wider than the control points).
