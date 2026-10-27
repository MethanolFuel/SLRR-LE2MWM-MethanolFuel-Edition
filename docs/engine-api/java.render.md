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

### java.render.GfxEngine

`extends java.lang.Object`. Compiled in `system/Scripts/render/GfxEngine.class`.

(H) Static facade for display and video. `Frontend.render` holds the one GfxEngine instance whose monitor the engine notifies each frame (LoadingScreen.run waits on it).

Static fields: `psLastFreeIndex = 100`

| Method | Decl / Reg | Typed sites (callers) | Name-only sites | Arguments seen | Semantics and evidence |
|---|---|---|---|---|---|
| `static void forceRendering()` | native/R | 1 (1) | 0 | (no args) | (H) Renders one frame immediately from inside a script (LoadingDialog calls it so the loading picture appears before a blocking load). e.g. `LoadingDialog`: `GfxEngine.forceRendering()` |
| `static void setGlobalEnvmap(ResourceRef)` | native/R | 0 | 22 (8) |  | (H) `setGlobalEnvmap(ResourceRef cube)` sets the reflection environment map; every track script calls it in enter() with skydome.rpk or MW_Mod.rpk resources (22 name-only sites). |
| `static int numDisplayModes()` | native/R | 0 | 0 |  | (L) Declared/registered, no script site. |
| `static int currDisplayMode()` | native/R | 0 | 0 |  | (L) No site. |
| `static String displayModeName(int)` | native/R | 0 | 0 |  | (L) No site. |
| `static void changeVideoMode(int, int, int)` | native/R | 2 (1) | 1 (1) | 0: 320, 640; 1: 240, 480; 2: 16, 32 | (H) `changeVideoMode(w, h, bitsPerPixel)` (320,240,16 and 640,480,32 seen in OptionsDialog). e.g. `OptionsDialog`: `GfxEngine.changeVideoMode(320, 240, 16)` |
| `static void printScreen(String)` | native/R | 1 (1) | 0 | 0: v1 | (H) Saves a screenshot to the file name; `printScreenIndexed("screenshots\\sl_shot")` (script) appends an index (hotkey AXIS_PRINTSCREEN). e.g. `GfxEngine`: `printScreen(v1)` |
| `static void flush()` | native/R | 14 (14) | 1 (1) | (no args) | (M) Called 14 times by track scripts right after building a scene (DealerTrack, Wichur_Drift_Track): hypothesis: submits/streams pending render instances before the first frame. e.g. `DealerTrack`: `GfxEngine.flush()` |
| `static int openVideo(String, int, int)` | native/R | 5 (5) | 0 | 0: "Data\\Fmv\\Prime.avi" x3, "data\\fmv\\prime.avi"; 1: 1 x4, 0; 2: 1 x4, 0 | (H) `openVideo(path, int, int)` opens an AVI (`Data\\Fmv\\Prime.avi`, args 1,1 in 4 of 5 sites); returns non-zero on success. Shown by MainMenuDialog/TrackSelect. e.g. `CarSelect`: `GfxEngine.openVideo("Data\\Fmv\\Prime.avi", 1, 1)` |
| `static void closeVideo()` | native/R | 6 (5) | 0 | (no args) | (H) Stops and closes the video. e.g. `CarSelect`: `GfxEngine.closeVideo()` |
| `static int isPlayingVideo()` | native/R | 1 (1) | 0 | (no args) | (H) Polled to leave the video screen when playback ends. e.g. `GfxEngine`: `GfxEngine.isPlayingVideo()` |

Decl/Reg: `native` = flagged 0x40 in the shipped .class, `script` = non-native in the .class but registered natively in an exe, `undeclared` = registered in an exe but missing from the shipped .class; `R` = registered in `StreetLegal_Redline.exe`, `G` = registered in `Slrr_GI.exe` (patched build), `-` = no registration found. Typed sites = call sites that name this method with its descriptor (distinct caller classes in brackets); name-only = sites that only name the method, counted over all classes (README conventions).

Script members: `playVideo(String)`, `printScreenIndexed(String)`


### java.render.Viewport

`extends java.util.resource.ResourceRef`. Compiled in `system/Scripts/render/Viewport.class`.

(H) Screen rectangle (normalised 0..1 coordinates) that cameras render into; a ResourceRef (RESOURCE_VIEWPORT=18). Created by `new Viewport(int layer, float x, float y, float w, float h)`; the 7-arg script ctor letterboxes to an aspect ratio. Osd owns one per screen.

Static fields: `RENDERFLAG_CLEARDEPTH = 1`, `RENDERFLAG_CLEARTARGET = 2`

| Method | Decl / Reg | Typed sites (callers) | Name-only sites | Arguments seen | Semantics and evidence |
|---|---|---|---|---|---|
| `void create(int, float, float, float, float)` | native/R | 1 (1) | 17 (10) | 0: p0; 1: p1; 2: p2; 3: p3; 4: p4 | (H) Native create(layer, x, y, w, h); layer is e.g. 127 for the sound debug view and the mouse cursor; Osd takes it as the last argument of its own constructor. e.g. `Viewport`: `create(p0, p1, p2, p3, p4)` |
| `void destroy()` | native/R | 6 (4) | 14 (7) | (no args) | (H) Destroys the viewport (finalize calls it). e.g. `Navigator`: `vp.destroy()` |
| `void activate(int)` | native/R | 1 (1) | 9 (7) | 0: RENDERFLAG_CLEARDEPTH | (H) `activate(int renderFlags)`: starts rendering into it; RENDERFLAG_CLEARDEPTH=1, RENDERFLAG_CLEARTARGET=2 (CarMarket passes both). The no-arg script form passes CLEARDEPTH. e.g. `Viewport`: `activate(RENDERFLAG_CLEARDEPTH)` |
| `void deactivate()` | native/R | 3 (3) | 1 (1) | (no args) | (H) Stops rendering into it. e.g. `Track`: `vp2.deactivate()` |
| `float getAspect()` | native/R | 3 (1) | 0 | (no args) | (H) Width/height ratio. e.g. `Osd`: `vp.getAspect()` |
| `float getWidth()` | native/R | 2 (2) | 2 (2) | (no args) | (H) Normalised width. e.g. `Osd`: `vp.getWidth()` |
| `float getHeight()` | native/R | 12 (3) | 2 (2) | (no args) | (H) Normalised height (Text.getHeight divides pixel font height by Config.video_y * height). e.g. `Osd`: `vp.getHeight()` |
| `float getTop()` | native/R | 1 (1) | 0 | (no args) | (H) Normalised top. e.g. `Osd`: `vp.getTop()` |
| `float getLeft()` | native/R | 1 (1) | 0 | (no args) | (H) Normalised left. e.g. `Osd`: `vp.getLeft()` |
| `void resize(float, float, float, float)` | native/R | 3 (2) | 0 | 0: p0, 0; 1: p1, 0; 2: p2, 1; 3: p3, 1 | (H) `resize(l, t, w, h)` (Navigator minimap resizes between small/large). e.g. `Navigator`: `vp.resize(p0, p1, p2, p3)` |
| `Vector3 unproject(Vector3, int)` | native/R | 0 | 0 |  | (L) Screen-to-world ray helper `(Vector3 screen, int)`; no site. |


Script members: `<init>()`, `<init>(int, float, float, float, float)`, `<init>(int, float, float, float, float, float, float)`, `finalize()`, `activate()`


### java.render.Camera

`extends java.util.resource.RenderRef`. Compiled in `system/Scripts/render/Camera.class`.

(H) RenderRef subclass. Script ctor Camera(parentNode, Viewport, int flags[, aov, near, far, lodBias, lodAmp, culling, planeTest]) passes `aov*0.5` to the native create (DEF_AOV 90). Camera objects are driven by command strings (see entry-points.md): "look", "move", "dist", "angle", "zoom", "smooth", "force", "torque", "roll", "render", "viewport", "hide".

Static fields: `DEF_AOV = 90.0f`, `DEF_DMIN = 0.1f`, `DEF_DMAX = 100.0f`, `DEF_LODBIAS = 1.0f`, `DEF_LODAMP = 1.0f`, `DEF_CULLING = 1`, `DEF_PLANETEST = 0`

| Method | Decl / Reg | Typed sites (callers) | Name-only sites | Arguments seen | Semantics and evidence |
|---|---|---|---|---|---|
| `void create(ResourceRef, Viewport, int, float, float, float, float, float, int, int)` | native/R | 0 | 17 (10) |  | (H) Native create(parent, viewport, flags, halfFov, near, far, lodBias, lodAmp, culling, planeTest). |
| `void destroy()` | native/R | 6 (5) | 14 (7) | (no args) | (H) Destroys the camera instance. e.g. `InventoryPanel`: `cam.destroy()` |
| `void activate(Viewport, int)` | native/- | 0 | 9 (7) |  | (L) Declared native (Viewport, int flags), NOT registered in the Redline exe; the by-name sites are Viewport.activate. |
| `void deactivate(Viewport)` | native/- | 0 | 1 (1) |  | (L) Declared native, not registered. |
| `void setFog(int, float, float)` | undeclared/R | 0 | 0 |  | (L) Registered in the exe as Camera.setFog(int,float,float) but not declared in the shipped Camera.class (fog is set via GroundRef.setFog). |


Script members: `<init>(int)`, `<init>(ResourceRef, Viewport, int)`, `<init>(ResourceRef, Viewport, int, float, float, float)`, `<init>(ResourceRef, Viewport, int, float, float, float, float, float, int, int)`, `finalize()`


### java.render.Text

`extends java.lang.Object`. Compiled in `system/Scripts/render/Text.class`.

(H) On-screen text instance. Script ctor Text(Group parent, ResourceRef font, String text, float x, float y[, int yAdjust, Viewport vp]) calls native create(parentNode, font, x, y) then changeText(text) (script: stores `text`, calls update()). finalize destroys `renderinst`. Fonts are bitmap font resources in frontend.rpk (RID_SLII24 = frontend.rpk:0x25, RID_CONSOLE5 = frontend.rpk:0xB2, ...). Alignment ALIGN_RIGHT=0, CENTER=1, LEFT=2.

Static fields: `RID_CONSOLE5 = frontend.rpk:0xB2r`, `RID_CONSOLE10 = frontend.rpk:0x23r`, `RID_SL28 = frontend.rpk:0x2Er`, `RID_SLII11 = frontend.rpk:0x138r`, `RID_SLII17 = frontend.rpk:0x139r`, `RID_SLII24 = frontend.rpk:0x25r`, `RID_SL14 = frontend.rpk:0x8Cr`, `RID_SIMPLE20 = frontend.rpk:0x20r`, `RID_SIMPLE40 = frontend.rpk:0x22r`, `RID_SYM_POINTERS = frontend.rpk:0x3Cr`, `ALIGN_LEFT = 2`, `ALIGN_CENTER = 1`, `ALIGN_RIGHT = 0`, `DEF_COLOR = -1`

Fields: `String text`, `RenderRef renderinst`

Engine-read field candidates (the exact field name is pushed as a string constant by exe code; first code xref in `StreetLegal_Redline.exe` after the `@`; short names can be coincidental): `text@0x48737c`, `renderinst@0x48707b`

| Method | Decl / Reg | Typed sites (callers) | Name-only sites | Arguments seen | Semantics and evidence |
|---|---|---|---|---|---|
| `static float getWidthPixels(String, ResourceRef)` | native/R | 1 (1) | 0 | 0: p0; 1: p1 | (H) Static `getWidthPixels(String s, ResourceRef font)` returns the pixel width; script getWidth normalises by Config.video_x * viewport width. e.g. `Text`: `getWidthPixels(p0, p1)` |
| `void create(ResourceRef, ResourceRef, float, float)` | native/R | 1 (1) | 17 (10) | 0: p0; 1: p1; 2: p3; 3: p4 | (H) Native create(ResourceRef parentNode, ResourceRef font, float x, float y). e.g. `Text`: `create(p0, p1, p3, p4)` |
| `void changeColor(int)` | native/R | 61 (16) | 41 (10) | 0: -4144960 x17, COLOR_UNSELECTED x11 | (H) ARGB int (signed): -4144960 = 0xFFC0C0C0, -10752 = 0xFFFFD600, -65536 = red; COLOR_UNSELECTED constants in menus. 61 typed sites. e.g. `DealerTrack`: `DialogText.changeColor(-10752)` |
| `void changeAlign(int)` | native/R | 1 (1) | 1 (1) | 0: p2 | (H) One of the ALIGN_* ints. e.g. `Osd`: `v8.changeAlign(p2)` |
| `void setPos(float, float)` | native/R | 0 | 1 (1) |  | (L) No typed site. |
| `Vector3 getPos()` | native/R | 0 | 2 (2) |  | (L) No typed site. |
| `void update()` | native/R | 1 (1) | 1 (1) | (no args) | (H) Re-uploads the `text` field after a change (changeText calls it). e.g. `Text`: `update()` |
| `int getDefaultColor()` | undeclared/R | 0 | 0 |  | (L) Registered, not declared in shipped Text.class. |
| `void setDefaultColor(int)` | undeclared/R | 0 | 0 |  | (L) Registered, not declared in shipped Text.class. |


Script members: `getFontSize(ResourceRef)`, `getHeight(ResourceRef, Viewport)`, `getWidth(String, ResourceRef, Viewport)`, `<init>(ResourceRef, ResourceRef, String, float, float)`, `<init>(ResourceRef, ResourceRef, String, float, float, int, Viewport)`, `finalize()`, `changeText(String)`


## Front end and loading screen

### java.render.Frontend

`extends java.lang.Object`. Compiled in `system/Scripts/render/Frontend.class`.

(H) Static holder of the 2D front end: `render` (GfxEngine instance whose monitor the engine notifies every frame), `loadingScreen`, `inputQueue` (Vector of Osd, top = receives hotkeys), `pointers`/fonts (frontend.rpk resources chosen by Config.video_x/y), `hotkeyWatcherThread` (HotkeyWatcher polls every 50 ms). `init()` is called by Init.<init>. The exe reads the static field `render` by name every frame (0x428db9) and notifies it.

Static fields: `defLoadingPic = new java.util.resource.ResourceRef(frontend.rpk:0xA8r)`, `SFX_MONEY = frontend.rpk:0x9Br`, `SFX_QUESTION = frontend.rpk:0x7Er`, `SFX_OK = frontend.rpk:0x80r`, `SFX_WARNING = frontend.rpk:0x81r`, `SFX_MENU_MOVE = frontend.rpk:0x79r`, `SFX_MENU_BACK = frontend.rpk:0x78r`, `SFX_MENU_SELECT = frontend.rpk:0x7Ar`, `inputQueue = new java.util.Vector()`

Fields: `ResourceRef largeFont`, `ResourceRef mediumFont`, `ResourceRef smallFont`, `ResourceRef pointers`, `LoadingScreen loadingScreen`, `Thread hotkeyWatcherThread`, `GfxEngine render`

Engine-read field candidates (the exact field name is pushed as a string constant by exe code; first code xref in `StreetLegal_Redline.exe` after the `@`; short names can be coincidental): `render@0x428dc8`

Script members: `init()`, `destroy()`, `setDefLoadingPic(ResourceRef)`, `setFonts()`, `rebuild()`


### java.render.LoadingScreen

`extends java.lang.Thread`. Compiled in `system/Scripts/render/LoadingScreen.class`.

(H) Thread subclass; shows/hides the modal loading dialog and the 'PRESS ENTER TO CONTINUE...' flash while System.isLoading() is true. Wakes on Frontend.render once per frame.

Fields: `RenderRef loadingObject`, `int init`, `int waitForUser`, `float waitForUserTimeLimit`, `LoadingDialog loadingDialog`, `Object termSig`, `Thread flashTextThread`

Engine-read field candidates (the exact field name is pushed as a string constant by exe code; first code xref in `StreetLegal_Redline.exe` after the `@`; short names can be coincidental): `init@0x4289a6`

Script members: `<init>()`, `run()`, `display()`, `display(LoadingDialog)`, `display(LoadingDialog, float)`, `userWait()`, `userWait(float)`, `track()`, `track(int)`, `show(LoadingDialog)`, `show(ResourceRef)`, `show()`, `hide()`


## On-screen display classes

The Osd machinery is pure script. Responsibilities, measured on the decompiled code: `Osd` owns the viewport and the
element lists; `Osd.addHook(subject, handlerObj, suffix)` subscribes `handlerObj` to `EVENT_CURSOR` of the button icon
`subject` with handler name `"event_handler"+suffix`; the cursor event's parameter string starts with the `EC_*` code; a left
click looks up the button and calls `buttonHandler[i].osdCommand(buttonCommand[i])`. Keyboard input arrives through
`Hotkey` objects registered with `Input.createHotkey` and checked by the 50 ms `HotkeyWatcher` against the top Osd of
`Frontend.inputQueue`. Dialogs (`Dialog`, `TextDialog`, `YesNoDialog`, ...) are modal GameTypes whose `display()` blocks
the calling script thread (`wait()`) until the user action calls `notify()`.

### java.render

| Class | Extends | Methods | Fields | Natives | Role |
|---|---|---|---|---|---|
| `java.render.Camera` | `java.util.resource.RenderRef` | 9 | 7 | 4 | RenderRef subclass. Script ctor Camera(parentNode, Viewport, int flags[, aov, near, far, lodBias, lodAmp, culling, planeTest]) passes `aov*0.5` to the native create (DEF_AOV 90). Camera objects are driven by command strings (see entry-points.md): "look", "move", "dist", "angle", "zoom", "smooth", "force", "torque", "roll", "render", "viewport", "hide". |
| `java.render.FlashText` | `java.lang.Object` | 2 | 2 | 0 | Runnable that blinks 'PRESS ENTER TO CONTINUE...' (sleep 600 ms on/off). |
| `java.render.Frontend` | `java.lang.Object` | 5 | 16 | 0 | Static holder of the 2D front end: `render` (GfxEngine instance whose monitor the engine notifies every frame), `loadingScreen`, `inputQueue` (Vector of Osd, top = receives hotkeys), `pointers`/fonts (frontend.rpk resources chosen by Config.video_x/y), `hotkeyWatcherThread` (HotkeyWatcher polls every 50 ms). `init()` is called by Init.<init>. The exe reads the static field `render` by name every frame (0x428db9) and notifies it. |
| `java.render.GfxEngine` | `java.lang.Object` | 13 | 1 | 11 | Static facade for display and video. `Frontend.render` holds the one GfxEngine instance whose monitor the engine notifies each frame (LoadingScreen.run waits on it). |
| `java.render.Group` | `java.util.resource.Dummy` | 8 | 4 | 0 | Dummy subclass (a GameType node) used as the parent node for the Texts/Rectangles of one OSD group so they can be shown/hidden together. |
| `java.render.HotkeyWatcher` | `java.lang.Object` | 1 | 0 | 0 | Runnable: `while(1){ Thread.sleep(50); Input.checkHotkeys(Input.cursor.controller, Frontend.inputQueue.lastElement()); }`. |
| `java.render.LoadingScreen` | `java.lang.Thread` | 13 | 7 | 0 | Thread subclass; shows/hides the modal loading dialog and the 'PRESS ENTER TO CONTINUE...' flash while System.isLoading() is true. Wakes on Frontend.render once per frame. |
| `java.render.Osd` | `java.lang.GameType` | 73 | 97 | 0 | Base class of every on-screen display (menus, HUD, dialogs): a GameType with a Viewport, Groups, Texts, Rectangles, Buttons. enableControl() sets the event mask EVENT_CURSOR\|EVENT_HOTKEY and the animate hook; cursor events arrive in event_handler(GameRef,int,String) where `p2.token(0)` is the EC_* code and a click on a registered button calls `buttonHandler[i].osdCommand(buttonCommand[i])`. |
| `java.render.Rectangle` | `java.lang.Object` | 5 | 5 | 0 | Textured quad on screen (from RectangleTemplate). |
| `java.render.RectangleTemplate` | `java.lang.Object` | 2 | 7 | 0 | Template resource + size for Rectangle. |
| `java.render.SimpleLoadingDialog` | `java.render.osd.dialog.LoadingDialog` | 2 | 1 | 0 | Full-screen picture dialog used by LoadingScreen.show(ResourceRef). |
| `java.render.SoftTimer` | `java.lang.Object` | 2 | 2 | 0 | Runnable: `Thread.sleep(waitTime*1000); listener.notify();`. |
| `java.render.Text` | `java.lang.Object` | 14 | 16 | 7 | On-screen text instance. Script ctor Text(Group parent, ResourceRef font, String text, float x, float y[, int yAdjust, Viewport vp]) calls native create(parentNode, font, x, y) then changeText(text) (script: stores `text`, calls update()). finalize destroys `renderinst`. Fonts are bitmap font resources in frontend.rpk (RID_SLII24 = frontend.rpk:0x25, RID_CONSOLE5 = frontend.rpk:0xB2, ...). Alignment ALIGN_RIGHT=0, CENTER=1, LEFT=2. |
| `java.render.Viewport` | `java.util.resource.ResourceRef` | 16 | 2 | 11 | Screen rectangle (normalised 0..1 coordinates) that cameras render into; a ResourceRef (RESOURCE_VIEWPORT=18). Created by `new Viewport(int layer, float x, float y, float w, float h)`; the 7-arg script ctor letterboxes to an aspect ratio. Osd owns one per screen. |


### java.render.osd

| Class | Extends | Methods | Fields | Natives | Role |
|---|---|---|---|---|---|
| `java.render.osd.Button` | `java.render.osd.Gadget` | 4 | 0 | 0 |  |
| `java.render.osd.Gadget` | `java.lang.Object` | 17 | 20 | 0 | Base of interactive widgets (Button, Slider, MultiChoice, StringInput). `sendMessage()`: if the handler is a GameType that has a MessagePort (async mode, e.g. a tuned part) it queues `new Event(cmd, F_GAD_CHANGE)` with `gadget = this` on that port, otherwise it calls `handler.osdCommand(cmd)` synchronously. Event flags: F_KEY_PRESS 1, F_KEY_RELEASE 2, F_GAD_CHANGE 4. |
| `java.render.osd.Label` | `java.lang.Object` | 1 | 1 | 0 |  |
| `java.render.osd.Menu` | `java.lang.Object` | 13 | 8 | 0 | Vertical list of Gadgets with keyboard focus handling. |
| `java.render.osd.MultiChoice` | `java.render.osd.Gadget` | 8 | 6 | 0 |  |
| `java.render.osd.Slider` | `java.render.osd.Gadget` | 16 | 12 | 0 |  |
| `java.render.osd.StringInput` | `java.render.osd.Gadget` | 5 | 2 | 0 |  |
| `java.render.osd.Style` | `java.lang.Object` | 2 | 9 | 0 | Visual style record (fonts, colours, align) passed to Osd.createButton/Slider/Menu. |
| `java.render.osd.TextBox` | `java.lang.Object` | 4 | 12 | 0 |  |


### java.render.osd.dialog

| Class | Extends | Methods | Fields | Natives | Role |
|---|---|---|---|---|---|
| `java.render.osd.dialog.CongratsDialog` | `java.render.osd.dialog.Dialog` | 2 | 2 | 0 |  |
| `java.render.osd.dialog.CsDialog` | `java.render.SimpleLoadingDialog` | 1 | 0 | 0 |  |
| `java.render.osd.dialog.Dialog` | `java.lang.GameType` | 8 | 30 | 0 | Base modal dialog (GameType). Flags DF_MODAL, DF_DEFAULTBG, DF_FULLSCREEN, DF_FREEZE, DF_LEAVEPOINTER; display() blocks the calling script thread until the dialog calls notify (SoftTimer or user action). |
| `java.render.osd.dialog.FileRequesterDialog` | `java.render.osd.dialog.OKCancelDialog` | 6 | 8 | 0 |  |
| `java.render.osd.dialog.LoadingDialog` | `java.render.osd.dialog.Dialog` | 2 | 2 | 0 |  |
| `java.render.osd.dialog.MenuDialog` | `java.render.osd.dialog.Dialog` | 3 | 2 | 0 |  |
| `java.render.osd.dialog.NoYesDialog` | `java.render.osd.dialog.TextDialog` | 2 | 0 | 0 |  |
| `java.render.osd.dialog.OKCancelDialog` | `java.render.osd.dialog.TextDialog` | 2 | 0 | 0 |  |
| `java.render.osd.dialog.StringRequesterDialog` | `java.render.osd.dialog.OKCancelDialog` | 3 | 1 | 0 |  |
| `java.render.osd.dialog.TextDialog` | `java.render.osd.dialog.Dialog` | 3 | 1 | 0 |  |
| `java.render.osd.dialog.WarningDialog` | `java.render.osd.dialog.TextDialog` | 2 | 0 | 0 |  |
| `java.render.osd.dialog.YesNoDialog` | `java.render.osd.dialog.TextDialog` | 2 | 0 | 0 |  |


### java.render.Osd

`extends java.lang.GameType`. Compiled in `system/Scripts/render/Osd.class`.

(H) Base class of every on-screen display (menus, HUD, dialogs): a GameType with a Viewport, Groups, Texts, Rectangles, Buttons. enableControl() sets the event mask EVENT_CURSOR|EVENT_HOTKEY and the animate hook; cursor events arrive in event_handler(GameRef,int,String) where `p2.token(0)` is the EC_* code and a click on a registered button calls `buttonHandler[i].osdCommand(buttonCommand[i])`.

<details><summary>43 static fields with initial values</summary>

`COLOR_UNSELECTED = -4144960`, `COLOR_SELECTED = -13568`, `COLOR_DISABLED = 1891680448`, `COLOR_PRESSED = -64`, `RID_SLD_BACK = frontend.rpk:0x89r`, `RID_SLD_KNOB = frontend.rpk:0x91r`, `RID_ARROWUP = frontend.rpk:0x4Cr`, `RID_ARROWDN = frontend.rpk:0x84r`, `RID_ARROWLF = frontend.rpk:0x85r`, `RID_ARROWRG = frontend.rpk:0x88r`, `RID_OK = frontend.rpk:0x10r`, `RID_CANCEL = frontend.rpk:0x1Dr`, `RID_BACK = frontend.rpk:0x126r`, `RID_EXIT = frontend.rpk:0x135r`, `RRT_HEADERBG = new java.util.resource.ResourceRef(frontend.rpk:0x28r)`, `RRT_TOOLTIP = new java.util.resource.ResourceRef(frontend.rpk:0xCBr)`, `HL_LABELCOLOR = 1`, `HL_BACKLIGHT = 2`, `MD_VERTICAL = 0`, `MD_HORIZONTAL = 1`, `RID_GHOSTBUTTON = frontend.rpk:0x38r`, `RRT_NONE = new java.util.resource.ResourceRef(frontend.rpk:0x18r)`, `RRT_EMPTY = new java.util.resource.ResourceRef(frontend.rpk:0xAFr)`, `RRT_DARKEN = new java.util.resource.ResourceRef(frontend.rpk:0xA2r)`, `RRT_TEST = RRT_NONE`, `HK_STATIC = 4`, `PRIVATEEVENT = -2147483648`, `CMD_MENU_INC = 65536`, `CMD_MENU_DEC = 131072`, `CMD_MENU_UP = 196608`, `CMD_MENU_DN = 262144`, `CMD_MENU_LF = 327680`, `CMD_MENU_RG = 393216`, `CMD_MENU_SEL = 458752`, `CAM_AOV = 60`, `CAM_OFFSET = 5.48f`, `SCALE_FS = 1.47f`, `SCALE_3D = 1.469f`, `OF_MODAL = 1`, `IL_NONE = 0`, `IL_TIPS = 1`, `IL_KEYS = 2`, `IL_MENU = 3`

</details>

<details><summary>54 instance/uninitialised fields</summary>

`int flags`, `ParticleSystem psPress`, `ParticleSystem psMove`, `Viewport vp`, `Camera cam`, `float screenAspect`, `float vpAspect`, `float vpWidth`, `float vpHeight`, `float vpTop`, `float vpLeft`, `int orientation`, `int menuKeysCreated`, `MessagePort alienMp`, `Object globalHandler`, `int iLevel`, `float lastUserActivityTimeStamp`, `int focusTracker`, `int controlCount`, `int prevKey`, `int prevKeyCount`, `float sliderValue`, `int multiValue`, `String stringValue`, `GameRef dropObject`, `Gadget dropGadget`, `GameRef[] button`, `Object[] buttonHandler`, `int[] buttonCommand`, `int[] textIndex`, `String[] description`, `int buttons`, `Vector text`, `Vector textbox`, `Vector groups`, `Vector hotkey`, `Vector object`, `Vector rectangles`, `int init`, `int defSelection`, `Gadget pointedGadget`, `Group pointedGadgetGroup`, `int activeCommand`, `int selectionMoved`, `int selected`, `int clickLock`, `Gadget actDown`, `Gadget actGadget`, `int controlMode`, `Gadget lastPointedGadget`, `Text tooltipText`, `Rectangle tooltipRect`, `Text textInput`, `String textInputString`

</details>

Engine-read field candidates (the exact field name is pushed as a string constant by exe code; first code xref in `StreetLegal_Redline.exe` after the `@`; short names can be coincidental): `flags@0x487d06`, `cam@0x482d60`, `description@0x5469e6`, `text@0x48737c`, `init@0x4289a6`

<details><summary>73 script-implemented members</summary>

`<init>()`, `<init>(float, float, int)`, `<init>(float, float, float, float, float, float, int)`, `<init>(Viewport)`, `finalize()`, `rebuild()`, `getViewport()`, `mapClickPosH(Vector3, float, float)`, `idleFor(float)`, `getIdleTime()`, `setIdleTime(float)`, `deactivateText()`, `activateText(Group, Gadget)`, `changeSelection2(int, int)`, `changeSelection(int, int)`, `animate()`, `handleEvent(Hotkey)`, `event_handler(GameRef, int, String)`, `show()`, `hide()`, `show(int)`, `hide(int)`, `enableControl()`, `disableControl()`, `findGadget(Object, int)`, `convertTextCoordinates(float, float, int)`, `addHook(GameRef, GameRef)`, `addHook(GameRef, GameRef, String)`, `createMenu(Style, float, float, float, int)`, `createMenu(Style, float, float, float)`, `createMenu(Style, float, float, float, Style, Style)`, `createHeader(String)`, `createBG(ResourceRef)`, `createRectangle(float, float, float, float, int, ResourceRef)`, `createRectangle(float, float, float, float, float, float, float, int, ResourceRef)`, `createObject(int, float, float, int)`, `createObject(int, float, float)`, `createObject(int, Vector3)`, `createObject(int, Vector3, Ypr)`, `createText(String, ResourceRef, int, float, float, int)`, `createText(String, ResourceRef, int, float, float)`, `createTextBox(String, ResourceRef, int, float, float, float)`, `createTextBox(String, ResourceRef, int, float, float, float, ResourceRef)`, `createTextBox(String, ResourceRef, int, float, float, float, ResourceRef, int)`, `createButton(Style, float, float, int, String)`, `createButton(Style, float, float, String, int)`, `createButton(Style, float, float, int, String, int)`, `createSlider(Style, Style, float, float, String, int, float, float, float, int)`, `createSlider(Style, Style, float, float, String, int, float)`, `createMulti(Style, float, float, String, int, int, Vector, int)`, `createMulti(Style, float, float, String, int, int, Vector)`, `createStringInput(Style, float, float, String, int, String)`, `createIcon(int, float, float, GameRef, String)`, `createButton(int, float, float, String, ResourceRef, int, Object, int)`, `createButton(int, float, float, String, ResourceRef, int, GameRef, String)`, `createButton(int, float, float, String, ResourceRef, int, GameRef, String, int)`, `createButton(int, float, float, String, ResourceRef, int, GameRef, String, int, String)`, `createMenuKeys()`, `createHotkey(int, int, int, GameRef)`, `createHotkey(int, int, int, GameRef, int)`, `createHotkey(int, int, int, int)`, `endGroup()`, `beginGroup()`, `hideGroup(int)`, `showGroup(int)`, `resetSelection()`, `setFocus(int)`, `isFocused()`, `setCommandText(int, String)`, `pressEffect()`, `pressEffectStop()`, `hoverEffect()`, `leaveEffect()`

</details>

