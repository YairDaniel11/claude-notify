using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;
using System.Windows.Automation;
using System.Windows.Forms;

// הודעה מתוזמנת לפתיחת מכסת 5 השעות של Claude.
// מבוסס על הסקריפט "הודעה לקלוד" של צבי דורש ציון (פורום מתמחים), שהועבר ל-C# ושולב בתוכנה.
// כל הקוד כאן נכתב ב-C# 5, כי build.cmd משתמש ב-csc של .NET Framework 4.x.

namespace ClaudeNotify
{
    // ---------------------------------------------------------------- Config (schedule.json)
    class SchedConfig
    {
        public string Method { get; set; }          // "browser" = דרך הדפדפן, "cli" = דרך Claude Code בלי דפדפן, "vscode" = לחלון VS Code שנבחר
        public string WindowTitle { get; set; }     // ישן: כותרת חלון בודד. נשאר כדי שהגדרות ישנות ימשיכו לעבוד (נקרא כמפתח כותרת#0)
        public List<string> WindowKeys { get; set; }   // חלונות ה-VS Code שנבחרו (לשיטת "vscode"), כל אחד כמפתח "כותרת#מספר"
        public Dictionary<string, string> WindowConvs { get; set; }   // מפתח חלון -> כותרת השיחה שלו בזמן הבחירה, לזיהוי חלונות עם אותה כותרת
        public bool NewChat { get; set; }           // true = לפתוח שיחה חדשה בכל חלון, false = להמשיך בשיחה הפעילה
        public string ProfileKey { get; set; }      // "Chrome|Profile 1"
        public string Url { get; set; }
        public string Text { get; set; }
        public bool UseTime { get; set; }           // false = אחרי השהיה, true = בשעה מסוימת
        public int DelaySeconds { get; set; }
        public string AtTime { get; set; }          // "HH:mm:ss"
        public int MinWaitSeconds { get; set; }
        public int MaxWaitSeconds { get; set; }
        public int TabCount { get; set; }
        public bool SendEnter { get; set; }
        public bool KeepAwake { get; set; }
        public bool Wake { get; set; }              // להעיר את המחשב משינה בשעת השליחה (דרך מתזמן המשימות של Windows)
        public bool AllowScreenOff { get; set; }
        public int MinBattery { get; set; }

        public SchedConfig()
        {
            Method = "browser";
            WindowTitle = "";
            WindowKeys = new List<string>();
            WindowConvs = new Dictionary<string, string>();
            NewChat = true;
            ProfileKey = "";
            Url = "https://claude.ai/new";
            Text = "היי";
            UseTime = false;
            DelaySeconds = 10;
            AtTime = DateTime.Now.AddHours(1).ToString("HH:mm:ss");
            MinWaitSeconds = 3;
            MaxWaitSeconds = 60;
            TabCount = 0;
            SendEnter = true;
            KeepAwake = true;
            Wake = true;
            AllowScreenOff = false;
            MinBattery = 20;
        }

        static string FilePath { get { return Path.Combine(Config.Dir, "schedule.json"); } }

        public static SchedConfig Load()
        {
            try
            {
                if (File.Exists(FilePath))
                {
                    var c = new JavaScriptSerializer().Deserialize<SchedConfig>(File.ReadAllText(FilePath, Encoding.UTF8));
                    if (c != null) { c.Fix(); return c; }
                }
            }
            catch { }
            return new SchedConfig();
        }

        public void Save()
        {
            try
            {
                Directory.CreateDirectory(Config.Dir);
                File.WriteAllText(FilePath, new JavaScriptSerializer().Serialize(this), new UTF8Encoding(false));
            }
            catch (Exception ex) { Log.Write("schedule save error: " + ex.Message); }
        }

        void Fix()
        {
            if (Method != "cli" && Method != "browser" && Method != "vscode") Method = "browser";
            if (WindowTitle == null) WindowTitle = "";
            if (WindowKeys == null) WindowKeys = new List<string>();
            if (WindowConvs == null) WindowConvs = new Dictionary<string, string>();
            if (WindowKeys.Count == 0 && WindowTitle.Length > 0) WindowKeys.Add(VsWindows.Key(WindowTitle, 0));
            if (ProfileKey == null) ProfileKey = "";
            if (string.IsNullOrEmpty(Url)) Url = "https://claude.ai/new";
            if (Text == null) Text = "";
            DelaySeconds = Math.Max(0, Math.Min(DelaySeconds, 86400));
            MinWaitSeconds = Math.Max(0, Math.Min(MinWaitSeconds, 60));
            MaxWaitSeconds = Math.Max(10, Math.Min(MaxWaitSeconds, 600));
            TabCount = Math.Max(0, Math.Min(TabCount, 30));
            MinBattery = Math.Max(0, Math.Min(MinBattery, 100));
            TimeSpan t;
            if (!TimeSpan.TryParse(AtTime, out t)) AtTime = DateTime.Now.AddHours(1).ToString("HH:mm:ss");
        }
    }

    // ---------------------------------------------------------------- Browser profiles
    class BrowserProfile
    {
        public string Browser, Exe, Folder, Label, Email;
        public string Key { get { return Browser + "|" + Folder; } }
        public override string ToString()
        {
            return Browser + " - " + Label + (Email.Length > 0 ? "  (" + Email + ")" : "");
        }
    }

    static class BrowserProfiles
    {
        static string Local { get { return Environment.GetEnvironmentVariable("LOCALAPPDATA") ?? ""; } }

        static string FindExe(string rel)
        {
            var bases = new List<string>();
            bases.Add(Environment.GetEnvironmentVariable("ProgramFiles"));
            bases.Add(Environment.GetEnvironmentVariable("ProgramFiles(x86)"));
            bases.Add(Local);
            foreach (var b in bases)
            {
                if (string.IsNullOrEmpty(b)) continue;
                string p = Path.Combine(b, rel);
                if (File.Exists(p)) return p;
            }
            return null;
        }

        // קורא את רשימת הפרופילים מקובץ Local State של כל דפדפן מותקן
        public static List<BrowserProfile> List()
        {
            var res = new List<BrowserProfile>();
            string[][] defs = new string[][]
            {
                new[] { "Chrome", @"Google\Chrome\User Data", @"Google\Chrome\Application\chrome.exe" },
                new[] { "Edge", @"Microsoft\Edge\User Data", @"Microsoft\Edge\Application\msedge.exe" },
                new[] { "Brave", @"BraveSoftware\Brave-Browser\User Data", @"BraveSoftware\Brave-Browser\Application\brave.exe" }
            };
            foreach (var d in defs)
            {
                try
                {
                    string exe = FindExe(d[2]);
                    string state = Path.Combine(Path.Combine(Local, d[1]), "Local State");
                    if (exe == null || !File.Exists(state)) continue;
                    string json;
                    // הדפדפן יכול להחזיק את הקובץ פתוח, לכן פותחים עם שיתוף
                    using (var fs = new FileStream(state, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                    using (var sr = new StreamReader(fs, Encoding.UTF8))
                        json = sr.ReadToEnd();
                    res.AddRange(Parse(json, d[0], exe));
                }
                catch (Exception ex) { Log.Write("profiles: " + d[0] + " " + ex.Message); }
            }
            return res;
        }

        public static List<BrowserProfile> Parse(string json, string browser, string exe)
        {
            var res = new List<BrowserProfile>();
            var root = new JavaScriptSerializer { MaxJsonLength = int.MaxValue }.Deserialize<Dictionary<string, object>>(json);
            var prof = root != null && root.ContainsKey("profile") ? root["profile"] as Dictionary<string, object> : null;
            var cache = prof != null && prof.ContainsKey("info_cache") ? prof["info_cache"] as Dictionary<string, object> : null;
            if (cache == null) return res;
            var keys = new List<string>(cache.Keys);
            keys.Sort(StringComparer.Ordinal);
            foreach (var folder in keys)
            {
                var info = cache[folder] as Dictionary<string, object>;
                string name = TextUtil.Str(info, "name");
                res.Add(new BrowserProfile
                {
                    Browser = browser,
                    Exe = exe,
                    Folder = folder,
                    Label = name.Length > 0 ? name : folder,
                    Email = TextUtil.Str(info, "user_name")
                });
            }
            return res;
        }
    }

    // ---------------------------------------------------------------- Windows API (schedule)
    [StructLayout(LayoutKind.Sequential)]
    struct SYSTEM_POWER_STATUS
    {
        public byte ACLineStatus, BatteryFlag, BatteryLifePercent, SystemStatusFlag;
        public uint BatteryLifeTime, BatteryFullLifeTime;
    }

    static class Native
    {
        [DllImport("user32.dll")] public static extern void keybd_event(byte vk, byte scan, uint flags, UIntPtr extra);
        [DllImport("user32.dll")] public static extern void mouse_event(uint flags, int dx, int dy, uint data, UIntPtr extra);
        [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
        [DllImport("user32.dll")] public static extern IntPtr OpenInputDesktop(uint flags, bool inherit, uint access);
        [DllImport("user32.dll")] public static extern bool CloseDesktop(IntPtr h);
        [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "GetUserObjectInformationW")]
        public static extern bool GetUserObjectInformation(IntPtr h, int index, StringBuilder buf, uint len, out uint needed);
        [DllImport("kernel32.dll")] public static extern uint SetThreadExecutionState(uint flags);
        [DllImport("kernel32.dll")] public static extern bool GetSystemPowerStatus(out SYSTEM_POWER_STATUS s);
        // מחזיר BOOLEAN (בית אחד), ולכן byte ולא bool
        [DllImport("powrprof.dll")] public static extern byte GetPwrCapabilities(IntPtr buf);
    }

    static class Power
    {
        const uint ES_CONTINUOUS = 0x80000000;
        const uint ES_SYSTEM_REQUIRED = 0x00000001;
        const uint ES_DISPLAY_REQUIRED = 0x00000002;

        public static void KeepAwake(bool allowScreenOff)
        {
            uint flags = ES_CONTINUOUS | ES_SYSTEM_REQUIRED;
            if (!allowScreenOff) flags |= ES_DISPLAY_REQUIRED;
            Native.SetThreadExecutionState(flags);
        }

        public static void Release() { Native.SetThreadExecutionState(ES_CONTINUOUS); }

        // מדליק את המסך אם כבה: איפוס טיימר המסך ותזוזת עכבר של פיקסל אחד הלוך וחזור
        public static void WakeDisplay()
        {
            Native.SetThreadExecutionState(ES_DISPLAY_REQUIRED);
            Native.mouse_event(0x0001, 1, 0, 0, UIntPtr.Zero);
            Native.mouse_event(0x0001, -1, 0, 0, UIntPtr.Zero);
        }

        // True במחשבים שבהם כיבוי המסך הוא כניסה לשינה (Modern Standby / S0)
        public static bool IsModernStandby()
        {
            IntPtr p = IntPtr.Zero;
            try
            {
                p = Marshal.AllocHGlobal(256);
                for (int i = 0; i < 256; i++) Marshal.WriteByte(p, i, 0);
                if (Native.GetPwrCapabilities(p) == 0) return true; // לא הצלחנו לבדוק - מתנהגים בזהירות
                return Marshal.ReadByte(p, 20) != 0;                // השדה AoAc במבנה SYSTEM_POWER_CAPABILITIES
            }
            catch { return true; }
            finally { if (p != IntPtr.Zero) Marshal.FreeHGlobal(p); }
        }

        // False אם אין סוללה
        public static bool Battery(out int percent, out bool pluggedIn)
        {
            percent = 100; pluggedIn = true;
            try
            {
                SYSTEM_POWER_STATUS s;
                if (!Native.GetSystemPowerStatus(out s)) return false;
                if (s.BatteryFlag == 128 || s.BatteryLifePercent == 255) return false;
                percent = s.BatteryLifePercent;
                pluggedIn = s.ACLineStatus == 1;
                return true;
            }
            catch { return false; }
        }

        // True אם המחשב נעול (מסך הכניסה פעיל), ואז אי אפשר להקליד לדפדפן
        public static bool IsLocked()
        {
            IntPtr d = Native.OpenInputDesktop(0, false, 0x0001);
            if (d == IntPtr.Zero) return true;
            try
            {
                var sb = new StringBuilder(256);
                uint needed;
                bool ok = Native.GetUserObjectInformation(d, 2, sb, 512, out needed);
                return ok && !sb.ToString().Equals("default", StringComparison.OrdinalIgnoreCase);
            }
            finally { Native.CloseDesktop(d); }
        }

        public static string ForegroundProcessName()
        {
            try
            {
                IntPtr h = Native.GetForegroundWindow();
                if (h == IntPtr.Zero) return "";
                uint pid; Native.GetWindowThreadProcessId(h, out pid);
                return Process.GetProcessById((int)pid).ProcessName;
            }
            catch { return ""; }
        }
    }

    // ---------------------------------------------------------------- Windows Task Scheduler
    // רושם משימה חד-פעמית שמעירה את המחשב משינה בשעת השליחה, ומפעילה את התוכנה (--run-schedule) אם היא כבר לא רצה
    // (למשל אחרי הפעלה מחדש). אם התוכנה כן רצה - היא שולחת בעצמה והמשימה רק מעירה את המחשב.
    // אין StartWhenAvailable: אם המחשב היה כבוי בשעה הזו - לא שולחים באיחור.
    static class TaskSched
    {
        const string Folder = "\\ClaudeNotify\\";

        static void RunPs(string script, bool wait)
        {
            try
            {
                string enc = Convert.ToBase64String(Encoding.Unicode.GetBytes("$ErrorActionPreference='Stop'\r\n" + script));
                var psi = new ProcessStartInfo("powershell", "-NoProfile -NonInteractive -EncodedCommand " + enc);
                psi.UseShellExecute = false;
                psi.CreateNoWindow = true;
                psi.RedirectStandardError = true;
                psi.RedirectStandardOutput = true;
                using (var p = Process.Start(psi))
                {
                    var err = p.StandardError.ReadToEndAsync();
                    p.StandardOutput.ReadToEndAsync();
                    if (!wait) return;
                    if (!p.WaitForExit(20000)) { try { p.Kill(); } catch { } Log.Write("task scheduler: timeout"); return; }
                    if (p.ExitCode != 0) Log.Write("task scheduler: " + err.Result.Trim());
                }
            }
            catch (Exception ex) { Log.Write("task scheduler error: " + ex.Message); }
        }

        static string Q(string s) { return "'" + s.Replace("'", "''") + "'"; }

        public static void Register(DateTime at, bool wake)
        {
            string exe = Application.ExecutablePath;
            RunPs(
                "$a = New-ScheduledTaskAction -Execute " + Q(exe) + " -Argument '--run-schedule'\r\n" +
                "$t = New-ScheduledTaskTrigger -Once -At ([datetime]" + Q(at.ToString("yyyy-MM-dd'T'HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture)) + ")\r\n" +
                "$st = New-ScheduledTaskSettingsSet -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries -ExecutionTimeLimit (New-TimeSpan -Minutes 10) -MultipleInstances IgnoreNew" + (wake ? " -WakeToRun" : "") + "\r\n" +
                "$p = New-ScheduledTaskPrincipal -UserId $env:USERNAME -LogonType Interactive\r\n" +
                "Register-ScheduledTask -TaskPath " + Q(Folder) + " -TaskName 'send' -Trigger $t -Action $a -Settings $st -Principal $p -Force | Out-Null", true);
        }

        public static void Remove()
        {
            RunPs("Unregister-ScheduledTask -TaskPath " + Q(Folder) + " -TaskName 'send' -Confirm:$false -ErrorAction SilentlyContinue", false);
        }
    }

    // ---------------------------------------------------------------- חלונות VS Code וכותרות שיחה
    class VsWin
    {
        public long Handle;
        public string Title;   // כותרת מנוקה מתווי כיווניות
        public string Key;     // כותרת + "#" + מספר מופע בין החלונות עם אותה כותרת (לפי סדר Z)
        public int Index, Count;
        public string Conv = "";   // כותרת השיחה בחלון, אם זוהתה
    }

    static class VsWindows
    {
        public static string Key(string title, int index) { return title + "#" + index; }

        public static string TitleOfKey(string key)
        {
            int i = key.LastIndexOf('#');
            return i > 0 ? key.Substring(0, i) : key;
        }

        // מפתח בלי "#מספר" (הגדרה ישנה) נחשב כמופע הראשון
        static string Norm(string key)
        {
            int i = key.LastIndexOf('#');
            int n;
            if (i > 0 && int.TryParse(key.Substring(i + 1), out n)) return key;
            return key + "#0";
        }

        public static List<VsWin> List()
        {
            var res = new List<VsWin>();
            var counts = new Dictionary<string, int>();
            foreach (var w in WinApi.Find(Config.Load().ProcessList()))
            {
                string t = WinApi.CleanTitle(w.Title);
                int n; counts.TryGetValue(t, out n);
                counts[t] = n + 1;
                res.Add(new VsWin { Handle = w.Handle, Title = t, Index = n, Key = Key(t, n) });
            }
            foreach (var w in res) w.Count = counts[w.Title];
            return res;
        }

        // מפתח -> חלון נוכחי. כשיש כמה חלונות עם אותה כותרת ונשמרה כותרת שיחה - מעדיפים את החלון שמציג אותה
        public static VsWin Resolve(List<VsWin> wins, string key, string conv)
        {
            key = Norm(key);
            string title = TitleOfKey(key);
            var same = new List<VsWin>();
            foreach (var w in wins) if (w.Title == title) same.Add(w);
            if (same.Count == 0) return null;
            if (same.Count > 1 && !string.IsNullOrEmpty(conv))
            {
                foreach (var w in same)
                    if (ConvTitles.HasTitle(w.Handle, conv)) return w;
            }
            foreach (var w in same) if (w.Key == key) return w;
            return null;
        }

        public static string LabelOf(long handle)
        {
            foreach (var w in List())
                if (w.Handle == handle) return w.Count > 1 ? w.Title + " (" + (w.Index + 1) + ")" : w.Title;
            return "";
        }
    }

    // כותרת השיחה (aiTitle) מוצגת בפאנל של Claude בכל חלון ככפתור ששמו הכותרת. מחפשים כפתורים ששמם שווה לכותרת ידועה מהתמלולים
    static class ConvTitles
    {
        static readonly Dictionary<string, KeyValuePair<DateTime, string>> cache = new Dictionary<string, KeyValuePair<DateTime, string>>();

        // כותרות השיחות של הקבצים האחרונים בתיקיית התמלולים (עם מטמון לפי זמן שינוי)
        public static HashSet<string> Known()
        {
            var set = new HashSet<string>();
            try
            {
                string root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), @".claude\projects");
                if (!Directory.Exists(root)) return set;
                var files = new List<FileInfo>();
                foreach (var f in Directory.GetFiles(root, "*.jsonl", SearchOption.AllDirectories)) files.Add(new FileInfo(f));
                files.Sort(delegate(FileInfo a, FileInfo b) { return b.LastWriteTimeUtc.CompareTo(a.LastWriteTimeUtc); });
                for (int i = 0; i < files.Count && i < 120; i++)
                {
                    string t = null;
                    lock (cache)
                    {
                        KeyValuePair<DateTime, string> kv;
                        if (cache.TryGetValue(files[i].FullName, out kv) && kv.Key == files[i].LastWriteTimeUtc) t = kv.Value;
                    }
                    if (t == null)
                    {
                        t = TextUtil.TitleFromTranscript(files[i].FullName);
                        lock (cache) cache[files[i].FullName] = new KeyValuePair<DateTime, string>(files[i].LastWriteTimeUtc, t);
                    }
                    if (t.Length > 0) set.Add(t);
                }
            }
            catch (Exception ex) { Log.Write("conv titles: " + ex.Message); }
            return set;
        }

        // כותרת השיחה המוצגת בחלון, או "" אם לא זוהתה
        public static string ForWindow(long hwnd, HashSet<string> known)
        {
            try
            {
                var root = AutomationElement.FromHandle(new IntPtr(hwnd));
                var cr = new CacheRequest();
                cr.Add(AutomationElement.NameProperty);
                cr.Add(AutomationElement.IsOffscreenProperty);
                var cond = new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Button);
                AutomationElementCollection all;
                using (cr.Activate()) all = root.FindAll(TreeScope.Descendants, cond);
                string found = "";
                foreach (AutomationElement e in all)
                {
                    string n = e.Cached.Name;
                    if (string.IsNullOrEmpty(n) || !known.Contains(n)) continue;
                    if (!e.Cached.IsOffscreen) return n;
                    if (found.Length == 0) found = n;
                }
                return found;
            }
            catch (Exception ex) { Log.Write("conv title uia: " + ex.Message); }
            return "";
        }

        public static bool HasTitle(long hwnd, string title)
        {
            try
            {
                var root = AutomationElement.FromHandle(new IntPtr(hwnd));
                var cond = new AndCondition(
                    new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Button),
                    new PropertyCondition(AutomationElement.NameProperty, title));
                return root.FindFirst(TreeScope.Descendants, cond) != null;
            }
            catch { return false; }
        }
    }

    // ---------------------------------------------------------------- Schedule runner
    // רץ כולו על ה-thread של הממשק (טיימר אחד), כמו ב-tkinter של הסקריפט המקורי.
    // הוא שייך ל-TrayContext ולא לחלון, ולכן סגירת החלון לא מבטלת את התזמון.
    class ScheduleRunner
    {
        enum Phase { Idle, WaitStart, WaitPage, Typing, Done, Sending, VsInput, VsEnter, VsVerify, VsGap }
        class FocusLost : Exception { }

        // בודק דרך UI Automation של Windows אם הפוקוס נמצא בתוך דף אינטרנט (רכיב Document) ולא בשורת הכתובת.
        // PAGE = שדה בדף, OUT = שורת הכתובת, UIAERR = הבדיקה רצה אבל הדף עוד לא ענה (קורה בזמן טעינה).
        const string FocusCheckPs = @"
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes
try {
    $doc = [System.Windows.Automation.ControlType]::Document
    $w = [System.Windows.Automation.TreeWalker]::ControlViewWalker
    $e = [System.Windows.Automation.AutomationElement]::FocusedElement
    for ($i = 0; $i -lt 50 -and $e -ne $null; $i++) {
        if ($e.Current.ControlType -eq $doc) { 'PAGE'; exit }
        $e = $w.GetParent($e)
    }
    'OUT'
} catch {
    'UIAERR'
}
";
        const int CheckFailuresLimit = 2;   // אחרי כמה כשלים רצופים של PowerShell מוותרים
        const int RetryEveryMs = 2000;
        const byte VK_CONTROL = 0x11, VK_V = 0x56, VK_TAB = 0x09, VK_RETURN = 0x0D;
        const byte VK_A = 0x41, VK_C = 0x43, VK_L = 0x4C, VK_F6 = 0x75, VK_END = 0x23;

        readonly Control host;
        readonly System.Windows.Forms.Timer timer = new System.Windows.Forms.Timer();
        readonly bool modernStandby = Power.IsModernStandby();

        Phase phase = Phase.Idle;
        SchedConfig c;
        BrowserProfile profile;
        DateTime deadline, typeDeadline;
        string prefix = "";
        int checkFailures, runId;
        IntPtr browserHwnd;
        long vsHandle;
        List<long> vsQueue = new List<long>();      // חלונות שעוד לא טופלו (לפי הסדר)
        List<string> vsFailed = new List<string>();
        int vsTotal, vsOk, vsIndex;
        string vsLabel = "", vsFirstErr = "";
        bool taskRegistered;
        DateTime vsStart;
        int vsTries, vsPasteTries;
        AutomationElement vsInput;
        DateTime vsEnterAt;
        DateTime vsDeadline, vsNewInputUntil;
        HashSet<string> vsOldInputs = new HashSet<string>();
        string oldClip;
        bool wantAwake, awakeOn, allowScreenOff;

        public string PowerWarning = "";
        public bool Cancelled;
        public DateTime Target;
        public bool Running { get { return phase != Phase.Idle; } }
        public bool ModernStandby { get { return modernStandby; } }

        public event Action<string, bool> Status;     // טקסט, האם שגיאה
        public event Action<string, bool> Finished;   // טקסט, האם שגיאה
        public event Action Started;
        public event Action Launching;                // הדפדפן עומד להיפתח - החלונות שלנו צריכים להתרחק

        public ScheduleRunner(Control host)
        {
            this.host = host;
            timer.Tick += OnTick;
        }

        // מחזיר null אם התחיל, או הסבר על מה שחסר
        public string Start(SchedConfig cfg) { return Start(cfg, false); }

        // immediate = הופעל ע"י משימת Windows בדיוק בשעת השליחה: לא מחכים ולא רושמים משימה חדשה
        public string Start(SchedConfig cfg, bool immediate)
        {
            if (Running) return "כבר יש תזמון פעיל";
            profile = null;
            if (string.IsNullOrWhiteSpace(cfg.Text)) return "צריך להקליד טקסט לשליחה";
            if (cfg.Method == "vscode" && (cfg.WindowKeys == null || cfg.WindowKeys.Count == 0) && string.IsNullOrEmpty(cfg.WindowTitle)) return "צריך לבחור לפחות חלון VS Code אחד (אם הרשימה ריקה - פתח את החלון ולחץ על הכפתור 'רענן')";
            if (cfg.Method == "browser")
            {
                foreach (var p in BrowserProfiles.List()) if (p.Key == cfg.ProfileKey) profile = p;
                if (profile == null) return "צריך לבחור פרופיל של Chrome, Edge או Brave";
                Uri u;
                if (!Uri.TryCreate(cfg.Url ?? "", UriKind.Absolute, out u) || (u.Scheme != "http" && u.Scheme != "https"))
                    return "כתובת האתר חייבת להתחיל ב-https://";
                cfg.Url = u.AbsoluteUri;
            }

            DateTime target;
            string when;
            if (immediate) { target = DateTime.Now; when = "עכשיו."; }
            else if (cfg.UseTime)
            {
                TimeSpan t;
                if (!TimeSpan.TryParse(cfg.AtTime, out t)) return "השעה לא תקינה";
                target = DateTime.Today + t;
                if (target <= DateTime.Now) target = target.AddDays(1);
                when = (target.Date == DateTime.Today ? "היום" : "מחר") + " ב-" + target.ToString("HH:mm:ss") + ". נשאר";
            }
            else
            {
                target = DateTime.Now.AddSeconds(cfg.DelaySeconds);
                when = "בעוד";
            }

            c = cfg;
            Cancelled = false;
            Target = target;
            deadline = target;
            prefix = (cfg.Method == "browser" ? "הדפדפן ייפתח " : "ההודעה תישלח ") + when;
            wantAwake = cfg.KeepAwake;
            allowScreenOff = cfg.AllowScreenOff && !modernStandby;
            awakeOn = false;
            oldClip = null;
            runId++;
            phase = Phase.WaitStart;
            taskRegistered = false;
            if (!immediate && (target - DateTime.Now).TotalSeconds >= 90)
            {
                TaskSched.Register(target, cfg.Wake);
                taskRegistered = true;
            }
            UpdateAwake();
            if (Started != null) Started();
            OnTick(null, EventArgs.Empty);
            return null;
        }

        public void Cancel()
        {
            if (!Running) return;
            Cancelled = true;
            RestoreClipboard();
            Finish("בוטל", false);
        }

        // ---- לולאת הזמן ----
        void Next(int ms)
        {
            if (phase == Phase.Idle) return;
            timer.Interval = Math.Max(50, ms);
            timer.Start();
        }

        void OnTick(object sender, EventArgs e)
        {
            timer.Stop();
            try
            {
                switch (phase)
                {
                    case Phase.WaitStart: Countdown(OnStart); break;
                    case Phase.WaitPage: Countdown(TryType); break;
                    case Phase.Typing: TryType(); break;
                    case Phase.VsInput: VsFillInput(); break;
                    case Phase.VsEnter: VsPressEnter(); break;
                    case Phase.VsVerify: VsVerify(); break;
                    case Phase.VsGap: VsNext(); break;
                    case Phase.Done:
                        RestoreClipboard();
                        if (c.Method == "vscode") VsAdvance(true, null);
                        else Finish("בוצע!", false);
                        break;
                }
            }
            catch (Exception ex)
            {
                Log.Write("schedule error: " + ex);
                RestoreClipboard();
                // תקלה בחלון אחד לא עוצרת את שאר החלונות
                if (c != null && c.Method == "vscode" && phase != Phase.Idle) VsAdvance(false, "שגיאה לא צפויה: " + ex.Message);
                else Finish("שגיאה לא צפויה: " + ex.Message, true);
            }
        }

        // מחשבים לפי השעון בכל פעם, כדי שלא תהיה סטייה בהמתנה של שעות
        void Countdown(Action then)
        {
            double left = (deadline - DateTime.Now).TotalSeconds;
            if (left < -120)
            {
                // השעון קפץ קדימה = המחשב בכל זאת נרדם (למשל בגלל סוללה נמוכה או סגירת מכסה)
                Finish("המחשב היה במצב שינה ופספס את השעה, ולכן לא נעשה כלום", true);
                return;
            }
            UpdateAwake();
            if (left <= 0) { then(); return; }
            SetStatus(prefix + " " + FmtLeft(left), false);
            Next((int)Math.Min(1000, left * 1000 + 1));
        }

        void OnStart()
        {
            if (c.Method == "cli") { SendViaClaudeCode(); return; }
            if (c.Method == "vscode") { SendToVsCodeWindow(); return; }

            Power.WakeDisplay();
            if (Power.IsLocked()) { Finish("המחשב היה נעול בשעת ההפעלה, ולכן לא נעשה כלום", true); return; }

            // הדפדפן נפתח רגיל לגמרי, בלי שום סימון של אוטומציה
            var psi = new ProcessStartInfo(profile.Exe, "\"--profile-directory=" + profile.Folder + "\" \"" + c.Url + "\"");
            psi.UseShellExecute = false;
            Process.Start(psi);
            if (Launching != null) Launching();

            oldClip = ReadClipboard();
            checkFailures = 0;
            typeDeadline = DateTime.Now.AddSeconds(c.MaxWaitSeconds);
            phase = Phase.WaitPage;
            deadline = DateTime.Now.AddSeconds(c.MinWaitSeconds);
            prefix = "ממתין לטעינת הדף:";
            Countdown(TryType);
        }

        // ---- שליחה לחלונות VS Code שנבחרו ----
        // עוברים על החלונות אחד אחד: מעלים את החלון לחזית, פותחים בו שיחה חדשה דרך הכתובת vscode://anthropic.claude-code/open
        // (או ממשיכים בשיחה הפעילה), מדביקים את הטקסט ב"Message input" ולוחצים Enter. כשל בחלון אחד לא עוצר את האחרים
        void SendToVsCodeWindow()
        {
            Power.WakeDisplay();
            if (Power.IsLocked()) { Finish("המחשב היה נעול בשעת ההפעלה, ולכן לא נעשה כלום", true); return; }
            VsBegin();
        }

        void VsBegin()
        {
            vsQueue = new List<long>();
            vsFailed = new List<string>();
            vsOk = 0; vsIndex = 0; vsFirstErr = "";
            var keys = c.WindowKeys;
            if (keys == null || keys.Count == 0)
            {
                keys = new List<string>();
                if (!string.IsNullOrEmpty(c.WindowTitle)) keys.Add(VsWindows.Key(c.WindowTitle, 0));
            }
            var wins = VsWindows.List();
            var seen = new HashSet<long>();
            var missing = new List<string>();
            foreach (var k in keys)
            {
                string conv = null;
                if (c.WindowConvs != null) c.WindowConvs.TryGetValue(k, out conv);
                var w = VsWindows.Resolve(wins, k, conv);
                if (w == null || !seen.Add(w.Handle)) { if (w == null) missing.Add(VsWindows.TitleOfKey(k)); continue; }
                vsQueue.Add(w.Handle);
            }
            vsTotal = vsQueue.Count + missing.Count;
            if (vsQueue.Count == 0)
            {
                if (missing.Count == 1) Finish("חלון ה-VS Code שנבחר (\"" + missing[0] + "\") כבר לא פתוח, ולכן לא נשלח כלום", true);
                else Finish("חלונות ה-VS Code שנבחרו כבר לא פתוחים, ולכן לא נשלח כלום", true);
                return;
            }
            foreach (var m in missing) vsFailed.Add(m + " - " + L.Tr("החלון כבר לא פתוח"));
            if (missing.Count > 0) vsFirstErr = "החלון כבר לא פתוח";
            if (Launching != null) Launching();   // החלון שלנו נעלם כדי שלא יגנוב פוקוס
            VsNext();
        }

        void VsStatus(string text)
        {
            SetStatus(vsTotal > 1 ? "[" + vsIndex + "/" + vsTotal + "] " + text : text, false);
        }

        // לוקח את החלון הבא מהתור ומתחיל בו
        void VsNext()
        {
            if (vsQueue.Count == 0) { VsFinishAll(); return; }
            long h = vsQueue[0];
            vsQueue.RemoveAt(0);
            vsIndex = vsOk + vsFailed.Count + 1;
            vsLabel = VsWindows.LabelOf(h);
            string scheme = WinApi.SchemeFor(h);
            if (scheme == null) { VsAdvance(false, "לא הצלחתי לזהות איזה עורך זה (Code או Cursor)"); return; }
            if (!WinApi.Focus(h)) { VsAdvance(false, "לא הצלחתי להעלות את חלון ה-VS Code לחזית (Windows חסם), ולכן לא נשלח כלום"); return; }
            Thread.Sleep(500); // ל-VS Code לוקח רגע לרשום שזה החלון הפעיל, ושם ה-URI מנותב
            if (Native.GetForegroundWindow().ToInt64() != h) { VsAdvance(false, "החלון שנבחר איבד פוקוס לפני השליחה, ולכן לא נשלח כלום"); return; }

            vsHandle = h;
            vsInput = null;
            vsStart = DateTime.Now.AddSeconds(-2);
            vsTries = 0;
            vsPasteTries = 0;
            oldClip = ReadClipboard();
            if (c.NewChat)
            {
                vsOldInputs = CurrentInputs(h);
                vsNewInputUntil = DateTime.Now.AddSeconds(8);   // 8 שניות ראשונות: מחכים לתיבה של השיחה החדשה ולא נוגעים בתיבות שהיו קודם
                vsDeadline = DateTime.Now.AddSeconds(30);
                var psi = new ProcessStartInfo(scheme + "://anthropic.claude-code/open");
                psi.UseShellExecute = true;
                Process.Start(psi);
                Log.Write("schedule: opened new chat in window [" + vsLabel + "]");
                phase = Phase.VsInput;
                VsStatus("ממתין שהשיחה תיפתח בחלון...");
                Next(2500);
            }
            else
            {
                // ממשיכים בשיחה הפעילה: משתמשים בתיבה שכבר גלויה (עדיפות לזו שבפוקוס)
                vsOldInputs = new HashSet<string>();
                vsNewInputUntil = DateTime.Now;
                vsDeadline = DateTime.Now.AddSeconds(15);
                Log.Write("schedule: continuing active chat in window [" + vsLabel + "]");
                phase = Phase.VsInput;
                VsStatus("מחפש את תיבת ההקלדה בחלון...");
                Next(300);
            }
        }

        // רושם את תוצאת החלון הנוכחי וממשיך לבא אחריו (אחרי הפסקה קצרה), או מסיים
        void VsAdvance(bool ok, string err)
        {
            timer.Stop();
            RestoreClipboard();
            vsInput = null;
            if (ok) vsOk++;
            else
            {
                Log.Write("schedule: window [" + vsLabel + "] failed: " + err);
                if (vsFirstErr.Length == 0) vsFirstErr = err;
                vsFailed.Add(vsLabel + " - " + L.Tr(err));
            }
            if (vsQueue.Count > 0)
            {
                phase = Phase.VsGap;
                VsStatus("ממתין לפני החלון הבא...");
                Next(3000);
                return;
            }
            VsFinishAll();
        }

        void VsFinishAll()
        {
            if (vsTotal <= 1)
            {
                if (vsFailed.Count == 0) Finish("בוצע!", false);
                else
                {
                    Finish(vsFirstErr, true);
                }
                return;
            }
            string head = L.T("נשלח ל-" + vsOk + " מתוך " + vsTotal + " חלונות", "Sent to " + vsOk + " of " + vsTotal + " windows");
            if (vsFailed.Count == 0) Finish(L.T("בוצע! ", "Done! ") + head, false);
            else Finish(head + L.T(". נכשל: ", ". Failed: ") + string.Join(" | ", vsFailed.ToArray()), true);
        }

        // ---- מילוי תיבת ההקלדה של Claude בחלון שנבחר ----
        // כתובת ה-URI עם prompt לא ממלאת את התיבה בפועל, ולכן מוצאים את התיבה ("Message input") דרך UI Automation,
        // מדביקים אליה את הטקסט, ובודקים שהוא באמת נכנס לפני שלוחצים Enter
        void VsFillInput()
        {
            if (Native.GetForegroundWindow().ToInt64() != vsHandle) WinApi.Focus(vsHandle);
            AutomationElement input = null;
            bool allowOld = DateTime.Now > vsNewInputUntil;
            try { input = FindMessageInput(vsHandle, allowOld ? null : vsOldInputs); }
            catch (Exception ex) { Log.Write("uia error: " + ex.Message); }
            if (input == null)
            {
                if (DateTime.Now > vsDeadline) { VsAdvance(false, "לא מצאתי תיבת הקלדה של Claude בחלון שנבחר, ולכן לא נשלח כלום"); return; }
                Next(1000);
                return;
            }
            browserHwnd = new IntPtr(vsHandle);
            try
            {
                try { input.SetFocus(); } catch { }
                Thread.Sleep(250);
                bool focused = false;
                try { focused = input.Current.HasKeyboardFocus; } catch { }
                if (focused)
                {
                    SetClipboard(c.Text);
                    Key(VK_A, true, false);   // רק אחרי שווידאנו שהפוקוס בתיבה עצמה, כדי לא לדרוס קובץ בעורך
                    Key(VK_V, true, false);
                    Thread.Sleep(500);
                }
            }
            catch (FocusLost) { VsAdvance(false, "חלון אחר קפץ לחזית לפני ההקלדה, ולכן לא נשלח כלום"); return; }

            string val = InputValue(input);
            if (val != null && Norm(val) == Norm(c.Text))
            {
                vsInput = input;
                Log.Write("schedule: text is in the input, pressing Enter");
                phase = Phase.VsEnter;
                Next(200);
                return;
            }
            vsPasteTries++;
            Log.Write("schedule: paste attempt " + vsPasteTries + " value=[" + val + "]");
            if (vsPasteTries >= 4) { VsAdvance(false, "מצאתי את תיבת ההקלדה אבל לא הצלחתי להכניס אליה את הטקסט, ולכן לא נשלח כלום"); return; }
            Next(1000);
        }

        // מחזיר את תיבת ההקלדה הנראית בחלון (עדיפות לזו שיש בה פוקוס). exclude = תיבות שהיו כבר לפני שפתחנו שיחה חדשה
        static AutomationElement FindMessageInput(long hwnd, HashSet<string> exclude)
        {
            var root = AutomationElement.FromHandle(new IntPtr(hwnd));
            var cond = new AndCondition(
                new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Edit),
                new PropertyCondition(AutomationElement.NameProperty, "Message input"));
            AutomationElement best = null;
            foreach (AutomationElement e in root.FindAll(TreeScope.Descendants, cond))
            {
                if (e.Current.IsOffscreen) continue;
                if (exclude != null && exclude.Contains(RuntimeKey(e))) continue;
                if (e.Current.HasKeyboardFocus) return e;
                if (best == null) best = e;
            }
            return best;
        }

        static HashSet<string> CurrentInputs(long hwnd)
        {
            var set = new HashSet<string>();
            try
            {
                var root = AutomationElement.FromHandle(new IntPtr(hwnd));
                var cond = new AndCondition(
                    new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Edit),
                    new PropertyCondition(AutomationElement.NameProperty, "Message input"));
                foreach (AutomationElement e in root.FindAll(TreeScope.Descendants, cond)) set.Add(RuntimeKey(e));
            }
            catch (Exception ex) { Log.Write("uia error: " + ex.Message); }
            return set;
        }

        static string RuntimeKey(AutomationElement e)
        {
            int[] id = e.GetRuntimeId();
            return id == null ? "" : string.Join(",", Array.ConvertAll(id, x => x.ToString()));
        }

        static string InputValue(AutomationElement e)
        {
            try { return ((ValuePattern)e.GetCurrentPattern(ValuePattern.Pattern)).Current.Value; }
            catch { return null; }
        }

        void VsPressEnter()
        {
            if (Power.IsLocked()) { VsAdvance(false, "המחשב ננעל לפני השליחה. הטקסט הוכנס לחלון אבל לא נשלח"); return; }
            browserHwnd = new IntPtr(vsHandle);
            if (Native.GetForegroundWindow() != browserHwnd) WinApi.Focus(vsHandle);
            try { Key(VK_RETURN, false, false); }
            catch (FocusLost) { VsAdvance(false, "חלון אחר קפץ לחזית לפני השליחה. הטקסט הוכנס לחלון אבל לא נשלח"); return; }
            vsTries++;
            if (vsTries == 1) vsEnterAt = DateTime.Now;
            phase = Phase.VsVerify;
            SetStatus("בודק שההודעה נשלחה...", false);
            Next(1500);
        }

        // בודקים שההודעה נשלחה, בלי ללחוץ Enter שוב בטעות (Enter שני קוטע את התשובה של Claude):
        // 1. נמצאה בתמלול = נשלחה.
        // 2. הטקסט עדיין יושב בתיבה = ה-Enter לא נקלט, ולוחצים שוב (עד 3 פעמים).
        // 3. התיבה התרוקנה אבל התמלול עוד לא התעדכן = נשלחה, ממשיכים לחכות לתמלול (עד 25 שניות מה-Enter הראשון)
        void VsVerify()
        {
            if (PromptInTranscripts(c.Text, vsStart)) { phase = Phase.Done; Next(500); return; }

            string val = vsInput != null ? InputValue(vsInput) : null;
            bool stillInBox = val != null && Norm(val) == Norm(c.Text);
            if (stillInBox)
            {
                if (vsTries >= 3) { VsAdvance(false, "לחצתי Enter, אבל ההודעה נשארה בתיבה ולא נשלחה. בדוק בחלון ה-VS Code"); return; }
                Log.Write("schedule: text still in the input after Enter " + vsTries + ", pressing again");
                phase = Phase.VsEnter;
                Next(200);
                return;
            }
            if ((DateTime.Now - vsEnterAt).TotalSeconds > 25)
            {
                // התיבה התרוקנה, כלומר ההודעה יצאה, גם אם לא הצלחנו לאשר בתמלול
                Log.Write("schedule: input cleared but no transcript match");
                phase = Phase.Done;
                Next(500);
                return;
            }
            Next(1000);
        }

        static bool PromptInTranscripts(string text, DateTime since)
        {
            try
            {
                string root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), @".claude\projects");
                if (!Directory.Exists(root)) return false;
                var ser = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };
                string want = Norm(text);
                foreach (var f in Directory.GetFiles(root, "*.jsonl", SearchOption.AllDirectories))
                {
                    if (File.GetLastWriteTime(f) < since) continue;
                    using (var fs = new FileStream(f, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                    using (var sr = new StreamReader(fs, Encoding.UTF8))
                    {
                        string line;
                        while ((line = sr.ReadLine()) != null)
                        {
                            if (line.IndexOf("\"user\"", StringComparison.Ordinal) < 0) continue;
                            try
                            {
                                var o = ser.Deserialize<Dictionary<string, object>>(line);
                                if (TextUtil.Str(o, "type") != "user") continue;
                                // הודעה זהה שנשלחה קודם (למשל לחלון הקודם בתור) לא נחשבת
                                DateTime ts;
                                if (DateTime.TryParse(TextUtil.Str(o, "timestamp"), System.Globalization.CultureInfo.InvariantCulture,
                                    System.Globalization.DateTimeStyles.AdjustToUniversal | System.Globalization.DateTimeStyles.AssumeUniversal, out ts)
                                    && ts < since.ToUniversalTime()) continue;
                                var msg = o["message"] as Dictionary<string, object>;
                                if (msg == null) continue;
                                object content = msg["content"];
                                string got = content as string;
                                if (got == null && content is System.Collections.IEnumerable)
                                    foreach (var part in (System.Collections.IEnumerable)content)
                                    {
                                        var pd = part as Dictionary<string, object>;
                                        if (pd != null && TextUtil.Str(pd, "type") == "text") got += TextUtil.Str(pd, "text") + " ";
                                    }
                                if (got != null && Norm(got) == want) return true;
                            }
                            catch { }
                        }
                    }
                }
            }
            catch (Exception ex) { Log.Write("verify error: " + ex.Message); }
            return false;
        }

        // ---- שליחה דרך Claude Code (בלי דפדפן) ----
        void SendViaClaudeCode()
        {
            phase = Phase.Sending;
            SetStatus("שולח הודעה דרך Claude Code...", false);
            int id = runId;
            string text = c.Text;
            ThreadPool.QueueUserWorkItem(delegate
            {
                string err;
                string reply = Summarizer.Ask(text, 120000, out err);
                host.BeginInvoke(new Action(delegate
                {
                    if (id != runId || phase != Phase.Sending) return; // בוטל בינתיים
                    if (reply != null) Finish("ההודעה נשלחה דרך Claude Code!", false);
                    else Finish("השליחה דרך Claude Code נכשלה: " + err, true);
                }));
            });
        }

        // ---- הקלדה בדפדפן ----
        // מנסה להקליד. אם הדף עוד לא מוכן - מנסה שוב כל כמה שניות, עד המקסימום
        void TryType()
        {
            phase = Phase.Typing;
            UpdateAwake();
            Power.WakeDisplay();
            if (Power.IsLocked())
            {
                RestoreClipboard();
                Finish("הדפדפן נפתח, אבל המחשב נעול ולכן לא הוקלד כלום", true);
                return;
            }

            if (AttemptPaste())
            {
                if (c.SendEnter)
                {
                    try
                    {
                        Key(VK_END, true, true);   // מבטלים את סימון הטקסט, כדי ש-Enter לא ימחק אותו
                        Thread.Sleep(200);
                        Key(VK_RETURN, false, false);
                    }
                    catch (FocusLost)
                    {
                        RestoreClipboard();
                        Finish("הטקסט נכתב, אבל חלון אחר קפץ לחזית לפני השליחה, ולכן לא נשלח", true);
                        return;
                    }
                }
                phase = Phase.Done;
                Next(1000);
                return;
            }

            if (checkFailures >= CheckFailuresLimit)
            {
                RestoreClipboard();
                Finish("לא הצלחתי לבדוק איפה הסמן נמצא. כנראה PowerShell חסום במחשב הזה (למשל על ידי אנטי-וירוס), ולכן לא נשלח כלום", true);
                return;
            }

            double left = (typeDeadline - DateTime.Now).TotalSeconds;
            if (left <= 0)
            {
                RestoreClipboard();
                Finish("הדף לא היה מוכן להקלדה בזמן שהוגדר, ולכן לא נשלח כלום", true);
                return;
            }
            SetStatus("ממתין שהדף יהיה מוכן... נשאר " + FmtLeft(left), false);
            Next(RetryEveryMs);
        }

        // מדביק את הטקסט ובודק שהוא באמת נכנס לשדה. מחזיר True אם הצליח
        bool AttemptPaste()
        {
            string browserExe = Path.GetFileNameWithoutExtension(profile.Exe);
            if (!Power.ForegroundProcessName().Equals(browserExe, StringComparison.OrdinalIgnoreCase))
            {
                // Windows לא תמיד מעלה את חלון הדפדפן לחזית כשהוא נפתח מתהליך ברקע. מחפשים בעצמנו את החלון
                // של claude.ai (לפי הכותרת) ומעלים אותו. לא מצאנו - לא מקלידים בתוכנה אחרת
                WinInfo pick = null;
                foreach (var w in WinApi.Find(new[] { browserExe }))
                {
                    if (w.Title.IndexOf("claude", StringComparison.OrdinalIgnoreCase) < 0) continue;
                    if (pick == null || (profile.Label.Length > 0 && w.Title.IndexOf(profile.Label, StringComparison.OrdinalIgnoreCase) >= 0)) pick = w;
                }
                if (pick == null || !WinApi.Focus(pick.Handle)) return false;
            }
            browserHwnd = Native.GetForegroundWindow();
            if (!Power.ForegroundProcessName().Equals(browserExe, StringComparison.OrdinalIgnoreCase))
                return false;
            try
            {
                if (c.TabCount > 0)
                {
                    // מחזירים את הפוקוס לתחילת הדף (שורת הכתובת ואז F6), ומשם סופרים את ה-Tab
                    Key(VK_L, true, false);
                    Key(VK_F6, false, false);
                    for (int i = 0; i < c.TabCount; i++) Key(VK_TAB, false, false);
                }
                bool? inPage = FocusInPage();
                if (inPage == null) { checkFailures++; return false; }
                checkFailures = 0;
                if (!inPage.Value) return false; // הפוקוס לא בתוך הדף, למשל בשורת הכתובת

                // עברית אי אפשר להקליד ישירות, לכן מעתיקים ללוח ומדביקים (Ctrl+V).
                // Ctrl+A לפני ההדבקה: אם כבר יש משהו בשדה, ההדבקה מחליפה אותו ולא מוסיפה עליו
                string text = c.Text;
                SetClipboard(text);
                Key(VK_A, true, false);
                Key(VK_V, true, false);
                Thread.Sleep(300);

                // מעתיקים בחזרה את מה שיש בשדה, ובודקים שזה בדיוק הטקסט שלנו
                SetClipboard("");
                Key(VK_A, true, false);
                Key(VK_C, true, false);
                Thread.Sleep(300);
                string got = ReadClipboard() ?? "";
                if (Native.GetForegroundWindow() != browserHwnd) return false;
                return Norm(got) == Norm(text);
            }
            catch (FocusLost) { return false; }
        }

        // True = הפוקוס בתוך הדף, False = לא (למשל בשורת הכתובת), null = PowerShell לא הצליח לבדוק בכלל
        static bool? FocusInPage()
        {
            try
            {
                string enc = Convert.ToBase64String(Encoding.Unicode.GetBytes(FocusCheckPs));
                var psi = new ProcessStartInfo("powershell", "-NoProfile -NonInteractive -EncodedCommand " + enc);
                psi.UseShellExecute = false;
                psi.CreateNoWindow = true; // כדי שחלון PowerShell לא יקפוץ ויגנוב את הפוקוס
                psi.RedirectStandardOutput = true;
                psi.RedirectStandardError = true;
                using (var p = Process.Start(psi))
                {
                    var outTask = p.StandardOutput.ReadToEndAsync();
                    p.StandardError.ReadToEndAsync();
                    if (!p.WaitForExit(15000)) { try { p.Kill(); } catch { } return null; }
                    string o = outTask.Result.Trim();
                    if (o.EndsWith("PAGE")) return true;
                    if (o.EndsWith("OUT") || o.EndsWith("UIAERR")) return false;
                    return null; // PowerShell רץ אבל לא החזיר תשובה - כנראה חסום
                }
            }
            catch { return null; }
        }

        // לוחץ על מקש רק אם חלון הדפדפן עדיין בחזית. אחרת עוצר את כל הניסיון
        void Key(byte vk, bool ctrl, bool extended)
        {
            if (Native.GetForegroundWindow() != browserHwnd) throw new FocusLost();
            uint ext = extended ? 0x0001u : 0u;
            if (ctrl) Native.keybd_event(VK_CONTROL, 0, 0, UIntPtr.Zero);
            Native.keybd_event(vk, 0, ext, UIntPtr.Zero);
            Thread.Sleep(50);
            Native.keybd_event(vk, 0, ext | 0x0002u, UIntPtr.Zero);
            if (ctrl) Native.keybd_event(VK_CONTROL, 0, 0x0002u, UIntPtr.Zero);
            Thread.Sleep(50);
        }

        static string Norm(string s)
        {
            return string.Join(" ", s.Split(new char[0], StringSplitOptions.RemoveEmptyEntries));
        }

        // ---- לוח ----
        static string ReadClipboard()
        {
            for (int i = 0; i < 10; i++)
            {
                try { return Clipboard.ContainsText() ? Clipboard.GetText() : null; }
                catch (ExternalException) { Thread.Sleep(50); }
            }
            return null;
        }

        static void SetClipboard(string text)
        {
            for (int i = 0; i < 10; i++)
            {
                try
                {
                    if (string.IsNullOrEmpty(text)) Clipboard.Clear();
                    else Clipboard.SetDataObject(text, true);
                    return;
                }
                catch (ExternalException) { Thread.Sleep(50); }
            }
        }

        // מחזירים ללוח את מה שהיה בו לפני כן (טקסט בלבד)
        void RestoreClipboard()
        {
            if (oldClip != null) SetClipboard(oldClip);
            oldClip = null;
        }

        // ---- שמירה על המחשב ער ----
        // נקרא בכל שנייה: שומר על המחשב ער, אלא אם הוא על סוללה מתחת לסף
        void UpdateAwake()
        {
            if (!wantAwake) return;
            int pct; bool plugged;
            bool hasBattery = Power.Battery(out pct, out plugged);
            bool low = hasBattery && !plugged && pct < c.MinBattery;
            if (low && awakeOn) { Power.Release(); awakeOn = false; }
            else if (!low && !awakeOn) { Power.KeepAwake(allowScreenOff); awakeOn = true; }
            PowerWarning = low ? L.Tr("הסוללה ב-" + pct + "%, ולכן המחשב כבר לא נשמר ער (יחזור אם תחבר לחשמל)") : "";
        }

        void SetStatus(string text, bool error)
        {
            if (Status != null) Status(L.Tr(text), error);
        }

        void Finish(string text, bool error)
        {
            timer.Stop();
            RestoreClipboard();
            phase = Phase.Idle;
            vsQueue.Clear();
            runId++;
            Power.Release();
            awakeOn = false;
            wantAwake = false;
            PowerWarning = "";
            oldClip = null;
            if (taskRegistered) { taskRegistered = false; TaskSched.Remove(); }
            if (Finished != null) Finished(L.Tr(text), error);
        }

        static string FmtLeft(double sec)
        {
            int s = (int)Math.Max(0, Math.Round(sec));
            int h = s / 3600, m = (s % 3600) / 60, ss = s % 60;
            return h > 0 ? string.Format("{0}:{1:00}:{2:00}", h, m, ss) : string.Format("{0}:{1:00}", m, ss);
        }
    }
}
