# BON / SKL (`.bon`, `.skl`) skeletons

Plain text, same record format in both extensions. 133 files (115 `.bon`, 18 `.skl`), 1,092 bones,
all parse.

Each bone is exactly 9 non-blank lines:

```
Bip01 Spine                         name (may contain spaces)
3                                   parent index, -1 = root
1.000000, 0.000001, -0.000796       rotation matrix row 0
-0.000001, 1.000000, -0.000004      row 1
0.000796, 0.000004, 1.000000        row 2
9.936633, 0.000014, 0.018537        position (relative to parent, hypothesis)
-0.261799, 0.261799                 X limit (min, max), radians
-0.785398, 0.261799                 Y limit
-0.261799, 0.261799                 Z limit
```

Part rigs (`bone00`, `bone01`, ...) use -1 parents and +/-pi limits; human rigs use `Bip01 *`
(3ds Max Biped names). `.skl` files repeat the `.bon` bones with full +/-pi limits (driver.skl vs driver.bon).

Open: whether the parent index can point forward, the matrix convention (row vs column), and how the
limits feed the ragdoll / IK.
