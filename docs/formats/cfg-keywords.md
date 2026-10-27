# CFG keyword argument schemas

Companion to [cfg.md](cfg.md). Per-keyword argument counts and token types, derived from the 1,182 shipped
`.cfg` files (24,037 directive lines before `eof`, `#`-disabled lines excluded).

## Reproduce

```
python3 -I tools/schema/gen_cfg_schema.py . src/Slrr.Formats/CfgSchema/cfg-schema.json
```

The JSON is embedded in `Slrr.Formats` and loaded by `CfgArgSchemas.Default`; `ContentValidator.ValidateCfg`
(`slrr validate`) emits **CFG300** (arg count outside min..max) and **CFG301** (token shape never seen at that
position), both warnings. `CmdCfgSchema` (`cfg-schema [root] [--list]`) lists the schema or groups violations by keyword.

## Measured facts

- 83 keywords have a schema: the 85 distinct directive keywords in the corpus minus the typos `stcokpart` (x2) and
  `s` (x1) (`compound` and `endcompound` are included, `eof` stops parsing). 75 are enforced (>= 3 lines); 8 are listed
  but not enforced (`bounce`, `dest_angle`, `notify`, `rendertypes`, `sfx_loop`, `shifter`, `source_angle`, `trigger`).
- Token types: `hex` (`0x` + 1-8 hex digits), `int`, `float` (int accepted where float allowed), `vector`
  (`0,1.8,0`), `pair` (`rpm:value`), `path` (has `\`, `/` or a 2-5 char extension), `word`.
- Fixed arity (min = max) for 59 keywords, variable for 24 (`body` 1-11, `camera` 7-12, `click` 1-7, `colors` 2-17,
  `controller` 1-7, `enginepower` 4-6, `flap` 11-12, `lods` 3-4, `linked` 1-2, `maxsteer` 1-2, `name` 1-2,
  `ratios` 6-8, `render` 1-7, `steerhelp` 5-6, `type` 1-3, `material` 3-4, `deformable` 1-4, plus 6 particle keywords).
- Typical signatures: `attach hex int`; `compatible hex int`; `lod int int`; `slot` 6 floats + int id (7 args);
  `wing` int + 9 floats; `wheel` 10 floats; `stockpart`/`mesh`/`slotdmgmode` one hex; `category`/`slottype`/`flags`
  one int; `ext_camera int vec vec vec int`; `noclick`, `nocollision`, `chassis`, `buoy`, `wheelbones` take no args.
- `body` carries the only path arguments (arg 8 or 9 in 982/460 lines, collision mesh).
- Full per-keyword table: `slrr cfg-schema --list`, or read `cfg-schema.json` (lines, files, min, max, per-position types).
- `ResourceConverter/Keywords.ini` contributed nothing to the schema: it is a catalog-sorting table, not cfg grammar.

## Tuning and results

The schema trims count shapes and token types seen on < 0.2% of a keyword's lines (`OUTLIER` in the script),
treating them as defects rather than grammar. Untrimmed, the corpus has 0 violations by construction.
With the trim, **4 violations in 24,037 lines (0.017%), in 4 files**, all judged true defects:

| Code | Location | Text | Reading |
|---|---|---|---|
| CFG300 | `cars/racers/Duhen_data/scripts/_main_SunStrip_1_8_DVC.cfg:155` | `slot -0.024 0.150 0.000 0.000 0.000 0.200` | slot id missing (6 of 7 args) |
| CFG300 | `..._main_SunStrip_2_2_DVC.cfg:156` | same | same, copy of the above |
| CFG301 | `parts/scripts/Wheel.cfg:6` | `attach 0x000002000 2` | 9-digit hex, extra zero |
| CFG301 | `parts/Neons/Scripts/N2O_rail.cfg:6` | `render 0x0000000??` | unfilled placeholder |

The two known typo keywords (`stcokpart`, `s`) are still reported by CFG100, not by the schema.

**False-positive rate**: 0 of the 4 flagged lines is a schema gap on the shipped corpus. This is in-sample (the
schema was fitted to the same files), so it says nothing about modded content; the trim threshold is the only knob.

## Hypotheses (not verified against the engine)

- Retained rare shapes may also be defects that the trim keeps: `body` with 1 arg (7 lines, `frontend/scripts/mask.cfg`),
  `click` with 2 args (2), `controller` with 1 (1), `deformable` with 4 (1), `render` with 4 (21), `click` with 4 (18).
  They are consistent across several files, so treated as legitimate variants.
- Keywords with 3-10 lines (all particle keywords, `bounds`, `rendertypes`, `sfx_*`) have a min=max range from a
  handful of samples; real engine arity is probably wider (e.g. optional trailing args). Expect CFG300 noise on new content.
- Whether the engine rejects or silently ignores bad arity is unknown; hence warnings, not errors.
- Semantic meaning of positions (e.g. which `body` float is mass) is not derived here.
