# How to run the game (C# rewrite preview)

This is an early, unofficial preview of a C# (.NET 8) rewrite of the Street Legal Racing: Redline engine. It plays the
shipped game scripts and data of this repository. It is **not** the original game and its physics, sound and graphics have
**not** been compared with it. What works and what does not is listed at the end.

You need two things: the **game data** (this repository) and the **program** (a downloaded package, or built from source).

## 1. Get the game data (all platforms)

```
git clone https://github.com/antomys/SLRR-LE2MWM-MethanolFuel-Edition.git
```

About 480 MB. The cloned folder (it contains `system.rpk` and `sl.rpk`) is the **game root** used below as `<game root>`.
The program never writes into it: careers, settings and logs go to a per-user folder (see "Where things are saved").

## 2. Get the program: a package, or source

**A package** (recommended): open the repository's **Releases** page and download the newest *Preview*:

| Your computer | File |
|---|---|
| Mac with Apple Silicon (M1, M2, M3, M4) | `slrr-osx-arm64.zip` |
| Mac with an Intel processor | `slrr-osx-x64.zip` |
| Windows 10/11, 64-bit | `slrr-win-x64.zip` |
| Linux, 64-bit | `slrr-linux-x64.tar.gz` |

Each package has two programs: `slrr-app` (the interactive game) and `slrr` (command-line tools and headless demos). They are
self-contained: no .NET install needed. They are **unsigned**, so each operating system warns the first time (see below).

**From source** (any platform with the [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)):

```
cd SLRR-LE2MWM-MethanolFuel-Edition
dotnet run -c Release --project src/Slrr.App -- --root .
```

The first build takes a few minutes. `--root .` means "the game data is the folder I am in".

## 3. Run it, per platform

### macOS (Apple Silicon and Intel)

1. Unzip the package, e.g. into `~/slrr`. Open Terminal there.
2. macOS blocks unsigned downloads. Remove the quarantine flag once and make the programs executable:
   ```
   cd ~/slrr
   xattr -dr com.apple.quarantine .
   chmod +x slrr slrr-app
   ```
   (Without this, macOS says the program "cannot be opened because the developer cannot be verified". Alternative: right-click
   the file in Finder, choose Open, then Open.)
3. Check that OpenGL works on your Mac (takes a second):
   ```
   ./slrr gl-probe
   ```
   It should end with `done: OpenGL works` and name your GPU. If it stops early, send me its output (see Troubleshooting).
4. Start the game, pointing at the game data you cloned:
   ```
   ./slrr-app --root ~/SLRR-LE2MWM-MethanolFuel-Edition
   ```
   Add `--size 1280x960` for a bigger window or `--fullscreen`. On a Retina screen that is slow, add `--render-scale 0.5`.

### Windows

1. Unzip `slrr-win-x64.zip` (right-click, Extract All), e.g. into `C:\slrr`.
2. First start: Windows SmartScreen may say "Windows protected your PC". Click **More info**, then **Run anyway**.
3. Open PowerShell in that folder:
   ```
   .\slrr.exe gl-probe
   .\slrr-app.exe --root C:\Users\you\SLRR-LE2MWM-MethanolFuel-Edition
   ```
   `gl-probe` should end with `done: OpenGL works`. If your graphics driver is very old, update it (OpenGL 3.3 is needed).

### Linux

1. Install the usual graphics and sound libraries (Debian/Ubuntu names; others are similar):
   ```
   sudo apt install libgl1 libopenal1
   ```
   You need a desktop session (X11 or Wayland through XWayland) and a GPU driver with OpenGL 3.3, or Mesa's software renderer.
2. Unpack and run:
   ```
   mkdir ~/slrr && tar -xzf slrr-linux-x64.tar.gz -C ~/slrr && cd ~/slrr
   chmod +x slrr slrr-app
   ./slrr gl-probe
   ./slrr-app --root ~/SLRR-LE2MWM-MethanolFuel-Edition
   ```
   On a machine without a display (a server) use `xvfb-run -a ./slrr-app ...` with `LIBGL_ALWAYS_SOFTWARE=1`.

## 4. What to do in the game

The mouse clicks menus. A first drive:

1. **New career** → type a name → OK → OK (choose character).
2. In the garage press **OK** on the welcome hint, click **Buy used cars or sell your car**, wait for the loading dialog, press
   **Enter**, pick a car and press **Enter** (select), then **Yes**.
3. In the garage click **Choose Track**, pick **Drag Track** (or another map) → **TAKE IT**.
4. Drive: **Up** = throttle, **Down** = brake / reverse, **Left / Right** = steer.

| Key | Effect |
|---|---|
| Arrow keys, Enter, Esc | menus and driving (the game's own control file) |
| **F3** | frame-time overlay on/off |
| **F10** | pause |
| **F11** or **Alt+Enter** | full screen on/off |
| **F12** | screenshot into `screenshots/` (see "Where things are saved") |
| Close button / Alt+F4 / Cmd+Q | quit |

A gamepad is mapped to the same keys (untested). Not yet working with the mouse: dragging parts in the garage, sliders, camera
control with the right button.

Useful options for `slrr-app`: `--size WxH`, `--fullscreen`, `--aspect stretch`, `--render-scale 0.5`, `--no-audio`,
`--no-vsync`, `--save-root <dir>`, `--verbose`. Full list: `docs/app.md`.

## 5. The command-line tool (`slrr`)

These need no window, so they work on any machine and are good first checks (replace `<root>` with the game root):

```
slrr coverage <root>                                     # parse every shipped file, print counts
slrr engine-boot <root>                                  # boot the script engine to the splash screen
slrr play --smoke --root <root>                          # headless: new career, buy a car, buy a part
slrr race-play --root <root> --track "Drag Track" --seconds 10      # headless drive; prints distance and speed
slrr race-play --root <root> --track "Drag Track" --render --audio --out frames --wav run.wav   # PNG frames + WAV (needs OpenGL)
slrr drive Baiern_DevilSport --scenario all              # physics scenarios: 0-100, top speed, braking, skidpad
slrr gl-probe                                            # OpenGL check, stage by stage
slrr                                                      # lists every command
```

## Where things are saved

| OS | Folder |
|---|---|
| Windows | `%APPDATA%\SLRR-CSharp\` |
| macOS | `~/Library/Application Support/SLRR-CSharp/` |
| Linux | `~/.local/share/SLRR-CSharp/` |

Careers are in `save/`, the log in `logs/slrr-app.log`, screenshots in `screenshots/`. Delete the folder to start fresh.
Override with `--save-root <dir>`.

## Troubleshooting

* **"game data missing" / exit code 1**: `--root` must be the folder that contains `system.rpk` and `sl.rpk`.
* **No window / exit code 3**: your system could not create an OpenGL 3.3 core context. Run `slrr gl-probe`: it prints a line
  before and after each stage, so the last line shows where it stopped. Update the graphics driver; in a virtual machine
  without a 3D GPU the program may crash instead of failing cleanly (this was seen on GitHub's macOS test machines).
* **Crash with "Segmentation fault" when drawing**: same cause, run `gl-probe` and report its output.
* **No sound**: the program falls back to silence if no OpenAL device opens, with a line in `logs/slrr-app.log`.
  Try `--no-audio` to rule sound out.
* **Slow**: lower the window (`--size 800x600`), use `--render-scale 0.5`, close other programs. It needs about 1 GB of memory.
* **Slow first start**: loading the game scripts takes a few seconds.
* **Please report problems** with: your OS and CPU, the output of `slrr gl-probe`, and `logs/slrr-app.log`.

## What works and what does not (honest status)

Works (checked by automatic tests, and on GitHub's macOS, Windows and Linux machines for the headless parts): all shipped
files parse; the script engine boots; main menu, career, garage, dealer, catalog, buying and installing parts, saving and
loading; driving a bought car on the free-roam maps (11 of 12; Valo City's start dialog blocks the car); the City day and night
races and the Race of Champions run through the real scripts with stand-ins for some pieces; engine sound follows the exe's
measured rate formula.

Does **not** work or is unverified: parity with the original game (physics, sound, graphics are plausible, never compared);
opponents in some races only partly; no replay, no multiplayer; five turbo cars have no engine torque; a few menus with drag and
drop or sliders; gamepads, real sound devices and real GPUs are untested; the interactive window has so far been run only on
Linux with a software renderer.
