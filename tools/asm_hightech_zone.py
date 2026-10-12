"""Assemble the SIMRCI part of the 'High-Tech Industrial' zone (zone type 12).

Zone type 12 is unused by the game (the zone simulator has 23 developer slots; INI types 1-3/5-7/9-11/14/15/17).
SC3ZoneLayer.ini gives it its own cSC3IndustrialZoneDeveloper whose building list holds only clean industry;
the code below wires the rest of the game to it:

  ct   cSC3ZoneToolFactory::CreateTool  new menu command CMD -> zone tool for type 12 (dense-industrial cursor)
  cnb  ChooseNewBuilding               a developer without dirty buildings always picks clean industry
                                       (otherwise it would only build when the education-based clean roll wins)
  zdi  GetZoneDisplayIndex             empty type-12 lots are drawn with the dense-industrial zone tiles
  za   ZoneArea                        type 12 is charged like dense zoning
  q    GetZoneQueryStrings             Query 'Zone Type:' shows string QUERY_STR (jump table entry 12)
  color table entry 12 (Map View > Zones) becomes magenta (data only)
Position independent except the jump-table entry, which is an existing relocated slot (the loader adds the delta).
"""
import struct
from keystone import Ks, KS_ARCH_X86, KS_MODE_32

CMD = 0xC8545A31           # new menu command (MenuItem.INI); must be > 0x82E88C9C to reach the hooked compare chain
ZONE = 12
QUERY_STR = 0x4801         # SC3StringsQuery.IXF, group 0x82E0074C
CAVE = 0x1004A2D0

HOOKS = {   # name: (hook va, original bytes, cave source)
    "ct": (0x1000BD41, "81f9d489e8e2 740b", """
        cmp ecx, 0xe2e889d4
        je 0x1000bd54
        cmp ecx, {cmd}
        jne 0x1000bd49
        push 0x12c
        call 0x10044c46
        pop ecx
        mov ecx, eax
        mov dword ptr [ebp+8], ecx
        test ecx, ecx
        mov dword ptr [ebp-4], 9
        je 0x1000be56
        push 0x26
        push 0x15
        push {zone}
        push 0x22e43512
        jmp 0x1000be29
    """),
    "cnb": (0x10018A08, "8b8eb0000000", """
        mov eax, dword ptr [esi+0x34]
        cmp dword ptr [eax+8], eax
        jne normal
        xor al, al
        jmp 0x10018b1b
    normal:
        mov ecx, dword ptr [esi+0xb0]
        jmp 0x10018a0e
    """),
    "zdi": (0x10035518, "83e80b 744c", """
        cmp eax, {zone}
        je 0x10035569
        sub eax, 0xb
        je 0x10035569
        jmp 0x1003551d
    """),
    "za": (0x10035FBE, "83e803 740f", """
        cmp eax, {zone_m11}
        je 0x10035fd6
        sub eax, 3
        je 0x10035fd2
        jmp 0x10035fc3
    """),
}
Q_SRC = """
        push esi
        push {qstr}
        jmp 0x10034ce7
"""
JUMPTABLE_ENTRY = 0x10035381 + (ZONE - 1) * 4      # GetZoneQueryStrings zone-type switch, entry for type 12
JUMPTABLE_OLD = 0x10035057                          # default case (no label)
COLOR_ENTRY = 0x10058100 + ZONE * 8                 # cSC3ZoneDataMap colour table: R,G,B then converted colour
COLOR_OLD, COLOR_NEW = bytes.fromhex("646400"), bytes.fromhex("ff00ff")


def _asm(ks, src, va):
    src = src.format(cmd=hex(CMD), zone=ZONE, zone_m11=ZONE - 11, qstr=hex(QUERY_STR))
    code, _ = ks.asm(src, va)
    return bytes(code)


def assemble():
    """Return a list of (va, old_bytes, new_bytes, description, allow_reloc)."""
    ks = Ks(KS_ARCH_X86, KS_MODE_32)
    blocks, va = [], CAVE
    for name, (hook, orig, src) in HOOKS.items():
        orig = bytes.fromhex(orig)
        code = _asm(ks, src, va)
        jmp = _asm(ks, "jmp %s" % hex(va), hook)
        blocks.append((va, b"\x00" * len(code), code, "hightech_zone: %s (code)" % name, False))
        blocks.append((hook, orig, jmp + b"\x90" * (len(orig) - len(jmp)), "hightech_zone: hook %s" % name, False))
        va = (va + len(code) + 15) & ~15
    q = _asm(ks, Q_SRC, va)
    blocks.append((va, b"\x00" * len(q), q, "hightech_zone: Query label (code)", False))
    blocks.append((JUMPTABLE_ENTRY, struct.pack("<I", JUMPTABLE_OLD), struct.pack("<I", va),
                   "hightech_zone: Query switch entry 12 -> label code", True))
    blocks.append((COLOR_ENTRY, COLOR_OLD, COLOR_NEW, "hightech_zone: Zones map colour 12 = magenta", False))
    return blocks


if __name__ == "__main__":
    from capstone import Cs, CS_ARCH_X86, CS_MODE_32
    md = Cs(CS_ARCH_X86, CS_MODE_32)
    for va, old, new, desc, rel in assemble():
        print("%08x %-50s %s -> %s" % (va, desc, old[:8].hex(), new[:8].hex() + ("..." if len(new) > 8 else "")))
        if desc.endswith("(code)") or "hook" in desc:
            for i in md.disasm(new, va):
                print("      %08x  %-20s %s %s" % (i.address, i.bytes.hex(), i.mnemonic, i.op_str))
