using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;
using System.Windows.Forms;

namespace ClaudeNotify
{
    // מספר הבנייה. ב-GitHub Actions הקובץ הזה נדרס עם מספר ההרצה (התגית v1.0.N). בבנייה מקומית הוא 0, ולכן אין בדיקת עדכון.
    static class AppVersion
    {
        public const int Build = 0;
    }

    // בדיקת גרסה חדשה ב-GitHub Releases: בהפעלה ואז כל שעה. מתריעים פעם אחת לכל גרסה.
    static class UpdateCheck
    {
        const string Repo = "YairDaniel11/claude-notify";
        public const string ReleasesPage = "https://github.com/" + Repo + "/releases/latest";

        static System.Threading.Timer timer;
        static int notified;

        public static void Start(Control ui, Action<int> onNewVersion)
        {
            if (AppVersion.Build <= 0) return;
            timer = new System.Threading.Timer(delegate { Check(ui, onNewVersion); }, null, 15000, 3600000);
        }

        static void Check(Control ui, Action<int> onNewVersion)
        {
            try
            {
                if (!Config.Load().CheckUpdates) return;
                try { ServicePointManager.SecurityProtocol |= (SecurityProtocolType)3072; } catch { }
                string json;
                using (var wc = new WebClient())
                {
                    wc.Encoding = Encoding.UTF8;
                    wc.Headers[HttpRequestHeader.UserAgent] = "ClaudeNotify";
                    json = wc.DownloadString("https://api.github.com/repos/" + Repo + "/releases/latest");
                }
                var d = new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(json);
                object tagObj;
                if (!d.TryGetValue("tag_name", out tagObj) || tagObj == null) return;
                int latest = ParseBuild(tagObj.ToString());
                if (latest <= AppVersion.Build || latest == notified) return;
                notified = latest;
                Log.Write("update: new version v1.0." + latest + " (current " + AppVersion.Build + ")");
                ui.BeginInvoke(new Action(delegate { onNewVersion(latest); }));
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
    }
}
