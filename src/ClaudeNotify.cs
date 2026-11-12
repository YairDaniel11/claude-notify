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

            // קריסות: רושמים ללוג את השגיאה המלאה
            AppDomain.CurrentDomain.UnhandledException += delegate(object s, UnhandledExceptionEventArgs e) { Log.Write("CRASH (unhandled): " + e.ExceptionObject); };
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            Application.ThreadException += delegate(object s, ThreadExceptionEventArgs e) { Log.Write("ERROR (ui thread): " + e.Exception); };

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
        public bool VoiceEnabled { get; set; }   // קריאה בקול כשהמשתמש לא ליד המחשב
        public string UserName { get; set; }     // השם שבו קוראים למשתמש (מוזן על ידו)
        public string VoiceHotkey { get; set; }  // למשל Ctrl+Alt+V
        public int VoiceRepeats { get; set; }    // כמה פעמים לקרוא
        public bool VoiceShowWindow { get; set; } // להציג את חלון ההאזנה (ומה שנשמע) או להסתיר
        public int VoiceIdleSeconds { get; set; } // קורא רק אם לא נגעתי במחשב כל כך הרבה שניות (0 = תמיד)
        public string VoicePos { get; set; }     // מיקום חלון ההאזנה: br, bl, tr, tl, center
        public string VoiceLang { get; set; }    // שפת הדיבור: auto = לפי שפת הממשק, he, en
        public string VoiceGender { get; set; }  // "male" או "female"
        public double VoiceRate { get; set; }    // 1.0 = רגיל
        public bool VoiceAnnounce { get; set; }  // להודיע בקול על הפעלה וכיבוי של הקריאה
        public bool VoiceDictate { get; set; }   // הכתבה ל-Claude אחרי שאמרתי שאני מגיע
        public bool VoiceNeedSend { get; set; }  // לשלוח רק אחרי שאמרתי "שלח"
        public bool VoiceLowMem { get; set; }    // לסגור את הדף כשהקריאה כבויה
        public bool VoiceNikud { get; set; }     // ניקוד אוטומטי של הטקסט לפני הקראה (דרך Claude)
        public bool VoiceNatural { get; set; }   // קול טבעי של Edge (דורש אינטרנט)
        public bool VoiceListen { get; set; }    // האזנה לתשובה בקול
        public bool CheckUpdates { get; set; }   // התראה כשיש גרסה חדשה ב-GitHub

        public Config()
        {
            VoiceEnabled = false; UserName = ""; VoiceRepeats = 3; VoiceListen = true; VoiceNatural = true; VoiceGender = "male"; VoiceLang = "auto"; VoicePos = "br"; VoiceIdleSeconds = 60; VoiceRate = 1.0; VoiceAnnounce = true; VoiceNikud = true; VoiceDictate = false; VoiceNeedSend = true; VoiceLowMem = false; VoiceHotkey = "Ctrl+Alt+V";
            DisplaySeconds = 8; UseCustomPopup = true; Language = "he"; CheckUpdates = true;
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
                        if (c.VoiceRepeats < 1 || c.VoiceRepeats > 10) c.VoiceRepeats = 3;
                        if (c.VoiceGender != "female") c.VoiceGender = "male";
                        if (c.VoiceLang != "he" && c.VoiceLang != "en") c.VoiceLang = "auto";
                        if (c.VoicePos != "bl" && c.VoicePos != "tr" && c.VoicePos != "tl" && c.VoicePos != "center") c.VoicePos = "br";
                        if (c.VoiceIdleSeconds < 0 || c.VoiceIdleSeconds > 600) c.VoiceIdleSeconds = 60;
                        if (c.VoiceRate < 0.5 || c.VoiceRate > 2.0) c.VoiceRate = 1.0;
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

    // חלון נסתר שמאזין לקיצור המקשים הגלובלי
    class HotkeyForm : Form
    {
        const int WM_HOTKEY = 0x0312, Id = 0xC1A1;
        public event Action Pressed;

        [System.Runtime.InteropServices.DllImport("user32.dll")] static extern bool RegisterHotKey(IntPtr h, int id, uint mod, uint vk);
        [System.Runtime.InteropServices.DllImport("user32.dll")] static extern bool UnregisterHotKey(IntPtr h, int id);

        public static bool TryParse(string s, out uint mod, out uint vk)
        {
            mod = 0; vk = 0;
            if (string.IsNullOrEmpty(s)) return false;
            foreach (var part in s.Split('+'))
            {
                string p = part.Trim();
                if (p.Equals("Ctrl", StringComparison.OrdinalIgnoreCase)) mod |= 2;
                else if (p.Equals("Alt", StringComparison.OrdinalIgnoreCase)) mod |= 1;
                else if (p.Equals("Shift", StringComparison.OrdinalIgnoreCase)) mod |= 4;
                else if (p.Equals("Win", StringComparison.OrdinalIgnoreCase)) mod |= 8;
                else
                {
                    try { vk = (uint)(Keys)Enum.Parse(typeof(Keys), p, true); } catch { return false; }
                }
            }
            return vk != 0 && mod != 0;
        }

        public bool Register(string hotkey)
        {
            UnregisterHotKey(Handle, Id);
            uint mod, vk;
            if (!TryParse(hotkey, out mod, out vk)) return false;
            return RegisterHotKey(Handle, Id, mod | 0x4000, vk);
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WM_HOTKEY && m.WParam.ToInt32() == Id && Pressed != null) Pressed();
            base.WndProc(ref m);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && IsHandleCreated) UnregisterHotKey(Handle, Id);
            base.Dispose(disposing);
        }
    }

    // מריץ סקריפט PowerShell מקובץ זמני. בלי -EncodedCommand: דפוס שאנטי-וירוס חוסם כי תוכנות זדוניות משתמשות בו
    static class PsFile
    {
        public static Process Start(string script, bool redirectOutput)
        {
            string dir = Path.Combine(Config.Dir, "tmp");
            Directory.CreateDirectory(dir);
            try { foreach (var old in Directory.GetFiles(dir, "*.ps1")) if (File.GetLastWriteTime(old) < DateTime.Now.AddHours(-1)) File.Delete(old); } catch { }
            string path = Path.Combine(dir, Guid.NewGuid().ToString("N") + ".ps1");
            File.WriteAllText(path, script, new UTF8Encoding(true)); // BOM: כדי ש-PowerShell 5.1 יקרא עברית נכון
            var psi = new ProcessStartInfo("powershell", "-NoProfile -NonInteractive -ExecutionPolicy Bypass -File \"" + path + "\"");
            psi.UseShellExecute = false;
            psi.CreateNoWindow = true;
            if (redirectOutput) { psi.RedirectStandardOutput = true; psi.RedirectStandardError = true; }
            var p = Process.Start(psi);
            p.EnableRaisingEvents = true;
            p.Exited += delegate { try { File.Delete(path); } catch { } };
            return p;
        }
    }

    // דף דפדפן שנשאר פתוח ברקע (מוסתר) ומשמש גם להקראה בקול טבעי של Edge וגם להאזנה (זיהוי דיבור).
    // כך ההקראה מוכנה מיד, והמיקרופון נדלק רק כשצריך להאזין
    class VoiceListener
    {
        const int Port = 47653;
        const string Page = @"<!doctype html><html><head><meta charset=""utf-8""><title>@TITLE@</title><link rel=""icon"" type=""image/png"" sizes=""256x256"" href=""/icon.png""><link rel=""icon"" href=""/favicon.ico"">
<meta name=""theme-color"" content=""#FAF9F5"">
<style>
*{box-sizing:border-box}
body{margin:0;padding:12px;min-height:100vh;overflow:hidden;display:flex;align-items:center;background:#FAF9F5;color:#3D3929;font-family:Segoe UI,sans-serif;direction:@DIR@}
.card{width:100%;background:#fff;border:1px solid #EBE5D9;border-radius:12px;padding:14px 16px;display:flex;align-items:center;gap:14px;box-shadow:0 1px 4px rgba(61,57,41,.08)}
.card img{width:46px;height:46px;border-radius:10px;flex:none}
.main{flex:1;min-width:0}
.row{display:flex;align-items:center;gap:8px;font-size:17px;font-weight:600}
.sub{font-size:12px;color:#837D6E;margin-top:2px}
.dot{width:10px;height:10px;border-radius:50%;background:#837D6E;flex:none}
.dot.ready{background:#D97757}.dot.err{background:#B33A3A}
.dot.listen{background:#467D55;animation:p 1.3s infinite}
@keyframes p{0%{box-shadow:0 0 0 0 rgba(70,125,85,.5)}100%{box-shadow:0 0 0 8px rgba(70,125,85,0)}}
#t{margin-top:10px;background:#FCF1EB;border-radius:8px;padding:6px 10px;color:#C15F3C;font-size:14px;word-break:break-word;max-height:5.6em;overflow:hidden}
#t.ph{background:transparent;border:1px dashed #D9D2C2;color:#837D6E;font-size:13px}
#t:empty{display:none}
</style></head><body>
<div class=""card""><img src=""/icon.png"" alt="""">
<div class=""main""><div class=""row""><span id=""d"" class=""dot""></span><span id=""s"">@S_START@</span></div><div class=""sub"">Claude Notify</div><div id=""t""></div></div></div>
<script>
var q=new URLSearchParams(location.search);
var rate=parseFloat(q.get('rate')||'1')||1,gender=q.get('gender')||'male',lang=q.get('lang')||'he-IL',pre=lang.substring(0,2);
var S=document.getElementById('s'),T=document.getElementById('t'),D=document.getElementById('d');
var HINT='@S_HINT@',delay=300;
function tail(t){t=t.trim();return t.length>80?'…'+t.slice(-80):t;}
function st(t,c){S.textContent=t;D.className='dot '+c;post('/status',c+'|'+t);
 if(c=='listen'){if(!T.textContent){T.className='ph';T.textContent=HINT;}}
 else if(T.className=='ph'){T.className='';T.textContent='';}}
function post(u,b){try{fetch(u,{method:'POST',body:b||''});}catch(e){}}
function postHeard(t){try{fetch('/heard',{method:'POST',body:t}).then(function(r){return r.text();}).then(function(x){if(x=='ok'){T.className='';T.textContent=tail(t);}});}catch(e){}}
var voice=null,reported=false;
function pick(){var all=speechSynthesis.getVoices().filter(function(v){return v.lang.replace('_','-').indexOf(pre)==0;});
 var vs=all.filter(function(v){return /Natural|Online/i.test(v.name);});
 var want=gender=='female'?/הילה|Hila|Jenny|Aria|Emma|Ava/i:/אברי|Avri|Guy|Davis|Andrew|Brian/i;
 for(var i=0;i<vs.length;i++)if(want.test(vs[i].name))return vs[i];
 if(vs.length)return vs[0];
 var g=all.filter(function(v){return /Google/i.test(v.name);});
 return g.length?g[0]:null;}
function report(){if(reported||speechSynthesis.getVoices().length==0)return;reported=true;voice=pick();
 post('/tts',voice?voice.name:'');post('/log','voices: '+speechSynthesis.getVoices().filter(function(x){return x.lang.indexOf(pre)==0;}).map(function(x){return x.name+'('+x.lang+')';}).join('; '));st('@S_READY@','ready');}
speechSynthesis.onvoiceschanged=report;report();
setTimeout(function(){if(!reported){reported=true;voice=pick();post('/tts',voice?voice.name:'');st('@S_READY@','ready');}},3000);
var R=window.SpeechRecognition||window.webkitSpeechRecognition,rec=null,listening=false;
function startRec(){
 if(!R){post('/error','unsupported');return;}
 if(rec)return;listening=true;
 var me=new R();rec=me;me.lang=lang;me.continuous=true;me.interimResults=true;
 me.onstart=function(){st('@S_LISTEN@','listen');post('/ready');};
 me.onresult=function(e){var t='';for(var i=e.resultIndex;i<e.results.length;i++){var tr=e.results[i][0].transcript;t+=tr+' ';if(e.results[i].isFinal)post('/final',tr);}delay=300;postHeard(t);};
 ['audiostart','soundstart','speechstart','nomatch'].forEach(function(n){me['on'+n]=function(){post('/log',n);};});
 me.onerror=function(e){if(e.error=='no-speech'||e.error=='aborted')return;if(e.error=='network'){delay=2500;st('@S_NET@','err');}else st('@S_ERR@'+e.error,'err');post('/error',e.error);};
 me.onend=function(){if(listening&&rec===me)setTimeout(function(){try{if(listening&&rec===me)me.start();}catch(e){}},delay);};
 try{me.start();}catch(e){post('/error',String(e));}
}
function stopRec(){listening=false;var r=rec;rec=null;if(r){try{r.stop();}catch(e){}}st('@S_READY@','ready');T.className='';T.textContent='';}
function speak(t){
 if(!voice){post('/spoke');return;}
 var go=function(){
  var u=new SpeechSynthesisUtterance(t);u.voice=voice;u.lang=voice.lang;u.rate=rate;
  u.onend=function(){post('/spoke');};
  u.onerror=function(e){if(e.error=='canceled'||e.error=='interrupted')return;post('/log','tts error '+e.error);post('/spoke');};
  speechSynthesis.speak(u);
 };
 if(speechSynthesis.speaking||speechSynthesis.pending){speechSynthesis.cancel();setTimeout(go,200);}else go();
}
setInterval(function(){
 fetch('/poll',{method:'POST'}).then(function(r){return r.text();}).then(function(t){
  if(!t)return;var c=t.charAt(0),a=t.substring(1);
  if(c=='L'){if(a=='1')startRec();else stopRec();}
  else if(c=='C'){if(speechSynthesis.speaking||speechSynthesis.pending)speechSynthesis.cancel();stopRec();}
  else if(c=='S')speak(a);
 }).catch(function(){});},200);
</script></body></html>";

        readonly object gate = new object();
        readonly List<KeyValuePair<DateTime, string>> heard = new List<KeyValuePair<DateTime, string>>();
        readonly List<KeyValuePair<DateTime, string>> finals = new List<KeyValuePair<DateTime, string>>();
        readonly Queue<string> cmds = new Queue<string>();
        readonly ManualResetEvent spokeEv = new ManualResetEvent(false);
        System.Net.HttpListener http;
        Process browser;
        IntPtr hwnd = IntPtr.Zero;
        volatile bool ready;
        volatile bool speaking;
        volatile bool ttsReported;
        DateTime mutedUntil = DateTime.MinValue;
        static bool staleKilled;

        // הדף עם הטקסטים בשפת הממשק
        static string PageHtml()
        {
            return Page.Replace("@TITLE@", L.T("Claude Notify - מאזין", "Claude Notify - Listening"))
                       .Replace("@DIR@", L.He ? "rtl" : "ltr")
                       .Replace("@S_START@", L.T("מתחיל...", "Starting..."))
                       .Replace("@S_READY@", L.T("מוכן", "Ready"))
                       .Replace("@S_LISTEN@", L.T("מאזין... אפשר לדבר", "Listening... you can speak"))
                       .Replace("@S_ERR@", L.T("שגיאה: ", "Error: "))
                       .Replace("@S_NET@", L.T("אין חיבור לשירות הזיהוי, מנסה שוב...", "No connection to the recognition service, retrying..."))
                       .Replace("@S_HINT@", L.T("אומרים משהו, והטקסט יופיע כאן", "Say something and the text will appear here"));
        }
        public DateTime StartedAt = DateTime.Now;
        public event Action<string, string> StatusChanged;   // מצב (ready/listen/err), טקסט
        public event Action<string> HeardChanged;            // מה שנשמע עכשיו
        public string NaturalVoice = "";
        public string Error;
        public bool Ready { get { return ready; } }
        public bool TtsReported { get { return ttsReported; } }
        public bool NaturalOk { get { return ttsReported && !string.IsNullOrEmpty(NaturalVoice); } }
        public bool IsAlive { get { return http != null && browser != null && !browser.HasExited; } }

        // שולח טקסט לדף להקראה בקול טבעי, וממתין לסיום
        public bool Speak(string text)
        {
            spokeEv.Reset();
            Enqueue("S" + text);
            return spokeEv.WaitOne(60000);
        }

        void Enqueue(string c) { lock (gate) cmds.Enqueue(c); }

        // מדליק מיקרופון והאזנה, וממתין שהדף יתחיל להאזין. אם צריך לאשר מיקרופון - מראה את החלון
        public bool StartListen()
        {
            lock (gate) { heard.Clear(); finals.Clear(); }
            ready = false;
            Enqueue("L1");
            for (int i = 0; i < 30 && !ready; i++) Thread.Sleep(200);
            if (!ready)
            {
                Reveal(true);
                for (int i = 0; i < 70 && !ready; i++) Thread.Sleep(200);
                Reveal(false);
            }
            return ready;
        }

        public void StopListen() { Enqueue("L0"); ready = false; }

        // עוצר הקראה והאזנה מיד
        public void Abort()
        {
            lock (gate) { cmds.Clear(); cmds.Enqueue("C"); }
            ready = false;
            spokeEv.Set();
        }

        // בזמן שהתוכנה מדברת (ומעט אחרי) המיקרופון קולט את הרמקולים, ולכן מתעלמים ממה שנשמע
        public void SetSpeaking(bool on)
        {
            speaking = on;
            if (!on) mutedUntil = DateTime.Now.AddMilliseconds(1500);
        }
        bool IsMuted { get { return speaking || DateTime.Now < mutedUntil; } }

        public static string FindBrowser()
        {
            string[] c = {
                @"C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe",
                @"C:\Program Files\Microsoft\Edge\Application\msedge.exe",
                @"C:\Program Files\Google\Chrome\Application\chrome.exe",
                @"C:\Program Files (x86)\Google\Chrome\Application\chrome.exe",
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Google\Chrome\Application\chrome.exe") };
            foreach (var p in c) if (File.Exists(p)) return p;
            return null;
        }

        // אם התוכנה נסגרה בכוח, נשאר דפדפן יתום עם הפרופיל שלנו, וחלון חדש היה נפתח בתוכו. סוגרים אותו
        static void KillStale()
        {
            if (staleKilled) return;
            staleKilled = true;
            try
            {
                using (var s = new System.Management.ManagementObjectSearcher("SELECT ProcessId, CommandLine FROM Win32_Process WHERE Name='msedge.exe' OR Name='chrome.exe'"))
                    foreach (System.Management.ManagementObject o in s.Get())
                    {
                        string cl = o["CommandLine"] as string;
                        if (cl != null && cl.IndexOf(@"ClaudeNotify\browser", StringComparison.OrdinalIgnoreCase) >= 0)
                            try { Process.GetProcessById(Convert.ToInt32(o["ProcessId"])).Kill(); } catch { }
                    }
            }
            catch (Exception ex) { Log.Write("kill stale: " + ex.Message); }
        }

        public bool Start(string lang, string gender, double rate)
        {
            string exe = FindBrowser();
            if (exe == null) { Error = "no Chrome/Edge found"; return false; }
            try
            {
                KillStale();
                http = new System.Net.HttpListener();
                http.Prefixes.Add("http://127.0.0.1:" + Port + "/");
                http.Start();
                var t = new Thread(Serve); t.IsBackground = true; t.Start();
                // פרופיל נפרד: ההרשאה למיקרופון נשמרת בו, והתהליך שלנו וקל לסגור
                string prof = Path.Combine(Config.Dir, "browser");
                Directory.CreateDirectory(Path.Combine(prof, "Default"));
                // מאשרים מראש גישה למיקרופון לכתובת המקומית שלנו בלבד (בפרופיל הנפרד), כדי שלא תקפוץ בקשה
                string prefs = Path.Combine(prof, @"Default\Preferences");
                if (!File.Exists(prefs))
                    File.WriteAllText(prefs, "{\"profile\":{\"content_settings\":{\"exceptions\":{\"media_stream_mic\":{\"http://127.0.0.1:" + Port + ",*\":{\"setting\":1}}}}}}", new UTF8Encoding(false));
                var psi = new ProcessStartInfo(exe,
                    "--user-data-dir=\"" + prof + "\" --app=http://127.0.0.1:" + Port + "/?gender=" + gender + "&rate=" + rate.ToString(System.Globalization.CultureInfo.InvariantCulture) + "&lang=" + lang +
                    " --autoplay-policy=no-user-gesture-required --window-size=380,220 --no-first-run --no-default-browser-check --disable-features=CalculateNativeWinOcclusion,Translate,OptimizationHints,MediaRouter --disable-backgrounding-occluded-windows --disable-renderer-backgrounding --disable-gpu --disable-extensions --disable-background-networking --disable-sync --disable-component-update --renderer-process-limit=1");
                psi.UseShellExecute = false;
                browser = Process.Start(psi);
                StartedAt = DateTime.Now;
                var trimmer = new Thread(TrimLoop); trimmer.IsBackground = true; trimmer.Start();
                var hider = new Thread(HideWindow); hider.IsBackground = true; hider.Start();
                return true;
            }
            catch (Exception ex) { Error = ex.Message; Stop(); return false; }
        }

        [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint flags);
        [DllImport("user32.dll")] static extern int GetWindowLong(IntPtr h, int i);
        [DllImport("user32.dll")] static extern int SetWindowLong(IntPtr h, int i, int v);

        [DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr h, int msg, IntPtr w, IntPtr l);
        Icon bigIcon, smallIcon;

        // מגדיר לחלון הדפדפן את אייקון התוכנה (חד, בגודל המתאים למסך), במקום האייקון הקטן והמטושטש שהדפדפן לוקח מהדף
        void SetIcons(IntPtr h)
        {
            try
            {
                if (bigIcon == null)
                {
                    bigIcon = new Icon(Branding.AppIcon, SystemInformation.IconSize);
                    smallIcon = new Icon(Branding.AppIcon, SystemInformation.SmallIconSize);
                }
                SendMessage(h, 0x0080, new IntPtr(1), bigIcon.Handle);       // WM_SETICON, ICON_BIG
                SendMessage(h, 0x0080, IntPtr.Zero, smallIcon.Handle);       // ICON_SMALL
            }
            catch { }
        }

        // מוצא את חלון הדפדפן, מגדיר לו את האייקון שלנו, ואם צריך - מזיז אותו אל מחוץ למסך (ומוריד מסרגל המשימות)
        void HideWindow()
        {
            try
            {
                var b = browser;
                IntPtr h = IntPtr.Zero;
                for (int i = 0; i < 100 && h == IntPtr.Zero && b != null && !b.HasExited; i++)
                {
                    Thread.Sleep(100); b.Refresh(); h = b.MainWindowHandle;
                }
                if (h == IntPtr.Zero) return;
                SetIcons(h);
                SetWindowLong(h, -20, (GetWindowLong(h, -20) | 0x80) & ~0x40000); // TOOLWINDOW, לא APPWINDOW
                hwnd = h;
                Reveal(false);
                // הדפדפן עשוי להחליף את האייקון אחרי שהדף נטען - מגדירים שוב
                for (int k = 0; k < 3; k++) { Thread.Sleep(3000); if (b == null || b.HasExited) return; SetIcons(h); }
            }
            catch { }
        }

        // מראה את החלון (כדי לאשר מיקרופון) או מחביא אותו מחוץ למסך
        void Reveal(bool show)
        {
            var h = hwnd;
            if (h == IntPtr.Zero) return;
            if (show) SetWindowPos(h, new IntPtr(-1), 200, 200, 0, 0, 0x0001 | 0x0010);   // TOPMOST, NOSIZE|NOACTIVATE
            else SetWindowPos(h, IntPtr.Zero, -32000, -32000, 0, 0, 0x0001 | 0x0004 | 0x0010);
        }

        [DllImport("psapi.dll")] static extern bool EmptyWorkingSet(IntPtr h);
        [DllImport("kernel32.dll")] static extern IntPtr OpenProcess(uint access, bool inherit, int pid);
        [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr h);

        // כל כמה דקות, כשהדף במנוחה, מחזירים ל-Windows את הזיכרון שהדפדפן לא משתמש בו כרגע. הוא נטען חזרה לפי הצורך
        void TrimLoop()
        {
            Thread.Sleep(15000);
            while (http != null)
            {
                try { if (!speaking && !ready) TrimMemory(); } catch { }
                for (int i = 0; i < 120 && http != null; i++) Thread.Sleep(1000);
            }
        }

        static void TrimMemory()
        {
            using (var q = new System.Management.ManagementObjectSearcher("SELECT ProcessId, CommandLine FROM Win32_Process WHERE Name='msedge.exe' OR Name='chrome.exe'"))
                foreach (System.Management.ManagementObject o in q.Get())
                {
                    string cl = o["CommandLine"] as string;
                    if (cl == null || cl.IndexOf(@"ClaudeNotify\browser", StringComparison.OrdinalIgnoreCase) < 0) continue;
                    IntPtr h = OpenProcess(0x0100 | 0x0400, false, Convert.ToInt32(o["ProcessId"])); // SET_QUOTA | QUERY_INFORMATION
                    if (h != IntPtr.Zero) { EmptyWorkingSet(h); CloseHandle(h); }
                }
        }

        void Serve()
        {
            while (http != null && http.IsListening)
            {
                System.Net.HttpListenerContext cx;
                try { cx = http.GetContext(); } catch { return; }
                try
                {
                    string path = cx.Request.Url.AbsolutePath;
                    if (path == "/favicon.ico")
                    {
                        var ms = new MemoryStream();
                        Branding.WriteIco(ms, new[] { 16, 24, 32, 48, 64, 128, 256 });
                        var ib = ms.ToArray();
                        cx.Response.ContentType = "image/x-icon";
                        cx.Response.OutputStream.Write(ib, 0, ib.Length);
                    }
                    else if (path == "/icon.png")
                    {
                        var ms = new MemoryStream();
                        using (var bmp = Branding.MakeBitmap(256)) bmp.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
                        var pb = ms.ToArray();
                        cx.Response.ContentType = "image/png";
                        cx.Response.OutputStream.Write(pb, 0, pb.Length);
                    }
                    else if (path == "/")
                    {
                        var b = Encoding.UTF8.GetBytes(PageHtml());
                        cx.Response.ContentType = "text/html; charset=utf-8";
                        cx.Response.OutputStream.Write(b, 0, b.Length);
                    }
                    else
                    {
                        string body = "", resp = "ok";
                        using (var sr = new StreamReader(cx.Request.InputStream, Encoding.UTF8)) body = sr.ReadToEnd();
                        if (path == "/ready") ready = true;
                        else if (path == "/error") { Error = body; Log.Write("voice listen error: " + body); }
                        else if (path == "/tts") { NaturalVoice = body; ttsReported = true; Log.Write("voice: natural voice [" + body + "]"); }
                        else if (path == "/final") { if (!IsMuted) lock (gate) finals.Add(new KeyValuePair<DateTime, string>(DateTime.Now, body)); }
                        else if (path == "/poll") { lock (gate) resp = cmds.Count > 0 ? cmds.Dequeue() : ""; }
                        else if (path == "/spoke") spokeEv.Set();
                        else if (path == "/status")
                        {
                            int bar = body.IndexOf('|');
                            var ev = StatusChanged;
                            if (bar > 0 && ev != null) ev(body.Substring(0, bar), body.Substring(bar + 1));
                        }
                        else if (path == "/log") Log.Write("voice page: " + body);
                        else if (path == "/heard")
                        {
                            if (IsMuted) resp = "mute";
                            else
                            {
                                lock (gate) heard.Add(new KeyValuePair<DateTime, string>(DateTime.Now, body));
                                var hv = HeardChanged;
                                if (hv != null) hv(body);
                            }
                        }
                        var rb = Encoding.UTF8.GetBytes(resp);
                        cx.Response.OutputStream.Write(rb, 0, rb.Length);
                    }
                    cx.Response.Close();
                }
                catch { }
            }
        }

        // כל מה שנשמע מאז הזמן הנתון
        public string HeardSince(DateTime t)
        {
            var sb = new StringBuilder();
            lock (gate) foreach (var h in heard) if (h.Key >= t) sb.Append(h.Value).Append(' ');
            return sb.ToString();
        }

        // משפטים סופיים בלבד (בלי הגרסאות החלקיות שמתעדכנות תוך כדי דיבור)
        public string FinalsSince(DateTime t)
        {
            var sb = new StringBuilder();
            lock (gate) foreach (var h in finals) if (h.Key >= t) sb.Append(h.Value.Trim()).Append(' ');
            return sb.ToString().Trim();
        }

        // הטקסט החלקי האחרון (אם עוד לא התקבל משפט סופי)
        public string LastSince(DateTime t)
        {
            string last = "";
            lock (gate) foreach (var h in heard) if (h.Key >= t) last = h.Value;
            return last.Trim();
        }

        public void Stop()
        {
            spokeEv.Set();
            try { if (http != null) http.Close(); } catch { }
            http = null;
            try
            {
                if (browser != null && !browser.HasExited)
                {
                    var k = Process.Start(new ProcessStartInfo("taskkill", "/PID " + browser.Id + " /T /F") { CreateNoWindow = true, UseShellExecute = false });
                    k.WaitForExit(3000);
                }
            }
            catch { }
            browser = null;
        }
    }

    // הקראה בקול טבעי של Edge (או בקול של Windows דרך PowerShell כגיבוי), והאזנה לתשובה דרך הדפדפן
    static class Voice
    {
        // שפת הדיבור (הקראה, האזנה, סיכום): לפי ההגדרה, או לפי שפת הממשק
        public static bool IsHe { get { string v = Config.Load().VoiceLang; return v == "he" || (v != "en" && L.He); } }
        public static string T(string he, string en) { return IsHe ? he : en; }

        [StructLayout(LayoutKind.Sequential)] struct LASTINPUTINFO { public uint cbSize; public uint dwTime; }
        [DllImport("user32.dll")] static extern bool GetLastInputInfo(ref LASTINPUTINFO info);

        // כמה שניות עברו מאז שהמשתמש נגע במקלדת או בעכבר
        public static double IdleSeconds()
        {
            var li = new LASTINPUTINFO(); li.cbSize = (uint)Marshal.SizeOf(typeof(LASTINPUTINFO));
            if (!GetLastInputInfo(ref li)) return 9999;
            return (uint)(Environment.TickCount - (int)li.dwTime) / 1000.0;
        }

        // האם המשתמש נגע במחשב אחרי הרגע הנתון
        static bool UserActiveSince(DateTime t)
        {
            return IdleSeconds() < (DateTime.Now - t).TotalSeconds - 1.0;
        }

        public static Control Ui;   // לחלון הראשי, כדי לפתוח חלונות משרשורי רקע
        static ListenForm lf;

        static void ShowListen(VoiceListener l)
        {
            var ui = Ui;
            if (ui == null || !Config.Load().VoiceShowWindow) return;
            ui.BeginInvoke(new Action(delegate
            {
                if (lf == null || lf.IsDisposed) lf = new ListenForm();
                lf.Attach(l);
                lf.Show();
            }));
        }

        static void HideListen()
        {
            var ui = Ui;
            if (ui == null) return;
            ui.BeginInvoke(new Action(delegate { if (lf != null && !lf.IsDisposed) { lf.Detach(); lf.Hide(); } }));
        }

        // החלפת שפה: החלון ייבנה מחדש בשפה החדשה
        public static void ResetListenWindow()
        {
            var ui = Ui;
            if (ui == null) return;
            ui.BeginInvoke(new Action(delegate { if (lf != null) { lf.Dispose(); lf = null; } }));
        }

        static Process current;
        static volatile int generation;
        static VoiceListener host;
        static string hostKey = "";
        static readonly object hostLock = new object();

        const string Ps = @"
$ErrorActionPreference = 'SilentlyContinue'
$text = [System.Text.Encoding]::UTF8.GetString([System.Convert]::FromBase64String('@TEXT@'))
[void][Windows.Media.SpeechSynthesis.SpeechSynthesizer, Windows.Media.SpeechSynthesis, ContentType=WindowsRuntime]
Add-Type -AssemblyName System.Runtime.WindowsRuntime
$asTask = ([System.WindowsRuntimeSystemExtensions].GetMethods() | Where-Object {
    $_.Name -eq 'AsTask' -and $_.GetParameters().Count -eq 1 -and
    $_.GetParameters()[0].ParameterType.Name -eq 'IAsyncOperation`1' })[0]
$synth = New-Object Windows.Media.SpeechSynthesis.SpeechSynthesizer
$synth.Options.SpeakingRate = @RATE@
$v = [Windows.Media.SpeechSynthesis.SpeechSynthesizer]::AllVoices | Where-Object { $_.Language -like '@LANG@*' } | Select-Object -First 1
if ($v) { $synth.Voice = $v }
$k = $asTask.MakeGenericMethod([Windows.Media.SpeechSynthesis.SpeechSynthesisStream]).Invoke($null, @($synth.SynthesizeTextToStreamAsync($text)))
[void]$k.Wait(-1)
$wav = Join-Path $env:TEMP ('claude-speak-' + [guid]::NewGuid().ToString('N') + '.wav')
$in = [System.IO.WindowsRuntimeStreamExtensions]::AsStreamForRead($k.Result)
$out = [System.IO.File]::Create($wav)
$in.CopyTo($out); $out.Close(); $in.Close()
$player = New-Object System.Media.SoundPlayer $wav
$player.PlaySync()
Remove-Item $wav -Force
";

        // מילים שמסמנות "שמעתי, אני בדרך" - ההקראה נעצרת כשנשמעת אחת מהן
        static readonly string[] AckWords = { "מגיע", "שנייה", "שניה", "רגע", "שומע", "שמעתי", "הנה", "אני כאן", "בסדר", "אוקיי", "אוקי", "טוב", "די", "מספיק", "עוצר", "coming", "second", "wait", "okay", "ok" };

        public static bool IsAck(string heard)
        {
            if (string.IsNullOrEmpty(heard)) return false;
            string h = heard.ToLowerInvariant();
            foreach (var w in AckWords) if (h.IndexOf(w, StringComparison.Ordinal) >= 0) return true;
            return false;
        }

        // מחזיר את הדף החם (ויוצר אותו אם צריך). קוראים לזה משרשור רקע: היצירה הראשונה לוקחת כמה שניות
        static VoiceListener GetHost()
        {
            lock (hostLock)
            {
                var c = Config.Load();
                string lang = IsHe ? "he-IL" : "en-US";
                string key = lang + "|" + c.VoiceGender + "|" + c.VoiceRate.ToString(System.Globalization.CultureInfo.InvariantCulture) + "|" + c.VoiceShowWindow;
                // אם הקול הטבעי לא נמצא (למשל כי לא היה אינטרנט) - מנסים שוב בעוד דקה
                bool stale = host != null && host.TtsReported && !host.NaturalOk && (DateTime.Now - host.StartedAt).TotalSeconds > 60;
                if (host != null && (!host.IsAlive || hostKey != key || stale)) { host.Stop(); host = null; }
                if (host == null)
                {
                    var h = new VoiceListener();
                    if (!h.Start(lang, c.VoiceGender, c.VoiceRate)) { Log.Write("voice: browser failed: " + h.Error); return null; }
                    host = h; hostKey = key;
                }
                for (int i = 0; i < 100 && !host.TtsReported && host.IsAlive; i++) Thread.Sleep(200);
                return host;
            }
        }

        // מחמם את הדף ברקע, כדי שההקראה תהיה מיידית
        public static void Warm()
        {
            if (!Config.Load().VoiceNatural) return;
            var t = new Thread(delegate() { try { GetHost(); } catch (Exception ex) { Log.Write("voice warm: " + ex.Message); } });
            t.IsBackground = true;
            t.Start();
        }

        // סוגר את הדף
        public static void Cool()
        {
            lock (hostLock) { if (host != null) host.Stop(); host = null; }
        }

        // מקריא ומחכה לסיום ההקראה
        static void SayBlocking(string text, int gen)
        {
            if (gen != generation) return;
            string script = Ps.Replace("@TEXT@", Convert.ToBase64String(Encoding.UTF8.GetBytes(text ?? "")))
                              .Replace("@RATE@", Config.Load().VoiceRate.ToString(System.Globalization.CultureInfo.InvariantCulture))
                              .Replace("@LANG@", IsHe ? "he" : "en");
            var p = PsFile.Start(script, false);
            current = p;
            p.WaitForExit(60000);
        }

        public static void Speak(string text) { Call(text, 1, false, ""); }

        // קורא repeats פעמים, ובין הקריאות מאזין. כששומעים "שנייה אני מגיע" וכד' - עוצר ועונה
        public static void Call(string text, int repeats, bool listen, string reply, Action<string, bool> dictate = null, bool stopOnActivity = false)
        {
            Stop();
            int gen = ++generation;
            repeats = Math.Max(1, Math.Min(10, repeats));
            bool natural = Config.Load().VoiceNatural;
            DateTime callStart = DateTime.Now;
            var state = new bool[2]; // [0] = הסתיים, [1] = המשתמש ענה (מפסיקים לפקח)
            if (stopOnActivity)
            {
                // משגיח: אם המשתמש נוגע במקלדת או בעכבר בזמן ההקראה (גם באמצע משפט) - עוצרים מיד
                var w = new Thread(delegate()
                {
                    while (!state[0] && !state[1] && gen == generation)
                    {
                        Thread.Sleep(250);
                        if (state[0] || state[1] || gen != generation) break;
                        if (UserActiveSince(callStart))
                        {
                            Log.Write("voice: user is back (activity during call), stopping");
                            Stop();
                            break;
                        }
                    }
                });
                w.IsBackground = true;
                w.Start();
            }
            var t = new Thread(delegate()
            {
                VoiceListener l = null;
                bool canListen = false, canNatural = false;
                try
                {
                    if (listen || natural)
                    {
                        l = GetHost();
                        if (l != null && gen == generation)
                        {
                            canNatural = natural && l.NaturalOk;
                            if (natural && !canNatural) Log.Write("voice: no natural voice, using Windows voice");
                            if (listen)
                            {
                                ShowListen(l);
                                canListen = l.StartListen();
                                if (!canListen) Log.Write("voice: listener not ready: " + l.Error);
                            }
                        }
                    }
                    var lst = l;
                    Action<string> say = delegate(string x)
                    {
                        if (gen != generation) return;
                        if (lst != null) lst.SetSpeaking(true);
                        try { if (lst != null && canNatural) lst.Speak(x); else SayBlocking(x, gen); }
                        finally { if (lst != null) lst.SetSpeaking(false); }
                    };
                    for (int i = 1; i <= repeats && gen == generation; i++)
                    {
                        if (i > 1 && UserActiveSince(callStart)) { Log.Write("voice: user is back, stopping calls"); break; }
                        say(text);
                        if (gen != generation) break;
                        DateTime t0 = DateTime.Now;
                        bool last = i == repeats;
                        if (l != null && canListen)
                        {
                            for (int w = 0; w < 30 && gen == generation; w++)
                            {
                                Thread.Sleep(200);
                                if (UserActiveSince(callStart)) { Log.Write("voice: user is back, stopping calls"); return; }
                                string h = l.HeardSince(t0);
                                if (IsAck(h))
                                {
                                    state[1] = true;
                                    Log.Write("voice: heard ack [" + h.Trim() + "]");
                                    if (!string.IsNullOrEmpty(reply)) say(reply);
                                    if (dictate != null) Dictate(l, gen, dictate, Config.Load().VoiceNeedSend);
                                    return;
                                }
                            }
                        }
                        else if (!last) Thread.Sleep(5000);
                    }
                }
                catch (Exception ex) { Log.Write("voice error: " + ex.Message); }
                finally { state[0] = true; if (l != null && canListen) l.StopListen(); if (listen) HideListen(); }
            });
            t.IsBackground = true;
            t.Start();
        }

        // אחרי שהמשתמש אמר שהוא מגיע: מאזינים להוראה שלו. מסיימים אחרי כמה שניות של שקט.
        // אם בסוף נאמר "שלח" - שולחים. אחרת (כשההגדרה "שלח רק כשאני אומר שלח" דלוקה) רק מקלידים
        static void Dictate(VoiceListener l, int gen, Action<string, bool> done, bool needSend)
        {
            DateTime t0 = DateTime.Now, start = DateTime.Now, lastAt = DateTime.MinValue;
            string text = "";
            while (gen == generation && (DateTime.Now - start).TotalSeconds < 90)
            {
                Thread.Sleep(250);
                string fin = l.FinalsSince(t0);
                if (fin.Length == 0) { if ((DateTime.Now - start).TotalSeconds > 20) break; continue; }   // 20 שניות להתחיל לדבר
                if (fin != text) { text = fin; lastAt = DateTime.Now; }
                if ((DateTime.Now - lastAt).TotalSeconds >= 2.5) break;
            }
            if (gen != generation || text.Length == 0) return;
            bool send = !needSend;
            string clean = text;
            var m = Regex.Match(text, @"[\s,.!?]*(?:שלח|תשלח|שלחי|send)[\s,.!?]*$", RegexOptions.IgnoreCase);
            if (m.Success) { send = true; clean = text.Substring(0, m.Index).Trim(); }
            Log.Write("voice: dictation [" + clean + "] send=" + send);
            if (clean.Length > 0) done(clean, send);
        }

        // בדיקת האזנה: מאזין 12 שניות ומחזיר את מה ששמע
        public static void TestListen(Action<string> done)
        {
            Stop();
            int gen = ++generation;
            var t = new Thread(delegate()
            {
                string result;
                VoiceListener l = null;
                bool started = false;
                try
                {
                    l = GetHost();
                    if (l != null) ShowListen(l);
                    if (l == null) result = L.T("ההאזנה נכשלה: לא נמצא Edge או Chrome", "Listening failed: Edge or Chrome not found");
                    else if (!(started = l.StartListen())) result = L.T("הדפדפן לא התחיל להאזין. אשר גישה למיקרופון בחלון שנפתח. ", "The browser did not start listening. Allow microphone access in the window. ") + l.Error;
                    else
                    {
                        DateTime t0 = DateTime.Now;
                        while ((DateTime.Now - t0).TotalSeconds < 12 && gen == generation) Thread.Sleep(200);
                        Thread.Sleep(1200); // משפט סופי יכול להגיע שנייה אחרי סוף הדיבור
                        string all = l.HeardSince(t0);
                        string fin = l.FinalsSince(t0);
                        string h = fin.Length > 0 ? fin : l.LastSince(t0);
                        result = h.Length == 0 ? L.T("לא שמעתי כלום.", "Heard nothing.")
                            : L.T("שמעתי: ", "Heard: ") + h + (IsAck(all) ? L.T("  ✓ זוהה כאישור", "  ✓ recognized as an answer") : L.T("  (לא זוהה כאישור)", "  (not recognized as an answer)"));
                    }
                }
                catch (Exception ex) { result = ex.Message; }
                finally { if (l != null && started) l.StopListen(); HideListen(); }
                done(result);
            });
            t.IsBackground = true;
            t.Start();
        }

        // עוצר הקראה והאזנה מיד (הדף נשאר פתוח, מוכן לפעם הבאה)
        public static void Stop()
        {
            generation++;
            try { if (current != null && !current.HasExited) current.Kill(); } catch { }
            current = null;
            var h = host;
            if (h != null) h.Abort();
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
        // מסיר ניקוד וטעמי מקרא (נשאיר מקף עברי), כדי שהחלון הקופץ יציג טקסט נקי
        public static string StripNikud(string t)
        {
            return string.IsNullOrEmpty(t) ? t : Regex.Replace(t, "[\u0591-\u05BD\u05BF\u05C1\u05C2\u05C4\u05C5\u05C7]", "");
        }

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
        public static string Run(string text, int maxWords, bool nikud = false)
        {
            string input = text.Length > 6000 ? text.Substring(0, 6000) : text;
            if (!Voice.IsHe)
            {
                string pe = "Summarize the following message in English in at most " + Math.Max(5, maxWords - 5) +
                    " words, short and clear. Return only the summary, with no preface and no formatting marks.\n\n--- MESSAGE ---\n" + input;
                string errE;
                string se = Ask(pe, 40000, out errE);
                return string.IsNullOrEmpty(se) ? null : se;
            }
            string prompt = "סכם את ההודעה הבאה בעברית בעד " + Math.Max(5, maxWords - 5) +
                " מילים, בניסוח קצר וברור. החזר רק את הסיכום, בלי הקדמה ובלי סימוני עיצוב." +
                (nikud ? " נקד את הסיכום בניקוד מלא ותקני (תנועות, שווא, דגש) כדי שיוקרא נכון בקול. בלי הסברים." : "") +
                "\n\n--- ההודעה ---\n" + input;
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
        readonly HotkeyForm host;
        readonly ToolStripMenuItem enabledItem;
        readonly ToolStripMenuItem voiceItem = new ToolStripMenuItem();
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
            host = new HotkeyForm();
            Voice.Ui = host;
            var handle = host.Handle; // יוצר handle כדי לאפשר BeginInvoke מ-threads אחרים
            sched = new ScheduleRunner(host);
            sched.Started += UpdateTrayText;
            sched.Finished += OnSchedFinished;

            tray = new NotifyIcon();
            tray.Icon = new Icon(Branding.AppIcon, SystemInformation.SmallIconSize);
            tray.Text = "Claude Notify";
            enabledItem = new ToolStripMenuItem();
            enabledItem.Click += delegate { cfg.Enabled = !cfg.Enabled; cfg.Save(); SyncEnabled(); };
            voiceItem.Click += delegate { ToggleVoice(); };
            host.Pressed += ToggleVoice;
            RegisterVoiceHotkey();
            if (cfg.VoiceNatural && (cfg.VoiceEnabled || !cfg.VoiceLowMem)) Voice.Warm();
            BuildMenu();
            tray.MouseDoubleClick += delegate(object s, MouseEventArgs e) { if (e.Button == MouseButtons.Left) ShowSettings(); };
            tray.BalloonTipClicked += delegate { if (lastTarget != 0) WinApi.FocusSession(lastTarget, lastSession); };
            tray.Visible = true;
            SyncEnabled();
            GuiMonitor.Start();
            UpdateCheck.Start(host, ShowUpdateToast);

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
            menu.Items.Add(L.T("הודעה מתוזמנת (פתיחת מכסה)...", "Scheduled message (start quota)..."), null, delegate { ShowSchedule(); });
            menu.Items.Add(L.T("מידע טכני...", "Technical info..."), null, delegate { ShowUsage(); });
            menu.Items.Add(L.T("עזרה והסברים...", "Help..."), null, delegate { ShowHelp(); });
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(L.T("יציאה", "Exit"), null, delegate { sched.Cancel(); Voice.Cool(); tray.Visible = false; Application.Exit(); });
            MenuStyle.Apply(menu);
            tray.ContextMenuStrip = menu;
        }

        void RegisterVoiceHotkey()
        {
            if (!string.IsNullOrEmpty(cfg.VoiceHotkey) && !host.Register(cfg.VoiceHotkey))
            {
                Log.Write("voice hotkey not registered: " + cfg.VoiceHotkey);
                tray.ShowBalloonTip(6000, "Claude Notify", L.T("קיצור המקשים " + cfg.VoiceHotkey + " תפוס על ידי תוכנה אחרת. בחר קיצור אחר בהגדרות.", "The hotkey " + cfg.VoiceHotkey + " is taken by another app. Choose a different one in Settings."), ToolTipIcon.Warning);
            }
        }

        System.Threading.Timer lowMemTimer;

        void ToggleVoice()
        {
            cfg.VoiceEnabled = !cfg.VoiceEnabled;
            cfg.Save();
            voiceItem.Checked = cfg.VoiceEnabled;
            if (settings != null) settings.Reload();
            if (voiceForm != null && !voiceForm.IsDisposed) voiceForm.Reload();
            if (!cfg.VoiceEnabled) Voice.Stop();
            if (cfg.VoiceEnabled) Voice.Warm();
            else if (cfg.VoiceLowMem)
            {
                // חיסכון בזיכרון: סוגרים את הדף אחרי שההודעה על הכיבוי נשמעה
                if (lowMemTimer != null) lowMemTimer.Dispose();
                lowMemTimer = new System.Threading.Timer(delegate { if (!cfg.VoiceEnabled) Voice.Cool(); }, null, 10000, Timeout.Infinite);
            }
            if (cfg.VoiceAnnounce)
                Voice.Speak(cfg.VoiceEnabled ? Voice.T("הַקרִיאָה בְּקוֹל הוּפְעֲלָה", "Voice call is on") : Voice.T("הַקרִיאָה בְּקוֹל כֻּבְּתָה", "Voice call is off"));
        }

        // ההודעה שמוקראת: פנייה בשם שהמשתמש הזין, ואחריה הטקסט (אם יש)
        public void TestSpeak()
        {
            Voice.Call(VoiceText(Voice.T("זו בדיקת הקראה.", "This is a voice test.")), 1, false, "");
        }

        public void TestListen()
        {
            Show(0, L.T("מאזין 12 שניות. אמור \"שנייה אני מגיע\"", "Listening for 12 seconds. Say \"just a second, I'm coming\""), "", "", L.T("בדיקת האזנה", "Listening test"));
            Voice.TestListen(delegate(string r)
            {
                host.BeginInvoke(new Action(delegate { Show(0, r, "", "", L.T("בדיקת האזנה", "Listening test")); }));
            });
        }

        void SpeakCall(string body, long target, string conv)
        {
            if (cfg.VoiceIdleSeconds > 0 && Voice.IdleSeconds() < cfg.VoiceIdleSeconds)
            {
                Log.Write("voice: skipped, user is at the computer (idle " + (int)Voice.IdleSeconds() + "s)");
                return;
            }
            bool dict = cfg.VoiceDictate && cfg.VoiceListen && target != 0;
            Action<string, bool> cb = null;
            if (dict) cb = delegate(string t, bool send) { host.BeginInvoke(new Action(delegate { SendDictation(target, t, send, conv); })); };
            Voice.Call(VoiceText(body), cfg.VoiceRepeats, cfg.VoiceListen,
                dict ? Voice.T("בסדר, אני מקשיב", "OK, I'm listening") : Voice.T("בסדר, אני מחכה לך", "OK, I'll wait for you"), cb, cfg.VoiceIdleSeconds > 0);
        }

        // מקליד את מה שהוכתב בתיבת השיחה של חלון ה-VS Code שממנו הגיעה ההתראה (בעזרת המנגנון של ההודעה המתוזמנת)
        void SendDictation(long target, string text, bool send, string conv)
        {
            try
            {
                string key = "";
                foreach (var w in VsWindows.List()) if (w.Handle == target) { key = w.Key; break; }
                if (key.Length == 0) { Show(0, L.T("לא מצאתי את חלון ה-VS Code להקלדה", "Could not find the VS Code window to type into"), "", "", "Claude Notify"); return; }
                var sc = new SchedConfig();
                sc.Method = "vscode"; sc.NewChat = false; sc.Text = text; sc.SendEnter = send;
                sc.KeepAwake = false; sc.Wake = false;
                sc.WindowKeys = new List<string> { key };
                // כותרת השיחה עוזרת להבחין בין חלונות עם אותה כותרת
                if (!string.IsNullOrEmpty(conv)) sc.WindowConvs[key] = conv;
                string err = sched.Start(sc, true);
                if (err != null) Show(0, L.Tr(err), "", "", "Claude Notify");
            }
            catch (Exception ex) { Log.Write("dictation error: " + ex.Message); }
        }

        string VoiceText(string body)
        {
            string name = (cfg.UserName ?? "").Trim();
            string head = (name.Length > 0 ? name + " - " : "") + Voice.T(cfg.VoiceNikud ? "קלוֹד סִיֵּים לְהָשִׁיב." : "קלוד סיים להשיב.", "Claude has finished replying.");
            return string.IsNullOrEmpty(body) ? head : head + " " + body;
        }

        void SyncEnabled()
        {
            voiceItem.Checked = cfg.VoiceEnabled;
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

        VoiceForm voiceForm;
        public void ShowVoice()
        {
            if (voiceForm == null || voiceForm.IsDisposed) voiceForm = new VoiceForm(this);
            voiceForm.Reload();
            voiceForm.Show();
            voiceForm.WindowState = FormWindowState.Normal;
            voiceForm.Activate();
        }

        HelpForm helpForm;
        public void ShowHelp()
        {
            if (helpForm == null || helpForm.IsDisposed) helpForm = new HelpForm();
            helpForm.Show();
            helpForm.WindowState = FormWindowState.Normal;
            helpForm.Activate();
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
            RegisterVoiceHotkey();
            if (c.VoiceNatural && (c.VoiceEnabled || !c.VoiceLowMem)) Voice.Warm(); else Voice.Cool();
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
                if (helpForm != null) { helpForm.Dispose(); helpForm = null; }
                if (voiceForm != null) { voiceForm.Dispose(); voiceForm = null; }
                Voice.ResetListenWindow();
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
                if (TextUtil.Str(j, "__cmd") == "testlisten") TestListen();
                if (TextUtil.Str(j, "__cmd") == "voice") ShowVoice();
                if (TextUtil.Str(j, "__cmd") == "help") ShowHelp();
                if (TextUtil.Str(j, "__cmd") == "shot")
                {
                    string which = TextUtil.Str(j, "which"), path = TextUtil.Str(j, "path");
                    PageForm f = which == "voice" ? (PageForm)voiceForm : which == "help" ? (PageForm)helpForm : settings;
                    if (f != null && path.Length > 0) f.SaveContentImage(path);
                }
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
                if (cfg.VoiceEnabled) SpeakCall(null, target, title);
                return;
            }
            int heb = 0, qm = 0;
            foreach (char ch in text) { if (ch >= 'א' && ch <= 'ת') heb++; else if (ch == '?') qm++; }
            Log.Write("notify: " + text.Length + " chars (hebrew=" + heb + ", '?'=" + qm + "), target=" + target + ", summarize=" + cfg.Summarize);

            int max = cfg.MaxWords;
            if (cfg.Summarize && text.Trim().Length > 0)
            {
                string src = text;
                bool nik = cfg.VoiceEnabled && cfg.VoiceNikud && Voice.IsHe;
                ThreadPool.QueueUserWorkItem(delegate
                {
                    string s = Summarizer.Run(src, max, nik);
                    host.BeginInvoke(new Action(delegate
                    {
                        // בזמן הסיכום ייתכן שהמשתמש כבר חזר לחלון
                        if (cfg.VoiceEnabled) SpeakCall(TextUtil.FirstWords(s ?? src, max), target, title);
                        if (cfg.SkipWhenVisible && target != 0 && WinApi.IsShown(target)) return;
                        Show(target, TextUtil.StripNikud(TextUtil.FirstWords(s ?? src, max)), session, cwd, title);
                    }));
                });
            }
            else
            {
                if (cfg.VoiceEnabled) SpeakCall(TextUtil.FirstWords(text, max), target, title);
                Show(target, TextUtil.FirstWords(text, max), session, cwd, title);
            }
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
                using (var p = PsFile.Start(script, true))
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

        // התראה על גרסה חדשה; לחיצה פותחת את דף ההורדה
        void ShowUpdateToast(int build)
        {
            if (toast != null && !toast.IsDisposed) toast.Close();
            toast = new ToastForm(L.T("גרסה 1.0." + build + " זמינה. לחץ כאן כדי לפתוח את דף ההורדה.", "Version 1.0." + build + " is available. Click here to open the download page."),
                L.T("יש גרסה חדשה של Claude Notify", "A new version of Claude Notify"), 0, "", "", 15, cfg.PopupLeft);
            toast.OnClick = UpdateCheck.OpenPage;
            toast.Cursor = Cursors.Hand;
            toast.Show();
        }

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

    // ---------------------------------------------------------------- GUI handle monitor
    // "Error creating window handle" קורה כשהתהליך מגיע למכסת ה-Handles של Windows (גופנים, חלונות).
    // המנטר רושם את הספירה ללוג, ומשחרר אובייקטים שלא שוחררו (איסוף זבל) לפני שמגיעים למכסה
    static class GuiMonitor
    {
        [DllImport("user32.dll")] static extern uint GetGuiResources(IntPtr hProcess, uint flags);
        static System.Threading.Timer timer;
        static int tick;

        public static void Start()
        {
            timer = new System.Threading.Timer(delegate { Check(); }, null, 60000, 300000);
        }

        static void Check()
        {
            try
            {
                var p = Process.GetCurrentProcess();
                uint gdi = GetGuiResources(p.Handle, 0), user = GetGuiResources(p.Handle, 1);
                bool high = gdi > 1500 || user > 1500;
                if (high || ++tick % 12 == 1)
                    Log.Write("handles: gdi=" + gdi + " user=" + user + " total=" + p.HandleCount + (high ? " (collecting)" : ""));
                if (high) { GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect(); }
            }
            catch { }
        }
    }

    // ---------------------------------------------------------------- Popup notification
    class ToastForm : Form
    {
        readonly System.Windows.Forms.Timer timer = new System.Windows.Forms.Timer();
        readonly long target;
        readonly string session;
        readonly Color borderColor = Color.FromArgb(217, 119, 87);
        Font titleFont, bodyFont, closeFont;
        int remainingMs;
        public Action OnClick;   // אם הוגדר: לחיצה על ההתראה מריצה אותו במקום להעלות את חלון ה-VS Code

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
            // הגופנים נשמרים כדי לשחרר אותם בסגירה (אחרת כל התראה מדליפה Handles של GDI עד שחלון חדש כבר לא ניתן ליצירה)
            titleFont = new Font("Segoe UI", 10f, FontStyle.Bold);
            bodyFont = new Font("Segoe UI", 10.5f);
            closeFont = new Font("Segoe UI", 9f);
            int textH = TextRenderer.MeasureText(body, bodyFont, new Size(inner, 0),
                TextFormatFlags.WordBreak | (rtl ? TextFormatFlags.RightToLeft : TextFormatFlags.Left) | TextFormatFlags.TextBoxControl).Height + 4;

            var title = new Label { Text = string.IsNullOrEmpty(heading) ? "Claude Code" : heading, AutoEllipsis = true, Font = titleFont, ForeColor = borderColor, AutoSize = false, Size = new Size(inner, Theme.S(22)), Location = new Point(Theme.S(20), Theme.S(10)), TextAlign = ContentAlignment.TopLeft, RightToLeft = rtl ? RightToLeft.Yes : RightToLeft.No };
            var lbl = new Label { Text = body, Font = bodyFont, ForeColor = fg, AutoSize = false, Size = new Size(inner, textH), Location = new Point(Theme.S(20), Theme.S(36)), TextAlign = ContentAlignment.TopLeft, RightToLeft = rtl ? RightToLeft.Yes : RightToLeft.No };
            var close = new Label { Text = "✕", Font = closeFont, ForeColor = closeColor, AutoSize = true, Location = new Point(rtl ? Theme.S(4) : width - Theme.S(22), Theme.S(4)), Cursor = Cursors.Hand };
            close.Click += delegate { Close(); };
            Controls.Add(close);
            Controls.Add(title);
            Controls.Add(lbl);
            Size = new Size(width, Theme.S(36) + textH + Theme.S(14));

            EventHandler click = delegate
            {
                if (OnClick != null) OnClick(); else WinApi.FocusSession(this.target, this.session);
                Close();
            };
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
            if (disposing)
            {
                timer.Dispose();
                if (titleFont != null) titleFont.Dispose();
                if (bodyFont != null) bodyFont.Dispose();
                if (closeFont != null) closeFont.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}

