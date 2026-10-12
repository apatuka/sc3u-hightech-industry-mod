"""Assemble the SIMGEOM 'High-Tech industries in city: N' Query line.

Hook: generic building query (cClass_1002a578 vtable slot 0x124 @ 0x10024603) at 0x100249B3, right after
the 'Make historical' checkbox is added. The cave counts every building whose occupant type is
8700/8900/9100 (clean = high-tech industry) by scanning the whole city (each building counted at its
origin cell), formats the text and adds it with builder->AddLine (slot 0x34). Position independent.
"""
import struct
from keystone import Ks, KS_ARCH_X86, KS_MODE_32

HOOK = 0x100249B3          # push ebx; push [ebp+8]; mov ecx,[ebp-0x18]   (7 bytes, no relocations)
RESUME = 0x100249BA        # call FUN_10014484 (original continues here)
APP = 0x1001BDB4           # SIMGEOM copy of the shared application accessor
STR_CTOR = 0x10001E40      # cRZString default constructor (ecx = this)
STR_TIDY = 0x1000161D      # frees the cRZString text buffer (ecx = this + 4)
CAVE = 0x10028460
TEXT = b"High-Tech industries in city: \x00"

SRC = """
    pushad
    push ebp
    mov ebp, esp
    sub esp, 0xc0
    mov dword ptr [ebp-0x3c], ebx
    xor eax, eax
    mov dword ptr [ebp-0x20], eax
    call {app}
    test eax, eax
    jz finish
    mov ecx, eax
    mov edx, dword ptr [ecx]
    call dword ptr [edx+0x18]
    test eax, eax
    jz finish
    mov esi, eax
    mov ecx, esi
    mov edx, dword ptr [ecx]
    call dword ptr [edx+0xcc]
    mov dword ptr [ebp-0x14], eax
    mov ecx, esi
    mov edx, dword ptr [ecx]
    call dword ptr [edx+0xd0]
    mov dword ptr [ebp-0x18], eax
    mov ecx, esi
    mov edx, dword ptr [ecx]
    call dword ptr [edx+0x11c]
    test eax, eax
    jz finish
    mov dword ptr [ebp-0x1c], eax
    mov dword ptr [ebp-0x28], 0
zloop:
    mov eax, dword ptr [ebp-0x28]
    cmp eax, dword ptr [ebp-0x18]
    jge show
    mov dword ptr [ebp-0x24], 0
xloop:
    mov eax, dword ptr [ebp-0x24]
    cmp eax, dword ptr [ebp-0x14]
    jge znext
    mov dword ptr [ebp-0x2c], 0
    lea edx, [ebp-0x2c]
    push edx
    push dword ptr [ebp-0x28]
    push dword ptr [ebp-0x24]
    mov ecx, dword ptr [ebp-0x1c]
    mov edx, dword ptr [ecx]
    call dword ptr [edx+0x7c]
    test al, al
    jz xnext
    mov edi, dword ptr [ebp-0x2c]
    test edi, edi
    jz xnext
    lea edx, [ebp-0x34]
    push edx
    lea edx, [ebp-0x30]
    push edx
    mov ecx, edi
    mov edx, dword ptr [ecx]
    call dword ptr [edx+0xbc]
    mov eax, dword ptr [ebp-0x30]
    cmp eax, dword ptr [ebp-0x24]
    jne release
    mov eax, dword ptr [ebp-0x34]
    cmp eax, dword ptr [ebp-0x28]
    jne release
    mov ecx, edi
    mov edx, dword ptr [ecx]
    call dword ptr [edx+0x8c]
    test eax, eax
    jz release
    mov ecx, eax
    mov edx, dword ptr [ecx]
    call dword ptr [edx+0x10]
    cmp eax, 8700
    je counted
    cmp eax, 8900
    je counted
    cmp eax, 9100
    jne release
counted:
    inc dword ptr [ebp-0x20]
release:
    mov ecx, edi
    mov edx, dword ptr [ecx]
    call dword ptr [edx+8]
xnext:
    inc dword ptr [ebp-0x24]
    jmp xloop
znext:
    inc dword ptr [ebp-0x28]
    jmp zloop
show:
    call getpc
getpc:
    pop esi
    add esi, {litoff}
    lea edi, [ebp-0xa0]
copy:
    mov al, byte ptr [esi]
    test al, al
    jz digits
    mov byte ptr [edi], al
    inc esi
    inc edi
    jmp copy
digits:
    mov eax, dword ptr [ebp-0x20]
    lea ecx, [ebp-0xa1]
    mov ebx, 10
    xor esi, esi
dloop:
    xor edx, edx
    div ebx
    add dl, 0x30
    mov byte ptr [ecx], dl
    dec ecx
    inc esi
    test eax, eax
    jnz dloop
    inc ecx
cp2:
    mov al, byte ptr [ecx]
    mov byte ptr [edi], al
    inc ecx
    inc edi
    dec esi
    jnz cp2
    mov byte ptr [edi], 0
    lea eax, [ebp-0xa0]
    sub edi, eax
    lea ecx, [ebp-0x60]
    call {ctor}
    push edi
    lea eax, [ebp-0xa0]
    push eax
    lea ecx, [ebp-0x60]
    mov edx, dword ptr [ecx]
    call dword ptr [edx+0xc]
    lea eax, [ebp-0x60]
    push eax
    mov ecx, dword ptr [ebp-0x3c]
    mov edx, dword ptr [ecx]
    call dword ptr [edx+0x34]
    lea ecx, [ebp-0x5c]
    call {tidy}
finish:
    mov esp, ebp
    pop ebp
    popad
    push ebx
    push dword ptr [ebp+8]
    mov ecx, dword ptr [ebp-0x18]
    jmp {resume}
"""


def assemble():
    ks = Ks(KS_ARCH_X86, KS_MODE_32)
    fmt = dict(app=hex(APP), ctor=hex(STR_CTOR), tidy=hex(STR_TIDY), resume=hex(RESUME))
    # pass 1: find where 'getpc' lands, so the literal offset can be computed exactly
    code1, _ = ks.asm(SRC.format(litoff="0x7fffffff", **fmt), CAVE)
    code1 = bytes(code1)
    # locate the pop esi (5e) that follows 'call getpc' (e8 00 00 00 00)
    i = code1.find(bytes.fromhex("e8000000005e"))
    assert i > 0
    getpc_va = CAVE + i + 5
    lit_va = CAVE + len(code1)
    code, _ = ks.asm(SRC.format(litoff=hex(lit_va - getpc_va), **fmt), CAVE)
    code = bytes(code)
    assert len(code) == len(code1), "length changed between passes"
    cave = code + TEXT
    hook, _ = ks.asm("jmp %s" % hex(CAVE), HOOK)
    hook = bytes(hook) + b"\x90" * (7 - len(hook))
    return cave, hook


if __name__ == "__main__":
    from capstone import Cs, CS_ARCH_X86, CS_MODE_32
    cave, hook = assemble()
    md = Cs(CS_ARCH_X86, CS_MODE_32)
    print("hook @%08x: %s" % (HOOK, hook.hex(" ")))
    code = cave[:-len(TEXT)]
    print("cave @%08x: %d bytes code + %d bytes text" % (CAVE, len(code), len(TEXT)))
    for i in md.disasm(code, CAVE):
        print("   %08x  %-24s %s %s" % (i.address, i.bytes.hex(" "), i.mnemonic, i.op_str))
    print("literal @%08x: %r" % (CAVE + len(code), cave[len(code):]))
