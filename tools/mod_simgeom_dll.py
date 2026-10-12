"""SC3U mods for Apps\\SIMGEOM.DLL (block based).

  hightech_query   Query window of every zoned building gets a line "High-Tech industries in city: N"
                   (N = buildings of type 8700/8900/9100 = clean industry, counted once each by scanning the map)
                   (hook 0x100249B3 in the building query, cClass_1002a578 slot 0x124; code at 0x10028460)

  python mod_simgeom_dll.py            apply every block that is still pending (backup SIMGEOM.DLL.orig once)
  python mod_simgeom_dll.py --check    show the state of every block, write nothing
  python mod_simgeom_dll.py --restore  put the original DLL back
Set SIMGEOM_PATH to work on another copy of the DLL.
"""
import os, sys
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import asm_hightech_query, dllpatch

DLL = os.environ.get("SIMGEOM_PATH", r"C:\GOG Games\SimCity 3000 Unlimited\Apps\SIMGEOM.DLL")
ORIG_SHA256 = "17a76e833412108298e0f88e6af49c56280837584d74ce0b1b93da03bdcdfaba"


def hightech_query_blocks():
    cave, hook = asm_hightech_query.assemble()
    return [
        (asm_hightech_query.CAVE, b"\x00" * len(cave), cave, "hightech_query: count + text line (code)"),
        (asm_hightech_query.HOOK, bytes.fromhex("53 ff7508 8b4de8"), hook, "hightech_query: hook building query"),
    ]


SETS = [("hightech_query", hightech_query_blocks())]

if __name__ == "__main__":
    dllpatch.run(DLL, ORIG_SHA256, SETS, sys.argv)
