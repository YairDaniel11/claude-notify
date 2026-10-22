using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;
using System.Windows.Forms;

// הודעה מתוזמנת לפתיחת מכסת 5 השעות של Claude.
// מבוסס על הסקריפט "הודעה לקלוד" של צבי דורש ציון (פורום מתמחים), שהועבר ל-C# ושולב בתוכנה.
// כל הקוד כאן נכתב ב-C# 5, כי build.cmd משתמש ב-csc של .NET Framework 4.x.

namespace ClaudeNotify
{
    // ---------------------------------------------------------------- Config (schedule.json)
    class SchedConfig
    {
        public string Method { get; set; }          // "browser" = דרך הדפדפן, "cli" = דרך Claude Code בלי דפדפן
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
        public bool AllowScreenOff { get; set; }
        public int MinBattery { get; set; }

        public SchedConfig()
        {
            Method = "browser";
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
            if (Method != "cli" && Method != "browser") Method = "browser";
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

    // ---------------------------------------------------------------- Schedule runner
    // רץ כולו על ה-thread של הממשק (טיימר אחד), כמו ב-tkinter של הסקריפט המקורי.
    // הוא שייך ל-TrayContext ולא לחלון, ולכן סגירת החלון לא מבטלת את התזמון.
    class ScheduleRunner
    {
        enum Phase { Idle, WaitStart, WaitPage, Typing, Done, Sending }
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
        public string Start(SchedConfig cfg)
        {
            if (Running) return "כבר יש תזמון פעיל";
            profile = null;
            if (string.IsNullOrWhiteSpace(cfg.Text)) return "צריך להקליד טקסט לשליחה";
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
            if (cfg.UseTime)
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
            prefix = (cfg.Method == "cli" ? "ההודעה תישלח " : "הדפדפן ייפתח ") + when;
            wantAwake = cfg.KeepAwake;
            allowScreenOff = cfg.AllowScreenOff && !modernStandby;
            awakeOn = false;
            oldClip = null;
            runId++;
            phase = Phase.WaitStart;
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
                    case Phase.Done: RestoreClipboard(); Finish("בוצע!", false); break;
                }
            }
            catch (Exception ex)
            {
                Log.Write("schedule error: " + ex);
                RestoreClipboard();
                Finish("שגיאה לא צפויה: " + ex.Message, true);
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
            browserHwnd = Native.GetForegroundWindow();
            if (!Power.ForegroundProcessName().Equals(browserExe, StringComparison.OrdinalIgnoreCase))
                return false; // כדי לא להקליד בתוכנה אחרת שקפצה לחזית
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
            PowerWarning = low ? "הסוללה ב-" + pct + "%, ולכן המחשב כבר לא נשמר ער (יחזור אם תחבר לחשמל)" : "";
        }

        void SetStatus(string text, bool error)
        {
            if (Status != null) Status(text, error);
        }

        void Finish(string text, bool error)
        {
            timer.Stop();
            phase = Phase.Idle;
            runId++;
            Power.Release();
            awakeOn = false;
            wantAwake = false;
            PowerWarning = "";
            oldClip = null;
            if (Finished != null) Finished(text, error);
        }

        static string FmtLeft(double sec)
        {
            int s = (int)Math.Max(0, Math.Round(sec));
            int h = s / 3600, m = (s % 3600) / 60, ss = s % 60;
            return h > 0 ? string.Format("{0}:{1:00}:{2:00}", h, m, ss) : string.Format("{0}:{1:00}", m, ss);
        }
    }

    // ---------------------------------------------------------------- Schedule window
    class ScheduleForm : Form
    {
        readonly ScheduleRunner runner;
        List<BrowserProfile> profiles = new List<BrowserProfile>();
        FlowLayoutPanel panel, rowProfile, rowUrl, rowDelay, rowTime, rowMinWait, rowMaxWait, rowTabs;
        ComboBox cmbMethod, cmbProfile, cmbWhen;
        TextBox txtUrl, txtText;
        NumericUpDown numDelay, numMinWait, numMaxWait, numTabs, numBattery;
        DateTimePicker dtpTime;
        CheckBox chkSend, chkAwake, chkScreenOff;
        Button btnStart, btnCancel;
        Label lblHint, lblStatus, lblPower;

        public ScheduleForm(ScheduleRunner runner)
        {
            this.runner = runner;
            Text = "Claude Notify - הודעה מתוזמנת";
            Font = new Font("Segoe UI", 10f);
            RightToLeft = RightToLeft.Yes;
            RightToLeftLayout = true;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(600, 660);
            Icon = Branding.MakeIcon(32);

            panel = new FlowLayoutPanel();
            panel.Dock = DockStyle.Fill;
            panel.FlowDirection = FlowDirection.TopDown;
            panel.WrapContents = false;
            panel.AutoScroll = true;
            panel.Padding = new Padding(16);
            panel.RightToLeft = RightToLeft.Yes;
            Controls.Add(panel);

            var intro = new Label { AutoSize = true, MaximumSize = new Size(540, 0), Margin = new Padding(3, 3, 3, 8), Font = new Font("Segoe UI", 10f, FontStyle.Bold),
                Text = "שולח הודעה קצרה לקלוד בשעה שתבחר, כדי שמכסת 5 השעות תתחיל לרוץ מראש." };
            panel.Controls.Add(intro);

            cmbMethod = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 340, RightToLeft = RightToLeft.Yes };
            cmbMethod.Items.Add("דרך הדפדפן (פרופיל מחובר ל-claude.ai)");
            cmbMethod.Items.Add("דרך Claude Code, בלי דפדפן");
            AddRow("שיטת שליחה:", cmbMethod);
            lblHint = new Label { AutoSize = true, MaximumSize = new Size(540, 0), ForeColor = Color.DimGray, Margin = new Padding(3, 0, 3, 8) };
            panel.Controls.Add(lblHint);

            cmbProfile = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 400, RightToLeft = RightToLeft.No };
            rowProfile = AddRow("פרופיל:", cmbProfile);
            txtUrl = new TextBox { Width = 340, RightToLeft = RightToLeft.No };
            rowUrl = AddRow("כתובת האתר:", txtUrl);
            txtText = new TextBox { Width = 340, RightToLeft = RightToLeft.Yes };
            AddRow("הטקסט לשליחה:", txtText);

            cmbWhen = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 200, RightToLeft = RightToLeft.Yes };
            cmbWhen.Items.Add("אחרי השהיה בשניות");
            cmbWhen.Items.Add("בשעה מסוימת");
            AddRow("מתי להפעיל:", cmbWhen);
            numDelay = new NumericUpDown { Minimum = 0, Maximum = 86400, Width = 90 };
            rowDelay = AddRow("שניות עד ההפעלה:", numDelay);
            dtpTime = new DateTimePicker { Format = DateTimePickerFormat.Custom, CustomFormat = "HH:mm:ss", ShowUpDown = true, Width = 110, RightToLeft = RightToLeft.No };
            rowTime = AddRow("שעה (אם כבר עברה - מחר):", dtpTime);

            numMinWait = new NumericUpDown { Minimum = 0, Maximum = 60, Width = 70 };
            rowMinWait = AddRow("שניות המתנה לפני הניסיון הראשון להקליד:", numMinWait);
            numMaxWait = new NumericUpDown { Minimum = 10, Maximum = 600, Width = 70 };
            rowMaxWait = AddRow("מקסימום שניות לחכות שהדף ייטען:", numMaxWait);
            numTabs = new NumericUpDown { Minimum = 0, Maximum = 30, Width = 70 };
            rowTabs = AddRow("מספר לחיצות Tab (0 = השדה שכבר מסומן):", numTabs);
            chkSend = Check("ללחוץ גם Enter (לשלוח)");

            chkAwake = Check("לשמור על המחשב ער עד אז");
            chkScreenOff = Check(runner.ModernStandby
                ? "לאפשר למסך לכבות - לא אפשרי במחשב הזה, כי כיבוי מסך מרדים אותו"
                : "לאפשר למסך לכבות - הוא יודלק מחדש בשעת ההפעלה");
            if (runner.ModernStandby) chkScreenOff.Enabled = false;
            numBattery = new NumericUpDown { Minimum = 0, Maximum = 100, Width = 70 };
            AddRow("על סוללה: להפסיק לשמור על המחשב ער מתחת לאחוז:", numBattery);

            var btns = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.LeftToRight, WrapContents = false, RightToLeft = RightToLeft.Yes, Margin = new Padding(3, 12, 3, 3) };
            btnStart = new Button { Text = "התחל", AutoSize = true, Padding = new Padding(14, 2, 14, 2) };
            btnCancel = new Button { Text = "בטל", AutoSize = true, Padding = new Padding(14, 2, 14, 2) };
            btnStart.Click += delegate { DoStart(); };
            btnCancel.Click += delegate { runner.Cancel(); };
            btns.Controls.Add(btnStart);
            btns.Controls.Add(btnCancel);
            panel.Controls.Add(btns);

            lblStatus = new Label { AutoSize = true, MaximumSize = new Size(540, 0), Margin = new Padding(3, 6, 3, 3), Font = new Font("Segoe UI", 10.5f, FontStyle.Bold) };
            panel.Controls.Add(lblStatus);
            lblPower = new Label { AutoSize = true, MaximumSize = new Size(540, 0), ForeColor = Color.Firebrick };
            panel.Controls.Add(lblPower);
            panel.Controls.Add(new Label { AutoSize = true, MaximumSize = new Size(540, 0), ForeColor = Color.DimGray, Margin = new Padding(3, 8, 3, 3),
                Text = "אפשר לסגור את החלון - התזמון ממשיך לרוץ ברקע מהאייקון. יציאה מהתוכנה מבטלת אותו, והמחשב צריך להישאר דלוק." });

            cmbMethod.SelectedIndexChanged += delegate { ApplyVisibility(); };
            cmbWhen.SelectedIndexChanged += delegate { ApplyVisibility(); };

            runner.Status += delegate(string text, bool error) { SetStatus(text, error); lblPower.Text = runner.PowerWarning; };
            runner.Finished += delegate(string text, bool error) { SetRunning(false); SetStatus(text, error); lblPower.Text = ""; };
            runner.Launching += delegate { Hide(); };   // שהחלון הזה לא יגנוב את הפוקוס מהדפדפן

            FormClosing += delegate(object s, FormClosingEventArgs e)
            {
                if (e.CloseReason == CloseReason.UserClosing) { e.Cancel = true; Hide(); }
            };

            LoadFromConfig(SchedConfig.Load());
        }

        FlowLayoutPanel AddRow(string label, Control ctl)
        {
            var r = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.LeftToRight, WrapContents = false, RightToLeft = RightToLeft.Yes };
            r.Controls.Add(new Label { Text = label, AutoSize = true, Margin = new Padding(3, 6, 3, 3) });
            r.Controls.Add(ctl);
            panel.Controls.Add(r);
            return r;
        }

        CheckBox Check(string text)
        {
            var c = new CheckBox { Text = text, AutoSize = true, Margin = new Padding(3, 4, 3, 4) };
            panel.Controls.Add(c);
            return c;
        }

        void ApplyVisibility()
        {
            bool browser = cmbMethod.SelectedIndex != 1;
            rowProfile.Visible = rowUrl.Visible = rowMinWait.Visible = rowMaxWait.Visible = rowTabs.Visible = chkSend.Visible = browser;
            rowDelay.Visible = cmbWhen.SelectedIndex != 1;
            rowTime.Visible = cmbWhen.SelectedIndex == 1;
            lblHint.Text = browser
                ? "פותח את הדפדפן הרגיל עם הפרופיל שבחרת (חייב להיות מחובר ל-claude.ai), ומקליד שם. בשעת ההפעלה לא לגעת במקלדת ובעכבר."
                : "שולח הודעה קצרה דרך Claude Code (מודל Haiku), בלי דפדפן ובלי הקלדה, ולכן גם לא תלוי במסך נעול. נספר על אותה מכסה של המנוי.";
        }

        void LoadProfiles(string selectedKey)
        {
            profiles = BrowserProfiles.List();
            cmbProfile.Items.Clear();
            int sel = 0;
            for (int i = 0; i < profiles.Count; i++)
            {
                cmbProfile.Items.Add(profiles[i].ToString());
                if (profiles[i].Key == selectedKey) sel = i;
            }
            if (profiles.Count > 0) cmbProfile.SelectedIndex = sel;
        }

        void LoadFromConfig(SchedConfig s)
        {
            cmbMethod.SelectedIndex = s.Method == "cli" ? 1 : 0;
            LoadProfiles(s.ProfileKey);
            txtUrl.Text = s.Url;
            txtText.Text = s.Text;
            cmbWhen.SelectedIndex = s.UseTime ? 1 : 0;
            numDelay.Value = s.DelaySeconds;
            TimeSpan t; TimeSpan.TryParse(s.AtTime, out t);
            dtpTime.Value = DateTime.Today + t;
            numMinWait.Value = s.MinWaitSeconds;
            numMaxWait.Value = s.MaxWaitSeconds;
            numTabs.Value = s.TabCount;
            chkSend.Checked = s.SendEnter;
            chkAwake.Checked = s.KeepAwake;
            chkScreenOff.Checked = s.AllowScreenOff && !runner.ModernStandby;
            numBattery.Value = s.MinBattery;
            ApplyVisibility();
            SetRunning(runner.Running);
        }

        SchedConfig Collect()
        {
            var s = new SchedConfig();
            s.Method = cmbMethod.SelectedIndex == 1 ? "cli" : "browser";
            int pi = cmbProfile.SelectedIndex;
            s.ProfileKey = pi >= 0 && pi < profiles.Count ? profiles[pi].Key : "";
            s.Url = txtUrl.Text.Trim();
            s.Text = txtText.Text;
            s.UseTime = cmbWhen.SelectedIndex == 1;
            s.DelaySeconds = (int)numDelay.Value;
            s.AtTime = dtpTime.Value.ToString("HH:mm:ss");
            s.MinWaitSeconds = (int)numMinWait.Value;
            s.MaxWaitSeconds = (int)numMaxWait.Value;
            s.TabCount = (int)numTabs.Value;
            s.SendEnter = chkSend.Checked;
            s.KeepAwake = chkAwake.Checked;
            s.AllowScreenOff = chkScreenOff.Checked && !runner.ModernStandby;
            s.MinBattery = (int)numBattery.Value;
            return s;
        }

        void DoStart()
        {
            var s = Collect();
            string err = runner.Start(s);
            if (err != null) { SetStatus(err, true); return; }
            s.Save();
            SetRunning(true);
        }

        void SetRunning(bool running)
        {
            btnStart.Enabled = !running;
            btnCancel.Enabled = running;
        }

        void SetStatus(string text, bool error)
        {
            lblStatus.Text = text;
            lblStatus.ForeColor = error ? Color.Firebrick : Color.FromArgb(60, 60, 60);
        }

        // נקרא בכל פתיחה של החלון מהאייקון
        public void Reload()
        {
            if (!runner.Running) LoadProfiles(SchedConfig.Load().ProfileKey);
            SetRunning(runner.Running);
        }
    }
}
