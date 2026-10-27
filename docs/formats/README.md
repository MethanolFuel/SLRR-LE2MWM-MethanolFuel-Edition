# SLRR file formats (Phase 1)

Specs derived from the shipped content of this repository by reverse engineering (allowed by the
owner's decision in `docs/CSHARP_ENGINE_REWRITE_PLAN.md`, section 1). Each page separates
**measured** facts (checked against every shipped file) from **hypotheses** (names and meanings
not yet confirmed against the engine).

| Format | Spec | C# | Shipped files | Parsed | Notes |
|---|---|---|---|---|---|
| RPAK `.rpk` | [rpak.md](rpak.md) | `Slrr.Formats.Rpak` | 87 | 87 | read->write and unpack->pack byte-identical for 87/87 |
| CFG `.cfg` | [cfg.md](cfg.md) | `Slrr.Formats.Cfg` | 1,182 | 1,182 with 0 errors | 18 files with warnings |
| SCX `.scx` | [scx.md](scx.md) | `Slrr.Formats.Scx` | 3,224 (v3 542, v4 2,682) | 3,224 | 100% byte coverage, 5,569 meshes, glTF export |
| PTX `.ptx` | [ptx.md](ptx.md) | `Slrr.Formats.Ptx` | 127 | 127 | 339 frames |
| BON/SKL | [bon-skl.md](bon-skl.md) | `Slrr.Formats.Bones` | 133 | 133 | 1,092 bones |
| TUFA `.class` | [tufa.md](tufa.md) | `Slrr.Formats.Tufa` | 1,692 (1,760 classes) | 1,692 | 387,977 instructions, 40 opcodes, sizes measured, semantics open |

Numbers were produced by `slrr coverage .` and `slrr roundtrip .` on 2026-10-27, built with the
.NET 8.0.132 SDK on Linux x64. Not yet covered: `.dds/.tga/.png` (standard), `.tex` (a short
text file naming a texture, e.g. `red_disc.tga`), `.wav` (standard), `.scm` (not surveyed).

## Tooling

```
dotnet build Slrr.sln -c Release
dotnet test Slrr.sln -c Release          # 24 tests; corpus tests find the game root automatically
tools/Slrr.Cli/bin/x64/Release/net8.0/slrr <command>
```

| Command | Purpose |
|---|---|
| `unpack <a.rpk> <dir>` / `pack <dir> <a.rpk>` | entries as files + `rpak.json` manifest; repack is byte-identical when nothing changed |
| `rpk-info <a.rpk>` | references, entry table, uncovered byte ranges |
| `roundtrip [root]` | read->write and unpack->pack of every `.rpk`, compares bytes |
| `validate [root] [--all]` | cfg grammar, dangling file refs, duplicate ids, rpk checks |
| `dump-class <f.class> [--no-code]` | TUFA constant pool, fields, methods, code |
| `dump-scx <f.scx> [--gltf out.gltf]` | mesh/chunk listing and glTF 2.0 geometry export |
| `coverage [root]` | parse every rpk/cfg/scx/ptx/bon/skl/class and report counts |

## `validate` result on the shipped content

No errors. Warnings and info items (real findings in the content, not parser failures):

| Code | Count | Meaning |
|---|---|---|
| CFG013 | 15 | cfg without `eof` terminator |
| CFG100 | 3 | unknown keyword: `stcokpart` twice (MC_data), a stray `s` |
| CFG101 | 1 | malformed hex id `0x0000000??` (parts/Neons/Scripts/N2O_rail.cfg) |
| CFG210 | 69 (info) | repeated `stockpart` id; normal for wheels (4 x the same id) |
| CFG212 | 7 (info) | repeated identical `compatible` line |
| RPK200 | 728 | text entry names a file missing on disk: 709 `sourcefile` (mostly source textures not shipped), 18 `shape`, 1 `native` |
| RPK210 | 36 | entry id repeated inside one archive (parts.rpk, cars.rpk, DamageTrack.rpk) |

10,116 file references were checked.
