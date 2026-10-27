#!/usr/bin/env python3
"""Extract the script-VM native method registrations from StreetLegal_Redline.exe (static only).

Pattern (MSVC6, __thiscall on the VM object at [0x63641c]):
    mov ecx, [0x63641c]; push FN; push "desc"; push "name"; push "java.class"; call REGISTER
Every direct call whose preceding pushes include a "java.*" class string and a "(...)"
descriptor string is reported, grouped by callee.

Usage: python3 -I native_table.py EXE [--tsv]
"""
import os
import struct
import sys
from collections import defaultdict

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from pe_xref import Image  # noqa: E402


def main() -> None:
    img = Image(sys.argv[1])
    tsv = "--tsv" in sys.argv
    m = img.mem
    # all executable sections (patched builds such as Slrr_GI.exe add a `.code` section)
    ranges = [(s.VirtualAddress, s.VirtualAddress + s.Misc_VirtualSize) for s in img.pe.sections
              if s.Characteristics & 0x20000000]
    found = defaultdict(list)
    offsets = (o for lo, hi in ranges for o in range(lo, hi - 5))
    for o in offsets:
        if m[o] != 0xE8:
            continue
        target = img.base + o + 5 + struct.unpack_from("<i", m, o + 1)[0]
        if not any(lo <= target - img.base < hi for lo, hi in ranges):
            continue
        # walk back over up to 6 `push imm32` (68 xx xx xx xx) and `mov ecx,[abs]` (8B 0D xx xx xx xx)
        pushes = []
        p = o
        for _ in range(8):
            if m[p - 5] == 0x68:
                pushes.append(struct.unpack_from("<I", m, p - 4)[0])
                p -= 5
            elif m[p - 6] == 0x8B and m[p - 5] == 0x0D:
                p -= 6
            elif m[p - 10] == 0xC7 and m[p - 9] == 0x05:  # mov dword ptr [abs], imm32
                p -= 10
            else:
                break
        if len(pushes) < 3:
            continue
        strs = [img.cstr(v) for v in pushes]
        # pushes are in reverse order: first element = last pushed = first argument
        if not (strs[0] and strs[0].startswith("java.")):
            continue
        if not any(s and s.startswith("(") for s in strs[1:]):
            continue
        found[target].append((img.base + o, pushes, strs))
    for target, rows in sorted(found.items(), key=lambda kv: -len(kv[1])):
        print(f"# register function 0x{target:08x}: {len(rows)} calls")
        for site, pushes, strs in rows:
            cls = strs[0]
            rest = [s if s is not None else f"0x{v:08x}" for v, s in zip(pushes[1:], strs[1:])]
            if tsv:
                print("\t".join([f"0x{site:08x}", cls] + rest))
            else:
                print(f"  0x{site:08x} {cls} " + " ".join(rest))


if __name__ == "__main__":
    main()
