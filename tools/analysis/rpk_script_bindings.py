#!/usr/bin/env python3
"""Scan every .rpk under ROOT and extract the text entries that bind a resource id to a script class / native cfg.

Output JSON: list of {rpk, id, super, type, alias, kv: [[key, value], ...]} for text entries that contain a 'script'
or 'native' line, plus a summary of all key words seen in text entries. Read-only.

Usage: python3 -I rpk_script_bindings.py ROOT OUT.json
"""
import json
import os
import struct
import sys
from collections import Counter


def read_rpk(path):
    d = open(path, "rb").read()
    assert d[:4] == b"RPAK"
    ref_count = struct.unpack_from("<I", d, 8)[0]
    refs = []
    p = 16
    for _ in range(ref_count):
        refs.append(d[p + 4:p + 64].split(b"\0")[0].decode("latin1"))
        p += 64
    table_size, n, _, _ = struct.unpack_from("<IIII", d, p)
    p += 16
    ents = []
    for _ in range(n):
        sup, eid, typ, add, pc, off, size, al = struct.unpack_from("<IIBBfIIB", d, p)
        p += 23
        alias = d[p:p + al].rstrip(b"\0").decode("latin1")
        p += al
        if typ == 1:
            p += 48
        ents.append(dict(super=sup, id=eid, type=typ, add=add, off=off, size=size, alias=alias))
    return d, refs, ents


def main(root, out):
    res = []
    keys = Counter()
    for dp, dn, fn in os.walk(root):
        dn[:] = [x for x in dn if not x.startswith(".")]
        for f in sorted(fn):
            if not f.lower().endswith(".rpk"):
                continue
            path = os.path.join(dp, f)
            try:
                d, refs, ents = read_rpk(path)
            except Exception as e:  # noqa
                print("skip", path, e)
                continue
            for e in ents:
                if e["type"] == 1 or e["size"] == 0 or e["size"] > 4096:
                    continue
                raw = d[e["off"]:e["off"] + e["size"]]
                if raw[:4] == b"ICFG" or any(b < 9 or (b > 13 and b < 32) or b > 126 for b in raw):
                    continue
                kv = [ln.split(None, 1) + [""] for ln in raw.decode("latin1").replace("\r", "").split("\n") if ln.strip()]
                kv = [[x[0], x[1].strip()] for x in kv]
                for k, _ in kv:
                    keys[k] += 1
                if any(k in ("script", "native") for k, _ in kv):
                    res.append(dict(rpk=f, refs=refs, id=e["id"], super=e["super"], type=e["type"], alias=e["alias"], kv=kv))
    json.dump(dict(bindings=res, keys=keys.most_common()), open(out, "w"))
    print(len(res), "binding entries;", len(keys), "distinct text keys")


if __name__ == "__main__":
    main(sys.argv[1], sys.argv[2])
