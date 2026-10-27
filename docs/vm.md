# Script VM (`src/Slrr.Vm`)

C# interpreter for the TUFA `.class` scripts (Phase 3). Opcode semantics come from
[`research/script-vm-opcodes.md`](research/script-vm-opcodes.md). This page records what was **measured** while
building and running the VM, what is **inferred** from shipped code, and what is still a **hypothesis**.

## Layout

| File | Contents |
|---|---|
| `VmClassPath.cs` | Class lookup: explicit files/dirs, then the engine package roots (`java.lang` → `system\Scripts\lang`, `java.game` → `sl\Scripts\game`, `java.game.parts` → `parts\scripts`, ...), then a one-time scan of every `.class` in the game tree. Also resolves rpk `script` lines (`system.rpk` holds e.g. `script java.util.Config` and `script system\Scripts\io\Controller.class`). |
| `VmClass.cs`, `VmCode.cs` | Runtime class model: constant pool, fields (static values, instance slots laid out base class first), methods, initializer bodies, superclass/interfaces. `VmCode` precomputes how many following instructions each instruction consumes. |
| `Vm.cs` | Loading/linking, lazy static init (superclass first), `NewObject` (instance field initializers, base first), `Construct`, `Invoke`, `InvokeVirtual`, scheduler (`Spawn`, `RunFrame(dt)`, `RunUntilIdle`) on a virtual clock. |
| `VmThread.cs`, `VmThread.Ops.cs` | The interpreter loop, operators, name paths, member resolution, dispatch. |
| `Natives.cs` | `NativeRegistry` keyed by (class, name, descriptor); `INativeModule` for other areas to add their own file; `Fallback` hook (`DefaultStub` = no-op natives for surveys). Unregistered natives throw `VmNativeMissingException`. |
| `LangNatives.cs`, `Printf.cs` | java.lang natives that need no engine: Object (7/7), String (6/6), Integer (2/2), Float (1/1), Math (4/4), Thread (11/11 incl. `getCurrentThread`), System 11/25 (log, exit ×2, currentTime, simTime, gc, runFinalization, analyze ×3, arraycopy). |
| `VmVerifier.cs` | Static stack-shape check of every body under the interpreter's semantics. |
| `tools/Slrr.Cli/CmdVm.cs` | `run-class` command (not yet wired into `Commands.cs`). |

CLI: `run-class <Class|file.class> [method[(desc)]|<init>] [args...] [--root dir] [--cp file|dir] [--trace]`,
plus `--verify`, `--survey [--deep] [--stub] [--list]` and `--natives [root] [tsv]`.

## Measured results (shipped corpus in this repository)

* **Stack-shape check:** 7,033/7,033 bodies of 1,692 files pass (4,373 methods, 2,660 field initializers, 405 empty
  bodies). Depth is consistent at every join, every CALL/CALLINIT/THISINIT/SUPERINIT/CALLBYNAME/SUPERCALL pops exactly
  the ARGC that was pushed, every assignment and `++/--` target is a Variable, RETVAL/ENDINIT see depth 1 and RETURN
  depth 0. Instructions: 255,172 reachable, 119,091 consumed by the preceding instruction, 13,714 dead (block-end
  drops after returns): 387,977 in total. No literal opcode (09–0E) is ever reached outside `07 1C`.
* **Argument slots are reversed** (also found independently by the decompiler agent on 136 pairs): after `this`, the
  *last* argument is in the lowest slot. `String.timeToString(FI)` reads the int flags from slot 0 and the float time
  from slot 1; `Tyre.SetInflation(FI)` (source available) tests `inflation != I` with slot 2 and `forced` with slot 1.
* **String conversion** (`JVM::stringConversion`, 0x417da0): ints use `"%d"`, floats `"%.3f"` (format strings read
  from the exe). `String.timeToString(3725.5)` returns `"01:02:05.500"`.
* **Constructor lookup walks up the hierarchy.** Static check of the 1,757 `this(...)`/`super(...)` sites in the 1,685 distinct classes (ARGC directly before; a
  first-found file per class name): THISINIT 32 resolve in the class itself, 1 one level up (`InventoryItem_Part` → `InventoryItem`);
  SUPERINIT 1,670 resolve in the direct superclass, 54 only further up (e.g. `FileRequesterDialog` calls a 5-argument
  `super(...)` that only `TextDialog`, two levels up, declares). Overloads are picked by runtime argument types;
  a candidate whose parameter cannot take the argument (object vs number) is skipped, so `new RenderRef(resRef)`
  reaches `ResourceRef(ResourceRef)`.
* **`arr.length`** compiles to a FIELD ref on the pseudo class `"[elem"` (e.g. `[java.lang.Object.length:I`, Roc).
* **Survey** (`run-class --survey`, a fresh VM per class/method, 2M-instruction budget, 1,685 distinct class names):
  * static initialization: 1,618 ok, 67 stop at `ResourceRef.newNative()V` (engine native);
  * every scripted method with default arguments (0 / 0.0 / null), lang natives only: 54 constructors, 96 instance
    and 19 static methods complete; 3,205 runs stop at `ResourceRef.newNative`;
  * same with every missing native stubbed (`--stub`): static init 1,685/1,685; constructors 1,716/1,759, instance
    methods 1,126/2,053, static methods 47/79 complete. All remaining failures were reviewed: null dereferences
    caused by the null arguments/stub results, `System.exit`, calls into a `Thread.sleep` inside a synchronous call,
    one class missing from the corpus (`java.game.MotionBlur`), and stale-code paths (`Scene` has no `show()`;
    `Block.getCylinderHead()` exists only in subclasses). No opcode or internal VM error remains.
* **Natives:** 358 distinct native methods are declared in class files; 335 of them are in the exe's 360
  registrations; 23 are declared but not registered by the exe (RawEdit ×5, `System.netHost/netJoin/netLeave`,
  `Thread.getCurrentThread`, ...). The C# registry covers 41 of the exe's 360 (all engine-independent java.lang ones).
* **DeadIslandTrack** (`MW_Mod/ForModders.rar`, extracted outside the repo; test `DeadIslandTrackFromForModders`
  runs with `SLRR_DEADISLAND_CLASS`): all 9 bodies verify; static init stops at `ResourceRef.newNative` with the trace
  ending `DeadIslandTrack.<MapImage initializer> line 20`, the source line of that declaration. With stubs,
  `MapName = "Dead Island"`, `TS_Version = 1.0`, and the constructor sets `posStart = (30, 5, -30)`.
* Running the shipped code exposed a script bug that the VM reproduces: `String.timeToAlphaString(3725.5, 0)` gives
  `"1 h 2 min 2 sec"` (the short form appends the minutes local where the seconds belong).

## Inferred from shipped code (behaviour required for shipped scripts to work)

* `null + x` concatenates (null → `""`): `timeToString` does `s += h` on a still-null String local. The engine's
  values carry their static type; ours do not, so a null operand of `+` is taken to be a String.
* `!obj` is a null test (`InventoryItem_Part.getPart`: `!partXXX`), and `&&`/`||` accept objects
  (`timeToAlphaString`: `(flags & X) && s`).
* Assignment converts float↔int to the variable's declared type (`timeToString`: `int h = t / 3600` with float `t`).
* `08 22` is a no-op and SUPERINIT/THISINIT push nothing. The alternative (they push, `08 22` pops) has the same
  stack effect and cannot be told apart statically.
* `CALL` path components: `19 CLASS` (static context), `01 LOCAL`, `1B FIELD`, `12 MNAME` (by-name member; also used
  as a by-name *field* inside `NAME`, e.g. `tire.wheel_radius` in Catalog). The last component of a CALL path is
  `1A METHOD` or `12 MNAME`.
* Method flags (decompiler agent, 136 pairs): 0x1 public, 0x2 protected, 0x4 private, 0x8 static, 0x10 abstract,
  0x20 final, 0x40 native, 0x80 synchronized; CLSS word 1 is the interface flag.

## Hypotheses / not confirmed

* Opcodes never used by the corpus (0F, 1D SUPERFIELD, 1E ARRAYINIT, 1F PUSHZEROS, 2E LOCALNAME) and operator
  0x0C (`>>>`): 0F and 2E are no-ops, 1E/1F follow the handler descriptions, 1D and `>>>` raise
  `VmOpcodeException`; `<<=`, `>>=`, `>>>=` raise "unsupported assignmenttype" like the engine. Note:
  `Slrr.Formats` `TufaOpcodes` lacks 0F/1D/1E/1F/2E, so a class using them would not even load (operand sizes from
  the typeword table: 0F, 1E, 1F int; 1D none; 2E const).
* Opcode 0D: int literal (the decompiler identifies it as the boolean literal; only 0/1 occur).
* Field initializers run at allocation (instance) and at first use of the class (static), not as engine threads.
* Object `==` compares references (no string contents); float division by zero gives IEEE inf/NaN (int division by
  zero is an error, as in the engine); int `MIN / -1` wraps.
* Natives: `Object.toString` format (`Class@id`), `String.append(I)` appends the character code, `String.token`
  is strtok-like and returns null past the last token, `Integer.toHexString` is lowercase, `Float.toString(f, null)`
  uses `%.3f`, `Thread.start` runs `target.run()` (`target` is `this` for plain Thread subclasses), Thread priorities
  are stored only, `currentTime`/`simTime` follow the VM's virtual clock, `Math.random` is a seeded .NET RNG.
* Resource literals (`0E`) push `Vm.ResolveResource(archive, id)`; the default returns the archive-local id. The
  engine's mapping to runtime ids (0x406660) is not reconstructed.
* Scheduler: round-robin slices of 20,000 instructions; blocking natives (`sleep`, `wait`, `suspend`) are only allowed
  on scheduled threads, not inside synchronous calls (field initializers, `Vm.Invoke`, native callbacks).

## Behaviours added after the first headless play-through (inferred, not read from the exe)

Found by the game host (`docs/phase4/game.md`) while walking main menu -> garage -> dealer. Each is covered by a test in
`tests/Slrr.Vm.Tests/InterpreterTests.cs`. All are **guessed** from how shipped scripts use the operation; none was
confirmed in the engine's interpreter.

| Behaviour | Evidence |
|---|---|
| `&&` / `||` accept float operands (non-zero is true); only `void` is rejected | `Block_Inline.updatevariables` line 137 |
| `x instanceof T` on a number is `false`, not an error | `TrackSelect.exit` |
| A number assigned to a `String` variable becomes its text (`%d` / `%.3f`, as for `+`) | garage clock thread |
| A non-void method that ends without `return` yields the default of its type (0, 0.0, null) | garage loading screen waits on such a method |
| String literals are interned: equal literals are the same object | `==` against literals; tyre names showed `<unnamed>` |
| A number passed for a `String` parameter becomes its text | `dayTxt.changeText( day )` in `GameLogic.run` |
