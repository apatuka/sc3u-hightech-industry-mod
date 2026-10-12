// Shared window + command line of the SC3U community patchers (identical file in every patcher repository).
//   <exe>                                   opens the window
//   <exe> --check|--install|--uninstall [game folder]
//   exit codes: 0 done / not installed, 1 nothing to do / installed, 5 partial, 2 unsupported files, 3 error or not found
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Microsoft.Win32;

namespace SC3UMod
{
    public sealed class ModInfo
    {
        public string Title, Version, DescriptionEs, DescriptionEn, BackupFolder;
        public List<IComponent> Components = new List<IComponent>();
        public Dictionary<string, string> DllShaOriginal = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    }

    public static class Game
    {
        /// <summary>Returns the game's root folder (the one that contains Apps\SIMRCI.DLL) or null.</summary>
        public static string Resolve(string p)
        {
            if (string.IsNullOrEmpty(p)) return null;
            try
            {
                if (File.Exists(p)) p = Path.GetDirectoryName(p);
                p = Path.GetFullPath(p);
                if (p.Length > Path.GetPathRoot(p).Length) p = p.TrimEnd('\\', '/');   // never turn "C:\" into "C:" (drive-relative)
                if (File.Exists(Path.Combine(p, @"Apps\SIMRCI.DLL"))) return p;
                if (File.Exists(Path.Combine(p, "SIMRCI.DLL")) && Path.GetFileName(p).Equals("Apps", StringComparison.OrdinalIgnoreCase))
                    return Path.GetDirectoryName(p);
            }
            catch { }
            return null;
        }

        public static string AutoDetect()
        {
            List<string> dirs = new List<string>();
            string exeDir = Path.GetDirectoryName(Application.ExecutablePath);
            for (string d = exeDir; !string.IsNullOrEmpty(d); d = Path.GetDirectoryName(d)) dirs.Add(d);
            foreach (string key in new string[] { @"SOFTWARE\WOW6432Node\GOG.com\Games\2086050016", @"SOFTWARE\GOG.com\Games\2086050016" })
            {
                try
                {
                    using (RegistryKey k = Registry.LocalMachine.OpenSubKey(key))
                        if (k != null && k.GetValue("path") != null) dirs.Add(k.GetValue("path").ToString());
                }
                catch { }
            }
            string pf86 = Environment.GetEnvironmentVariable("ProgramFiles(x86)") ?? @"C:\Program Files (x86)";
            string pf = Environment.GetEnvironmentVariable("ProgramFiles") ?? @"C:\Program Files";
            dirs.Add(@"C:\GOG Games\SimCity 3000 Unlimited");
            dirs.Add(Path.Combine(pf86, @"GOG Galaxy\Games\SimCity 3000 Unlimited"));
            dirs.Add(Path.Combine(pf86, @"GOG.com\SimCity 3000 Unlimited"));
            dirs.Add(Path.Combine(pf86, @"Maxis\SimCity 3000 Unlimited"));
            dirs.Add(Path.Combine(pf, @"Maxis\SimCity 3000 Unlimited"));
            foreach (string d in dirs)
            {
                string r = Resolve(d);
                if (r != null) return r;
            }
            return null;
        }
    }

    public sealed class Runner
    {
        public readonly ModInfo Mod;
        public Runner(ModInfo m) { Mod = m; }

        public List<KeyValuePair<IComponent, CompState>> States(string root)
        {
            FileSet fs = new FileSet();
            string apps = Path.Combine(root, "Apps");
            List<KeyValuePair<IComponent, CompState>> r = new List<KeyValuePair<IComponent, CompState>>();
            foreach (IComponent c in Mod.Components)
            {
                CompState s;
                try { s = c.GetState(fs, apps); } catch { s = CompState.Unsupported; }
                r.Add(new KeyValuePair<IComponent, CompState>(c, s));
            }
            return r;
        }

        public CompState Overall(string root)
        {
            List<CompState> l = new List<CompState>();
            foreach (KeyValuePair<IComponent, CompState> kv in States(root)) l.Add(kv.Value);
            return Util.Combine(l);
        }

        /// <returns>number of files written (0 = nothing to do)</returns>
        public int Run(string root, bool install)
        {
            CompState o = Overall(root);
            if (o == CompState.Unsupported || o == CompState.Missing) throw new NotSupportedException();
            FileSet fs = new FileSet();
            string apps = Path.Combine(root, "Apps");
            foreach (IComponent c in Mod.Components) c.Plan(fs, apps, install);
            int n = fs.Commit(root, Path.Combine(root, Mod.BackupFolder), Mod.DllShaOriginal);
            CompState after = Overall(root);
            if (after != (install ? CompState.Installed : CompState.NotInstalled))
                throw new InvalidDataException("verification after writing failed: " + after);
            return n;
        }
    }

    public static class StateText
    {
        public static string Of(CompState s)
        {
            switch (s)
            {
                case CompState.Installed: return L.T("Instalado", "Installed");
                case CompState.NotInstalled: return L.T("No instalado", "Not installed");
                case CompState.Partial: return L.T("Instalado a medias", "Partially installed");
                case CompState.Unsupported: return L.T("Versión no compatible", "Unsupported version");
                default: return L.T("No encontrado", "Not found");
            }
        }
    }

    sealed class ModForm : Form
    {
        readonly Runner run;
        readonly TextBox pathBox = new TextBox();
        readonly ListView list = new ListView();
        readonly Label status = new Label();
        readonly Button installBtn = new Button(), uninstallBtn = new Button(), browse = new Button(), closeBtn = new Button();
        string root;

        public ModForm(Runner r)
        {
            run = r;
            Text = r.Mod.Title + " " + r.Mod.Version;
            Font = new Font("Segoe UI", 9F);
            AutoScaleMode = AutoScaleMode.Font;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(660, 560);
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }

            Label title = new Label();
            title.Text = r.Mod.Title;
            title.Font = new Font("Segoe UI", 13F, FontStyle.Bold);
            title.AutoSize = false; title.SetBounds(16, 12, 628, 30);
            Label info = new Label();
            info.Text = L.T(r.Mod.DescriptionEs, r.Mod.DescriptionEn);
            info.AutoSize = false; info.SetBounds(16, 46, 628, 150);

            Label pathLbl = new Label();
            pathLbl.Text = L.T("Carpeta de SimCity 3000 Unlimited:", "SimCity 3000 Unlimited folder:");
            pathLbl.AutoSize = false; pathLbl.SetBounds(16, 200, 628, 20);
            pathBox.ReadOnly = true; pathBox.TabStop = false; pathBox.SetBounds(16, 222, 520, 24);
            browse.Text = L.T("Buscar…", "Browse…"); browse.SetBounds(544, 220, 100, 28);
            browse.Click += delegate { Browse(); };

            list.View = View.Details; list.FullRowSelect = true; list.HeaderStyle = ColumnHeaderStyle.Nonclickable;
            list.Columns.Add(L.T("Componente", "Component"), 450);
            list.Columns.Add(L.T("Estado", "State"), 150);
            list.SetBounds(16, 256, 628, 130);

            status.AutoSize = false; status.SetBounds(16, 394, 628, 96);
            status.BorderStyle = BorderStyle.FixedSingle; status.Padding = new Padding(8);

            installBtn.Text = L.T("Instalar", "Install"); installBtn.SetBounds(16, 500, 170, 36);
            installBtn.Font = new Font("Segoe UI", 10F, FontStyle.Bold);
            installBtn.Click += delegate { Do(true); };
            uninstallBtn.Text = L.T("Desinstalar", "Uninstall"); uninstallBtn.SetBounds(196, 500, 170, 36);
            uninstallBtn.Click += delegate { Do(false); };
            closeBtn.Text = L.T("Salir", "Exit"); closeBtn.SetBounds(544, 500, 100, 36);
            closeBtn.Click += delegate { Close(); };

            Label foot = new Label();
            foot.Text = L.T("Mod de la comunidad · no afiliado a EA ni Maxis · ", "Community mod · not affiliated with EA or Maxis · ") + r.Mod.Version;
            foot.ForeColor = SystemColors.GrayText; foot.AutoSize = false; foot.SetBounds(16, 540, 628, 18);

            Controls.AddRange(new Control[] { title, info, pathLbl, pathBox, browse, list, status, installBtn, uninstallBtn, closeBtn, foot });
            Load += delegate
            {
                SetRoot(Game.AutoDetect());
                ActiveControl = installBtn.Enabled ? installBtn : uninstallBtn.Enabled ? uninstallBtn : (Control)browse;
            };
        }

        void Browse()
        {
            using (FolderBrowserDialog dlg = new FolderBrowserDialog())
            {
                dlg.Description = L.T("Elige la carpeta de SimCity 3000 Unlimited (la que contiene «Apps»).",
                    "Choose the SimCity 3000 Unlimited folder (the one that contains \"Apps\").");
                if (root != null) dlg.SelectedPath = root;
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                string r = Game.Resolve(dlg.SelectedPath);
                if (r == null)
                    MessageBox.Show(this, L.T("En esa carpeta no está Apps\\SIMRCI.DLL.", "Apps\\SIMRCI.DLL is not in that folder."), Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                else SetRoot(r);
            }
        }

        void SetRoot(string r) { root = r; pathBox.Text = r ?? ""; Refresh2(); }

        void Refresh2()
        {
            list.Items.Clear();
            installBtn.Enabled = uninstallBtn.Enabled = false;
            if (root == null)
            {
                SetStatus(SystemColors.ControlText, L.T("No se encontró el juego automáticamente. Usa «Buscar…».", "The game was not found automatically. Use \"Browse…\"."));
                return;
            }
            List<CompState> all = new List<CompState>();
            foreach (KeyValuePair<IComponent, CompState> kv in run.States(root))
            {
                ListViewItem it = new ListViewItem(kv.Key.Name);
                it.SubItems.Add(StateText.Of(kv.Value));
                list.Items.Add(it);
                all.Add(kv.Value);
            }
            CompState o = Util.Combine(all);
            switch (o)
            {
                case CompState.NotInstalled:
                    SetStatus(Color.FromArgb(0, 100, 0), L.T("Juego compatible encontrado. Listo para instalar.\nCierra SimCity 3000 antes de instalar.",
                        "Compatible game found. Ready to install.\nClose SimCity 3000 before installing."));
                    installBtn.Enabled = true; break;
                case CompState.Installed:
                    SetStatus(Color.FromArgb(0, 70, 140), L.T("El mod está instalado. Puedes desinstalarlo cuando quieras (solo se quitan sus propios cambios).",
                        "The mod is installed. You can uninstall it at any time (only its own changes are removed)."));
                    uninstallBtn.Enabled = true; break;
                case CompState.Partial:
                    SetStatus(Color.FromArgb(150, 90, 0), L.T("El mod está instalado a medias. Pulsa «Instalar» para completarlo o «Desinstalar» para quitarlo.",
                        "The mod is partially installed. Press \"Install\" to complete it or \"Uninstall\" to remove it."));
                    installBtn.Enabled = uninstallBtn.Enabled = true; break;
                case CompState.Unsupported:
                    SetStatus(Color.FromArgb(160, 0, 0), L.T("Algún archivo no es de la versión compatible (SimCity 3000 Unlimited de GOG) o fue modificado por otro programa. No se cambiará nada.",
                        "Some file is not the supported version (GOG SimCity 3000 Unlimited) or was changed by another program. Nothing will be changed."));
                    break;
                default:
                    SetStatus(Color.FromArgb(160, 0, 0), L.T("Faltan archivos del juego en esa carpeta.", "Game files are missing in that folder."));
                    break;
            }
        }

        void SetStatus(Color c, string msg) { status.ForeColor = c; status.Text = msg; }

        void Do(bool install)
        {
            try
            {
                run.Run(root, install);
                Refresh2();
                MessageBox.Show(this, install
                    ? L.T("¡Mod instalado!\n\nCopias de los archivos originales: " + Path.Combine(root, run.Mod.BackupFolder) + "\n\nAbre SimCity 3000 y carga tu ciudad.",
                          "Mod installed!\n\nCopies of the original files: " + Path.Combine(root, run.Mod.BackupFolder) + "\n\nStart SimCity 3000 and load your city.")
                    : L.T("Mod desinstalado.", "Mod uninstalled."), L.T("Listo", "Done"), MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                string msg;
                if (ex is UnauthorizedAccessException)
                    msg = L.T("Sin permiso para escribir en la carpeta del juego.\nCierra el programa y ábrelo con clic derecho → «Ejecutar como administrador».",
                        "No permission to write to the game folder.\nClose the program and open it with right click → \"Run as administrator\".");
                else if (ex is IOException)
                    msg = L.T("No se pudo escribir un archivo: probablemente SimCity 3000 está abierto. Ciérralo y vuelve a intentarlo.\n\n" + ex.Message,
                        "Could not write a file: SimCity 3000 is probably running. Close it and try again.\n\n" + ex.Message);
                else if (ex is NotSupportedException)
                    msg = L.T("Archivos del juego no compatibles. No se modificó nada.", "Unsupported game files. Nothing was changed.");
                else msg = ex.GetType().Name + ": " + ex.Message;
                Refresh2();
                MessageBox.Show(this, msg, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }
    }

    public static class App
    {
        [DllImport("kernel32.dll")] static extern bool AttachConsole(int pid);

        public static int Start(ModInfo mod, string[] args)
        {
            Runner run = new Runner(mod);
            if (args.Length >= 2 && args[0] == "--screenshot")       // <exe> --screenshot out.png [es|en]  (README images)
            {
                if (args.Length >= 3) L.Es = args[2] == "es";
                Application.EnableVisualStyles();
                using (ModForm f = new ModForm(run))
                {
                    f.StartPosition = FormStartPosition.Manual; f.Location = new Point(-4000, -4000);
                    f.Show(); Application.DoEvents();
                    using (Bitmap bmp = new Bitmap(f.Width, f.Height))
                    {
                        f.DrawToBitmap(bmp, new Rectangle(0, 0, f.Width, f.Height));
                        bmp.Save(args[1], System.Drawing.Imaging.ImageFormat.Png);
                    }
                }
                return 0;
            }
            if (args.Length >= 1 && args[0].StartsWith("--"))
            {
                AttachConsole(-1);
                string root = args.Length >= 2 ? Game.Resolve(args[1]) : Game.AutoDetect();
                if (root == null) { Console.WriteLine("game folder not found"); return 3; }
                try
                {
                    if (args[0] == "--check")
                    {
                        foreach (KeyValuePair<IComponent, CompState> kv in run.States(root)) Console.WriteLine("  " + kv.Value + "  " + kv.Key.Name);
                        CompState o = run.Overall(root);
                        Console.WriteLine("state: " + o);
                        return o == CompState.NotInstalled ? 0 : o == CompState.Installed ? 1 : o == CompState.Partial ? 5 : o == CompState.Unsupported ? 2 : 3;
                    }
                    if (args[0] == "--install" || args[0] == "--uninstall")
                    {
                        int n = run.Run(root, args[0] == "--install");
                        Console.WriteLine(n == 0 ? "nothing to do" : (args[0] == "--install" ? "installed" : "uninstalled") + " (" + n + " file(s) written)");
                        return n == 0 ? 1 : 0;
                    }
                }
                catch (NotSupportedException) { Console.WriteLine("unsupported game files - nothing changed"); return 2; }
                catch (Exception ex) { Console.WriteLine("error: " + ex.Message); return 3; }
                Console.WriteLine("usage: --check|--install|--uninstall [game folder]");
                return 4;
            }
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new ModForm(run));
            return 0;
        }
    }
}
