# Plan: full C# (.NET 8, x64) rewrite of the SLRR engine

Status: proposal. Nothing here has been started.

## 0. Ground truth

- No engine source is in this repo. The game is `StreetLegal_Redline.exe`
  (PE32 / i386, 2.4 MB, "SL2 build 606, distr. v2.2.1"). A rewrite means
  re-implementing the engine from its observable behaviour and file formats.
- The content is a mod: ~9,400 files, 479 MB. Formats seen in the repo:

| Ext | Magic / shape | Role | Count |
|---|---|---|---|
| `.rpk` | `RPAK`, version, entry count, name table | resource archive | 87 |
| `.class` | `TUFA`, `CONS` constant pool, `java.game.*` names | compiled script bytecode | 1,692 |
| `.java` | game dialect (`parts:0xAB5C` resource literals, `humans:0x1r`) | script source | 138 |
| `.scx` / `.SCX` | `INVO` chunked | mesh / scene | ~3,200 |
| `.bon`, `.skl` | text skeleton (`Bip01 Spine`) | rig | 115+ |
| `.ptx` | 28-byte header + embedded JPEG | texture (particles/UI) | 127 |
| `.dds`, `.tga`, `.png`, `.tex` | standard / unknown | textures | ~2,200 |
| `.cfg` | line-based DSL ending in `eof`, `compound`/`endcompound` | parts, cars, physics, render | 1,182 |
| `.wav` | standard | audio | 156 |
| `.ini`, `.txt`, `.fx`, `.h` | ENB / SweetFX / injector settings | post-processing | - |

- The runtime scripting API is `java.lang.*`, `java.game.*`, `java.util.*`,
  `java.io.*`, `java.render.*`, `java.sound.*`. The stock classes live in
  `system.rpk` and `system/Scripts/*` as `.class` bytecode only.
  `lang/Native.class` is the bridge to engine internals.
- Existing prior art in the repo: `ResourceConverter/` (RPK creator, resconvert,
  resdecode, SLRReditor2), `Catalog_SuperIDs.txt`, `Keywords.ini`,
  `Supers.ini`, `StreetLegalDevKit.exe`, `MW_Mod/ForModders.rar`. These are
  the best specification we have for the formats and the keyword set.

## 1. Legal and scope gate (week 0, blocking)

1. Written decision on IP: clean-room approach vs. permission from the rights
   holders. Do not copy engine code or decompile the exe into the new codebase.
2. Decide the compatibility target: **must** load unmodified `.rpk`/`.cfg`/
   `.scx`/`.class` content from this build (recommended), or a new content format.
3. Third-party mods (Miran Wichur Mod, RevPhysics, ExhaustiveBits, ENB,
   SweetFX) have their own terms. The ENB / SweetFX / `*.asi` hooks are
   32-bit D3D injectors; they are replaced by native post-processing in the
   new renderer, not ported.

## 2. Architecture

.NET 8, C# 12, `AnyCPU`/x64 only. Solution layout:

```
Slrr.sln
  src/Slrr.Core        math, handles, resource IDs (rpk:0xID), logging, DI
  src/Slrr.Formats     RPAK, SCX, PTX, CFG, BON/SKL, TUFA parsers (+ writers)
  src/Slrr.Vm          TUFA bytecode VM and java.* class library shim
  src/Slrr.Physics     rigid body, tyre, suspension, drivetrain, engine model
  src/Slrr.Render      graphics abstraction + backend
  src/Slrr.Audio       sound, engine-sound synthesis, 3D positioning
  src/Slrr.Game        GameLogic, catalog, garage, save/load, races, AI
  src/Slrr.App         window, input, main loop
  tools/Slrr.Cli       pack / unpack / validate / convert / disasm
  tests/*              unit, golden-file, replay and perf tests
```

Key choices (decide in week 1, spike before locking):
- **Rendering backend**: Silk.NET (Vulkan/D3D11, thin) vs. Stride vs. MonoGame.
  Recommendation: Silk.NET + own renderer. It gives x64 and no engine
  imposing its own scene model, and the content is mesh + material based.
- **Script strategy** (biggest decision):
  - (a) Implement a TUFA bytecode VM and run the existing `.class` files
    unmodified. This keeps all 1,692 compiled scripts and every other mod
    working. Requires the full `java.*` API (including `Native`).
  - (b) Transpile the dialect to C# and drop the VM. Only possible for the
    138 source files; the other ~1,550 classes have no source. Rejected as the
    primary path, but a decompiler can help migrate later.
  - Recommendation: (a), with the stock `java.*` library reimplemented in C#.
- **Physics**: custom fixed-timestep vehicle simulation, because the mod
  physics (RevPhysics, CMS brakes, shocks, swaybars, engine curves) is
  expressed in `.cfg` and script parameters that must keep their semantics.
  Use BepuPhysics for generic collision / static world only.
- **Determinism**: fixed 120 Hz sim step, seeded RNG, so replay tests work.

## 3. Phases

Each phase ends with a demo and automated tests; nothing starts before the
previous one is accepted.

### Phase 1: Formats and tooling (6-8 weeks)
- `Slrr.Formats`: RPAK read/write, CFG parser (full grammar derived from
  `Keywords.ini` + the 1,182 files), SCX, PTX, BON/SKL, TUFA reader.
- Round-trip tests: unpack then repack is byte-identical for all 87 RPKs; every
  `.cfg` parses with zero errors.
- `Slrr.Cli`: `unpack`, `pack`, `validate` (dangling file refs, duplicate IDs,
  broken `compound` nesting), `dump-class`, `dump-scx`.
- **Exit**: all repo content loads; a viewer dumps any mesh to glTF.

### Phase 2: Asset viewer and renderer foundation (6-8 weeks)
- Window, input, camera, mesh/material/texture pipeline, skeletal animation.
- Scene viewer: load a map `.scx`, a car (`compound` render parts) and a human.
- Reimplement fixed-function effects the game relied on (lighting, fog,
  alpha, env-map) as shaders; native tonemap/bloom/SMAA replace ENB/SweetFX.
- **Exit**: render a car in a garage and a map with correct textures/LODs.

### Phase 3: Script VM (8-12 weeks, highest risk)
- Disassembler for TUFA (constant pool `CONS`, method tables, opcodes). Use
  the 138 `.java` sources against their compiled `.class` twins to infer the
  opcode set empirically; use `StreetLegalDevKit.exe` and `ForModders.rar`
  as references.
- Interpreter first, then a JIT/AOT-to-IL backend if profiling needs it.
- Reimplement `java.lang`, `java.util`, `java.io`, `java.game`, `java.render`,
  `java.sound` and `Native`, driven by the API surface used by shipped scripts
  (derive via a call-graph scan of all 1,692 classes).
- **Exit**: `GameLogic`, `Catalog` and `OptionsDialog` classes run headless
  with a stub engine and produce the same state transitions as the original
  in recorded traces.

### Phase 4: Physics and vehicles (10-14 weeks)
- Parts model (`parts/`, `cars/` configs), slot/attach system, engine torque
  curves, gearbox, differential, clutch, tyre model, suspension, brakes, aero.
- Validation against the original game: capture reference runs (0-100 times,
  top speeds, braking distance, skidpad grip) for 20+ cars and tune until
  within an agreed tolerance (proposal: 3%).
- **Exit**: drive a car around a test track with parity numbers.

### Phase 5: Game systems (10-12 weeks)
- Garage, parts catalog, dealer, save/load (`SAVEFILEVERSION` format), money,
  clubs/racers, races, traffic and AI, police/damage, camera modes, UI/OSD
  (options, catalog menus, resolution handling natively, no changer exe).
- Port the logic through the VM where scripts own it; native code only for
  engine services.
- **Exit**: full career loop playable end-to-end on the stock content.

### Phase 6: Audio, polish, compatibility (6-8 weeks)
- Engine-sound system and `.wav` banks, positional audio, music.
- Controller/wheel/force-feedback support.
- Mod compatibility sweep: load every third-party car/part RPK listed in
  `res_ban.log`/community packs, fix loader gaps.
- Performance and memory targets; Windows x64 first, Linux x64 as stretch.

### Phase 7: Release (4 weeks)
- Installer, save migration from the 32-bit game, docs for modders, public
  API for scripts, beta test, bug-fix freeze.

Total: roughly 12-16 months with a team of 4-6 (graphics, physics, VM/tools,
gameplay, QA, content). One engineer alone: 3+ years.

## 4. Testing and acceptance

- Golden-file tests per format; fuzzing of all parsers.
- Replay harness: scripted input -> state hash per frame, compared with
  checked-in goldens (new engine vs. itself) and with recorded original
  traces (new engine vs. original).
- Visual regression: screenshot diffs of 30 reference scenes.
- Physics parity suite (Phase 4) is a release gate.
- CI: GitHub Actions on Windows and Linux, `dotnet test`, format + analyzers.

## 5. Risks

| Risk | Impact | Mitigation |
|---|---|---|
| IP / licensing of engine and third-party mods | stop-ship | Legal gate before Phase 1; clean-room rules |
| Unknown TUFA opcodes / `Native` semantics | VM blocked | Source/bytecode pairs, trace capture from the original exe in a VM |
| Physics feel differs from original | players reject | Parity suite and tolerance gates |
| Undocumented `.scx` chunk types | assets fail to render | Chunk-coverage report in Phase 1; fall back to skip + warn |
| Scope creep (new features) | schedule | Parity first, features after Phase 7 |
| Mod ecosystem breakage | adoption | Compatibility sweep in Phase 6 |

## 6. Immediate next steps (first 2 weeks)

1. Legal sign-off on clean-room approach.
2. Create the .NET solution and CI; commit this plan.
3. Implement the RPAK reader and `validate` command; report coverage.
4. Parse all 1,182 `.cfg` files; publish the grammar.
5. Spike: decode one `.class` constant pool and one simple method; write up
   the opcode findings.
6. Spike: render one `.scx` mesh with its `.dds` textures.
