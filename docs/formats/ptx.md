# PTX (`.ptx`) textures

Used for particles and UI. All 127 shipped files parse exactly to EOF (339 frames).

```
u32 unknown      0 in all files
u32 frameCount   1 in 90 files; 5, 6, 7, 8 in the 37 animated ones (e.g. Flare.ptx: 7)
u32 width
u32 height
frameCount x {
  u32 totalLen   = colorLen + alphaLen
  u32 colorLen
  u32 alphaLen
  u32 unknown    0
  f32 time       (k+1)/frameCount, e.g. 0.143, 0.286 ... 1.0
  colorLen bytes JPEG (JFIF, starts FF D8 FF)
  alphaLen bytes JPEG (greyscale alpha mask)
}
```

The first JPEG therefore starts at byte 36, not at 28 as the plan guessed (16-byte header +
20-byte frame record). To build an RGBA texture: decode both JPEGs and use the luminance of the
second one as alpha.
