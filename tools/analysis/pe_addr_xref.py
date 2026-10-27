#!/usr/bin/env python3
"""Static address cross-reference helper for PE32 images (never executes the target).

Library + CLI.  CLI (python3 -I):  pe_addr_xref.py EXE HEXADDR [HEXADDR ...]
For each address prints: section, initial value (u32/float/ASCII), and every instruction whose operand
(memory displacement or immediate) equals the address, with the enclosing function (nearest preceding direct
call target; approximate) and the strings/natives that function references.
Requires: pefile, capstone.
"""
import struct
import sys

import capstone
import pefile
from capstone import x86_const as X


class Image:
    def __init__(self, path):
        self.path = path
        self.pe = pefile.PE(path)
        self.base = self.pe.OPTIONAL_HEADER.ImageBase
        self.mem = bytearray(self.pe.get_memory_mapped_image())
        self.sections = []
        for s in self.pe.sections:
            self.sections.append((s.Name.rstrip(b"\0").decode("latin1"), self.base + s.VirtualAddress,
                                  s.Misc_VirtualSize, s.SizeOfRawData, s.Characteristics))
        self.code_secs = [(n, lo, sz) for (n, lo, sz, raw, ch) in self.sections if ch & 0x20000000]
        self._xrefs = None
        self._funcs = None

    def section_of(self, va):
        for n, lo, vs, raw, ch in self.sections:
            if lo <= va < lo + max(vs, raw):
                return n, (va - lo) < raw  # (name, has_file_bytes)
        return None, False

    def read(self, va, n):
        o = va - self.base
        return bytes(self.mem[o:o + n])

    def u32(self, va):
        return struct.unpack_from("<I", self.mem, va - self.base)[0]

    def f32(self, va):
        return struct.unpack_from("<f", self.mem, va - self.base)[0]

    def cstr(self, va, limit=120):
        o = va - self.base
        if o < 0 or o >= len(self.mem):
            return None
        end = self.mem.find(b"\0", o, o + limit)
        if end < 0:
            return None
        s = bytes(self.mem[o:end])
        if len(s) < 3 or any(c < 0x20 or c > 0x7E for c in s if c not in (9, 10, 13)):
            return None
        return s.decode("latin1")

    def build(self):
        if self._xrefs is not None:
            return
        md = capstone.Cs(capstone.CS_ARCH_X86, capstone.CS_MODE_32)
        md.detail = True
        xrefs, calls = {}, set()
        ins_list = []
        for name, lo, sz in self.code_secs:
            off = lo - self.base
            code = bytes(self.mem[off:off + sz])
            pos = 0
            while pos < len(code):
                chunk_end = min(len(code), pos + 65536)
                start = pos
                for ins in md.disasm(code[pos:chunk_end], lo + pos):
                    ins_list.append(ins.address)
                    if ins.id == X.X86_INS_CALL and ins.operands and ins.operands[0].type == X.X86_OP_IMM:
                        calls.add(ins.operands[0].imm & 0xFFFFFFFF)
                    for op in ins.operands:
                        v = None
                        if op.type == X.X86_OP_MEM and op.mem.base == 0 and op.mem.index == 0:
                            v = op.mem.disp & 0xFFFFFFFF
                        elif op.type == X.X86_OP_IMM and ins.id not in (X.X86_INS_CALL, X.X86_INS_JMP):
                            v = op.imm & 0xFFFFFFFF
                        elif op.type == X.X86_OP_MEM and op.mem.base != 0 and op.mem.disp:
                            xrefs.setdefault(("d", op.mem.disp & 0xFFFFFFFF), []).append(ins.address)
                        if v is not None and 0x400000 <= v < 0x1000000:
                            xrefs.setdefault(v, []).append(ins.address)
                    pos = ins.address + ins.size - lo
                if chunk_end < len(code) and pos >= chunk_end - 16:
                    continue  # chunk boundary, not a decode error
                pos += 1
        self._xrefs = xrefs
        self._funcs = sorted(calls)
        self._ins = ins_list

    def xrefs(self, addr):
        self.build()
        return self._xrefs.get(addr, [])

    def displaced(self, disp):
        self.build()
        return self._xrefs.get(("d", disp), [])

    def func_of(self, va):
        self.build()
        import bisect
        i = bisect.bisect_right(self._funcs, va) - 1
        return self._funcs[i] if i >= 0 else None

    def disasm_at(self, va, n=1):
        md = capstone.Cs(capstone.CS_ARCH_X86, capstone.CS_MODE_32)
        out = []
        for ins in md.disasm(self.read(va, 16 * n), va):
            out.append(ins)
            if len(out) >= n:
                break
        return out

    def func_strings(self, fva, span=0x600):
        """Strings pushed/loaded inside [fva, next function)."""
        self.build()
        import bisect
        i = bisect.bisect_right(self._funcs, fva)
        end = self._funcs[i] if i < len(self._funcs) else fva + span
        end = min(end, fva + 0x2000)
        out = []
        md = capstone.Cs(capstone.CS_ARCH_X86, capstone.CS_MODE_32)
        md.detail = True
        for ins in md.disasm(self.read(fva, end - fva), fva):
            for op in ins.operands:
                if op.type == X.X86_OP_IMM and 0x5e7000 <= (op.imm & 0xFFFFFFFF) < 0x7a2000 and ins.id != X.X86_INS_CALL:
                    s = self.cstr(op.imm & 0xFFFFFFFF)
                    if s:
                        out.append(s)
        return out, end


def describe(img, addr):
    sec, has = img.section_of(addr)
    raw = img.read(addr, 4) if has else b""
    desc = f"0x{addr:08x} {sec}{'' if has else ' (BSS)'}"
    if raw:
        desc += f" u32={struct.unpack('<I', raw)[0]} f32={struct.unpack('<f', raw)[0]:.6g}"
    return desc


if __name__ == "__main__":
    im = Image(sys.argv[1])
    for a in sys.argv[2:]:
        a = int(a, 16)
        print(describe(im, a))
        for x in im.xrefs(a):
            f = im.func_of(x)
            ins = im.disasm_at(x)[0]
            strs, end = im.func_strings(f) if f else ([], 0)
            print(f"   {x:08x} {ins.mnemonic} {ins.op_str}   func=0x{f:08x}" if f else f"   {x:08x} {ins.mnemonic} {ins.op_str}",
                  strs[:4])
