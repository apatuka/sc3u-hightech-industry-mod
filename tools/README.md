# tools

Python sources of the byte patches (shared by the SC3U patchers). They are only needed to audit or regenerate
`src/PatchData.cs`; building the patcher does not need them.

- `asm_hightech_map.py`, `asm_hightech_zone.py`, `asm_hightech_query.py`: assembly of the new code (Keystone).
- `mod_rci_dll.py`, `mod_simgeom_dll.py`: block definitions per DLL (also usable directly on a DLL, see `--check`).
- `dllpatch.py`: block patcher used by the two scripts above.
- `gen_patchdata.py rci|ht <out.cs>`: writes `PatchData.cs` for SC3U Full RCI Patch (`rci`) or SC3U High-Tech Industry (`ht`).

Requirements: `pip install keystone-engine capstone pefile`.
