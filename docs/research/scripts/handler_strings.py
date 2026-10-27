#!/usr/bin/env python3
"""For each handler VA, disassemble linearly up to the next handler (or N instructions) and print
string references, direct call targets and FPU/int arithmetic mnemonics found. Static only.

Usage: python3 -I handler_strings.py EXE LABEL=VA [LABEL=VA ...] [--max N] [--full]
"""
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from pe_xref import Image  # noqa: E402

INTERESTING = {"fadd", "fsub", "fsubr", "fmul", "fdiv", "fdivr", "fchs", "add", "sub", "imul", "idiv", "neg",
               "shl", "sar", "shr", "and", "or", "xor", "not", "fcomp", "fcompp", "fcom", "fild", "fistp", "sete",
               "setne", "setl", "setg", "setle", "setge", "setb", "seta", "fprem", "cdq"}


def main() -> None:
    img = Image(sys.argv[1])
    args = [a for a in sys.argv[2:] if "=" in a]
    mx = int(sys.argv[sys.argv.index("--max") + 1]) if "--max" in sys.argv else 120
    full = "--full" in sys.argv
    hs = sorted((int(v, 16), k) for k, v in (a.split("=") for a in args))
    allstarts = sorted(set(h for h, _ in hs))
    for va, label in hs:
        later = [s for s in allstarts if s > va]
        end = later[0] if later else va + 0x400
        strs, calls, ops = [], [], []
        lines = []
        for ins in img.dis(va, mx):
            if ins.address >= end:
                break
            lines.append(f"    0x{ins.address:08x}: {ins.mnemonic} {ins.op_str}")
            for tok in ins.op_str.replace(",", " ").replace("[", " ").replace("]", " ").split():
                if tok.startswith("0x") and len(tok) >= 8:
                    s = img.cstr(int(tok, 16))
                    if s and len(s) > 1:
                        strs.append(s[:70])
                        lines[-1] += f'   ; "{s[:50]}"'
            if ins.mnemonic == "call":
                calls.append(ins.op_str)
            if ins.mnemonic in INTERESTING and not (ins.mnemonic == "xor" and ins.op_str.split(",")[0] == ins.op_str.split(",")[-1].strip()):
                ops.append(ins.mnemonic)
            if ins.mnemonic == "jmp" and ins.op_str == "0x4238ed":
                pass
        print(f"{label} @0x{va:08x}..0x{end:08x}")
        if strs:
            print(f"  strings: {strs}")
        print(f"  calls: {calls[:12]}")
        print(f"  ops: {sorted(set(ops))}")
        if full:
            print("\n".join(lines))


if __name__ == "__main__":
    main()
