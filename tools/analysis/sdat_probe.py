#!/usr/bin/env python3
"""Decode a java.io.File ("SDAT") data file: 8-byte header, then raw little-endian int32/float32 values and
strings stored as u32 (length including NUL) + bytes + NUL. Verified against save/controls/active_control_set
(ControlSet.load layout, see docs/engine-api/java.io.md).

Usage: python3 -I sdat_probe.py FILE      (read-only)
"""
import struct
import sys


def main(path):
    d = open(path, "rb").read()
    print("magic", d[:4], "version bytes", d[4:8].hex(), "size", len(d))
    p = 8
    rd = lambda: struct.unpack_from("<i", d, p)[0]
    fid = rd(); p += 4
    ver = rd(); p += 4
    print(f"id {fid & 0xffffffff:#x} ({d[8:12]}) version {ver}")
    ndev = rd(); p += 4
    names = []
    for _ in range(ndev):
        n = rd(); p += 4
        names.append(d[p:p + n - 1].decode("latin1")); p += n
    print("devices", names)
    nitems = rd(); p += 4
    print("items", nitems, "bytes per item 36 ->", p + nitems * 36, "of", len(d))
    items = []
    for _ in range(nitems):
        v = struct.unpack_from("<iiii5f", d, p); p += 36
        items.append(v)
    print("first items (group, vaxis, device, axis, from_min, from_max, to_min, to_max, dead):")
    for it in items[:5]:
        print("  ", [round(x, 3) if isinstance(x, float) else x for x in it])
    nv = rd(); p += 4
    print("virtual-axis smooth records", nv, "remaining bytes", len(d) - p)


if __name__ == "__main__":
    main(sys.argv[1])
