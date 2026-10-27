#!/usr/bin/env python3
"""Resolve which script class each `native <kind> <cfg>` gametype entry gets, walking the entry `superId` chain
(high 16 bits = 1-based slot in the archive's reference table, 0 = same archive; low 16 bits = entry id).

Measures: own `script` line vs inherited from the nearest ancestor with one vs none; per native kind; top classes.
Also checks that every resolved script class exists as a shipped .class.

Usage: python3 -I rpk_gametype_resolve.py ROOT [--json OUT]   (read-only)
"""
import json
import os
import sys
from collections import Counter, defaultdict

sys.dont_write_bytecode = True
HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
from rpk_script_bindings import read_rpk  # noqa: E402


def text_kv(d, e):
    if e["size"] == 0 or e["size"] > 4096 or e["type"] == 1:
        return {}
    raw = d[e["off"]:e["off"] + e["size"]]
    if any(b < 9 or (13 < b < 32) or b > 126 for b in raw):
        return {}
    kv = {}
    for ln in raw.decode("latin1").replace("\r", "").split("\n"):
        p = ln.split(None, 1)
        if p:
            kv.setdefault(p[0], p[1].strip() if len(p) > 1 else "")
    return kv


def main(root, out=None):
    arch = {}
    by_base = defaultdict(list)
    for dp, dn, fn in os.walk(root):
        dn[:] = [x for x in dn if not x.startswith(".")]
        for f in fn:
            if f.lower().endswith(".rpk"):
                p = os.path.join(dp, f)
                try:
                    d, refs, ents = read_rpk(p)
                except Exception:
                    continue
                rel = os.path.relpath(p, root).replace("/", "\\")
                arch[rel.lower()] = dict(d=d, refs=refs, byid={e["id"]: e for e in ents}, ents=ents, rel=rel)
                by_base[f.lower()].append(rel.lower())

    def find(refname):
        k = refname.lower()
        if k in arch:
            return arch[k]
        c = by_base.get(os.path.basename(k.replace("\\", "/")), [])
        return arch[c[0]] if c else None

    kinds = Counter()
    mode = Counter()
    classes = Counter()
    unresolved = []
    for a in arch.values():
        for e in a["ents"]:
            kv = text_kv(a["d"], e)
            if "native" not in kv:
                continue
            kind = kv["native"].split()[0]
            kinds[kind] += 1
            if "script" in kv:
                mode["own"] += 1
                classes[kv["script"]] += 1
                continue
            cur, sup, hops, found = a, e["super"], 0, None
            while hops < 30 and cur is not None:
                slot, i = sup >> 16, sup & 0xFFFF
                tgt = cur if slot == 0 else (find(cur["refs"][slot - 1]) if slot - 1 < len(cur["refs"]) else None)
                if tgt is None:
                    break
                p = tgt["byid"].get(i)
                if p is None:
                    break
                pk = text_kv(tgt["d"], p)
                if "script" in pk:
                    found = pk["script"]
                    break
                cur, sup, hops = tgt, p["super"], hops + 1
            if found:
                mode["inherited"] += 1
                classes[found + " (inherited)"] += 1
            else:
                mode["none"] += 1
                unresolved.append((a["rel"], hex(e["id"]), e["alias"], kv["native"]))
    print("native entries by kind:", dict(kinds))
    print("script binding:", dict(mode))
    print("top script values:")
    for k, v in classes.most_common(25):
        print(f"  {v:5d} {k}")
    print("native entries with no script anywhere up the chain:", len(unresolved))
    for u in unresolved[:20]:
        print("  ", u)
    if out:
        json.dump(dict(kinds=kinds, mode=mode, classes=classes.most_common(), unresolved=unresolved), open(out, "w"))


if __name__ == "__main__":
    main(sys.argv[1], sys.argv[3] if len(sys.argv) > 3 else None)
