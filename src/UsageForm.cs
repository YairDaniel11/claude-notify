using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;
using System.Windows.Forms;

// חלון "מידע טכני": מודל, מכסות (5 שעות / שבועית) ושימוש בטוקנים לפי שיחה.
// הנתונים מגיעים מקבצי ה-transcript המקומיים של Claude Code (~\.claude\projects), מההגדרות, ומאירוע rate_limit_event של claude -p (רק בלחיצה, כי זו בקשה קטנה אמיתית).

namespace ClaudeNotify
{
    // ---------------------------------------------------------------- נתונים
    class UsageRec
    {
        public long T;                 // ticks UTC
        public string Id, Model;
        public long In, Out, CC, CR;   // input, output, cache creation, cache read
    }

    class UsageFile
    {
        public long Size, Mtime;
        public string SessionId, Title, Cwd, ProjDir;
        public bool Sub;
        public List<UsageRec> Recs = new List<UsageRec>();
        public long Last;
    }

    class UsageSession
    {
        public string Id, Title, Project, Model;
        public long In, Out, CC, CR, Last, Context;
    }

    class UsageModelSum { public string Model; public long In, Out, CC, CR, Count; }

    class UsageSnapshot
    {
        public List<UsageSession> Sessions = new List<UsageSession>();
        public List<UsageModelSum> Models = new List<UsageModelSum>();
        public long Tok5h, Tok24h, TokToday, Tok7d;
        public long WinStart, WinEnd;     // הערכה, ticks UTC. 0 = אין חלון פעיל
        public string LastModel;
        public int Files;
    }

    class LimitInfo
    {
        public DateTime Fetched;
        public double Five = -1, Seven = -1;      // 0..1, או -1 כשלא ידוע
        public long FiveReset, SevenReset;         // unix seconds
        public string Status;
    }

    static class UsageData
    {
        static readonly Dictionary<string, UsageFile> cache = new Dictionary<string, UsageFile>();
        static readonly object gate = new object();

        public static string ProjectsDir
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), @".claude\projects"); }
        }

        // ---- עזרי JSON טקסטואליים (מהירים, בלי פענוח מלא של שורות ענקיות)
        static string JStr(string s, int i)
        {
            var sb = new StringBuilder();
            while (i < s.Length)
            {
                char c = s[i++];
                if (c == '"') break;
                if (c == '\\' && i < s.Length)
                {
                    char n = s[i++];
                    if (n == 'n') sb.Append(' ');
                    else if (n == 't') sb.Append(' ');
                    else if (n == 'u' && i + 4 <= s.Length)
                    {
                        int v;
                        if (int.TryParse(s.Substring(i, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out v)) sb.Append((char)v);
                        i += 4;
                    }
                    else sb.Append(n);
                }
                else sb.Append(c);
            }
            return sb.ToString();
        }

        static string StrAfter(string line, string key, int from)
        {
            int i = line.IndexOf(key, from, StringComparison.Ordinal);
            return i < 0 ? null : JStr(line, i + key.Length);
        }

        static long NumAfter(string line, string key, int from)
        {
            int i = line.IndexOf(key, from, StringComparison.Ordinal);
            if (i < 0) return 0;
            i += key.Length;
            long v = 0;
            while (i < line.Length && line[i] >= '0' && line[i] <= '9') { v = v * 10 + (line[i] - '0'); i++; }
            return v;
        }

        static UsageFile Scan(string path, FileInfo fi, string sessionId, bool sub, string projDir)
        {
            var f = new UsageFile { Size = fi.Length, Mtime = fi.LastWriteTimeUtc.Ticks, SessionId = sessionId, Sub = sub, ProjDir = projDir };
            var byId = new Dictionary<string, UsageRec>();
            try
            {
                using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 1 << 16))
                using (var sr = new StreamReader(fs, Encoding.UTF8, false, 1 << 16))
                {
                    string line;
                    while ((line = sr.ReadLine()) != null)
                    {
                        if (f.Cwd == null && !sub)
                        {
                            int ci = line.IndexOf("\"cwd\":\"", StringComparison.Ordinal);
                            if (ci >= 0 && ci < 4000) f.Cwd = JStr(line, ci + 7);
                        }
                        if (line.IndexOf("\"type\":\"ai-title\"", StringComparison.Ordinal) >= 0)
                        {
                            string t = StrAfter(line, "\"aiTitle\":\"", 0);
                            if (!string.IsNullOrEmpty(t)) f.Title = t;
                            continue;
                        }
                        if (line.IndexOf("\"type\":\"assistant\"", StringComparison.Ordinal) < 0) continue;
                        int m = line.IndexOf("\"message\":{", StringComparison.Ordinal);
                        if (m < 0) continue;
                        string model = StrAfter(line, "\"model\":\"", m);
                        if (model == null || model.StartsWith("<")) continue;
                        int u = line.IndexOf("\"usage\":{", m, StringComparison.Ordinal);
                        if (u < 0) continue;
                        var r = new UsageRec { Model = model };
                        r.In = NumAfter(line, "\"input_tokens\":", u);
                        r.Out = NumAfter(line, "\"output_tokens\":", u);
                        r.CC = NumAfter(line, "\"cache_creation_input_tokens\":", u);
                        r.CR = NumAfter(line, "\"cache_read_input_tokens\":", u);
                        r.Id = StrAfter(line, "\"id\":\"", m);
                        string ts = StrAfter(line, "\"timestamp\":\"", 0);
                        DateTime dt;
                        if (ts != null && DateTime.TryParse(ts, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out dt)) r.T = dt.Ticks;
                        else r.T = fi.LastWriteTimeUtc.Ticks;
                        UsageRec old;
                        if (r.Id != null && byId.TryGetValue(r.Id, out old))
                        {
                            // הודעה שנכתבה כמה פעמים בזרימה: שומרים את הגדולה
                            if (r.Out >= old.Out) { old.In = r.In; old.Out = r.Out; old.CC = r.CC; old.CR = r.CR; old.T = Math.Max(old.T, r.T); }
                            continue;
                        }
                        if (r.Id != null) byId[r.Id] = r;
                        f.Recs.Add(r);
                    }
                }
            }
            catch { }
            foreach (var r in f.Recs) if (r.T > f.Last) f.Last = r.T;
            if (f.Last == 0) f.Last = f.Mtime;
            return f;
        }

        public static UsageSnapshot Collect()
        {
            var snap = new UsageSnapshot();
            DateTime nowU = DateTime.UtcNow;
            long weekAgo = nowU.AddDays(-7).Ticks;
            var files = new List<UsageFile>();
            string root = ProjectsDir;
            if (Directory.Exists(root))
            {
                string[] all;
                try { all = Directory.GetFiles(root, "*.jsonl", SearchOption.AllDirectories); } catch { all = new string[0]; }
                foreach (string p in all)
                {
                    try
                    {
                        var fi = new FileInfo(p);
                        if (fi.LastWriteTimeUtc.Ticks < weekAgo) continue;
                        string dir = fi.DirectoryName;
                        bool sub = string.Equals(Path.GetFileName(dir), "subagents", StringComparison.OrdinalIgnoreCase);
                        string sid, proj;
                        if (sub) { string sd = Path.GetDirectoryName(dir); sid = Path.GetFileName(sd); proj = Path.GetFileName(Path.GetDirectoryName(sd)); }
                        else { sid = Path.GetFileNameWithoutExtension(p); proj = Path.GetFileName(dir); }
                        UsageFile f;
                        lock (gate) { cache.TryGetValue(p, out f); }
                        if (f == null || f.Size != fi.Length || f.Mtime != fi.LastWriteTimeUtc.Ticks)
                        {
                            f = Scan(p, fi, sid, sub, proj);
                            lock (gate) { cache[p] = f; }
                        }
                        files.Add(f);
                    }
                    catch { }
                }
            }
            snap.Files = files.Count;

            long t5 = nowU.AddHours(-5).Ticks, t24 = nowU.AddHours(-24).Ticks;
            long tToday = DateTime.Now.Date.ToUniversalTime().Ticks;
            var sess = new Dictionary<string, UsageSession>();
            var models = new Dictionary<string, UsageModelSum>();
            var times = new List<long>();
            long lastT = 0;
            foreach (var f in files)
            {
                UsageSession s;
                if (!sess.TryGetValue(f.SessionId, out s)) { s = new UsageSession { Id = f.SessionId }; sess[f.SessionId] = s; }
                if (!f.Sub)
                {
                    if (f.Title != null) s.Title = f.Title;
                    if (f.Cwd != null) s.Project = LastSegment(f.Cwd);
                    else if (s.Project == null) s.Project = f.ProjDir;
                }
                else if (s.Project == null) s.Project = f.ProjDir;
                if (f.Last > s.Last) s.Last = f.Last;
                long bestT = 0;
                foreach (var r in f.Recs)
                {
                    s.In += r.In; s.Out += r.Out; s.CC += r.CC; s.CR += r.CR;
                    if (!f.Sub && r.T >= bestT) { bestT = r.T; s.Context = r.In + r.CC + r.CR; s.Model = r.Model; }
                    if (r.T >= weekAgo)
                    {
                        long tot = r.In + r.Out + r.CC;      // בלי cache read, שהוא זול ונספר חלקית
                        snap.Tok7d += tot;
                        if (r.T >= t24) snap.Tok24h += tot;
                        if (r.T >= t5) snap.Tok5h += tot;
                        if (r.T >= tToday) snap.TokToday += tot;
                        times.Add(r.T);
                        UsageModelSum ms;
                        if (!models.TryGetValue(r.Model, out ms)) { ms = new UsageModelSum { Model = r.Model }; models[r.Model] = ms; }
                        ms.In += r.In; ms.Out += r.Out; ms.CC += r.CC; ms.CR += r.CR; ms.Count++;
                        if (r.T > lastT) { lastT = r.T; snap.LastModel = r.Model; }
                    }
                }
            }
            foreach (var s in sess.Values) snap.Sessions.Add(s);
            snap.Sessions.Sort(delegate(UsageSession a, UsageSession b) { return b.Last.CompareTo(a.Last); });
            if (snap.Sessions.Count > 10) snap.Sessions.RemoveRange(10, snap.Sessions.Count - 10);
            foreach (var m in models.Values) snap.Models.Add(m);
            snap.Models.Sort(delegate(UsageModelSum a, UsageModelSum b) { return (b.In + b.Out + b.CC).CompareTo(a.In + a.Out + a.CC); });

            // הערכת חלון 5 שעות: חלון חדש מתחיל בהודעה הראשונה אחרי שהקודם הסתיים
            times.Sort();
            long start = 0, span = TimeSpan.FromHours(5).Ticks;
            foreach (long t in times) if (start == 0 || t >= start + span) start = t;
            if (start != 0 && nowU.Ticks < start + span) { snap.WinStart = start; snap.WinEnd = start + span; }
            return snap;
        }

        static string LastSegment(string path)
        {
            string p = path.TrimEnd('\\', '/');
            int i = Math.Max(p.LastIndexOf('\\'), p.LastIndexOf('/'));
            return i >= 0 ? p.Substring(i + 1) : p;
        }

        // ---- הגדרות
        public static string ConfiguredModel(out string effort, out string source)
        {
            effort = null; source = null;
            string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            string env = Environment.GetEnvironmentVariable("ANTHROPIC_MODEL");
            string model = null;
            try
            {
                string p = Path.Combine(home, @".claude\settings.json");
                if (File.Exists(p))
                {
                    var d = new JavaScriptSerializer { MaxJsonLength = int.MaxValue }.Deserialize<Dictionary<string, object>>(File.ReadAllText(p, Encoding.UTF8));
                    object o;
                    if (d.TryGetValue("model", out o) && o != null) { model = o.ToString(); source = "~\\.claude\\settings.json"; }
                    if (d.TryGetValue("effortLevel", out o) && o != null) effort = o.ToString();
                }
            }
            catch { }
            if (!string.IsNullOrEmpty(env)) { model = env; source = "ANTHROPIC_MODEL"; }
            return model;
        }

        // ---- מכסות מדויקות (מתוך claude -p)
        static string LimitsFile
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), @"ClaudeNotify\usage_limits.json"); }
        }

        public static LimitInfo LoadLimits()
        {
            try
            {
                if (!File.Exists(LimitsFile)) return null;
                var d = new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(File.ReadAllText(LimitsFile, Encoding.UTF8));
                var li = new LimitInfo();
                li.Fetched = new DateTime(Convert.ToInt64(d["fetched"]), DateTimeKind.Utc).ToLocalTime();
                li.Five = Convert.ToDouble(d["five"], CultureInfo.InvariantCulture);
                li.Seven = Convert.ToDouble(d["seven"], CultureInfo.InvariantCulture);
                li.FiveReset = Convert.ToInt64(d["fiveReset"]);
                li.SevenReset = Convert.ToInt64(d["sevenReset"]);
                li.Status = d.ContainsKey("status") && d["status"] != null ? d["status"].ToString() : null;
                return li;
            }
            catch { return null; }
        }

        static void SaveLimits(LimitInfo li)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(LimitsFile));
                var d = new Dictionary<string, object>();
                d["fetched"] = li.Fetched.ToUniversalTime().Ticks;
                d["five"] = li.Five; d["seven"] = li.Seven;
                d["fiveReset"] = li.FiveReset; d["sevenReset"] = li.SevenReset;
                d["status"] = li.Status;
                File.WriteAllText(LimitsFile, new JavaScriptSerializer().Serialize(d), new UTF8Encoding(false));
            }
            catch { }
        }

        static double Util(object w, out long reset)
        {
            reset = 0;
            var d = w as Dictionary<string, object>;
            if (d == null) return -1;
            object o;
            if (d.TryGetValue("resetsAt", out o) && o != null) reset = Convert.ToInt64(o, CultureInfo.InvariantCulture);
            if (d.TryGetValue("utilization", out o) && o != null) return Convert.ToDouble(o, CultureInfo.InvariantCulture);
            return -1;
        }

        // שולח בקשת haiku קטנה ל-claude ומוציא מהפלט את אירוע rate_limit_event. מחזיר null בכשל, ובמשתנה error הסבר
        public static LimitInfo FetchLimits(out string error)
        {
            error = null;
            try
            {
                string exe = Summarizer.FindClaude();
                if (exe == null) { error = L.T("Claude Code לא נמצא במחשב", "Claude Code was not found"); return null; }
                string args = "-p --output-format stream-json --verbose --model haiku --effort low --no-session-persistence --strict-mcp-config --setting-sources local --disable-slash-commands --mcp-config \"{\\\"mcpServers\\\":{}}\"";
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
                    var bytes = new UTF8Encoding(false).GetBytes("hi");
                    p.StandardInput.BaseStream.Write(bytes, 0, bytes.Length);
                    p.StandardInput.Close();
                    var outTask = p.StandardOutput.ReadToEndAsync();
                    var errTask = p.StandardError.ReadToEndAsync();
                    if (!p.WaitForExit(150000)) { try { p.Kill(); } catch { } error = L.T("הזמן הקצוב עבר", "Timed out"); return null; }
                    string output = outTask.Result ?? "";
                    var ser = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };
                    LimitInfo best = null;
                    foreach (string ln in output.Split('\n'))
                    {
                        if (ln.IndexOf("\"rate_limit_event\"", StringComparison.Ordinal) < 0) continue;
                        try
                        {
                            var j = ser.Deserialize<Dictionary<string, object>>(ln);
                            var info = j["rate_limit_info"] as Dictionary<string, object>;
                            if (info == null) continue;
                            var li = new LimitInfo { Fetched = DateTime.Now };
                            object o;
                            if (info.TryGetValue("status", out o) && o != null) li.Status = o.ToString();
                            var w = info.TryGetValue("unifiedWindows", out o) ? o as Dictionary<string, object> : null;
                            if (w != null)
                            {
                                if (w.TryGetValue("five_hour", out o)) li.Five = Util(o, out li.FiveReset);
                                if (w.TryGetValue("seven_day", out o)) li.Seven = Util(o, out li.SevenReset);
                            }
                            if (li.Five < 0 && li.Seven < 0)
                            {
                                // אין חלונות מפורטים: משתמשים בסוג המכסה שדווח
                                string typ = info.TryGetValue("rateLimitType", out o) && o != null ? o.ToString() : "";
                                long rs = 0; double ut = -1;
                                if (info.TryGetValue("resetsAt", out o) && o != null) rs = Convert.ToInt64(o, CultureInfo.InvariantCulture);
                                if (info.TryGetValue("utilization", out o) && o != null) ut = Convert.ToDouble(o, CultureInfo.InvariantCulture);
                                if (typ == "five_hour") { li.Five = ut; li.FiveReset = rs; }
                                else if (typ.StartsWith("seven_day")) { li.Seven = ut; li.SevenReset = rs; }
                            }
                            best = li;
                        }
                        catch { }
                    }
                    if (best == null || (best.Five < 0 && best.Seven < 0))
                    {
                        string detail = ((errTask.Result ?? "").Trim()).Trim();
                        if (detail.Length > 160) detail = detail.Substring(0, 160);
                        error = L.T("Claude לא החזיר נתוני מכסה", "Claude returned no quota data") + (detail.Length > 0 ? ": " + detail : "");
                        return null;
                    }
                    // מכסה שלא דווחה בבקשה הזו נשארת מהמדידה הקודמת
                    var prev = LoadLimits();
                    if (prev != null)
                    {
                        if (best.Five < 0) { best.Five = prev.Five; best.FiveReset = prev.FiveReset; }
                        if (best.Seven < 0) { best.Seven = prev.Seven; best.SevenReset = prev.SevenReset; }
                    }
                    SaveLimits(best);
                    return best;
                }
            }
            catch (Exception ex) { error = ex.Message; return null; }
        }
    }

    // ---------------------------------------------------------------- פקדים קטנים
    // פס התקדמות בצבעי העיצוב. Value בין 0 ל-1, או מינוס אחד כשלא ידוע
    class UsageBar : Panel
    {
        double val = -1;
        public UsageBar()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            Height = Theme.S(12);
            BackColor = Theme.Card;
        }
        public double Value { get { return val; } set { val = value; Invalidate(); } }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(Theme.Card);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var r = new RectangleF(0, 0, Width - 1, Height - 1);
            float rad = (Height - 1) / 2f;
            using (var path = Theme.RoundRect(r, rad))
            using (var b = new SolidBrush(Color.FromArgb(240, 236, 228)))
                g.FillPath(b, path);
            if (val > 0)
            {
                float w = (float)Math.Min(1.0, val) * (Width - 1);
                if (w < Height - 1) w = Height - 1;
                Color c = val >= 0.9 ? Theme.Danger : val >= 0.75 ? Theme.AccentDark : Theme.Accent;
                using (var path = Theme.RoundRect(new RectangleF(0, 0, w, Height - 1), rad))
                using (var b = new SolidBrush(c))
                    g.FillPath(b, path);
            }
        }
    }

    // ערך בשורה: טקסט ראשי מודגש ושורה משנית אפורה, צמוד לקצה החיצוני של השורה
    class UsageValue : Panel
    {
        public string Main = "", Sub = "";
        public UsageValue()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            BackColor = Theme.Card;
            Height = Theme.S(38);
        }
        public void Set(string main, string sub) { Main = main ?? ""; Sub = sub ?? ""; Invalidate(); }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.Clear(Theme.Card);
            // בעברית הפקד נמצא בצד שמאל, ולכן הטקסט צמוד שמאלה; באנגלית צמוד ימינה
            var f = TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis | (L.He ? TextFormatFlags.Left : TextFormatFlags.Right);
            int h1 = Theme.S(22);
            using (var f1 = Theme.Semi(11f))
                TextRenderer.DrawText(e.Graphics, Main, f1, new Rectangle(0, 0, Width, h1), Theme.Text, f | TextFormatFlags.VerticalCenter);
            if (Sub.Length > 0)
                using (var f2 = Theme.F(8.5f))
                    TextRenderer.DrawText(e.Graphics, Sub, f2, new Rectangle(0, h1, Width, Height - h1), Theme.Muted, f | TextFormatFlags.Top);
        }
    }

    // ---------------------------------------------------------------- החלון
    class UsageForm : PageForm
    {
        Header header;
        Card cardModel, cardLimits, cardModels, cardSessions;
        Row rowCfg, rowLast, rowEffort, row5, row7, rowFetch, rowEst, rowTok;
        UsageBar bar5, bar7;
        PillButton btnFetch, btnRefresh;
        Note note;
        int gen;
        bool fetching;
        LimitInfo limits;
        UsageSnapshot snap;
        bool loadingSnap;
        string fetchMsg;
        System.Windows.Forms.Timer tick;
        Dropdown cmbModel, cmbEffort;
        Field fldModel;
        Row rowCustom;
        ToggleSwitch tglThink, tglFast;
        static readonly string[] ModelCodes = { "", "fable", "opus", "sonnet", "haiku", "opusplan", null };
        static readonly string[] EffortCodes = { "", "low", "medium", "high", "xhigh" };

        public UsageForm() : base("Claude Notify - " + L.T("מידע טכני", "Technical info"), 60)
        {
            ClientSize = new Size(Theme.S(560), Math.Min(Theme.S(640), Screen.PrimaryScreen.WorkingArea.Height - Theme.S(60)));
            header = new Header(L.T("מידע טכני", "Technical info"), L.T("שימוש ב-Claude, לפי הנתונים המקומיים במחשב.", "Claude usage, from the data stored on this computer."), null);
            content.Controls.Add(header);

            // ---- מודל
            cardModel = new Card(L.T("מודל", "Model"));
            rowCfg = AddRow(cardModel, L.T("מודל מוגדר", "Configured model"), "", new UsageValue(), false, 190,
                L.T("המודל שמוגדר ב-settings.json של Claude Code (או במשתנה הסביבה ANTHROPIC_MODEL). ב-VS Code אפשר לבחור מודל אחר לשיחה מסוימת, ואז הוא יופיע כאן רק ב\"מודל אחרון בשימוש\".",
                    "The model set in Claude Code's settings.json (or the ANTHROPIC_MODEL variable). In VS Code you can pick another model for a single conversation; it then shows only under \"Last model used\"."));
            rowLast = AddRow(cardModel, L.T("מודל אחרון בשימוש", "Last model used"), "", new UsageValue(), false, 190,
                L.T("המודל שענה בפועל בהודעה האחרונה, לפי קבצי השיחות.", "The model that actually answered the latest message, from the conversation files."));
            rowEffort = AddRow(cardModel, L.T("רמת מאמץ", "Effort level"), "", new UsageValue(), false, 190, null);
            content.Controls.Add(cardModel);

            // ---- שליטה בהגדרות Claude Code (נכתב ל-settings.json של Claude Code)
            var cardCtl = new Card(L.T("שליטה בהגדרות Claude", "Claude settings"));
            cmbModel = Ctl.Combo(250, L.He);
            cmbModel.Items.Add(L.T("ברירת מחדל (Claude Code בוחר)", "Default (Claude Code decides)"));
            cmbModel.Items.Add("Fable");
            cmbModel.Items.Add("Opus");
            cmbModel.Items.Add("Sonnet");
            cmbModel.Items.Add("Haiku");
            cmbModel.Items.Add(L.T("Opus Plan (תכנון ב-Opus, ביצוע ב-Sonnet)", "Opus Plan (plans with Opus, runs with Sonnet)"));
            cmbModel.Items.Add(L.T("מותאם אישית...", "Custom..."));
            AddRow(cardCtl, L.T("מודל", "Model"), L.T("המודל שישמש בשיחות חדשות. שיחה שכבר פתוחה ממשיכה עם המודל שלה (אפשר להחליף בה עם /model).", "The model used for new conversations. A conversation that is already open keeps its model (switch it with /model)."), cmbModel, false, 250,
                L.T("נשמר בשדה model בקובץ ~\\.claude\\settings.json. הכינויים (fable, opus, sonnet, haiku) תמיד מצביעים על הגרסה האחרונה של המשפחה.", "Saved to the model field of ~\\.claude\\settings.json. The aliases (fable, opus, sonnet, haiku) always point to the latest version of the family."));
            fldModel = new Field(false);
            rowCustom = AddRow(cardCtl, L.T("שם מודל מלא", "Full model name"), L.T("למשל claude-sonnet-5-5, או sonnet[1m] להקשר ארוך.", "For example claude-sonnet-5-5, or sonnet[1m] for long context."), fldModel, true, 0, null);
            cmbEffort = Ctl.Combo(250, L.He);
            cmbEffort.Items.Add(L.T("ברירת מחדל", "Default"));
            cmbEffort.Items.Add(L.T("נמוכה (low)", "Low"));
            cmbEffort.Items.Add(L.T("בינונית (medium)", "Medium"));
            cmbEffort.Items.Add(L.T("גבוהה (high)", "High"));
            cmbEffort.Items.Add(L.T("גבוהה מאוד (xhigh)", "Extra high (xhigh)"));
            AddRow(cardCtl, L.T("רמת מאמץ", "Effort level"), L.T("כמה Claude משקיע בחשיבה. גבוה יותר = תשובות טובות יותר, אבל איטי ומוציא יותר מהמכסה.", "How much effort Claude puts into thinking. Higher = better answers, but slower and uses more of your quota."), cmbEffort, false, 250, null);
            tglThink = new ToggleSwitch();
            AddRow(cardCtl, L.T("חשיבה מורחבת", "Extended thinking"), L.T("כשכבוי, Claude עונה בלי שלב חשיבה. חוסך מכסה ומאיץ, על חשבון איכות.", "When off, Claude answers without a thinking step. Saves quota and time at the cost of quality."), tglThink, false, 54, null);
            tglFast = new ToggleSwitch();
            AddRow(cardCtl, L.T("מצב מהיר", "Fast mode"), L.T("תשובות מהירות יותר במודלים שתומכים בכך. עשוי לעלות יותר.", "Faster output on models that support it. May cost more."), tglFast, false, 54, null);
            var btnSaveCfg = new PillButton(L.T("שמור הגדרות", "Save settings"), true);
            btnSaveCfg.Height = Theme.S(32);
            btnSaveCfg.Click += delegate { SaveClaudeSettings(); };
            AddRow(cardCtl, L.T("החלת השינויים", "Apply changes"), L.T("נכתב לקובץ ההגדרות של Claude Code, ונוצר גיבוי settings.json.bak.", "Written to the Claude Code settings file; a backup settings.json.bak is created."), btnSaveCfg, false, 140, null);
            content.Controls.Add(cardCtl);
            cmbModel.SelectedIndexChanged += delegate { rowCustom.Shown = cmbModel.SelectedIndex == ModelCodes.Length - 1; };
            rowCustom.Shown = false;

            // ---- מכסות
            cardLimits = new Card(L.T("מכסות", "Limits"));
            bar5 = new UsageBar(); bar7 = new UsageBar();
            row5 = AddRow(cardLimits, L.T("מכסת 5 שעות", "5-hour limit"), "", bar5, true, 0,
                L.T("חלון של 5 שעות שמתחיל בהודעה הראשונה. כשמגיעים ל-100% צריך לחכות לאיפוס.", "A 5-hour window that starts with your first message. At 100% you wait for the reset."));
            row7 = AddRow(cardLimits, L.T("מכסה שבועית", "Weekly limit"), "", bar7, true, 0,
                L.T("אין מכסה יומית: Anthropic מגבילה לפי חלון של 5 שעות ולפי שבוע.", "There is no daily limit: Anthropic limits per 5-hour window and per week."));
            btnFetch = new PillButton(L.T("מדוד עכשיו", "Measure now"), false);
            btnFetch.Height = Theme.S(32);
            btnFetch.Click += delegate { StartFetch(); };
            rowFetch = AddRow(cardLimits, L.T("מדידה מדויקת", "Exact measurement"), "", btnFetch, false, 130,
                L.T("שולחת הודעת \"hi\" קטנה ל-Haiku דרך Claude Code, וקוראת ממנה את אחוזי המכסה האמיתיים של Anthropic. זו בקשה אמיתית: היא נספרת במכסה, ואם אין חלון פעיל - היא גם מתחילה אותו.",
                    "Sends a tiny \"hi\" to Haiku through Claude Code and reads Anthropic's real quota percentages from it. It is a real request: it counts against your quota and, if no window is active, it starts one."));
            rowEst = AddRow(cardLimits, L.T("חלון נוכחי (הערכה)", "Current window (estimate)"), "", new UsageValue(), false, 190,
                L.T("הערכה מקבצי השיחות: חלון חדש מתחיל בהודעה הראשונה אחרי שהקודם הסתיים. האיפוס האמיתי עשוי להיות שונה מעט.", "Estimated from the conversation files: a new window starts at the first message after the previous one ended. The real reset may differ slightly."));
            rowTok = AddRow(cardLimits, L.T("טוקנים (הערכה)", "Tokens (estimate)"), "", new UsageValue(), false, 190,
                L.T("קלט + פלט + יצירת cache, ללא קריאות cache. Anthropic לא חושפת את סכום המכסה המוחלט, לכן אי אפשר לחשב ממנו אחוזים.", "Input + output + cache creation, without cache reads. Anthropic does not expose the absolute quota, so no percentage can be computed from it."));
            content.Controls.Add(cardLimits);

            cardModels = new Card(L.T("פילוח לפי מודל (7 ימים)", "By model (7 days)"));
            content.Controls.Add(cardModels);

            cardSessions = new Card(L.T("שיחות אחרונות", "Recent conversations"));
            content.Controls.Add(cardSessions);

            note = new Note(L.T("הטוקנים נקראים מקבצי השיחות של Claude Code, כולל סוכני משנה. טוקני cache (קריאה/יצירה) מוצגים בנפרד באפור.",
                "Tokens are read from the conversation files in ~\\.claude\\projects, including subagents. Cache tokens (read/creation) are shown separately in grey."));
            content.Controls.Add(note);

            // ---- שורת כפתורים תחתונה
            var flow = new FlowLayoutPanel { Dock = DockStyle.Fill, Padding = Theme.P(18, 10, 18, 0), WrapContents = false, BackColor = Color.White, FlowDirection = L.He ? FlowDirection.RightToLeft : FlowDirection.LeftToRight };
            btnRefresh = new PillButton(L.T("רענן", "Refresh"), true);
            var btnClose = new PillButton(L.T("סגור", "Close"), false);
            btnRefresh.Margin = Theme.P(0, 0, 10, 0);
            btnRefresh.Click += delegate { Reload(); };
            btnClose.Click += delegate { Hide(); };
            flow.Controls.Add(btnRefresh); flow.Controls.Add(btnClose);
            bar.Controls.Add(flow);
            AcceptButton = btnRefresh;
            tip.SetToolTip(btnRefresh, L.T("קורא מחדש את קבצי השיחות וההגדרות.", "Re-reads the conversation files and settings."));

            // עדכון הספירה לאחור
            tick = new System.Windows.Forms.Timer { Interval = 30000 };
            tick.Tick += delegate { if (Visible) UpdateLimitRows(); };
            tick.Start();
            var h = Handle;   // יוצר ידית מראש, כדי ש-BeginInvoke יעבוד גם כשהחלון מוסתר
            Reload();
        }

        // ---- עיצוב ערכים
        static string Fmt(long n)
        {
            if (n < 1000) return n.ToString(CultureInfo.InvariantCulture);
            if (n < 1000000) return (n / 1000.0).ToString(n < 100000 ? "0.0" : "0", CultureInfo.InvariantCulture) + "K";
            return (n / 1000000.0).ToString(n < 100000000 ? "0.00" : "0.0", CultureInfo.InvariantCulture) + "M";
        }

        static string Ago(long ticksUtc)
        {
            var d = DateTime.UtcNow - new DateTime(ticksUtc, DateTimeKind.Utc);
            if (d.TotalMinutes < 1) return L.T("עכשיו", "just now");
            if (d.TotalHours < 1) return L.T("לפני ", "") + (int)d.TotalMinutes + L.T(" דק'", " min ago");
            if (d.TotalDays < 1) return L.T("לפני ", "") + (int)d.TotalHours + L.T(" שע'", " h ago");
            return L.T("לפני ", "") + (int)d.TotalDays + L.T(" ימים", " d ago");
        }

        static string Remain(DateTime local)
        {
            var d = local - DateTime.Now;
            if (d.TotalSeconds <= 0) return null;
            if (d.TotalDays >= 1) return L.T("בעוד ", "in ") + (int)d.TotalDays + L.T(" ימים ו-", "d ") + d.Hours + L.T(" שעות", "h");
            if (d.TotalHours >= 1) return L.T("בעוד ", "in ") + (int)d.TotalHours + L.T(" שעות ו-", "h ") + d.Minutes + L.T(" דקות", "m");
            return L.T("בעוד ", "in ") + Math.Max(1, d.Minutes) + L.T(" דקות", "m");
        }

        static string When(DateTime local)
        {
            return local.Date == DateTime.Today ? local.ToString("HH:mm") : local.ToString("ddd dd/MM HH:mm", L.He ? new CultureInfo("he-IL") : new CultureInfo("en-US"));
        }

        static string ModelName(string m)
        {
            return string.IsNullOrEmpty(m) ? L.T("לא ידוע", "unknown") : m;
        }

        static UsageValue Val(Row r)
        {
            foreach (Control c in r.Controls) { var v = c as UsageValue; if (v != null) return v; }
            return null;
        }

        // ---- שליטה בהגדרות Claude Code
        void LoadClaudeSettings()
        {
            string model, effort; bool thinking, fast;
            ClaudeSettings.Read(out model, out effort, out thinking, out fast);
            int mi = Array.IndexOf(ModelCodes, model);
            if (mi < 0) { mi = ModelCodes.Length - 1; fldModel.Box.Text = model; }
            cmbModel.SelectedIndex = mi;
            rowCustom.Shown = mi == ModelCodes.Length - 1;
            int ei = Array.IndexOf(EffortCodes, effort);
            cmbEffort.SelectedIndex = ei < 0 ? 0 : ei;
            tglThink.Checked = thinking;
            tglFast.Checked = fast;
        }

        void SaveClaudeSettings()
        {
            try
            {
                string model = ModelCodes[Math.Max(0, cmbModel.SelectedIndex)];
                if (model == null) model = fldModel.Box.Text.Trim();
                string effort = EffortCodes[Math.Max(0, cmbEffort.SelectedIndex)];
                ClaudeSettings.Write(model, effort, tglThink.Checked, tglFast.Checked);
                Dialog.Show(this, "Claude Notify", L.T("ההגדרות נשמרו בקובץ ההגדרות של Claude Code.\nשיחה חדשה תשתמש בהן. שיחה שכבר פתוחה ממשיכה עם ההגדרות שלה.", "The settings were saved to the Claude Code settings file.\nA new conversation will use them. A conversation that is already open keeps its own settings."));
                Reload();
            }
            catch (Exception ex)
            {
                Dialog.Show(this, "Claude Notify", L.T("השמירה נכשלה: ", "Saving failed: ") + ex.Message);
            }
        }

        // ---- טעינה
        public void Reload()
        {
            LoadClaudeSettings();
            string effort, src;
            string cfgModel = UsageData.ConfiguredModel(out effort, out src);
            Val(rowCfg).Set(string.IsNullOrEmpty(cfgModel) ? L.T("ברירת מחדל", "Default") : cfgModel, "");
            rowCfg.SetTexts(rowCfg.Title.Text, src != null ? L.T("מקור: ", "Source: ") + (src.StartsWith("~") ? "settings.json" : src) : L.T("לא הוגדר מודל; Claude Code בוחר ברירת מחדל.", "No model set; Claude Code picks a default."));
            Val(rowEffort).Set(string.IsNullOrEmpty(effort) ? L.T("ברירת מחדל", "Default") : effort, "");
            rowEffort.SetTexts(rowEffort.Title.Text, L.T("מוגדר בקובץ ההגדרות של Claude Code.", "Set in the Claude Code settings file."));
            limits = UsageData.LoadLimits();
            UpdateLimitRows();

            loadingSnap = true;
            Val(rowLast).Set(L.T("טוען...", "Loading..."), "");
            Val(rowEst).Set(L.T("טוען...", "Loading..."), "");
            Val(rowTok).Set(L.T("טוען...", "Loading..."), "");
            btnRefresh.Enabled = false;
            int my = ++gen;
            var th = new Thread(delegate()
            {
                UsageSnapshot s = null;
                try { s = UsageData.Collect(); } catch (Exception ex) { Log.Write("usage scan: " + ex.Message); }
                try
                {
                    BeginInvoke(new Action(delegate { if (my == gen) ApplySnapshot(s); }));
                }
                catch { }
            });
            th.IsBackground = true;
            th.Priority = ThreadPriority.BelowNormal;
            th.Start();
            FitContent();
        }

        void ApplySnapshot(UsageSnapshot s)
        {
            loadingSnap = false;
            btnRefresh.Enabled = true;
            snap = s;
            if (s == null)
            {
                Val(rowLast).Set(L.T("שגיאה בקריאה", "Read error"), "");
                Val(rowEst).Set("-", ""); Val(rowTok).Set("-", "");
                return;
            }
            rowLast.SetTexts(rowLast.Title.Text, s.LastModel == null ? L.T("לא נמצאו שיחות ב-7 הימים האחרונים.", "No conversations in the last 7 days.") : L.T("לפי ההודעה האחרונה בקבצי השיחות.", "From the latest message in the conversation files."));
            Val(rowLast).Set(ModelName(s.LastModel), "");
            UpdateLimitRows();
            Val(rowTok).Set(Fmt(s.Tok5h) + L.T(" ב-5 שעות", " in 5h"), "");
            rowTok.SetTexts(rowTok.Title.Text, L.T("היום: ", "Today: ") + Fmt(s.TokToday) + "\n" + L.T("24 שעות: ", "24 hours: ") + Fmt(s.Tok24h) + "\n" + L.T("7 ימים: ", "7 days: ") + Fmt(s.Tok7d));
            tip.SetToolTip(Val(rowTok), L.T("5 שעות: ", "5h: ") + s.Tok5h.ToString("N0") + "\n" + L.T("היום: ", "Today: ") + s.TokToday.ToString("N0") + "\n24: " + s.Tok24h.ToString("N0") + "\n7: " + s.Tok7d.ToString("N0"));

            // פילוח מודלים
            ClearCard(cardModels);
            if (s.Models.Count == 0) AddRow(cardModels, L.T("אין נתונים", "No data"), L.T("לא נמצאה פעילות ב-7 הימים האחרונים.", "No activity in the last 7 days."), null, false, 0, null);
            foreach (var m in s.Models)
            {
                var v = new UsageValue();
                v.Set(Fmt(m.In + m.Out), "cache " + Fmt(m.CR) + " / " + Fmt(m.CC));
                var r = AddRow(cardModels, ModelName(m.Model), m.Count + L.T(" תשובות", " replies"), v, false, 150,
                    L.T("קלט + פלט: ", "Input + output: ") + (m.In + m.Out).ToString("N0") + "\ncache read: " + m.CR.ToString("N0") + "\ncache creation: " + m.CC.ToString("N0"));
            }

            // שיחות אחרונות
            ClearCard(cardSessions);
            if (s.Sessions.Count == 0) AddRow(cardSessions, L.T("אין שיחות", "No conversations"), L.T("לא נמצאו שיחות ב-7 הימים האחרונים.", "No conversations in the last 7 days."), null, false, 0, null);
            foreach (var x in s.Sessions)
            {
                var v = new UsageValue();
                v.Set(Fmt(x.In + x.Out), "cache " + Fmt(x.CR) + " / " + Fmt(x.CC));
                string title = string.IsNullOrEmpty(x.Title) ? L.T("(שיחה בלי כותרת)", "(untitled conversation)") : x.Title;
                string desc = (x.Project ?? "") + " | " + ModelName(x.Model) + " | " + Ago(x.Last) + "\n" + L.T("הקשר נוכחי: ", "Context: ") + Fmt(x.Context);
                AddRow(cardSessions, title, desc, v, false, 130,
                    L.T("קלט + פלט: ", "Input + output: ") + (x.In + x.Out).ToString("N0") + "\ncache read: " + x.CR.ToString("N0") + "\ncache creation: " + x.CC.ToString("N0") + "\n" +
                    L.T("cache = חלקי שיחה שנשמרו אצל Anthropic כדי לחסוך. קריאה זולה, יצירה יקרה מעט. \"הקשר\" = גודל ההקשר בהודעה האחרונה.", "cache = conversation parts kept by Anthropic to save cost. Reads are cheap, creation costs slightly more. \"Context\" = context size at the last message.") + "\n" + x.Id);
            }
            FitContent();
        }

        void ClearCard(Card c)
        {
            for (int i = c.Controls.Count - 1; i >= 1; i--)
            {
                var ctl = c.Controls[i];
                c.Controls.RemoveAt(i);
                ctl.Dispose();
            }
            c.Relayout();
        }

        // ---- מכסות
        void UpdateLimitRows()
        {
            SetLimit(row5, bar5, limits != null ? limits.Five : -1, limits != null ? limits.FiveReset : 0, L.T("מכסת 5 שעות", "5-hour limit"));
            SetLimit(row7, bar7, limits != null ? limits.Seven : -1, limits != null ? limits.SevenReset : 0, L.T("מכסה שבועית", "Weekly limit"));
            string st = fetching ? L.T("מודד...", "Measuring...") :
                fetchMsg != null ? fetchMsg :
                limits != null ? L.T("נמדד לאחרונה ב-", "Last measured at ") + When(limits.Fetched) : L.T("עוד לא נמדד. לחץ \"מדוד עכשיו\" לקבלת האחוזים המדויקים.", "Not measured yet. Press \"Measure now\" for the exact percentages.");
            rowFetch.SetTexts(rowFetch.Title.Text, st);
            btnFetch.Enabled = !fetching;

            var v = Val(rowEst);
            if (snap == null) return;
            if (snap.WinStart == 0) { v.Set(L.T("אין חלון פעיל", "No active window"), ""); rowEst.SetTexts(rowEst.Title.Text, L.T("לא נשלחה הודעה ב-5 השעות האחרונות.", "No message in the last 5 hours.")); }
            else
            {
                var st0 = new DateTime(snap.WinStart, DateTimeKind.Utc).ToLocalTime();
                var en = new DateTime(snap.WinEnd, DateTimeKind.Utc).ToLocalTime();
                v.Set(L.T("עד ", "until ") + When(en), L.T("התחיל ב-", "started ") + When(st0));
                rowEst.SetTexts(rowEst.Title.Text, Remain(en));
            }
        }

        void SetLimit(Row r, UsageBar b, double util, long resetUnix, string name)
        {
            if (util < 0)
            {
                b.Value = -1;
                r.SetTexts(name + ": " + L.T("לא ידוע", "unknown"), L.T("לחץ \"מדוד עכשיו\" כדי לקבל את הנתון מ-Claude.", "Press \"Measure now\" to get the figure from Claude."));
                return;
            }
            DateTime reset = resetUnix > 0 ? new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddSeconds(resetUnix).ToLocalTime() : DateTime.MinValue;
            bool expired = resetUnix > 0 && reset <= DateTime.Now;
            string txt;
            if (expired)
            {
                b.Value = -1;
                r.SetTexts(name + ": " + L.T("המדידה ישנה", "outdated"), L.T("החלון הסתיים ב-", "The window ended at ") + When(reset) + L.T(". מדוד שוב.", ". Measure again."));
                return;
            }
            b.Value = util;
            txt = Math.Round(util * 100).ToString(CultureInfo.InvariantCulture) + "% " + L.T("בשימוש", "used");
            string d = reset == DateTime.MinValue ? "" : L.T("מתאפס ב-", "Resets at ") + When(reset) + " (" + Remain(reset) + ")";
            if (limits != null) d += (d.Length > 0 ? " | " : "") + L.T("נמדד ב-", "measured ") + When(limits.Fetched);
            r.SetTexts(name + ": " + txt, d);
        }

        void StartFetch()
        {
            if (fetching) return;
            fetching = true; fetchMsg = null;
            UpdateLimitRows();
            var th = new Thread(delegate()
            {
                string err;
                LimitInfo li = null;
                try { li = UsageData.FetchLimits(out err); } catch (Exception ex) { err = ex.Message; }
                try
                {
                    BeginInvoke(new Action(delegate
                    {
                        fetching = false;
                        if (li != null) { limits = li; fetchMsg = null; } else fetchMsg = L.T("המדידה נכשלה: ", "Measurement failed: ") + err;
                        UpdateLimitRows();
                    }));
                }
                catch { }
            });
            th.IsBackground = true;
            th.Start();
        }

        protected override void OnVisibleChanged(EventArgs e)
        {
            base.OnVisibleChanged(e);
            if (Visible) UpdateLimitRows();
        }
    }
}
