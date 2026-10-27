# CFG (`.cfg`) text configuration

Line-based DSL used for parts, cars, objects, particles, skid marks and humans. Derived from all 1,182
shipped files (1,177 LF, 5 CRLF, Latin-1/ASCII). `ResourceConverter/Keywords.ini` turned out to be a
catalog-sorting table (part name keyword -> catalog section), not cfg grammar.

## Grammar (as implemented in `CfgDocument`)

```
file      := line* ( 'eof' line* )?          ; 15 files have no eof (warning CFG013)
line      := ws* body? ws* comment? EOL
comment   := ';' text | '//' text            ; first of the two wins; quotes have no meaning
body      := '#' text                        ; disabled directive or banner (3,685 '#' banner lines)
           | '[' name ']'                    ; sections, particle cfgs only: [STATE], [ACTION]
           | 'compound' args?                ; opens a block; nests (42 files use depth 2)
           | 'endcompound' args?             ; closes it, usually 'endcompound auto'
           | 'eof'                           ; ends parsing; later lines are ignored
           | keyword (ws+ arg)*              ; arg = any run of non-whitespace
ws        := ' ' | '\t'
```

Arguments are untyped tokens. Shapes seen: hex ids `0x0004F242`, ints, floats, words (`sphere`, `point`,
`line`, `plane`, `auto`), paths (`cars\racers\X\meshes\_phys_chassis.scx`), `rpm:value` pairs
(`#torque 14 500:0.0 ...`) and comma triples (`ext_camera 120 0,1.8,0 ...`).

## Diagnostics

| Code | Severity | Rule |
|---|---|---|
| CFG001 | error | control/binary character in a line |
| CFG002 | error | malformed `[section]` |
| CFG010 | error | `endcompound` without `compound` |
| CFG011 | error | `compound` never closed |
| CFG012 | info | text after `eof` |
| CFG013 | warning | missing `eof` |
| CFG100 | warning | keyword not in the observed vocabulary (`CfgKeywords.Known`) |
| CFG101 | warning | `0x` token that is not valid hex |
| CFG200 | warning | file-looking argument (known extension) missing on disk (validator) |
| CFG210 | info | repeated `stockpart` id (normal for wheels) |
| CFG211 | warning | repeated slot id inside one compound part |
| CFG212 | info | repeated identical `compatible` line |

Result on the shipped content: **1,182/1,182 files parse with zero errors**; warnings as listed in
[README.md](README.md).

## Vocabulary

85 keywords (see `CfgKeywords.cs`), most frequent: `slot` 3,753, `attach` 2,753, `render` 2,231,
`body` 1,522, `compatible` 1,265, `eof` 1,167, `category` 1,131, `click` 1,085, `lod` 1,040.
Only `body` carries file paths in practice (988 lines, collision meshes).

## Open questions

- Argument schemas per keyword (counts and types) are not enforced yet; next step is to derive
  them from the corpus and confirm against the engine's cfg loader.
- Whether the engine accepts `//` comments everywhere or only by accident of tokenizing.
