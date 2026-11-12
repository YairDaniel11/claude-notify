using System;
using System.Drawing;
using System.Windows.Forms;

// חלון עזרה: הסבר בשפה פשוטה על כל יכולת של התוכנה

namespace ClaudeNotify
{
    class HelpForm : PageForm
    {
        public HelpForm() : base("Claude Notify - " + L.T("עזרה והסברים", "Help"), 60)
        {
            content.Controls.Add(new Header(L.T("עזרה והסברים", "Help and explanations"),
                L.T("מה כל דבר עושה, בשפה פשוטה.", "What everything does, in plain language."), null));

            Section(L.T("מה התוכנה עושה", "What this app does"),
                L.T("התראה כש-Claude מסיים", "A notification when Claude finishes"),
                L.T("כש-Claude Code מסיים להשיב לך ב-VS Code, קופץ חלון קטן בפינת המסך עם תקציר התשובה. לחיצה עליו מחזירה אותך לחלון ולשיחה הנכונים.",
                    "When Claude Code finishes replying in VS Code, a small window pops up in the corner of the screen with a summary. Clicking it takes you back to the right window and conversation."),
                L.T("למה זה שימושי", "Why it is useful"),
                L.T("אפשר לעבוד על משהו אחר, ולדעת בדיוק מתי Claude סיים, בלי לבדוק כל כמה דקות.",
                    "You can work on something else and know exactly when Claude is done, without checking every few minutes."));

            Section(L.T("ההתראה", "The notification"),
                L.T("סכם את ההודעה", "Summarize the message"),
                L.T("Claude עצמו מסכם את התשובה הארוכה בכמה מילים. זה לוקח כ-15 שניות ונספר במכסת השימוש שלך. כשכבוי, מוצגת תחילת ההודעה.",
                    "Claude itself summarizes the long reply in a few words. It takes about 15 seconds and counts against your usage quota. When off, the start of the message is shown."),
                L.T("אל תציג כשהעורך גלוי", "Skip when the editor is visible"),
                L.T("אם חלון השיחה כבר מולך על המסך, אין צורך להפריע לך.",
                    "If the conversation window is already in front of you, there is no need to interrupt you."),
                L.T("חלון התראה משלי", "Custom popup window"),
                L.T("חלון בפינת המסך שאפשר לקבוע את זמן ההצגה והמיקום שלו. כשכבוי, מופיעה התראה רגילה של Windows.",
                    "A window in the corner of the screen with adjustable display time and position. When off, a regular Windows notification appears."));

            Section(L.T("קריאה בקול - כשאתה לא ליד המחשב", "Voice call - when you are away"),
                L.T("מה זה", "What it is"),
                L.T("המחשב קורא לך בקול (\"<השם שלך>, קלוד סיים להשיב\") ואומר את הסיכום. מתאים כשקמת מהמחשב או עובד במקום אחר בחדר.",
                    "The computer calls you out loud (\"<your name>, Claude has finished replying\") and reads the summary. Good when you stepped away from the computer or work elsewhere in the room."),
                L.T("הפעלה וכיבוי מהירים", "Quick on/off"),
                L.T("בקיצור מקשים (ברירת המחדל Ctrl+Alt+V) או במתג \"קרא לי בקול\" בחלון הזה. אפשר לשנות את הקיצור בהגדרות. אם הוא לא נרשם - תוכנה אחרת כבר משתמשת בו, ובוחרים אחר.",
                    "With a hotkey (default Ctrl+Alt+V) or the \"Call me out loud\" switch in this window. You can change the hotkey in Settings. If it is not registered, another app already uses it - choose a different one."),
                L.T("השם שלי וכמה פעמים לקרוא", "My name and how many times"),
                L.T("הקול קורא לך בשם שכתבת. אפשר לקבוע כמה פעמים הוא יקרא לך ברצף, עם הפסקה של 5 שניות בין קריאה לקריאה.",
                    "The voice calls you by the name you typed. You choose how many times it calls in a row, with a 5-second pause between calls."),
                L.T("מתי הוא קורא לי", "When it calls me"),
                L.T("רק כשאתה לא ליד המחשב: אם נגעת במקלדת או בעכבר בדקות האחרונות (ברירת המחדל: 60 שניות), הוא לא יקרא. ואם חזרת למחשב באמצע הקריאות, הוא יפסיק. אפשר לשנות את הזמן, או לקבוע 0 כדי שיקרא תמיד.",
                    "Only when you are away: if you touched the keyboard or mouse recently (default: 60 seconds), it will not call. And if you come back to the computer in the middle of the calls, it stops. You can change the time, or set 0 to always call."),
                L.T("קול, מהירות וניקוד", "Voice, speed and nikud"),
                L.T("קול גבר או אישה (קולות טבעיים של Edge), ומהירות הקראה לבחירתך. \"נקד לפני הקראה\" מבקש מ-Claude להוסיף ניקוד לסיכום כדי שההגייה תהיה נכונה יותר. זה נספר במכסה.",
                    "A male or female voice (natural Edge voices) and a speaking speed of your choice. \"Add nikud before speaking\" asks Claude to add vowel marks to the summary so pronunciation is better. It counts against your quota."));

            Section(L.T("להשיב לקול", "Answering the voice"),
                L.T("האזן לתשובה שלי", "Listen for my answer"),
                L.T("אחרי שהמחשב קורא לך אפשר לומר \"שנייה אני מגיע\", \"רגע\", \"שמעתי\" או \"בסדר\", והוא מפסיק לקרוא ועונה \"בסדר, אני מחכה לך\". המיקרופון פועל רק בזמן הקריאה.",
                    "After the computer calls you, you can say \"just a second, I'm coming\", \"wait\", \"I heard\" or \"okay\", and it stops calling and answers \"OK, I'll wait for you\". The microphone is on only during the call."),
                L.T("הכתבה ל-Claude", "Dictate to Claude"),
                L.T("אחרי שאמרת שאתה בא אפשר גם להכתיב הוראה (\"כן, אני מאשר, תעשה כך וכך\"). התוכנה מקלידה אותה בתיבת השיחה של חלון ה-VS Code הנכון. כדי לשלוח - אומרים בסוף \"שלח\". כשההגדרה \"שלח רק כשאני אומר שלח\" כבויה, ההודעה נשלחת לבד אחרי כמה שניות של שקט.",
                    "After you say you are coming you can also dictate an instruction (\"yes, I approve, do such and such\"). The app types it into the chat box of the right VS Code window. To send, say \"send\" at the end. When \"Send only when I say send\" is off, the message is sent automatically after a few seconds of silence."),
                L.T("חשוב לדעת", "Good to know"),
                L.T("ההכתבה לא תעבוד אם המחשב נעול או אם חלון אחר בחזית. בזמן ההקלדה אל תיגע במקלדת ובעכבר.",
                    "Dictation does not work if the computer is locked or another window is in front. While it types, do not touch the keyboard or mouse."));

            Section(L.T("כלים נוספים", "More tools"),
                L.T("הודעה מתוזמנת", "Scheduled message"),
                L.T("שולחת הודעה קצרה ל-Claude בשעה שתבחר, כדי שמכסת 5 השעות תתחיל לרוץ מראש (למשל כבר ב-6:00 בבוקר).",
                    "Sends Claude a short message at a time you choose, so the 5-hour quota window starts early (for example at 6:00 AM)."),
                L.T("מידע טכני", "Technical info"),
                L.T("מראה כמה טוקנים נצרכו בכל שיחה, מצב מכסת 5 השעות והמכסה השבועית, ובאיזה מודל Claude משתמש.",
                    "Shows how many tokens each conversation used, the 5-hour and weekly quota status, and which model Claude uses."));

            Section(L.T("פרטיות, מכסה וזיכרון", "Privacy, quota and memory"),
                L.T("מה יוצא מהמחשב", "What leaves your computer"),
                L.T("זיהוי הדיבור והקול הטבעי עוברים דרך Edge לשירותים של מיקרוסופט, . סיכום וניקוד מבצע Claude, והם נספרים במכסת המנוי שלך. התוכנה לא שומרת הקלטות.",
                    "Speech recognition and the natural voice go through Edge to Microsoft services, . Summaries and nikud are done by Claude and count against your subscription quota. The app does not store recordings."),
                L.T("זיכרון", "Memory"),
                L.T("כשהקול הטבעי דלוק, דף Edge מוסתר נשאר פתוח ברקע כדי שההקראה תהיה מיידית. התוכנה מצמצמת את הזיכרון שלו כל כמה דקות. \"חיסכון בזיכרון\" סוגר אותו לגמרי כשהקריאה בקול כבויה (ההקראה הראשונה תתעכב כמה שניות).",
                    "While the natural voice is on, a hidden Edge page stays open in the background so speaking is instant. The app trims its memory every few minutes. \"Save memory\" closes it completely while voice call is off (the first speech is delayed by a few seconds)."));

            Section(L.T("אם משהו לא עובד", "If something does not work"),
                L.T("לא קופצות לי התראות", "I get no notifications"),
                L.T("1. חפש את האייקון של התוכנה ליד השעון (אולי בתוך החץ הקטן ^).\n2. פתח את ההגדרות ובדוק בכרטיס \"חיבור ל-Claude Code\" שכתוב \"החיבור מותקן\". אם לא - לחץ \"התקן חיבור\".\n3. פתח מחדש את Claude Code (או הקלד בו /hooks) וכך הוא יתחיל לשלוח לתוכנה הודעות.",
                    "1. Look for the app's icon by the clock (maybe inside the small ^ arrow).\n2. Open Settings and check that the \"Claude Code connection\" card says \"Connection installed\". If not, press \"Install connection\".\n3. Reopen Claude Code (or type /hooks in it) so it starts sending messages to the app."),
                L.T("הקול לא נשמע, או שהוא לא שומע אותי", "I hear no voice, or it does not hear me"),
                L.T("1. ודא שהקריאה בקול דלוקה (הקיצור שהגדרת, או המתג \"קרא לי בקול\" בחלון \"קריאה בקול\").\n2. ודא שהרמקולים דלוקים ושיש מיקרופון מחובר.\n3. בחלון \"קריאה בקול\" לחץ \"בדוק\" ליד \"בדיקת הקראה\" או \"בדיקת האזנה\" וראה מה קורה.\n4. אם עדיין לא עובד, אפשר לשלוח למי שבנה את התוכנה את הקובץ log.txt שנמצא בתיקייה שנפתחת כשמקלידים %APPDATA%\\ClaudeNotify בשורת הכתובת של סייר הקבצים.",
                    "1. Make sure voice call is on (your hotkey, or the \"Call me out loud\" switch in the \"Voice call\" window).\n2. Make sure the speakers are on and a microphone is connected.\n3. In the \"Voice call\" window press \"Test\" next to \"Test speaking\" or \"Test listening\" and see what happens.\n4. If it still does not work, send the log.txt file to whoever built the app. Find it by typing %APPDATA%\\ClaudeNotify in the File Explorer address bar."),
                L.T("אנטי-וירוס או Windows מתריעים", "Antivirus or Windows shows a warning"),
                L.T("זו לא תוכנה מסוכנת. אנטי-וירוס ו-Windows מתריעים על כל תוכנה קטנה שאין לה \"חתימה דיגיטלית\" (תעודה שעולה כסף), וזה מה שקורה כאן. כדי שההתראה תפסיק:\n1. פתח את \"אבטחת Windows\" (חפש אותה בתפריט התחל).\n2. לחץ \"הגנה מפני וירוסים ואיומים\", ואז \"ניהול הגדרות\".\n3. גלול מטה ללחוץ \"הוספה או הסרה של החרגות\", ואז \"הוסף החרגה\" ← \"תיקיה\".\n4. בחר את התיקייה שבה נמצאת התוכנה ClaudeNotify.\nאם יש לך אנטי-וירוס אחר, חפש בהגדרות שלו את המילה \"חריגים\" או \"Exclusions\".",
                    "This is not a dangerous program. Antivirus software and Windows warn about every small program that has no \"digital signature\" (a certificate that costs money), and that is what happens here. To stop the warning:\n1. Open \"Windows Security\" (search for it in the Start menu).\n2. Click \"Virus & threat protection\", then \"Manage settings\".\n3. Scroll down and click \"Add or remove exclusions\", then \"Add an exclusion\" > \"Folder\".\n4. Choose the folder where the ClaudeNotify program is.\nIf you use another antivirus, look in its settings for \"Exclusions\"."));

            var flow = new FlowLayoutPanel { Dock = DockStyle.Fill, Padding = Theme.P(18, 10, 18, 0), WrapContents = false, BackColor = Color.White, FlowDirection = L.He ? FlowDirection.RightToLeft : FlowDirection.LeftToRight };
            var close = new PillButton(L.T("סגור", "Close"), true);
            close.Click += delegate { Hide(); };
            flow.Controls.Add(close);
            bar.Controls.Add(flow);
            AcceptButton = close;
            FitContent();
        }

        // כרטיס עם שורות של כותרת והסבר. הפרמטרים: כותרת הכרטיס, ואז זוגות של (כותרת, הסבר)
        void Section(string card, params string[] pairs)
        {
            var c = new Card(card);
            for (int i = 0; i + 1 < pairs.Length; i += 2) AddRow(c, pairs[i], pairs[i + 1], null, false, 0, null);
            content.Controls.Add(c);
        }
    }
}
