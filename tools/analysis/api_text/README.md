# Engine API specification (script layer)

What the C# engine must provide to the script VM so the shipped content runs. Phase 3 deliverable of
`docs/CSHARP_ENGINE_REWRITE_PLAN.md`. The pages are **generated** by `tools/analysis/gen_engine_api.py`
from the shipped `.class` corpus, the native tables in `docs/research/natives-*.tsv` and static facts from
`StreetLegal_Redline.exe`; the prose lives in `tools/analysis/api_text/`. Nothing was executed: every statement is
either read from data, decompiled from scripts, or read from the disassembly.

| Page | Contents |
|---|---|
| [entry-points.md](entry-points.md) | boot sequence, gametype entries (`script`/`native`), `GameRef.create`, object lifecycles, callbacks the engine calls, the cfg `slot`/`attach`/`compatible` protocol, command-string vocabulary |
| [java.lang.md](java.lang.md) | Object, Thread, Signal, String, Math, System, Vector3, Ypr, GameType, RawEdit (engine tuning variables) |
| [java.util.md](java.util.md) | `java.util.resource` natives (ResourceRef, GameRef, RenderRef, PhysicsRef, GroundRef, Animation, ParticleSystem, SfxRef), Config, Message, Vector |
| [java.io.md](java.io.md) | File, FindFile, Input, Controller, Hotkey, MouseCursor, ControlSet |
| [java.render.md](java.render.md) | GfxEngine, Viewport, Camera, Text, Osd and the dialog/gadget classes, Frontend, LoadingScreen |
| [java.sound.md](java.sound.md) | Sound, plus the car-sound natives that live elsewhere |
| [java.game.md](java.game.md) | GameLogic, Init, Vehicle, Painter, Navigator, tracks, states, catalogue of the 97 `java.game` classes |
| [java.game.parts.md](java.game.parts.md) | Part, Chassis, WheelRef, SfxTable, DynoData, part families, slot ids, the field contract the physics reads |
| [open-questions.md](open-questions.md) | prioritised list that needs dynamic traces of the original game |

## Corpus facts (measured)

{{STATS}}
## Conventions

* **Confidence tags.** `(H)` visible in shipped script code or in the disassembly; `(M)` inferred from argument
  patterns, names or neighbouring code; `(L)` name and signature only (often no script uses it). Hypotheses are
  always labelled as such; everything unlabelled in a table cell is a measurement (a count or a literal).
* **Native tables.** Columns: `Decl/Reg` (legend under the first table of each page), `Typed sites (callers)` = call sites whose
  constant-pool reference names the method with its descriptor, resolved up the class chain to the declaring class,
  with the number of distinct caller classes; `Name-only sites` = sites that only name the method (receiver type
  unknown to the compiler), counted over ALL classes, so they overstate a single class. `Arguments seen` lists the
  two most frequent literal/expression texts per argument position in typed sites.
* **Source dialect vs bytecode.** Examples use the decompiler's pseudo-Java (`tools/analysis/tufa_decomp.py`).
  Arguments are numbered `p0..pN` in declaration order (the compiled slots run backwards: slot `n` is the FIRST
  parameter, measured on `Message(int type,int cmd)` and `Vector3(x,y,z)`).
* **Resource literals.** In `.java` sources: `archive.path:0xIDr`, archive path without `.rpk`, dots instead of
  backslashes, case-insensitive: `cars:0x1000r`, `humans:0x00000001r`, `parts:0xAB5Cr`, `cars.racers.einvagen:0x113r`,
  `Maps.Addon_Maps.Drag_Track:0x00000001r` (661 literals in the 138 sources). In `.class` files they are constant-pool
  type 3 entries `(archive string such as "parts\\engines\\Baiern_Emer.rpk", id)` (3,036 literals over 69
  archives, 4,222 `RES` instructions) and evaluate to an **int** (docs/research/script-vm-opcodes.md, opcode 0E).
  The numeric composition of that int is not known (open question 1).
* **Ids of instances.** `ref.id()` returns an int too; ids are concatenated into command strings
  (`"leave " + car.id()`), stored in saves and compared with `==`. Strings are decimal.

## Regenerating

```
python3 -I tools/analysis/gen_engine_api.py .
```

Helper tools (all static): `tufa_decomp.py` (class -> pseudo-Java, call-site extraction), `rpk_script_bindings.py`
and `rpk_gametype_resolve.py` (gametype entry -> script class).
