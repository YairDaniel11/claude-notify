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
        [System.Runtime.InteropServices.DllImport("user32.dll")] static extern bool SetProcessDPIAware();

        [STAThread]
        static void Main(string[] args)
        {
            string a0 = args.Length > 0 ? args[0] : "";
            try { SetProcessDPIAware(); } catch { }   // ממשק חד במסכים עם סקאלה, במקום תמונה מתוחה ומטושטשת
            if (a0 == "--make-icon")
            {
                using (var fs = File.Create(args[1])) Branding.WriteIco(fs, new[] { 16, 24, 32, 48, 64 });
                return;
            }
            if (a0 == "--notify") { NotifyClient.Run(); return; }
            if (a0 == "--install") { HookInstaller.Install(); return; }
            bool runSchedule = a0 == "--run-schedule";
            if (a0 == "--uninstall") { HookInstaller.Remove(); return; }

            L.Load(Config.Load().Language);
            bool created;
            using (var m = new Mutex(true, "Local\\ClaudeNotifyTray", out created))
            {
                if (!created)
                {
                    // כבר רץ - מבקשים ממנו לפתוח את חלון ההגדרות
                    // --run-schedule כשהתוכנה כבר רצה: היא שולחת בעצמה בשעה, והמשימה רק העירה את המחשב
                    if (a0 != "--hidden" && !runSchedule) NotifyClient.Send("{\"__cmd\":\"settings\"}", false);
                    return;
                }
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.Run(new TrayContext(a0 == "--hidden" || runSchedule, runSchedule));
            }
        }
    }

    // ---------------------------------------------------------------- Branding
    static class Branding
    {
        static readonly Color Orange = Color.FromArgb(217, 119, 87);

        // בועת שיחה לבנה קטנה עם זנב, שמסמלת הודעת פוש. dots = שלוש נקודות כתומות בתוכה (רק כשהיא גדולה מספיק)
        static void DrawBubble(Graphics g, RectangleF r, bool dots) { DrawBubble(g, r, dots, Color.White); }

        static void DrawBubble(Graphics g, RectangleF r, bool dots, Color fill)
        {
            float tail = r.Height * 0.28f;
            var body = new RectangleF(r.X, r.Y, r.Width, r.Height - tail);
            using (var path = Theme.RoundRect(body, body.Height * 0.38f))
            using (var b = new SolidBrush(fill))
            {
                g.FillPath(b, path);
                g.FillPolygon(b, new[]
                {
                    new PointF(r.X + r.Width * 0.18f, body.Bottom - 1),
                    new PointF(r.X + r.Width * 0.18f, r.Bottom),
                    new PointF(r.X + r.Width * 0.46f, body.Bottom - 1)
                });
            }
            if (!dots) return;
            float d = body.Height * 0.17f, cy = body.Y + body.Height / 2f;
            using (var b = new SolidBrush(Orange))
                for (int i = -1; i <= 1; i++)
                    g.FillEllipse(b, body.X + body.Width / 2f + i * d * 2.2f - d / 2f, cy - d / 2f, d, d);
        }

        // לוגו רחב לכותרות: "Claude" בלבן על רקע כתום, ומימין לו בועת שיחה לבנה
        public static Bitmap MakeLogo(int height)
        {
            using (var probe = new Bitmap(1, 1))
            using (var pg = Graphics.FromImage(probe))
            using (var f = new Font("Segoe UI", height * 0.46f, FontStyle.Bold, GraphicsUnit.Pixel))
            {
                float pad = height * 0.34f, gap = height * 0.18f, bw = height * 0.52f;
                float tw = pg.MeasureString("Claude", f, 1000, StringFormat.GenericTypographic).Width;
                int w = (int)Math.Ceiling(pad + tw + gap + bw + pad);
                var bmp = new Bitmap(w, height);
                using (var g = Graphics.FromImage(bmp))
                {
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
                    g.Clear(Color.Transparent);
                    using (var path = Theme.RoundRect(new RectangleF(0.5f, 0.5f, w - 1, height - 1), height * 0.3f))
                    using (var b = new SolidBrush(Orange))
                        g.FillPath(b, path);
                    float ty = (height - f.Size * 1.2f) / 2f;
                    g.DrawString("Claude", f, Brushes.White, pad, ty, StringFormat.GenericTypographic);
                    DrawBubble(g, new RectangleF(pad + tw + gap, height * 0.22f, bw, height * 0.56f), height >= 24);
                }
                return bmp;
            }
        }

        // סמל מרובע (אייקון תוכנה, שורת משימות, כותרת חלון): ריבוע כתום עם "Claude" או "C" בלבן, ובועת שיחה בפינה
        public static Bitmap MakeBitmap(int size)
        {
            var bmp = new Bitmap(size, size);
            using (var g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAlias;
                g.Clear(Color.Transparent);
                using (var path = Theme.RoundRect(new RectangleF(0.5f, 0.5f, size - 1, size - 1), size * 0.24f))
                using (var b = new SolidBrush(Orange))
                    g.FillPath(b, path);
                bool full = size >= 48;   // רק בגדלים גדולים יש מקום למילה המלאה
                if (full)
                {
                    using (var f = new Font("Segoe UI", size * 0.2f, FontStyle.Bold, GraphicsUnit.Pixel))
                    using (var sf = new StringFormat(StringFormat.GenericTypographic) { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
                        g.DrawString("Claude", f, Brushes.White, new RectangleF(0, size * 0.36f, size, size * 0.5f), sf);
                    float bs = size * 0.3f;
                    DrawBubble(g, new RectangleF(size - bs - size * 0.1f, size * 0.08f, bs, bs * 0.9f), true);
                }
                else
                {
                    // קטן (שורת משימות, כותרת חלון): C גיאומטרית עבה בלבן, ונקודת התראה לבנה בפינה
                    float cx = size * 0.46f, cy = size * 0.54f, R = size * 0.26f;
                    using (var pen = new Pen(Color.White, size * 0.17f) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                        g.DrawArc(pen, cx - R, cy - R, R * 2, R * 2, 38, 284);
                    // בועת שיחה קטנה בפינה (עם מסגרת כתומה שמפרידה אותה מה-C)
                    float bw = size * 0.44f, bh = size * 0.40f, bx = size * 0.52f, by = size * 0.04f, o = size * 0.05f;
                    DrawBubble(g, new RectangleF(bx - o, by - o, bw + o * 2, bh + o * 2 + 0.5f), false, Orange);
                    DrawBubble(g, new RectangleF(bx, by, bw, bh), false, Color.White);
                }
            }
            return bmp;
        }

        // קובץ ICO עם כמה גדלים (פורמט BMP בן 32 סיביות, שתואם גם ל-csc הישן). Icon.Save של סמל שנוצר מ-GetHicon מאבד את הצבעים
        public static void WriteIco(Stream fs, int[] sizes)
        {
            var bw = new BinaryWriter(fs);
            var datas = new List<byte[]>();
            foreach (int size in sizes)
                using (var bmp = MakeBitmap(size)) datas.Add(Dib(bmp));
            bw.Write((short)0); bw.Write((short)1); bw.Write((short)sizes.Length);
            int offset = 6 + 16 * sizes.Length;
            for (int i = 0; i < sizes.Length; i++)
            {
                bw.Write((byte)sizes[i]); bw.Write((byte)sizes[i]); bw.Write((byte)0); bw.Write((byte)0);
                bw.Write((short)1); bw.Write((short)32); bw.Write(datas[i].Length); bw.Write(offset);
                offset += datas[i].Length;
            }
            foreach (var d in datas) bw.Write(d);
            bw.Flush();
        }

        static byte[] Dib(Bitmap b)
        {
            int w = b.Width, h = b.Height;
            var ms = new MemoryStream();
            var bw = new BinaryWriter(ms);
            bw.Write(40); bw.Write(w); bw.Write(h * 2); bw.Write((short)1); bw.Write((short)32);
            bw.Write(0); bw.Write(0); bw.Write(0); bw.Write(0); bw.Write(0); bw.Write(0);
            for (int y = h - 1; y >= 0; y--)
                for (int x = 0; x < w; x++)
                {
                    Color c = b.GetPixel(x, y);
                    bw.Write(c.B); bw.Write(c.G); bw.Write(c.R); bw.Write(c.A);
                }
            bw.Write(new byte[((w + 31) / 32) * 4 * h]);
            return ms.ToArray();
        }

        static Icon appIcon;

        // סמל התוכנה בכמה גדלים: Windows בוחר לבד את המתאים (שורת משימות, כותרת חלון, אייקון ליד השעון)
        public static Icon AppIcon
        {
            get
            {
                if (appIcon == null)
                {
                    var ms = new MemoryStream();
                    WriteIco(ms, new[] { 16, 20, 24, 32, 40, 48, 64 });
                    ms.Position = 0;
                    appIcon = new Icon(ms);
                }
                return appIcon;
            }
        }

        public static Icon MakeIcon(int size)
        {
            return Icon.FromHandle(MakeBitmap(size).GetHicon());
        }
    }

    // ---------------------------------------------------------------- Theme
    static class ThemeUtil
    {
        // קורא את ערכת הנושא (בהירה/כהה) שהמשתמש בחר באפליקציות Windows - משמש כגיבוי כשלא מצליחים
        // לזהות את ערכת הנושא של VS Code/Cursor עצמם
        public static bool IsLightTheme()
        {
            try
            {
                using (var k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize"))
                {
                    object v = k != null ? k.GetValue("AppsUseLightTheme") : null;
                    if (v is int) return (int)v != 0;
                }
            }
            catch { }
            return true;
        }
    }

    // מזהה אם ערכת הנושא שנבחרה בפועל ב-VS Code/Cursor (ולא ב-Windows) היא בהירה או כהה, כדי
    // שחלון ההתראה ייראה עקבי עם העורך. קורא קודם את settings.json של הסביבה (עם תמיכה ב-JSONC:
    // הערות ופסיקים עודפים), ומנחש בהיר/כהה לפי שם ערכת הנושא. כשלא ניתן לקבוע - נופל חזרה לערכת
    // הנושא של Windows.
    static class VSCodeTheme
    {
        public static bool IsLightTheme(long handle, string cwd)
        {
            try
            {
                string procName = handle != 0 ? WinApi.ProcessNameOf(handle) : null;
                if (procName == null) return ThemeUtil.IsLightTheme();

                string label = null;
                if (!string.IsNullOrEmpty(cwd))
                    label = ReadColorThemeLabel(Path.Combine(cwd, ".vscode", "settings.json"));
                if (label == null)
                {
                    string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                    label = ReadColorThemeLabel(Path.Combine(appData, procName, "User", "settings.json"));
                }
                if (label == null) return ThemeUtil.IsLightTheme();

                bool? byName = ClassifyByName(label);
                return byName ?? ThemeUtil.IsLightTheme();
            }
            catch { return true; }
        }

        static string ReadColorThemeLabel(string settingsPath)
        {
            try
            {
                if (!File.Exists(settingsPath)) return null;
                string json = StripJsonComments(File.ReadAllText(settingsPath, Encoding.UTF8));
                var d = new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(json);
                object v;
                if (d != null && d.TryGetValue("workbench.colorTheme", out v) && v != null) return v.ToString();
            }
            catch { }
            return null;
        }

        static bool? ClassifyByName(string label)
        {
            string l = label.ToLowerInvariant();
            if (l.Contains("high contrast light")) return true;
            if (l.Contains("light")) return true;
            if (l.Contains("dark")) return false;
            if (l.Contains("high contrast") || l.Contains("contrast black")) return false;
            return null;
        }

        // settings.json של VS Code הוא JSONC (תומך בהערות // ו-/* */ ובפסיק עודף לפני } או ]),
        // מה שה-JSON הסטנדרטי לא יודע לפרש
        static string StripJsonComments(string json)
        {
            var sb = new StringBuilder();
            bool inStr = false, inLine = false, inBlock = false;
            for (int i = 0; i < json.Length; i++)
            {
                char c = json[i];
                char next = i + 1 < json.Length ? json[i + 1] : '\0';
                if (inLine) { if (c == '\n') { inLine = false; sb.Append(c); } continue; }
                if (inBlock) { if (c == '*' && next == '/') { inBlock = false; i++; } continue; }
                if (inStr)
                {
                    sb.Append(c);
                    if (c == '\\' && i + 1 < json.Length) sb.Append(json[++i]);
                    else if (c == '"') inStr = false;
                    continue;
                }
                if (c == '"') { inStr = true; sb.Append(c); continue; }
                if (c == '/' && next == '/') { inLine = true; i++; continue; }
                if (c == '/' && next == '*') { inBlock = true; i++; continue; }
                sb.Append(c);
            }
            return Regex.Replace(sb.ToString(), @",(\s*[}\]])", "$1");
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
        public string Language { get; set; }  // "he" או "en"

        public Config()
        {
            DisplaySeconds = 8; UseCustomPopup = true; Language = "he";
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
                        if (c.Language != "en") c.Language = "he";
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
        [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool fAttach);
        [DllImport("kernel32.dll")] static extern uint GetCurrentThreadId();
        [DllImport("user32.dll")] static extern bool BringWindowToTop(IntPtr h);
        [DllImport("user32.dll")] static extern void keybd_event(byte vk, byte scan, uint flags, UIntPtr extra);

        public static long Foreground() { return GetForegroundWindow().ToInt64(); }

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

        // מעלה חלון לחזית ומוודא שזה באמת קרה. הערך שמחזיר SetForegroundWindow לא אמין (הוא יכול להחזיר True
        // והחלון בפועל לא עלה), ולכן בודקים בעצמנו מי החלון הקדמי ומנסים שוב בשיטה חזקה יותר
        public static bool Focus(long handle)
        {
            IntPtr h = new IntPtr(handle);
            ShowWindow(h, IsIconic(h) ? 9 : 5);
            for (int attempt = 0; attempt < 3; attempt++)
            {
                if (GetForegroundWindow() == h) { Log.Write("focus: ok (attempt " + attempt + ")"); return true; }
                TryForeground(h, attempt > 0);
                if (GetForegroundWindow() == h) { Log.Write("focus: ok (attempt " + attempt + ")"); return true; }
                Thread.Sleep(80);
            }
            Log.Write("focus: FAILED, foreground is " + GetForegroundWindow().ToInt64() + " instead of " + handle);
            return false;
        }

        // Windows חוסם SetForegroundWindow לתהליך שאינו בחזית - וזה כמעט תמיד המצב כאן, כי האפליקציה רצה
        // ברקע וחלון ההתראה לא מקבל פוקוס (NOACTIVATE). העוקף: לחבר את תור הקלט שלנו לתור של החלון הקדמי
        // ושל חלון היעד, ובניסיון חוזר גם "ללחוץ" על מקש חסר משמעות (F24) כדי שנחשב כמי שקיבל קלט
        static void TryForeground(IntPtr h, bool keyTrick)
        {
            uint pid;
            uint cur = GetCurrentThreadId();
            IntPtr fg = GetForegroundWindow();
            uint fgThread = fg != IntPtr.Zero ? GetWindowThreadProcessId(fg, out pid) : 0;
            uint tgtThread = GetWindowThreadProcessId(h, out pid);
            bool a1 = fgThread != 0 && fgThread != cur && AttachThreadInput(cur, fgThread, true);
            bool a2 = tgtThread != 0 && tgtThread != cur && tgtThread != fgThread && AttachThreadInput(cur, tgtThread, true);
            try
            {
                if (keyTrick) { keybd_event(0x87, 0, 0, UIntPtr.Zero); keybd_event(0x87, 0, 2, UIntPtr.Zero); }
                BringWindowToTop(h);
                SetForegroundWindow(h);
            }
            finally
            {
                if (a1) AttachThreadInput(cur, fgThread, false);
                if (a2) AttachThreadInput(cur, tgtThread, false);
            }
        }

        // מעלה את החלון ואז פותח בו את השיחה. ה-URI מגיע לחלון ה-VS שהיה אחרון בפוקוס, ולכן קודם מעלים את החלון
        // ומחכים שהוא אכן יהפוך לחלון הקדמי בפועל (ולא רק מניחים שעברו מספיק מילישניות)
        public static void FocusSession(long handle, string session)
        {
            if (handle == 0) return;
            Focus(handle);
            if (string.IsNullOrEmpty(session) || !Regex.IsMatch(session, "^[0-9a-fA-F-]{8,64}$")) return;
            string scheme = SchemeFor(handle);
            if (scheme == null) return;
            IntPtr h = new IntPtr(handle);
            ThreadPool.QueueUserWorkItem(delegate
            {
                try
                {
                    int waited = 0;
                    while (GetForegroundWindow() != h && waited < 2000) { Thread.Sleep(25); waited += 25; }
                    bool reallyForeground = GetForegroundWindow() == h;
                    Log.Write("focus session: waited=" + waited + "ms foreground=" + reallyForeground + " session=" + session);
                    // ה-URI מנותב לחלון שהיה אחרון בפוקוס. אם החלון שלנו לא הגיע לחזית, שליחת ה-URI
                    // תפתח את השיחה בחלון אחר (או תיצור חלון חדש) - עדיף לא לשלוח כלל
                    if (!reallyForeground) return;
                    Thread.Sleep(400); // ל-VS Code לוקח רגע לרשום שהחלון הזה הוא האחרון בפוקוס
                    if (GetForegroundWindow() != h) { Log.Write("focus session: lost foreground, not opening"); return; }
                    var psi = new ProcessStartInfo(scheme + "://anthropic.claude-code/open?session=" + session);
                    psi.UseShellExecute = true;
                    Process.Start(psi);
                }
                catch (Exception ex) { Log.Write("open session error: " + ex.Message); }
            });
        }

        // כותרות בעברית מגיעות עם תווי כיווניות בלתי נראים (RLM, LRE/RLE/PDF וכו') שמונעים השוואה
        public static string CleanTitle(string t)
        {
            return Regex.Replace(t ?? "", "[‎‏‪-‮⁦-⁩]", "");
        }

        public static string SchemeFor(long handle)
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

        // שם תהליך החלון - משמש גם לאיתור תיקיית הנתונים של VS Code/Cursor (זהה לשם התהליך)
        public static string ProcessNameOf(long handle)
        {
            try
            {
                uint pid; GetWindowThreadProcessId(new IntPtr(handle), out pid);
                return Process.GetProcessById((int)pid).ProcessName;
            }
            catch { return null; }
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

        // כותרת השיחה כפי ש-Claude Code שומר אותה בתמלול (השורה האחרונה מנצחת). ריק אם אין
        public static string TitleFromTranscript(string path)
        {
            string title = "";
            try
            {
                var ser = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };
                foreach (var line in File.ReadLines(path, Encoding.UTF8))
                {
                    bool ai = line.IndexOf("\"ai-title\"", StringComparison.Ordinal) >= 0;
                    bool custom = line.IndexOf("\"custom-title\"", StringComparison.Ordinal) >= 0;
                    if (!ai && !custom) continue;
                    try
                    {
                        var o = ser.Deserialize<Dictionary<string, object>>(line);
                        string t = Str(o, ai ? "aiTitle" : "customTitle");
                        if (t.Length == 0 && custom) t = Str(o, "title");
                        if (t.Trim().Length > 0) title = t.Trim();
                    }
                    catch { }
                }
            }
            catch { }
            return title;
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
            if (text.Length == 0) return L.T("קלוד סיים להשיב לך", "Claude finished replying");
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
            string input = text.Length > 6000 ? text.Substring(0, 6000) : text;
            string prompt = "סכם את ההודעה הבאה בעברית בעד " + Math.Max(5, maxWords - 5) +
                " מילים, בניסוח קצר וברור. החזר רק את הסיכום, בלי הקדמה ובלי סימוני עיצוב.\n\n--- ההודעה ---\n" + input;
            string err;
            string s = Ask(prompt, 40000, out err);
            return string.IsNullOrEmpty(s) ? null : s;
        }

        // שולח prompt ל-claude -p ומחזיר את התשובה. בכל כשל מחזיר null, ובמשתנה error מופיע הסבר קצר
        public static string Ask(string prompt, int timeoutMs, out string error)
        {
            error = null;
            try
            {
                string exe = FindClaude();
                if (exe == null) { Log.Write("summary: claude not found"); error = "Claude Code לא נמצא במחשב"; return null; }
                string args = "-p --model haiku --no-session-persistence --strict-mcp-config --setting-sources local --disable-slash-commands --mcp-config \"{\\\"mcpServers\\\":{}}\"";
                var psi = new ProcessStartInfo();
                if (exe.EndsWith(".cmd", StringComparison.OrdinalIgnoreCase)) { psi.FileName = "cmd.exe"; psi.Arguments = "/c \"" + exe + "\" " + args; }
                else { psi.FileName = exe; psi.Arguments = args; }
                psi.UseShellExecute = false;
                psi.CreateNoWindow = true;
                psi.WorkingDirectory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                psi.RedirectStandardInput = true;
                psi.RedirectStandardOutput = true;
                psi.RedirectStandardError = true;
                psi.StandardOutputEncoding = Encoding.UTF8;
                psi.EnvironmentVariables["CLAUDE_NOTIFY_CHILD"] = "1";
                using (var p = Process.Start(psi))
                {
                    var bytes = new UTF8Encoding(false).GetBytes(prompt);
                    p.StandardInput.BaseStream.Write(bytes, 0, bytes.Length);
                    p.StandardInput.Close();
                    var outTask = p.StandardOutput.ReadToEndAsync();
                    var errTask = p.StandardError.ReadToEndAsync();
                    if (!p.WaitForExit(timeoutMs)) { try { p.Kill(); } catch { } Log.Write("summary: timeout"); error = "הזמן הקצוב עבר"; return null; }
                    if (p.ExitCode != 0)
                    {
                        // claude מדווח על הסיבה (לא מחובר, מכסה מלאה...) בפלט, לכן מצרפים אותה להודעת השגיאה וללוג
                        string detail = ((errTask.Result ?? "").Trim() + " " + (outTask.Result ?? "").Trim()).Trim();
                        if (detail.Length > 200) detail = detail.Substring(0, 200);
                        Log.Write("summary: exit " + p.ExitCode + " " + detail);
                        error = "claude החזיר שגיאה (קוד " + p.ExitCode + ")" + (detail.Length > 0 ? ": " + detail : "");
                        return null;
                    }
                    return outTask.Result.Trim();
                }
            }
            catch (Exception ex) { Log.Write("summary error: " + ex.Message); error = ex.Message; return null; }
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

        internal static Dictionary<string, object> ReadSettings()
        {
            var ser = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };
            if (File.Exists(SettingsPath))
            {
                string t = File.ReadAllText(SettingsPath, Encoding.UTF8);
                if (t.Trim().Length > 0) return ser.Deserialize<Dictionary<string, object>>(t);
            }
            return new Dictionary<string, object>();
        }

        internal static void WriteSettings(Dictionary<string, object> root)
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
        readonly ScheduleRunner sched;
        ScheduleForm schedForm;
        UsageForm usageForm;
        long lastTarget;
        string lastSession = "";

        readonly bool runOnce;

        public TrayContext(bool hidden, bool runSchedule = false)
        {
            runOnce = runSchedule;
            cfg = Config.Load();
            host = new Form();
            var handle = host.Handle; // יוצר handle כדי לאפשר BeginInvoke מ-threads אחרים
            sched = new ScheduleRunner(host);
            sched.Started += UpdateTrayText;
            sched.Finished += OnSchedFinished;

            tray = new NotifyIcon();
            tray.Icon = new Icon(Branding.AppIcon, SystemInformation.SmallIconSize);
            tray.Text = "Claude Notify";
            enabledItem = new ToolStripMenuItem();
            enabledItem.Click += delegate { cfg.Enabled = !cfg.Enabled; cfg.Save(); SyncEnabled(); };
            BuildMenu();
            tray.MouseDoubleClick += delegate(object s, MouseEventArgs e) { if (e.Button == MouseButtons.Left) ShowSettings(); };
            tray.BalloonTipClicked += delegate { if (lastTarget != 0) WinApi.FocusSession(lastTarget, lastSession); };
            tray.Visible = true;
            SyncEnabled();

            var t = new Thread(PipeLoop);
            t.IsBackground = true;
            t.Start();

            if (!hidden) ShowSettings();
            if (runSchedule)
            {
                // הופעלנו ע"י משימת Windows בשעת השליחה: שולחים מיד לפי ההגדרות האחרונות, ואחרי ההצלחה נסגרים
                string err = sched.Start(SchedConfig.Load(), true);
                if (err != null) { Log.Write("run-schedule: " + err); tray.Visible = false; Environment.Exit(1); }
            }
        }

        // בונה את תפריט האייקון (גם מחדש, אחרי החלפת שפה)
        void BuildMenu()
        {
            var menu = new ContextMenuStrip();
            menu.RightToLeft = L.He ? RightToLeft.Yes : RightToLeft.No;
            
            menu.Items.Add(L.T("הגדרות...", "Settings..."), null, delegate { ShowSettings(); });
            enabledItem.Text = L.T("התראות פעילות", "Notifications on");
            menu.Items.Add(enabledItem);
            menu.Items.Add(L.T("התראת בדיקה", "Test notification"), null, delegate { TestNotification(); });
            menu.Items.Add(L.T("הודעה מתוזמנת (פתיחת מכסה)...", "Scheduled message (start quota)..."), null, delegate { ShowSchedule(); });
            menu.Items.Add(L.T("מידע טכני...", "Technical info..."), null, delegate { ShowUsage(); });
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(L.T("יציאה", "Exit"), null, delegate { sched.Cancel(); tray.Visible = false; Application.Exit(); });
            MenuStyle.Apply(menu);
            tray.ContextMenuStrip = menu;
        }

        void SyncEnabled()
        {
            enabledItem.Checked = cfg.Enabled;
            UpdateTrayText();
            if (settings != null) settings.Reload();
        }

        void UpdateTrayText()
        {
            string t = cfg.Enabled ? "Claude Notify - " + L.T("פעיל", "on") : "Claude Notify - " + L.T("כבוי", "off");
            if (sched != null && sched.Running) t += " | " + L.T("מתוזמן ", "scheduled ") + sched.Target.ToString("HH:mm");
            tray.Text = t;
        }

        void OnSchedFinished(string text, bool error)
        {
            UpdateTrayText();
            if (sched.Cancelled) return;
            tray.ShowBalloonTip(5000, "Claude Notify", L.Tr(text), error ? ToolTipIcon.Warning : ToolTipIcon.Info);
            if (error) ShowSchedule();
            else if (runOnce)
            {
                var t = new System.Windows.Forms.Timer { Interval = 8000 };
                t.Tick += delegate { t.Stop(); tray.Visible = false; Application.Exit(); };
                t.Start();
            }
        }

        public void ShowUsage()
        {
            if (usageForm == null || usageForm.IsDisposed) usageForm = new UsageForm();
            usageForm.Reload();
            usageForm.Show();
            usageForm.WindowState = FormWindowState.Normal;
            usageForm.Activate();
        }

        public void ShowSchedule()
        {
            if (schedForm == null || schedForm.IsDisposed) schedForm = new ScheduleForm(sched);
            schedForm.Reload();
            schedForm.Show();
            schedForm.WindowState = FormWindowState.Normal;
            schedForm.Activate();
        }

        public void ConfigChanged(Config c)
        {
            bool langChanged = (c.Language == "en") == L.He;
            cfg = c;
            L.Load(c.Language);
            SyncEnabled();
            if (!langChanged) return;
            // החלפת שפה: בונים מחדש את התפריט והחלונות, ופותחים שוב את ההגדרות
            host.BeginInvoke(new Action(delegate
            {
                BuildMenu();
                UpdateTrayText();
                if (settings != null) { settings.Dispose(); settings = null; }
                if (schedForm != null) { schedForm.Dispose(); schedForm = null; }
                if (usageForm != null) { usageForm.Dispose(); usageForm = null; }
                ShowSettings();
            }));
        }

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
            Show(0, L.T("זו התראת בדיקה מ-Claude Notify. אם אתה רואה אותה, הכול עובד.", "This is a test notification from Claude Notify. If you can see it, everything works."), "", "", L.T("התראת בדיקה", "Test notification"));
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
                if (TextUtil.Str(j, "__cmd") == "schedule") ShowSchedule();
                if (TextUtil.Str(j, "__cmd") == "usage") ShowUsage();
                return;
            }
            if (!cfg.Enabled) { Log.Write("skipped: disabled"); return; }

            string cwd = TextUtil.Str(j, "cwd");
            string session = TextUtil.Str(j, "session_id");
            string text = TextUtil.Str(j, "last_assistant_message");
            string title = "";
            string tp = TextUtil.Str(j, "transcript_path");
            if (tp.Length > 0 && File.Exists(tp))
            {
                title = TextUtil.TitleFromTranscript(tp);
                if (text.Trim().Length == 0) text = TextUtil.FromTranscript(tp);
            }

            // קודם מנסים לזהות את החלון לפי כותרת השיחה (היא מופיעה בתוך הפאנל של התוסף ורק בחלון שבו השיחה פתוחה),
            // וזה מבחין גם בין כמה חלונות בלי תיקייה. אם אין כותרת או שלא נמצאה - נופלים לזיהוי לפי כותרת החלון
            long target = title.Length > 0 ? FindByConversationTitle(title) : 0;
            if (target != 0) Log.Write("find: matched window by conversation title [" + title + "]");
            else target = FindTarget(cwd);
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
                        Show(target, TextUtil.FirstWords(s ?? src, max), session, cwd, title);
                    }));
                });
            }
            else Show(target, TextUtil.FirstWords(text, max), session, cwd, title);
        }

        // UI Automation: מחפש בכל חלון עורך רכיב ששמו בדיוק כותרת השיחה. רץ ב-PowerShell כדי לא להוסיף תלות בבנייה
        const string TitleSearchPs = @"
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes
$title = [System.Text.Encoding]::UTF8.GetString([System.Convert]::FromBase64String('@TITLE@'))
$cond = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::NameProperty, $title)
foreach ($h in @(@HANDLES@)) {
    try {
        $el = [System.Windows.Automation.AutomationElement]::FromHandle([IntPtr]$h)
        if ($el.FindFirst('Descendants', $cond) -ne $null) { 'HIT ' + $h; exit }
    } catch { }
}
";

        static long FindByConversationTitle(string title)
        {
            try
            {
                var wins = WinApi.Find(Config.Load().ProcessList());
                if (wins.Count == 0) return 0;
                var handles = new List<string>();
                foreach (var w in wins) handles.Add(w.Handle.ToString());
                string script = TitleSearchPs
                    .Replace("@TITLE@", Convert.ToBase64String(Encoding.UTF8.GetBytes(title)))
                    .Replace("@HANDLES@", string.Join(",", handles.ToArray()));
                string enc = Convert.ToBase64String(Encoding.Unicode.GetBytes(script));
                var psi = new ProcessStartInfo("powershell", "-NoProfile -NonInteractive -EncodedCommand " + enc);
                psi.UseShellExecute = false;
                psi.CreateNoWindow = true;
                psi.RedirectStandardOutput = true;
                psi.RedirectStandardError = true;
                using (var p = Process.Start(psi))
                {
                    var outTask = p.StandardOutput.ReadToEndAsync();
                    p.StandardError.ReadToEndAsync();
                    if (!p.WaitForExit(8000)) { try { p.Kill(); } catch { } Log.Write("find by title: timeout"); return 0; }
                    string o = outTask.Result.Trim();
                    if (o.StartsWith("HIT ")) { long h; if (long.TryParse(o.Substring(4).Trim(), out h)) return h; }
                }
            }
            catch (Exception ex) { Log.Write("find by title error: " + ex.Message); }
            return 0;
        }

        long FindTarget(string cwd)
        {
            try
            {
                var wins = WinApi.Find(cfg.ProcessList());
                // כותרת חלון ב-VS Code: "קובץ - תיקיית-עבודה - Visual Studio Code". מתאימים לפי קטע שלם
                // בכותרת (ולא תת-מחרוזת), קודם לתיקיית ה-cwd ואז לתיקיות האב שלה (ה-cwd יכול להיות
                // תת-תיקייה של תיקיית העבודה שנפתחה בעורך)
                string dir = cwd.Trim().TrimEnd('\\', '/');
                for (int level = 0; level < 4 && dir.Length > 0; level++)
                {
                    string leaf = Path.GetFileName(dir);
                    if (leaf.Length == 0) break;
                    foreach (var w in wins)
                    {
                        // כותרות בעברית מגיעות עם תווי כיווניות בלתי נראים (RLM, LRE/RLE/PDF וכו') שמונעים השוואה
                        string title = Regex.Replace(w.Title, "[‎‏‪-‮⁦-⁩]", "");
                        foreach (var seg in title.Split(new[] { " - " }, StringSplitOptions.None))
                            if (seg.Trim().Equals(leaf, StringComparison.OrdinalIgnoreCase)) return w.Handle;
                    }
                    dir = Path.GetDirectoryName(dir) ?? "";
                }
                // אין התאמה מדויקת: מנחשים רק כשיש חלון אחד. עם כמה חלונות עדיף לא לבחור חלון אקראי -
                // אחרת הלחיצה מעלה את החלון הלא נכון ופותחת בו שיחה
                if (wins.Count == 1) return wins[0].Handle;
                // חלון בלי תיקייה פתוחה (הכותרת היא רק שם האפליקציה) מתאים ל-cwd שלא תואם אף תיקייה
                WinInfo bare = null; int bareCount = 0;
                foreach (var w in wins)
                {
                    string t = Regex.Replace(w.Title, "[‎‏‪-‮⁦-⁩]", "");
                    if (t.IndexOf(" - ", StringComparison.Ordinal) < 0) { bare = w; bareCount++; }
                }
                if (bareCount == 1) return bare.Handle;
                // כמה חלונות בלי תיקייה (למשל Claude שרץ בתיקיית הבית): אי אפשר לדעת מי מהם. EnumWindows מחזיר לפי סדר
                // Z, כלומר החלון שהמשתמש עבד בו לאחרונה ראשון - זה הניחוש הטוב ביותר, והוא עדיף על חוסר יעד בכלל
                // (בלי יעד ההתראה תמיד קופצת, והלחיצה עליה לא עושה כלום)
                if (bareCount > 1 && bare != null)
                {
                    WinInfo topBare = null;
                    foreach (var w in wins)
                    {
                        string t2 = Regex.Replace(w.Title, "[‎‏‪-‮⁦-⁩]", "");
                        if (t2.IndexOf(" - ", StringComparison.Ordinal) < 0) { topBare = w; break; }
                    }
                    if (topBare != null) { Log.Write("find: guessing topmost folderless window for cwd=" + cwd); return topBare.Handle; }
                }
                Log.Write("find: no unambiguous window for cwd=" + cwd + " among " + wins.Count);
                foreach (var w in wins) Log.Write("find: window title=[" + w.Title + "]");
            }
            catch (Exception ex) { Log.Write("find error: " + ex.Message); }
            return 0;
        }

        ToastForm toast;

        void Show(long target, string body, string session, string cwd, string title)
        {
            lastTarget = target;
            lastSession = session;
            if (body.Length > 250) body = body.Substring(0, 247) + "...";
            if (cfg.UseCustomPopup)
            {
                if (toast != null && !toast.IsDisposed) toast.Close();
                toast = new ToastForm(body, title, target, session, cwd, cfg.DisplaySeconds, cfg.PopupLeft);
                toast.Show();
            }
            else tray.ShowBalloonTip(cfg.DisplaySeconds * 1000, string.IsNullOrEmpty(title) ? "Claude Code" : title, body, ToolTipIcon.None);
        }
    }

    // ---------------------------------------------------------------- Popup notification
    class ToastForm : Form
    {
        readonly System.Windows.Forms.Timer timer = new System.Windows.Forms.Timer();
        readonly long target;
        readonly string session;
        readonly Color borderColor = Color.FromArgb(217, 119, 87);
        int remainingMs;

        public ToastForm(string body, string heading, long target, string session, string cwd, int seconds, bool left)
        {
            this.target = target;
            this.session = session;
            remainingMs = seconds * 1000;
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            StartPosition = FormStartPosition.Manual;
            bool light = VSCodeTheme.IsLightTheme(target, cwd);
            Color bg = light ? Color.White : Color.FromArgb(40, 40, 44);
            Color fg = light ? Color.FromArgb(30, 30, 30) : Color.White;
            Color closeColor = light ? Color.Gray : Color.Silver;
            BackColor = bg;
            Cursor = target != 0 ? Cursors.Hand : Cursors.Default;
            bool rtl = L.He;

            int width = Theme.S(380), inner = Theme.S(340);
            var titleFont = new Font("Segoe UI", 10f, FontStyle.Bold);
            var bodyFont = new Font("Segoe UI", 10.5f);
            int textH = TextRenderer.MeasureText(body, bodyFont, new Size(inner, 0),
                TextFormatFlags.WordBreak | (rtl ? TextFormatFlags.RightToLeft : TextFormatFlags.Left) | TextFormatFlags.TextBoxControl).Height + 4;

            var title = new Label { Text = string.IsNullOrEmpty(heading) ? "Claude Code" : heading, AutoEllipsis = true, Font = titleFont, ForeColor = borderColor, AutoSize = false, Size = new Size(inner, Theme.S(22)), Location = new Point(Theme.S(20), Theme.S(10)), TextAlign = ContentAlignment.TopLeft, RightToLeft = rtl ? RightToLeft.Yes : RightToLeft.No };
            var lbl = new Label { Text = body, Font = bodyFont, ForeColor = fg, AutoSize = false, Size = new Size(inner, textH), Location = new Point(Theme.S(20), Theme.S(36)), TextAlign = ContentAlignment.TopLeft, RightToLeft = rtl ? RightToLeft.Yes : RightToLeft.No };
            var close = new Label { Text = "✕", Font = new Font("Segoe UI", 9f), ForeColor = closeColor, AutoSize = true, Location = new Point(rtl ? Theme.S(4) : width - Theme.S(22), Theme.S(4)), Cursor = Cursors.Hand };
            close.Click += delegate { Close(); };
            Controls.Add(close);
            Controls.Add(title);
            Controls.Add(lbl);
            Size = new Size(width, Theme.S(36) + textH + Theme.S(14));

            EventHandler click = delegate { WinApi.FocusSession(this.target, this.session); Close(); };
            Click += click; title.Click += click; lbl.Click += click;

            var wa = Screen.PrimaryScreen.WorkingArea;
            Location = new Point(left ? wa.Left + Theme.S(16) : wa.Right - Width - Theme.S(16), wa.Bottom - Height - Theme.S(16));

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
            using (var p = new Pen(borderColor))
                e.Graphics.DrawRectangle(p, 0, 0, Width - 1, Height - 1);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) timer.Dispose();
            base.Dispose(disposing);
        }
    }
}

