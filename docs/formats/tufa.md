# TUFA (`.class`) compiled scripts

All 1,692 shipped `.class` files parse completely: 1,760 classes (24 files hold 2 classes, 3 hold 3),
4,374 methods, 7,033 code bodies, 387,977 instructions. Every chunk is consumed to its last byte.

## File structure

```
repeat (one per class):
  "TUFA" u32 version (4) u32 stamp (0x00011AE1 in the samples; checksum/compiler build?)
  chunks: char[4] tag, u32 size, payload
    CONS  constant pool (always)
    FILD  fields (265 classes)
    MTHD  methods (1,752 classes)
    CLSS  class header (always)
    TREE  code (always)
```

### CONS

`u32 count`, then entries `u32 type` + payload:

| Type | Payload | Count | Meaning |
|---|---|---|---|
| 0 | u32 len, len bytes, NUL | 59,785 | string (names, descriptors `(I)`, `Ljava.lang.String;`, literals) |
| 3 | u32 stringIdx, u32 id | 3,036 | resource literal `rpk:0xID` (string = archive, e.g. `MW_Mod.rpk`) |
| 4 | u32 nameIdx | 6,448 | class |
| 5 | u32 classIdx, u32 nameAndTypeIdx | 25,387 | field or method ref (descriptor starting `(` = method) |
| 7 | u32 nameIdx, u32 descIdx | 24,912 | name-and-type |

Descriptors use dots, not slashes: `Ljava.lang.String;`, `(Ljava.render.Text;)`.
Note: constructors have descriptors without return type (`(I)`), methods end with one (`(F)F`, `()V`).

### FILD / MTHD

Two sections each: `u32 staticCount`, static rows, `u32 instanceCount`, instance rows.
FILD row = `flags, nameIdx, descIdx, value` (value 0xFFFFFFFF = none; static finals hold small ints,
hypothesis: the constant value). MTHD row = `flags, nameIdx, descIdx, bodyIndex, slots`
(slots = this + args + locals, hypothesis). Flags seen on methods: 0x1 (3,895), 0x41, 0xA1, 0x0, 0x4, 0x21,
0x40; static methods 0x9 / 0x29; static final fields 0x28.

### CLSS

`u32 0xFFFFFFFF, u32 (0 or 1), u32 thisClassIdx, u32 superClassIdx, u32 interfaceCount, interfaces`.

### TREE

`u32 bodyCount`, then per body `u32 instructionCount` and instructions:
`u8 opcode, u16 sourceLine, [i32 operand]`. 40 opcodes exist; the operand size per opcode is fixed and
was solved from the corpus (a constraint search over all TREE chunks, then verified: 1,760/1,760 decode exactly).

Operand (4 bytes): 01 02 03 06 07 08 09 0A 0B 0D 0E 11 12 14 15 16 17 19 1A 1B 1C 25 26 27.
No operand: 04 05 0C 10 18 20 21 22 23 24 28 29 2A 2B 2C 2D.

Operand statistics (what the 4-byte value points at in CONS), the basis of the tentative mnemonics
in `TufaOpcodes.cs`:

| Op | Uses | Operand | Tentative |
|---|---|---|---|
| 1B | 49,637 | always a field ref | field access |
| 1A | 13,682 | always a method ref | invoke |
| 19 | 5,681 | always a class | class ref (new/cast) |
| 0E | 4,222 | always a resource literal | push resource |
| 09 | 8,256 | always a string | push string |
| 0A | 18,040 | raw 32-bit immediate (floats such as 50.0) | push immediate |
| 07 / 08 | 73,649 / 37,830 | small values 27, 28, 35, 34 ... | push/pop "register"? |
| 14 15 16 | 3,149 / 5,793 / 3 | include negative values | relative branches? |
| 01 02 03 11 27 | | small ints 0..7 | locals / arg counts? |
| 2A | 1,839 | none | return? (seen before the trailing `03 n` run in the samples; not present in every body) |

Example (`parts/scripts/bodypart/RollCage.class`, constructor; `name = "Roll cage";` is line 25 of today's source):

```
L20  07 push_reg? 28
L20  09 ldc_string #15 "Roll cage"
L20  1B field #10 java.game.parts.bodypart.RollCage.name:Ljava.lang.String;
L20  08 pop_reg? 35
```

## Pairing with the `.java` sources

136 of the 138 `.java` files have a `.class` with the same base name. **The pairs are not reliable
twins**: e.g. `RollCage.java` has 12 methods and a second class `SetAgain`, while `RollCage.class`
holds only the constructor. Source line numbers in TREE are 5 lower than the current `.java`
(`super(id)` / `name = ...` / `prestige_calc_weight = ...` at L18/L20/L22 vs lines 23/25/27). So the sources were edited after compilation, and opcode semantics cannot be read off the
pairs directly. Plan for Phase 3: get the original compiler (StreetLegalDevKit.exe /
`MW_Mod/ForModders.rar` were not examined in Phase 1) to compile known snippets, and disassemble the
interpreter loop in `StreetLegal_Redline.exe`.

## Open questions

- 2,660 of the 7,033 bodies are not referenced by any MTHD row. Methods use the last N bodies
  (e.g. MainMenuDialog: 9 methods use bodies 56..64 of 65). Hypothesis: nested blocks (the chunk is called TREE),
  referenced from instructions (candidates: ops 12, 1C, 25 with large operands).
- Trailing `03 n` instructions after `2A` in the inspected bodies (locals table / debug info?).
- Meaning of `stamp`, CLSS word 1, and the register numbers of 07/08.
- Exceptions, switch tables and `native` methods (`lang/Native.class` has 0 bodies).
