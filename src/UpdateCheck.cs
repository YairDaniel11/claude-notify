using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;
using System.Windows.Forms;

namespace ClaudeNotify
{
    // מספר הבנייה. ב-GitHub Actions הקובץ הזה נדרס עם מספר ההרצה (התגית v1.0.N). בבנייה מקומית הוא 0, ולכן אין בדיקת עדכון אוטומטית.
    static class AppVersion
    {
        public const int Build = 0;
    }

    // גרסה חדשה שנמצאה ב-GitHub Releases
    class UpdateInfo
    {
        public int Build;
        public string Notes = "";
        public string AssetUrl;     // ClaudeNotify.exe שבתוך ה-Release (null אם אין)
        public long AssetSize;
        public string Sha256;       // null אם GitHub לא סיפק
        public string PageUrl;
        public string VersionText { get { return "1.0." + Build; } }
    }

    // בדיקת גרסה חדשה ב-GitHub Releases: בהפעלה ואז כל שעה. מתריעים פעם אחת לכל גרסה.
    static class UpdateCheck
    {
        const string Repo = "YairDaniel11/claude-notify";
        public const string ReleasesPage = "https://github.com/" + Repo + "/releases/latest";

        static System.Threading.Timer timer;
        static int notified;

        public static void Start(Control ui, Action<UpdateInfo> onNewVersion)
        {
            if (AppVersion.Build <= 0) return;
            timer = new System.Threading.Timer(delegate { Check(ui, onNewVersion); }, null, 15000, 3600000);
        }

        // פרטי ה-Release האחרון ב-GitHub. זורק חריגה אם הבדיקה נכשלה
        // מסיר מהערות הגרסה את הנוסח הקבוע של ה-workflow (גרסה אוטומטית מהקומיט..., הוראות הורדה ידנית). נשאר רק תיאור השינוי
        static string CleanNotes(string body)
        {
            var keep = new List<string>();
            foreach (string line in body.Replace("\r\n", "\n").Split('\n'))
            {
                string t = line.Trim();
                if (t.StartsWith("להורדה:")) break;
                if (t.StartsWith("גרסה אוטומטית מהקומיט")) continue;
                keep.Add(t);
            }
            return string.Join("\r\n", keep.ToArray()).Trim();
        }

        static UpdateInfo FetchLatest()
        {
            try { ServicePointManager.SecurityProtocol |= (SecurityProtocolType)3072; } catch { }
            string json;
            using (var wc = new WebClient())
            {
                wc.Encoding = Encoding.UTF8;
                wc.Headers[HttpRequestHeader.UserAgent] = "ClaudeNotify";
                json = wc.DownloadString("https://api.github.com/repos/" + Repo + "/releases/latest");
            }
            var d = new JavaScriptSerializer { MaxJsonLength = int.MaxValue }.Deserialize<Dictionary<string, object>>(json);
            object tagObj;
            if (!d.TryGetValue("tag_name", out tagObj) || tagObj == null) throw new InvalidOperationException("no tag_name");
            var info = new UpdateInfo();
            info.Build = ParseBuild(tagObj.ToString());
            info.PageUrl = ReleasesPage;
            object o;
            if (d.TryGetValue("body", out o) && o != null) info.Notes = CleanNotes(o.ToString());
            if (d.TryGetValue("assets", out o) && o is IEnumerable)
                foreach (object a in (IEnumerable)o)
                {
                    var ad = a as Dictionary<string, object>;
                    if (ad == null || !ad.ContainsKey("name") || !string.Equals(Convert.ToString(ad["name"]), "ClaudeNotify.exe", StringComparison.OrdinalIgnoreCase)) continue;
                    object u, sz, dg;
                    if (ad.TryGetValue("browser_download_url", out u) && u != null) info.AssetUrl = u.ToString();
                    if (ad.TryGetValue("size", out sz) && sz != null) info.AssetSize = Convert.ToInt64(sz);
                    if (ad.TryGetValue("digest", out dg) && dg != null && dg.ToString().StartsWith("sha256:", StringComparison.OrdinalIgnoreCase))
                        info.Sha256 = dg.ToString().Substring(7);
                    break;
                }
            return info;
        }

        // בדיקה ידנית מכפתור ההגדרות. התוצאה: פרטי הגרסה האחרונה, או null אם הבדיקה נכשלה
        public static void CheckNow(Control ui, Action<UpdateInfo> done)
        {
            var t = new Thread(delegate()
            {
                UpdateInfo r = null;
                try { r = FetchLatest(); }
                catch (Exception ex) { Log.Write("update check failed: " + ex.Message); }
                try { ui.BeginInvoke(new Action(delegate { done(r); })); } catch { }
            });
            t.IsBackground = true;
            t.Start();
        }

        static void Check(Control ui, Action<UpdateInfo> onNewVersion)
        {
            try
            {
                if (!Config.Load().CheckUpdates) return;
                var info = FetchLatest();
                if (info.Build <= AppVersion.Build || info.Build == notified) return;
                notified = info.Build;
                Log.Write("update: new version v1.0." + info.Build + " (current " + AppVersion.Build + ")");
                ui.BeginInvoke(new Action(delegate { onNewVersion(info); }));
            }
            catch (Exception ex) { Log.Write("update check failed: " + ex.Message); }
        }

        // v1.0.57 -> 57
        static int ParseBuild(string tag)
        {
            string[] parts = tag.TrimStart('v', 'V').Split('.');
            int n;
            return parts.Length >= 3 && int.TryParse(parts[parts.Length - 1], out n) ? n : 0;
        }

        public static void OpenPage()
        {
            try { Process.Start(new ProcessStartInfo(ReleasesPage) { UseShellExecute = true }); }
            catch (Exception ex) { Log.Write("update: cannot open page: " + ex.Message); }
        }

        static bool IsTrustedHost(Uri u)
        {
            return u.Scheme == Uri.UriSchemeHttps &&
                (u.Host == "github.com" || u.Host.EndsWith(".github.com") || u.Host.EndsWith(".githubusercontent.com"));
        }

        // מוריד את ה-exe החדש לתיקייה זמנית ומאמת (כתובת מ-GitHub, גודל, SHA-256, ושזה קובץ הרצה). זורק חריגה אם נכשל או בוטל
        public static string Download(UpdateInfo info, Action<double> progress, Func<bool> cancelled)
        {
            if (info.AssetUrl == null) throw new IOException(L.T("אין קובץ התקנה בגרסה זו", "This release has no installer file"));
            if (!IsTrustedHost(new Uri(info.AssetUrl))) throw new IOException(L.T("כתובת ההורדה אינה מ-GitHub", "The download address is not from GitHub"));
            try { ServicePointManager.SecurityProtocol |= (SecurityProtocolType)3072; } catch { }
            string dir = Path.Combine(Path.GetTempPath(), "ClaudeNotifyUpdate");
            Directory.CreateDirectory(dir);
            foreach (string old in Directory.GetFiles(dir)) { try { File.Delete(old); } catch { } }
            string dest = Path.Combine(dir, "ClaudeNotify-" + info.Build + ".exe");
            string part = dest + ".part";

            var req = (HttpWebRequest)WebRequest.Create(info.AssetUrl);
            req.UserAgent = "ClaudeNotify";
            req.Timeout = 30000;
            req.ReadWriteTimeout = 30000;
            using (var resp = (HttpWebResponse)req.GetResponse())
            {
                if (!IsTrustedHost(resp.ResponseUri)) throw new IOException(L.T("ההורדה הופנתה לכתובת לא מוכרת", "The download was redirected to an unknown address"));
                long total = resp.ContentLength > 0 ? resp.ContentLength : info.AssetSize;
                using (var src = resp.GetResponseStream())
                using (var fs = new FileStream(part, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    var buf = new byte[81920];
                    long got = 0;
                    int n;
                    while ((n = src.Read(buf, 0, buf.Length)) > 0)
                    {
                        if (cancelled()) { fs.Close(); File.Delete(part); throw new OperationCanceledException(); }
                        fs.Write(buf, 0, n);
                        got += n;
                        if (total > 0) progress((double)got / total);
                    }
                }
            }
            var fi = new FileInfo(part);
            if (info.AssetSize > 0 && fi.Length != info.AssetSize) { File.Delete(part); throw new IOException(L.T("ההורדה לא הושלמה (גודל לא תואם)", "The download is incomplete (size mismatch)")); }
            if (!string.IsNullOrEmpty(info.Sha256))
            {
                string hash;
                using (var s = File.OpenRead(part)) hash = BitConverter.ToString(SHA256.Create().ComputeHash(s)).Replace("-", "");
                if (!hash.Equals(info.Sha256, StringComparison.OrdinalIgnoreCase)) { File.Delete(part); throw new IOException(L.T("הקובץ פגום (SHA-256 לא תואם)", "The file is corrupt (SHA-256 mismatch)")); }
            }
            using (var s = File.OpenRead(part))
                if (s.ReadByte() != 'M' || s.ReadByte() != 'Z') { File.Delete(part); throw new IOException(L.T("הקובץ שהורד אינו תוכנה", "The downloaded file is not an application")); }
            if (File.Exists(dest)) File.Delete(dest);
            File.Move(part, dest);
            return dest;
        }

        // מפעיל סקריפט ברקע שמחכה שהתוכנה הנוכחית תיסגר, מחליף את קובץ ההרצה (וגם את העותק שה-hook של Claude Code מפנה אליו), ופותח את התוכנה מחדש
        public static void LaunchReplace(string newExe)
        {
            string me = Application.ExecutablePath;
            string installed = HookInstaller.InstalledExe;
            string extra = File.Exists(installed) && !string.Equals(Path.GetFullPath(me), Path.GetFullPath(installed), StringComparison.OrdinalIgnoreCase) ? installed : "";
            string ps1 = Path.Combine(Path.GetDirectoryName(newExe), "apply-update.ps1");
            string script =
                "param($ProcId,$Src,$Dst,$Dst2)\r\n" +
                "try { Wait-Process -Id $ProcId -Timeout 40 -ErrorAction SilentlyContinue } catch {}\r\n" +
                "$ok = $false\r\n" +
                "for ($i = 0; $i -lt 60 -and -not $ok; $i++) { try { Copy-Item -LiteralPath $Src -Destination $Dst -Force -ErrorAction Stop; $ok = $true } catch { Start-Sleep -Milliseconds 500 } }\r\n" +
                "if ($ok -and $Dst2) { for ($i = 0; $i -lt 20; $i++) { try { Copy-Item -LiteralPath $Src -Destination $Dst2 -Force -ErrorAction Stop; break } catch { Start-Sleep -Milliseconds 500 } } }\r\n" +
                "Start-Process -FilePath $Dst\r\n";
            File.WriteAllText(ps1, script, new UTF8Encoding(true));
            var psi = new ProcessStartInfo("powershell.exe",
                "-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File \"" + ps1 + "\" -ProcId " + Process.GetCurrentProcess().Id +
                " -Src \"" + newExe + "\" -Dst \"" + me + "\" -Dst2 \"" + extra + "\"");
            psi.CreateNoWindow = true;
            psi.UseShellExecute = false;
            Process.Start(psi);
            Log.Write("update: replacing exe, new=" + newExe + " target=" + me);
        }
    }

    // חלון העדכון: מציג מה חדש, מוריד את הגרסה, סוגר את התוכנה, מחליף אותה ופותח מחדש
    class UpdateForm : Form
    {
        readonly UpdateInfo info;
        readonly Action exitApp;
        readonly Label status = new Label();
        readonly ProgressBar bar = new ProgressBar();
        readonly PillButton btnInstall, btnLater;
        volatile bool cancel;
        bool busy;

        public UpdateForm(UpdateInfo info, Action exitApp)
        {
            this.info = info; this.exitApp = exitApp;
            bool rtl = L.He;
            RightToLeft rt = rtl ? RightToLeft.Yes : RightToLeft.No;
            Text = "Claude Notify - " + L.T("עדכון", "Update");
            Font = Theme.F(9.5f);
            BackColor = Theme.Bg;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false; MinimizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            Icon = Branding.AppIcon;
            RightToLeft = rt;
            int w = Theme.S(500), pad = Theme.S(22), inner = w - pad * 2;
            ClientSize = new Size(w, Theme.S(400));

            var head = new Label { Text = L.T("יש גרסה חדשה של Claude Notify", "A new version of Claude Notify is available"), Font = Theme.Semi(14f), ForeColor = Theme.Text, AutoSize = false, Location = new Point(pad, Theme.S(18)), Size = new Size(inner, Theme.S(30)), TextAlign = ContentAlignment.MiddleLeft };
            string size = info.AssetSize > 0 ? "   ·   " + (info.AssetSize / 1024) + " KB" : "";
            string cur = AppVersion.Build > 0 ? "1.0." + AppVersion.Build : "-";
            var ver = new Label { Text = L.T("גרסה מותקנת: ", "Installed: ") + cur + "     " + L.T("גרסה חדשה: ", "New: ") + info.VersionText + size, Font = Theme.F(9.5f), ForeColor = Theme.Muted, AutoSize = false, Location = new Point(pad, Theme.S(52)), Size = new Size(inner, Theme.S(24)), TextAlign = ContentAlignment.MiddleLeft };
            var notes = new TextBox { Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, BorderStyle = BorderStyle.FixedSingle, BackColor = Color.White, ForeColor = Theme.Text, Font = Theme.F(9.5f), Location = new Point(pad, Theme.S(84)), Size = new Size(inner, Theme.S(170)), Text = info.Notes.Length > 0 ? info.Notes : L.T("(אין הערות לגרסה)", "(no release notes)") };
            status.AutoSize = false; status.Location = new Point(pad, Theme.S(264)); status.Size = new Size(inner, Theme.S(44)); status.ForeColor = Theme.Muted; status.TextAlign = ContentAlignment.TopLeft;
            status.Text = info.AssetUrl == null
                ? L.T("אין קובץ התקנה בגרסה זו. אפשר להוריד מדף הגרסה.", "This release has no installer file. You can download it from the release page.")
                : L.T("לחיצה על \"הורד והתקן\" תוריד את הגרסה, תסגור את התוכנה, תחליף אותה ותפתח אותה מחדש.", "\"Download and install\" downloads the update, closes the app, replaces it and opens it again.");
            bar.Location = new Point(pad, Theme.S(314)); bar.Size = new Size(inner, Theme.S(10)); bar.Minimum = 0; bar.Maximum = 1000; bar.Visible = false;
            bar.RightToLeft = RightToLeft.No;

            btnInstall = new PillButton(info.AssetUrl == null ? L.T("פתח את דף הגרסה", "Open release page") : L.T("הורד והתקן", "Download and install"), true);
            btnLater = new PillButton(L.T("אחר כך", "Later"), false);
            btnInstall.Size = new Size(Theme.S(170), Theme.S(36));
            btnLater.Size = new Size(Theme.S(110), Theme.S(36));
            int by = Theme.S(344);
            // כפתור הפעולה הראשי בצד שמתחיל את הקריאה (ימין בעברית)
            btnInstall.Location = new Point(rtl ? w - pad - btnInstall.Width : pad, by);
            btnLater.Location = new Point(rtl ? w - pad - btnInstall.Width - Theme.S(10) - btnLater.Width : pad + btnInstall.Width + Theme.S(10), by);
            btnInstall.Click += delegate { Install(); };
            btnLater.Click += delegate { if (busy) cancel = true; else Close(); };
            AcceptButton = btnInstall;
            Controls.AddRange(new Control[] { head, ver, notes, status, bar, btnInstall, btnLater });
            FormClosing += delegate(object s, FormClosingEventArgs e) { if (busy) { cancel = true; e.Cancel = true; } };
        }

        void Install()
        {
            if (busy) return;
            if (info.AssetUrl == null) { UpdateCheck.OpenPage(); return; }
            busy = true; cancel = false;
            btnInstall.Enabled = false;
            btnLater.Text = L.T("ביטול", "Cancel");
            bar.Visible = true; bar.Value = 0;
            status.ForeColor = Theme.Muted;
            status.Text = L.T("מוריד את הגרסה החדשה...", "Downloading the new version...");
            var t = new Thread(delegate()
            {
                try
                {
                    string path = UpdateCheck.Download(info, delegate(double f) { try { BeginInvoke(new Action(delegate { bar.Value = (int)Math.Max(0, Math.Min(1000, f * 1000)); })); } catch { } }, delegate { return cancel; });
                    BeginInvoke(new Action(delegate
                    {
                        status.Text = L.T("מתקין... התוכנה תיסגר ותיפתח מחדש.", "Installing... the app will close and open again.");
                        try { UpdateCheck.LaunchReplace(path); }
                        catch (Exception ex) { Fail(L.T("העדכון נכשל: ", "The update failed: ") + ex.Message); return; }
                        busy = false;
                        Close();
                        exitApp();
                    }));
                }
                catch (OperationCanceledException) { try { BeginInvoke(new Action(delegate { Fail(L.T("ההורדה בוטלה.", "The download was cancelled.")); })); } catch { } }
                catch (Exception ex)
                {
                    Log.Write("update download failed: " + ex.Message);
                    string msg = L.T("העדכון נכשל: ", "The update failed: ") + ex.Message;
                    try { BeginInvoke(new Action(delegate { Fail(msg); })); } catch { }
                }
            });
            t.IsBackground = true;
            t.Start();
        }

        void Fail(string msg)
        {
            busy = false; cancel = false;
            status.ForeColor = Theme.Danger;
            status.Text = msg;
            bar.Visible = false;
            btnInstall.Enabled = true;
            btnInstall.Text = L.T("נסה שוב", "Try again");
            btnLater.Text = L.T("אחר כך", "Later");
        }
    }
}
