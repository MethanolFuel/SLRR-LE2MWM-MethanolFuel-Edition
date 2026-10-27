# The interactive game: `slrr-app` (`src/Slrr.App`)

A windowed, real-time executable on top of the race-wave composition root (`RaceHost.Create`, docs/race/integration.md): the
shipped scripts run the game, `Slrr.Game.Render` draws it, `Slrr.Game.Audio` mixes it and OpenAL plays it. You click through
**main menu → new career → garage → used-car dealer → buy → Choose Track → a map** with the mouse and drive with the keyboard
(or a gamepad). Silk.NET 2.22 (pinned 2.x: Windowing, OpenGL, Input, OpenAL) over GLFW 3.4.

Labels: **MEASURED** = observed by running this code (Linux, Xvfb + Mesa llvmpipe, numbers below); **GUESSED** = a choice that
fits the scripts/data but was not traced in the original; **UNTESTED** = written from the Silk.NET/GLFW/Apple documentation,
never run here (no macOS, no Windows, no real GPU, no audio device in the build sandbox).

## Running it

```
dotnet run -c Release --project src/Slrr.App/Slrr.App.csproj -- --root "/path/to/Street Legal Racing Redline"
dotnet run -c Release -p:PlatformTarget=AnyCPU --project src/Slrr.App/Slrr.App.csproj -- --root ~/Games/SLRR   # Apple Silicon (see macOS)
```

The game data is **not** part of the app: point it at a Street Legal Racing: Redline directory (this repository's root is one).
Lookup order: `--root <dir>`, else the environment variable `SLRR_GAME_ROOT`, else the first directory that contains both
`system.rpk` and `sl.rpk` among the working directory, the executable's directory and their parents (matched
case-insensitively). An explicit `--root`/`SLRR_GAME_ROOT` that lacks the archives is an error (exit 1) with the reason, not
silently replaced.

Careers, the control file the game writes and the log live in a per-user folder, never in the game directory:

| OS | Folder |
|---|---|
| Windows | `%APPDATA%\SLRR-CSharp\` |
| macOS | `~/Library/Application Support/SLRR-CSharp/` |
| Linux | `$XDG_DATA_HOME/SLRR-CSharp/` (default `~/.local/share/SLRR-CSharp/`) |

`save/` (the game's `save\...` paths; reads fall back to the game's own `save/`, so the shipped control set loads), `logs/slrr-app.log`,
`screenshots/`. Override with `--save-root <dir>` or `SLRR_SAVE_ROOT`.

### Command line

| Option | Meaning |
|---|---|
| `--root <dir>` | game directory (see above) |
| `--save-root <dir>` | careers and settings |
| `--size WxH` | window size in window units (default 1024x768) |
| `--fullscreen` | start full screen |
| `--aspect fit\|stretch` | `fit` (default): keep the game's 4:3 picture with black bars; `stretch`: fill the window (3D uses the window's aspect, the menus stretch horizontally) |
| `--render-scale s` | offscreen resolution relative to the picture's pixels (0.25..2; e.g. 0.5 on a slow GPU or a 5K Retina screen) |
| `--host-hz n` | fixed script/physics step rate (default 30: every measured race-wave run used 1/30 s) |
| `--no-vsync`, `--max-fps n` | VSync is on by default; without it the loop is capped at 240 fps |
| `--no-audio`, `--wav file` | no sound / write the mix to a WAV instead of the device |
| `--headless-frames n` | no window, GL or audio: n host frames (CI), same input path |
| `--input-script file\|builtin:drive` | synthetic input for tests (below) |
| `--lockstep` | exactly one host frame per displayed frame (deterministic scripted runs) |
| `--capture-every n`, `--out dir` | PNG of the window every n host frames / where captures go |
| `--quit-after s`, `--log file`, `--verbose` | |

Exit codes: 0 ok, 1 game data missing / boot failed / input script failed / GL errors in a scripted run, 2 bad arguments, 3 no
window or OpenGL 3.3 context.

### Controls

| Input | Effect |
|---|---|
| Mouse | the game's cursor (`MouseCursor.getPos`); hovering highlights a menu item, left click (press and release on the same item) clicks it |
| Keyboard | every key goes to the game as its DirectInput scan code; the shipped control set maps **Up** = throttle, **Down** = brake, **Left/Right** = steer (MEASURED in the race-wave run), Enter/Esc/arrows drive the menus, typing fills text fields (career name) |
| Esc | the game's own menu / back (not the app's) |
| **F3** | FPS / frame-time overlay on/off (on by default) |
| **F10** | pause (stops the host; the app keeps drawing) |
| **F11** or **Alt+Enter** | full screen on/off |
| **F12** | screenshot (PNG of the window) into `screenshots/` |
| Close button, Alt+F4, Cmd+Q | quit |
| Gamepad (first connected, GLFW mapping) | right trigger = Up (throttle), left trigger = Down (brake), left stick X = Left/Right with its magnitude, A = Enter, B/Start = Esc, X = Space, D-pad = arrows (GUESSED mapping, UNTESTED: no pad here) |

F3, F10, F11 and F12 are taken by the app and never reach the game.

## How it works

```
per displayed frame:  poll events ─► input script (tests) ─► InputRouter ─► FixedStepClock ─► host.RunFrame() x n ─► draw scene (if the host advanced)
                      ─► blit offscreen target into the letterboxed picture ─► overlay ─► glGetError ─► SwapBuffers (VSync paces)
```

| File | Role |
|---|---|
| `AppMain.cs`, `Program.cs` | arguments, game/save root, headless loop; `Main` is synchronous on the first thread (Cocoa needs windowing + GL there) |
| `WindowRunner.cs` | Silk.NET window, GL 3.3 core forward-compatible context, the loop, present/blit, F-keys, screenshots, GL error check, report |
| `ViewportMapper.cs` | window units ↔ framebuffer pixels (Retina) ↔ letterboxed 4:3 picture ↔ OSD −1..1 ↔ 800x600 virtual UI |
| `KeyMap.cs` | Silk.NET/GLFW keys → DIK scan codes; control characters for Enter/Esc/Backspace/Tab |
| `InputRouter.cs` | raw events → game: key+char pairing, auto-repeat filter, mouse hover/click, gamepad mapping, app keys |
| `HostGameInput.cs` | gadget hit testing from the scripts' own physics boxes; clicks via `UiDriver.Click/ClickIcon`, hover via `UiDriver.Hover` |
| `FixedStepClock.cs` | fixed host step, ≤4 steps per displayed frame, long frames clamped to 0.25 s (the game slows instead of spiralling) |
| `RealtimeAudioSink.cs` | non-blocking OpenAL streaming, bundled OpenAL Soft first, then the system library, else NullSink + log line |
| `DebugOverlay.cs` | 5x7 bitmap font text boxes (overlay, LOADING, PAUSED, errors), GLSL 330 core |
| `InputScript.cs`, `Scripts/drive.input` | test-only synthetic input |
| `GameSession.cs` | RaceHost + router + hit tester + script; log |

**Mouse → gadget (MEASURED formulas, GUESSED box semantics).** The engine hit-tests the mouse against each gadget's physics box.
`GadgetHitTester` rebuilds the boxes from the decompiled `Osd.createButton/createMulti/createStringInput` and `Style`:
`createBox(group, rWidth, rHeight, 0.001)` placed by `setMatrix(convertTextCoordinates(x, y))`, `convertTextCoordinates =
(x·SCALE_3D·vpAspect·vpWidth, −y·SCALE_3D·vpHeight, z/1000)`, `rWidth = width·SCALE_FS·aspect`, `rHeight = height·SCALE_FS`. So the
OSD centre is `(pos.x/(SCALE_3D·vpAspect·vpWidth), −pos.y/(SCALE_3D·vpHeight))` and the size `(box.x/(SCALE_FS·vpAspect·vpWidth),
box.y/(SCALE_FS·vpHeight))` — the same conversion the renderer uses for the button's background rectangle. GUESSED: the box sizes are
full extents and the viewport is full screen (as the renderer assumes). Icon buttons (`Osd.createButton(int rid, ...)`) get a GUESSED
0.12 x 0.16 box. Order: the top Osd of `Frontend.inputQueue` first, then the smallest box. Corpus test
`MainMenuLabelsLieInsideTheirClickableBoxes`: every labelled main-menu button's label anchor lies inside its box (MEASURED).

**Keyboard.** GLFW key tokens name US-layout physical positions, which is what a DirectInput scan code is, so the table is
layout-independent (an AZERTY "A" key sends DIK 16 = Q, as DirectInput would). GLFW sends the typed character in a separate
callback right after the key; the router attaches it to that key press (`Input.lastKey` = `(char << 16) | dik`, read by `Osd.animate`
for text fields). Enter/Esc/Backspace/Tab carry 13/27/8/9 (the values `StringInput.key` tests, MEASURED from the decompiled script).
A character without a key press (IME, dead keys) is typed with scan code 0 (GUESSED). Auto-repeat presses are dropped (the game
polls held keys). Focus loss releases every held key. The key and edge queues are bounded at 32 (GUESSED: a DirectInput buffer is
bounded; unread keys must not pile up while driving).

**Timing.** Host step = 1/30 s fixed (`--host-hz`), the scene is redrawn only when the host advanced, the window is presented every
display frame (VSync). No interpolation between host frames (the host has no render-state snapshots): at 60 Hz every scene frame
is shown twice.

**Audio.** The mixer renders exactly one host step of sound per step (GameAudioSink); the sink queues it on one OpenAL streaming
source, never blocks, drops a chunk when > 0.25 s is queued and restarts after an underrun once 2 chunks are queued. Pause stops the
source. UNTESTED with a device (none here); in the sandbox both libraries load and report no device, and the app falls back to the
NullSink with a log line (MEASURED).

## Proof run (Linux, Xvfb 1600x1200x24, Mesa 25.2.8 llvmpipe, 4 shared vCPUs, other agents running)

```
LIBGL_ALWAYS_SOFTWARE=1 xvfb-run -a -s "-screen 0 1600x1200x24" dotnet src/Slrr.App/bin/Debug/net8.0/slrr-app.dll \
    --root . --input-script builtin:drive --lockstep --size 1024x768 --out out
dotnet test src/Slrr.App/App.sln          # 56 tests (2 corpus tests need the game data, ~8 s)
dotnet src/Slrr.App/bin/Debug/net8.0/slrr-app.dll --root . --headless-frames 3000 --input-script builtin:drive
```

`builtin:drive` (MEASURED, every step through the router): main menu → **click** NEW CAREER → **type** "Bob" → OK → OK → garage →
OK (hint) → **click** "Buy used cars or sell your car" → **key Enter** at "PRESS ENTER" → **click** the buy button (cmd SELECT) → YES →
garage with the car → **click** Choose Track → **click** Drag Track → TAKE IT → track entered → **Up held** 8 s. 10 synthetic clicks,
each a mouse move/press/release at the window position computed from the gadget's box and checked to have reached a gadget.

| Run | Result |
|---|---|
| windowed, lockstep, 1024x768 | script finished, exit 0, **0 GL errors**, Drag Track at **73.6 km/h**, 6853 rpm, 3rd gear after 8 s of Up, money 20000 → 3999, 0 script thread errors |
| frame times (lockstep, 932 frames) | displayed frame mean 66.8 ms (p50 54.2, p95 135.7, max 2880 = map load); host step mean 5.4 ms (p50 4.0); scene draw (CPU submit, no glFinish) mean 21.1 ms (p50 11.8, p95 45.5) |
| windowed, real time (VSync no-op under Xvfb) | script finished, 0 GL errors, 78.6 km/h; displayed frame p50 19.5 ms / mean 74 ms; the clock dropped 17.7 s of wall time (llvmpipe cannot keep 30 Hz while loading maps and dealer cars) |
| app keys, real time, 1280x720 | F3 overlay off/on, F11 → full screen 1600x1200 and back to 1280x720, F12 screenshot, F10 pause/resume, letterbox bars 160 px each side: all as expected, 0 GL errors |
| headless (`--headless-frames`) | same script, 932 host frames in 6.4 s wall, host step mean 4.4 ms, 73.6 km/h |
| memory | peak working set 816-878 MB windowed (llvmpipe keeps textures in process memory; a GPU driver does not) |

Images were inspected, not committed (frames at `--out`): the main menu with its 3D background, the name dialog, the garage, the
dealer's buy dialog over the car, Track select, and the chase view on Drag Track with the HUD navigator frame and the overlay.

## macOS (UNTESTED: written from the Silk.NET / GLFW / Apple documentation; please report what happens)

* **Context.** macOS offers OpenGL 2.1 (legacy) or 3.2+ **core** only; GLFW must ask for a forward-compatible core context. The app
  (and `GlHost`) request `ContextProfile.Core` + `ContextFlags.ForwardCompatible` 3.3; Apple's driver returns its 4.1 core context.
  The debug-context flag is never set on macOS.
* **Main thread.** `Main` is synchronous; the window, the context, the event pump and every GL call stay on the first thread (Cocoa
  requires it). Nothing in the app starts threads.
* **Retina.** GLFW reports window size and cursor in points and the framebuffer in pixels (`GLFW_COCOA_RETINA_FRAMEBUFFER` is on by
  default). `ViewportMapper` multiplies the cursor by framebuffer/window (unit-tested with a 2x framebuffer); the offscreen target is
  sized in pixels (use `--render-scale 0.5` if a 5K display is slow).
* **Native libraries.** `Ultz.Native.GLFW` ships `libglfw.3.dylib` for osx-arm64 and osx-x64, `Silk.NET.OpenAL.Soft.Native` ships
  `libopenal.dylib` for both; `dotnet run`/`dotnet build` resolve them from `runtimes/osx-*/native` via the deps file, a RID publish copies
  them next to the executable (MEASURED by cross-publishing below). Audio tries the bundled OpenAL Soft first (CoreAudio backend), then
  `/System/Library/Frameworks/OpenAL.framework` (deprecated by Apple but still shipped), then plays nothing with a log line.
* **Apple Silicon.** `Directory.Build.props` marks every assembly x64, which an arm64 .NET process refuses to load
  (BadImageFormatException; documented .NET behaviour, UNTESTED here). MEASURED by cross-publishing `-r osx-arm64` from Linux and reading
  the PE headers: without the flag below the 18 `Slrr.*` libraries are AMD64-only; with `-p:PlatformTarget=AnyCPU` all 109 app/library
  assemblies are AnyCPU, `slrr-app`, `libglfw.3.dylib` and `libopenal.dylib` are Mach-O arm64. So always pass `-p:PlatformTarget=AnyCPU`
  on an arm64 Mac (it applies to all referenced projects; a clean build or a separate configuration avoids stale x64 outputs):
  ```
  dotnet publish src/Slrr.App/Slrr.App.csproj -c Release -r osx-arm64 --self-contained -p:PlatformTarget=AnyCPU -o out/osx-arm64
  dotnet run -c Release -p:PlatformTarget=AnyCPU --project src/Slrr.App/Slrr.App.csproj -- --root ~/Games/SLRR
  ```
  Intel Macs (and Rosetta) work with the default x64 settings (`-r osx-x64`).
* **Gatekeeper.** The binaries are not signed or notarised. If macOS refuses to start a downloaded build ("cannot be opened",
  "is damaged", or the process is killed), remove the quarantine flag: `xattr -dr com.apple.quarantine out/osx-arm64` (and the game
  directory if it came from a download). An arm64 executable must carry at least an ad-hoc signature; the .NET SDK signs the apphost
  only when publishing **on** a Mac, so for a build cross-published from Linux/Windows run `codesign --force --deep -s - out/osx-arm64/slrr-app`
  or start it as `dotnet out/osx-arm64/slrr-app.dll`. Building on the Mac with `dotnet run` avoids both issues.
* **File names.** APFS is case-insensitive by default; the engine already resolves every script/content path case-insensitively and
  accepts `\` separators (`GameFileSystem`, `GameSandbox.Walk`, `ObjectCatalog.ResolvePath`), which is what lets it run on Linux's
  case-sensitive file systems (MEASURED: every run here is on Linux). A case-sensitive APFS volume behaves like Linux.
* **Shader audit (both renderer projects, every GLSL source):** see the next section; nothing found that Apple's 4.1 core driver rejects.

### OpenGL / GLSL audit for macOS core profiles

| Check | Slrr.Render / Slrr.Game.Render | Status |
|---|---|---|
| `#version` | all 6 shaders `#version 330 core` (mesh, overlay, billboard; app overlay) | OK on 4.1 core |
| `layout(binding=)` (GLSL 4.20) | not used; samplers are set with `glUniform1i` | OK |
| `layout(location=)` on vertex inputs | used (GLSL 3.30 feature) | OK |
| `#extension`, ARB/EXT calls, `GL_EXTENSIONS` string | none | OK |
| `gl_FragColor`, `texture2D`, `varying`/`attribute` | none (`out vec4 FragColor`, `texture()`) | OK |
| `gl_PointSize`, wide lines (`glLineWidth` > 1 is an error in forward-compatible contexts), `GL_QUADS`, `glPolygonMode` | none (triangles only) | OK |
| VAO bound for every draw / attribute setup (VAO 0 is invalid in core) | yes (mesh, overlay, billboard, app overlay) | OK |
| removed formats (`GL_LUMINANCE/ALPHA`, `GL_CLAMP`) | none (`RGBA8`, `ClampToEdge`, `Repeat`) | OK |
| sRGB | none (no sRGB textures or framebuffer; colours are as in the data) | OK, no gamma change |
| texture size | largest loose DDS/PNG in the data 2048 px (2,194 files scanned, MEASURED); Apple GPUs allow 16384 | OK (PTX not scanned) |
| samplers whose branch is not taken left without a texture (unit 1 decal, unit 2 env) | Apple's driver logs "unit N GLD_TEXTURE_INDEX_2D is unloadable" and may validate slowly | **fixed**: a 1x1 white texture is bound to unused units (`SceneRenderer.Bind`) |
| `glFinish` every frame (offscreen timing) | stalls CPU/GPU overlap in a window | **fixed**: `SceneRenderer.FinishEachFrame` / `GameRenderOptions.FinishEachFrame` (default true as before, the app turns it off) |
| `glGetError` loops | none in the renderers; the app drains at most 16 per frame (a lost context can return errors forever) | OK |
| blit offscreen → window | `glBlitFramebuffer` colour only, RGBA8 renderbuffer to the default framebuffer, no multisampling on either side (a sample-count mismatch is an error) | OK |

## Windows notes (UNTESTED)

GLFW makes the process per-monitor DPI aware; window size and cursor are in pixels there, so a 1024x768 window is small on a 200 %
display (pass `--size`). Audio uses the bundled `soft_oal.dll` (Windows has no system OpenAL). Publish: `dotnet publish
src/Slrr.App/Slrr.App.csproj -c Release -r win-x64 --self-contained -o out/win-x64`.

## Troubleshooting

| Symptom | Cause / fix |
|---|---|
| `game data not found` | pass `--root` or set `SLRR_GAME_ROOT` to the directory with `system.rpk` and `sl.rpk` |
| exit 3 `cannot open a window with an OpenGL 3.3 core context` | old driver / remote desktop without GL / Linux without a display: update the driver; on Linux CI use `xvfb-run -a` with `LIBGL_ALWAYS_SOFTWARE=1`, or `--headless-frames n` |
| no sound | see `audio:` lines in the log: no OpenAL device → NullSink; `--wav out.wav` to check the mix |
| slow | `--render-scale 0.5`, smaller `--size`, check the overlay (F3): DRAW is the scene, STEP the scripts + physics |
| keyboard layout | keys are physical positions (DirectInput behaviour); text typed into the career name uses the layout's characters |
| macOS "damaged"/killed | Gatekeeper / unsigned arm64: `xattr -dr com.apple.quarantine <dir>`, `codesign --force --deep -s - slrr-app` |
| log | `<user data>/logs/slrr-app.log` (path printed at start) |

## Test-only input scripts (`--input-script`)

One command per line, `#` comments; every action becomes a raw window event through the router (not a direct UI-driver call):
`wait s`, `until section|screen|text "..."|track [timeout]`, `click "label"`, `click~ "part"`, `clickcmd SELECT|n`, `key Name`,
`keydown/keyup Name`, `type "text"`, `mouse x y` (800x600 virtual), `pad Axis value` / `pad button:A 1`, `on screen~ Name click "label"`,
`expect section X | track | text "..." | speed > v`, `capture name`, `print`, `quit`. A click that reaches no gadget, a timeout or a failed
expectation fails the run (exit 1).

## Changes outside `src/Slrr.App` (minimal, for this area)

* `src/Slrr.Render/Gl/GlHost.cs`: `GlHost.FromWindow(IWindow)` (wraps an app window's context; does not own the window).
* `src/Slrr.Render/Gl/GlResources.cs`: `OffscreenTarget.Framebuffer` (GL name, for the blit).
* `src/Slrr.Game.Render/Gl/SceneRenderer.cs`: `FinishEachFrame` (default true = old behaviour); white texture on unused sampler units.
* `src/Slrr.Game.Render/GameRenderAdapter.cs`: `Resize(w, h)`, `DrawNow()`, `Width/Height`, `TargetFramebuffer`, `GameRenderOptions.FinishEachFrame`.
* `src/Slrr.Race/RaceHost.cs`: `RaceHostOptions.SaveRoot`, `AudioOutput`, `RenderHost`, `RenderFinishEachFrame` (all default to the old behaviour).

## Integration (for the lead)

1. `Slrr.sln`: add `src/Slrr.App/Slrr.App.csproj` and `tests/Slrr.App.Tests/Slrr.App.Tests.csproj` (project-local `src/Slrr.App/App.sln` builds both).
2. CLI: `tools/Slrr.Cli/Slrr.Cli.csproj` gets `<ProjectReference Include="..\..\src\Slrr.App\Slrr.App.csproj" />`, `Commands.cs` gets
   `"app" => CmdApp.Run(args[1..], o, e),` and a usage line (`CmdApp.Usage`). Until then `CmdApp.cs` does not compile in Slrr.Cli
   (verified to build warnings-as-errors clean with the reference added).
3. CI (optional): `dotnet src/Slrr.App/bin/Release/net8.0/slrr-app.dll --root . --headless-frames 3000 --input-script builtin:drive`
   (exit 0) and the windowed run under `xvfb-run` above.
4. Consider `PlatformTarget` AnyCPU in `Directory.Build.props` (keeps x64 on x64 machines, lets Apple Silicon and Windows on Arm run natively).

## Open questions

1. Real hardware: frame times on a GPU, VSync pacing, Retina mapping, Windows DPI, OpenAL device output and latency — all UNTESTED.
2. Mouse semantics beyond clicks: dragging inventory items onto the car (`EC_LDROP`), slider dragging, right-button camera control in the
   garage, the mouse wheel — not wired (the clicks and hover are).
3. The original's gadget hit area (full vs half box extents; viewport offsets for non-full-screen Osds) and icon-button sizes.
4. Gamepad: the shipped control set names the author's DirectInput pads; mapping a pad as analog keyboard keys is a stand-in.
5. No interpolation between 30 Hz host frames; a 60 Hz host (`--host-hz 60`) was not measured for physics/script equivalence.
