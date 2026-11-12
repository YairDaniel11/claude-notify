using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

// חלון קטן שמראה שהתוכנה מאזינה ומה היא שומעת ממך. חלון רגיל של התוכנה (אייקון חד, עיצוב אחיד),
// והדף של הדפדפן שמבצע את הזיהוי נשאר מוסתר

namespace ClaudeNotify
{
    class ListenForm : Form
    {
        string state = "ready", status = "", heard = "";
        VoiceListener attached;
        readonly Bitmap logo;
        readonly Timer pulse = new Timer { Interval = 60 };
        int phase;

        public ListenForm()
        {
            Text = "Claude Notify - " + L.T("מאזין", "Listening");
            Icon = Branding.AppIcon;
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            ShowInTaskbar = true;
            TopMost = true;
            StartPosition = FormStartPosition.Manual;
            BackColor = Theme.Bg;
            DoubleBuffered = true;
            ClientSize = new Size(Theme.S(400), Theme.S(170));
            PlaceWindow();
            logo = Branding.MakeBitmap(Theme.S(48));
            status = L.T("מתחיל...", "Starting...");
            pulse.Tick += delegate { phase = (phase + 1) % 20; if (state == "listen") Invalidate(); };
            pulse.Start();
            FormClosing += delegate(object s, FormClosingEventArgs e)
            {
                if (e.CloseReason == CloseReason.UserClosing) { e.Cancel = true; Hide(); }
            };
        }

        // המיקום לפי ההגדרה
        void PlaceWindow()
        {
            var wa = Screen.PrimaryScreen.WorkingArea;
            int m = Theme.S(16);
            string p = Config.Load().VoicePos;
            int x = (p == "bl" || p == "tl") ? wa.Left + m : wa.Right - Width - m;
            int y = (p == "tr" || p == "tl") ? wa.Top + m : wa.Bottom - Height - m;
            if (p == "center") { x = wa.Left + (wa.Width - Width) / 2; y = wa.Top + (wa.Height - Height) / 2; }
            Location = new Point(x, y);
        }

        protected override bool ShowWithoutActivation { get { return true; } }

        // מחברים את החלון למאזין: עדכוני סטטוס וטקסט מגיעים משרשור אחר
        public void Attach(VoiceListener l)
        {
            Detach();
            attached = l;
            PlaceWindow();
            heard = "";
            state = "ready"; status = L.T("מתחיל...", "Starting...");
            l.StatusChanged += OnStatus;
            l.HeardChanged += OnHeard;
            Invalidate();
        }

        public void Detach()
        {
            if (attached != null) { attached.StatusChanged -= OnStatus; attached.HeardChanged -= OnHeard; }
            attached = null;
        }

        void OnStatus(string st, string text)
        {
            if (IsDisposed || !IsHandleCreated) return;
            BeginInvoke(new Action(delegate
            {
                state = st; status = text;
                if (st != "listen") heard = "";
                Invalidate();
            }));
        }

        void OnHeard(string text)
        {
            if (IsDisposed || !IsHandleCreated) return;
            BeginInvoke(new Action(delegate
            {
                text = (text ?? "").Trim();
                heard = text.Length > 80 ? "…" + text.Substring(text.Length - 80) : text;
                Invalidate();
            }));
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
            bool rtl = L.He;
            int W = ClientSize.Width, H = ClientSize.Height, m = Theme.S(12);

            // כרטיס לבן
            var card = new RectangleF(m, m, W - 2 * m, H - 2 * m);
            using (var path = Theme.RoundRect(card, Theme.S(12)))
            using (var b = new SolidBrush(Theme.Card))
            using (var pen = new Pen(Theme.Border))
            {
                g.FillPath(b, path);
                g.DrawPath(pen, path);
            }

            int pad = Theme.S(16), logoS = logo.Width, gap = Theme.S(14);
            int cx = m + pad, cw = W - 2 * (m + pad);
            int logoX = rtl ? cx + cw - logoS : cx;
            int textX = rtl ? cx : cx + logoS + gap;
            int textW = cw - logoS - gap;
            g.DrawImage(logo, logoX, m + pad);

            var fmt = new StringFormat(StringFormatFlags.NoWrap) { Trimming = StringTrimming.EllipsisCharacter, Alignment = StringAlignment.Near };
            var fmtWrap = new StringFormat { Trimming = StringTrimming.EllipsisWord, Alignment = StringAlignment.Near };
            if (rtl) { fmt.FormatFlags |= StringFormatFlags.DirectionRightToLeft; fmtWrap.FormatFlags |= StringFormatFlags.DirectionRightToLeft; }

            // נקודת סטטוס + כותרת
            Color dot = state == "listen" ? Theme.Ok : (state == "err" ? Theme.Danger : Theme.Accent);
            int ds = Theme.S(10), ty = m + pad;
            int dotX = rtl ? textX + textW - ds : textX;
            if (state == "listen")
            {
                int r = (int)(Theme.S(2) + phase * 0.35 * Theme.S(1));
                int a = Math.Max(0, 120 - phase * 6);
                using (var pb = new SolidBrush(Color.FromArgb(a, dot)))
                    g.FillEllipse(pb, dotX - r, ty + Theme.S(7) - r, ds + 2 * r, ds + 2 * r);
            }
            using (var db = new SolidBrush(dot)) g.FillEllipse(db, dotX, ty + Theme.S(7), ds, ds);
            int tx = rtl ? textX : textX + ds + Theme.S(8), tw = textW - ds - Theme.S(8);
            using (var f = Theme.Semi(12.5f))
            using (var tb = new SolidBrush(Theme.Text))
                g.DrawString(status, f, tb, new RectangleF(tx, ty, tw, Theme.S(28)), fmt);

            using (var f = Theme.F(9f))
            using (var mb = new SolidBrush(Theme.Muted))
                g.DrawString("Claude Notify", f, mb, new RectangleF(textX, ty + Theme.S(28), textW, Theme.S(18)), fmt);

            // תיבת הטקסט שנשמע, או הסבר
            var box = new RectangleF(textX, ty + Theme.S(54), textW, H - 2 * m - pad - Theme.S(54) - Theme.S(4));
            if (state == "listen")
            {
                bool hasText = heard.Length > 0;
                string t = hasText ? heard : L.T("אומרים משהו, והטקסט יופיע כאן", "Say something and the text will appear here");
                using (var path = Theme.RoundRect(box, Theme.S(8)))
                {
                    if (hasText) using (var bb = new SolidBrush(Theme.AccentSoft)) g.FillPath(bb, path);
                    else using (var pen = new Pen(Theme.Border) { DashStyle = DashStyle.Dash }) g.DrawPath(pen, path);
                }
                var inner = new RectangleF(box.X + Theme.S(8), box.Y + Theme.S(5), box.Width - Theme.S(16), box.Height - Theme.S(8));
                using (var f = Theme.F(hasText ? 10.5f : 9f))
                using (var hb = new SolidBrush(hasText ? Theme.AccentDark : Theme.Muted))
                    g.DrawString(t, f, hb, inner, fmtWrap);
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) { Detach(); pulse.Dispose(); logo.Dispose(); }
            base.Dispose(disposing);
        }
    }
}
