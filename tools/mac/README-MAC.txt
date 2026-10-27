SLRR C# rewrite - macOS preview build
=====================================

This is an EARLY PREVIEW of the C# (.NET 8) rewrite of the Street Legal Racing: Redline engine.
It is not the original game and it has NOT been compared against it. It needs the game data
(the files of the repository this was built from), which are NOT in this zip.

1. Get the game data (about 480 MB):
     git clone https://github.com/antomys/SLRR-LE2MWM-MethanolFuel-Edition.git
   The folder you get (it contains system.rpk and sl.rpk) is the "game root" below.

2. Unzip this archive somewhere outside the game root, then in Terminal:
     cd <where you unzipped>
     chmod +x slrr slrr-app 2>/dev/null
     xattr -dr com.apple.quarantine .      # the binaries are unsigned; this lets macOS run them

3. Try it (replace ROOT with the game root folder):
     ./slrr coverage ROOT                  # parse every shipped file and print counts
     ./slrr engine-boot ROOT               # boot the script engine to the splash screen
     ./slrr play --smoke --root ROOT       # headless: new career, buy a car, buy a part
     ./slrr race-play --root ROOT --track "Drag Track" --seconds 10
     ./slrr race-play --root ROOT --track "Drag Track" --render --audio --out ~/slrr-out
         (writes PNG frames and a WAV; needs a working OpenGL 3.3 core context)
   If an interactive build is included (a file named slrr-app), run:
     ./slrr-app --root ROOT
   See docs/app.md in the repository for controls.

Saves are written under the folder given by --save (default: a per-user app-data folder).

Known limits: no opponents or finish line in the free-roam maps, City is not drivable yet,
physics and sound are plausible but unverified against the original. Please report what you see.
