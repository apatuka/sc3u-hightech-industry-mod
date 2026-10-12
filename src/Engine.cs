// Shared engine of the SC3U community patchers (identical file in every patcher repository).
// C# 5 / .NET Framework 4 - builds with the csc.exe that ships with Windows.
//
// Every change is made of "blocks" (exact original bytes -> exact new bytes) or of exact text/record edits,
// so each patcher can install and uninstall ONLY its own changes, whatever other mod is installed.
using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace SC3UMod
{
    public enum CompState { NotInstalled, Installed, Partial, Unsupported, Missing }

    public static class Util
    {
        public static readonly Encoding Latin1 = Encoding.GetEncoding(28591);

        public static string Sha256(byte[] d)
        {
            using (SHA256 sha = SHA256.Create())
                return BitConverter.ToString(sha.ComputeHash(d)).Replace("-", "").ToLowerInvariant();
        }

        public static byte[] Hex(string s)
        {
            byte[] r = new byte[s.Length / 2];
            for (int i = 0; i < r.Length; i++) r[i] = Convert.ToByte(s.Substring(i * 2, 2), 16);
            return r;
        }

        public static bool At(byte[] d, int off, byte[] b)
        {
            if (off < 0 || off + b.Length > d.Length) return false;
            for (int i = 0; i < b.Length; i++) if (d[off + i] != b[i]) return false;
            return true;
        }

        public static uint U32(byte[] d, int o) { return BitConverter.ToUInt32(d, o); }
        public static ushort U16(byte[] d, int o) { return BitConverter.ToUInt16(d, o); }
        public static void Put(byte[] d, int o, uint v) { Array.Copy(BitConverter.GetBytes(v), 0, d, o, 4); }

        public static bool Same(byte[] a, byte[] b)
        {
            if (a == null || b == null || a.Length != b.Length) return false;
            for (int i = 0; i < a.Length; i++) if (a[i] != b[i]) return false;
            return true;
        }

        public static CompState Combine(IEnumerable<CompState> states)
        {
            bool any = false, inst = false, notInst = false;
            foreach (CompState s in states)
            {
                any = true;
                if (s == CompState.Unsupported) return CompState.Unsupported;
                if (s == CompState.Missing) return CompState.Missing;
                if (s == CompState.Partial) return CompState.Partial;
                if (s == CompState.Installed) inst = true; else notInst = true;
            }
            if (!any) return CompState.Missing;
            return inst && notInst ? CompState.Partial : inst ? CompState.Installed : CompState.NotInstalled;
        }
    }

    public static class L
    {
        public static bool Es = System.Globalization.CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "es";
        public static string T(string es, string en) { return Es ? es : en; }
    }

    /// <summary>Minimal PE reader: section table, VA -> file offset, .text VirtualSize.</summary>
    public sealed class Pe
    {
        public const uint Base = 0x10000000;
        readonly byte[] d;
        readonly int secTable, nSec;
        public Pe(byte[] data)
        {
            d = data;
            int pe = (int)Util.U32(d, 0x3C);
            if (Util.U32(d, pe) != 0x4550) throw new InvalidDataException("not a PE file");
            nSec = Util.U16(d, pe + 6);
            secTable = pe + 24 + Util.U16(d, pe + 20);
        }
        public uint TimeDateStamp { get { return Util.U32(d, (int)Util.U32(d, 0x3C) + 8); } }
        int Sec(string name)
        {
            for (int i = 0; i < nSec; i++)
                if (Encoding.ASCII.GetString(d, secTable + i * 40, 8).TrimEnd('\0') == name) return secTable + i * 40;
            throw new InvalidDataException("no section " + name);
        }
        public int TextHeader { get { return Sec(".text"); } }
        public uint TextVa { get { return Base + Util.U32(d, TextHeader + 12); } }
        public uint TextVSize { get { return Util.U32(d, TextHeader + 8); } set { Util.Put(d, TextHeader + 8, value); } }
        public uint TextRawSize { get { return Util.U32(d, TextHeader + 16); } }
        public int Offset(uint va)
        {
            uint rva = va - Base;
            for (int i = 0; i < nSec; i++)
            {
                int h = secTable + i * 40;
                uint v = Util.U32(d, h + 12), raw = Util.U32(d, h + 16), ptr = Util.U32(d, h + 20);
                if (rva >= v && rva < v + raw) return (int)(ptr + rva - v);
            }
            throw new InvalidDataException("address not in file: 0x" + va.ToString("X8"));
        }
    }

    public sealed class Block
    {
        public readonly uint Va;
        public readonly byte[] Old, New;
        public Block(uint va, string oldHex, string newHex) { Va = va; Old = Util.Hex(oldHex); New = Util.Hex(newHex); }
    }

    /// <summary>A set of byte blocks for one DLL of the GOG version (identified by size + PE timestamp).
    /// New code lives in the zero padding after .text; VirtualSize grows to cover it on install. On uninstall it is
    /// set to cover exactly the code caves (of any SC3U mod) still present, or kept as is if unknown code is there.</summary>
    public sealed class DllPatch
    {
        public readonly string FileName, ShaOriginal;
        public readonly int Size;
        public readonly uint TimeStamp, OrigVSize;
        public readonly uint[] KnownCaves;   // [start, end) pairs of the code caves of all SC3U mods in this DLL
        public readonly Block[] Blocks;
        public DllPatch(string file, int size, uint stamp, uint origVSize, string sha, uint[] knownCaves, Block[] blocks)
        { FileName = file; Size = size; TimeStamp = stamp; OrigVSize = origVSize; ShaOriginal = sha; KnownCaves = knownCaves; Blocks = blocks; }

        public CompState State(byte[] d)
        {
            if (d == null) return CompState.Missing;
            if (d.Length != Size) return CompState.Unsupported;
            Pe pe;
            try { pe = new Pe(d); } catch { return CompState.Unsupported; }
            if (pe.TimeDateStamp != TimeStamp) return CompState.Unsupported;
            int done = 0, todo = 0;
            foreach (Block b in Blocks)
            {
                int o = pe.Offset(b.Va);
                if (Util.At(d, o, b.New)) done++;
                else if (Util.At(d, o, b.Old)) todo++;
                else return CompState.Unsupported;
            }
            return todo == 0 ? CompState.Installed : done == 0 ? CompState.NotInstalled : CompState.Partial;
        }

        public byte[] Transform(byte[] src, bool install)
        {
            CompState s = State(src);
            if (s == CompState.Unsupported || s == CompState.Missing) throw new NotSupportedException(FileName);
            byte[] d = (byte[])src.Clone();
            Pe pe = new Pe(d);
            uint caveStart = pe.TextVa + OrigVSize - 0x10, caveLimit = pe.TextVa + pe.TextRawSize, caveEnd = 0;
            foreach (Block b in Blocks)
            {
                int o = pe.Offset(b.Va);
                byte[] to = install ? b.New : b.Old;
                if (!Util.At(d, o, to)) Array.Copy(to, 0, d, o, to.Length);
                if (b.Va >= caveStart && b.Va < caveLimit) caveEnd = Math.Max(caveEnd, b.Va + (uint)b.New.Length);
            }
            if (install)
            {
                if (caveEnd - pe.TextVa > pe.TextVSize) pe.TextVSize = caveEnd - pe.TextVa;
            }
            else
            {
                uint need = OrigVSize;
                bool unknown = false;
                int textOff = pe.Offset(pe.TextVa);
                for (uint va = pe.TextVa + OrigVSize; va < caveLimit; va++)
                {
                    if (d[textOff + (int)(va - pe.TextVa)] == 0) continue;
                    bool known = false;
                    for (int k = 0; k + 1 < KnownCaves.Length; k += 2)
                        if (va >= KnownCaves[k] && va < KnownCaves[k + 1]) { known = true; need = Math.Max(need, KnownCaves[k + 1] - pe.TextVa); }
                    if (!known) unknown = true;
                }
                if (!unknown) pe.TextVSize = need;
            }
            if (State(d) != (install ? CompState.Installed : CompState.NotInstalled))
                throw new InvalidDataException("self-check failed for " + FileName);
            return d;
        }
    }

    /// <summary>All file edits of one install/uninstall, applied together (with rollback on failure).</summary>
    public sealed class FileSet
    {
        readonly Dictionary<string, byte[]> orig = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
        readonly Dictionary<string, byte[]> cur = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
        readonly List<string> order = new List<string>();

        public byte[] Get(string path)
        {
            byte[] d;
            if (cur.TryGetValue(path, out d)) return d;
            if (!File.Exists(path)) return null;
            d = File.ReadAllBytes(path);
            orig[path] = d; cur[path] = d; order.Add(path);
            return d;
        }

        public void Set(string path, byte[] data) { Get(path); cur[path] = data; }

        public List<string> Changed()
        {
            List<string> r = new List<string>();
            foreach (string p in order) if (!Util.Same(orig[p], cur[p])) r.Add(p);
            return r;
        }

        /// <summary>Writes every changed file. A copy of each file is kept once in backupDir (never overwritten).
        /// DLLs whose SHA-256 matches the original also get the classic "&lt;dll&gt;.orig" copy next to them.</summary>
        public int Commit(string gameDir, string backupDir, Dictionary<string, string> dllShaOriginal)
        {
            List<string> changed = Changed();
            foreach (string p in changed)   // fail before writing anything if a file is locked (game running) or read-only
                using (new FileStream(p, FileMode.Open, FileAccess.ReadWrite, FileShare.None)) { }
            foreach (string p in changed)
            {
                string rel = p.StartsWith(gameDir, StringComparison.OrdinalIgnoreCase) ? p.Substring(gameDir.Length).TrimStart('\\', '/') : Path.GetFileName(p);
                string bak = Path.Combine(backupDir, rel);
                if (!File.Exists(bak))
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(bak));
                    File.WriteAllBytes(bak, orig[p]);
                }
                string sha;
                if (dllShaOriginal != null && dllShaOriginal.TryGetValue(Path.GetFileName(p), out sha)
                    && !File.Exists(p + ".orig") && Util.Sha256(orig[p]) == sha)
                    File.WriteAllBytes(p + ".orig", orig[p]);
            }
            List<string> written = new List<string>();
            try
            {
                foreach (string p in changed)
                {
                    string tmp = p + ".tmp";
                    File.WriteAllBytes(tmp, cur[p]);
                    File.Copy(tmp, p, true);
                    File.Delete(tmp);
                    written.Add(p);
                }
            }
            catch
            {
                foreach (string p in written) { try { File.WriteAllBytes(p, orig[p]); } catch { } }
                throw;
            }
            return changed.Count;
        }
    }

    /// <summary>One part of a mod (shown as one status line).</summary>
    public interface IComponent
    {
        string Name { get; }
        CompState GetState(FileSet fs, string apps);
        /// <summary>Edits the files in fs (in memory). Throws NotSupportedException on unexpected content.</summary>
        void Plan(FileSet fs, string apps, bool install);
    }

    public sealed class DllComponent : IComponent
    {
        readonly DllPatch patch; readonly string nameEs, nameEn;
        public DllComponent(DllPatch p, string es, string en) { patch = p; nameEs = es; nameEn = en; }
        public DllPatch Patch { get { return patch; } }
        public string Name { get { return L.T(nameEs, nameEn); } }
        public CompState GetState(FileSet fs, string apps) { return patch.State(fs.Get(Path.Combine(apps, patch.FileName))); }
        public void Plan(FileSet fs, string apps, bool install)
        {
            string p = Path.Combine(apps, patch.FileName);
            byte[] d = fs.Get(p);
            CompState s = patch.State(d);
            if (s == CompState.Missing || s == CompState.Unsupported) throw new NotSupportedException(patch.FileName);
            if (s != (install ? CompState.Installed : CompState.NotInstalled)) fs.Set(p, patch.Transform(d, install));
        }
    }
}
