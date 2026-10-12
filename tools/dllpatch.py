"""Block-based patcher shared by the SC3U DLL mods.

A set is (name, [(va, old_bytes, new_bytes, description[, allow_reloc]), ...]). Each block is 'applied',
'pending' (old bytes present) or UNKNOWN (anything else -> refuse). New code must live in the zero padding at the
end of .text; .text VirtualSize is extended (relative to the section start) to cover it. Bytes covered by a base
relocation are never touched, except by allow_reloc blocks: those must replace exactly one relocated dword with
another address inside the image (e.g. a switch jump-table entry), which the loader then relocates as before.
Hooks are also checked against direct branches that land inside the replaced bytes.
"""
import hashlib, os, shutil, struct, sys
import pefile

BASE = 0x10000000


def rel32(src_next, dst):
    return struct.pack("<i", dst - src_next)


def _branch_targets(data, pe, text):
    from capstone import Cs, CS_ARCH_X86, CS_MODE_32
    md = Cs(CS_ARCH_X86, CS_MODE_32)
    md.skipdata = True
    start = text.PointerToRawData
    code = data[start:start + text.Misc_VirtualSize]
    out = {}
    for i in md.disasm(code, BASE + text.VirtualAddress):
        if (i.mnemonic.startswith("j") or i.mnemonic == "call") and i.op_str.startswith("0x"):
            out.setdefault(int(i.op_str, 16), []).append(i.address)
    return out


def run(dll, orig_sha256, sets, argv):
    label = os.path.basename(dll)
    if "--restore" in argv:
        o = dll + ".orig"
        if os.path.exists(o) and hashlib.sha256(open(o, "rb").read()).hexdigest() == orig_sha256:
            shutil.copy2(o, dll)
            print("restored", dll)
        else:
            print("no valid %s.orig backup found" % label)
        return
    data = bytearray(open(dll, "rb").read())
    pe = pefile.PE(data=bytes(data))
    text = [s for s in pe.sections if s.Name.startswith(b".text")][0]
    text_start = BASE + text.VirtualAddress
    text_end_orig = text_start + text.Misc_VirtualSize
    in_cave = lambda va: text_end_orig - 0x10 <= va < text_start + text.SizeOfRawData
    off = lambda va: pe.get_offset_from_rva(va - BASE)
    pending, errors = [], []
    for name, blocks in sets:
        for b in blocks:
            va, old, new, desc = b[:4]
            cur = bytes(data[off(va):off(va) + len(new)])
            state = "applied" if cur == new else "pending" if cur == old else "UNKNOWN"
            print("  %-14s %-9s %08x %s" % (name, state, va, desc))
            if state == "pending":
                pending.append((va, new, len(b) > 4 and b[4]))
            elif state == "UNKNOWN":
                errors.append(desc)
    if errors:
        sys.exit("%s has unexpected bytes (unsupported version?) - nothing changed" % label)
    if not pending:
        print("everything already applied")
        return
    if "--check" in argv:
        print("%d block(s) would be applied" % len(pending))
        return
    rel = {BASE + e.rva for b in pe.DIRECTORY_ENTRY_BASERELOC for e in b.entries if e.type}
    image_end = BASE + pe.OPTIONAL_HEADER.SizeOfImage
    for va, new, allow_reloc in pending:
        if allow_reloc:
            target = struct.unpack("<I", new)[0] if len(new) == 4 else 0
            if len(new) != 4 or va not in rel or not BASE <= target < image_end:
                sys.exit("relocated-slot patch at %08x is not a single in-image dword - aborting" % va)
        elif any(va - 3 <= r < va + len(new) for r in rel):
            sys.exit("relocation inside patch at %08x - aborting" % va)
    targets = _branch_targets(bytes(data), pe, text)
    for va, new, allow_reloc in pending:
        if text_start <= va < text_end_orig and not allow_reloc:
            bad = [(hex(t), [hex(s) for s in targets[t]]) for t in range(va + 1, va + len(new)) if t in targets]
            if bad:
                sys.exit("branch into the middle of the hook at %08x: %s - aborting" % (va, bad))
    for va, new, allow_reloc in pending:
        data[off(va):off(va) + len(new)] = new
    cave_end = max([va + len(new) for _, blocks in sets for va, old, new, *rest in blocks if in_cave(va)] + [0])
    need = cave_end - text_start
    nxt = min(s.VirtualAddress for s in pe.sections if s.VirtualAddress > text.VirtualAddress)
    if text.VirtualAddress + need > nxt or need > text.SizeOfRawData:
        sys.exit("code would overflow .text - aborting")
    if need > text.Misc_VirtualSize:
        h = text.get_file_offset() + 8
        data[h:h + 4] = struct.pack("<I", need)
    if not os.path.exists(dll + ".orig") and hashlib.sha256(open(dll, "rb").read()).hexdigest() == orig_sha256:
        shutil.copy2(dll, dll + ".orig")
        print("backup:", dll + ".orig")
    open(dll, "wb").write(data)
    pe2 = pefile.PE(dll)
    secs = sorted(pe2.sections, key=lambda s: s.VirtualAddress)
    for a, b in zip(secs, secs[1:]):
        assert a.VirtualAddress + ((a.Misc_VirtualSize + 0xFFF) & ~0xFFF) <= b.VirtualAddress, "section overlap"
    print("applied %d block(s); sha256 %s" % (len(pending), hashlib.sha256(data).hexdigest()))
