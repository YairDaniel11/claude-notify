using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

// רכיבי הממשק המשותפים: שפה (עברית/אנגלית), צבעים בסגנון Claude, וכרטיסים/מתגים/כפתורים מעוצבים.
// הכול נכתב ב-C# 5, כי build.cmd משתמש ב-csc של .NET Framework 4.x.

namespace ClaudeNotify
{
    // ---------------------------------------------------------------- Language
    static class L
    {
        public static bool He = true;

        public static void Load(string lang) { He = lang != "en"; }

        public static string T(string he, string en) { return He ? he : en; }

        // תרגום הודעות סטטוס ושגיאה שנוצרות בקוד בעברית. מחליף קטעי טקסט ידועים, ולכן עובד גם על הודעות עם חלקים דינמיים
        static readonly string[][] Map = new string[][]
        {
            new[] { "כבר יש תזמון פעיל", "A schedule is already running" },
            new[] { "צריך להקליד טקסט לשליחה", "Type the text to send" },
            new[] { "צריך לבחור חלון VS Code (אם הרשימה ריקה - פתח את החלון ולחץ על הכפתור 'רענן')", "Choose a VS Code window (if the list is empty, open the window and press Refresh)" },
            new[] { "צריך לבחור פרופיל של Chrome, Edge או Brave", "Choose a Chrome, Edge or Brave profile" },
            new[] { "כתובת האתר חייבת להתחיל ב-https://", "The address must start with https://" },
            new[] { "השעה לא תקינה", "Invalid time" },
            new[] { "ההודעה תישלח ", "The message will be sent " },
            new[] { "הדפדפן ייפתח ", "The browser will open " },
            new[] { "היום ב-", "today at " },
            new[] { "מחר ב-", "tomorrow at " },
            new[] { ". נשאר", ". Remaining:" },
            new[] { "בעוד", "in" },
            new[] { "עכשיו.", "now." },
            new[] { "בוטל", "Cancelled" },
            new[] { "המחשב היה במצב שינה ופספס את השעה, ולכן לא נעשה כלום", "The computer was asleep and missed the time, so nothing was done" },
            new[] { "המחשב היה נעול בשעת ההפעלה, ולכן לא נעשה כלום", "The computer was locked at the scheduled time, so nothing was done" },
            new[] { "ממתין לטעינת הדף:", "Waiting for the page to load:" },
            new[] { "שולח הודעה דרך Claude Code...", "Sending the message through Claude Code..." },
            new[] { "ההודעה נשלחה דרך Claude Code!", "The message was sent through Claude Code!" },
            new[] { "השליחה דרך Claude Code נכשלה: ", "Sending through Claude Code failed: " },
            new[] { "בוצע!", "Done!" },
            new[] { "שגיאה לא צפויה: ", "Unexpected error: " },
            new[] { "הדפדפן נפתח, אבל המחשב נעול ולכן לא הוקלד כלום", "The browser opened, but the computer is locked so nothing was typed" },
            new[] { "הטקסט נכתב, אבל חלון אחר קפץ לחזית לפני השליחה, ולכן לא נשלח", "The text was typed, but another window came to the front before sending, so it was not sent" },
            new[] { "לא הצלחתי לבדוק איפה הסמן נמצא. כנראה PowerShell חסום במחשב הזה (למשל על ידי אנטי-וירוס), ולכן לא נשלח כלום", "Could not check where the cursor is. PowerShell is probably blocked on this computer (for example by an antivirus), so nothing was sent" },
            new[] { "הדף לא היה מוכן להקלדה בזמן שהוגדר, ולכן לא נשלח כלום", "The page was not ready for typing in time, so nothing was sent" },
            new[] { "ממתין שהדף יהיה מוכן... נשאר ", "Waiting for the page to be ready... remaining " },
            new[] { "חלון ה-VS Code שנבחר (", "The chosen VS Code window (" },
            new[] { ") כבר לא פתוח, ולכן לא נשלח כלום", ") is no longer open, so nothing was sent" },
            new[] { "לא הצלחתי לזהות איזה עורך זה (Code או Cursor)", "Could not tell which editor this is (Code or Cursor)" },
            new[] { "לא הצלחתי להעלות את חלון ה-VS Code לחזית (Windows חסם), ולכן לא נשלח כלום", "Could not bring the VS Code window to the front (Windows blocked it), so nothing was sent" },
            new[] { "החלון שנבחר איבד פוקוס לפני השליחה, ולכן לא נשלח כלום", "The chosen window lost focus before sending, so nothing was sent" },
            new[] { "ממתין שהשיחה תיפתח בחלון...", "Waiting for the conversation to open in the window..." },
            new[] { "המחשב ננעל לפני השליחה. הטקסט הוכנס לחלון אבל לא נשלח", "The computer locked before sending. The text is in the window but was not sent" },
            new[] { "חלון אחר קפץ לחזית לפני השליחה. הטקסט הוכנס לחלון אבל לא נשלח", "Another window came to the front before sending. The text is in the window but was not sent" },
            new[] { "בודק שההודעה נשלחה...", "Checking that the message was sent..." },
            new[] { "לחצתי Enter, אבל ההודעה נשארה בתיבה ולא נשלחה. בדוק בחלון ה-VS Code", "Pressed Enter, but the message stayed in the box and was not sent. Check the VS Code window" },
            new[] { "לא מצאתי תיבת הקלדה של Claude בחלון שנבחר, ולכן לא נשלח כלום", "Could not find the Claude input box in the chosen window, so nothing was sent" },
            new[] { "חלון אחר קפץ לחזית לפני ההקלדה, ולכן לא נשלח כלום", "Another window came to the front before typing, so nothing was sent" },
            new[] { "מצאתי את תיבת ההקלדה אבל לא הצלחתי להכניס אליה את הטקסט, ולכן לא נשלח כלום", "Found the input box but could not put the text in it, so nothing was sent" },
            new[] { "הסוללה ב-", "Battery at " },
            new[] { "%, ולכן המחשב כבר לא נשמר ער (יחזור אם תחבר לחשמל)", "%, so the computer is no longer kept awake (it resumes when plugged in)" },
            new[] { "Claude Code לא נמצא במחשב", "Claude Code was not found on this computer" },
            new[] { "הזמן הקצוב עבר", "Timed out" },
            new[] { "claude החזיר שגיאה (קוד ", "claude returned an error (code " }
        };

        static string[][] sorted;

        public static string Tr(string s)
        {
            if (He || string.IsNullOrEmpty(s)) return s;
            if (sorted == null)
            {
                var l = new List<string[]>(Map);
                l.Sort(delegate(string[] a, string[] b) { return b[0].Length.CompareTo(a[0].Length); });
                sorted = l.ToArray();
            }
            foreach (var m in sorted) s = s.Replace(m[0], m[1]);
            return s;
        }
    }

    // ---------------------------------------------------------------- Theme
    static class Theme
    {
        public static readonly Color Bg = Color.FromArgb(250, 249, 245);
        public static readonly Color Card = Color.White;
        public static readonly Color Border = Color.FromArgb(235, 229, 217);
        public static readonly Color Accent = Color.FromArgb(217, 119, 87);
        public static readonly Color AccentDark = Color.FromArgb(193, 95, 60);
        public static readonly Color AccentSoft = Color.FromArgb(252, 241, 235);
        public static readonly Color Text = Color.FromArgb(61, 57, 41);
        public static readonly Color Muted = Color.FromArgb(131, 125, 110);
        public static readonly Color Danger = Color.FromArgb(179, 58, 58);
        public static readonly Color Ok = Color.FromArgb(70, 125, 85);

        static float scale;
        public static float Scale
        {
            get
            {
                if (scale == 0) { using (var g = Graphics.FromHwnd(IntPtr.Zero)) scale = g.DpiX / 96f; }
                return scale;
            }
        }
        public static int S(int v) { return (int)Math.Round(v * Scale); }
        public static Padding P(int l, int t, int r, int b) { return new Padding(S(l), S(t), S(r), S(b)); }

        public static Font F(float size) { return new Font("Segoe UI", size); }
        public static Font Semi(float size) { return new Font("Segoe UI Semibold", size); }

        public static GraphicsPath RoundRect(RectangleF r, float radius)
        {
            var p = new GraphicsPath();
            float d = radius * 2;
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }

        public static int TextHeight(string text, Font font, int width, bool rtl)
        {
            if (string.IsNullOrEmpty(text)) return 0;
            var flags = TextFormatFlags.WordBreak | TextFormatFlags.NoPadding | (rtl ? TextFormatFlags.RightToLeft : TextFormatFlags.Left);
            return TextRenderer.MeasureText(text, font, new Size(Math.Max(20, width), 0), flags).Height + 3;
        }
    }

    // ---------------------------------------------------------------- Layout building blocks
    // פריט שאפשר לסדר אנכית בתוך Stack. Shown במקום Visible, כי Visible מחזיר False כל עוד האב לא מוצג
    class Item : Panel
    {
        bool shown = true;
        public Item() { DoubleBuffered = true; }

        public bool Shown
        {
            get { return shown; }
            set
            {
                if (shown == value) return;
                shown = value;
                Visible = value;
                var p = Parent as Stack;
                if (p != null) p.Relayout();
            }
        }

        public virtual void Fit(int width) { Width = width; }
    }

    // מסדר את הפריטים הנראים אחד מתחת לשני
    class Stack : Item
    {
        public int PadX, PadTop, PadBottom, Gap;
        bool busy;

        public void Relayout()
        {
            if (busy) return;
            busy = true;
            try
            {
                int w = Math.Max(50, Width - PadX * 2);
                int y = PadTop;
                Item last = null;
                foreach (Control c in Controls)
                {
                    var it = c as Item;
                    if (it == null || !it.Shown) continue;
                    it.Fit(w);
                    it.Location = new Point(PadX, y);
                    y += it.Height + Gap;
                    last = it;
                }
                foreach (Control c in Controls)
                {
                    var r = c as Row;
                    if (r != null) r.Divider = r != last;
                }
                int h = Math.Max(0, y - (last != null ? Gap : 0)) + PadBottom;
                if (h != Height)
                {
                    Height = h;
                    var p = Parent as Stack;
                    if (p != null) p.Relayout();
                }
            }
            finally { busy = false; }
        }

        public override void Fit(int width)
        {
            Width = width;
            Relayout();
        }
    }

    // כרטיס לבן מעוגל עם כותרת
    class Card : Stack
    {
        public Card(string heading)
        {
            BackColor = Theme.Bg;
            PadX = Theme.S(18); PadTop = Theme.S(12); PadBottom = Theme.S(2); Gap = 0;
            Controls.Add(new Caption(heading));
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            var r = new RectangleF(0.5f, 0.5f, Width - 1, Height - 1);
            using (var path = Theme.RoundRect(r, Theme.S(12)))
            using (var b = new SolidBrush(Theme.Card))
            using (var pen = new Pen(Theme.Border))
            {
                e.Graphics.FillPath(b, path);
                e.Graphics.DrawPath(pen, path);
            }
        }

        protected override void OnResize(EventArgs e) { base.OnResize(e); Invalidate(); }

        public Row Add(Row r) { Controls.Add(r); Relayout(); return r; }
    }

    class Caption : Item
    {
        readonly Label lbl = new Label();
        public Caption(string text)
        {
            BackColor = Theme.Card;
            lbl.AutoSize = false;
            lbl.Text = text;
            lbl.Font = Theme.Semi(11.5f);
            lbl.ForeColor = Theme.Accent;
            lbl.BackColor = Theme.Card;
            Controls.Add(lbl);
        }
        public override void Fit(int width)
        {
            Width = width;
            Height = Theme.S(30);
            lbl.RightToLeft = L.He ? RightToLeft.Yes : RightToLeft.No;
            lbl.TextAlign = ContentAlignment.MiddleLeft;
            lbl.SetBounds(0, 0, width, Theme.S(26));
        }
        protected override void OnPaint(PaintEventArgs e) { base.OnPaint(e); }
    }

    // שורת הגדרה: כותרת + הסבר קצר בצד אחד, והפקד בצד השני (או מתחת, כשהפקד רחב)
    class Row : Item
    {
        public readonly Label Title = new Label();
        public readonly Label Desc = new Label();
        readonly Control ctl;
        readonly bool stacked;
        readonly int ctlW;
        public bool Divider = true;

        public Row(string title, string desc, Control control, bool stack, int controlWidth)
        {
            BackColor = Theme.Card;
            ctl = control; stacked = stack; ctlW = Theme.S(controlWidth);
            Title.AutoSize = false; Title.Font = Theme.Semi(10f); Title.ForeColor = Theme.Text; Title.BackColor = Theme.Card;
            Desc.AutoSize = false; Desc.Font = Theme.F(9f); Desc.ForeColor = Theme.Muted; Desc.BackColor = Theme.Card;
            Title.Text = title; Desc.Text = desc ?? "";
            Controls.Add(Title); Controls.Add(Desc);
            if (ctl != null) Controls.Add(ctl);
        }

        public void SetTexts(string title, string desc)
        {
            Title.Text = title; Desc.Text = desc ?? "";
            var p = Parent as Stack;
            if (p != null) p.Relayout();
        }

        public override void Fit(int width)
        {
            Width = width;
            bool rtl = L.He;
            int pad = Theme.S(10);
            int textW = stacked || ctl == null ? width : width - ctlW - Theme.S(24);
            Title.RightToLeft = Desc.RightToLeft = rtl ? RightToLeft.Yes : RightToLeft.No;
            Title.TextAlign = Desc.TextAlign = ContentAlignment.TopLeft;
            int th = Theme.TextHeight(Title.Text, Title.Font, textW, rtl);
            int dh = Theme.TextHeight(Desc.Text, Desc.Font, textW, rtl);
            int block = th + (dh > 0 ? dh + 1 : 0);

            if (stacked && ctl != null)
            {
                ctl.Width = width;
                Title.SetBounds(0, pad, width, th);
                Desc.SetBounds(0, pad + th + 1, width, dh);
                ctl.Location = new Point(0, pad + block + Theme.S(6));
                Height = pad + block + Theme.S(6) + ctl.Height + pad;
            }
            else
            {
                int content = Math.Max(block, ctl != null ? ctl.Height : 0);
                Height = pad * 2 + content;
                int tx = rtl ? width - textW : 0;
                Title.SetBounds(tx, pad + (content - block) / 2, textW, th);
                Desc.SetBounds(tx, pad + (content - block) / 2 + th + 1, textW, dh);
                if (ctl != null)
                {
                    ctl.Width = ctlW;
                    ctl.Location = new Point(rtl ? 0 : width - ctlW, pad + (content - ctl.Height) / 2);
                }
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            if (Divider)
                using (var p = new Pen(Theme.Border))
                    e.Graphics.DrawLine(p, 0, Height - 1, Width, Height - 1);
        }
    }

    // כותרת העמוד: לוגו, שם, תיאור, וכפתור אופציונלי
    class Header : Item
    {
        readonly PictureBox logo = new PictureBox();
        readonly Label title = new Label(), sub = new Label();
        readonly Control extra;

        public Header(string t, string s, Control extraControl)
        {
            BackColor = Theme.Bg;
            extra = extraControl;
            logo.Image = Branding.MakeBitmap(Theme.S(36));
            logo.SizeMode = PictureBoxSizeMode.Normal;
            logo.Size = new Size(Theme.S(36), Theme.S(36));
            logo.BackColor = Theme.Bg;
            title.AutoSize = false; title.Text = t; title.Font = Theme.Semi(16f); title.ForeColor = Theme.Text; title.BackColor = Theme.Bg;
            sub.AutoSize = false; sub.Text = s; sub.Font = Theme.F(9.5f); sub.ForeColor = Theme.Muted; sub.BackColor = Theme.Bg;
            Controls.Add(logo); Controls.Add(title); Controls.Add(sub);
            if (extra != null) Controls.Add(extra);
        }

        public string Sub
        {
            set
            {
                sub.Text = value;
                var p = Parent as Stack;
                if (p != null) p.Relayout();
            }
        }

        public override void Fit(int width)
        {
            Width = width;
            bool rtl = L.He;
            int logoW = Theme.S(36), gap = Theme.S(14);
            int extraW = extra != null ? extra.Width + gap : 0;
            int textW = width - logoW - gap - extraW;
            title.RightToLeft = sub.RightToLeft = rtl ? RightToLeft.Yes : RightToLeft.No;
            title.TextAlign = sub.TextAlign = ContentAlignment.TopLeft;
            int sh = Theme.TextHeight(sub.Text, sub.Font, textW, rtl);
            int th = Theme.S(30);
            Height = Math.Max(logoW + 4, th + sh) + 4;
            int tx = rtl ? width - logoW - gap - textW : logoW + gap;
            logo.Location = new Point(rtl ? width - logoW : 0, 0);
            title.SetBounds(tx, 0, textW, th);
            sub.SetBounds(tx, th, textW, sh);
            if (extra != null) extra.Location = new Point(rtl ? 0 : width - extra.Width, Theme.S(2));
        }
    }

    // טקסט הערה בתחתית (אפור, בלי מסגרת)
    class Note : Item
    {
        readonly Label lbl = new Label();
        public Note(string text)
        {
            BackColor = Theme.Bg;
            lbl.AutoSize = false; lbl.Text = text; lbl.Font = Theme.F(9f); lbl.ForeColor = Theme.Muted; lbl.BackColor = Theme.Bg;
            Controls.Add(lbl);
        }
        public override void Fit(int width)
        {
            Width = width;
            bool rtl = L.He;
            lbl.RightToLeft = rtl ? RightToLeft.Yes : RightToLeft.No;
            lbl.TextAlign = ContentAlignment.TopLeft;
            int h = Theme.TextHeight(lbl.Text, lbl.Font, width, rtl);
            lbl.SetBounds(0, 0, width, h);
            Height = h + 4;
        }
    }

    // ---------------------------------------------------------------- Controls
    class ToggleSwitch : Control
    {
        bool on;
        public event EventHandler CheckedChanged;

        public ToggleSwitch()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
            Size = new Size(Theme.S(46), Theme.S(26));
            Cursor = Cursors.Hand;
            TabStop = true;
        }

        public bool Checked
        {
            get { return on; }
            set { if (on == value) return; on = value; Invalidate(); if (CheckedChanged != null) CheckedChanged(this, EventArgs.Empty); }
        }

        protected override void OnClick(EventArgs e) { base.OnClick(e); if (Enabled) Checked = !Checked; }
        protected override void OnKeyDown(KeyEventArgs e) { base.OnKeyDown(e); if (e.KeyCode == Keys.Space && Enabled) Checked = !Checked; }
        protected override void OnEnabledChanged(EventArgs e) { base.OnEnabledChanged(e); Invalidate(); }
        protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
        protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(Parent != null ? Parent.BackColor : Theme.Card);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            Color track = on ? Theme.Accent : Color.FromArgb(214, 208, 195);
            if (!Enabled) track = Color.FromArgb(232, 228, 219);
            var r = new RectangleF(1, 2, Width - 2, Height - 4);
            using (var path = Theme.RoundRect(r, r.Height / 2))
            using (var b = new SolidBrush(track))
                g.FillPath(b, path);
            if (Focused)
                using (var path = Theme.RoundRect(new RectangleF(0, 1, Width - 1, Height - 3), (Height - 3) / 2f))
                using (var pen = new Pen(Theme.AccentDark, 1.5f))
                    g.DrawPath(pen, path);
            float k = r.Height - Theme.S(6);
            bool right = on != L.He;   // בעברית "דלוק" הוא שמאלה, כמו בשאר Windows בממשק מימין לשמאל
            float x = right ? r.Right - k - Theme.S(3) : r.Left + Theme.S(3);
            using (var b = new SolidBrush(Color.White))
                g.FillEllipse(b, x, r.Top + Theme.S(3), k, k);
        }
    }

    class PillButton : Button
    {
        public bool Primary;
        bool hover, down;

        public PillButton(string text, bool primary)
        {
            Primary = primary;
            Text = text;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
            FlatStyle = FlatStyle.Flat;
            FlatAppearance.BorderSize = 0;
            Font = Theme.Semi(10f);
            Height = Theme.S(34);
            Cursor = Cursors.Hand;
            int w = TextRenderer.MeasureText(text, Font).Width + Theme.S(40);
            Width = Math.Max(Theme.S(primary ? 100 : 84), w);
            UseVisualStyleBackColor = false;
        }

        protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); hover = true; Invalidate(); }
        protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); hover = false; down = false; Invalidate(); }
        protected override void OnMouseDown(MouseEventArgs e) { base.OnMouseDown(e); down = true; Invalidate(); }
        protected override void OnMouseUp(MouseEventArgs e) { base.OnMouseUp(e); down = false; Invalidate(); }
        protected override void OnEnabledChanged(EventArgs e) { base.OnEnabledChanged(e); Invalidate(); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(Parent != null ? Parent.BackColor : Theme.Bg);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var r = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
            Color fill, border, fore;
            if (!Enabled) { fill = Color.FromArgb(240, 236, 228); border = Color.FromArgb(232, 227, 216); fore = Color.FromArgb(180, 174, 160); }
            else if (Primary) { fill = down ? Theme.AccentDark : hover ? Color.FromArgb(205, 107, 74) : Theme.Accent; border = fill; fore = Color.White; }
            else { fill = hover ? Theme.AccentSoft : Color.White; border = hover ? Theme.Accent : Color.FromArgb(222, 215, 200); fore = Theme.Text; }
            using (var path = Theme.RoundRect(r, Theme.S(10)))
            using (var b = new SolidBrush(fill))
            using (var pen = new Pen(border))
            {
                g.FillPath(b, path);
                g.DrawPath(pen, path);
            }
            TextRenderer.DrawText(g, Text, Font, new Rectangle(0, 0, Width, Height), fore,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | (L.He ? TextFormatFlags.RightToLeft : TextFormatFlags.Left));
            if (Focused && ShowFocusCues)
                using (var path = Theme.RoundRect(new RectangleF(3, 3, Width - 7, Height - 7), 9))
                using (var pen = new Pen(Primary ? Color.FromArgb(255, 220, 205) : Theme.Accent) { DashStyle = DashStyle.Dot })
                    g.DrawPath(pen, path);
        }
    }

    // תיבת טקסט עם מסגרת מעוגלת, שנצבעת בכתום בפוקוס
    class Field : Panel
    {
        public readonly TextBox Box = new TextBox();

        public Field(bool rtl)
        {
            DoubleBuffered = true;
            BackColor = Theme.Bg;
            Height = Theme.S(34);
            Box.BorderStyle = BorderStyle.None;
            Box.Font = Theme.F(10f);
            Box.ForeColor = Theme.Text;
            Box.BackColor = Color.White;
            Box.RightToLeft = rtl ? RightToLeft.Yes : RightToLeft.No;
            Controls.Add(Box);
            Box.Enter += delegate { Invalidate(); };
            Box.Leave += delegate { Invalidate(); };
            Click += delegate { Box.Focus(); };
        }

        protected override void OnLayout(LayoutEventArgs e)
        {
            base.OnLayout(e);
            int h = Box.PreferredHeight;
            Box.SetBounds(Theme.S(10), (Height - h) / 2, Math.Max(10, Width - Theme.S(20)), h);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            var r = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
            using (var path = Theme.RoundRect(r, Theme.S(8)))
            using (var b = new SolidBrush(Color.White))
            using (var pen = new Pen(Box.Focused ? Theme.Accent : Color.FromArgb(222, 215, 200), Box.Focused ? 1.5f : 1f))
            {
                e.Graphics.FillPath(b, path);
                e.Graphics.DrawPath(pen, path);
            }
        }

        protected override void OnResize(EventArgs e) { base.OnResize(e); Invalidate(); }
    }
}
