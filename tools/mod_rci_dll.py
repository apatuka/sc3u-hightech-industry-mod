"""SC3U mods for Apps\\SIMRCI.DLL (block based; works from the original DLL or from an earlier version).

  rci            demand R/C/I always at maximum + zone developers never abandon buildings
                 (hooks 0x10042FD0, 0x10029A38, 0x100191FE; code at 0x1004A170-0x1004A1D7)
  hightech_map   Map View > Zones: clean (high-tech) industry drawn in magenta
                 (hook 0x10036169 cSC3ZoneDataMap::GetCellColor; code at 0x1004A1E0-0x1004A2BF)
  hightech_zone  4th industrial zone 'High-Tech Industrial' = zone type 12 (code from 0x1004A2D0, see
                 asm_hightech_zone.py; needs the SYS.PAK / text changes of mod_hightech_zone.py)

  python mod_rci_dll.py            apply every block that is still pending (backup SIMRCI.DLL.orig once)
  python mod_rci_dll.py --check    show the state of every block, write nothing
  python mod_rci_dll.py --restore  put the original DLL back
All new code lives in the zero padding at the end of .text; .text VirtualSize is extended to cover it.
No new code contains absolute addresses (the DLL is relocated at load time); the only relocated slot touched is
the Query switch jump-table entry of hightech_zone, which keeps its relocation.
"""
import hashlib, os, shutil, struct, sys
import pefile
from capstone import Cs, CS_ARCH_X86, CS_MODE_32
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import asm_hightech_map, asm_hightech_zone, dllpatch

DLL = os.environ.get("SIMRCI_PATH", r"C:\GOG Games\SimCity 3000 Unlimited\Apps\SIMRCI.DLL")
ORIG_SHA256 = "41378d72f09f89980b19e210787225950e7f6f477928f62d960ce1f811395f53"
BASE = 0x10000000


def rel32(src_next, dst):
    return struct.pack("<i", dst - src_next)


def rci_blocks():
    CAVE_A, CAVE_B1, CAVE_B2 = 0x1004A170, 0x1004A1A0, 0x1004A1C0
    out = []
    site = 0x10042FD0
    orig = bytes.fromhex("8b462c 5b 85c0")
    cave = bytearray()
    cave += bytes.fromhex("8b4608")
    cave += bytes.fromhex("3db80b0000") + bytes.fromhex("740e")
    cave += bytes.fromhex("3d88130000") + bytes.fromhex("7407")
    cave += bytes.fromhex("3d401f0000") + bytes.fromhex("7506")
    cave += bytes.fromhex("8b4624") + bytes.fromhex("89462c")
    cave += orig
    cave += b"\xe9" + rel32(CAVE_A + len(cave) + 5, site + 6)
    out.append((CAVE_A, b"\x00" * len(cave), bytes(cave), "rci: R/C/I demand = max (code)"))
    out.append((site, orig, b"\xe9" + rel32(site + 5, CAVE_A) + b"\x90", "rci: hook valve recompute"))
    for site, cave_va, label in ((0x10029A38, CAVE_B1, "res/com"), (0x100191FE, CAVE_B2, "ind")):
        orig = bytes.fromhex("55 8bec 83ec10")
        cave = bytearray(bytes.fromhex("f644240c02 7405 33c0 c20c00")) + orig
        cave += b"\xe9" + rel32(cave_va + len(cave) + 5, site + 6)
        out.append((cave_va, b"\x00" * len(cave), bytes(cave), "rci: block abandonment %s (code)" % label))
        out.append((site, orig, b"\xe9" + rel32(site + 5, cave_va) + b"\x90", "rci: hook SetBuildingState %s" % label))
    return out


def hightech_map_blocks():
    cave, hook = asm_hightech_map.assemble()
    return [
        (asm_hightech_map.CAVE, b"\x00" * len(cave), cave, "hightech_map: magenta for clean industry (code)"),
        (asm_hightech_map.HOOK, bytes.fromhex("55 8bec 8b493c"), hook, "hightech_map: hook ZoneDataMap::GetCellColor"),
    ]


SETS = [("rci", rci_blocks()), ("hightech_map", hightech_map_blocks()), ("hightech_zone", asm_hightech_zone.assemble())]


def main():
    dllpatch.run(DLL, ORIG_SHA256, SETS, sys.argv)


if __name__ == "__main__":
    main()
