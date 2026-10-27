#!/usr/bin/env python3
"""Resolve an MSVC6 switch (compare tree + byte-index jump tables) statically.

Walks the instructions starting at DISPATCH_VA with the switch register holding a known
value, evaluating only cmp/sub/add/dec/inc/test and conditional jumps, and the
`xor r,r; mov rl, byte [reg+T]; jmp [r*4+J]` idiom. Nothing from the target is executed.

Usage: python3 -I switch_resolve.py EXE DISPATCH_VA REG VALUE [VALUE...]
       values may be hex (0x101a) or ranges 0x0-0x7f
Prints: value -> handler VA (or 'default@VA' when it falls to the given default).
"""
import struct
import sys
import os

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from pe_xref import Image  # noqa: E402


def to_signed(v: int) -> int:
    v &= 0xFFFFFFFF
    return v - (1 << 32) if v & 0x80000000 else v


def resolve(img: Image, start: int, reg: str, value: int, max_steps: int = 400):
    regs = {reg: value}
    flags = None  # (signed_a, signed_b, unsigned_a, unsigned_b)
    pc = start
    trace = []
    for _ in range(max_steps):
        ins = next(img.md.disasm(img.read(pc, 16), pc))
        trace.append(ins.address)
        mn, ops = ins.mnemonic, [o.strip() for o in ins.op_str.split(",")]
        nxt = pc + ins.size

        def val(o):
            if o in regs:
                return regs[o]
            return int(o, 16) if o.startswith("0x") else int(o)

        if mn == "cmp" and ops[0] in regs:
            a, b = val(ops[0]), val(ops[1])
            flags = (to_signed(a), to_signed(b), a & 0xFFFFFFFF, b & 0xFFFFFFFF)
        elif mn in ("sub", "add") and ops[0] in regs:
            a, b = regs[ops[0]], val(ops[1])
            r = a - b if mn == "sub" else a + b
            flags = (to_signed(r), 0, r & 0xFFFFFFFF, 0) if mn == "add" else (to_signed(a), to_signed(b), a & 0xFFFFFFFF, b & 0xFFFFFFFF)
            regs[ops[0]] = r & 0xFFFFFFFF
        elif mn in ("dec", "inc") and ops[0] in regs:
            a = regs[ops[0]]
            r = a - 1 if mn == "dec" else a + 1
            regs[ops[0]] = r & 0xFFFFFFFF
            flags = (to_signed(r), 0, r & 0xFFFFFFFF, 0)
        elif mn == "test" and ops[0] in regs and ops[0] == ops[1]:
            a = regs[ops[0]]
            flags = (to_signed(a), 0, a & 0xFFFFFFFF, 0)
        elif mn == "lea" and ops[1].startswith("["):
            inner = ops[1].strip("[]").replace(" ", "")
            r, sign, off = (inner.partition("-") if "-" in inner else inner.partition("+"))
            if r not in regs:
                return pc, trace
            o = int(off, 16)
            regs[ops[0]] = (regs[r] - o if sign == "-" else regs[r] + o) & 0xFFFFFFFF
        elif mn == "xor" and ops[0] == ops[1]:
            regs[ops[0]] = 0
        elif mn == "mov" and ops[0] in ("dl", "cl", "al", "bl") and "byte ptr" in ops[1]:
            inner = ops[1].split("[")[1].rstrip("]")
            r, t = [x.strip() for x in inner.split("+")]
            idx = regs[r]
            full = {"dl": "edx", "cl": "ecx", "al": "eax", "bl": "ebx"}[ops[0]]
            regs[full] = img.read(int(t, 16) + idx, 1)[0]
        elif mn == "movzx" and "byte ptr" in ops[1]:
            inner = ops[1].split("[")[1].rstrip("]")
            r, t = [x.strip() for x in inner.split("+")]
            regs[ops[0]] = img.read(int(t, 16) + regs[r], 1)[0]
        elif mn == "jmp" and ops[0].startswith("dword ptr ["):
            inner = ops[0].split("[")[1].rstrip("]")
            a, t = [x.strip() for x in inner.split("+")]
            r = a.split("*")[0]
            return img.u32(int(t, 16) + 4 * regs[r]), trace
        elif mn == "jmp":
            nxt = int(ops[0], 16)
        elif mn.startswith("j"):
            sa, sb, ua, ub = flags
            cond = {
                "je": sa == sb, "jne": sa != sb, "jg": sa > sb, "jge": sa >= sb, "jl": sa < sb, "jle": sa <= sb,
                "ja": ua > ub, "jae": ua >= ub, "jb": ua < ub, "jbe": ua <= ub,
            }[mn]
            if cond:
                nxt = int(ops[0], 16)
        elif mn == "mov" and ops[0] in regs:
            # register overwritten by something we do not track: switch ends here
            return pc, trace
        else:
            return pc, trace
        pc = nxt
    return None, trace


def parse_values(args):
    for a in args:
        if "-" in a:
            lo, hi = (int(x, 16) for x in a.split("-"))
            yield from range(lo, hi + 1)
        else:
            yield int(a, 16)


def main() -> None:
    img = Image(sys.argv[1])
    start = int(sys.argv[2], 16)
    reg = sys.argv[3]
    for v in parse_values(sys.argv[4:]):
        target, _ = resolve(img, start, reg, v)
        print(f"0x{v:04x} -> 0x{target:08x}" if target else f"0x{v:04x} -> ?")


if __name__ == "__main__":
    main()
