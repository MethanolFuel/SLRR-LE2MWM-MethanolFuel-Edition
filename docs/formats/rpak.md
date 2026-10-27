# RPAK (`.rpk`) resource archive

All integers little-endian. Measured on all 87 shipped archives (117.8 MB of data region).

## Layout

```
offset  size  field
0       4     magic "RPAK"
4       4     version            0x200 in all 87 files
8       4     refCount
12      4     headerUnknown      0 in all files
16      64*n  references[refCount]
              u16 unknown (0)  u16 index (1-based, = i+1 in all files)  char[60] name, NUL-padded
              NOTE: bytes after the NUL are not always zero (160 slots carry stale text,
              e.g. "cars.rpk\0ge.rpk"); kept verbatim for byte-identical repacking.
..      4     tableSize          bytes of the entry table that follows
..      4     entryCount
..      4     typeOneCount       number of entries with TypeOfEntry == 1
..      4     otherCount         entryCount - typeOneCount
..      ..    entries[entryCount]
..      ..    data region, to EOF
```

Entry (23 bytes + alias, + 48 when `TypeOfEntry == 1`):

```
u32  superId            shared by many entries (10,773 repeats in 12,247): parent / "super" id
u32  id                 near-unique per archive (36 repeats): the resource id
u8   typeOfEntry        observed: 14 mesh, 5/6/7 texture-ish (text starts "sourcefile"),
                        8 script/ICFG/lod, 9 shape (.scx), 13 light ("type spot"),
                        19, 1 = instance tree (see below), 0, 2..4, 10..12, 15..18, 20 (system.rpk type table)
u8   additionalType     0, 4, 0x11, 1 seen
f32  parentCompatible   1.0 or 0.0 in inspected files (kept as raw bits)
u32  offset             absolute file offset of the entry's data
u32  size               data length (0 for folder-like entries)
u8   aliasLength        includes the trailing NUL
...  alias
[48] extra              only when typeOfEntry == 1: 6 floats (bounding box min/max, hypothesis) + 24 bytes (zero in MP1.rpk)
```

## Data region

- Entries' `[offset, offset+size)` ranges never overlap and are sorted in all shipped files.
- Text entries (most kinds other than 1) are CRLF text with `key value` lines, e.g.
  `sourcefile parts\textures\x.ptx`, `shape cars\...\FL_spring.scx`, `script java.game.parts.Part`,
  `native part parts\...\x.cfg`, `mesh 0x00000030`, `texture 0x00000031`, `skeleton humans\drivers\vin.skl`.
  Some type-8 entries are binary and start with `ICFG` (compiled cfg, not decoded yet).
- **Instance trees.** In 16 archives (maps, garage, dealer, carlot) the bytes after a TypeOfEntry 1
  entry are not covered by any entry: 116.6 MB of the 117.8 MB total data region. They hold a nested
  structure (`RSD\0` blocks, child records with bounding boxes) that contains **absolute file offsets**
  (e.g. MP1.rpk: `d0 03 00 00` = 976). The C# keeps these bytes raw ("gaps") and refuses to relocate
  archives that have them. Decoding these trees is Phase 2 work (map loading).

## Round-trip results (`slrr roundtrip .`)

- read -> write: 87/87 byte-identical
- unpack -> pack via directory (`rpak.json` + `entries/*.dat` + `gaps/*.bin`): 87/87 byte-identical
- Packing after an entry changed size works only for archives without gaps/TypeOfEntry 1 entries
  (sequential relayout); that path is covered by a unit test on a synthetic archive only.

## Open questions

- Exact meaning of `superId` vs `id` and of the high 16 bits (external-reference slot?).
- typeOfEntry / additionalType enumeration (system.rpk defines names: `typ_game_object`, `res_mesh`, ...).
- Instance-tree layout and the 48-byte extra block (24 trailing bytes are always zero?).
- `ICFG` binary cfg encoding.
