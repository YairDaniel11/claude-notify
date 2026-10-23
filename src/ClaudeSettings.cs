using System;
using System.Collections.Generic;

// קריאה וכתיבה של הגדרות Claude Code (~\.claude\settings.json): מודל, רמת מאמץ, חשיבה ומצב מהיר.
// שאר ההגדרות בקובץ (hooks וכו') נשמרות כמו שהן, ולפני כל כתיבה נוצר גיבוי settings.json.bak

namespace ClaudeNotify
{
    static class ClaudeSettings
    {
        public static void Read(out string model, out string effort, out bool thinking, out bool fast)
        {
            model = ""; effort = ""; thinking = true; fast = false;
            try
            {
                var root = HookInstaller.ReadSettings();
                object v;
                if (root.TryGetValue("model", out v) && v != null) model = v.ToString();
                if (root.TryGetValue("effortLevel", out v) && v != null) effort = v.ToString();
                if (root.TryGetValue("alwaysThinkingEnabled", out v) && v is bool) thinking = (bool)v;
                if (root.TryGetValue("fastMode", out v) && v is bool) fast = (bool)v;
            }
            catch (Exception ex) { Log.Write("claude settings read: " + ex.Message); }
        }

        // ערך ריק = מסירים את המפתח, ו-Claude Code חוזר לברירת המחדל
        public static void Write(string model, string effort, bool thinking, bool fast)
        {
            var root = HookInstaller.ReadSettings();
            Put(root, "model", string.IsNullOrEmpty(model) ? null : model);
            Put(root, "effortLevel", string.IsNullOrEmpty(effort) ? null : effort);
            if (thinking) root.Remove("alwaysThinkingEnabled"); else root["alwaysThinkingEnabled"] = false;
            if (fast) root["fastMode"] = true; else root.Remove("fastMode");
            HookInstaller.WriteSettings(root);
        }

        static void Put(Dictionary<string, object> root, string key, string value)
        {
            if (value == null) root.Remove(key); else root[key] = value;
        }
    }
}
