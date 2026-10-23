using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using Microsoft.Win32;

// חלון ההגדרות וחלון ההודעה המתוזמנת, בעיצוב המשותף (UiKit.cs). עברית (RTL מלא) או אנגלית (LTR), לפי ההגדרה Language.

namespace ClaudeNotify
{
    // בסיס משותף: אזור גלילה עם כרטיסים, שורת כפתורים קבועה בתחתית, ובועות הסבר
    class PageForm : Form
    {
        protected readonly Panel host = new Panel();
        protected readonly Stack content = new Stack();
        protected readonly Panel bar = new Panel();
        protected readonly ToolTip tip = new ToolTip { AutoPopDelay = 30000, InitialDelay = 350, ReshowDelay = 100, ShowAlways = true };

        public PageForm(string title, int barHeight)
        {
            Text = title;
            Font = Theme.F(9.5f);
            BackColor = Theme.Bg;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            Icon = Branding.AppIcon;
            ClientSize = new Size(Theme.S(540), Math.Min(Theme.S(620), Screen.PrimaryScreen.WorkingArea.Height - Theme.S(60)));

            bar.Dock = DockStyle.Bottom;
            bar.Height = Theme.S(barHeight);
            bar.BackColor = Color.White;
            bar.Paint += delegate(object s, PaintEventArgs e) { using (var p = new Pen(Theme.Border)) e.Graphics.DrawLine(p, 0, 0, bar.Width, 0); };

            host.Dock = DockStyle.Fill;
            host.AutoScroll = true;
            host.BackColor = Theme.Bg;
            content.PadX = Theme.S(18); content.PadTop = Theme.S(16); content.PadBottom = Theme.S(16); content.Gap = Theme.S(12);
            content.BackColor = Theme.Bg;
            host.Controls.Add(content);
            Controls.Add(host);
            Controls.Add(bar);   // נוסף אחרון, ולכן מוצמד לתחתית לפני שהאזור הראשי ממלא את השאר
            host.Resize += delegate { FitContent(); };
            FormClosing += delegate(object s, FormClosingEventArgs e)
            {
                if (e.CloseReason == CloseReason.UserClosing) { e.Cancel = true; Hide(); }
            };
        }

        protected void FitContent()
        {
            content.Width = Math.Max(300, host.ClientSize.Width);
            content.Relayout();
        }

        protected override void OnShown(EventArgs e) { base.OnShown(e); FitContent(); }
        protected override void OnVisibleChanged(EventArgs e) { base.OnVisibleChanged(e); if (Visible) FitContent(); }

        protected Row AddRow(Card card, string title, string desc, Control c, bool stacked, int width, string tooltip)
        {
            var r = new Row(title, desc, c, stacked, width);
            card.Add(r);
            if (!string.IsNullOrEmpty(tooltip)) { tip.SetToolTip(r.Title, tooltip); tip.SetToolTip(r.Desc, tooltip); }
            return r;
        }

        protected static MessageBoxOptions MsgOpts { get { return L.He ? MessageBoxOptions.RtlReading | MessageBoxOptions.RightAlign : 0; } }
        protected RightToLeft Dir { get { return L.He ? RightToLeft.Yes : RightToLeft.No; } }
    }

    // ---------------------------------------------------------------- Settings
    class SettingsForm : PageForm
    {
        readonly TrayContext ctx;
        Config cfg;
        ToggleSwitch tglEnabled, tglSummarize, tglSkip, tglPopup, tglStartup;
        Stepper numWords, numSeconds;
        Dropdown cmbPos, cmbLang;
        Field fldProcs;
        Header header;
        Row hookRow, secondsRow, posRow;
        PillButton btnInstall, btnRemove;
        bool loading;

        const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

        public SettingsForm(TrayContext ctx, Config cfg) : base("Claude Notify - " + L.T("הגדרות", "Settings"), 60)
        {
            this.ctx = ctx; this.cfg = cfg;

            header = new Header(L.T("הגדרות", "Settings"), "", null);
            content.Controls.Add(header);

            // ---- התראות
            var cn = new Card(L.T("התראות", "Notifications"));
            tglEnabled = new ToggleSwitch();
            AddRow(cn, L.T("הפעל התראות", "Enable notifications"), L.T("מציג חלון קטן כש-Claude מסיים להשיב.", "Shows a small popup when Claude finishes replying."), tglEnabled, false, 54, null);
            tglSummarize = new ToggleSwitch();
            AddRow(cn, L.T("סכם את ההודעה", "Summarize the message"), L.T("איטי יותר (כ-15 שניות). כשכבוי, מוצגת תחילת ההודעה.", "Slower (about 15 seconds). When off, the start of the message is shown."), tglSummarize, false, 54,
                L.T("מבקש מ-Claude לסכם את התשובה בכמה מילים. נספר במכסה, ומאט את ההתראה.", "Asks Claude to summarize the reply in a few words. Counts against your quota and delays the popup."));
            tglSkip = new ToggleSwitch();
            AddRow(cn, L.T("אל תציג כשהעורך גלוי", "Skip when the editor is visible"), L.T("אם חלון ה-VS Code של השיחה כבר על המסך, אין צורך להתריע.", "If the VS Code window of the conversation is already on screen, there is no need to notify."), tglSkip, false, 54, null);
            numWords = Ctl.Num(5, 60);
            AddRow(cn, L.T("מספר מילים מקסימלי", "Maximum words"), L.T("אורך הטקסט שמוצג בהתראה.", "Length of the text shown in the popup."), numWords, false, 124, null);
            tglPopup = new ToggleSwitch();
            AddRow(cn, L.T("חלון התראה משלי", "Custom popup window"), L.T("חלון בפינת המסך, עם שליטה בזמן ובמיקום. כשכבוי - התראת Windows רגילה.", "A window in the corner of the screen with control over time and position. When off - a regular Windows notification."), tglPopup, false, 54, null);
            numSeconds = Ctl.Num(2, 60);
            secondsRow = AddRow(cn, L.T("זמן הצגה (שניות)", "Display time (seconds)"), null, numSeconds, false, 124, null);
            cmbPos = Ctl.Combo(220, L.He);
            cmbPos.Items.Add(L.T("פינה ימנית למטה", "Bottom right"));
            cmbPos.Items.Add(L.T("פינה שמאלית למטה", "Bottom left"));
            posRow = AddRow(cn, L.T("מיקום על המסך", "Position on screen"), null, cmbPos, false, 220, null);
            tglPopup.CheckedChanged += delegate { numSeconds.Enabled = tglPopup.Checked; cmbPos.Enabled = tglPopup.Checked; };
            content.Controls.Add(cn);

            // ---- כללי
            var cg = new Card(L.T("כללי", "General"));
            cmbLang = Ctl.Combo(200, false);
            cmbLang.Items.Add("עברית");
            cmbLang.Items.Add("English");
            AddRow(cg, L.T("שפה", "Language"), L.T("שפת הממשק וההודעות.", "Language of the interface and messages."), cmbLang, false, 200, null);
            fldProcs = new Field(false);
            AddRow(cg, L.T("תהליכי העורך", "Editor processes"), L.T("שמות התהליכים של העורכים לזיהוי החלון, מופרדים בפסיק.", "Process names of the editors used to find the window, separated by commas."), fldProcs, true, 0, null);
            tglStartup = new ToggleSwitch();
            AddRow(cg, L.T("הפעל עם Windows", "Start with Windows"), L.T("התוכנה תרוץ ברקע מהאייקון ליד השעון.", "The app runs in the background from the tray icon."), tglStartup, false, 54, null);
            content.Controls.Add(cg);

            // ---- חיבור ל-Claude Code
            var ch = new Card(L.T("חיבור ל-Claude Code", "Claude Code connection"));
            var hookButtons = new FlowLayoutPanel { Height = Theme.S(46), Padding = new Padding(0, Theme.S(2), 0, Theme.S(2)), BackColor = Theme.Card, WrapContents = false, FlowDirection = L.He ? FlowDirection.RightToLeft : FlowDirection.LeftToRight };
            btnInstall = new PillButton(L.T("התקן חיבור", "Install connection"), true);
            btnRemove = new PillButton(L.T("הסר חיבור", "Remove connection"), false);
            btnInstall.Margin = Theme.P(0, 0, 8, 0);
            hookButtons.Controls.Add(btnInstall);
            hookButtons.Controls.Add(btnRemove);
            hookRow = AddRow(ch, "", "", hookButtons, true, 0, L.T("הוספת הוק לקובץ ההגדרות של Claude Code, כדי שהתוכנה תקבל התראה בכל פעם שהוא מסיים להשיב.", "Adds a hook to the Claude Code settings so this app is notified whenever it finishes replying."));
            content.Controls.Add(ch);

            // ---- כלים: שני החלונות הנוספים של התוכנה
            var ct = new Card(L.T("כלים", "Tools"));
            var btnSched = new PillButton(L.T("פתח", "Open"), false);
            btnSched.Click += delegate { ctx.ShowSchedule(); };
            AddRow(ct, L.T("הודעה מתוזמנת", "Scheduled message"), L.T("שליחת הודעה קצרה לקלוד בשעה שתבחר, כדי להתחיל את מכסת 5 השעות מראש.", "Sends Claude a short message at a time you choose, to start the 5-hour quota window early."), btnSched, false, 100, null);
            var btnUsage = new PillButton(L.T("פתח", "Open"), false);
            btnUsage.Click += delegate { ctx.ShowUsage(); };
            AddRow(ct, L.T("מידע טכני", "Technical info"), L.T("טוקנים לפי שיחה, מכסת 5 השעות והמכסה השבועית, ובאיזה מודל Claude משתמש.", "Tokens per conversation, the 5-hour and weekly limits, and which model Claude uses."), btnUsage, false, 100, null);
            content.Controls.Add(ct);

            btnInstall.Click += delegate
            {
                try
                {
                    HookInstaller.Install(); SaveStartup(tglStartup.Checked);
                    Dialog.Show(this, "Claude Notify", L.T("החיבור הותקן. אם ההתראות לא מופיעות, פתח /hooks ב-Claude Code או הפעל אותו מחדש.", "The connection was installed. If notifications do not appear, open /hooks in Claude Code or restart it."));
                }
                catch (Exception ex) { Fail(ex); }
                Reload();
            };
            btnRemove.Click += delegate
            {
                try { HookInstaller.Remove(); } catch (Exception ex) { Fail(ex); }
                Reload();
            };

            // ---- שורת כפתורים תחתונה
            var flow = new FlowLayoutPanel { Dock = DockStyle.Fill, Padding = Theme.P(18, 10, 18, 0), WrapContents = false, BackColor = Color.White, FlowDirection = L.He ? FlowDirection.RightToLeft : FlowDirection.LeftToRight };
            var btnSave = new PillButton(L.T("שמור", "Save"), true);
            var btnTest = new PillButton(L.T("התראת בדיקה", "Test notification"), false);
            btnSave.Margin = btnTest.Margin = Theme.P(0, 0, 10, 0);
            btnSave.Click += delegate { Save(); Hide(); };
            btnTest.Click += delegate { Save(); ctx.TestNotification(); };
            flow.Controls.Add(btnSave); flow.Controls.Add(btnTest);
            bar.Controls.Add(flow);
            AcceptButton = btnSave;
            Reload();
        }

        void Fail(Exception ex)
        {
            Dialog.Show(this, "Claude Notify", L.T("הפעולה נכשלה: ", "The action failed: ") + ex.Message);
        }

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
            tglEnabled.Checked = cfg.Enabled;
            tglSummarize.Checked = cfg.Summarize;
            tglSkip.Checked = cfg.SkipWhenVisible;
            numWords.Value = Math.Max(numWords.Minimum, Math.Min(numWords.Maximum, cfg.MaxWords));
            fldProcs.Box.Text = cfg.Processes;
            tglPopup.Checked = cfg.UseCustomPopup;
            numSeconds.Value = Math.Max(numSeconds.Minimum, Math.Min(numSeconds.Maximum, cfg.DisplaySeconds));
            numSeconds.Enabled = cfg.UseCustomPopup;
            cmbPos.SelectedIndex = cfg.PopupLeft ? 1 : 0;
            cmbPos.Enabled = cfg.UseCustomPopup;
            cmbLang.SelectedIndex = cfg.Language == "en" ? 1 : 0;
            tglStartup.Checked = StartupEnabled();
            bool inst = HookInstaller.IsInstalled();
            hookRow.SetTexts(inst ? L.T("החיבור מותקן", "Connection installed") : L.T("החיבור לא מותקן", "Connection not installed"),
                inst ? L.T("Claude Code ישלח התראה בכל פעם שהוא מסיים להשיב.", "Claude Code will notify you whenever it finishes replying.")
                     : L.T("נדרש כדי לקבל התראות. לחץ על \"התקן חיבור\".", "Required to receive notifications. Press \"Install connection\"."));
            hookRow.Title.ForeColor = inst ? Theme.Ok : Theme.Danger;
            btnInstall.Text = inst ? L.T("התקן מחדש", "Reinstall") : L.T("התקן חיבור", "Install connection");
            btnRemove.Enabled = inst;
            header.Sub = cfg.Enabled ? L.T("ההתראות פעילות", "Notifications are on") : L.T("ההתראות כבויות", "Notifications are off");
            loading = false;
            FitContent();
        }

        void Save()
        {
            if (loading) return;
            cfg.Enabled = tglEnabled.Checked;
            cfg.Summarize = tglSummarize.Checked;
            cfg.SkipWhenVisible = tglSkip.Checked;
            cfg.MaxWords = (int)numWords.Value;
            cfg.UseCustomPopup = tglPopup.Checked;
            cfg.DisplaySeconds = (int)numSeconds.Value;
            cfg.PopupLeft = cmbPos.SelectedIndex == 1;
            cfg.Language = cmbLang.SelectedIndex == 1 ? "en" : "he";
            string procs = fldProcs.Box.Text.Trim();
            cfg.Processes = procs.Length > 0 ? procs : "Code, Code - Insiders, Cursor";
            cfg.Save();
            SaveStartup(tglStartup.Checked);
            ctx.ConfigChanged(cfg);
        }
    }

    // בורר חלונות VS Code: שורה לכל חלון עם מתג, מתג "בחר הכל", וכפתור רענון
    class WinEntry { public VsWin Win; public Label Lbl; public ToggleSwitch Tgl; }

    class WindowPicker : Panel
    {
        public readonly PillButton RefreshBtn;
        readonly ToggleSwitch allToggle = new ToggleSwitch();
        readonly Label lblAll = new Label(), lblEmpty = new Label(), lblNote = new Label();
        readonly List<WinEntry> items = new List<WinEntry>();
        readonly ToolTip tip = new ToolTip { AutoPopDelay = 30000, InitialDelay = 350, ShowAlways = true };
        bool busy;
        int RowH { get { return Theme.S(34); } }

        public WindowPicker()
        {
            BackColor = Theme.Card;
            DoubleBuffered = true;
            SetStyle(ControlStyles.ResizeRedraw, true);
            RefreshBtn = new PillButton(L.T("רענן", "Refresh"), false);
            RefreshBtn.Height = Theme.S(30);
            StyleLabel(lblAll, true); lblAll.Text = L.T("בחר הכל", "Select all");
            StyleLabel(lblEmpty, false); lblEmpty.Text = L.T("לא נמצאו חלונות VS Code פתוחים", "No open VS Code windows found");
            StyleLabel(lblNote, false); lblNote.Font = Theme.F(8.5f);
            allToggle.CheckedChanged += delegate
            {
                if (busy) return;
                busy = true;
                foreach (var it in items) it.Tgl.Checked = allToggle.Checked;
                busy = false;
            };
            Controls.Add(lblAll); Controls.Add(allToggle); Controls.Add(lblEmpty); Controls.Add(lblNote); Controls.Add(RefreshBtn);
            SetWindows(new List<VsWin>(), null);
        }

        static void StyleLabel(Label l, bool semi)
        {
            l.AutoSize = false;
            l.AutoEllipsis = true;
            l.BackColor = Theme.Card;
            l.ForeColor = semi ? Theme.Text : Theme.Muted;
            l.Font = semi ? Theme.Semi(10f) : Theme.F(9f);
            l.RightToLeft = L.He ? RightToLeft.Yes : RightToLeft.No;
            l.TextAlign = ContentAlignment.MiddleLeft;   // WinForms הופך יישור תחת RTL, ולכן MiddleLeft = ההתחלה בשני הכיוונים
        }

        static string Display(VsWin w)
        {
            string t = w.Title;
            const string suffix = " - Visual Studio Code";
            bool bare = t == "Visual Studio Code";
            if (t.EndsWith(suffix, StringComparison.Ordinal)) t = t.Substring(0, t.Length - suffix.Length);
            if (w.Count > 1) t += " (" + (w.Index + 1) + ")";
            if (w.Conv.Length > 0) return t + " - " + w.Conv;
            return bare ? t + " " + L.T("(חלון בלי תיקייה)", "(window with no folder)") : t;
        }

        public void SetWindows(List<VsWin> wins, ICollection<string> selectedKeys)
        {
            foreach (var it in items) { Controls.Remove(it.Lbl); Controls.Remove(it.Tgl); it.Lbl.Dispose(); it.Tgl.Dispose(); }
            items.Clear();
            busy = true;
            foreach (var w in wins)
            {
                var e = new WinEntry { Win = w, Lbl = new Label(), Tgl = new ToggleSwitch() };
                StyleLabel(e.Lbl, false);
                e.Lbl.ForeColor = Theme.Text;
                e.Lbl.Text = Display(w);
                tip.SetToolTip(e.Lbl, e.Lbl.Text);
                e.Tgl.Checked = selectedKeys != null && selectedKeys.Contains(w.Key);
                e.Tgl.CheckedChanged += delegate { if (!busy) SyncAll(); };
                items.Add(e);
                Controls.Add(e.Lbl); Controls.Add(e.Tgl);
            }
            busy = false;
            SyncAll();
            lblEmpty.Visible = items.Count == 0;
            lblAll.Visible = allToggle.Visible = items.Count > 0;
            DoLayout();
        }

        void SyncAll()
        {
            bool prev = busy;
            busy = true;
            bool all = items.Count > 0;
            foreach (var it in items) if (!it.Tgl.Checked) all = false;
            allToggle.Checked = all;
            allToggle.Enabled = items.Count > 0;
            busy = prev;
        }

        // מעדכן כותרות שיחה שזוהו ברקע (מפתח חלון -> כותרת)
        public void SetConvs(Dictionary<string, string> convs)
        {
            foreach (var it in items)
            {
                string c;
                if (convs.TryGetValue(it.Win.Key, out c) && c.Length > 0) it.Win.Conv = c;
                it.Lbl.Text = Display(it.Win);
                tip.SetToolTip(it.Lbl, it.Lbl.Text);
            }
        }

        public void SetNote(string text) { lblNote.Text = text; }

        public List<string> SelectedKeys()
        {
            var res = new List<string>();
            foreach (var it in items) if (it.Tgl.Checked) res.Add(it.Win.Key);
            return res;
        }

        public Dictionary<string, string> SelectedConvs()
        {
            var res = new Dictionary<string, string>();
            foreach (var it in items) if (it.Tgl.Checked && it.Win.Conv.Length > 0) res[it.Win.Key] = it.Win.Conv;
            return res;
        }

        protected override void OnLayout(LayoutEventArgs e) { base.OnLayout(e); DoLayout(); }

        void DoLayout()
        {
            if (RefreshBtn == null) return;
            bool he = L.He;
            int tw = Theme.S(46), gap = Theme.S(12), rowH = RowH;
            int n = items.Count;
            int y = 0;
            int lw = Math.Max(50, Width - tw - gap);
            if (n > 0)
            {
                PlaceRow(lblAll, allToggle, y, lw, tw, rowH, he);
                y += rowH;
                for (int i = 0; i < n; i++, y += rowH) PlaceRow(items[i].Lbl, items[i].Tgl, y, lw, tw, rowH, he);
            }
            else
            {
                lblEmpty.SetBounds(0, y, Width, rowH);
                y += rowH;
            }
            int bw = RefreshBtn.Width;
            int by = y + Theme.S(4);
            RefreshBtn.Top = by + (rowH - RefreshBtn.Height) / 2;
            int nw = Math.Max(20, Width - bw - gap);
            if (he) { RefreshBtn.Left = Width - bw; lblNote.SetBounds(0, by, nw, rowH); }
            else { RefreshBtn.Left = 0; lblNote.SetBounds(bw + gap, by, nw, rowH); }
            int h = by + rowH + Theme.S(2);
            if (Height != h) { Height = h; RelayoutUp(); }
        }

        void PlaceRow(Label l, ToggleSwitch t, int y, int lw, int tw, int rowH, bool he)
        {
            t.Top = y + (rowH - t.Height) / 2;
            l.SetBounds(he ? Width - lw : 0, y, lw, rowH - 2);
            t.Left = he ? 0 : Width - tw;
        }

        // גובה הבורר השתנה: מסדרים מחדש את הכרטיס שמכיל אותו
        void RelayoutUp()
        {
            Control p = Parent;
            while (p != null)
            {
                var st = p as Stack;
                if (st != null) { st.Relayout(); return; }
                p = p.Parent;
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) tip.Dispose();
            base.Dispose(disposing);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            if (items.Count > 0)
                using (var p = new Pen(Theme.Border))
                    e.Graphics.DrawLine(p, 0, RowH - 1, Width, RowH - 1);
        }
    }

    // ---------------------------------------------------------------- Schedule
    class ScheduleForm : PageForm
    {
        readonly ScheduleRunner runner;
        List<BrowserProfile> profiles = new List<BrowserProfile>();
        Dictionary<string, string> savedConvs = new Dictionary<string, string>();
        int loadGen;
        Dropdown cmbMethod, cmbProfile, cmbWhen;
        WindowPicker picker;
        Field fldUrl, fldText;
        Stepper numDelay, numMinWait, numMaxWait, numTabs, numBattery;
        TimeBox dtpTime;
        ToggleSwitch tglSend, tglWake, tglAwake, tglScreenOff, tglAdv, tglNewChat;
        Row rowMethod, rowProfile, rowUrl, rowWindow, rowDelay, rowTime, rowSend, rowAdv, rowMin, rowMax, rowTabs, rowNewChat;
        Card cardAdv;
        PillButton btnStart, btnCancel;
        Label lblStatus, lblPower;
        Panel statusPanel;
        readonly Action<string, bool> onStatus, onFinished;
        readonly Action onLaunching;

        public ScheduleForm(ScheduleRunner runner) : base("Claude Notify - " + L.T("הודעה מתוזמנת", "Scheduled message"), 60)
        {
            this.runner = runner;

            var btnHelp = new PillButton(L.T("איך זה עובד?", "How does it work?"), false);
            btnHelp.Height = Theme.S(30);
            btnHelp.Click += delegate { ShowHelp(); };
            content.Controls.Add(new Header(L.T("הודעה מתוזמנת", "Scheduled message"),
                L.T("שולח הודעה קצרה לקלוד בשעה שתבחר, כדי שמכסת 5 השעות תתחיל לרוץ מראש.", "Sends Claude a short message at a time you choose, so your 5-hour quota window starts early."), btnHelp));

            // ---- מה לשלוח
            var c1 = new Card(L.T("מה לשלוח ולאן", "What to send, and where"));
            cmbMethod = Ctl.Combo(300, L.He);
            cmbMethod.Items.Add(L.T("דרך הדפדפן (פרופיל מחובר ל-claude.ai)", "Through the browser (profile signed in to claude.ai)"));
            cmbMethod.Items.Add(L.T("דרך Claude Code, בלי דפדפן", "Through Claude Code, no browser"));
            cmbMethod.Items.Add(L.T("לחלונות VS Code שנבחרו (רואים את ההודעה)", "To chosen VS Code windows (you can see the message)"));
            rowMethod = AddRow(c1, L.T("שיטת שליחה", "Send method"), "", cmbMethod, true, 0,
                L.T("דרך הדפדפן: פותח Chrome/Edge/Brave ומקליד ב-claude.ai (דורש פרופיל מחובר ומחשב לא נעול).\nדרך Claude Code: שליחה שקטה ברקע, בלי חלון - הכי אמין, אבל לא רואים את ההודעה באף חלון.\nלחלונות VS Code: שולח הודעה נפרדת לכל חלון שתסמן (אחד אחרי השני), בשיחה חדשה או בשיחה הפעילה.",
                    "Browser: opens Chrome/Edge/Brave and types into claude.ai (needs a signed-in profile and an unlocked computer).\nClaude Code: silent background send, no window - most reliable, but you will not see the message in any window.\nVS Code windows: sends a separate message to each window you tick (one after another), in a new or the active conversation."));

            picker = new WindowPicker();
            picker.RefreshBtn.Click += delegate { LoadWindows(picker.SelectedKeys(), picker.SelectedConvs()); };
            rowWindow = AddRow(c1, L.T("חלונות VS Code", "VS Code windows"), L.T("סמן לאילו חלונות לשלוח. לכל חלון נשלחת הודעה נפרדת, בזה אחר זה. לצד שם החלון מופיעה כותרת השיחה שבו. אם פתחת או סגרת חלונות - לחץ \"רענן\".", "Tick the windows to send to. Each window gets its own message, one after another. Each window shows its conversation title. If you opened or closed windows, press \"Refresh\"."), picker, true, 0, null);
            tglNewChat = new ToggleSwitch();
            rowNewChat = AddRow(c1, L.T("שיחה חדשה בכל חלון", "New chat in each window"), L.T("דלוק = נפתחת שיחה חדשה. כבוי = ממשיכים בשיחה הפעילה של החלון (מתאים להמשך עבודה אחרי איפוס המכסה).", "On = a new conversation is opened. Off = continue in the window's active conversation (good for resuming work after the quota resets)."), tglNewChat, false, 54,
                L.T("כבוי: ההודעה נכתבת בתיבת ההקלדה הגלויה של Claude בחלון, ללא פתיחת שיחה חדשה. טיוטה שכבר כתובה בתיבה תוחלף.", "Off: the message is typed into the visible Claude input box of the window, without opening a new conversation. A draft already in the box is replaced."));

            cmbProfile = Ctl.Combo(300, false);
            rowProfile = AddRow(c1, L.T("פרופיל דפדפן", "Browser profile"), L.T("הפרופיל שמחובר לחשבון Claude שלך. הדפדפן ייפתח איתו.", "The profile signed in to your Claude account. The browser opens with it."), cmbProfile, true, 0, null);
            fldUrl = new Field(false);
            rowUrl = AddRow(c1, L.T("כתובת האתר", "Website address"), L.T("ברירת המחדל https://claude.ai/new פותחת שיחה חדשה ומסמנת את תיבת הכתיבה.", "The default https://claude.ai/new opens a new chat and focuses the input box."), fldUrl, true, 0, null);
            fldText = new Field(L.He);
            AddRow(c1, L.T("ההודעה", "Message"), L.T("עדיף משהו קצר וזול, כמו \"היי\". המטרה רק להתחיל את ספירת 5 השעות.", "Something short and cheap, like \"hi\". The goal is only to start the 5-hour count."), fldText, true, 0, null);
            content.Controls.Add(c1);

            // ---- מתי
            var c2 = new Card(L.T("מתי", "When"));
            cmbWhen = Ctl.Combo(260, L.He);
            cmbWhen.Items.Add(L.T("אחרי השהיה בשניות", "After a delay (seconds)"));
            cmbWhen.Items.Add(L.T("בשעה מסוימת", "At a specific time"));
            AddRow(c2, L.T("מתי להפעיל", "When to run"), L.T("\"אחרי השהיה\" טוב לבדיקה מהירה. \"בשעה מסוימת\" - למשל 06:00 בבוקר.", "\"After a delay\" is good for a quick test. \"At a specific time\" - for example 06:00 in the morning."), cmbWhen, false, 260, null);
            numDelay = Ctl.Num(0, 86400);
            rowDelay = AddRow(c2, L.T("שניות עד ההפעלה", "Seconds until it runs"), L.T("10 שניות מספיקות לבדיקה.", "10 seconds is enough for a test."), numDelay, false, 120, null);
            dtpTime = new TimeBox();
            rowTime = AddRow(c2, L.T("שעה", "Time"), L.T("אם השעה כבר עברה היום - ההודעה תישלח מחר באותה שעה.", "If the time has already passed today, the message is sent tomorrow at the same time."), dtpTime, false, 250,
                L.T("שעות : דקות : שניות. אפשר ללחוץ על + ו- או להקליד מספר.", "Hours : minutes : seconds. Use + and - or type a number."));
            content.Controls.Add(c2);

            // ---- אפשרויות
            var c3 = new Card(L.T("אפשרויות", "Options"));
            tglSend = new ToggleSwitch();
            rowSend = AddRow(c3, L.T("לשלוח (Enter)", "Send it (Enter)"), L.T("כבוי = הטקסט רק מוכנס לתיבה.", "Off = the text is only typed into the box."), tglSend, false, 54, null);
            tglWake = new ToggleSwitch();
            AddRow(c3, L.T("להעיר את המחשב משינה", "Wake the computer from sleep"), L.T("מתזמן המשימות של Windows יעיר אותו בשעה שנקבעה.", "Windows Task Scheduler wakes it at the set time."), tglWake, false, 54,
                L.T("מתזמן המשימות יעיר את המחשב בדיוק בשעה שנקבעה, וגם יפעיל את התוכנה אם היא נסגרה. אם המחשב כבוי לגמרי - לא נשלח כלום. במחשב נייד עם מכסה סגור לרוב לא יעבוד.", "Task Scheduler wakes the computer at the exact time and also starts this app if it was closed. If the computer is fully off, nothing is sent. Usually does not work on a laptop with the lid closed."));
            tglAwake = new ToggleSwitch();
            AddRow(c3, L.T("לשמור על המחשב ער עד אז", "Keep the computer awake until then"), L.T("מונע שינה עד שעת השליחה. כדאי להשאיר דלוק.", "Prevents sleep until the send time. Best left on."), tglAwake, false, 54, null);
            tglScreenOff = new ToggleSwitch();
            AddRow(c3, L.T("לאפשר למסך לכבות", "Allow the screen to turn off"),
                runner.ModernStandby ? L.T("לא אפשרי במחשב הזה, כי כיבוי מסך מרדים אותו.", "Not possible on this computer, because turning the screen off puts it to sleep.") : L.T("המסך יידלק מחדש בשעת ההפעלה.", "The screen turns back on at the scheduled time."),
                tglScreenOff, false, 54, null);
            if (runner.ModernStandby) tglScreenOff.Enabled = false;
            numBattery = Ctl.Num(0, 100);
            AddRow(c3, L.T("סף סוללה (%)", "Battery threshold (%)"), L.T("על סוללה: מתחת לאחוז הזה התוכנה מפסיקה למנוע שינה.", "On battery: below this percentage the app stops preventing sleep."), numBattery, false, 124, null);
            tglAdv = new ToggleSwitch();
            rowAdv = AddRow(c3, L.T("הגדרות מתקדמות לדפדפן", "Advanced browser settings"), L.T("זמני המתנה והקשות Tab.", "Wait times and Tab presses."), tglAdv, false, 54, null);
            content.Controls.Add(c3);

            cardAdv = new Card(L.T("מתקדם - דפדפן", "Advanced - browser"));
            numMinWait = Ctl.Num(0, 60);
            rowMin = AddRow(cardAdv, L.T("המתנה לפני הקלדה (שניות)", "Wait before typing (seconds)"), L.T("זמן לדף להיטען לפני הניסיון הראשון. ברשת איטית - הגדל.", "Time for the page to load before the first try. On a slow network, increase."), numMinWait, false, 124, null);
            numMaxWait = Ctl.Num(10, 600);
            rowMax = AddRow(cardAdv, L.T("מקסימום המתנה לדף (שניות)", "Maximum wait for the page (seconds)"), L.T("אחרי זה התוכנה מוותרת ומדווחת שגיאה.", "After that the app gives up and reports an error."), numMaxWait, false, 124, null);
            numTabs = Ctl.Num(0, 30);
            rowTabs = AddRow(cardAdv, L.T("לחיצות Tab", "Tab presses"), L.T("בדרך כלל 0. אם הטקסט לא נכנס לתיבה - נסה מספר שמגיע אליה.", "Usually 0. If the text does not go into the box, try a number that reaches it."), numTabs, false, 124, null);
            content.Controls.Add(cardAdv);

            content.Controls.Add(new Note(L.T("אפשר לסגור את החלון - התזמון ממשיך לרוץ ברקע מהאייקון. יציאה מהתוכנה מבטלת אותו, והמחשב צריך להישאר דלוק.",
                "You can close this window - the schedule keeps running in the background from the tray icon. Exiting the app cancels it, and the computer must stay on.")));

            // ---- שורה תחתונה: סטטוס וכפתורים
            statusPanel = new Panel { Dock = DockStyle.Top, Height = 0, BackColor = Color.White };
            lblStatus = new Label { Dock = DockStyle.Top, Height = Theme.S(34), Font = Theme.Semi(10f), ForeColor = Theme.Text, BackColor = Color.White, RightToLeft = Dir, TextAlign = ContentAlignment.MiddleLeft, Padding = Theme.P(18, 4, 18, 0) };
            lblPower = new Label { Dock = DockStyle.Top, Height = Theme.S(16), Font = Theme.F(9f), ForeColor = Theme.Danger, BackColor = Color.White, RightToLeft = Dir, TextAlign = ContentAlignment.MiddleLeft, Padding = Theme.P(18, 0, 18, 0) };
            statusPanel.Controls.Add(lblPower);
            statusPanel.Controls.Add(lblStatus);
            var flow = new FlowLayoutPanel { Dock = DockStyle.Fill, Padding = Theme.P(18, 9, 18, 0), WrapContents = false, BackColor = Color.White, FlowDirection = L.He ? FlowDirection.RightToLeft : FlowDirection.LeftToRight };
            btnStart = new PillButton(L.T("התחל", "Start"), true);
            btnCancel = new PillButton(L.T("בטל", "Cancel"), false);
            btnStart.Margin = Theme.P(0, 0, 10, 0);
            btnStart.Click += delegate { DoStart(); };
            btnCancel.Click += delegate { runner.Cancel(); };
            flow.Controls.Add(btnStart); flow.Controls.Add(btnCancel);
            bar.Controls.Add(flow);          // הפקד שממלא נוסף ראשון, ואז החלק העליון
            bar.Controls.Add(statusPanel);
            tip.SetToolTip(btnStart, L.T("מפעיל את התזמון. אפשר לסגור את החלון אחרי זה.", "Starts the schedule. You can close this window afterwards."));
            tip.SetToolTip(btnCancel, L.T("מבטל את התזמון הפעיל.", "Cancels the running schedule."));

            cmbMethod.SelectedIndexChanged += delegate { ApplyVisibility(); };
            cmbWhen.SelectedIndexChanged += delegate { ApplyVisibility(); };
            tglAdv.CheckedChanged += delegate { ApplyVisibility(); };

            onStatus = delegate(string text, bool error) { if (IsDisposed) return; SetStatus(text, error); lblPower.Text = runner.PowerWarning; };
            onFinished = delegate(string text, bool error) { if (IsDisposed) return; SetRunning(false); SetStatus(text, error); lblPower.Text = ""; };
            onLaunching = delegate { if (!IsDisposed) Hide(); };   // שהחלון הזה לא יגנוב את הפוקוס
            runner.Status += onStatus;
            runner.Finished += onFinished;
            runner.Launching += onLaunching;

            LoadFromConfig(SchedConfig.Load());
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                runner.Status -= onStatus;
                runner.Finished -= onFinished;
                runner.Launching -= onLaunching;
            }
            base.Dispose(disposing);
        }

        void ShowHelp()
        {
            Dialog.Show(this, L.T("Claude Notify - עזרה", "Claude Notify - Help"), L.T("למה צריך את זה?\nמכסת השימוש של Claude מתאפסת בחלונות של 5 שעות, שמתחילים מההודעה הראשונה. אם ההודעה הראשונה נשלחת מוקדם (למשל ב-6:00 בבוקר), המכסה מתחדשת ב-11:00, באמצע יום העבודה.\n\n" +
                    "איך משתמשים?\n1. בחר שיטת שליחה (מומלץ: \"דרך Claude Code\", או \"לחלון VS Code\" אם רוצים לראות את ההודעה).\n2. אם בחרת חלונות VS Code - סמן לאילו חלונות לשלוח (לכל אחד נשלחת הודעה נפרדת), ובחר אם לפתוח שיחה חדשה או להמשיך בפעילה.\n3. כתוב הודעה קצרה, למשל \"היי\".\n4. בחר מתי: אחרי כמה שניות (לבדיקה) או בשעה קבועה.\n5. לחץ \"התחל\".\n\n" +
                    "חשוב לדעת:\n• המחשב צריך להישאר דלוק (בשיטות עם חלון - גם לא נעול).\n• אפשר לסגור את החלון - התזמון רץ ברקע מהאייקון ליד השעון.\n• יציאה מהתוכנה מבטלת את התזמון.\n• בשעת השליחה בשיטות עם חלון - לא לגעת במקלדת ובעכבר.",
                    "Why do you need this?\nClaude's usage quota resets in 5-hour windows that start from the first message. If the first message is sent early (for example at 6:00 AM), the quota renews at 11:00, in the middle of your workday.\n\n" +
                    "How to use it?\n1. Choose a send method (recommended: \"Through Claude Code\", or \"To a VS Code window\" if you want to see the message).\n2. If you chose VS Code windows - tick which windows to send to (each gets its own message), and choose a new chat or continuing the active one.\n3. Write a short message, for example \"hi\".\n4. Choose when: after a few seconds (to test) or at a fixed time.\n5. Press \"Start\".\n\n" +
                    "Good to know:\n• The computer must stay on (for window-based methods - also unlocked).\n• You can close this window - the schedule keeps running in the background from the tray icon.\n• Exiting the app cancels the schedule.\n• While window-based methods run, do not touch the keyboard or mouse."));
        }

        // מציג מיד את רשימת החלונות, ואז מזהה ברקע את כותרת השיחה של כל חלון (UI Automation) ומעדכן
        void LoadWindows(ICollection<string> selected, Dictionary<string, string> convs)
        {
            int gen = ++loadGen;
            var wins = VsWindows.List();
            var sel = new List<string>();
            foreach (var k in selected ?? new List<string>())
                foreach (var w in wins) if (w.Key == k || w.Key == k + "#0") { sel.Add(w.Key); break; }
            if (sel.Count == 0 && wins.Count > 0 && (selected == null || selected.Count == 0)) sel.Add(wins[0].Key);
            if (convs != null)
                foreach (var w in wins) { string c; if (convs.TryGetValue(w.Key, out c)) w.Conv = c; }
            picker.SetWindows(wins, sel);
            if (wins.Count == 0) { picker.SetNote(""); FitContent(); return; }
            picker.SetNote(L.T("מזהה כותרות שיחה...", "Reading conversation titles..."));
            FitContent();
            var sync = System.Threading.SynchronizationContext.Current;
            System.Threading.ThreadPool.QueueUserWorkItem(delegate
            {
                var res = new Dictionary<string, string>();
                try
                {
                    var known = ConvTitles.Known();
                    foreach (var w in wins)
                    {
                        string t = ConvTitles.ForWindow(w.Handle, known);
                        if (t.Length > 0) res[w.Key] = t;
                    }
                }
                catch (Exception ex) { Log.Write("conv titles: " + ex.Message); }
                System.Threading.SendOrPostCallback done = delegate
                {
                    if (IsDisposed || gen != loadGen) return;
                    picker.SetConvs(res);
                    picker.SetNote("");
                };
                if (sync != null) sync.Post(done, null);
            });
        }

        void ApplyVisibility()
        {
            int m = cmbMethod.SelectedIndex;
            bool browser = m == 0, vs = m == 2;
            rowProfile.Shown = browser;
            rowUrl.Shown = browser;
            rowSend.Shown = browser;
            rowAdv.Shown = browser;
            rowWindow.Shown = vs;
            rowNewChat.Shown = vs;
            cardAdv.Shown = browser && tglAdv.Checked;
            rowDelay.Shown = cmbWhen.SelectedIndex != 1;
            rowTime.Shown = cmbWhen.SelectedIndex == 1;
            string hint = vs
                ? L.T("מעלה כל חלון שבחרת לחזית (בזה אחר זה), פותח בו שיחה חדשה או ממשיך בפעילה, ושולח הודעה נפרדת. בשעת ההפעלה לא לגעת במקלדת ובעכבר.", "Brings each chosen window to the front (one after another), opens a new conversation or continues the active one, and sends a separate message. Do not touch the keyboard or mouse while it runs.")
                : browser
                ? L.T("פותח את הדפדפן עם הפרופיל שבחרת (חייב להיות מחובר ל-claude.ai), ומקליד שם. בשעת ההפעלה לא לגעת במקלדת ובעכבר.", "Opens the browser with the chosen profile (must be signed in to claude.ai) and types there. Do not touch the keyboard or mouse while it runs.")
                : L.T("שליחה שקטה דרך Claude Code (מודל Haiku), בלי חלון ובלי הקלדה - לכן גם לא תלויה במסך נעול. נספרת על אותה מכסה.", "A silent send through Claude Code (Haiku model), with no window and no typing - so it does not depend on the screen being locked. Counts against the same quota.");
            rowMethod.SetTexts(L.T("שיטת שליחה", "Send method"), hint);
            FitContent();
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
            cmbMethod.SelectedIndex = s.Method == "cli" ? 1 : s.Method == "vscode" ? 2 : 0;
            LoadProfiles(s.ProfileKey);
            savedConvs = s.WindowConvs ?? new Dictionary<string, string>();
            LoadWindows(s.WindowKeys, savedConvs);
            tglNewChat.Checked = s.NewChat;
            fldUrl.Box.Text = s.Url;
            fldText.Box.Text = s.Text;
            cmbWhen.SelectedIndex = s.UseTime ? 1 : 0;
            numDelay.Value = s.DelaySeconds;
            TimeSpan t; TimeSpan.TryParse(s.AtTime, out t);
            dtpTime.Time = t;
            numMinWait.Value = s.MinWaitSeconds;
            numMaxWait.Value = s.MaxWaitSeconds;
            numTabs.Value = s.TabCount;
            tglSend.Checked = s.SendEnter;
            tglWake.Checked = s.Wake;
            tglAwake.Checked = s.KeepAwake;
            tglScreenOff.Checked = s.AllowScreenOff && !runner.ModernStandby;
            numBattery.Value = s.MinBattery;
            tglAdv.Checked = s.MinWaitSeconds != 3 || s.MaxWaitSeconds != 60 || s.TabCount != 0;
            ApplyVisibility();
            SetRunning(runner.Running);
        }

        SchedConfig Collect()
        {
            var s = new SchedConfig();
            s.Method = cmbMethod.SelectedIndex == 1 ? "cli" : cmbMethod.SelectedIndex == 2 ? "vscode" : "browser";
            s.WindowKeys = picker.SelectedKeys();
            s.WindowTitle = s.WindowKeys.Count > 0 ? VsWindows.TitleOfKey(s.WindowKeys[0]) : "";
            s.WindowConvs = picker.SelectedConvs();
            foreach (var k in s.WindowKeys) { string sc; if (!s.WindowConvs.ContainsKey(k) && savedConvs.TryGetValue(k, out sc)) s.WindowConvs[k] = sc; }
            s.NewChat = tglNewChat.Checked;
            int pi = cmbProfile.SelectedIndex;
            s.ProfileKey = pi >= 0 && pi < profiles.Count ? profiles[pi].Key : "";
            s.Url = fldUrl.Box.Text.Trim();
            s.Text = fldText.Box.Text;
            s.UseTime = cmbWhen.SelectedIndex == 1;
            s.DelaySeconds = (int)numDelay.Value;
            s.AtTime = dtpTime.Time.ToString(@"hh\:mm\:ss");
            s.MinWaitSeconds = (int)numMinWait.Value;
            s.MaxWaitSeconds = (int)numMaxWait.Value;
            s.TabCount = (int)numTabs.Value;
            s.SendEnter = tglSend.Checked;
            s.Wake = tglWake.Checked;
            s.KeepAwake = tglAwake.Checked;
            s.AllowScreenOff = tglScreenOff.Checked && !runner.ModernStandby;
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
            lblStatus.Text = L.Tr(text);
            lblStatus.ForeColor = error ? Theme.Danger : Theme.Text;
            // השורה התחתונה גדלה רק כשיש מה להציג
            statusPanel.Height = string.IsNullOrEmpty(text) ? 0 : Theme.S(52);
            bar.Height = Theme.S(58) + statusPanel.Height;
        }

        // נקרא בכל פתיחה של החלון מהאייקון
        public void Reload()
        {
            if (!runner.Running)
            {
                var sc = SchedConfig.Load();
                LoadProfiles(sc.ProfileKey);
                savedConvs = sc.WindowConvs ?? new Dictionary<string, string>();
                LoadWindows(sc.WindowKeys, savedConvs);
            }
            SetRunning(runner.Running);
        }
    }
}


