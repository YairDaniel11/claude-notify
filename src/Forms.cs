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
            Icon = Branding.MakeIcon(32);
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

            header = new Header("Claude Notify", "", null);
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

            btnInstall.Click += delegate
            {
                try
                {
                    HookInstaller.Install(); SaveStartup(tglStartup.Checked);
                    MessageBox.Show(this, L.T("החיבור הותקן. אם ההתראות לא מופיעות, פתח /hooks ב-Claude Code או הפעל אותו מחדש.", "The connection was installed. If notifications do not appear, open /hooks in Claude Code or restart it."), "Claude Notify", MessageBoxButtons.OK, MessageBoxIcon.Information, MessageBoxDefaultButton.Button1, MsgOpts);
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
            var btnSched = new PillButton(L.T("הודעה מתוזמנת...", "Scheduled message..."), false);
            btnSave.Margin = btnTest.Margin = Theme.P(0, 0, 10, 0);
            btnSave.Click += delegate { Save(); Hide(); };
            btnTest.Click += delegate { Save(); ctx.TestNotification(); };
            btnSched.Click += delegate { ctx.ShowSchedule(); };
            flow.Controls.Add(btnSave); flow.Controls.Add(btnTest); flow.Controls.Add(btnSched);
            bar.Controls.Add(flow);
            AcceptButton = btnSave;
            tip.SetToolTip(btnSched, L.T("שליחת הודעה קצרה לקלוד בשעה שתבחר, כדי להתחיל את מכסת 5 השעות מראש.", "Sends a short message to Claude at a time you choose, to start the 5-hour quota window early."));
            Reload();
        }

        void Fail(Exception ex)
        {
            MessageBox.Show(this, L.T("הפעולה נכשלה: ", "The action failed: ") + ex.Message, "Claude Notify", MessageBoxButtons.OK, MessageBoxIcon.Error, MessageBoxDefaultButton.Button1, MsgOpts);
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

    // בורר חלון VS Code: תיבה נפתחת + כפתור רענון
    class WindowPicker : Panel
    {
        public readonly Dropdown Combo;
        public readonly PillButton RefreshBtn;

        public WindowPicker()
        {
            BackColor = Theme.Card;
            Height = Theme.S(40);
            Combo = Ctl.Combo(300, L.He);
            RefreshBtn = new PillButton(L.T("רענן", "Refresh"), false);
            RefreshBtn.Height = Theme.S(30);
            Controls.Add(Combo); Controls.Add(RefreshBtn);
        }

        protected override void OnLayout(LayoutEventArgs e)
        {
            base.OnLayout(e);
            if (Combo == null || RefreshBtn == null) return;
            int bw = RefreshBtn.Width;
            RefreshBtn.Top = (Height - RefreshBtn.Height) / 2;
            Combo.Top = (Height - Combo.Height) / 2;
            Combo.Width = Math.Max(100, Width - bw - Theme.S(10));
            if (L.He) { Combo.Left = Width - Combo.Width; RefreshBtn.Left = 0; }
            else { Combo.Left = 0; RefreshBtn.Left = Width - bw; }
        }
    }

    // ---------------------------------------------------------------- Schedule
    class ScheduleForm : PageForm
    {
        readonly ScheduleRunner runner;
        List<BrowserProfile> profiles = new List<BrowserProfile>();
        List<string> windowTitles = new List<string>();
        Dropdown cmbMethod, cmbProfile, cmbWhen;
        WindowPicker picker;
        Field fldUrl, fldText;
        Stepper numDelay, numMinWait, numMaxWait, numTabs, numBattery;
        TimeBox dtpTime;
        ToggleSwitch tglSend, tglWake, tglAwake, tglScreenOff, tglAdv;
        Row rowMethod, rowProfile, rowUrl, rowWindow, rowDelay, rowTime, rowSend, rowAdv, rowMin, rowMax, rowTabs;
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
            cmbMethod.Items.Add(L.T("לחלון VS Code שנבחר (רואים את ההודעה)", "To a chosen VS Code window (you can see the message)"));
            rowMethod = AddRow(c1, L.T("שיטת שליחה", "Send method"), "", cmbMethod, true, 0,
                L.T("דרך הדפדפן: פותח Chrome/Edge/Brave ומקליד ב-claude.ai (דורש פרופיל מחובר ומחשב לא נעול).\nדרך Claude Code: שליחה שקטה ברקע, בלי חלון - הכי אמין, אבל לא רואים את ההודעה באף חלון.\nלחלון VS Code: פותח שיחה חדשה עם ההודעה בחלון שתבחר, ושולח.",
                    "Browser: opens Chrome/Edge/Brave and types into claude.ai (needs a signed-in profile and an unlocked computer).\nClaude Code: silent background send, no window - most reliable, but you will not see the message in any window.\nVS Code window: opens a new conversation with the message in the window you choose, and sends it."));

            picker = new WindowPicker();
            picker.RefreshBtn.Click += delegate { LoadWindows(SelectedWindowTitle()); };
            rowWindow = AddRow(c1, L.T("חלון VS Code", "VS Code window"), L.T("לאיזה חלון עורך לשלוח. חלון בלי תיקייה מופיע כ-\"Visual Studio Code\". אם פתחת או סגרת חלונות - לחץ \"רענן\".", "Which editor window to send to. A window with no folder appears as \"Visual Studio Code\". If you opened or closed windows, press \"Refresh\"."), picker, true, 0, null);

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
            MessageBox.Show(this,
                L.T("למה צריך את זה?\nמכסת השימוש של Claude מתאפסת בחלונות של 5 שעות, שמתחילים מההודעה הראשונה. אם ההודעה הראשונה נשלחת מוקדם (למשל ב-6:00 בבוקר), המכסה מתחדשת ב-11:00, באמצע יום העבודה.\n\n" +
                    "איך משתמשים?\n1. בחר שיטת שליחה (מומלץ: \"דרך Claude Code\", או \"לחלון VS Code\" אם רוצים לראות את ההודעה).\n2. אם בחרת חלון VS Code - בחר איזה מהחלונות הפתוחים.\n3. כתוב הודעה קצרה, למשל \"היי\".\n4. בחר מתי: אחרי כמה שניות (לבדיקה) או בשעה קבועה.\n5. לחץ \"התחל\".\n\n" +
                    "חשוב לדעת:\n• המחשב צריך להישאר דלוק (בשיטות עם חלון - גם לא נעול).\n• אפשר לסגור את החלון - התזמון רץ ברקע מהאייקון ליד השעון.\n• יציאה מהתוכנה מבטלת את התזמון.\n• בשעת השליחה בשיטות עם חלון - לא לגעת במקלדת ובעכבר.",
                    "Why do you need this?\nClaude's usage quota resets in 5-hour windows that start from the first message. If the first message is sent early (for example at 6:00 AM), the quota renews at 11:00, in the middle of your workday.\n\n" +
                    "How to use it?\n1. Choose a send method (recommended: \"Through Claude Code\", or \"To a VS Code window\" if you want to see the message).\n2. If you chose a VS Code window - pick which of the open windows.\n3. Write a short message, for example \"hi\".\n4. Choose when: after a few seconds (to test) or at a fixed time.\n5. Press \"Start\".\n\n" +
                    "Good to know:\n• The computer must stay on (for window-based methods - also unlocked).\n• You can close this window - the schedule keeps running in the background from the tray icon.\n• Exiting the app cancels the schedule.\n• While window-based methods run, do not touch the keyboard or mouse."),
                L.T("Claude Notify - עזרה", "Claude Notify - Help"), MessageBoxButtons.OK, MessageBoxIcon.Information, MessageBoxDefaultButton.Button1, MsgOpts);
        }

        string SelectedWindowTitle()
        {
            int i = picker.Combo.SelectedIndex;
            return i >= 0 && i < windowTitles.Count ? windowTitles[i] : "";
        }

        void LoadWindows(string selected)
        {
            windowTitles = new List<string>();
            picker.Combo.Items.Clear();
            int sel = -1;
            foreach (var w in WinApi.Find(Config.Load().ProcessList()))
            {
                string t = WinApi.CleanTitle(w.Title);
                if (windowTitles.Contains(t)) continue;   // כותרות זהות: הרשימה מציגה אחת, והשליחה תלך לעליונה
                if (t == selected) sel = windowTitles.Count;
                windowTitles.Add(t);
                picker.Combo.Items.Add(t == "Visual Studio Code" ? "Visual Studio Code " + L.T("(חלון בלי תיקייה)", "(window with no folder)") : t);
            }
            if (sel < 0 && windowTitles.Count > 0) sel = 0;
            if (sel >= 0) picker.Combo.SelectedIndex = sel;
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
            cardAdv.Shown = browser && tglAdv.Checked;
            rowDelay.Shown = cmbWhen.SelectedIndex != 1;
            rowTime.Shown = cmbWhen.SelectedIndex == 1;
            string hint = vs
                ? L.T("מעלה את החלון שבחרת לחזית, פותח בו שיחה חדשה עם ההודעה ושולח. בשעת ההפעלה לא לגעת במקלדת ובעכבר.", "Brings the chosen window to the front, opens a new conversation with the message and sends it. Do not touch the keyboard or mouse while it runs.")
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
            LoadWindows(s.WindowTitle);
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
            s.WindowTitle = SelectedWindowTitle();
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
                LoadWindows(sc.WindowTitle);
            }
            SetRunning(runner.Running);
        }
    }
}


