#!/usr/bin/env python3
"""Command-string vocabulary: (a) verbs the shipped scripts send with GameRef.command()/queueEvent(.., EVENT_COMMAND, s)
(from the call-site extraction of tufa_decomp.py), (b) sscanf formats of the same verbs in StreetLegal_Redline.exe
(strings '%*s %d ...' immediately preceded by the verb word, restricted to the command-parser string area
0x60d848..0x610fe0 and 0x6124b4..0x612c20; the cfg keyword parser strings further up are excluded).

Usage: python3 -I command_vocab.py ROOT                (read-only; prints a markdown table)
"""
import os
import re
import sys
from collections import Counter, defaultdict

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
sys.dont_write_bytecode = True


def script_verbs(calls):
    verbs = defaultdict(lambda: dict(n=0, recv=Counter(), ex=None))
    for c in calls:
        if c["name"] not in ("command", "queueEvent"):
            continue
        a = c["args"][-1] if c["args"] else ""
        if c["name"] == "queueEvent" and "EVENT_COMMAND" not in " ".join(c["args"]):
            continue
        m = re.search(r'"([A-Za-z_]+)[ "]', a) or re.search(r'"([A-Za-z_]+)"', a)
        if m:
            v = verbs[m.group(1)]
            v["n"] += 1
            v["recv"][(c.get("recv") or "?").split(".")[-1]] += 1
            if v["ex"] is None:
                v["ex"] = a[:110]
    return verbs


def exe_formats(root):
    d = open(os.path.join(root, "StreetLegal_Redline.exe"), "rb").read()
    items = [(m.start(), m.group().decode()) for m in re.finditer(rb"[\x20-\x7e]{2,}", d[0x200000:0x230000])]
    out = defaultdict(list)
    for i, (o, s) in enumerate(items):
        va = 0x600000 + o
        if s.startswith("%*s") and i and (0x60d848 <= va <= 0x610fe0 or 0x6124b4 <= va <= 0x612c20):
            prev = items[i - 1][1]
            if re.fullmatch(r"[A-Za-z_]+", prev):
                out[prev].append((hex(va), s))
    return out


def table(calls, root):
    verbs = script_verbs(calls)
    fmts = exe_formats(root)
    rows = ["| Verb | Script sends | Typical receivers | exe sscanf format (VA) | Example script argument |",
            "|---|---|---|---|---|"]
    for v in sorted(set(verbs) | set(fmts), key=lambda x: x.lower()):
        s = verbs.get(v)
        fm = "; ".join(f"`{f}` ({a})" for a, f in fmts.get(v, [])[:2])
        recv = ", ".join(k for k, _ in s["recv"].most_common(3)) if s else ""
        ex = ("`" + s["ex"].replace("|", "\\|") + "`") if s and s["ex"] else ""
        rows.append(f"| {v} | {s['n'] if s else 0} | {recv} | {fm.replace('|', chr(92) + '|')} | {ex} |")
    return "\n".join(rows) + "\n"


def main(root):
    import tufa_decomp as td
    calls = []
    for dp, dn, fn in os.walk(root):
        dn[:] = [x for x in dn if not x.startswith(".")]
        for f in fn:
            if f.lower().endswith(".class"):
                for cl in td.parse_file(os.path.join(dp, f)):
                    d = td.Dec(cl)
                    for m in cl["methods"]:
                        if not (m["flags"] & 0x40) and m["body"] < len(cl["bodies"]):
                            d.run(cl["bodies"][m["body"]], ("static " if m["static"] else "") + m["name"], m["desc"])
                    calls.extend(d.calls)
    print(table(calls, root))


if __name__ == "__main__":
    main(sys.argv[1])
