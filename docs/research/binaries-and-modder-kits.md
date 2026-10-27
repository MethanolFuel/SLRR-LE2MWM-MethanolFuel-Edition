# Repo binaries and modder kits: static survey

All findings are static (pefile, capstone, strings, archive listings). No repo `.exe`/`.dll` was executed.
Archives were extracted into a scratch directory outside the repo. Nothing extracted is committed.

## MW_Mod/ForModders.rar (RAR 4, 34 files, extracted with `unar`)

There is **no script API documentation, no header and no compiler**. Contents:

| Path | What |
|---|---|
| `Tracks/Dead Island/…/Scripts/src/DeadIslandTrack.java` + `Scripts/DeadIslandTrack.class` | **Exact source/bytecode twin** (every TREE line number matches the source). The basis of the opcode work in `script-vm-opcodes.md`. Shows the track-addon API: extend `java.game.Track`, set `posStart`/`oriStart`/`map = new GroundRef(rpk:0x1r)`, `enter()` creates a water `RenderRef` and calls `map.setWater(...)`, `StartMap()` → `GameLogic.changeActiveSection(new …)` |
| `Tracks/Dead Island/…/Dead_Island.rdb/.rpk`, `maps.rdb`, `system.rdb`, `Meshes/*.scx/.SCY/.SCM/.tex`, `Textures/*.dds/.png` | Sample track resource tree. `RDB to RPK - Open this with Notepad.bat` = `resconvert maps\Addon_Maps\Dead_Island` (rdb → rpk) |
| `Tracks/Dead Island/…/resconvert.exe` | Console resource converter (different build, 188,478 bytes) |
| `Tracks/Dead Island/3D model export/` | `Dead_Island.max`, `Track.z3d`, BMP textures |
| `Tracks/Phys Lab/Physic_Lab_Track.java` | Another Track example (source only) |
| `Vehicles&Parts/Uncommon Wheels amount vehicles/Einvagen_110_GT.java`, `_main_110_GT.cfg` | Car with an unusual number of wheels (source + cfg) |
| `Vehicles&Parts/Catalog_SuperIDs.txt` | Catalog category super-IDs `0x0004xxxx` (e.g. `025C` engine blocks). Notes that `parts\` must be the 4th `external_links` entry |
| `Vehicles&Parts/suspension clicks fix.txt` | cfg `slot`/`attach`/`slottype` snippet |

## StreetLegalDevKit.exe

* **Not a PE**: a 16-bit **NE** self-extractor stub with an appended **ZIP** (92 files, 2003). Listed and
  extracted with 7-Zip as data.
* Contents: the SL1-era "#1 Vehicle import" kit. 3ds Max plug-ins `InvictusExportV3.dle` (SCX export) and
  `InvictusExportGroups.dle`, `ITC.exe` (PNG → PTX/mipmaps), `slresman.exe` (Borland C++ Builder 5 resource
  manager plus BPL/DLL runtime), `resconvert.exe` (151,614 bytes, same size as `ResourceConverter/resconvert.exe`),
  sample `cars/racers/YOUR_RIDE` rdb tree, `Car import tutorial.max`, `WHAT2DO.TXT` guides, `_LEGAL_.TXT`.
* **No script compiler, no script docs.** The `.java` compiler lives inside the game exe (below).

## ResourceConverter/*.exe

| File | Kind (measured) |
|---|---|
| `resconvert.exe` | PE32 console, MSVC 6, 2002. Imports only KERNEL32/USER32. rdb ↔ rpk converter |
| `resdecode.exe` | PE32 console subsystem, MSVC 7 (linker 7.0), 2003; imports GDI/comdlg/WINSPOOL/COMCTL (MFC-style) |
| `Resource Converter.exe`, `RPK_Creator.exe`, `SLRReditor2.exe`, `B.O.R.A.T For MWM.exe` | .NET (only `mscoree.dll` imported). Community tools |
| `Creator 32bit.exe` / `Creator 64bit.exe` | Mixed-mode C++/CLI (MSVC 11 + mscoree), 2014 |

None of them reference TUFA, `.class` or the script VM (strings checked).

## StreetLegal_Redline.exe (the engine)

* PE32 i386 GUI, MSVC 6 (linker 6.0), timestamp `0x420765d4` (Feb 2005), image base `0x400000`, entry
  `0x5514c0`. Sections: `.text` 1.9 MB, `.rdata` 128 KB, `.data` 1.6 MB virtual, `.rsrc`, `.reloc`. Imports:
  d3d9 (1), KERNEL32 (127), USER32 (31), GDI32, DINPUT8, DSOUND, ADVAPI32, WINMM (15), ole32, OLEAUT32. Not packed
  (entropy 6.6).
* `StreetLegal_Redline.bak.exe` has the same size, and `Slrr_GI.exe` is the same build with an extra `.code`
  section that adds natives (see `script-vm-natives.md`).
* **The script toolchain is built in**: a flex lexer (`fatal flex scanner internal error`), a yacc parser
  (`yyparse`, `syntax error`), `JVM::compileSource` / `compileHook`, `System.compileAll(String)I`, class loader
  `JavaMachine::loadClass` with chunk dispatch (`JVM::addClass: unknown chunk`). The TUFA chunk tags are
  compared as immediates in `.text` (file offsets 0x11b67–0x11d1f and 0x160ce–0x16330: reader and writer).
* Interpreter: `Thread::run` at `0x420ff0`; opcode table `0x5f0914`; JTree I/O `0x41a5f0` (read pure) and
  `0x41a930` (write pure). Details in `script-vm-opcodes.md`.
* Source-path leak: `D:\Invictus\StreetLegalRedline\!Engine\Game\Natives.cpp`.

## Measured vs hypothesis summary

* Measured: archive contents; PE headers, sections and imports; string locations; the opcode→typeword table;
  handler addresses for every typeword; the 360 native registrations.
* Hypothesis: the role of each community tool beyond what its strings or file name say; the exact recompile
  policy of `JVM::compileSource` (inferred from its messages).
