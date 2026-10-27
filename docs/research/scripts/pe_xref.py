#!/usr/bin/env python3
"""Static x86 helper for PE32 images (never executes the target).

Usage (run with python3 -I):
  pe_xref.py EXE str TEXT            -> VAs of ASCII string TEXT (exact, NUL-terminated) and code xrefs to it
  pe_xref.py EXE imm HEX             -> code locations where the 32-bit immediate HEX appears (push/cmp/mov)
  pe_xref.py EXE dis VA [N]          -> linear disassembly of N instructions from VA (hex)
  pe_xref.py EXE calls VA            -> direct call sites of function VA
  pe_xref.py EXE jt VA COUNT         -> dump COUNT dword entries of a jump table at VA
Requires: pefile, capstone.
"""
import struct
import sys

import capstone
import pefile


class Image:
    def __init__(self, path: str) -> None:
        self.pe = pefile.PE(path)
        self.base = self.pe.OPTIONAL_HEADER.ImageBase
        self.mem = bytearray(self.pe.get_memory_mapped_image())
        text = next(s for s in self.pe.sections if s.Name.startswith(b".text"))
        self.text_lo = self.base + text.VirtualAddress
        self.text_hi = self.text_lo + text.Misc_VirtualSize
        self.md = capstone.Cs(capstone.CS_ARCH_X86, capstone.CS_MODE_32)
        self.md.detail = False

    def read(self, va: int, n: int) -> bytes:
        o = va - self.base
        return bytes(self.mem[o:o + n])

    def u32(self, va: int) -> int:
        return struct.unpack_from("<I", self.mem, va - self.base)[0]

    def cstr(self, va: int, limit: int = 200) -> str | None:
        o = va - self.base
        if o < 0 or o >= len(self.mem):
            return None
        end = self.mem.find(b"\0", o, o + limit)
        if end < 0:
            return None
        s = self.mem[o:end]
        if not s or any(c < 0x20 or c > 0x7E for c in s if c not in (9, 10, 13)):
            return None
        return s.decode("latin1")

    def find_bytes(self, needle: bytes, text_only: bool = False):
        lo = (self.text_lo - self.base) if text_only else 0
        hi = (self.text_hi - self.base) if text_only else len(self.mem)
        i = self.mem.find(needle, lo, hi)
        while i >= 0:
            yield self.base + i
            i = self.mem.find(needle, i + 1, hi)

    def dis(self, va: int, n: int = 40):
        code = self.read(va, n * 16)
        out = []
        for ins in self.md.disasm(code, va):
            out.append(ins)
            if len(out) >= n:
                break
        return out

    def calls_to(self, target: int):
        lo, hi = self.text_lo - self.base, self.text_hi - self.base
        m = self.mem
        for o in range(lo, hi - 5):
            if m[o] == 0xE8:
                rel = struct.unpack_from("<i", m, o + 1)[0]
                if self.base + o + 5 + rel == target:
                    yield self.base + o


def main() -> None:
    img = Image(sys.argv[1])
    cmd = sys.argv[2]
    if cmd == "str":
        needle = sys.argv[3].encode("latin1") + b"\0"
        for va in img.find_bytes(needle):
            if img.mem[va - img.base - 1] != 0:
                continue
            refs = list(img.find_bytes(struct.pack("<I", va), text_only=True))
            print(f"string @0x{va:08x} code refs: {' '.join(f'0x{r:08x}' for r in refs)}")
            data_refs = [r for r in img.find_bytes(struct.pack('<I', va)) if not img.text_lo <= r < img.text_hi]
            if data_refs:
                print(f"  data refs: {' '.join(f'0x{r:08x}' for r in data_refs[:20])}")
    elif cmd == "imm":
        needle = struct.pack("<I", int(sys.argv[3], 16))
        for va in img.find_bytes(needle, text_only=True):
            print(f"0x{va:08x}")
    elif cmd == "dis":
        va = int(sys.argv[3], 16)
        n = int(sys.argv[4]) if len(sys.argv) > 4 else 40
        for ins in img.dis(va, n):
            note = ""
            for tok in ins.op_str.replace(",", " ").replace("[", " ").replace("]", " ").split():
                if tok.startswith("0x") and len(tok) >= 8:
                    s = img.cstr(int(tok, 16))
                    if s:
                        note = f'  ; "{s[:60]}"'
            print(f"0x{ins.address:08x}: {ins.mnemonic:6s} {ins.op_str}{note}")
    elif cmd == "calls":
        for va in img.calls_to(int(sys.argv[3], 16)):
            print(f"0x{va:08x}")
    elif cmd == "writes":
        # direct stores to a global: mov [abs], reg  (A3 / 89 modrm=00rrr101); prints preceding instructions
        g = struct.pack("<I", int(sys.argv[3], 16))
        ctx = int(sys.argv[4]) if len(sys.argv) > 4 else 8
        for va in img.find_bytes(g, text_only=True):
            o = va - img.base
            op_va = None
            if img.mem[o - 1] == 0xA3:
                op_va = va - 1
            elif img.mem[o - 2] == 0x89 and (img.mem[o - 1] & 0xC7) == 0x05:
                op_va = va - 2
            if op_va is None:
                continue
            # find a decodable window that ends exactly on op_va
            for back in range(ctx * 7, 0, -1):
                ins = img.dis(op_va - back, ctx * 3)
                addrs = [i.address for i in ins]
                if op_va in addrs:
                    k = addrs.index(op_va)
                    for i in ins[max(0, k - ctx):k + 1]:
                        note = ""
                        for tok in i.op_str.replace(",", " ").replace("[", " ").replace("]", " ").split():
                            if tok.startswith("0x") and len(tok) >= 8:
                                s = img.cstr(int(tok, 16))
                                if s:
                                    note = f'  ; "{s[:60]}"'
                        print(f"0x{i.address:08x}: {i.mnemonic:6s} {i.op_str}{note}")
                    print("--")
                    break
    elif cmd == "jt":
        va, n = int(sys.argv[3], 16), int(sys.argv[4])
        for i in range(n):
            print(f"[{i:3d} 0x{i:02x}] 0x{img.u32(va + 4 * i):08x}")


if __name__ == "__main__":
    main()
