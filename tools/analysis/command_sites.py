#!/usr/bin/env python3
"""Command-verb call-site census for the race wave (docs/race/commands.md): every GameRef.command(..) and
queueEvent(.., EVENT_COMMAND, ..) call site in all shipped .class files, ranked by literal verb, with receivers and
calling classes. Static and read-only (uses tufa_decomp.py; never executes game code).

Usage: python3 -I command_sites.py ROOT [VERB ...]     (no verbs: ranking; verbs: every site of those verbs)
Measured on the repository data: 1,692 .class files, 1,760 class definitions, 528 sites, 526 with a literal verb.
"""
import os
import re
import sys
from collections import Counter, defaultdict

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
sys.dont_write_bytecode = True
SKIP = {"src", "tests", "tools", "docs", "bin", "obj"}


def collect(root):
    import tufa_decomp as td
    sites, files, defs = [], 0, 0
    for dp, dn, fn in os.walk(root):
        dn[:] = sorted(x for x in dn if not x.startswith(".") and x not in SKIP)
        for f in sorted(fn):
            if not f.lower().endswith(".class"):
                continue
            files += 1
            for cl in td.parse_file(os.path.join(dp, f)):
                defs += 1
                d = td.Dec(cl)
                for m in cl["methods"]:
                    if m["flags"] & 0x40 or m["body"] >= len(cl["bodies"]):
                        continue
                    d.calls = []
                    d.run(cl["bodies"][m["body"]], ("static " if m["static"] else "") + m["name"], m["desc"])
                    for c in d.calls:
                        if c["name"] in ("command", "queueEvent"):
                            sites.append(dict(cls=cl["name"], meth=m["name"], name=c["name"], recv=str(c.get("recv")), args=c["args"]))
    return sites, files, defs


def verb_of(s):
    a = s["args"][-1] if s["args"] else ""
    if s["name"] == "queueEvent" and "EVENT_COMMAND" not in " ".join(s["args"]):
        return None, False
    m = re.search(r'"([A-Za-z_]+)[ "]', a) or re.search(r'"([A-Za-z_]+)"', a)
    return (m.group(1) if m else None), True


def main(root, want):
    sites, files, defs = collect(root)
    verbs, other = defaultdict(list), []
    for s in sites:
        v, cmd = verb_of(s)
        if v:
            verbs[v].append(s)
        elif cmd:
            other.append(s)
    total = sum(len(x) for x in verbs.values())
    print(f"{files} class files, {defs} class definitions, {total} literal-verb sites, {len(other)} non-literal")
    if want:
        for v in want:
            for s in verbs.get(v, []):
                print(f"{v}: {s['cls']}.{s['meth']} | {s['recv']} | {s['args'][-1][:200]}")
        return
    for v, ss in sorted(verbs.items(), key=lambda kv: (-len(kv[1]), kv[0])):
        recv = ", ".join(f"{k} {n}" for k, n in Counter(x["recv"] for x in ss).most_common(3))
        cls = ", ".join(f"{k.split('.')[-1]} {n}" for k, n in Counter(x["cls"] for x in ss).most_common(3))
        print(f"{len(ss):4d} {v:18s} recv: {recv}; classes: {cls}")


if __name__ == "__main__":
    main(sys.argv[1], sys.argv[2:])
