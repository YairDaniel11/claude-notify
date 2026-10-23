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
            var m = new ContextMenuStrip();
            m.ShowImageMargin = false;
            m.Font = Font;
            m.RightToLeft = RightToLeft;
            m.BackColor = Color.White;
            m.MinimumSize = new Size(Width, 0);
            for (int i = 0; i < items.Count; i++)
            {
                int idx = i;
                var it = new ToolStripMenuItem(items[i]);
                it.Padding = Theme.P(8, 6, 8, 6);
                if (i == sel) { it.Font = Theme.Semi(10f); it.ForeColor = Theme.AccentDark; }
                it.Click += delegate { SelectedIndex = idx; };
                m.Items.Add(it);
            }
            open = true;
            Invalidate();
            m.Closed += delegate { open = false; Invalidate(); };
            m.Show(this, new Point(0, Height + 2));
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

    // שעה (שעות:דקות:שניות) משלושה שדות מספר
    class TimeBox : Panel
    {
        readonly Stepper h = new Stepper(), m = new Stepper(), s = new Stepper();
        readonly Label c1 = new Label(), c2 = new Label();

        public TimeBox()
        {
            BackColor = Theme.Card;
            Height = Theme.S(34);
            h.Maximum = 23; m.Maximum = 59; s.Maximum = 59;
            h.Format = m.Format = s.Format = "00";
            foreach (var c in new[] { c1, c2 })
            {
                c.Text = ":"; c.AutoSize = false; c.TextAlign = ContentAlignment.MiddleCenter; c.Font = Theme.Semi(12f); c.ForeColor = Theme.Muted; c.BackColor = Theme.Card;
            }
            Controls.AddRange(new Control[] { h, c1, m, c2, s });
        }

        public TimeSpan Time
        {
            get { return new TimeSpan((int)h.Value, (int)m.Value, (int)s.Value); }
            set { h.Value = value.Hours; m.Value = value.Minutes; s.Value = value.Seconds; }
        }

        protected override void OnLayout(LayoutEventArgs e)
        {
            base.OnLayout(e);
            int colon = Theme.S(10);
            int w = Math.Max(Theme.S(60), (Width - colon * 2) / 3);
            // תמיד שעות משמאל לדקות ולשניות, כמו שעון דיגיטלי
            h.SetBounds(0, 0, w, Height);
            c1.SetBounds(w, 0, colon, Height);
            m.SetBounds(w + colon, 0, w, Height);
            c2.SetBounds(w * 2 + colon, 0, colon, Height);
            s.SetBounds(w * 2 + colon * 2, 0, w, Height);
        }
    }
}
