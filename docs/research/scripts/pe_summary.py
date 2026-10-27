#!/usr/bin/env python3
"""Static PE summary: header, sections (with entropy), imports grouped by DLL.

Never executes the target. Usage: python3 -I pe_summary.py FILE [--imports]
Requires: pefile (pip install pefile).
"""
import math
import sys

import pefile


def entropy(data: bytes) -> float:
    if not data:
        return 0.0
    counts = [0] * 256
    for b in data:
        counts[b] += 1
    n = len(data)
    return -sum(c / n * math.log2(c / n) for c in counts if c)


def main() -> None:
    path = sys.argv[1]
    show_imports = "--imports" in sys.argv
    pe = pefile.PE(path, fast_load=False)
    fh, oh = pe.FILE_HEADER, pe.OPTIONAL_HEADER
    print(f"# {path}")
    print(f"machine=0x{fh.Machine:04x} timestamp=0x{fh.TimeDateStamp:08x} "
          f"imagebase=0x{oh.ImageBase:08x} entry=0x{oh.ImageBase + oh.AddressOfEntryPoint:08x} "
          f"linker={oh.MajorLinkerVersion}.{oh.MinorLinkerVersion} subsystem={oh.Subsystem}")
    print("sections:")
    for s in pe.sections:
        name = s.Name.rstrip(b"\0").decode("latin1")
        data = s.get_data()
        print(f"  {name:8s} va=0x{oh.ImageBase + s.VirtualAddress:08x} vsize=0x{s.Misc_VirtualSize:07x} "
              f"rawsize=0x{s.SizeOfRawData:07x} flags=0x{s.Characteristics:08x} entropy={entropy(data):.2f}")
    if hasattr(pe, "DIRECTORY_ENTRY_IMPORT"):
        print("imports:")
        for imp in pe.DIRECTORY_ENTRY_IMPORT:
            names = [(i.name.decode() if i.name else f"#{i.ordinal}") for i in imp.imports]
            print(f"  {imp.dll.decode()}: {len(names)}")
            if show_imports:
                for i, n in zip(imp.imports, names):
                    print(f"    0x{i.address:08x} {n}")
    if hasattr(pe, "DIRECTORY_ENTRY_EXPORT"):
        print("exports:", len(pe.DIRECTORY_ENTRY_EXPORT.symbols))


if __name__ == "__main__":
    main()
