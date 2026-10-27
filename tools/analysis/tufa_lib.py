"""Minimal TUFA (.class) reader for static corpus analysis (data only, never executes anything).
Layout per docs/formats/tufa.md and docs/research/script-vm-opcodes.md."""
import os
import struct

NOOPERAND = {0x04, 0x05, 0x0C, 0x10, 0x18, 0x20, 0x21, 0x22, 0x23, 0x24, 0x28, 0x29, 0x2A, 0x2B, 0x2C, 0x2D}
NONE = 0xFFFFFFFF


class Cls:
    __slots__ = ("file", "cons", "bodies", "fields", "methods", "name", "super", "ifaces")

    def s(self, i):
        e = self.cons[i]
        return e[1] if e[0] == "s" else None

    def cname(self, i):  # class entry -> name
        e = self.cons[i]
        return self.s(e[1]) if e[0] == 4 else None

    def ref(self, i):  # member ref -> (class, name, desc)
        e = self.cons[i]
        c = self.cname(e[1])
        nt = self.cons[e[2]]
        return c, self.s(nt[1]), self.s(nt[2])


def _u32(b, p):
    return struct.unpack_from("<I", b, p)[0]


def parse_file(path):
    data = open(path, "rb").read()
    pos, out = 0, []
    while pos < len(data):
        if data[pos:pos + 4] != b"TUFA":
            raise ValueError(f"{path}: bad magic at {pos}")
        pos += 12
        c = Cls()
        c.file = path
        c.cons, c.bodies, c.fields, c.methods, c.ifaces = [], [], [], [], []
        c.name = c.super = None
        while pos < len(data) and data[pos:pos + 4] != b"TUFA":
            tag = data[pos:pos + 4].decode("latin1")
            size = _u32(data, pos + 4)
            b = data[pos + 8:pos + 8 + size]
            pos += 8 + size
            if tag == "CONS":
                n, p = _u32(b, 0), 4
                for _ in range(n):
                    t = _u32(b, p)
                    p += 4
                    if t == 0:
                        ln = _u32(b, p)
                        c.cons.append(("s", b[p + 4:p + 4 + ln].decode("latin1")))
                        p += 5 + ln
                    elif t in (3, 5, 7):
                        c.cons.append((t, _u32(b, p), _u32(b, p + 4)))
                        p += 8
                    elif t == 4:
                        c.cons.append((4, _u32(b, p)))
                        p += 4
                    else:
                        raise ValueError(f"cons type {t}")
            elif tag in ("FILD", "MTHD"):
                w = 4 if tag == "FILD" else 5
                p, rows = 0, []
                for static in (True, False):
                    n = _u32(b, p)
                    p += 4
                    for _ in range(n):
                        rows.append((static,) + struct.unpack_from("<" + "I" * w, b, p))
                        p += 4 * w
                (c.fields if tag == "FILD" else c.methods).extend(rows)
            elif tag == "CLSS":
                this, sup, ni = _u32(b, 8), _u32(b, 12), _u32(b, 16)
                c.name = c.cname(this)
                c.super = c.cname(sup) if sup != NONE and sup < len(c.cons) else None
                c.ifaces = [c.cname(_u32(b, 20 + 4 * i)) for i in range(ni)]
            elif tag == "TREE":
                n, p = _u32(b, 0), 4
                for _ in range(n):
                    cnt = _u32(b, p)
                    p += 4
                    ins = []
                    for _ in range(cnt):
                        op = b[p]
                        line = struct.unpack_from("<H", b, p + 1)[0]
                        p += 3
                        arg = None
                        if op not in NOOPERAND:
                            arg = struct.unpack_from("<i", b, p)[0]
                            p += 4
                        ins.append((op, line, arg))
                    c.bodies.append(ins)
        out.append(c)
    return out


def load_corpus(root):
    """All .class files under root (skips hidden dirs). Returns list of Cls."""
    res = []
    for d, dirs, fs in os.walk(root):
        dirs[:] = sorted(x for x in dirs if not x.startswith("."))
        for f in sorted(fs):
            if f.lower().endswith(".class"):
                res.extend(parse_file(os.path.join(d, f)))
    return res
