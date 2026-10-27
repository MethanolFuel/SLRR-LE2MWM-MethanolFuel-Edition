# Script VM: TUFA `TREE` opcode semantics

Phase 2 static research. Sources: static disassembly of `StreetLegal_Redline.exe` (PE32 i386, MSVC 6,
timestamp 0x420765d4); the shipped `.class` corpus (1,692 files); and one exact source/class twin,
`DeadIslandTrack.java` / `DeadIslandTrack.class` from `MW_Mod/ForModders.rar` (all source lines match the TREE
line numbers). **Nothing was executed.** Scripts: `docs/research/scripts/` (see the end of this document).

Confidence levels: **H** = read directly from the handler code and consistent with the corpus. **M** = handler
read, meaning inferred from its calls, strings or corpus context. **L** = only partly read, or unused in the corpus.

## 1. What a TREE body really is (measured)

* The engine calls the code a **JTree** (string `JTree::ioWritePure: not a runtime opcode`). Before saving it
  flattens the tree into a "pure", runtime-only linear form. That form is what `.class` files store.
* **Opcode byte → 16-bit type word**: table at VA `0x5f0914` (47 × u16, read by the loader at `0x41a5f0`).
  The high nibble gives the operand kind: `0x1000` int, `0x2000` float, `0x4000` 32-bit constant-pool index.
  Opcodes ≥ 0x2F make `ioWritePure` (0x41a930) fail. The table gives the operand sizes that Phase 1 solved from the
  corpus, and adds three opcodes with operands that the corpus never uses (0F, 1E, 1F).

  ```
  op: 00 01   02   03   04 05 06   07   08   09   0A   0B   0C 0D   0E   0F   10 11   12   13 14   15   16   17
  tw: 0  1001 1002 1003 4  5  1006 1007 1008 4009 200A 100B C  100D 100E 100F 10 1011 4012 13 1014 1015 1016 1017
  op: 18 19   1A   1B   1C   1D 1E   1F   20 21 22 23 24 25   26   27   28 29 2A 2B 2C 2D 2E
  tw: 18 1019 101A 101B 401C 1D 101E 101F 20 21 22 23 24 4025 4026 1027 28 29 2A 2B 2C 2D 402E
  ```
* **In memory** each instruction is 8 bytes `{u16 typeword, u16 line, u32 operand}`. The interpreter
  `Thread::run` (`0x420ff0`) switches on the full type word through a compare tree and byte-indexed jump tables
  (`0x4239b0`/`0x42396c`, `0x423ac4`/`0x423ab0`, `0x423b34`). After a handler, the default path (`0x4238ed`) does
  `ip += 8`, checks the time slice (`Thread::run(float budget)`) and the suspend flags, then loops.
* The code is **postfix stack code with prefix markers**. Operands come before operators. Some instructions
  read the *following* instruction and skip it: `OP lit` before a literal, `OP new`/`newarray`/`instanceof`
  before a `TYPE`, `OP cast` before a `CLASS`, `NAME n`/`CALL` before an n-component name path, and `DECLLOCAL`
  before `[LOCALNAME] TYPE [DECLUSE]`. When the main loop reaches one of those consumed instructions it is a
  no-op (it resolves to the default handler).
* Each **operand-stack** entry is polymorphic. `vtbl[0]()` returns 2 for a *Variable* (an lvalue: local, field,
  array element). Anything else is a *Value* `{vtbl 0x5e734c, type*, data}`, allocated from the pool at
  `[0x62f130]`. Popping an rvalue copies a Variable into a Value (`0x423e30`). The engine reports
  `stack access: value on top (instead of variable)` when an lvalue was expected.
* There are only **four primitive VM types**: two int types `"I"` (globals `0x62e00c` and `0x62def0`), `"F"`
  (`0x62deec`), object `"L;"` (`0x62e000`, also used for `null`) and `"V"`. Booleans and chars are ints; there is
  no long, double or byte.
* Thread layout (M): `+0x20` current frame, `+0x24` ip, `+0x2c` state flags (`0x04` running, `0x40` finished).
  Frame: `+0x18` locals container, `+0x28` operand stack (`T_Container {cap, data*, count}`), `+0x2c` saved
  return ip, `+0x30` this instance, `+0x34` class. The class has its constant pool at `+0x14` and its superclass
  at `+0x1c8`.
* Jump operands are **relative to the jump instruction itself, in instructions** (`ip += n*8`, which skips the
  generic `+8`). Example: `15 JMPF +24` at index 25 goes to index 49.

### Bodies, fields and methods (measured; resolves Phase 1 open questions)

* The **2,660 "unreferenced" bodies are field initializers**. Each one ends with `10 ENDINIT`, which sets the
  thread "finished" flag and leaves the value on the stack. The engine runs them as threads named
  `THRD-CLASSVAR-INI` (`<static field init>`) and `THRD-INSTVAR-INI` (`<instance field init>`). The **FILD
  `value` column is the body index** of the initializer, or `0xFFFFFFFF` for none. DeadIslandTrack checks this:
  `MapName = 0` → body 0 `"Dead Island"`, `TS_Version = 4` → body 4 `1.0f`, `MapImage = 3` → body 3
  `new ResourceRef(…:0xDC2)`.
* MTHD flags follow Java access flags: `0x1` public, `0x8` static, `0x40` **native** (e.g. `SfxTable` methods
  are `0x41` with `slots = 0`). Native methods have no code; they are looked up by `class.name` in the native
  registry (`Thread::callmethod: native implementation missing for "%s.%s"`).
* The trailing `03 n` runs are `DROPLOCAL`s. A method epilogue is `PUSH0|RVALUE`, then a drop of each live
  local, then `RETVAL`, then the block-end drops (unreachable after a return).
  Counts check this: PUSH0 1,587 + RVALUE 912 = RETVAL 2,499. RETURN 1,839 = SUPERINIT 1,805 + THISINIT 34
  (constructors). DECLUSE 2,075 = ASSIGNINIT 2,075.

## 2. Opcode table

Stack notation: `[… a b] → [… r]`, top on the right. "next" = the following instruction, which is consumed.

| Op | Name (new) | Operand | Stack effect / semantics | Handler | Conf. | Uses |
|---|---|---|---|---|---|---|
| 00 | INVALID | – | `fatal script error` | 0x421739 | H | 0 |
| 01 | LOCAL | slot | push the local-variable Variable `locals[slot]` (slot 0 = `this` in instance methods, then arguments) | 0x421714 | H | 22,386 |
| 02 | DECLLOCAL | slot | create a local: optional next `2E LOCALNAME`, then next `06 TYPE` gives its type. Appended to `locals`. If the instruction after that is `2C DECLUSE`, also push the new Variable | 0x421752 | H | 2,658 |
| 03 | DROPLOCAL | slot | pop the last local from `locals` and destroy it (scope end; the operand is informational) | 0x42182e | H | 20,883 |
| 04 | SKIPIF_T | – | `[… v] → [… v]`; if `v` is true, skip the next instruction (always a `14 JMP`). Short-circuit left side of `&&` | 0x42116c | H | 424 (= `&&` count) |
| 05 | SKIPIF_F | – | same, skip if false. Left side of `\|\|` | 0x4211e0 | H | 181 (= `\|\|` count) |
| 06 | TYPE | CONS idx of a type descriptor (`I`, `Ljava.lang.Vector3;`, `[Ljava.game.Vehicle;`) | runtime no-op; read by 02, `OP new`, `OP newarray`, `OP instanceof` | default | H | 6,750 |
| 07 | OP | operator code (§3) | apply operator and **push** the result | 0x4218a5 | H | 73,649 |
| 08 | OPDISCARD | operator code (§3) | apply operator and **discard** the result (statement form). `08 1B` = expression statement, `08 22` = void (after constructor calls) | 0x422eda | H | 37,830 |
| 09 | STR | CONS string idx | literal, read by the preceding `07 1C` (creates a String instance, `0x4173d0`) | default / 0x421a50 | H | 8,256 |
| 0A | FLOAT | raw IEEE-754 float bits | float literal (read by `07 1C`) | 0x421a70 | H | 18,040 |
| 0B | INT | int32 | int literal, type `I` (0x62e00c) | 0x421948 | H | 14,912 |
| 0C | NULL | – | `null` literal (object type `L;`) | 0x42199b | H | 1,511 |
| 0D | INT2 | int32 | literal with the second int type (0x62def0). Only values 0/1 occur, so probably `true`/`false` | 0x4218f5 | M | 4 |
| 0E | RES | CONS resource idx | resource literal `pkg:0xIDr`. Pushed as an **int**, the resolved resource id (`0x406660`) | 0x4219e0 | H | 4,222 |
| 0F | – | int | no handler (no-op); unused | default | L | 0 |
| 10 | ENDINIT | – | end of a field-initializer body: stop the thread, result stays on the stack | 0x423932 | H | 2,660 |
| 11 | NAME | n components | evaluate the next n instructions as a dotted name and push the resulting Variable (`Thread::evalName`, 0x420d20). Component 1 may be `19 CLASS` (static context), otherwise it is implicit `this`/class. Later components are `01 LOCAL`, `1B FIELD` or by-name ones. Errors: `classname found alone`, `field access on primitive variable` | 0x422e98 | H | 21,629 |
| 12 | MNAME | CONS string idx | method name *by string* (unresolved; dynamic lookup) as the last component of a CALL path | consumed | H | 5,622 |
| 13 | INVALID | – | `fatal script error` | 0x421739 | H | 0 |
| 14 | JMP | rel. instr. offset | `ip += off` | 0x423648 | H | 3,149 |
| 15 | JMPF | rel. offset | pop v; jump if false (`if`, `while`, `for`) | 0x423239 | H | 5,793 |
| 16 | JMPT | rel. offset | pop v; jump if true (only negative offsets: `do … while`) | 0x423267 | H | 3 |
| 17 | CASE | rel. offset | `[… sw case] →` if `case == sw` (via the `==` routine 0x41c5c0) drop both and jump, else `[… sw]` | 0x423608 | H | 83 |
| 18 | POP | – | pop and destroy the top (e.g. the switch value before `break`) | 0x4216bb | H | 7 |
| 19 | CLASS | CONS class idx | class reference, consumed by NAME/CALL paths and `OP cast` | consumed | H | 5,681 |
| 1A | METHOD | CONS member ref | resolved method, last component of a CALL path | consumed | H | 13,682 |
| 1B | FIELD | CONS member ref | push the Variable for a field. Standalone it means `this.f` or a static `C.f` (`instance field access from static method`); inside NAME it is applied to the previous component | 0x4234b4 | H | 49,637 |
| 1C | FIELDBYNAME | CONS string idx | `[… obj] → [… obj.<name>]`, dynamic lookup by name (`unknown fieldname: %s.%s`, `null.%s`) | 0x4237ce | H | 573 |
| 1D | SUPERFIELD | (none in the typeword) | `super.<name>` field lookup (`unknown fieldname: super.%s`). Unused | 0x4214bc | M | 0 |
| 1E | ARRAYINIT | n | `[… var v1 … vn] → [… var arr]`: build an array of n elements from the element type of `var` (array initializer `{…}`). Unused | 0x4232bb | M | 0 |
| 1F | PUSHZEROS | n | push n int zeros. Unused | 0x423585 | M | 0 |
| 20 | INDEX | – | `[… index array] → [… array[index]]` (Variable). Errors on non-array, negative index or non-int index. Note: index is pushed **before** array | 0x421398 | H | 4,137 |
| 21 | CALLINIT | – | `[… obj a1..an n] → [… obj]`: call `<init>` on `obj`, found at depth n+2 (after `07 1E new`) | 0x421254 | H | 3,290 |
| 22 | THISINIT | – | `this(...)` constructor call (0x408a70) | 0x4212c7 | M | 34 |
| 23 | SUPERINIT | – | `super(...)` constructor call on the superclass (0x408a90, class `+0x1c8`) | 0x4212a4 | H | 1,805 |
| 24 | CALL | – | `[… a1..an n] → [… ret]`: the name path that follows (optional `11 NAME k`, then components, ending with `1A METHOD` or `12 MNAME`) gives target and method (`0x4208e0`). Calls a script method (`0x41fbc0`) or a native. Errors: `script error`, `instance missing` | 0x4212e4 | H | 19,299 |
| 25 | CALLBYNAME | CONS string idx | `[… a1..an n obj] → [… ret]`: call method `<name>` on the popped object (`[illegal methodcall] null.%s()`) | 0x423725 | H | 1,283 |
| 26 | SUPERCALL | CONS string idx | `super.<name>(args)` on `this` (0x4207c0 with the superclass) | 0x4236ee | H | 603 |
| 27 | ARGC | n | push int n (argument count; always just before 21/23/24/25/26) | 0x423677 | H | 26,314 |
| 28 | RVALUE | – | pop (dereferencing a Variable) and push the Value; used before `return expr` | 0x42164e | H | 912 |
| 29 | PUSH0 | – | push int 0 (implicit return value of `void` methods) | 0x421583 | H | 1,587 |
| 2A | RETURN | – | destroy the frame, restore the caller ip; if none, the thread finishes (constructors) | 0x421697 | H | 1,839 |
| 2B | RETVAL | – | pop the return value, destroy the frame, restore the caller ip, push the value onto the caller's stack | 0x421662 | H | 2,499 |
| 2C | DECLUSE | – | marker read by 02 (push the declared variable for its initializer) | consumed | H | 2,075 |
| 2D | ASSIGNINIT | – | `[… var value] → […]`: `var = value` (operator 0x23, `0x41acb0`); the local-initializer form | 0x4210ec | H | 2,075 |
| 2E | LOCALNAME | CONS string idx | debug name of a local, read by 02. Not present in shipped classes | consumed | H | 0 |

Compiler bytes that are not runtime opcodes: in-memory JTree nodes use types up to at least `0x4d` (`0x42`,
`0x48`, `0x4a`, `0x4d` all go to `fatal script error`). `ioWritePure` refuses to save them.

## 3. Operator codes for `07 OP` / `08 OPDISCARD`

The same enum feeds the `07` sub-switch (`0x423a80`/`0x4239fc`), the `08` sub-switch (`0x423b04`/`0x423ad4`) and the
assignment routine `0x41acb0(var, value, code)`. Binary operators pop `right` first, then `left`, and call
`f(left, right)`. The Value routines were identified by their error strings, e.g. `non-integral operands (>>)`.
Numeric promotion runs first (`0x417570`, `JVM::numericPromotion`).

| Code | Op | Evidence | Conf. | Uses (07 / 08) |
|---|---|---|---|---|
| 0x00 | `c ? a : b` (3 pops, both branches evaluated) | 3-pop handler 0x421d04 | L | 1 / 0 |
| 0x01 | `\|\|` | `non-integral operands (\|\|)` 0x41bfb0 | H | 181 |
| 0x02 | `&&` | `(&&)` 0x41bea0 | H | 424 |
| 0x03 | `\|` | `(\|)` 0x41c290 | H | 319 |
| 0x04 | `^` | `(^)` 0x41c380 | H | 2 |
| 0x05 | `&` | `(&)` 0x41c1a0 | H | 79 |
| 0x06 | `!=` | 0x41c6c0: equality, then inverted with `test; sete` | H | 197 |
| 0x07 | `==` | 0x41c5c0: int `a-b;neg;sbb;inc`, float ZF; also used by CASE | H | 2,243 |
| 0x08 | `>=` | 0x41c860: `eq \|\| gt` | H | 370 |
| 0x09 | `<=` | 0x41cb60: `!(a > b)` | H | 336 |
| 0x0A | `>` | 0x41c790: `setg` / float C0\|C3 clear | H | 332 |
| 0x0B | `<` | 0x41c9e0 (the remaining `_eq`+`_gt` combination); corpus `i < arr.length` | H | 537 |
| 0x0C | (`>>>`?) | no handler (default) | L | 0 |
| 0x0D | `>>` | `(>>)` `sar` | H | 7 |
| 0x0E | `<<` | `(<<)` `shl` | H | 2 |
| 0x0F | binary `-` | `unsupported type (-)` 0x41b760 | H | 823 |
| 0x10 | `+` (also String concatenation: `Ljava.lang.String;` branch) | 0x41b4d0 | H | 2,599 |
| 0x11 | `%` | `division by zero (%)` | H | 33 |
| 0x12 | `/` | `division by zero`, `idiv`/`fdiv` | H | 686 |
| 0x13 | `*` | `imul`/`fmul` | H | 1,486 |
| 0x14 | `~` | `non-integral operand (~)` | H | 6 |
| 0x15 | `!` | `non-integral operand (!)` | H | 512 |
| 0x16 | unary `-` | 0x41b1d0 `neg`/`fchs`; corpus `-8.4` → `0A 8.4f; 07 16` | H | 2,434 |
| 0x17 | `x++` (postfix) | copy pushed, then `++` (0x41b2d0) | H | 36 / 332 |
| 0x18 | `x--` (postfix) | copy pushed, then `--` (0x41b3d0) | H | 20 / 58 |
| 0x19 | `++x` (prefix) | `++`, then copy pushed | H | 53 / 2 |
| 0x1A | `--x` (prefix) | `--`, then copy pushed | H | 9 / 3 |
| 0x1B | expression marker. 07: no-op; 08: pop and discard (expression statement) | 0x422ef5 | H | 8,007 / 13,178 |
| 0x1C | literal: read and skip the next 09/0A/0B/0C/0D/0E instruction and push its value (`unhandled literal type`). 08: skip it | 0x4218c0 | H | 46,945 |
| 0x1D | – (no-op) | default | L | 0 |
| 0x1E | `new T`: next `06 TYPE`; create the instance (`0x410890` class lookup, `0x404e20` instantiate) and push it; `21 CALLINIT` follows the args | 0x422f4b | H | 3,290 |
| 0x1F | `new T[n]`: next `06 TYPE` (`[L…;`); pops the dimension; `multi-dimensional arrays not supported!` | 0x421ad6 | H | 614 |
| 0x20 | `instanceof T`: next `06 TYPE`; pop the object, push int | 0x422cb7 (corpus: `x instanceof RaceSetup` then `!`) | M-H | 182 |
| 0x21 | cast `(C)x`: next `19 CLASS`; `invalid cast structure`, `cannot cast %s` | 0x422d92 | H | 414 |
| 0x22 | void marker (no-op; follows `this(...)`/`super(...)`) | default | H | 0 / 1,839 |
| 0x23 | `=` | 0x41acb0 case 0 | H | 470 / 21,868 |
| 0x24 | `*=` | 0x41acb0 → `*` | H | 0 / 80 |
| 0x25 | `/=` | → `/` | H | 0 / 8 |
| 0x26 | `%=` | → `%` | H | 0 |
| 0x27 | `+=` | → `+` | H | 0 / 331 |
| 0x28 | `-=` | → `+ (-b)` | H | 0 / 106 |
| 0x29–0x2B | `<<=` `>>=` `>>>=` | **unsupported** (`unsupported assignmenttype`) | H | 0 |
| 0x2C | `&=` | → `&` | H | 0 / 5 |
| 0x2D | `^=` | → `^` | H | 0 |
| 0x2E | `\|=` | → `\|` | H | 0 / 20 |

The assignment codes follow the Java grammar order `= *= /= %= += -= <<= >>= >>>= &= ^= |=`. For assignment the
stack is `[… value var]`: the target Variable is pushed **after** the value.

## 4. Worked example (DeadIslandTrack, source line → code)

```
posStart = new Vector3( 30.0, 5.0, -30.0 );          // L27
  07 1E new / 06 TYPE Vector3 / 07 1C lit / 0A 30.0 / 07 1C lit / 0A 5.0 / 07 1C lit / 0A 30.0 / 07 16 neg
  27 ARGC 3 / 21 CALLINIT / 1B FIELD posStart / 08 23 (=, discard)
GameLogic.changeActiveSection( new DeadIslandTrack() );   // L36
  07 1E new / 06 TYPE DeadIslandTrack / 27 ARGC 0 / 21 CALLINIT        (argument)
  27 ARGC 1 / 24 CALL / 11 NAME 2 / 19 CLASS GameLogic / 12 MNAME "changeActiveSection" / 08 1B (discard)
super.enter( prev_state );                            // L47
  01 LOCAL 1 / 27 ARGC 1 / 26 SUPERCALL "enter" / 08 1B
end of void method                                    // L48
  29 PUSH0 / 03 DROP 1 / 03 DROP 0 / 2B RETVAL / 03 1 / 03 0   (the last two are unreachable)
```

## 5. Language limits compiled into the engine (strings, measured)

The in-engine compiler (flex/yacc, `JVM::compileSource`, `JVM::compileHook`) rejects: labeled statements,
`throw`, `try`/`catch`/`finally`, `synchronized`, some cast forms, multi-dimensional arrays, `<<=`/`>>=`/`>>>=`,
and octal literals. It also reports `'case:' label found after 'default:'`. There are no exceptions at runtime:
errors go to `!Script error` / `Thread: %s` / `%s:%s (line:%d)` and the thread stops.

## 6. Open points / next steps

* `0x00` (`?:`), `0x0D` (bool literal?), `0x20` (`instanceof`) and `0x22` (`this(...)`) are M or L. Check them
  with the C# VM against shipped behaviour.
* The `CALL` path resolution (`0x4208e0`) and the script-vs-native dispatch (`0x41fbc0`, `0x4207c0`) were only
  read at the call-site level: static vs instance (`[+0xc] == -2`), native flag `+4 & 8`.
* The compiler is inside the game exe, so Phase 3 cannot obtain a separate one. `.java` files under `…\src\`
  are recompiled when the `.class` is out of date (`JVM::compileSource: recompilation needed of "…"`).
* Script package roots (from strings): `java.lang` → `system\scripts\lang`, `java.io` → `system\scripts\io`,
  `java.util` → `system\scripts\util`, `java.render` → `system\scripts\render`, `java.sound` →
  `system\scripts\sound`, `java.game` → `sl\scripts\game`, `java.game.parts` → `parts\scripts`,
  `java.game.parts.accessories` → `parts\accessories\scripts`. Other packages resolve through `.rpk` /
  `external_links` (`java.unnamed` fallback).

## Scripts (all static; run with `python3 -I`)

* `pe_summary.py EXE [--imports]`: sections with entropy, imports per DLL.
* `pe_xref.py EXE str|imm|dis|calls|jt|writes …`: string xrefs, immediates, disassembly, call sites, jump
  tables, global writes (capstone).
* `switch_resolve.py EXE VA REG VALUES…`: statically resolves MSVC compare-tree/jump-table switches (gives
  the typeword → handler map above, e.g. `4210c5 eax 0x1001-0x102e`).
* `handler_strings.py EXE LABEL=VA …`: strings, calls and arithmetic per handler.
* `native_table.py EXE [--tsv]`: native registrations (→ `natives-redline.tsv`).
* `tufa_dis.py dis|stats|grep`: Python TUFA decoder with the new mnemonics, corpus statistics.
