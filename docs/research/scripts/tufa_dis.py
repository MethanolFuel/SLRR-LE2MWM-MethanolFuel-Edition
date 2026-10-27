#!/usr/bin/env python3
"""Reference TUFA (.class) disassembler / corpus statistics, using the opcode names derived
from the static analysis of StreetLegal_Redline.exe (see docs/research/script-vm-opcodes.md).

Usage (python3 -I):
  tufa_dis.py dis FILE.class            -> listing
  tufa_dis.py stats DIR                 -> opcode usage, operand ranges, 07/08 operator histogram
  tufa_dis.py grep DIR HEXOP [N]        -> first N contexts (+-4 instr) of an opcode
Only parses data; never executes anything.
"""
import os
import struct
import sys
from collections import Counter, defaultdict

# opcode -> (typeword from the engine table at VA 0x5f0914, mnemonic)
TYPEWORD = [0x0, 0x1001, 0x1002, 0x1003, 0x4, 0x5, 0x1006, 0x1007, 0x1008, 0x4009, 0x200A, 0x100B, 0xC, 0x100D,
            0x100E, 0x100F, 0x10, 0x1011, 0x4012, 0x13, 0x1014, 0x1015, 0x1016, 0x1017, 0x18, 0x1019, 0x101A,
            0x101B, 0x401C, 0x1D, 0x101E, 0x101F, 0x20, 0x21, 0x22, 0x23, 0x24, 0x4025, 0x4026, 0x1027, 0x28,
            0x29, 0x2A, 0x2B, 0x2C, 0x2D, 0x402E]
MNEMONIC = {
    0x01: "LOCAL", 0x02: "DECLLOCAL", 0x03: "DROPLOCAL", 0x04: "SKIPIF_T", 0x05: "SKIPIF_F", 0x06: "TYPE",
    0x07: "OP", 0x08: "OPDISCARD", 0x09: "STR", 0x0A: "FLOAT", 0x0B: "INT", 0x0C: "NULL", 0x0D: "INT2",
    0x0E: "RES", 0x0F: "UNUSED0F", 0x10: "ENDINIT", 0x11: "NAME", 0x12: "MNAME", 0x13: "INVALID13",
    0x14: "JMP", 0x15: "JMPF", 0x16: "JMPT", 0x17: "CASE", 0x18: "POP", 0x19: "CLASS", 0x1A: "METHOD",
    0x1B: "FIELD", 0x1C: "FIELDBYNAME", 0x1D: "SUPERFIELD", 0x1E: "ARRAYINIT", 0x1F: "PUSHZEROS",
    0x20: "INDEX", 0x21: "CALLINIT", 0x22: "THISINIT", 0x23: "SUPERINIT", 0x24: "CALL", 0x25: "CALLBYNAME",
    0x26: "SUPERCALL", 0x27: "ARGC", 0x28: "RVALUE", 0x29: "PUSH0", 0x2A: "RETURN", 0x2B: "RETVAL",
    0x2C: "DECLUSE", 0x2D: "ASSIGNINIT", 0x2E: "LOCALNAME",
}
# operator codes used by OP (07) / OPDISCARD (08)
OPERATOR = {
    0x00: "?:", 0x01: "||", 0x02: "&&", 0x03: "|", 0x04: "^", 0x05: "&", 0x06: "!=", 0x07: "==",
    0x08: ">=", 0x09: "<=", 0x0A: ">", 0x0B: "<", 0x0C: ">>>?", 0x0D: ">>", 0x0E: "<<",
    0x0F: "-", 0x10: "+", 0x11: "%", 0x12: "/", 0x13: "*", 0x14: "~", 0x15: "!", 0x16: "neg",
    0x17: "x++", 0x18: "x--", 0x19: "++x", 0x1A: "--x", 0x1B: "expr", 0x1C: "lit",
    0x1E: "new", 0x1F: "newarray", 0x20: "instanceof", 0x21: "cast", 0x22: "void", 0x23: "=",
    0x24: "*=", 0x25: "/=", 0x26: "%=", 0x27: "+=", 0x28: "-=", 0x29: "<<=(unsup)", 0x2A: ">>=(unsup)",
    0x2B: ">>>=(unsup)", 0x2C: "&=", 0x2D: "^=", 0x2E: "|=",
}


def has_operand(op: int) -> bool:
    return (TYPEWORD[op] & 0xF000) in (0x1000, 0x2000, 0x4000) if op < len(TYPEWORD) else False


def parse(path: str):
    data = open(path, "rb").read()
    pos = 0
    classes = []
    while pos < len(data):
        assert data[pos:pos + 4] == b"TUFA", (path, pos)
        pos += 12
        cls = {"cons": [], "bodies": [], "methods": []}
        while pos < len(data) and data[pos:pos + 4] != b"TUFA":
            tag = data[pos:pos + 4].decode("latin1")
            size = struct.unpack_from("<I", data, pos + 4)[0]
            body = data[pos + 8:pos + 8 + size]
            pos += 8 + size
            if tag == "CONS":
                n = struct.unpack_from("<I", body, 0)[0]
                p = 4
                for _ in range(n):
                    t = struct.unpack_from("<I", body, p)[0]
                    p += 4
                    if t == 0:
                        ln = struct.unpack_from("<I", body, p)[0]
                        cls["cons"].append(("s", body[p + 4:p + 4 + ln].decode("latin1")))
                        p += 4 + ln + 1
                    elif t in (3, 5, 7):
                        a, b = struct.unpack_from("<II", body, p)
                        cls["cons"].append((t, a, b))
                        p += 8
                    elif t == 4:
                        cls["cons"].append((4, struct.unpack_from("<I", body, p)[0]))
                        p += 4
                    else:
                        raise ValueError(f"cons type {t}")
            elif tag == "TREE":
                n = struct.unpack_from("<I", body, 0)[0]
                p = 4
                for _ in range(n):
                    cnt = struct.unpack_from("<I", body, p)[0]
                    p += 4
                    ins = []
                    for _ in range(cnt):
                        op = body[p]
                        line = struct.unpack_from("<H", body, p + 1)[0]
                        p += 3
                        arg = None
                        if has_operand(op):
                            arg = struct.unpack_from("<i", body, p)[0]
                            p += 4
                        ins.append((op, line, arg))
                    cls["bodies"].append(ins)
        classes.append(cls)
    return classes


def cname(cons, i):
    try:
        e = cons[i]
    except (IndexError, TypeError):
        return f"#{i}?"
    if e[0] == "s":
        return repr(e[1][:60])
    if e[0] == 4:
        return cname(cons, e[1])
    if e[0] == 5:
        return f"{cname(cons, e[1])}.{cname(cons, e[2])}"
    if e[0] == 7:
        return f"{cname(cons, e[1])}:{cname(cons, e[2])}"
    if e[0] == 3:
        return f"res {cname(cons, e[1])}:0x{e[2]:x}"
    return str(e)


def fmt(cls, i, ins):
    op, line, arg = ins
    m = MNEMONIC.get(op, f"op{op:02x}")
    s = f"{i:4d} L{line:<5d} {op:02X} {m:11s}"
    if arg is None:
        return s
    if op in (0x07, 0x08):
        return s + f" {arg} ({OPERATOR.get(arg, '?')})"
    if op in (0x09, 0x0E, 0x19, 0x1A, 0x1B, 0x06, 0x12, 0x1C, 0x25, 0x26, 0x2E):
        return s + f" #{arg} {cname(cls['cons'], arg)}"
    if op == 0x0A:
        return s + f" {struct.unpack('<f', struct.pack('<i', arg))[0]!r}"
    if op in (0x14, 0x15, 0x16, 0x17):
        return s + f" {arg:+d} -> {i + arg}"
    return s + f" {arg}"


def files(d):
    for root, _, fs in os.walk(d):
        for f in fs:
            if f.lower().endswith(".class"):
                yield os.path.join(root, f)


def main():
    cmd = sys.argv[1]
    if cmd == "dis":
        for ci, cls in enumerate(parse(sys.argv[2])):
            for bi, body in enumerate(cls["bodies"]):
                print(f"class {ci} body {bi}")
                for i, ins in enumerate(body):
                    print("  " + fmt(cls, i, ins))
    elif cmd == "stats":
        ops, opers, rng = Counter(), defaultdict(Counter), defaultdict(lambda: [1 << 31, -(1 << 31)])
        follow = defaultdict(Counter)
        for f in files(sys.argv[2]):
            for cls in parse(f):
                for body in cls["bodies"]:
                    for i, (op, _, arg) in enumerate(body):
                        ops[op] += 1
                        if arg is not None:
                            r = rng[op]
                            r[0], r[1] = min(r[0], arg), max(r[1], arg)
                        if op in (0x07, 0x08):
                            opers[op][arg] += 1
                        nxt = body[i + 1][0] if i + 1 < len(body) else -1
                        follow[op][nxt] += 1
        for op in sorted(ops):
            r = rng.get(op)
            top = ", ".join(f"{k:02X}:{v}" if k >= 0 else f"end:{v}" for k, v in follow[op].most_common(4))
            print(f"{op:02X} {MNEMONIC.get(op, '?'):11s} {ops[op]:7d} range={r if r else '-'} next=[{top}]")
        for op in (0x07, 0x08):
            print(f"operators under {op:02X}:")
            for k, v in sorted(opers[op].items()):
                print(f"   {k:3d} 0x{k:02x} {OPERATOR.get(k, '?'):8s} {v}")
    elif cmd == "grep":
        want = int(sys.argv[3], 16)
        n = int(sys.argv[4]) if len(sys.argv) > 4 else 5
        shown = 0
        for f in files(sys.argv[2]):
            for cls in parse(f):
                for body in cls["bodies"]:
                    for i, ins in enumerate(body):
                        if ins[0] == want and shown < n:
                            shown += 1
                            print(f"== {f}")
                            for j in range(max(0, i - 6), min(len(body), i + 5)):
                                print(("> " if j == i else "  ") + fmt(cls, j, body[j]))
        if shown == 0:
            print("no occurrences")


if __name__ == "__main__":
    main()
