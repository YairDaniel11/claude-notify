using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using Microsoft.Win32;

[assembly: System.Reflection.AssemblyTitle("Claude Notify")]
[assembly: System.Reflection.AssemblyVersion("1.0.0.0")]

namespace ClaudeNotify
{
    // ---------------------------------------------------------------- Program
    static class Program
    {
        [STAThread]
        static void Main(string[] args)
        {
            string a0 = args.Length > 0 ? args[0] : "";
            if (a0 == "--make-icon")
            {
                using (var fs = File.Create(args[1])) Branding.MakeIcon(32).Save(fs);
                return;
            }
            if (a0 == "--notify") { NotifyClient.Run(); return; }
            if (a0 == "--install") { HookInstaller.Install(); return; }
            if (a0 == "--uninstall") { HookInstaller.Remove(); return; }

            bool created;
            using (var m = new Mutex(true, "Local\\ClaudeNotifyTray", out created))
            {
                if (!created)
                {
                    // כבר רץ - מבקשים ממנו לפתוח את חלון ההגדרות
                    if (a0 != "--hidden") NotifyClient.Send("{\"__cmd\":\"settings\"}", false);
                    return;
                }
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.Run(new TrayContext(a0 == "--hidden"));
            }
        }
    }

    // ---------------------------------------------------------------- Branding
    static class Branding
    {
        public static Icon MakeIcon(int size)
        {
            var bmp = new Bitmap(size, size);
            using (var g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
                g.Clear(Color.Transparent);
                using (var b = new SolidBrush(Color.FromArgb(217, 119, 87)))
                    g.FillEllipse(b, 1, 1, size - 2, size - 2);
                using (var f = new Font("Segoe UI", size * 0.55f, FontStyle.Bold, GraphicsUnit.Pixel))
                using (var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
                    g.DrawString("C", f, Brushes.White, new RectangleF(0, 0, size, size), sf);
            }
            return Icon.FromHandle(bmp.GetHicon());
        }
    }

    // ---------------------------------------------------------------- Config
    class Config
    {
        public bool Enabled { get; set; }
        public bool Summarize { get; set; }
        public int MaxWords { get; set; }
        public bool SkipWhenVisible { get; set; }
        public string Processes { get; set; }
        public int DisplaySeconds { get; set; }
        public bool UseCustomPopup { get; set; }
        public bool PopupLeft { get; set; }   // false = פינה ימנית תחתונה, true = שמאלית תחתונה

        public Config()
        {
            DisplaySeconds = 8; UseCustomPopup = true;
            Enabled = true; Summarize = true; MaxWords = 30; SkipWhenVisible = true;
            Processes = "Code, Code - Insiders, Cursor";
        }

        public static string Dir
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ClaudeNotify"); }
        }
        static string FilePath { get { return Path.Combine(Dir, "config.json"); } }

        public static Config Load()
        {
            try
            {
                if (File.Exists(FilePath))
                {
                    var c = new JavaScriptSerializer().Deserialize<Config>(File.ReadAllText(FilePath, Encoding.UTF8));
                    if (c != null)
                    {
                        if (c.MaxWords < 5 || c.MaxWords > 100) c.MaxWords = 30;
                        if (c.DisplaySeconds < 2 || c.DisplaySeconds > 60) c.DisplaySeconds = 8;
                        if (string.IsNullOrEmpty(c.Processes)) c.Processes = "Code, Code - Insiders, Cursor";
                        return c;
                    }
                }
            }
            catch { }
            return new Config();
        }

        public void Save()
        {
            Directory.CreateDirectory(Dir);
            File.WriteAllText(FilePath, new JavaScriptSerializer().Serialize(this), new UTF8Encoding(false));
        }

        public string[] ProcessList()
        {
            var l = new List<string>();
            foreach (var p in Processes.Split(','))
            {
                var t = p.Trim();
                if (t.Length > 0) l.Add(t);
            }
            return l.ToArray();
        }
    }

    static class Log
    {
        public static void Write(string msg)
        {
            try
            {
                Directory.CreateDirectory(Config.Dir);
                string p = Path.Combine(Config.Dir, "log.txt");
                var fi = new FileInfo(p);
                if (fi.Exists && fi.Length > 200000) File.Delete(p);
                File.AppendAllText(p, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "  " + msg + "\r\n", Encoding.UTF8);
            }
            catch { }
        }
    }

    // ---------------------------------------------------------------- Hook client (--notify)
    static class NotifyClient
    {
        public static void Run()
        {
            // מניעת לולאה: הסיכום מריץ claude בעצמו, וה-hook שלו לא צריך להתריע
            if (Environment.GetEnvironmentVariable("CLAUDE_NOTIFY_CHILD") != null) return;
            string input = "";
            try
            {
                using (var sr = new StreamReader(Console.OpenStandardInput(), new UTF8Encoding(false)))
                    input = sr.ReadToEnd();
            }
            catch { }
            if (input.Trim().Length == 0) input = "{}";
            Send(input, true);
        }

        public static bool Send(string payload, bool launchIfMissing)
        {
            try
            {
                if (!TrySend(payload, 600))
                {
                    if (!launchIfMissing) return false;
                    var psi = new ProcessStartInfo(Application.ExecutablePath, "--hidden");
                    psi.UseShellExecute = false;
                    Process.Start(psi);
                    return TrySend(payload, 10000);
                }
                return true;
            }
            catch { return false; }
        }

        static bool TrySend(string payload, int timeoutMs)
        {
            try
            {
                using (var c = new NamedPipeClientStream(".", "ClaudeNotifyPipe", PipeDirection.Out))
                {
                    c.Connect(timeoutMs);
                    var b = new UTF8Encoding(false).GetBytes(payload);
                    c.Write(b, 0, b.Length);
                    c.Flush();
                    return true;
                }
            }
            catch { return false; }
        }
    }

    // ---------------------------------------------------------------- Windows API
    class WinInfo { public long Handle; public string Title; }

    static class WinApi
    {
        delegate bool EnumProc(IntPtr h, IntPtr l);
        [StructLayout(LayoutKind.Sequential)] struct RECT { public int L, T, R, B; }
        [StructLayout(LayoutKind.Sequential)] struct POINT { public int X, Y; }
        [DllImport("user32.dll")] static extern bool EnumWindows(EnumProc p, IntPtr l);
        [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr h);
        [DllImport("user32.dll")] static extern bool IsIconic(IntPtr h);
        [DllImport("user32.dll")] static extern int GetWindowTextLength(IntPtr h);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetWindowText(IntPtr h, StringBuilder s, int n);
        [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
        [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr h, out RECT r);
        [DllImport("user32.dll")] static extern IntPtr WindowFromPoint(POINT p);
        [DllImport("user32.dll")] static extern IntPtr GetAncestor(IntPtr h, uint f);
        [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr h);
        [DllImport("user32.dll")] static extern bool ShowWindow(IntPtr h, int c);
        [DllImport("dwmapi.dll")] static extern int DwmGetWindowAttribute(IntPtr h, int a, out int v, int s);

        public static List<WinInfo> Find(string[] procNames)
        {
            var pids = new HashSet<uint>();
            foreach (var n in procNames)
                foreach (var p in Process.GetProcessesByName(n)) pids.Add((uint)p.Id);
            var res = new List<WinInfo>();
            if (pids.Count == 0) return res;
            EnumWindows(delegate(IntPtr h, IntPtr l)
            {
                uint pid; GetWindowThreadProcessId(h, out pid);
                if (pids.Contains(pid) && IsWindowVisible(h))
                {
                    int n = GetWindowTextLength(h);
                    if (n > 0)
                    {
                        var sb = new StringBuilder(n + 1);
                        GetWindowText(h, sb, n + 1);
                        res.Add(new WinInfo { Handle = h.ToInt64(), Title = sb.ToString() });
                    }
                }
                return true;
            }, IntPtr.Zero);
            return res;
        }

        // האם חלק כלשהו מהחלון נראה על המסך (לא ממוזער ולא מוסתר לגמרי)
        public static bool IsShown(long handle)
        {
            IntPtr h = new IntPtr(handle);
            if (!IsWindowVisible(h) || IsIconic(h)) return false;
            int cloaked; DwmGetWindowAttribute(h, 14, out cloaked, 4);
            if (cloaked != 0) return false;
            RECT r; if (!GetWindowRect(h, out r)) return false;
            double[] f = { 0.15, 0.5, 0.85 };
            foreach (double fy in f)
                foreach (double fx in f)
                {
                    POINT p;
                    p.X = r.L + (int)((r.R - r.L) * fx);
                    p.Y = r.T + (int)((r.B - r.T) * fy);
                    if (GetAncestor(WindowFromPoint(p), 2) == h) return true;
                }
            return false;
        }

        public static void Focus(long handle)
        {
            IntPtr h = new IntPtr(handle);
            if (IsIconic(h)) ShowWindow(h, 9);
            SetForegroundWindow(h);
        }

        // מעלה את החלון ואז פותח בו את השיחה. ה-URI מגיע לחלון ה-VS שהיה אחרון בפוקוס, ולכן קודם מעלים את החלון
        public static void FocusSession(long handle, string session)
        {
            if (handle == 0) return;
            Focus(handle);
            if (string.IsNullOrEmpty(session) || !Regex.IsMatch(session, "^[0-9a-fA-F-]{8,64}$")) return;
            string scheme = SchemeFor(handle);
            if (scheme == null) return;
            ThreadPool.QueueUserWorkItem(delegate
            {
                try
                {
                    Thread.Sleep(300);
                    var psi = new ProcessStartInfo(scheme + "://anthropic.claude-code/open?session=" + session);
                    psi.UseShellExecute = true;
                    Process.Start(psi);
                }
                catch (Exception ex) { Log.Write("open session error: " + ex.Message); }
            });
        }

        static string SchemeFor(long handle)
        {
            try
            {
                uint pid; GetWindowThreadProcessId(new IntPtr(handle), out pid);
                string n = Process.GetProcessById((int)pid).ProcessName;
                if (n.Equals("Code", StringComparison.OrdinalIgnoreCase)) return "vscode";
                if (n.Equals("Code - Insiders", StringComparison.OrdinalIgnoreCase)) return "vscode-insiders";
                if (n.Equals("Cursor", StringComparison.OrdinalIgnoreCase)) return "cursor";
            }
            catch { }
            return null;
        }
    }

    // ---------------------------------------------------------------- Text helpers
    static class TextUtil
    {
        public static string Str(Dictionary<string, object> d, string key)
        {
            object o;
            if (d != null && d.TryGetValue(key, out o) && o != null) return o.ToString();
            return "";
        }

        public static string FromTranscript(string path)
        {
            string last = "";
            try
            {
                var ser = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };
                foreach (var line in File.ReadLines(path, Encoding.UTF8))
                {
                    if (line.IndexOf("\"assistant\"", StringComparison.Ordinal) < 0) continue;
                    try
                    {
                        var o = ser.Deserialize<Dictionary<string, object>>(line);
                        if (Str(o, "type") != "assistant") continue;
                        var msg = o["message"] as Dictionary<string, object>;
                        if (msg == null) continue;
                        var content = msg["content"] as IEnumerable;
                        if (content == null || msg["content"] is string) continue;
                        var sb = new StringBuilder();
                        foreach (var part in content)
                        {
                            var pd = part as Dictionary<string, object>;
                            if (pd != null && Str(pd, "type") == "text") sb.Append(Str(pd, "text")).Append(' ');
                        }
                        if (sb.ToString().Trim().Length > 0) last = sb.ToString();
                    }
                    catch { }
                }
            }
            catch { }
            return last;
        }

        public static string Clean(string text)
        {
            text = Regex.Replace(text, @"```[\s\S]*?```", " ");
            text = Regex.Replace(text, @"\[([^\]]*)\]\([^)]*\)", "$1");
            text = Regex.Replace(text, @"(?m)^\s*[-*]\s+", " ");
            text = Regex.Replace(text, @"[#*>|_~`]+", " ");
            text = Regex.Replace(text, @"\s+", " ");
            return text.Trim();
        }

        public static string FirstWords(string text, int max)
        {
            text = Clean(text);
            if (text.Length == 0) return "קלוד סיים להשיב לך";
            var words = text.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            string body = string.Join(" ", words, 0, Math.Min(max, words.Length));
            if (words.Length > max) body += "...";
            if (body.Length > 250) body = body.Substring(0, 247) + "...";
            return body;
        }
    }

    // ---------------------------------------------------------------- Summarizer
    static class Summarizer
    {
        public static string FindClaude()
        {
            try
            {
                string pathVar = Environment.GetEnvironmentVariable("PATH") ?? "";
                foreach (var d in pathVar.Split(';'))
                {
                    if (d.Trim().Length == 0) continue;
                    foreach (var n in new[] { "claude.exe", "claude.cmd" })
                    {
                        string p = Path.Combine(d.Trim(), n);
                        if (File.Exists(p)) return p;
                    }
                }
                string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                string local = Path.Combine(home, @".local\bin\claude.exe");
                if (File.Exists(local)) return local;
                string best = null; DateTime bestTime = DateTime.MinValue;
                foreach (var vs in Directory.GetDirectories(home, ".vscode*"))
                {
                    string ext = Path.Combine(vs, "extensions");
                    if (!Directory.Exists(ext)) continue;
                    foreach (var e in Directory.GetDirectories(ext, "anthropic.claude-code-*"))
                    {
                        string exe = Path.Combine(e, @"resources\native-binary\claude.exe");
                        if (File.Exists(exe) && File.GetLastWriteTime(exe) > bestTime)
                        { best = exe; bestTime = File.GetLastWriteTime(exe); }
                    }
                }
                return best;
            }
            catch { return null; }
        }

        // מחזיר null בכל כשל
        public static string Run(string text, int maxWords)
        {
            try
            {
                string exe = FindClaude();
                if (exe == null) { Log.Write("summary: claude not found"); return null; }
                string input = text.Length > 6000 ? text.Substring(0, 6000) : text;
                string args = "-p --model haiku --no-session-persistence --strict-mcp-config --setting-sources local --disable-slash-commands --mcp-config \"{\\\"mcpServers\\\":{}}\"";
                var psi = new ProcessStartInfo();
                if (exe.EndsWith(".cmd", StringComparison.OrdinalIgnoreCase)) { psi.FileName = "cmd.exe"; psi.Arguments = "/c \"" + exe + "\" " + args; }
                else { psi.FileName = exe; psi.Arguments = args; }
                psi.UseShellExecute = false;
                psi.CreateNoWindow = true;
                psi.RedirectStandardInput = true;
                psi.RedirectStandardOutput = true;
                psi.RedirectStandardError = true;
                psi.StandardOutputEncoding = Encoding.UTF8;
                psi.EnvironmentVariables["CLAUDE_NOTIFY_CHILD"] = "1";
                using (var p = Process.Start(psi))
                {
                    string prompt = "סכם את ההודעה הבאה בעברית בעד " + Math.Max(5, maxWords - 5) +
                        " מילים, בניסוח קצר וברור. החזר רק את הסיכום, בלי הקדמה ובלי סימוני עיצוב.\n\n--- ההודעה ---\n" + input;
                    var bytes = new UTF8Encoding(false).GetBytes(prompt);
                    p.StandardInput.BaseStream.Write(bytes, 0, bytes.Length);
                    p.StandardInput.Close();
                    var outTask = p.StandardOutput.ReadToEndAsync();
                    p.StandardError.ReadToEndAsync();
                    if (!p.WaitForExit(40000)) { try { p.Kill(); } catch { } Log.Write("summary: timeout"); return null; }
                    if (p.ExitCode != 0) { Log.Write("summary: exit " + p.ExitCode); return null; }
                    string s = outTask.Result.Trim();
                    return s.Length > 0 ? s : null;
                }
            }
            catch (Exception ex) { Log.Write("summary error: " + ex.Message); return null; }
        }
    }

    // ---------------------------------------------------------------- Claude Code hook installer
    static class HookInstaller
    {
        public static string InstallDir
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ClaudeNotify"); }
        }
        public static string InstalledExe { get { return Path.Combine(InstallDir, "ClaudeNotify.exe"); } }
        static string SettingsPath
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), @".claude\settings.json"); }
        }

        static Dictionary<string, object> ReadSettings()
        {
            var ser = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };
            if (File.Exists(SettingsPath))
            {
                string t = File.ReadAllText(SettingsPath, Encoding.UTF8);
                if (t.Trim().Length > 0) return ser.Deserialize<Dictionary<string, object>>(t);
            }
            return new Dictionary<string, object>();
        }

        static void WriteSettings(Dictionary<string, object> root)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath));
            if (File.Exists(SettingsPath)) File.Copy(SettingsPath, SettingsPath + ".bak", true);
            var ser = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };
            File.WriteAllText(SettingsPath, Pretty(ser.Serialize(root)), new UTF8Encoding(false));
        }

        static bool IsOurs(object entry)
        {
            string s = new JavaScriptSerializer().Serialize(entry);
            return s.IndexOf("ClaudeNotify.exe", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   s.IndexOf("notify.ps1", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        static List<object> StopList(Dictionary<string, object> root, bool create)
        {
            var list = new List<object>();
            object h;
            Dictionary<string, object> hooks = null;
            if (root.TryGetValue("hooks", out h)) hooks = h as Dictionary<string, object>;
            if (hooks == null)
            {
                if (!create) return list;
                hooks = new Dictionary<string, object>();
                root["hooks"] = hooks;
            }
            object s;
            if (hooks.TryGetValue("Stop", out s) && s is IEnumerable && !(s is string))
                foreach (var e in (IEnumerable)s) list.Add(e);
            return list;
        }

        static void SetStop(Dictionary<string, object> root, List<object> list)
        {
            var hooks = root.ContainsKey("hooks") ? root["hooks"] as Dictionary<string, object> : null;
            if (hooks == null) { if (list.Count == 0) return; hooks = new Dictionary<string, object>(); root["hooks"] = hooks; }
            if (list.Count > 0) hooks["Stop"] = list.ToArray();
            else hooks.Remove("Stop");
            if (hooks.Count == 0) root.Remove("hooks");
        }

        public static bool IsInstalled()
        {
            try
            {
                foreach (var e in StopList(ReadSettings(), false))
                    if (new JavaScriptSerializer().Serialize(e).IndexOf("ClaudeNotify.exe", StringComparison.OrdinalIgnoreCase) >= 0) return true;
            }
            catch { }
            return false;
        }

        public static void Install()
        {
            Directory.CreateDirectory(InstallDir);
            string me = Application.ExecutablePath;
            if (!string.Equals(Path.GetFullPath(me), Path.GetFullPath(InstalledExe), StringComparison.OrdinalIgnoreCase))
                File.Copy(me, InstalledExe, true);

            var root = ReadSettings();
            var list = StopList(root, true);
            list.RemoveAll(delegate(object e) { return IsOurs(e); });
            var cmd = new Dictionary<string, object>();
            cmd["type"] = "command";
            // בלי "$input |": PowerShell 5.1 ממיר את ה-stdin ל-ASCII ושובר עברית. ללא pipe התוכנה יורשת את ה-stdin ישירות כ-UTF-8
            cmd["command"] = "& \"" + InstalledExe + "\" --notify";
            cmd["shell"] = "powershell";
            cmd["async"] = true;
            var entry = new Dictionary<string, object>();
            entry["hooks"] = new object[] { cmd };
            list.Add(entry);
            SetStop(root, list);
            WriteSettings(root);
        }

        public static void Remove()
        {
            var root = ReadSettings();
            var list = StopList(root, false);
            list.RemoveAll(delegate(object e) { return IsOurs(e); });
            SetStop(root, list);
            WriteSettings(root);
        }

        static string Pretty(string json)
        {
            var sb = new StringBuilder();
            int indent = 0; bool inStr = false;
            for (int i = 0; i < json.Length; i++)
            {
                char c = json[i];
                if (inStr)
                {
                    sb.Append(c);
                    if (c == '\\' && i + 1 < json.Length) { sb.Append(json[++i]); }
                    else if (c == '"') inStr = false;
                    continue;
                }
                switch (c)
                {
                    case '"': inStr = true; sb.Append(c); break;
                    case '{': case '[':
                        sb.Append(c);
                        if (i + 1 < json.Length && (json[i + 1] == '}' || json[i + 1] == ']')) break;
                        indent++; sb.Append("\r\n").Append(' ', indent * 2); break;
                    case '}': case ']':
                        if (i > 0 && (json[i - 1] == '{' || json[i - 1] == '[')) { sb.Append(c); break; }
                        indent--; sb.Append("\r\n").Append(' ', indent * 2).Append(c); break;
                    case ',': sb.Append(c).Append("\r\n").Append(' ', indent * 2); break;
                    case ':': sb.Append(": "); break;
                    default: sb.Append(c); break;
                }
            }
            return sb.ToString();
        }
    }

    // ---------------------------------------------------------------- Tray
    class TrayContext : ApplicationContext
    {
        readonly NotifyIcon tray;
        readonly Form host;
        readonly ToolStripMenuItem enabledItem;
        Config cfg;
        SettingsForm settings;
        long lastTarget;
        string lastSession = "";

        public TrayContext(bool hidden)
        {
            cfg = Config.Load();
            host = new Form();
            var handle = host.Handle; // יוצר handle כדי לאפשר BeginInvoke מ-threads אחרים

            tray = new NotifyIcon();
            tray.Icon = Branding.MakeIcon(32);
            tray.Text = "Claude Notify";
            var menu = new ContextMenuStrip();
            menu.RightToLeft = RightToLeft.Yes;
            menu.Items.Add("הגדרות...", null, delegate { ShowSettings(); });
            enabledItem = new ToolStripMenuItem("התראות פעילות");
            enabledItem.Checked = cfg.Enabled;
            enabledItem.Click += delegate { cfg.Enabled = !cfg.Enabled; cfg.Save(); SyncEnabled(); };
            menu.Items.Add(enabledItem);
            menu.Items.Add("התראת בדיקה", null, delegate { Show(0, "זו התראת בדיקה מ-Claude Notify. אם אתה רואה אותה, הכול עובד.", ""); });
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("יציאה", null, delegate { tray.Visible = false; Application.Exit(); });
            tray.ContextMenuStrip = menu;
            tray.MouseDoubleClick += delegate(object s, MouseEventArgs e) { if (e.Button == MouseButtons.Left) ShowSettings(); };
            tray.BalloonTipClicked += delegate { if (lastTarget != 0) WinApi.FocusSession(lastTarget, lastSession); };
            tray.Visible = true;
            SyncEnabled();

            var t = new Thread(PipeLoop);
            t.IsBackground = true;
            t.Start();

            if (!hidden) ShowSettings();
        }

        void SyncEnabled()
        {
            enabledItem.Checked = cfg.Enabled;
            tray.Text = cfg.Enabled ? "Claude Notify - פעיל" : "Claude Notify - כבוי";
            if (settings != null) settings.Reload();
        }

        public void ConfigChanged(Config c) { cfg = c; SyncEnabled(); }

        void ShowSettings()
        {
            if (settings == null || settings.IsDisposed) settings = new SettingsForm(this, cfg);
            settings.Reload();
            settings.Show();
            settings.WindowState = FormWindowState.Normal;
            settings.Activate();
        }

        public void TestNotification()
        {
            Show(0, "זו התראת בדיקה מ-Claude Notify. אם אתה רואה אותה, הכול עובד.", "");
        }

        void PipeLoop()
        {
            while (true)
            {
                try
                {
                    using (var server = new NamedPipeServerStream("ClaudeNotifyPipe", PipeDirection.In, 1, PipeTransmissionMode.Byte, PipeOptions.None))
                    {
                        server.WaitForConnection();
                        string payload;
                        using (var sr = new StreamReader(server, new UTF8Encoding(false)))
                            payload = sr.ReadToEnd();
                        host.BeginInvoke(new Action(delegate { OnMessage(payload); }));
                    }
                }
                catch (Exception ex) { Log.Write("pipe error: " + ex.Message); Thread.Sleep(500); }
            }
        }

        void OnMessage(string payload)
        {
            Dictionary<string, object> j;
            try { j = new JavaScriptSerializer { MaxJsonLength = int.MaxValue }.Deserialize<Dictionary<string, object>>(payload); }
            catch { Log.Write("bad payload"); return; }
            if (j == null) return;

            if (j.ContainsKey("__cmd"))
            {
                if (TextUtil.Str(j, "__cmd") == "settings") ShowSettings();
                return;
            }
            if (!cfg.Enabled) { Log.Write("skipped: disabled"); return; }

            string cwd = TextUtil.Str(j, "cwd");
            string session = TextUtil.Str(j, "session_id");
            string text = TextUtil.Str(j, "last_assistant_message");
            if (text.Trim().Length == 0)
            {
                string tp = TextUtil.Str(j, "transcript_path");
                if (tp.Length > 0 && File.Exists(tp)) text = TextUtil.FromTranscript(tp);
            }

            long target = FindTarget(cwd);
            if (cfg.SkipWhenVisible && target != 0 && WinApi.IsShown(target))
            {
                Log.Write("skipped: editor visible");
                return;
            }
            int heb = 0, qm = 0;
            foreach (char ch in text) { if (ch >= 'א' && ch <= 'ת') heb++; else if (ch == '?') qm++; }
            Log.Write("notify: " + text.Length + " chars (hebrew=" + heb + ", '?'=" + qm + "), target=" + target + ", summarize=" + cfg.Summarize);

            int max = cfg.MaxWords;
            if (cfg.Summarize && text.Trim().Length > 0)
            {
                string src = text;
                ThreadPool.QueueUserWorkItem(delegate
                {
                    string s = Summarizer.Run(src, max);
                    host.BeginInvoke(new Action(delegate
                    {
                        // בזמן הסיכום ייתכן שהמשתמש כבר חזר לחלון
                        if (cfg.SkipWhenVisible && target != 0 && WinApi.IsShown(target)) return;
                        Show(target, TextUtil.FirstWords(s ?? src, max), session);
                    }));
                });
            }
            else Show(target, TextUtil.FirstWords(text, max), session);
        }

        long FindTarget(string cwd)
        {
            try
            {
                var wins = WinApi.Find(cfg.ProcessList());
                string leaf = "";
                if (cwd.Length > 0) leaf = Path.GetFileName(cwd.TrimEnd('\\', '/'));
                if (leaf.Length > 0)
                    foreach (var w in wins)
                        if (w.Title.IndexOf(leaf, StringComparison.OrdinalIgnoreCase) >= 0) return w.Handle;
                if (wins.Count > 0) return wins[0].Handle;
            }
            catch (Exception ex) { Log.Write("find error: " + ex.Message); }
            return 0;
        }

        ToastForm toast;

        void Show(long target, string body, string session)
        {
            lastTarget = target;
            lastSession = session;
            if (body.Length > 250) body = body.Substring(0, 247) + "...";
            if (cfg.UseCustomPopup)
            {
                if (toast != null && !toast.IsDisposed) toast.Close();
                toast = new ToastForm(body, target, session, cfg.DisplaySeconds, cfg.PopupLeft);
                toast.Show();
            }
            else tray.ShowBalloonTip(cfg.DisplaySeconds * 1000, "Claude Code", body, ToolTipIcon.None);
        }
    }

    // ---------------------------------------------------------------- Popup notification
    class ToastForm : Form
    {
        readonly System.Windows.Forms.Timer timer = new System.Windows.Forms.Timer();
        readonly long target;
        readonly string session;
        int remainingMs;

        public ToastForm(string body, long target, string session, int seconds, bool left)
        {
            this.target = target;
            this.session = session;
            remainingMs = seconds * 1000;
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            StartPosition = FormStartPosition.Manual;
            BackColor = Color.FromArgb(40, 40, 44);
            Cursor = target != 0 ? Cursors.Hand : Cursors.Default;

            const int width = 380, inner = 340;
            var titleFont = new Font("Segoe UI", 10f, FontStyle.Bold);
            var bodyFont = new Font("Segoe UI", 10.5f);
            int textH = TextRenderer.MeasureText(body, bodyFont, new Size(inner, 0),
                TextFormatFlags.WordBreak | TextFormatFlags.RightToLeft | TextFormatFlags.TextBoxControl).Height + 4;

            var title = new Label { Text = "Claude Code", Font = titleFont, ForeColor = Color.FromArgb(217, 119, 87), AutoSize = false, Size = new Size(inner, 22), Location = new Point(20, 10), TextAlign = ContentAlignment.TopRight, RightToLeft = RightToLeft.Yes };
            var lbl = new Label { Text = body, Font = bodyFont, ForeColor = Color.White, AutoSize = false, Size = new Size(inner, textH), Location = new Point(20, 36), TextAlign = ContentAlignment.TopRight, RightToLeft = RightToLeft.Yes };
            var close = new Label { Text = "✕", Font = new Font("Segoe UI", 9f), ForeColor = Color.Silver, AutoSize = true, Location = new Point(4, 4), Cursor = Cursors.Hand };
            close.Click += delegate { Close(); };
            Controls.Add(close);
            Controls.Add(title);
            Controls.Add(lbl);
            Size = new Size(width, 36 + textH + 14);

            EventHandler click = delegate { WinApi.FocusSession(this.target, this.session); Close(); };
            Click += click; title.Click += click; lbl.Click += click;

            var wa = Screen.PrimaryScreen.WorkingArea;
            Location = new Point(left ? wa.Left + 16 : wa.Right - Width - 16, wa.Bottom - Height - 16);

            timer.Interval = 100;
            timer.Tick += delegate
            {
                // העכבר מעל ההתראה - הזמן נעצר
                if (Bounds.Contains(Cursor.Position)) return;
                remainingMs -= 100;
                if (remainingMs <= 0) Close();
            };
            timer.Start();
        }

        protected override bool ShowWithoutActivation { get { return true; } }

        protected override CreateParams CreateParams
        {
            get
            {
                var cp = base.CreateParams;
                cp.ExStyle |= 0x08000000 | 0x00000080; // NOACTIVATE | TOOLWINDOW
                return cp;
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            using (var p = new Pen(Color.FromArgb(217, 119, 87)))
                e.Graphics.DrawRectangle(p, 0, 0, Width - 1, Height - 1);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) timer.Dispose();
            base.Dispose(disposing);
        }
    }

    // ---------------------------------------------------------------- Settings GUI
    class SettingsForm : Form
    {
        readonly TrayContext ctx;
        Config cfg;
        CheckBox chkEnabled, chkSummarize, chkSkip, chkStartup, chkPopup;
        NumericUpDown numWords, numSeconds;
        Label lblSec, lblPos;
        ComboBox cmbPos;
        TextBox txtProcs;
        Label lblStatus, lblHook;
        Button btnInstall, btnRemove;
        bool loading;

        public SettingsForm(TrayContext ctx, Config cfg)
        {
            this.ctx = ctx; this.cfg = cfg;
            Text = "Claude Notify - הגדרות";
            Font = new Font("Segoe UI", 10f);
            RightToLeft = RightToLeft.Yes;
            RightToLeftLayout = true;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(560, 600);
            Icon = Branding.MakeIcon(32);

            var panel = new FlowLayoutPanel();
            panel.Dock = DockStyle.Fill;
            panel.FlowDirection = FlowDirection.TopDown;
            panel.WrapContents = false;
            panel.Padding = new Padding(16);
            panel.RightToLeft = RightToLeft.Yes;
            Controls.Add(panel);

            lblStatus = new Label { AutoSize = true, Font = new Font("Segoe UI", 11f, FontStyle.Bold), Margin = new Padding(3, 3, 3, 10) };
            panel.Controls.Add(lblStatus);

            chkEnabled = Check(panel, "הפעל התראות");
            chkSummarize = Check(panel, "סכם את ההודעה (איטי יותר, כ-15 שניות; אחרת תוצג תחילת ההודעה)");
            chkSkip = Check(panel, "אל תציג התראה כשחלון העורך גלוי על המסך");

            var row1 = Row(panel);
            row1.Controls.Add(new Label { Text = "מספר מילים מקסימלי בהתראה:", AutoSize = true, Margin = new Padding(3, 6, 3, 3) });
            numWords = new NumericUpDown { Minimum = 5, Maximum = 60, Width = 60 };
            row1.Controls.Add(numWords);

            chkPopup = Check(panel, "השתמש בחלון התראה משלי בפינת המסך (מאפשר שליטה בזמן התצוגה)");
            var rowSec = Row(panel);
            lblSec = new Label { Text = "זמן הצגת ההתראה (שניות):", AutoSize = true, Margin = new Padding(3, 6, 3, 3) };
            rowSec.Controls.Add(lblSec);
            numSeconds = new NumericUpDown { Minimum = 2, Maximum = 60, Width = 60 };
            rowSec.Controls.Add(numSeconds);
            var rowPos = Row(panel);
            lblPos = new Label { Text = "מיקום ההתראה על המסך:", AutoSize = true, Margin = new Padding(3, 6, 3, 3) };
            rowPos.Controls.Add(lblPos);
            cmbPos = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 160, RightToLeft = RightToLeft.Yes };
            cmbPos.Items.Add("פינה ימנית למטה");
            cmbPos.Items.Add("פינה שמאלית למטה");
            rowPos.Controls.Add(cmbPos);
            chkPopup.CheckedChanged += delegate { numSeconds.Enabled = chkPopup.Checked; cmbPos.Enabled = chkPopup.Checked; };

            var row2 = Row(panel);
            row2.Controls.Add(new Label { Text = "תהליכי העורך (מופרדים בפסיק):", AutoSize = true, Margin = new Padding(3, 6, 3, 3) });
            txtProcs = new TextBox { Width = 240, RightToLeft = RightToLeft.No };
            row2.Controls.Add(txtProcs);

            chkStartup = Check(panel, "הפעל עם Windows");

            var sep = new Label { AutoSize = false, Height = 2, Width = 480, BorderStyle = BorderStyle.Fixed3D, Margin = new Padding(3, 10, 3, 10) };
            panel.Controls.Add(sep);

            lblHook = new Label { AutoSize = true, Margin = new Padding(3, 3, 3, 6) };
            panel.Controls.Add(lblHook);
            var hookRow = Row(panel);
            btnInstall = new Button { Text = "התקן חיבור ל-Claude Code", AutoSize = true, Padding = new Padding(6, 2, 6, 2) };
            btnRemove = new Button { Text = "הסר חיבור", AutoSize = true, Padding = new Padding(6, 2, 6, 2) };
            btnInstall.Click += delegate
            {
                try { HookInstaller.Install(); SaveStartup(chkStartup.Checked); MessageBox.Show(this, "החיבור הותקן. אם ההתראות לא מופיעות, פתח /hooks ב-Claude Code או הפעל אותו מחדש.", "Claude Notify", MessageBoxButtons.OK, MessageBoxIcon.Information, MessageBoxDefaultButton.Button1, MessageBoxOptions.RtlReading | MessageBoxOptions.RightAlign); }
                catch (Exception ex) { Fail(ex); }
                Reload();
            };
            btnRemove.Click += delegate
            {
                try { HookInstaller.Remove(); } catch (Exception ex) { Fail(ex); }
                Reload();
            };
            hookRow.Controls.Add(btnInstall);
            hookRow.Controls.Add(btnRemove);

            var btnRow = Row(panel);
            btnRow.Margin = new Padding(3, 18, 3, 3);
            var btnSave = new Button { Text = "שמור", AutoSize = true, Padding = new Padding(14, 2, 14, 2) };
            var btnTest = new Button { Text = "התראת בדיקה", AutoSize = true, Padding = new Padding(6, 2, 6, 2) };
            btnSave.Click += delegate { Save(); Hide(); };
            btnTest.Click += delegate { Save(); ctx.TestNotification(); };
            btnRow.Controls.Add(btnSave);
            btnRow.Controls.Add(btnTest);
            AcceptButton = btnSave;

            FormClosing += delegate(object s, FormClosingEventArgs e)
            {
                if (e.CloseReason == CloseReason.UserClosing) { e.Cancel = true; Hide(); }
            };
        }

        static CheckBox Check(FlowLayoutPanel p, string text)
        {
            var c = new CheckBox { Text = text, AutoSize = true, Margin = new Padding(3, 4, 3, 4) };
            p.Controls.Add(c);
            return c;
        }

        static FlowLayoutPanel Row(FlowLayoutPanel p)
        {
            var r = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.LeftToRight, WrapContents = false, RightToLeft = RightToLeft.Yes };
            p.Controls.Add(r);
            return r;
        }

        void Fail(Exception ex)
        {
            MessageBox.Show(this, "הפעולה נכשלה: " + ex.Message, "Claude Notify", MessageBoxButtons.OK, MessageBoxIcon.Error, MessageBoxDefaultButton.Button1, MessageBoxOptions.RtlReading | MessageBoxOptions.RightAlign);
        }

        const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

        static bool StartupEnabled()
        {
            try { using (var k = Registry.CurrentUser.OpenSubKey(RunKey)) return k != null && k.GetValue("ClaudeNotify") != null; }
            catch { return false; }
        }

        static void SaveStartup(bool on)
        {
            try
            {
                using (var k = Registry.CurrentUser.OpenSubKey(RunKey, true))
                {
                    if (on)
                    {
                        string exe = File.Exists(HookInstaller.InstalledExe) ? HookInstaller.InstalledExe : Application.ExecutablePath;
                        k.SetValue("ClaudeNotify", "\"" + exe + "\" --hidden");
                    }
                    else k.DeleteValue("ClaudeNotify", false);
                }
            }
            catch { }
        }

        public void Reload()
        {
            loading = true;
            cfg = Config.Load();
            chkEnabled.Checked = cfg.Enabled;
            chkSummarize.Checked = cfg.Summarize;
            chkSkip.Checked = cfg.SkipWhenVisible;
            numWords.Value = Math.Max(numWords.Minimum, Math.Min(numWords.Maximum, cfg.MaxWords));
            txtProcs.Text = cfg.Processes;
            chkPopup.Checked = cfg.UseCustomPopup;
            numSeconds.Value = Math.Max(numSeconds.Minimum, Math.Min(numSeconds.Maximum, cfg.DisplaySeconds));
            numSeconds.Enabled = cfg.UseCustomPopup;
            cmbPos.SelectedIndex = cfg.PopupLeft ? 1 : 0;
            cmbPos.Enabled = cfg.UseCustomPopup;
            chkStartup.Checked = StartupEnabled();
            bool inst = HookInstaller.IsInstalled();
            lblHook.Text = inst ? "החיבור ל-Claude Code: מותקן" : "החיבור ל-Claude Code: לא מותקן (נדרש כדי לקבל התראות)";
            lblHook.ForeColor = inst ? Color.DarkGreen : Color.Firebrick;
            btnInstall.Text = inst ? "התקן מחדש" : "התקן חיבור ל-Claude Code";
            btnRemove.Enabled = inst;
            lblStatus.Text = cfg.Enabled ? "ההתראות פעילות" : "ההתראות כבויות";
            loading = false;
        }

        void Save()
        {
            if (loading) return;
            cfg.Enabled = chkEnabled.Checked;
            cfg.Summarize = chkSummarize.Checked;
            cfg.SkipWhenVisible = chkSkip.Checked;
            cfg.MaxWords = (int)numWords.Value;
            cfg.UseCustomPopup = chkPopup.Checked;
            cfg.DisplaySeconds = (int)numSeconds.Value;
            cfg.PopupLeft = cmbPos.SelectedIndex == 1;
            cfg.Processes = txtProcs.Text.Trim().Length > 0 ? txtProcs.Text.Trim() : "Code, Code - Insiders, Cursor";
            cfg.Save();
            SaveStartup(chkStartup.Checked);
            ctx.ConfigChanged(cfg);
        }
    }
}
