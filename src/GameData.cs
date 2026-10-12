// Data-file parts of the SC3U High-Tech Industry mod: SYS.PAK + INI lines, text records, building names.
// Every edit has an exact inverse, so uninstalling restores the original bytes and leaves other mods' edits alone.
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace SC3UMod
{
    /// <summary>Apps\Sys\SYS.PAK: u32 count, {u32 nameLen, name, u32 offset}*, then per file u32 lineCount,
    /// {u32 len, bytes}*; a few trailing bytes after the last file are preserved.</summary>
    public sealed class SysPak
    {
        public readonly List<string> Names = new List<string>();
        public readonly List<List<string>> Lines = new List<List<string>>();
        byte[] trailer = new byte[0];

        public static SysPak Parse(byte[] d)
        {
            SysPak s = new SysPak();
            int pos = 4, count = (int)Util.U32(d, 0);
            List<int> offs = new List<int>();
            for (int i = 0; i < count; i++)
            {
                int n = (int)Util.U32(d, pos); pos += 4;
                s.Names.Add(Util.Latin1.GetString(d, pos, n)); pos += n;
                offs.Add((int)Util.U32(d, pos)); pos += 4;
            }
            for (int i = 0; i < count; i++)
            {
                int p = offs[i], nl = (int)Util.U32(d, p); p += 4;
                List<string> lines = new List<string>(nl);
                for (int k = 0; k < nl; k++)
                {
                    int len = (int)Util.U32(d, p); p += 4;
                    lines.Add(Util.Latin1.GetString(d, p, len)); p += len;
                }
                s.Lines.Add(lines);
                if (i + 1 < count) { if (p != offs[i + 1]) throw new NotSupportedException("SYS.PAK layout"); }
                else
                {
                    if (d.Length - p > 16) throw new NotSupportedException("SYS.PAK trailer");
                    s.trailer = new byte[d.Length - p];
                    Array.Copy(d, p, s.trailer, 0, s.trailer.Length);
                }
            }
            return s;
        }

        public byte[] Build()
        {
            MemoryStream blobs = new MemoryStream();
            List<int> offs = new List<int>();
            int hdr = 4;
            foreach (string n in Names) hdr += 8 + Util.Latin1.GetByteCount(n);
            foreach (List<string> lines in Lines)
            {
                offs.Add(hdr + (int)blobs.Length);
                W(blobs, (uint)lines.Count);
                foreach (string l in lines) { byte[] b = Util.Latin1.GetBytes(l); W(blobs, (uint)b.Length); blobs.Write(b, 0, b.Length); }
            }
            MemoryStream o = new MemoryStream();
            W(o, (uint)Names.Count);
            for (int i = 0; i < Names.Count; i++)
            {
                byte[] nb = Util.Latin1.GetBytes(Names[i]);
                W(o, (uint)nb.Length); o.Write(nb, 0, nb.Length); W(o, (uint)offs[i]);
            }
            blobs.WriteTo(o);
            o.Write(trailer, 0, trailer.Length);
            return o.ToArray();
        }

        static void W(Stream s, uint v) { s.Write(BitConverter.GetBytes(v), 0, 4); }

        public int Find(string name)
        {
            for (int i = 0; i < Names.Count; i++) if (Names[i].Equals(name, StringComparison.OrdinalIgnoreCase)) return i;
            return -1;
        }
    }

    /// <summary>Line edits of SC3ZoneLayer.ini (zone 12 developer) and MenuItem.INI (4th industrial button).</summary>
    public static class IniEdits
    {
        public const string Zone12 = "12=0xe1a53b30,8000:8700,C,1,255,1;8900,C,2,255,1;9100,C,3,255,1";
        public static readonly string MenuItem = "0x10003304=1,0x225872FE,0x00004801,0x225872FE,0x00004801,COMM,0xC8545A31,0,0,0,END  ; Ind High-Tech";
        public const string MenuBtn = "0x00003304=0x10003304,0x00000000,END ;High-Tech";
        const string Sub3 = "3,0x00003301,0x00003302,0x00003303,END", Sub4 = "4,0x00003301,0x00003302,0x00003303,0x00003304,END";

        static List<string> Rules11(List<string> lines)
        {
            List<string> r = new List<string>();
            foreach (string l in lines) if (l.StartsWith("11,0x")) r.Add(l);
            return r;
        }

        public static CompState ZoneState(List<string> lines)
        {
            bool ours = false;
            foreach (string l in lines)
                if (l.StartsWith("12=")) { if (l == Zone12) ours = true; else return CompState.Unsupported; }
            bool anchor = false;
            foreach (string l in lines) if (l.StartsWith("11=0xe1a53b30")) anchor = true;
            if (!anchor || Rules11(lines).Count == 0) return CompState.Unsupported;
            return ours ? CompState.Installed : CompState.NotInstalled;
        }

        public static List<string> Zone(List<string> lines, bool install)
        {
            List<string> rules = Rules11(lines), outL = new List<string>();
            if (install)
            {
                if (ZoneState(lines) == CompState.Installed) return lines;
                foreach (string l in lines)
                {
                    outL.Add(l);
                    if (l.StartsWith("11=0xe1a53b30")) outL.Add(Zone12);
                    if (l == rules[rules.Count - 1]) foreach (string r in rules) outL.Add("12" + r.Substring(2));
                }
                if (outL.Count != lines.Count + 1 + rules.Count) throw new NotSupportedException("SC3ZoneLayer.ini");
            }
            else
            {
                HashSet<string> drop = new HashSet<string>();
                drop.Add(Zone12);
                foreach (string r in rules) drop.Add("12" + r.Substring(2));
                foreach (string l in lines) if (!drop.Contains(l)) outL.Add(l);
            }
            return outL;
        }

        public static CompState MenuState(List<string> lines)
        {
            bool item = false, btn = false, sub4 = false, sub3 = false;
            foreach (string l in lines)
            {
                if (l.StartsWith("0x10003304=")) { if (l == MenuItem) item = true; else return CompState.Unsupported; }
                if (l.StartsWith("0x00003304=")) { if (l == MenuBtn) btn = true; else return CompState.Unsupported; }
                if (l.StartsWith("0xD1003300=0x225872FE")) { if (l.Contains(Sub4)) sub4 = true; else if (l.Contains(Sub3)) sub3 = true; }
            }
            if (!sub3 && !sub4) return CompState.Unsupported;
            if (item && btn && sub4) return CompState.Installed;
            if (!item && !btn && sub3) return CompState.NotInstalled;
            return CompState.Partial;
        }

        public static List<string> Menu(List<string> lines, bool install)
        {
            List<string> outL = new List<string>();
            foreach (string l0 in lines)
            {
                string l = l0;
                if (install)
                {
                    if (l == MenuItem || l == MenuBtn) continue;    // re-added below at the right place
                    if (l.StartsWith("0xD1003300=0x225872FE")) l = l.Replace(Sub3, Sub4);
                    outL.Add(l);
                    if (l.StartsWith("0x10003303=1,0x225872FE")) outL.Add(MenuItem);
                    if (l.StartsWith("0x00003303=0x10003303")) outL.Add(MenuBtn);
                }
                else
                {
                    if (l == MenuItem || l == MenuBtn) continue;
                    if (l.StartsWith("0xD1003300=0x225872FE")) l = l.Replace(Sub4, Sub3);
                    outL.Add(l);
                }
            }
            if (install && MenuState(outL) != CompState.Installed) throw new NotSupportedException("MenuItem.INI");
            return outL;
        }

        /// <summary>Splits a loose INI file into lines (keeps its line ending style).</summary>
        public static List<string> Split(byte[] raw, out string nl)
        {
            string s = Util.Latin1.GetString(raw);
            nl = s.Contains("\r\n") ? "\r\n" : "\n";
            return new List<string>(s.Split(new string[] { nl }, StringSplitOptions.None));
        }

        public static byte[] Join(List<string> lines, string nl) { return Util.Latin1.GetBytes(string.Join(nl, lines.ToArray())); }
    }

    /// <summary>IXF resource files: u32 magic, 20-byte index entries {a, b, c, offset, size} up to an all-zero entry.</summary>
    public static class Ixf
    {
        public const uint Magic = 0x80C381D7;
        public sealed class Entry { public int Pos; public uint A, B, C; public int Off, Size; }

        public static List<Entry> Index(byte[] d)
        {
            if (d.Length < 4 || Util.U32(d, 0) != Magic) throw new NotSupportedException("not an IXF file");
            List<Entry> r = new List<Entry>();
            for (int p = 4; p + 20 <= d.Length; p += 20)
            {
                Entry e = new Entry();
                e.Pos = p; e.A = Util.U32(d, p); e.B = Util.U32(d, p + 4); e.C = Util.U32(d, p + 8);
                e.Off = (int)Util.U32(d, p + 12); e.Size = (int)Util.U32(d, p + 16);
                if (e.A == 0 && e.B == 0 && e.C == 0 && e.Off == 0 && e.Size == 0) break;
                r.Add(e);
            }
            return r;
        }

        public static Entry Find(List<Entry> idx, uint a, uint b, uint c)
        {
            foreach (Entry e in idx) if (e.A == a && e.B == b && e.C == c) return e;
            return null;
        }

        /// <summary>Appends a string record (u32 length + latin-1 text) with a new index entry.</summary>
        public static byte[] AddString(byte[] d, uint a, uint b, uint c, string text)
        {
            List<Entry> idx = Index(d);
            if (Find(idx, a, b, c) != null) return d;
            int first = int.MaxValue;
            foreach (Entry e in idx) first = Math.Min(first, e.Off);
            int pos = idx.Count == 0 ? 4 : idx[idx.Count - 1].Pos + 20;
            if (pos + 40 > first) throw new NotSupportedException("IXF index full");
            byte[] t = Util.Latin1.GetBytes(text);
            byte[] o = new byte[d.Length + 4 + t.Length];
            Array.Copy(d, o, d.Length);
            Util.Put(o, d.Length, (uint)t.Length);
            Array.Copy(t, 0, o, d.Length + 4, t.Length);
            Util.Put(o, pos, a); Util.Put(o, pos + 4, b); Util.Put(o, pos + 8, c);
            Util.Put(o, pos + 12, (uint)d.Length); Util.Put(o, pos + 16, (uint)(4 + t.Length));
            return o;
        }

        public static byte[] RemoveRecord(byte[] d, uint a, uint b, uint c)
        {
            List<Entry> idx = Index(d);
            Entry e = Find(idx, a, b, c);
            if (e == null) return d;
            int last = idx[idx.Count - 1].Pos;
            bool atEnd = e.Off + e.Size == d.Length;
            byte[] o = new byte[atEnd ? e.Off : d.Length];
            Array.Copy(d, o, o.Length);
            Array.Copy(d, e.Pos + 20, o, e.Pos, last - e.Pos);       // shift later entries up
            for (int i = 0; i < 20; i++) o[last + i] = 0;
            return o;
        }

        /// <summary>Rewrites the records in file order with some replaced (offsets shift); index rewritten in place.</summary>
        public static byte[] Rebuild(byte[] d, Dictionary<int, byte[]> changes)
        {
            List<Entry> idx = Index(d);
            List<int> order = new List<int>();
            for (int i = 0; i < idx.Count; i++) order.Add(i);
            order.Sort(delegate(int x, int y) { int c = idx[x].Off.CompareTo(idx[y].Off); return c != 0 ? c : x.CompareTo(y); });
            MemoryStream o = new MemoryStream();
            o.Write(d, 0, idx[order[0]].Off);
            int[] noff = new int[idx.Count], nsize = new int[idx.Count];
            foreach (int i in order)
            {
                byte[] rec;
                if (!changes.TryGetValue(i, out rec)) { rec = new byte[idx[i].Size]; Array.Copy(d, idx[i].Off, rec, 0, rec.Length); }
                noff[i] = (int)o.Length; nsize[i] = rec.Length;
                o.Write(rec, 0, rec.Length);
            }
            byte[] r = o.ToArray();
            for (int i = 0; i < idx.Count; i++) { Util.Put(r, idx[i].Pos + 12, (uint)noff[i]); Util.Put(r, idx[i].Pos + 16, (uint)nsize[i]); }
            return r;
        }

        public static bool RoundTrips(byte[] d)
        {
            try { return Util.Same(Rebuild(d, new Dictionary<int, byte[]>()), d); } catch { return false; }
        }

        public static byte[] Record(byte[] d, Entry e) { byte[] r = new byte[e.Size]; Array.Copy(d, e.Off, r, 0, e.Size); return r; }
    }

    /// <summary>TKB1 occupant property sets: "TKB1", then {u32 id, u16 type, u16 count, values}*.</summary>
    public static class Tkb1
    {
        public sealed class Prop { public uint Id; public ushort Type, Count; public int Start, End, ValStart; }
        static int Size(int type)
        {
            switch (type) { case 1: return 1; case 2: return 2; case 3: case 4: case 5: return 4; case 6: return 8; case 8: return 12; }
            throw new NotSupportedException("TKB1 type " + type);
        }

        public static List<Prop> Parse(byte[] rec)
        {
            if (rec.Length < 4 || Encoding.ASCII.GetString(rec, 0, 4) != "TKB1") return null;
            List<Prop> r = new List<Prop>();
            int p = 4;
            while (p + 8 <= rec.Length)
            {
                Prop pr = new Prop();
                pr.Start = p; pr.Id = Util.U32(rec, p); pr.Type = Util.U16(rec, p + 4); pr.Count = Util.U16(rec, p + 6);
                p += 8; pr.ValStart = p;
                if (pr.Type == 7) for (int k = 0; k < pr.Count; k++) p += 4 + (int)Util.U32(rec, p);
                else p += Size(pr.Type) * pr.Count;
                pr.End = p;
                r.Add(pr);
            }
            if (p != rec.Length) throw new NotSupportedException("TKB1 trailing bytes");
            return r;
        }

        public static Prop Get(List<Prop> ps, uint id) { foreach (Prop p in ps) if (p.Id == id) return p; return null; }

        public static long Num(byte[] rec, Prop p)
        {
            switch (Size(p.Type)) { case 1: return rec[p.ValStart]; case 2: return Util.U16(rec, p.ValStart); default: return Util.U32(rec, p.ValStart); }
        }

        public static string Str(byte[] rec, Prop p) { return Util.Latin1.GetString(rec, p.ValStart + 4, (int)Util.U32(rec, p.ValStart)); }

        /// <summary>Same record with the single string of property p replaced.</summary>
        public static byte[] ReplaceString(byte[] rec, Prop p, string s)
        {
            if (p.Type != 7 || p.Count != 1) throw new NotSupportedException("name property");
            byte[] t = Util.Latin1.GetBytes(s);
            MemoryStream o = new MemoryStream();
            o.Write(rec, 0, p.ValStart);
            o.Write(BitConverter.GetBytes((uint)t.Length), 0, 4);
            o.Write(t, 0, t.Length);
            o.Write(rec, p.End, rec.Length - p.End);
            return o.ToArray();
        }
    }

    // ---------------------------------------------------------------- components

    static class Paths
    {
        public static string Find(string dir, string name)
        {
            if (!Directory.Exists(dir)) return null;
            foreach (string f in Directory.GetFiles(dir)) if (Path.GetFileName(f).Equals(name, StringComparison.OrdinalIgnoreCase)) return f;
            return null;
        }
        public static List<string> LangDirs(string apps)
        {
            List<string> r = new List<string>();
            string t = Path.Combine(apps, @"Res\Text");
            if (Directory.Exists(t)) foreach (string d in Directory.GetDirectories(t)) r.Add(d);
            r.Sort(StringComparer.OrdinalIgnoreCase);
            return r;
        }
    }

    /// <summary>SYS.PAK (what the game reads) + the loose copies of SC3ZoneLayer.ini / MenuItem.INI.</summary>
    public sealed class ZoneDataComponent : IComponent
    {
        public string Name { get { return L.T("SYS.PAK / INI: zona 12 «High-Tech Industrial» y su botón en el menú", "SYS.PAK / INI: zone 12 \"High-Tech Industrial\" and its menu button"); } }

        static IEnumerable<CompState> States(FileSet fs, string apps)
        {
            string sys = Path.Combine(apps, "Sys");
            byte[] pak = fs.Get(Path.Combine(sys, "SYS.PAK"));
            if (pak == null) { yield return CompState.Missing; yield break; }
            SysPak s = SysPak.Parse(pak);
            if (!Util.Same(s.Build(), pak)) { yield return CompState.Unsupported; yield break; }
            int z = s.Find("SC3ZoneLayer.ini"), m = s.Find("MenuItem.INI");
            if (z < 0 || m < 0) { yield return CompState.Unsupported; yield break; }
            yield return IniEdits.ZoneState(s.Lines[z]);
            yield return IniEdits.MenuState(s.Lines[m]);
            string nl;
            string zp = Paths.Find(sys, "SC3ZoneLayer.ini"), mp = Paths.Find(sys, "MenuItem.INI");
            if (zp != null) yield return IniEdits.ZoneState(IniEdits.Split(fs.Get(zp), out nl));
            if (mp != null) yield return IniEdits.MenuState(IniEdits.Split(fs.Get(mp), out nl));
        }

        public CompState GetState(FileSet fs, string apps) { return Util.Combine(States(fs, apps)); }

        public void Plan(FileSet fs, string apps, bool install)
        {
            CompState st = GetState(fs, apps);
            if (st == CompState.Unsupported || st == CompState.Missing) throw new NotSupportedException("SYS.PAK");
            string sys = Path.Combine(apps, "Sys"), pakPath = Path.Combine(sys, "SYS.PAK");
            SysPak s = SysPak.Parse(fs.Get(pakPath));
            int z = s.Find("SC3ZoneLayer.ini"), m = s.Find("MenuItem.INI");
            s.Lines[z] = IniEdits.Zone(s.Lines[z], install);
            s.Lines[m] = IniEdits.Menu(s.Lines[m], install);
            fs.Set(pakPath, s.Build());
            string nl;
            string zp = Paths.Find(sys, "SC3ZoneLayer.ini"), mp = Paths.Find(sys, "MenuItem.INI");
            if (zp != null) fs.Set(zp, IniEdits.Join(IniEdits.Zone(IniEdits.Split(fs.Get(zp), out nl), install), nl));
            if (mp != null) fs.Set(mp, IniEdits.Join(IniEdits.Menu(IniEdits.Split(fs.Get(mp), out nl), install), nl));
        }
    }

    /// <summary>Menu label (SC3StringsGUI.IXF) and Query "Zone Type:" label (SC3StringsQuery.IXF) for zone 12.</summary>
    public sealed class ZoneStringsComponent : IComponent
    {
        public const uint StrType = 0x2026960B, GuiGroup = 0x225872FE, QueryGroup = 0x82E0074C, Instance = 0x4801;
        static readonly Dictionary<string, string> Names = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "ENGLISH", "High-Tech Industrial" }, { "English-UK", "High-Tech Industrial" },
            { "FRENCH", "Industrielle high-tech" }, { "GERMAN", "Hightech-Industriegebiet" },
            { "ITALIAN", "Industriale High-Tech" }, { "DUTCH", "Hightech-industriegebied" },
            { "SWEDISH", "Högteknologiskt industriområde" },
        };

        public string Name { get { return L.T("Textos del menú y del Query para la zona High-Tech (todos los idiomas)", "Menu and Query texts for the High-Tech zone (all languages)"); } }

        static IEnumerable<KeyValuePair<string, uint>> Targets(string apps)
        {
            foreach (string d in Paths.LangDirs(apps))
            {
                string g = Paths.Find(d, "SC3StringsGUI.IXF"), q = Paths.Find(d, "SC3StringsQuery.IXF");
                if (g != null) yield return new KeyValuePair<string, uint>(g, GuiGroup);
                if (q != null) yield return new KeyValuePair<string, uint>(q, QueryGroup);
            }
        }

        public CompState GetState(FileSet fs, string apps)
        {
            List<CompState> l = new List<CompState>();
            foreach (KeyValuePair<string, uint> t in Targets(apps))
                l.Add(Ixf.Find(Ixf.Index(fs.Get(t.Key)), t.Value, Instance, StrType) != null ? CompState.Installed : CompState.NotInstalled);
            return Util.Combine(l);
        }

        public void Plan(FileSet fs, string apps, bool install)
        {
            foreach (KeyValuePair<string, uint> t in Targets(apps))
            {
                string lang = Path.GetFileName(Path.GetDirectoryName(t.Key)), text;
                if (!Names.TryGetValue(lang, out text)) text = Names["ENGLISH"];
                byte[] d = fs.Get(t.Key);
                fs.Set(t.Key, install ? Ixf.AddString(d, t.Value, Instance, StrType, text) : Ixf.RemoveRecord(d, t.Value, Instance, StrType));
            }
        }
    }

    /// <summary>" (High-Tech)" after the names of clean-industry buildings (types 8700/8900/9100): internal name
    /// (TKB1 property 0x64 in Res\Occupant\*.IXF) and localized names (Res\Text\&lt;lang&gt;\*StringsBuildings*.IXF).</summary>
    public sealed class BuildingNamesComponent : IComponent
    {
        public const string Tag = " (High-Tech)";
        const uint NameGroup = 0xA1096A4F, NameType = 0x2026960B;
        static readonly long[] Types = { 8700, 8900, 9100 };

        public string Name { get { return L.T("Nombres «(High-Tech)» de los edificios de industria limpia", "\"(High-Tech)\" names of the clean-industry buildings"); } }

        static List<string> OccupantFiles(string apps)
        {
            List<string> r = new List<string>();
            string dir = Path.Combine(apps, @"Res\Occupant");
            if (Directory.Exists(dir))
                foreach (string f in Directory.GetFiles(dir)) if (f.EndsWith(".IXF", StringComparison.OrdinalIgnoreCase)) r.Add(f);
            r.Sort(StringComparer.OrdinalIgnoreCase);
            return r;
        }

        static List<string> TextFiles(string apps)
        {
            List<string> r = new List<string>();
            foreach (string d in Paths.LangDirs(apps))
            {
                List<string> fl = new List<string>(Directory.GetFiles(d));
                fl.Sort(StringComparer.OrdinalIgnoreCase);
                foreach (string f in fl)
                {
                    string n = Path.GetFileName(f);
                    if (n.IndexOf("stringsbuildings", StringComparison.OrdinalIgnoreCase) >= 0 && n.EndsWith(".IXF", StringComparison.OrdinalIgnoreCase)) r.Add(f);
                }
            }
            return r;
        }

        static bool IsClean(byte[] rec, List<Tkb1.Prop> ps)
        {
            Tkb1.Prop t = Tkb1.Get(ps, 0x65);
            if (t == null || t.Count == 0 || t.Type == 7) return false;
            return Array.IndexOf(Types, Tkb1.Num(rec, t)) >= 0;
        }

        /// <summary>Visits every target name. edit(name) returns the new name or null to keep it.</summary>
        static void Walk(FileSet fs, string apps, Func<string, string> edit, bool write)
        {
            HashSet<uint> instances = new HashSet<uint>();
            List<string> occ = OccupantFiles(apps);
            if (occ.Count == 0) throw new FileNotFoundException("Res\\Occupant");
            foreach (string f in occ)
            {
                byte[] d = fs.Get(f);
                if (!Ixf.RoundTrips(d)) throw new NotSupportedException(Path.GetFileName(f));
                List<Ixf.Entry> idx = Ixf.Index(d);
                Dictionary<int, byte[]> changes = new Dictionary<int, byte[]>();
                for (int i = 0; i < idx.Count; i++)
                {
                    byte[] rec = Ixf.Record(d, idx[i]);
                    List<Tkb1.Prop> ps = Tkb1.Parse(rec);
                    if (ps == null || !IsClean(rec, ps)) continue;
                    Tkb1.Prop key = Tkb1.Get(ps, 0x6D);
                    if (key != null && key.Type == 8 && key.Count > 0 && Util.U32(rec, key.ValStart) == NameType && Util.U32(rec, key.ValStart + 4) == NameGroup)
                        instances.Add(Util.U32(rec, key.ValStart + 8));
                    Tkb1.Prop name = Tkb1.Get(ps, 0x64);
                    if (name == null || name.Type != 7) continue;
                    string n = edit(Tkb1.Str(rec, name));
                    if (n != null) changes[i] = Tkb1.ReplaceString(rec, name, n);
                }
                if (write && changes.Count > 0) fs.Set(f, Ixf.Rebuild(d, changes));
            }
            foreach (string f in TextFiles(apps))
            {
                byte[] d = fs.Get(f);
                if (!Ixf.RoundTrips(d)) continue;               // layout not understood (e.g. MBE file): left alone
                List<Ixf.Entry> idx = Ixf.Index(d);
                Dictionary<int, byte[]> changes = new Dictionary<int, byte[]>();
                for (int i = 0; i < idx.Count; i++)
                {
                    Ixf.Entry e = idx[i];
                    if (e.A != NameGroup || e.C != NameType || !instances.Contains(e.B) || e.Size < 4) continue;
                    if ((int)Util.U32(d, e.Off) + 4 != e.Size) continue;
                    string n = edit(Util.Latin1.GetString(d, e.Off + 4, e.Size - 4));
                    if (n == null) continue;
                    byte[] t = Util.Latin1.GetBytes(n), rec = new byte[4 + t.Length];
                    Util.Put(rec, 0, (uint)t.Length); Array.Copy(t, 0, rec, 4, t.Length);
                    changes[i] = rec;
                }
                if (write && changes.Count > 0) fs.Set(f, Ixf.Rebuild(d, changes));
            }
        }

        public CompState GetState(FileSet fs, string apps)
        {
            int tagged = 0, plain = 0;
            Walk(fs, apps, delegate(string n) { if (n.EndsWith(Tag)) tagged++; else plain++; return null; }, false);
            return plain == 0 && tagged > 0 ? CompState.Installed : tagged == 0 ? CompState.NotInstalled : CompState.Partial;
        }

        public void Plan(FileSet fs, string apps, bool install)
        {
            if (install) Walk(fs, apps, delegate(string n) { return n.EndsWith(Tag) ? null : n + Tag; }, true);
            else Walk(fs, apps, delegate(string n) { return n.EndsWith(Tag) ? n.Substring(0, n.Length - Tag.Length) : null; }, true);
        }
    }
}
