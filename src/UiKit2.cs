using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

// פקדי בחירה בולטים: רשימה נפתחת עם חץ ברור, ושדה מספר עם כפתורי פלוס ומינוס במקום החצים הקטנים

namespace ClaudeNotify
{
    static class Ctl
    {
        public static Dropdown Combo(int width, bool rtl)
        {
            var c = new Dropdown();
            c.Font = Theme.F(10f);
            c.RightToLeft = rtl ? RightToLeft.Yes : RightToLeft.No;
            c.Size = new Size(Theme.S(width), Theme.S(34));
            return c;
        }

        public static Stepper Num(int min, int max)
        {
            var n = new Stepper();
            n.Minimum = min; n.Maximum = max;
            return n;
        }
    }

    // ---------------------------------------------------------------- Dropdown
    class Dropdown : Control
    {
        public class ItemList
        {
            readonly Dropdown d;
            public ItemList(Dropdown owner) { d = owner; }
            public void Add(string s) { d.items.Add(s); d.Invalidate(); }
            public void Clear() { d.items.Clear(); d.sel = -1; d.Invalidate(); }
            public int Count { get { return d.items.Count; } }
        }

        readonly List<string> items = new List<string>();
        int sel = -1;
        bool hover, open;
        public readonly ItemList Items;
        public event EventHandler SelectedIndexChanged;

        public Dropdown()
        {
            Items = new ItemList(this);
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
            TabStop = true;
            Cursor = Cursors.Hand;
        }

        public int SelectedIndex
        {
            get { return sel; }
            set
            {
                if (value < -1 || value >= items.Count) value = -1;
                if (sel == value) return;
                sel = value;
                Invalidate();
                if (SelectedIndexChanged != null) SelectedIndexChanged(this, EventArgs.Empty);
            }
        }

        void ShowList()
        {
            if (items.Count == 0 || !Enabled) return;
            bool rtl = RightToLeft == RightToLeft.Yes;
            var pop = new DropPopup(items, sel, Font, rtl, Width, delegate(int idx) { SelectedIndex = idx; });
            Point below = PointToScreen(new Point(0, Height + 2));
            Rectangle wa = Screen.FromControl(this).WorkingArea;
            int x = rtl ? PointToScreen(new Point(Width, 0)).X - pop.Width : below.X;
            int y = below.Y + pop.Height > wa.Bottom ? PointToScreen(Point.Empty).Y - pop.Height - 2 : below.Y;
            x = Math.Max(wa.Left, Math.Min(x, wa.Right - pop.Width));
            pop.Location = new Point(x, y);
            open = true;
            Invalidate();
            pop.FormClosed += delegate { open = false; Invalidate(); };
            pop.Show(FindForm());
        }

        protected override void OnClick(EventArgs e) { base.OnClick(e); Focus(); ShowList(); }
        protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); hover = true; Invalidate(); }
        protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); hover = false; Invalidate(); }
        protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
        protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }
        protected override void OnEnabledChanged(EventArgs e) { base.OnEnabledChanged(e); Invalidate(); }
        protected override bool IsInputKey(Keys k) { return k == Keys.Up || k == Keys.Down || base.IsInputKey(k); }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (e.KeyCode == Keys.Down && sel < items.Count - 1) SelectedIndex = sel + 1;
            else if (e.KeyCode == Keys.Up && sel > 0) SelectedIndex = sel - 1;
            else if (e.KeyCode == Keys.Space || e.KeyCode == Keys.Enter) ShowList();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(Parent != null ? Parent.BackColor : Theme.Card);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            bool rtl = RightToLeft == RightToLeft.Yes;
            bool active = Enabled && (hover || open || Focused);
            var r = new RectangleF(0.75f, 0.75f, Width - 2f, Height - 2f);
            using (var path = Theme.RoundRect(r, Theme.S(8)))
            using (var b = new SolidBrush(Enabled ? Color.White : Color.FromArgb(244, 241, 234)))
            using (var pen = new Pen(active ? Theme.Accent : Color.FromArgb(205, 197, 180), active ? 1.8f : 1.3f))
            {
                g.FillPath(b, path);
                g.DrawPath(pen, path);
            }

            // כפתור החץ: ריבוע כתום בהיר עם חץ כתום בולט
            int bs = Height - Theme.S(10);
            int bx = rtl ? Theme.S(5) : Width - bs - Theme.S(5);
            var box = new RectangleF(bx, Theme.S(5), bs, bs);
            using (var path = Theme.RoundRect(box, Theme.S(6)))
            using (var b = new SolidBrush(Enabled ? (active ? Color.FromArgb(249, 224, 212) : Theme.AccentSoft) : Color.FromArgb(240, 236, 228)))
                g.FillPath(b, path);
            float cx = box.X + box.Width / 2f, cy = box.Y + box.Height / 2f, aw = Theme.S(5), ah = Theme.S(3);
            using (var pen = new Pen(Enabled ? Theme.AccentDark : Color.FromArgb(190, 184, 170), 2.2f) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round })
                g.DrawLines(pen, new[] { new PointF(cx - aw, cy - ah), new PointF(cx, cy + ah), new PointF(cx + aw, cy - ah) });

            string text = sel >= 0 && sel < items.Count ? items[sel] : "";
            int pad = Theme.S(10);
            var tr = rtl ? new Rectangle(bs + Theme.S(12), 0, Width - bs - Theme.S(12) - pad, Height) : new Rectangle(pad, 0, Width - bs - Theme.S(12) - pad, Height);
            TextRenderer.DrawText(g, text, Font, tr, Enabled ? Theme.Text : Theme.Muted,
                TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding | (rtl ? TextFormatFlags.Right | TextFormatFlags.RightToLeft : TextFormatFlags.Left));
        }
    }

    // ---------------------------------------------------------------- Stepper
    class Stepper : Control
    {
        readonly TextBox box = new TextBox();
        readonly System.Windows.Forms.Timer repeat = new System.Windows.Forms.Timer();
        decimal val;
        int dir, hot;   // hot: -1 = מינוס, 1 = פלוס
        public decimal Minimum = 0, Maximum = 100;
        public string Format = "0";
        public event EventHandler ValueChanged;

        public Stepper()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            Size = new Size(Theme.S(120), Theme.S(34));
            box.BorderStyle = BorderStyle.None;
            box.TextAlign = HorizontalAlignment.Center;
            box.Font = Theme.F(10.5f);
            box.ForeColor = Theme.Text;
            box.BackColor = Color.White;
            box.Text = val.ToString(Format);
            Controls.Add(box);
            box.Leave += delegate { Commit(); };
            box.KeyDown += delegate(object s, KeyEventArgs e)
            {
                if (e.KeyCode == Keys.Enter) { Commit(); e.SuppressKeyPress = true; }
                else if (e.KeyCode == Keys.Up) { Step(1); e.SuppressKeyPress = true; }
                else if (e.KeyCode == Keys.Down) { Step(-1); e.SuppressKeyPress = true; }
            };
            box.MouseWheel += delegate(object s, MouseEventArgs e) { Step(e.Delta > 0 ? 1 : -1); };
            repeat.Tick += delegate { repeat.Interval = 70; Step(dir); };
        }

        public decimal Value
        {
            get { Commit(); return val; }
            set { SetVal(value); }
        }

        void SetVal(decimal v)
        {
            if (v < Minimum) v = Minimum;
            if (v > Maximum) v = Maximum;
            box.Text = v.ToString(Format);
            if (v == val) return;
            val = v;
            if (ValueChanged != null) ValueChanged(this, EventArgs.Empty);
        }

        void Commit()
        {
            decimal v;
            if (decimal.TryParse(box.Text.Trim(), out v)) SetVal(v); else SetVal(val);
        }

        void Step(int d) { Commit(); SetVal(val + d); }

        int ButtonW { get { return Height; } }

        protected override void OnLayout(LayoutEventArgs e)
        {
            base.OnLayout(e);
            int h = box.PreferredHeight;
            box.SetBounds(ButtonW, (Height - h) / 2, Math.Max(10, Width - ButtonW * 2), h);
        }

        int Zone(int x) { return x < ButtonW ? -1 : x > Width - ButtonW ? 1 : 0; }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (!Enabled) return;
            dir = Zone(e.X);
            if (dir == 0) return;
            Step(dir);
            repeat.Interval = 400;
            repeat.Start();
        }
        protected override void OnMouseUp(MouseEventArgs e) { base.OnMouseUp(e); repeat.Stop(); }
        protected override void OnMouseMove(MouseEventArgs e) { base.OnMouseMove(e); int z = Zone(e.X); if (z != hot) { hot = z; Cursor = z != 0 ? Cursors.Hand : Cursors.Default; Invalidate(); } }
        protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); hot = 0; repeat.Stop(); Invalidate(); }
        protected override void OnEnabledChanged(EventArgs e) { base.OnEnabledChanged(e); box.Enabled = Enabled; Invalidate(); }

        protected override void Dispose(bool disposing)
        {
            if (disposing) repeat.Dispose();
            base.Dispose(disposing);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(Parent != null ? Parent.BackColor : Theme.Card);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var r = new RectangleF(0.75f, 0.75f, Width - 2f, Height - 2f);
            using (var path = Theme.RoundRect(r, Theme.S(8)))
            using (var b = new SolidBrush(Enabled ? Color.White : Color.FromArgb(244, 241, 234)))
            using (var pen = new Pen(Color.FromArgb(205, 197, 180), 1.3f))
            {
                g.FillPath(b, path);
                g.DrawPath(pen, path);
            }
            for (int side = -1; side <= 1; side += 2)
            {
                int bw = ButtonW - Theme.S(8);
                var br = new RectangleF(side < 0 ? Theme.S(4) : Width - bw - Theme.S(4), Theme.S(4), bw, Height - Theme.S(8));
                using (var path = Theme.RoundRect(br, Theme.S(6)))
                using (var b = new SolidBrush(Enabled ? (hot == side ? Color.FromArgb(249, 224, 212) : Theme.AccentSoft) : Color.FromArgb(240, 236, 228)))
                    g.FillPath(b, path);
                float cx = br.X + br.Width / 2f, cy = br.Y + br.Height / 2f, a = Theme.S(5);
                using (var pen = new Pen(Enabled ? Theme.AccentDark : Color.FromArgb(190, 184, 170), 2.2f) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                {
                    g.DrawLine(pen, cx - a, cy, cx + a, cy);
                    if (side > 0) g.DrawLine(pen, cx, cy - a, cx, cy + a);
                }
            }
        }
    }

    // קטע אחד של שעון: חץ למעלה, מספר גדול (אפשר גם להקליד), חץ למטה. נעטף מ-59 ל-0 כמו שעון מעורר
    class Segment : Control
    {
        readonly TextBox box = new TextBox();
        readonly System.Windows.Forms.Timer repeat = new System.Windows.Forms.Timer();
        int val, dir, hot;   // hot: -1 = חץ למעלה, 1 = חץ למטה
        public int Max = 59;
        public event EventHandler ValueChanged;

        public Segment()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            Size = new Size(Theme.S(66), Theme.S(92));
            box.BorderStyle = BorderStyle.None;
            box.TextAlign = HorizontalAlignment.Center;
            box.Font = Theme.Semi(17f);
            box.ForeColor = Theme.Text;
            box.BackColor = Color.White;
            box.MaxLength = 2;
            box.Text = "00";
            Controls.Add(box);
            box.Leave += delegate { Commit(); };
            box.Enter += delegate { box.SelectAll(); };
            box.MouseUp += delegate { if (box.SelectionLength == 0) box.SelectAll(); };
            box.KeyPress += delegate(object s, KeyPressEventArgs e) { if (!char.IsControl(e.KeyChar) && !char.IsDigit(e.KeyChar)) e.Handled = true; };
            box.KeyDown += delegate(object s, KeyEventArgs e)
            {
                if (e.KeyCode == Keys.Enter) { Commit(); e.SuppressKeyPress = true; }
                else if (e.KeyCode == Keys.Up) { Step(1); box.SelectAll(); e.SuppressKeyPress = true; }
                else if (e.KeyCode == Keys.Down) { Step(-1); box.SelectAll(); e.SuppressKeyPress = true; }
            };
            box.MouseWheel += delegate(object s, MouseEventArgs e) { Step(e.Delta > 0 ? 1 : -1); };
            repeat.Tick += delegate { repeat.Interval = 90; Step(dir); };
        }

        public int Value
        {
            get { Commit(); return val; }
            set { SetVal(value); }
        }

        void SetVal(int v)
        {
            int range = Max + 1;
            v = ((v % range) + range) % range;
            box.Text = v.ToString("00");
            if (v == val) return;
            val = v;
            if (ValueChanged != null) ValueChanged(this, EventArgs.Empty);
        }

        void Commit()
        {
            int v;
            if (int.TryParse(box.Text.Trim(), out v)) SetVal(Math.Min(v, Max)); else SetVal(val);
        }

        void Step(int d) { Commit(); SetVal(val + d); }

        int BtnH { get { return Theme.S(26); } }

        protected override void OnLayout(LayoutEventArgs e)
        {
            base.OnLayout(e);
            int h = box.PreferredHeight;
            box.SetBounds(Theme.S(6), BtnH + Theme.S(4) + (Height - BtnH * 2 - Theme.S(8) - h) / 2, Width - Theme.S(12), h);
        }

        int Zone(int y) { return y < BtnH ? -1 : y > Height - BtnH ? 1 : 0; }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (!Enabled) return;
            dir = -Zone(e.Y);   // חץ למעלה מגדיל
            if (dir == 0) return;
            Step(dir);
            repeat.Interval = 400;
            repeat.Start();
        }
        protected override void OnMouseUp(MouseEventArgs e) { base.OnMouseUp(e); repeat.Stop(); }
        protected override void OnMouseMove(MouseEventArgs e) { base.OnMouseMove(e); int z = Zone(e.Y); if (z != hot) { hot = z; Cursor = z != 0 ? Cursors.Hand : Cursors.Default; Invalidate(); } }
        protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); hot = 0; repeat.Stop(); Invalidate(); }
        protected override void Dispose(bool disposing) { if (disposing) repeat.Dispose(); base.Dispose(disposing); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(Parent != null ? Parent.BackColor : Theme.Card);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var mid = new RectangleF(0.75f, BtnH + Theme.S(2), Width - 2f, Height - BtnH * 2 - Theme.S(4));
            using (var path = Theme.RoundRect(mid, Theme.S(8)))
            using (var b = new SolidBrush(Color.White))
            using (var pen = new Pen(Color.FromArgb(205, 197, 180), 1.3f))
            {
                g.FillPath(b, path);
                g.DrawPath(pen, path);
            }
            for (int side = -1; side <= 1; side += 2)
            {
                var br = new RectangleF(Theme.S(2), side < 0 ? 0 : Height - BtnH, Width - Theme.S(4), BtnH);
                using (var path = Theme.RoundRect(br, Theme.S(7)))
                using (var b = new SolidBrush(hot == side ? Color.FromArgb(249, 224, 212) : Theme.AccentSoft))
                    g.FillPath(b, path);
                float cx = br.X + br.Width / 2f, cy = br.Y + br.Height / 2f, aw = Theme.S(6), ah = Theme.S(3);
                float k = side < 0 ? 1 : -1;   // למעלה: ^  למטה: v
                using (var pen = new Pen(Theme.AccentDark, 2.4f) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round })
                    g.DrawLines(pen, new[] { new PointF(cx - aw, cy + k * ah), new PointF(cx, cy - k * ah), new PointF(cx + aw, cy + k * ah) });
            }
        }
    }

    // שעה (שעות : דקות : שניות) כשלושה קטעי שעון עם חצים בולטים
    class TimeBox : Panel
    {
        readonly Segment h = new Segment(), m = new Segment(), s = new Segment();
        readonly Label c1 = new Label(), c2 = new Label();

        public TimeBox()
        {
            BackColor = Theme.Card;
            h.Max = 23;
            Height = h.Height;
            foreach (var c in new[] { c1, c2 })
            {
                c.Text = ":"; c.AutoSize = false; c.TextAlign = ContentAlignment.MiddleCenter; c.Font = Theme.Semi(16f); c.ForeColor = Theme.Muted; c.BackColor = Theme.Card;
            }
            Controls.AddRange(new Control[] { h, c1, m, c2, s });
        }

        public TimeSpan Time
        {
            get { return new TimeSpan(h.Value, m.Value, s.Value); }
            set { h.Value = value.Hours; m.Value = value.Minutes; s.Value = value.Seconds; }
        }

        protected override void OnLayout(LayoutEventArgs e)
        {
            base.OnLayout(e);
            int colon = Theme.S(14), w = h.Width;
            // תמיד שעות מצד שמאל ושניות מצד ימין, כמו שעון דיגיטלי
            int total = w * 3 + colon * 2;
            int x0 = Math.Max(0, (Width - total) / 2);
            h.Location = new Point(x0, 0);
            c1.SetBounds(x0 + w, 0, colon, Height);
            m.Location = new Point(x0 + w + colon, 0);
            c2.SetBounds(x0 + w * 2 + colon, 0, colon, Height);
            s.Location = new Point(x0 + w * 2 + colon * 2, 0);
        }
    }

    // רשימת הבחירה הנפתחת: חלון קטן בעיצוב התוכנה, שמוצמד לפקד ונסגר בלחיצה בחוץ
    class DropPopup : Form
    {
        readonly ListBox list = new ListBox();
        readonly int selected;
        readonly bool rtl;
        int hover = -1;

        public DropPopup(List<string> items, int sel, Font font, bool rtl, int minWidth, Action<int> onPick)
        {
            this.rtl = rtl; selected = sel;
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            TopMost = true;
            BackColor = Theme.Accent;
            Padding = new Padding(1);
            Font = font;

            int itemH = Theme.S(36), w = minWidth;
            foreach (var t in items) w = Math.Max(w, TextRenderer.MeasureText(t, font).Width + Theme.S(44));
            list.Dock = DockStyle.Fill;
            list.BorderStyle = BorderStyle.None;
            list.BackColor = Color.White;
            list.DrawMode = DrawMode.OwnerDrawFixed;
            list.ItemHeight = itemH;
            list.IntegralHeight = false;
            foreach (var t in items) list.Items.Add(t);
            Controls.Add(list);
            Size = new Size(w + 2, Math.Min(items.Count, 8) * itemH + 2);

            list.DrawItem += OnDrawItem;
            list.MouseMove += delegate(object s, MouseEventArgs e)
            {
                int i = list.IndexFromPoint(e.Location);
                if (i != hover) { hover = i; list.Invalidate(); }
            };
            list.MouseLeave += delegate { hover = -1; list.Invalidate(); };
            list.MouseClick += delegate(object s, MouseEventArgs e)
            {
                int i = list.IndexFromPoint(e.Location);
                if (i >= 0) { Close(); onPick(i); }
            };
            list.KeyDown += delegate(object s, KeyEventArgs e)
            {
                if (e.KeyCode == Keys.Escape) Close();
                else if (e.KeyCode == Keys.Enter && list.SelectedIndex >= 0) { int i = list.SelectedIndex; Close(); onPick(i); }
            };
            Deactivate += delegate { if (!IsDisposed) Close(); };
            Shown += delegate { if (selected >= 0) list.SelectedIndex = selected; list.Focus(); };
        }

        void OnDrawItem(object sender, DrawItemEventArgs e)
        {
            if (e.Index < 0) return;
            var g = e.Graphics;
            bool isSel = e.Index == selected;
            bool isHot = e.Index == hover || (hover < 0 && (e.State & DrawItemState.Selected) != 0 && !isSel);
            using (var b = new SolidBrush(isHot ? Theme.AccentSoft : Color.White))
                g.FillRectangle(b, e.Bounds);
            var r = new Rectangle(e.Bounds.X + Theme.S(12), e.Bounds.Y, e.Bounds.Width - Theme.S(24), e.Bounds.Height);
            using (var f = isSel ? Theme.Semi(Font.Size) : Font)
                TextRenderer.DrawText(g, list.Items[e.Index].ToString(), f, r, isSel ? Theme.AccentDark : Theme.Text,
                    TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding | (rtl ? TextFormatFlags.Right | TextFormatFlags.RightToLeft : TextFormatFlags.Left));
        }

        protected override CreateParams CreateParams
        {
            get { var cp = base.CreateParams; cp.ClassStyle |= 0x00020000; return cp; }   // CS_DROPSHADOW
        }
    }

    // תפריט קליק ימני בעיצוב התוכנה
    class MenuColors : ProfessionalColorTable
    {
        public override Color MenuItemSelected { get { return Theme.AccentSoft; } }
        public override Color MenuItemSelectedGradientBegin { get { return Theme.AccentSoft; } }
        public override Color MenuItemSelectedGradientEnd { get { return Theme.AccentSoft; } }
        public override Color MenuItemBorder { get { return Theme.AccentSoft; } }
        public override Color MenuBorder { get { return Theme.Border; } }
        public override Color ToolStripDropDownBackground { get { return Color.White; } }
        public override Color ImageMarginGradientBegin { get { return Color.White; } }
        public override Color ImageMarginGradientMiddle { get { return Color.White; } }
        public override Color ImageMarginGradientEnd { get { return Color.White; } }
        public override Color SeparatorDark { get { return Theme.Border; } }
        public override Color SeparatorLight { get { return Color.White; } }
        public override Color CheckBackground { get { return Theme.AccentSoft; } }
        public override Color CheckSelectedBackground { get { return Theme.AccentSoft; } }
        public override Color CheckPressedBackground { get { return Theme.AccentSoft; } }
    }

    static class MenuStyle
    {
        public static void Apply(ContextMenuStrip m)
        {
            m.Renderer = new ToolStripProfessionalRenderer(new MenuColors());
            m.BackColor = Color.White;
            m.ForeColor = Theme.Text;
            m.Font = Theme.F(10.5f);
            m.Padding = Theme.P(4, 6, 4, 6);
            foreach (ToolStripItem it in m.Items)
                if (it is ToolStripMenuItem) it.Padding = Theme.P(6, 7, 14, 7);
        }
    }

    // חלון הודעה בעיצוב התוכנה (במקום MessageBox של Windows)
    static class Dialog
    {
        public static void Show(Form owner, string title, string body)
        {
            bool rtl = L.He;
            using (var f = new Form())
            {
                f.Text = title;
                f.FormBorderStyle = FormBorderStyle.FixedDialog;
                f.MaximizeBox = false; f.MinimizeBox = false; f.ShowInTaskbar = false;
                f.StartPosition = owner != null ? FormStartPosition.CenterParent : FormStartPosition.CenterScreen;
                f.BackColor = Theme.Bg;
                f.Font = Theme.F(10f);
                f.Icon = Branding.AppIcon;

                int pad = Theme.S(24), w = Theme.S(500);
                var lbl = new Label { AutoSize = false, Text = body, Font = Theme.F(10f), ForeColor = Theme.Text, BackColor = Theme.Bg, RightToLeft = rtl ? RightToLeft.Yes : RightToLeft.No, TextAlign = ContentAlignment.TopLeft };
                int bodyH = Theme.TextHeight(body, lbl.Font, w - pad * 2, rtl) + Theme.S(4);
                lbl.SetBounds(pad, pad, w - pad * 2, bodyH);
                var ok = new PillButton(L.T("אישור", "OK"), true);
                ok.Top = pad + bodyH + Theme.S(18);
                ok.Left = rtl ? w - pad - ok.Width : pad;
                ok.Click += delegate { f.Close(); };
                f.ClientSize = new Size(w, ok.Bottom + pad);
                f.Controls.Add(lbl); f.Controls.Add(ok);
                f.AcceptButton = ok; f.CancelButton = ok;
                f.Paint += delegate(object s, PaintEventArgs e) { using (var b = new SolidBrush(Theme.Accent)) e.Graphics.FillRectangle(b, 0, 0, f.ClientSize.Width, Theme.S(4)); };
                if (owner != null) f.ShowDialog(owner); else f.ShowDialog();
            }
        }
    }
}
