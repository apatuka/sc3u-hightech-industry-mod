"""Assemble the SIMRCI 'high-tech industry on the Zones map' hook.

cSC3ZoneDataMap::GetCellColor(x, z, &color) @ 0x10036169 (vtable slot) is redirected to a wrapper:
  r = original(x, z, &color)            (called through a trampoline with copies of the args)
  if r and the building at (x,z) has occupant type 8700/8900/9100 (clean = high-tech industry):
      color = graphics->ColorFromRGB(255, 0, 255)    (magenta, same conversion the map uses)
Position independent: only relative calls/jumps and vtable calls (the DLL is relocated at load).
"""
from keystone import Ks, KS_ARCH_X86, KS_MODE_32

HOOK = 0x10036169          # function entry: push ebp; mov ebp,esp; mov ecx,[ecx+0x3c]  (6 bytes, no relocations)
RESUME = 0x1003616F
APP_ACCESSOR = 0x1003CEEC  # SIMRCI copy of the shared application accessor (returns app)
GFX = 0x10039B83           # returns the graphics system (cdecl, no args)
CAVE = 0x1004A1E0

SRC = """
wrapper:
    push ebp
    mov ebp, esp
    push ebx
    push esi
    push edi
    push 0                          ; [ebp-0x10] = occupant out-pointer
    push dword ptr [ebp+0x10]       ; &color
    push dword ptr [ebp+0x0c]       ; z
    push dword ptr [ebp+0x08]       ; x
    call tramp                      ; original GetCellColor (ecx = this, untouched)
    mov bl, al
    test al, al
    jz done
    call {app}                      ; app
    test eax, eax
    jz done
    mov ecx, eax
    mov edx, dword ptr [ecx]
    call dword ptr [edx+0x18]       ; app->GetCity()
    test eax, eax
    jz done
    mov ecx, eax
    mov edx, dword ptr [ecx]
    call dword ptr [edx+0x11c]      ; city->GetOccupantManager()
    test eax, eax
    jz done
    lea edx, [ebp-0x10]
    push edx                        ; &occupant
    push dword ptr [ebp+0x0c]       ; z
    push dword ptr [ebp+0x08]       ; x
    mov ecx, eax
    mov edx, dword ptr [ecx]
    call dword ptr [edx+0x7c]       ; occMgr->GetOccupantAt(x, z, &occ)  (AddRef'd)
    test al, al
    jz done
    mov esi, dword ptr [ebp-0x10]
    test esi, esi
    jz done
    xor edi, edi
    mov ecx, esi
    mov edx, dword ptr [ecx]
    call dword ptr [edx+0x8c]       ; occ->GetAgentType()  (type node)
    test eax, eax
    jz release
    mov ecx, eax
    mov edx, dword ptr [ecx]
    call dword ptr [edx+0x10]       ; node->GetID()
    cmp eax, 8700
    je hightech
    cmp eax, 8900
    je hightech
    cmp eax, 9100
    jne release
hightech:
    mov edi, 1
release:
    mov ecx, esi
    mov edx, dword ptr [ecx]
    call dword ptr [edx+8]          ; occ->Release()
    test edi, edi
    jz done
    call {gfx}                      ; graphics system
    test eax, eax
    jz done
    push 0xff                       ; b
    push 0                          ; g
    push 0xff                       ; r
    mov ecx, eax
    mov edx, dword ptr [ecx]
    call dword ptr [edx+0x84]       ; gfx->ColorFromRGB(r, g, b)
    movzx eax, ax
    mov edx, dword ptr [ebp+0x10]
    mov dword ptr [edx], eax        ; *color = magenta
done:
    mov al, bl
    add esp, 4
    pop edi
    pop esi
    pop ebx
    pop ebp
    ret 0x0c
tramp:
    push ebp                        ; displaced original prologue
    mov ebp, esp
    mov ecx, dword ptr [ecx+0x3c]
    jmp {resume}
""".format(app=hex(APP_ACCESSOR), gfx=hex(GFX), resume=hex(RESUME))


def strip_comments(src):
    # keystone treats ';' as a statement separator, so comments must be removed first
    return "\n".join(l.split(";")[0].rstrip() for l in src.splitlines() if l.split(";")[0].strip())


def assemble():
    ks = Ks(KS_ARCH_X86, KS_MODE_32)
    cave, _ = ks.asm(strip_comments(SRC), CAVE)
    hook, _ = ks.asm("jmp %s" % hex(CAVE), HOOK)
    hook = bytes(hook) + b"\x90" * (6 - len(hook))
    return bytes(cave), hook


if __name__ == "__main__":
    from capstone import Cs, CS_ARCH_X86, CS_MODE_32
    cave, hook = assemble()
    md = Cs(CS_ARCH_X86, CS_MODE_32)
    print("hook @%08x: %s" % (HOOK, hook.hex(" ")))
    for i in md.disasm(hook, HOOK):
        print("   %08x %s %s" % (i.address, i.mnemonic, i.op_str))
    print("cave @%08x: %d bytes" % (CAVE, len(cave)))
    for i in md.disasm(cave, CAVE):
        print("   %08x  %-22s %s %s" % (i.address, i.bytes.hex(" "), i.mnemonic, i.op_str))
