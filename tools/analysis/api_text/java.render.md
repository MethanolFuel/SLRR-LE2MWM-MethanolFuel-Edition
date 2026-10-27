# java.render (and java.render.osd, java.render.osd.dialog)

Script roots: `system\Scripts\render\*.class` (14 classes), `render\osd\` (9), `render\osd\dialog\` (12). Natives:
GfxEngine 11, Viewport 11, Text 9, Camera 3 (registered) — plus the `RenderRef`/`ParticleSystem` natives in
`java.util.resource`. All HUD, menus and dialogs are SCRIPT code that builds screens out of these few primitives, so the
renderer contract is small: viewports, cameras, text instances, render instances with a matrix, lights and fog.

## Rendering model seen by scripts

* A **Viewport** is a normalised screen rectangle (0..1) with a layer number (Osd uses its own, the mouse cursor and the
  sound debug view use 127). A **Camera** is a RenderRef created under a parent node, bound to a Viewport. A viewport is
  `activate(flags)`d to start rendering (`RENDERFLAG_CLEARDEPTH` 1 | `RENDERFLAG_CLEARTARGET` 2).
* 2D UI is rendered through the same pipeline: `Osd` creates a Viewport, a `Group` (a Dummy GameType node), then
  `Text` instances (bitmap fonts from frontend.rpk), `Rectangle`s (textured quads) and cursor-aware buttons whose icons are
  `GameRef`s of the `button` native kind (frontend.rpk:0x24). Positions are in normalised OSD coordinates; scripts convert
  using `Config.video_x/y` and the viewport aspect (`Osd.convertTextCoordinates`, `Text.getHeight/getWidth`).
* 3D scenes: a `GroundRef` map root, `RenderRef`s for objects, sun light via `RenderRef.setLight/setFlare`, fog via
  `GroundRef.setFog(rgb, near, far)`, reflection map via `GfxEngine.setGlobalEnvmap`. Cameras are driven by command strings
  (see entry-points.md) rather than by script math.
* Colours: `Text.changeColor` and `RenderRef.setColor` take signed ARGB ints (alpha in the top byte, e.g. -65536 =
  0xFFFF0000); light and fog colours are 24-bit RGB ints.
* The video options (`Config.video_x/y/depth/windowed`) are read by the exe in its start-up function (0x58c833) before the
  `Init` gametype is created; `GfxEngine.changeVideoMode` is only called from the options dialog.

{{CLASS java.render.GfxEngine}}

{{CLASS java.render.Viewport}}

{{CLASS java.render.Camera}}

{{CLASS java.render.Text}}

## Front end and loading screen

{{CLASS java.render.Frontend}}

{{CLASS java.render.LoadingScreen}}

## On-screen display classes

The Osd machinery is pure script. Responsibilities, measured on the decompiled code: `Osd` owns the viewport and the
element lists; `Osd.addHook(subject, handlerObj, suffix)` subscribes `handlerObj` to `EVENT_CURSOR` of the button icon
`subject` with handler name `"event_handler"+suffix`; the cursor event's parameter string starts with the `EC_*` code; a left
click looks up the button and calls `buttonHandler[i].osdCommand(buttonCommand[i])`. Keyboard input arrives through
`Hotkey` objects registered with `Input.createHotkey` and checked by the 50 ms `HotkeyWatcher` against the top Osd of
`Frontend.inputQueue`. Dialogs (`Dialog`, `TextDialog`, `YesNoDialog`, ...) are modal GameTypes whose `display()` blocks
the calling script thread (`wait()`) until the user action calls `notify()`.

### java.render

{{CLASSLIST java.render.}}

### java.render.osd

{{CLASSLIST java.render.osd.}}

### java.render.osd.dialog

{{CLASSLIST java.render.osd.dialog.}}

{{CLASS java.render.Osd}}
