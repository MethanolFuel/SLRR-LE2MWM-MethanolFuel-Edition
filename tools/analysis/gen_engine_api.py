#!/usr/bin/env python3
"""Generate docs/engine-api/<package>.md from (a) the shipped .class corpus, (b) docs/research/natives-*.tsv,
(c) static string/xref facts from StreetLegal_Redline.exe, (d) hand-written text in tools/analysis/api_text/.

Template syntax inside api_text/<page>.md (everything else is copied verbatim):
  {{CLASS java.lang.Thread}}        generated class section (constants, fields, natives table, script members)
  {{CLASSLIST java.render.osd.}}    one-line table of script-only classes whose name starts with the prefix
  {{RAWEDIT}}                       table of the RawEdit address constants parsed from RawEdit.java
  {{STATS}}                         corpus numbers used in the README
  {{COMMANDS}}                      command-string verb table (scripts vs exe sscanf formats)
  {{PACKAGES java.game.parts.}}     sub-package class counts

Per-native / per-class notes come from api_text/notes.tsv (class, method|*, confidence H/M/L, text).
Confidence: H = visible in shipped script code, M = inferred from argument patterns/names, L = name only.

Usage: python3 -I gen_engine_api.py REPO_ROOT          (static, read-only on the repo except docs/engine-api/*)
"""
import csv
import os
import re
import struct
import sys
from collections import Counter, defaultdict

sys.dont_write_bytecode = True
HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import tufa_decomp as td  # noqa: E402
import command_vocab  # noqa: E402


def load_corpus(root):
    classes = {}
    files = {}
    for dp, dn, fn in os.walk(root):
        dn[:] = sorted(x for x in dn if not x.startswith("."))
        for f in sorted(fn):
            if f.lower().endswith(".class"):
                p = os.path.join(dp, f)
                for cl in td.parse_file(p):
                    cl["file"] = os.path.relpath(p, root).replace(os.sep, "/")
                    classes.setdefault(cl["name"], []).append(cl)
    return classes


def collect_calls(classes):
    calls = []
    for name, defs in classes.items():
        for cl in defs:
            d = td.Dec(cl)
            for m in cl["methods"]:
                if not (m["flags"] & 0x40) and m["body"] < len(cl["bodies"]):
                    d.run(cl["bodies"][m["body"]], ("static " if m["static"] else "") + m["name"], m["desc"])
            for fl in cl["fields"]:
                if fl["init"] is not None and fl["init"] < len(cl["bodies"]):
                    d.run(cl["bodies"][fl["init"]], "init:" + fl["name"], None)
            for c in d.calls:
                c["cls"] = cl["name"]
                calls.append(c)
    return calls


def readable(desc):
    """(Ljava.util.resource.GameRef;I)V -> ('GameRef, int', 'void')"""
    def one(s, i):
        arr = 0
        while s[i] == "[":
            arr += 1
            i += 1
        c = s[i]
        if c == "L":
            j = s.index(";", i)
            t = s[i + 1:j].rsplit(".", 1)[-1]
            i = j + 1
        else:
            t = {"I": "int", "F": "float", "V": "void", "Z": "boolean"}.get(c, c)
            i += 1
        return t + "[]" * arr, i
    i = desc.index("(") + 1
    ps = []
    while desc[i] != ")":
        t, i = one(desc, i)
        ps.append(t)
    rest = desc[i + 1:]
    ret = one(rest, 0)[0] if rest else ""
    return ", ".join(ps), ret


def build_usage(classes, calls):
    supers = {n: defs[0]["super"] for n, defs in classes.items()}
    declared = defaultdict(set)
    for n, defs in classes.items():
        for cl in defs:
            for m in cl["methods"]:
                declared[n].add((m["name"], m["desc"]))

    def resolve(cls, name, desc):
        k = 0
        while cls and k < 60:
            if (name, desc) in declared.get(cls, ()):
                return cls
            cls = supers.get(cls)
            k += 1
        return None

    typed = defaultdict(lambda: dict(n=0, callers=Counter(), args=defaultdict(Counter), ex=[]))
    byname = defaultdict(lambda: dict(n=0, callers=set(), ex=[]))
    for c in calls:
        if c["kind"] == "typed":
            dc = resolve(c["target"], c["name"], c["desc"]) or c["target"]
            u = typed[(dc, c["name"], c["desc"])]
            u["n"] += 1
            u["callers"][c["cls"]] += 1
            for i, a in enumerate(c["args"]):
                u["args"][i][a] += 1
            if len(u["ex"]) < 3 and all(e[0] != c["cls"] for e in u["ex"]):
                u["ex"].append((c["cls"], c.get("recv", ""), c["args"]))
        elif c["kind"] in ("byname", "super"):
            b = byname[c["name"]]
            b["n"] += 1
            b["callers"].add(c["cls"])
            if len(b["ex"]) < 3:
                b["ex"].append((c["cls"], c.get("recv", ""), c["args"]))
    return typed, byname


def exe_field_xrefs(root, names):
    """names -> {name: [code VAs]} for NUL-delimited exact strings in the exe that are pushed as 32-bit immediates"""
    sys.path.insert(0, os.path.join(root, "docs", "research", "scripts"))
    from pe_xref import Image  # noqa: E402
    img = Image(os.path.join(root, "StreetLegal_Redline.exe"))
    mem = bytes(img.mem)
    wanted = set(names)
    strs = {}
    for m in re.finditer(rb"(?<=\x00)[A-Za-z_][A-Za-z0-9_]{2,}(?=\x00)", mem):
        s = m.group().decode()
        if s in wanted:
            strs.setdefault(s, []).append(img.base + m.start())
    va2name = {va: n for n, vs in strs.items() for va in vs}
    lo, hi = img.text_lo - img.base, img.text_hi - img.base
    refs = defaultdict(list)
    for sh in range(4):
        start = lo + sh
        end = start + ((hi - start) // 4) * 4
        view = memoryview(mem[start:end]).cast("I")
        for i, v in enumerate(view):
            if v in va2name:
                refs[va2name[v]].append(img.base + start + 4 * i)
    return {n: sorted(set(v)) for n, v in refs.items()}


def cell(s):
    return str(s).replace("|", "\\|").replace("\n", " ")


class Gen:
    def __init__(self, root):
        self.root = root
        self.classes = load_corpus(root)
        self.calls = collect_calls(self.classes)
        self.typed, self.byname = build_usage(self.classes, self.calls)
        self.reg = {}
        self.gi = set()
        base = os.path.join(root, "docs", "research")
        for r in csv.DictReader(open(os.path.join(base, "natives-redline.tsv")), delimiter="\t"):
            self.reg[(r["class"], r["method"], r["descriptor"])] = r["function"]
        self.gi_rows = {}
        for r in csv.DictReader(open(os.path.join(base, "natives-slrr_gi-extra.tsv")), delimiter="\t"):
            self.gi.add((r["class"], r["method"], r["descriptor"]))
            self.gi_rows[(r["class"], r["method"], r["descriptor"])] = r["function"]
        self.notes = defaultdict(list)
        self.cnotes = defaultdict(list)
        tdir = os.path.join(HERE, "api_text")
        for nf in sorted(x for x in os.listdir(tdir) if x.startswith("notes") and x.endswith(".tsv")):
            with open(os.path.join(tdir, nf), encoding="utf-8") as f:
                for r in csv.DictReader(f, delimiter="\t", quoting=csv.QUOTE_NONE):
                    if r["method"] == "*":
                        self.cnotes[r["class"]].append((r["conf"], r["note"]))
                    else:
                        self.notes[(r["class"], r["method"])].append((r["conf"], r["note"]))
        allf = {f["name"] for defs in self.classes.values() for cl in defs for f in cl["fields"]}
        self.xrefs = exe_field_xrefs(root, allf)
        self.used_notes = set()
        self.legend_shown = False

    # ---------- class section ----------
    def native_rows(self, cname):
        rows = []
        defs = self.classes.get(cname, [])
        seen = set()
        for cl in defs:
            for m in cl["methods"]:
                key = (m["name"], m["desc"])
                if key in seen:
                    continue
                seen.add(key)
                reg = (cname, m["name"], m["desc"]) in self.reg
                gi = (cname, m["name"], m["desc"]) in self.gi
                if m["flags"] & 0x40 or reg or gi:
                    rows.append(dict(name=m["name"], desc=m["desc"], decl="native" if m["flags"] & 0x40 else "script",
                                     static=m["static"], flags=m["flags"], reg=reg, gi=gi))
        # registered natives with no declaration in the shipped class
        for (c, n, d), fn in self.reg.items():
            if c == cname and (n, d) not in seen:
                rows.append(dict(name=n, desc=d, decl="absent", static=False, flags=0, reg=True, gi=False))
        for (c, n, d) in self.gi:
            if c == cname and (n, d) not in seen and not any(r["name"] == n and r["desc"] == d for r in rows):
                rows.append(dict(name=n, desc=d, decl="absent", static=False, flags=0, reg=False, gi=True))
        return rows

    def note_text(self, cname, mname):
        out = []
        for key in ((cname, mname),):
            for conf, t in self.notes.get(key, []):
                out.append(f"({conf}) {t}")
                self.used_notes.add(key)
        return " ".join(out)

    def sig(self, row, cname):
        ps, ret = readable(row["desc"])
        if row["name"] == "<init>":
            return f"{cname.rsplit('.', 1)[-1]}({ps})"
        return f"{'static ' if row['static'] else ''}{ret} {row['name']}({ps})"

    def class_section(self, cname, level=3):
        defs = self.classes.get(cname)
        h = "#" * level
        if not defs:
            return f"{h} {cname}\n\n(not present in the shipped .class corpus; engine-only name)\n"
        cl = defs[0]
        out = [f"{h} {cname}", ""]
        imp = f" implements {', '.join(cl['ifaces'])}" if cl["ifaces"] else ""
        files = sorted({d["file"] for d in defs})
        out.append(f"`extends {cl['super']}`{imp}. Compiled in `{files[0]}`"
                   + (f" (+{len(files) - 1} more definitions)" if len(files) > 1 else "") + ".")
        out.append("")
        for conf, t in self.cnotes.get(cname, []):
            out.append(f"({conf}) {t}")
            out.append("")
            self.used_notes.add((cname, "*"))
        # constants
        consts = []
        for f in cl["fields"]:
            if f["static"] and f["init"] is not None and f["init"] < len(cl["bodies"]):
                d = td.Dec(cl)
                r = [t for _, t in d.run(cl["bodies"][f["init"]], "init", None)]
                val = " ".join(x.replace("/*init*/ ", "").rstrip(";") for x in r)
                consts.append((f["name"], val, f["flags"]))
        if consts:
            txt = ", ".join(f"`{n} = {v}`" for n, v, _ in consts)
            if len(consts) > 24:
                out.append(f"<details><summary>{len(consts)} static fields with initial values</summary>\n\n{txt}\n\n</details>")
            else:
                out.append(f"Static fields: {txt}")
            out.append("")
        inst = [f for f in cl["fields"] if not f["static"]]
        stat_noinit = [f for f in cl["fields"] if f["static"] and f["init"] is None]
        flds = [f"`{readable('()' + f['desc'])[1]} {f['name']}`" for f in inst + stat_noinit]
        if flds:
            txt = ", ".join(flds)
            if len(flds) > 24:
                out.append(f"<details><summary>{len(flds)} instance/uninitialised fields</summary>\n\n{txt}\n\n</details>")
            else:
                out.append(f"Fields: {txt}")
            out.append("")
        eng = [(f["name"], self.xrefs[f["name"]]) for f in cl["fields"] if f["name"] in self.xrefs]
        if eng:
            out.append("Engine-read field candidates (the exact field name is pushed as a string constant by exe code; first code xref in "
                       "`StreetLegal_Redline.exe` after the `@`; short names can be coincidental): "
                       + ", ".join(f"`{n}@{hex(v[0])}`" for n, v in eng))
            out.append("")
        rows = self.native_rows(cname)
        if rows:
            out.append("| Method | Decl / Reg | Typed sites (callers) | Name-only sites | Arguments seen | Semantics and evidence |")
            out.append("|---|---|---|---|---|---|")
            for r in rows:
                u = self.typed.get((cname, r["name"], r["desc"]))
                b = self.byname.get(r["name"])
                regc = ("R" if r["reg"] else "") + ("G" if r["gi"] else "")
                declc = {"native": "native", "script": "script", "absent": "undeclared"}[r["decl"]]
                if not regc:
                    regc = "-"
                args = ""
                if u:
                    parts = []
                    for i, c in sorted(u["args"].items()):
                        top = ", ".join(f"{v} x{n}" if n > 1 else v for v, n in c.most_common(2))
                        parts.append(f"{i}: {top}")
                    args = "; ".join(parts)
                    if len(args) > 150:
                        args = args[:147] + "..."
                    if not args and u["ex"]:
                        args = "(no args)"
                ex = ""
                if u and u["ex"]:
                    c0 = u["ex"][0]
                    ex = f" e.g. `{c0[0].rsplit('.', 1)[-1]}`: `{(c0[1] + '.' if c0[1] else '')}{r['name']}({', '.join(c0[2])})`"
                    if len(ex) > 140:
                        ex = ex[:137] + "...`"
                note = self.note_text(cname, r["name"])
                typed_s = f"{u['n']} ({len(u['callers'])})" if u else "0"
                name_s = f"{b['n']} ({len(b['callers'])})" if b else "0"
                out.append(f"| `{cell(self.sig(r, cname))}` | {declc}/{regc} | {typed_s} | {name_s} | {cell(args)} | {cell(note + ex)} |")
            out.append("")
            if not self.legend_shown:
                self.legend_shown = True
                out.append("Decl/Reg: `native` = flagged 0x40 in the shipped .class, `script` = non-native in the .class but registered natively in an exe, "
                           "`undeclared` = registered in an exe but missing from the shipped .class; `R` = registered in `StreetLegal_Redline.exe`, "
                           "`G` = registered in `Slrr_GI.exe` (patched build), `-` = no registration found. "
                           "Typed sites = call sites that name this method with its descriptor (distinct caller classes in brackets); "
                           "name-only = sites that only name the method, counted over all classes (README conventions).")
            out.append("")
        # other methods
        sm = []
        nat = {(r["name"], r["desc"]) for r in rows}
        for m in cl["methods"]:
            if (m["name"], m["desc"]) in nat:
                continue
            ps, ret = readable(m["desc"])
            sm.append(f"`{m['name']}({ps})`")
        if sm:
            txt = ", ".join(sm)
            if len(sm) > 14:
                out.append(f"<details><summary>{len(sm)} script-implemented members</summary>\n\n{txt}\n\n</details>")
            else:
                out.append(f"Script members: {txt}")
            out.append("")
        return "\n".join(out)

    def classlist(self, prefix):
        rows = []
        for n in sorted(self.classes):
            if n.startswith(prefix) and n[len(prefix):].count(".") == 0:
                cl = self.classes[n][0]
                nn = sum(1 for m in cl["methods"] if m["flags"] & 0x40)
                rows.append(f"| `{n}` | `{cl['super']}` | {len(cl['methods'])} | {len(cl['fields'])} | {nn} | "
                            f"{cell(self.note_text(n, '*') or ' '.join(t for _, t in self.cnotes.get(n, [])))} |")
        return "| Class | Extends | Methods | Fields | Natives | Role |\n|---|---|---|---|---|---|\n" + "\n".join(rows) + "\n"

    def packages(self, prefix):
        agg = defaultdict(list)
        for n in self.classes:
            if n.startswith(prefix):
                rest = n[len(prefix):]
                key = rest.split(".")[0] if "." in rest else "(this package)"
                agg[key].append(n)
        rows = ["| Sub-package | Classes | Examples |", "|---|---|---|"]
        for k in sorted(agg):
            ex = ", ".join(f"`{x.rsplit('.', 1)[-1]}`" for x in sorted(agg[k])[:4])
            rows.append(f"| `{prefix}{k if k != '(this package)' else ''}` | {len(agg[k])} | {ex} |")
        return "\n".join(rows) + "\n"

    def rawedit(self):
        p = os.path.join(self.root, "system", "Scripts", "lang", "src", "RawEdit.java")
        rows = []
        for ln in open(p, encoding="latin1"):
            m = re.search(r"public static (Int|Float)ValueAddress (Address_\w+)\s*=\s*new \w+\((0x[0-9A-Fa-f]+)\)\s*;?\s*(//.*)?", ln)
            if m:
                rows.append((m.group(3).upper().replace("0X", "0x"), "int" if m.group(1) == "Int" else "float",
                             m.group(2)[len("Address_X_"):], (m.group(4) or "").lstrip("/ ").strip()))
        rows.sort()
        out = ["| Address | Type | Name (RawEdit.java) | Author comment |", "|---|---|---|---|"]
        for a, t, n, c in rows:
            out.append(f"| `{a}` | {t} | `{n}` | {cell(c)} |")
        return f"{len(rows)} named engine variables.\n\n" + "\n".join(out) + "\n"

    def stats(self):
        ndef = sum(len(v) for v in self.classes.values())
        nat = sum(1 for defs in self.classes.values() for cl in defs for m in cl["methods"] if m["flags"] & 0x40)
        return (f"- {ndef:,} class definitions ({len(self.classes):,} distinct names) decoded from the shipped `.class` files.\n"
                f"- {len(self.calls):,} call sites extracted by `tufa_decomp.py` (= the ARGC instruction count, 26,314).\n"
                f"- {len(self.reg)} natives registered in `StreetLegal_Redline.exe` ({len(self.gi)} more in `Slrr_GI.exe`), "
                f"{nat} methods flagged native in the shipped classes.\n")

    def render(self, text):
        self.legend_shown = False
        def repl(m):
            kind, arg = m.group(1), (m.group(2) or "").strip()
            if kind == "CLASS":
                return self.class_section(arg)
            if kind == "CLASSLIST":
                return self.classlist(arg)
            if kind == "COMMANDS":
                return command_vocab.table(self.calls, self.root)
            if kind == "PACKAGES":
                return self.packages(arg)
            if kind == "RAWEDIT":
                return self.rawedit()
            if kind == "STATS":
                return self.stats()
            return m.group(0)
        return re.sub(r"\{\{(CLASSLIST|CLASS|PACKAGES|COMMANDS|RAWEDIT|STATS)\b([^}]*)\}\}", repl, text)


def main():
    root = os.path.abspath(sys.argv[1])
    g = Gen(root)
    outdir = os.path.join(root, "docs", "engine-api")
    os.makedirs(outdir, exist_ok=True)
    tdir = os.path.join(HERE, "api_text")
    for f in sorted(os.listdir(tdir)):
        if f.endswith(".md"):
            text = open(os.path.join(tdir, f), encoding="utf-8").read()
            open(os.path.join(outdir, f), "w", encoding="utf-8", newline="\n").write(g.render(text))
            print("wrote", f)
    unused = [k for k in list(g.notes) if k not in g.used_notes]
    if unused:
        print("notes never matched a native row (check names):")
        for k in unused[:80]:
            print("  ", k)


if __name__ == "__main__":
    main()
