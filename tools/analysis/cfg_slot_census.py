#!/usr/bin/env python3
"""Census of `slot` ids in the car main cfgs (cars/**/_main_*.cfg): for every slot id how many cars declare it, the most
common trailing comment, the `slottype` / `slotdmgmode` that follow, and the `attach` target(s).

Usage: python3 -I cfg_slot_census.py ROOT [MIN_CARS]     (read-only)
"""
import os
import re
import sys
from collections import Counter, defaultdict


def main(root, min_cars=8):
    ids = defaultdict(lambda: dict(cars=set(), comments=Counter(), types=Counter(), attach=Counter(), dmg=Counter()))
    ncars = 0
    for dp, dn, fn in os.walk(os.path.join(root, "cars")):
        for f in fn:
            if not (f.startswith("_main_") and f.endswith(".cfg")):
                continue
            ncars += 1
            cur = None
            for ln in open(os.path.join(dp, f), encoding="latin1"):
                code, _, com = ln.partition(";")
                code = code.split("//")[0]
                t = code.split()
                if not t:
                    continue
                if t[0] == "slot" and len(t) >= 8:
                    try:
                        cur = int(t[7])
                    except ValueError:
                        cur = None
                        continue
                    ids[cur]["cars"].add(f)
                    if com.strip():
                        ids[cur]["comments"][com.strip()[:48]] += 1
                elif cur is not None and t[0] == "slottype" and len(t) > 1:
                    ids[cur]["types"][t[1]] += 1
                elif cur is not None and t[0] == "slotdmgmode" and len(t) > 1:
                    ids[cur]["dmg"][t[1]] += 1
                elif cur is not None and t[0] == "attach" and len(t) > 2:
                    ids[cur]["attach"][f"{t[1]}:{t[2]}"] += 1
    print(f"{ncars} car main cfgs, {len(ids)} distinct slot ids")
    print("| Slot id | Cars | Typical comment | slottype | stock attach (part:childSlot) |")
    print("|---|---|---|---|---|")
    for sid in sorted(ids):
        d = ids[sid]
        if len(d["cars"]) < min_cars:
            continue
        com = d["comments"].most_common(1)[0][0] if d["comments"] else ""
        ty = ", ".join(k for k, _ in d["types"].most_common(2))
        at = ", ".join(k for k, _ in d["attach"].most_common(2))
        print(f"| {sid} | {len(d['cars'])} | {com} | {ty} | {at} |")


if __name__ == "__main__":
    main(sys.argv[1], int(sys.argv[2]) if len(sys.argv) > 2 else 8)
