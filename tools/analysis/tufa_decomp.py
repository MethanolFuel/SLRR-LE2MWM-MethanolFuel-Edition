#!/usr/bin/env python3
"""TUFA (.class) -> pseudo-Java decompiler, built on the opcode semantics in
docs/research/script-vm-opcodes.md. Static, read-only; never executes anything.

It is a heuristic stack-machine reconstruction meant for READING scripts and for call-site statistics. Control
flow is shown as `if !(c) goto Ln;` / `goto Ln;` / `Ln:` rather than structured blocks, and unknown pops print `<?>`.

Usage (python3 -I):
  tufa_decomp.py show FILE.class            pseudo-Java for every class/method/initializer in the file
  tufa_decomp.py calls ROOT OUT.json        every call site in the tree: {cls, method, line, kind, target, name, desc, args}
"""
import json
import os
import struct
import sys

HAS_OPERAND = {0x01, 0x02, 0x03, 0x06, 0x07, 0x08, 0x09, 0x0A, 0x0B, 0x0D, 0x0E, 0x11, 0x12, 0x14, 0x15, 0x16, 0x17,
               0x19, 0x1A, 0x1B, 0x1C, 0x25, 0x26, 0x27}
BIN = {0x01: "||", 0x02: "&&", 0x03: "|", 0x04: "^", 0x05: "&", 0x06: "!=", 0x07: "==", 0x08: ">=", 0x09: "<=",
       0x0A: ">", 0x0B: "<", 0x0D: ">>", 0x0E: "<<", 0x0F: "-", 0x10: "+", 0x11: "%", 0x12: "/", 0x13: "*"}
UN = {0x14: "~", 0x15: "!", 0x16: "-"}
INCDEC = {0x17: "{}++", 0x18: "{}--", 0x19: "++{}", 0x1A: "--{}"}
ASSIGN = {0x23: "=", 0x24: "*=", 0x25: "/=", 0x26: "%=", 0x27: "+=", 0x28: "-=", 0x2C: "&=", 0x2D: "^=", 0x2E: "|="}


def parse_file(path):
    data = open(path, "rb").read()
    pos = 0
    out = []
    while pos < len(data):
        assert data[pos:pos + 4] == b"TUFA"
        pos += 12
        ch = {}
        while pos < len(data) and data[pos:pos + 4] != b"TUFA":
            tag = data[pos:pos + 4].decode("latin1")
            size = struct.unpack_from("<I", data, pos + 4)[0]
            ch[tag] = data[pos + 8:pos + 8 + size]
            pos += 8 + size
        out.append(build(ch))
    return out


def build(ch):
    body = ch["CONS"]
    n = struct.unpack_from("<I", body, 0)[0]
    p = 4
    cons = []
    for _ in range(n):
        t = struct.unpack_from("<I", body, p)[0]
        p += 4
        if t == 0:
            ln = struct.unpack_from("<I", body, p)[0]
            cons.append(("s", body[p + 4:p + 4 + ln].decode("latin1")))
            p += 5 + ln
        elif t in (3, 5, 7):
            a, b = struct.unpack_from("<II", body, p)
            cons.append((t, a, b))
            p += 8
        else:
            cons.append((4, struct.unpack_from("<I", body, p)[0]))
            p += 4
    cl = {"cons": cons}
    c = cons
    cl_ = ch["CLSS"]
    _, _, this, sup, nif = struct.unpack_from("<IIIII", cl_, 0)
    cl["name"] = c[c[this][1]][1]
    cl["super"] = c[c[sup][1]][1] if sup != 0xFFFFFFFF else None
    cl["ifaces"] = [c[c[struct.unpack_from("<I", cl_, 20 + 4 * k)[0]][1]][1] for k in range(nif)]
    for tag, key, rowfmt in (("FILD", "fields", "<IIII"), ("MTHD", "methods", "<IIIII")):
        rows = []
        if tag in ch:
            b = ch[tag]
            q = 0
            for static in (True, False):
                cnt = struct.unpack_from("<I", b, q)[0]
                q += 4
                for _ in range(cnt):
                    v = struct.unpack_from(rowfmt, b, q)
                    q += struct.calcsize(rowfmt)
                    r = dict(static=static, flags=v[0], name=c[v[1]][1], desc=c[v[2]][1])
                    if tag == "FILD":
                        r["init"] = None if v[3] == 0xFFFFFFFF else v[3]
                    else:
                        r["body"], r["slots"] = v[3], v[4]
                    rows.append(r)
        cl[key] = rows
    bodies = []
    tb = ch["TREE"]
    n = struct.unpack_from("<I", tb, 0)[0]
    p = 4
    for _ in range(n):
        cnt = struct.unpack_from("<I", tb, p)[0]
        p += 4
        ins = []
        for _ in range(cnt):
            op = tb[p]
            line = struct.unpack_from("<H", tb, p + 1)[0]
            p += 3
            arg = None
            if op in HAS_OPERAND:
                arg = struct.unpack_from("<i", tb, p)[0]
                p += 4
            ins.append((op, line, arg))
        bodies.append(ins)
    cl["bodies"] = bodies
    return cl


def count_params(desc):
    """number of parameters in a dotted JVM-style descriptor such as (IF[Ljava.lang.String;)V"""
    n = 0
    i = desc.index("(") + 1
    while i < len(desc) and desc[i] != ")":
        while desc[i] == "[":
            i += 1
        if desc[i] == "L":
            i = desc.index(";", i)
        i += 1
        n += 1
    return n


def short(n):
    return n.rsplit(".", 1)[-1]


class Dec:
    def __init__(self, cl):
        self.cl = cl
        self.c = cl["cons"]
        self.calls = []

    def s(self, i):
        e = self.c[i]
        return e[1] if e[0] == "s" else "?"

    def cname(self, i):
        return self.s(self.c[i][1])

    def ref(self, i):
        e = self.c[i]
        nt = self.c[e[2]]
        return self.cname(e[1]), self.s(nt[1]), self.s(nt[2])

    def lit(self, op, arg):
        if op == 0x09:
            return json.dumps(self.s(arg), ensure_ascii=False)
        if op == 0x0A:
            f = struct.unpack("<f", struct.pack("<i", arg))[0]
            return repr(round(f, 6)) + "f"
        if op in (0x0B, 0x0D):
            return str(arg)
        if op == 0x0C:
            return "null"
        if op == 0x0E:
            e = self.c[arg]
            return f"{self.s(e[1])}:0x{e[2]:X}r"
        return "<lit?>"

    def run(self, body, mname, desc=None):
        st = []
        out = []  # (line, text)
        locs = {}
        labels = {}
        skip = set()
        n = len(body)
        # pre-scan for expression-internal jumps (after SKIPIF) so they are not treated as control flow
        for i, (op, _, a) in enumerate(body):
            if op in (0x04, 0x05) and i + 1 < n and body[i + 1][0] == 0x14:
                skip.add(i + 1)
        for i, (op, _, a) in enumerate(body):
            if op in (0x14, 0x15, 0x16, 0x17) and i not in skip:
                labels[i + a] = True
        pop = lambda: st.pop() if st else "<?>"

        nparams = count_params(desc) if desc else None
        is_static = mname.startswith("static ")

        def lname(k):
            if k in locs:
                return locs[k]
            if not is_static and k == 0:
                return "this"
            if nparams is not None:
                # arguments are numbered from the LAST parameter (measured: Message(type,cmd) stores type=slot2, cmd=slot1)
                pi = nparams - k if not is_static else nparams - 1 - k
                if 0 <= pi < nparams:
                    return f"p{pi}"
            return f"a{k}"

        def popargs():
            m = pop()
            k = m[1] if isinstance(m, tuple) else 0
            args = [pop() for _ in range(k)]
            args.reverse()
            return args

        def path(i, k):
            """components i..i+k-1 -> (receiver text, last instr, next index)"""
            parts = []
            last = None
            j = i
            for _ in range(k):
                if j >= n:
                    break
                op, _, a = body[j]
                if op == 0x19:
                    parts.append(("class", short(self.cname(a))))
                elif op == 0x01:
                    parts.append(("local", lname(a)))
                elif op == 0x1B:
                    parts.append(("field", self.ref(a)[1]))
                elif op == 0x1C:
                    parts.append(("field", self.s(a)))
                elif op in (0x1A, 0x12):
                    last = body[j]
                    j += 1
                    break
                else:
                    parts.append(("?", f"<op{op:02x}>"))
                j += 1
            return parts, last, j

        i = 0
        lastline = 0
        while i < n:
            op, line, a = body[i]
            lastline = line
            if i in labels:
                out.append((line, f"L{i}:"))
            if op == 0x01:
                st.append(lname(a))
            elif op == 0x02:
                j = i + 1
                nm = f"v{a}"
                if j < n and body[j][0] == 0x2E:
                    nm = self.s(body[j][2]) if False else f"v{a}"
                    j += 1
                typ = "?"
                if j < n and body[j][0] == 0x06:
                    typ = self.s(body[j][2]).replace("Ljava.lang.", "").replace(";", "")
                    j += 1
                locs[a] = nm
                if j < n and body[j][0] == 0x2C:
                    st.append(("decl", nm, typ))
                    j += 1
                else:
                    out.append((line, f"{typ} {nm};"))
                i = j
                continue
            elif op in (0x04, 0x05):
                pass
            elif op in (0x07, 0x08):
                disc = op == 0x08
                code = a
                if code == 0x1C:
                    if i + 1 < n:
                        if not disc:
                            st.append(self.lit(body[i + 1][0], body[i + 1][2]))
                        i += 1
                elif code == 0x1B:
                    if disc:
                        v = pop()
                        if not isinstance(v, tuple) and v != "<void>":
                            out.append((line, f"{v};"))
                elif code == 0x22:
                    pass
                elif code == 0x1E:
                    t = self.s(body[i + 1][2]) if i + 1 < n and body[i + 1][0] == 0x06 else "?"
                    st.append(("new", t.strip("L;").replace("java.lang.", "")))
                    i += 1
                elif code == 0x1F:
                    t = self.s(body[i + 1][2]) if i + 1 < n and body[i + 1][0] == 0x06 else "?"
                    d = pop()
                    st.append("new " + t.lstrip("[").strip("L;") + f"[{d}]")
                    i += 1
                elif code == 0x20:
                    t = self.s(body[i + 1][2]) if i + 1 < n and body[i + 1][0] == 0x06 else "?"
                    v = pop()
                    st.append(f"({v} instanceof {short(t.strip('L;'))})")
                    i += 1
                elif code == 0x21:
                    t = short(self.cname(body[i + 1][2])) if i + 1 < n and body[i + 1][0] == 0x19 else "?"
                    v = pop()
                    st.append(f"(({t}){v})")
                    i += 1
                elif code in ASSIGN:
                    var = pop()
                    val = pop()
                    if isinstance(var, tuple):
                        var = var[1]
                    txt = f"{var} {ASSIGN[code]} {val}"
                    if disc:
                        out.append((line, txt + ";"))
                    else:
                        st.append(f"({txt})")
                elif code in INCDEC:
                    v = pop()
                    txt = INCDEC[code].replace("{}", str(v))
                    if disc:
                        out.append((line, txt + ";"))
                    else:
                        st.append(txt)
                elif code in UN:
                    v = pop()
                    st.append(f"{UN[code]}{v}")
                    if disc:
                        st.pop()
                elif code == 0x00:
                    c3 = pop()
                    b2 = pop()
                    a1 = pop()
                    st.append(f"({a1} ? {b2} : {c3})")
                elif code in BIN:
                    r = pop()
                    l = pop()
                    st.append(f"({l} {BIN[code]} {r})")
                    if disc:
                        st.pop()
                else:
                    st.append(f"<opr{code:02x}>")
            elif op in (0x09, 0x0A, 0x0B, 0x0C, 0x0D, 0x0E):
                st.append(self.lit(op, a))
            elif op == 0x0F or op == 0x13:
                pass
            elif op == 0x10:
                v = pop()
                out.append((line, f"/*init*/ {v};"))
            elif op == 0x11:
                parts, last, j = path(i + 1, a)
                if last is None:
                    txt = ".".join(p[1] for p in parts)
                    st.append(txt)
                    i = j
                    continue
                # a path that ends in a method belongs to a preceding CALL; handled in CALL
                st.append(("path", parts, last))
                i = j
                continue
            elif op == 0x14:
                if i not in skip:
                    out.append((line, f"goto L{i + a};"))
            elif op in (0x15, 0x16):
                v = pop()
                nm = "if !" if op == 0x15 else "if "
                out.append((line, f"{nm}({v}) goto L{i + a};"))
            elif op == 0x17:
                cv = pop()
                sw = st[-1] if st else "<?>"
                out.append((line, f"case {cv} (switch {sw}) -> L{i + a};"))
            elif op == 0x18:
                pop()
            elif op == 0x1B:
                r = self.ref(a)
                st.append(r[1] if r[0] == self.cl["name"] else f"{short(r[0])}.{r[1]}")
            elif op == 0x1C:
                o = pop()
                st.append(f"{o}.{self.s(a)}")
            elif op == 0x20:
                arr = pop()
                idx = pop()
                st.append(f"{arr}[{idx}]")
            elif op == 0x21:
                args = popargs()
                obj = pop()
                t = obj[1] if isinstance(obj, tuple) else str(obj)
                st.append(f"new {t}({', '.join(map(str, args))})")
                self.calls.append(dict(kind="new", target=t, name="<init>", desc="", argc=len(args),
                                       args=[str(x) for x in args], line=line, method=mname))
            elif op in (0x22, 0x23):
                args = popargs()
                kw = "this" if op == 0x22 else "super"
                out.append((line, f"{kw}({', '.join(map(str, args))});"))
                self.calls.append(dict(kind=kw + "init", target=self.cl["super"] if op == 0x23 else self.cl["name"],
                                       name="<init>", desc="", argc=len(args), args=[str(x) for x in args],
                                       line=line, method=mname))
            elif op == 0x24:
                args = popargs()
                j = i + 1
                parts = []
                last = None
                if j < n and body[j][0] == 0x11:
                    parts, last, j = path(j + 1, body[j][2])
                elif j < n and body[j][0] in (0x1A, 0x12):
                    last = body[j]
                    j += 1
                recv = ".".join(p[1] for p in parts)
                if last is None:
                    st.append(f"<call?>({', '.join(map(str, args))})")
                else:
                    lop, _, la = last
                    if lop == 0x1A:
                        tc, nm, desc = self.ref(la)
                        kind = "typed"
                    else:
                        tc, nm, desc, kind = "", self.s(la), "", "byname"
                    txt = (recv + "." if recv else "") + nm + "(" + ", ".join(map(str, args)) + ")"
                    st.append(txt)
                    self.calls.append(dict(kind=kind, target=tc, name=nm, desc=desc, argc=len(args), recv=recv,
                                           args=[str(x) for x in args], line=line, method=mname))
                i = j
                continue
            elif op == 0x25:
                obj = pop()
                args = popargs() if st else []
                # CALLBYNAME: [a1..an n obj]; obj was popped first, so re-fetch args
                nm = self.s(a)
                st.append(f"{obj}.{nm}({', '.join(map(str, args))})")
                self.calls.append(dict(kind="byname", target="", name=nm, desc="", argc=len(args), recv=str(obj),
                                       args=[str(x) for x in args], line=line, method=mname))
            elif op == 0x26:
                args = popargs()
                nm = self.s(a)
                st.append(f"super.{nm}({', '.join(map(str, args))})")
                self.calls.append(dict(kind="super", target=self.cl["super"], name=nm, desc="", argc=len(args),
                                       args=[str(x) for x in args], line=line, method=mname))
            elif op == 0x27:
                st.append(("argc", a))
            elif op == 0x28:
                pass
            elif op == 0x29:
                st.append("<void>")
            elif op == 0x2A:
                out.append((line, "return;"))
            elif op == 0x2B:
                v = pop()
                out.append((line, "return;" if v == "<void>" else f"return {v};"))
            elif op == 0x2D:
                val = pop()
                var = pop()
                if isinstance(var, tuple) and var[0] == "decl":
                    out.append((line, f"{var[2]} {var[1]} = {val};"))
                else:
                    out.append((line, f"{var} = {val};"))
            i += 1
        return out


def show(cl, w):
    d = Dec(cl)
    w(f"// class {cl['name']} extends {cl['super']} implements {cl['ifaces']}")
    for f in cl["fields"]:
        val = ""
        if f["init"] is not None and f["init"] < len(cl["bodies"]):
            r = [t for _, t in d.run(cl["bodies"][f["init"]], "init")]
            val = " = " + " ".join(x.replace("/*init*/ ", "").rstrip(";") for x in r)
        w(f"//   {'static ' if f['static'] else ''}field {f['desc']} {f['name']}{val} flags=0x{f['flags']:x}")
    used = set()
    for m in cl["methods"]:
        nat = " native" if m["flags"] & 0x40 else ""
        st = "static " if m["static"] else ""
        w(f"\n{st}{m['name']}{m['desc']} flags=0x{m['flags']:x}{nat} body={m['body']} slots={m['slots']} {{")
        if not (m["flags"] & 0x40) and m["body"] < len(cl["bodies"]):
            used.add(m["body"])
            for line, t in d.run(cl["bodies"][m["body"]], st + m["name"], m["desc"]):
                w(f"  L{line}: {t}")
        w("}")
    for f in cl["fields"]:
        if f["init"] is not None and f["init"] < len(cl["bodies"]):
            used.add(f["init"])
            w(f"\n// initializer of {f['name']}")
            for line, t in d.run(cl["bodies"][f["init"]], "init"):
                w(f"  L{line}: {t}")


def main():
    cmd = sys.argv[1]
    if cmd == "show":
        for cl in parse_file(sys.argv[2]):
            show(cl, print)
    elif cmd == "calls":
        res = []
        for dp, dn, fn in os.walk(sys.argv[2]):
            dn[:] = [x for x in dn if not x.startswith(".")]
            for f in fn:
                if f.lower().endswith(".class"):
                    for cl in parse_file(os.path.join(dp, f)):
                        d = Dec(cl)
                        for m in cl["methods"]:
                            if not (m["flags"] & 0x40) and m["body"] < len(cl["bodies"]):
                                d.run(cl["bodies"][m["body"]], ("static " if m["static"] else "") + m["name"], m["desc"])
                        for fl in cl["fields"]:
                            if fl["init"] is not None and fl["init"] < len(cl["bodies"]):
                                d.run(cl["bodies"][fl["init"]], "init:" + fl["name"])
                        for c in d.calls:
                            c["cls"] = cl["name"]
                            res.append(c)
        json.dump(res, open(sys.argv[3], "w"))
        print(len(res), "call sites")


if __name__ == "__main__":
    main()
