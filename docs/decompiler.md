# TUFA decompiler (`src/Slrr.Decompiler`)

Turns compiled `.class` scripts (TUFA, see `formats/tufa.md`) back into the game's Java dialect. Opcode
semantics follow `research/script-vm-opcodes.md`. Nothing is executed: the decompiler reads the parsed
`TufaClass` from `Slrr.Formats` and works on the instruction lists.

```
slrr decompile <file.class>                 # print the source of every class in the file
slrr decompile <file.class> --out dir       # write dir/<Name>.java
slrr decompile <dir> [--out dir]            # every .class below dir (hidden dirs skipped), summary + problem files
slrr decompile <gameRoot> --fidelity        # also compare with the shipped src/*.java twins
            [--quiet]                       # only the summary lines
```

Exit code 0 when everything decompiled cleanly (single file: 1 if any class had errors or unstructured jumps).
Build/test in isolation: `dotnet build Decompiler.sln -warnaserror && dotnet test Decompiler.sln`.

## Results on the shipped corpus

| Check | Result |
|---|---|
| Files / classes decompiled | 1,692 / 1,760; **1,760 clean** (no errors, no unstructured jumps) |
| Bodies | 6,675 decompiled (4,015 methods + 2,660 field initializers) + 358 empty native bodies = 7,033 / 7,033 |
| Unstructured jumps (`goto`) | 0 |
| Unresolved opcodes / operator codes | none (0F, 1D, 1E, 1F, 2E and operator 0C never occur) |
| Structures recovered | 5,494 `if` (1,216 `else if` chains), 227 `for`, 79 `while` (4 `while(true)`), 3 `do…while`, 7 `switch`, 115 `break`, 3 `continue`, 8 `?:` |
| `.java` files paired with a `.class` | 136 / 138 (168 classes matched by name) |
| Members compared (methods, constructors, fields) | 2,930 matched; 175 exist only in the `.java`, 0 only in the `.class` |
| Exact token match after normalisation | 2,829 / 2,930 = **96.6 %**; ignoring parentheses 2,891 = **98.7 %** |
| Token similarity (2·LCS / total) | **99.72 %** |
| Files where every matched member matches (ignoring parentheses) | 118 / 136 |

All 39 remaining mismatches (28 distinct members; the root `src/*.java` copies repeat 11 of them) were inspected
(several spot-checked against the raw instructions): they are edits made to the `.java` files after the shipped `.class` was compiled
(swapped `min_force`/`max_force` constants in the six swaybars, `"124 PTX"` vs `"128 PTX"`, array sizes 7 vs 13,
fields declared `int` vs `float`, statements and `if`s present only on one side, `&&` vs `||` in
`ShockAbsorber.isTuneable`, a nested `if` in `OptionsDialog.show` where the source now has `&&`), one resource
literal the compiler could not resolve (`Chassis` has `system:0x0000r` where the source names
`parts.amilmandfinalmerge:0x0190r`), and one float (`Tyre`: source `3.141593`, stored bits = correctly rounded
`3.141592`; either an edit or a limit of the float model below). The 62 parenthesis-only differences are
redundant parentheses in the sources (`return(deg * 3.141 / 180);`, `(a / b) * c`).

### Normalisation used by the fidelity check

Both sides are tokenised (comments and whitespace dropped) and split into classes and members (key: method name
plus parameter types, or field name). Then: `package`/`import` dropped; modifiers sorted; `T a, b = x;` split
into one declaration per variable; single statements under `if`/`else`/`for`/`while`/`do` wrapped in braces;
bare nested `{ }` blocks flattened; empty statements, a trailing `return;` and a leading `super();` dropped;
parameters and locals renamed `$0, $1, …` in declaration order; ints compared as 32-bit values
(`0xFFC0C0C0` = `4290822336` = `-4144960`); float literals compared as the bits the game compiler would produce
(see below); resource literals compared case-insensitively by archive and id.

## How it works

* **Expressions** – a symbolic operand stack replays the postfix code. Prefix markers consume the following
  instruction(s): `07 1C` literal, `07 1E new`/`07 1F new[]`/`07 20 instanceof` + `06 TYPE`, `07 21` cast +
  `19 CLASS` or `06 TYPE`, `11 NAME n` + n path components, `24 CALL` + optional `NAME` + `1A`/`12` method,
  `02 DECLLOCAL` + `06 TYPE` (+ `2C DECLUSE` … `2D ASSIGNINIT` for `T x = init;`). `27 ARGC n` is a stack marker
  popped by the call that follows its arguments. Precedence-driven printing adds only necessary parentheses.
* **Short-circuit** – `a; 04|05; 14 JMP +k; b; 07 02|01`: the jump lands just after the `&&`/`||` operator, so
  the right operand is the range between the jump and the operator.
* **if / else** – `cond; 15 JMPF t`. If the instruction before `t` is a forward `14 JMP u` with `u > t` inside the
  current block, it is `if/else` with the else branch `[t, u)`, unless `u` is the innermost break/continue target
  and the JMP's line differs from the JMPF's line (then it is `if (…) { …; break; }`). Both jumps of an if/else
  carry the line of the end of the whole statement; a `break` carries its own line.
* **Loops** – a backward `14 JMP` (or `16 JMPT` for `do…while`) at `b` targeting `i` makes `i` a loop header.
  `while (cond)` when the code from `i` is an expression ending in a JMPF to `b+1`, otherwise `while (true)`.
  Trailing statements before `b` that carry the condition's source line are the `for` update (and the
  `continue` target); a preceding assignment/declaration on the same line becomes the `for` init.
* **switch** – `value; (case-value; 17 CASE +off)*; 18 POP; 14 JMP default-or-end`. Labels with the same target are
  grouped; the end is the furthest forward jump inside the region; `break` = jump to the end.
* **Methods** – flags `0x1 public, 0x2 protected, 0x4 private, 0x8 static, 0x10 abstract, 0x20 final,
  0x40 native, 0x80 synchronized`. Native methods (358) point at empty bodies and print as declarations.
  The epilogue (`PUSH0`/`RETURN`, drops, `RETVAL`) is dropped; `super(...)`/`this(...)` come from `23`/`22`.
* **Fields** – the FILD value is the index of the initializer body (ends with `10 ENDINIT`).
* **Locals** – names are not stored; locals are named from type and slot (`i3`, `f2`, `slider5`, `ints2`).

## Findings (additions to / contradictions with `research/script-vm-opcodes.md`)

1. **Parameters occupy the slots in reverse order.** The *last* parameter is slot 1 (slot 0 in static methods),
   the first parameter the highest slot: `SetAgain(float posX2, …, int pID)` reads `posX2` as `LOCAL 8`. The doc's
   "slot 0 = this, then arguments" suggests ascending order. Declared locals are appended after the parameters.
2. **`07 21` cast is also followed by `06 TYPE`** (primitive casts `(int)`, `(float)`; 6 uses), not only by
   `19 CLASS`.
3. `0D` is the boolean literal (`return true;` in `Disc.isTuneable`), confirming the M-rated guess.
4. Operator `0x00` pops `[cond, a, b]` and is `cond ? a : b` (confirmed against `Tyre.java` line 394).
5. **CLSS word 1 is the interface flag**: 1 only for `java.lang.Runnable` and `java.game.GameState`.
6. A `14 JMP +1` occurs 28 times: an empty jump (e.g. the last `break` of a switch placed after the enclosing
   `if`'s end in `City.handleEvent`, or an empty `else`). It is treated as a no-op.
7. **The game compiler's float parsing is not correctly rounded.** The integer part is accumulated in a wrapping
   uint32 and each fraction digit is added in single precision (`f += d * scale; scale /= 10`). This model hits
   2,237 of 2,389 float literals of the paired sources (correct rounding: 2,079; it never misses a literal
   that correct rounding hits). Examples: `3.141` → 3.1409998f, `0.0035` → 0.0034999996f,
   `50000000000.0` → 2755359744f (`Tyre.SetInflation`). The decompiler prints the shortest decimal that this
   parser maps back to the stored bits, so the shipped spelling comes back (`3.141`, `0.0035`).
8. Negative `0B INT` operands only come from hex constants (`-x` compiles to `07 16`), so they are printed as hex
   (`0xFFC0C0C0`).
9. Unused in the corpus and therefore untested: `0F`, `1D`, `1E`, `1F`, `2E` (local names), operator `0x0C`.

## Known lossy details

Local variable and parameter names, comments, formatting, redundant parentheses, `T a, b;` grouping,
braces around single statements, member order (the decompiler prints fields, then methods in table order),
import lists (the decompiler imports every referenced package with `.*`), and the source spelling of numbers
(`0x10` vs `16`). A `while` whose last statement shares the condition's line is printed as a `for` with that
statement as the update; the two are equivalent.

## Integration notes

* `tools/Slrr.Cli/CmdDecompile.cs` exposes `Run(string[] args, TextWriter o, TextWriter e)`; `Commands.cs` needs
  `"decompile" => CmdDecompile.Run(args[1..], o, e),` and a usage line. `Slrr.Cli.csproj` references
  `Slrr.Decompiler`.
* `Slrr.sln` needs `src/Slrr.Decompiler` and `tests/Slrr.Decompiler.Tests`. The corpus tests use the same
  `SLRR_GAME_ROOT` / repository-root lookup as `Slrr.Formats.Tests` and pass vacuously without game data.
