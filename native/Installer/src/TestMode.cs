// Hidden test switches. MOVIESHELF_SETUP_TEST_RENDER=<folder> draws every page to PNG in both languages at several scales.
// MOVIESHELF_SETUP_TEST_DRIVE=<script> walks the real wizard through its real Next and Back handlers. Nothing here runs
// unless one of those variables is set.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Text;
using System.Windows.Forms;

static class TestMode
{
    // ---------- render ----------
    public static void Render(string folder, SetupFile sf)
    {
        Directory.CreateDirectory(folder);
        foreach (string lang in new[] { "en", "fa" })
            foreach (float scale in new[] { 1f, 1.25f, 1.5f, 2f })
            {
                L.Fa = Ui.Rtl = lang == "fa";
                Ui.SetScale(scale);
                string tag = lang + "-" + ((int)(scale * 100)).ToString(CultureInfo.InvariantCulture) + "-";
                RenderInstall(folder, tag, sf);
                RenderUninstall(folder, tag);
            }
    }

    static void Pump(int ms)
    {
        DateTime until = DateTime.UtcNow.AddMilliseconds(ms);
        do { Application.DoEvents(); System.Threading.Thread.Sleep(5); } while (DateTime.UtcNow < until);
    }

    /// <summary>Lists text that does not fit its control, so all the scales and languages get checked and not only the pictures looked at.</summary>
    static void CheckFit(Control c, string name)
    {
        foreach (Control x in c.Controls)
        {
            if (!x.Visible) continue;
            string problem = null;
            var t = x as TextView;
            var card = x as Card;
            var box = x as CheckBox;
            var btn = x as Button;
            var row = x as StatusRow;
            if (t != null && t.Text.Length > 0)
            {
                int need = Ui.TextHeight(t.Text, t.Font, t.Width);
                if (need > t.Height + 2) problem = "needs height " + need + ", has " + t.Height + ": " + t.Text;
            }
            else if (card != null)
            {
                int room = card.Width - Ui.S(54) - (card.Shield ? Ui.S(24) : 0);
                if (TextRenderer.MeasureText(card.Title, Ui.Font(card.Compact ? 13 : 14, true)).Width > room) problem = "title too wide: " + card.Title;
                if (card.Desc.Length > 0 && Ui.TextHeight(card.Desc, Ui.Font(12, false), room) > card.Height - Ui.S(34)) problem = "description too tall: " + card.Desc;
            }
            else if (box != null && TextRenderer.MeasureText(box.Text.Replace("&", ""), box.Font).Width + Ui.S(22) > box.Width) problem = "text too wide: " + box.Text;
            else if (btn != null && TextRenderer.MeasureText(btn.Text.Replace("&", ""), btn.Font).Width + Ui.S(14) > btn.Width) problem = "text too wide: " + btn.Text;
            else if (row != null && TextRenderer.MeasureText(row.Detail, Ui.Font(12, false)).Width > row.Width - Ui.S(32) - row.Reserve) problem = "detail too wide: " + row.Detail;
            if (problem != null) File.AppendAllText(fitLog, name + ": " + problem + "\r\n", new UTF8Encoding(false));
            CheckFit(x, name);
        }
    }

    static string fitLog;

    static void Snap(Form f, string path)
    {
        Pump(40);
        Control root = f.Controls[0];
        fitLog = Path.Combine(Path.GetDirectoryName(path), "fit.txt");
        CheckFit(root, Path.GetFileNameWithoutExtension(path));
        using (var b = new Bitmap(root.Width, root.Height, PixelFormat.Format32bppArgb))
        {
            root.DrawToBitmap(b, new Rectangle(0, 0, root.Width, root.Height));
            b.Save(path, ImageFormat.Png);
        }
    }

    static void Prepare(Form f)
    {
        f.StartPosition = FormStartPosition.Manual;
        f.Location = new Point(-30000, -30000);
        f.ShowInTaskbar = false;
        f.Show();
    }

    static void RenderInstall(string folder, string tag, SetupFile sf)
    {
        using (var w = new InstallWizard(sf))
        {
            w.Demo = true;
            w.Prev = null;
            w.C.Upgrade = false;
            w.C.Previous = null;
            w.C.InstallDir = Env.DefaultDir(false);
            w.BuildAll();
            Prepare(w);
            Action<string> go = id => w.GoTo(id);
            Action<string> snap = name => Snap(w, Path.Combine(folder, tag + name + ".png"));
            Action<Form, string> snap2 = (f, name) => Snap(f, Path.Combine(folder, tag + name + ".png"));

            go("welcome"); snap("01-welcome");
            go("system");
            ((SystemPage)w.Current).Demo(false); snap("02-system");
            ((SystemPage)w.Current).Demo(true); snap("02b-system-problems");
            go("license"); snap("03-license");
            go("location"); snap("04-location");
            w.Current.Set("scope", "all"); snap("04b-location-all");
            w.Current.Set("scope", "user");
            w.Current.Set("path", @"C:\Windows\Apps"); snap("04c-location-refused");
            w.Current.Set("path", Env.DefaultDir(false));
            w.Online = true;
            go("components"); snap("05-components");
            w.Current.Set("type", "full"); snap("05b-components-full");
            w.Current.Set("type", "typical");
            w.Online = false;
            go("components"); snap("05c-components-offline");
            w.Online = true;
            go("components");
            w.Current.Set("type", "full");
            go("shortcuts"); snap("06-shortcuts");
            go("ready"); snap("07-ready");
            go("progress");
            ((ProgressPage)w.Current).Sample(0.42, L.T("Downloading IMDb data", "در حال دانلود داده‌های IMDb"),
                Fmt.Ltr("title.principals.tsv.gz") + L.T(": ", ": ") + Fmt.Size(330L << 20) + L.T(" of ", " از ") + Fmt.Size(752L << 20) + L.T(", ", "، ") + Fmt.Size(9L << 20) + L.T("/s", " در ثانیه") + L.T(", ", "، ") + Fmt.Time(47) + L.T(" left", " مانده"),
                new[] { "MovieShelf setup 1.0.0, Windows build 26300", "title.ratings.tsv.gz is less than 7 days old. Skipped.", "Downloaded name.basics.tsv.gz" }, false);
            snap("08-progress");
            ((ProgressPage)w.Current).Sample(0.81, L.T("Unpacking files", "در حال باز کردن فایل‌ها"), "Microsoft.UI.Xaml.Controls.dll",
                new[] { "MovieShelf setup 1.0.0, Windows build 26300", "title.ratings.tsv.gz is less than 7 days old. Skipped.", "Downloaded name.basics.tsv.gz", "Downloaded title.akas.tsv.gz", "ffmpeg download verified" }, true);
            snap("08b-progress-log");
            w.CloseWithoutAsking = true;

            var finish = (FinishPage)w.Find("finish");
            w.Log = null;
            finish.Sample(Exit.Ok, new List<string>(), false, null);
            go("finish"); snap("09-finish");
            finish.Sample(Exit.Partial, new List<string> { L.T("IMDb data: ", "داده‌های IMDb: ") + Fmt.Ltr("title.akas, title.principals"), L.T("ffprobe, for reading the real quality of video files", "ffprobe، برای خواندن کیفیت واقعی فایل‌های ویدیویی") }, true, null);
            go("finish"); snap("09b-finish-partial");
            finish.Sample(Exit.Cancelled, new List<string>(), false, L.T("Installation cancelled.", "نصب لغو شد."));
            go("finish"); snap("09c-finish-cancelled");
            finish.Sample(Exit.Damaged, new List<string>(), false, Texts.Damaged());
            go("finish"); snap("09d-finish-failed");

            using (var p = new Prompt(L.T("Cancel setup?", "نصب لغو شود؟"), L.T("MovieShelf has not been installed yet. Nothing on your computer has changed.", "MovieShelf هنوز نصب نشده است و چیزی در رایانه شما تغییر نکرده است."),
                L.T("Cancel setup", "لغو نصب"), L.T("Continue", "ادامه"), true))
            {
                Prepare(p);
                snap2(p, "10-prompt");
            }

            // Maintenance mode: MovieShelf is already installed.
            var prev = new Installed { Dir = w.C.InstallDir, Version = "1.9.0", M = null };
            w.Prev = prev;
            w.BuildAll();
            go("welcome"); snap("01b-welcome-installed");
        }
    }

    static void RenderUninstall(string folder, string tag)
    {
        var m = new Manifest();
        m["version"] = BuildInfo.Version;
        m["scope"] = "user";
        using (var w = new UninstallWizard(@"C:\Users\Jason\AppData\Local\Programs\MovieShelf", m))
        {
            w.Demo = true;
            w.AppSize = 170L << 20;
            long[] sizes = { 412L << 20, 2L << 20, 2 << 10, 1800L << 20, 2950L << 20 };
            for (int i = 0; i < sizes.Length; i++) w.Items[i].Size = sizes[i];
            Prepare(w);
            Action<string> go = id => w.GoTo(id);
            Action<string> snap = name => Snap(w, Path.Combine(folder, tag + name + ".png"));
            go("uwelcome"); snap("u1-welcome");
            go("keep"); snap("u2-keep");
            w.Current.Set("posters", "0");
            w.Current.Set("imdb", "0"); snap("u2b-keep-delete");
            go("uready"); snap("u3-ready");
            go("removing");
            ((URemovePage)w.Current).Sample(0.6, L.T("Removing files", "در حال حذف فایل‌ها"), "Microsoft.UI.Xaml.Controls.dll");
            snap("u4-removing");
            w.CloseWithoutAsking = true;
            w.ResultCode = Exit.Ok;
            go("ufinish"); snap("u5-finish");
            w.ResultCode = Exit.Partial;
            w.Remaining = new List<string> { @"C:\Users\Jason\AppData\Local\Programs\MovieShelf\notes.txt", @"C:\Users\Jason\AppData\Local\Programs\MovieShelf\my-files" };
            go("ufinish"); snap("u5b-finish-left");
        }
    }

    // ---------- drive ----------
    public static bool Attach(Wizard w)
    {
        string script = Env.Test("DRIVE");
        if (script == null) return false;
        w.StartPosition = FormStartPosition.Manual;
        w.Location = new Point(-30000, -30000);
        w.ShowInTaskbar = false;
        new Driver(w, script).Start();
        return true;
    }

    /// <summary>
    /// Runs a script, one line at a time, against the live wizard: next, back, cancel, yes, no, press ok,
    /// set KEY VALUE, expect page ID, expect next on|off, wait page ID [seconds], wait code N [seconds], lang fa|en, stop, sleep MS, close.
    /// Everything it does and every failed expectation goes to SCRIPT.log.
    /// </summary>
    class Driver
    {
        readonly Wizard W;
        readonly string[] lines;
        readonly string logFile;
        readonly Timer timer = new Timer { Interval = 60 };
        int at, failures;
        string waitPage;
        int waitCode = int.MinValue;
        DateTime deadline, sleepUntil;

        public Driver(Wizard w, string script)
        {
            W = w;
            lines = File.ReadAllLines(script, Encoding.UTF8);
            logFile = script + ".log";
            File.WriteAllText(logFile, "");
        }

        public void Start()
        {
            timer.Tick += (o, e) => Tick();
            W.Shown += (o, e) => timer.Start();
        }

        void Say(string s) { File.AppendAllText(logFile, s + "\r\n", new UTF8Encoding(false)); }
        void Fail(string s) { failures++; Say("FAIL " + s); }

        void Tick()
        {
            if (DateTime.UtcNow < sleepUntil) return;
            if (waitPage != null || waitCode != int.MinValue)
            {
                bool done = waitPage != null ? W.Current.Id == waitPage : W.Code == waitCode;
                if (done) { Say("  reached"); waitPage = null; waitCode = int.MinValue; return; }
                if (DateTime.UtcNow > deadline) { Fail("timed out on page " + W.Current.Id + " (code " + W.Code + ")"); waitPage = null; waitCode = int.MinValue; }
                return;
            }
            if (at >= lines.Length)
            {
                timer.Stop();
                Say(failures == 0 ? "PASS" : "FAILED " + failures);
                W.CloseWithoutAsking = true;
                W.Close();
                return;
            }
            string line = lines[at++].Trim();
            if (line.Length == 0 || line[0] == '#') return;
            Say("> " + line);
            try { Do(line); }
            catch (Exception e) { Fail(line + ": " + e.Message); }
        }

        void Do(string line)
        {
            string[] p = line.Split(new[] { ' ' }, 3, StringSplitOptions.RemoveEmptyEntries);
            string a1 = p.Length > 1 ? p[1] : "", a2 = p.Length > 2 ? Environment.ExpandEnvironmentVariables(p[2]) : "";
            switch (p[0])
            {
                case "next": if (!W.NextBtn.Enabled) Say("  next is disabled"); W.NextBtn.PerformClick(); break;
                case "back": W.BackBtn.PerformClick(); break;
                case "cancel": W.CancelBtn.PerformClick(); break;
                case "yes": Prompt.TestAnswer = true; break;
                case "no": Prompt.TestAnswer = false; break;
                case "set": W.Current.Set(a1, a2); break;
                case "lang": ((InstallWizard)W).SwitchLanguage(a1 == "fa"); break;
                case "sleep": sleepUntil = DateTime.UtcNow.AddMilliseconds(int.Parse(a1, CultureInfo.InvariantCulture)); break;
                case "close": W.CloseWithoutAsking = true; W.Close(); break;
                case "stop": W.CancelBtn.PerformClick(); break;
                case "say": Say(a1 + " " + a2); break;
                case "key": W.Activate(); SendKeys.SendWait(a1); break;
                case "focus": Say("  focus=" + Focused()); break;
                case "show": Say("  page=" + W.Current.Id + " next=" + W.NextBtn.Enabled + " back=" + W.BackBtn.Enabled + " cancel=" + W.CancelBtn.Enabled + " dir=" + DirOf() + " message=" + MessageOf()); break;
                case "expect":
                    if (a1 == "page" && W.Current.Id != a2) Fail("expected page " + a2 + ", on " + W.Current.Id);
                    if (a1 == "next" && W.NextBtn.Enabled != (a2 == "on")) Fail("expected next " + a2);
                    if (a1 == "dir" && !Files.Same(DirOf(), Environment.ExpandEnvironmentVariables(a2))) Fail("expected dir " + a2 + ", got " + DirOf());
                    if (a1 == "shown" && W.Pages.Find(x => x.Id == a2).Shown == false) Fail("page not shown: " + a2);
                    if (a1 == "hidden" && W.Pages.Find(x => x.Id == a2).Shown) Fail("page shown: " + a2);
                    if (a1 == "message" && MessageOf().IndexOf(a2, StringComparison.OrdinalIgnoreCase) < 0) Fail("expected message with " + a2 + ", got " + MessageOf());
                    break;
                case "wait":
                    string[] q = a2.Split(' ');
                    deadline = DateTime.UtcNow.AddSeconds(q.Length > 1 ? int.Parse(q[1], CultureInfo.InvariantCulture) : 120);
                    if (a1 == "page") waitPage = q[0]; else waitCode = int.Parse(q[0], CultureInfo.InvariantCulture);
                    break;
                default: Fail("unknown command " + p[0]); break;
            }
        }

        string DirOf() { var i = W as InstallWizard; return i != null ? i.C.InstallDir : ""; }

        string Focused()
        {
            Control c = W.ActiveControl;
            while (c is ContainerControl && ((ContainerControl)c).ActiveControl != null) c = ((ContainerControl)c).ActiveControl;
            return c == null ? "none" : c.GetType().Name + " " + c.Text;
        }

        string MessageOf()
        {
            var i = W as InstallWizard;
            if (i != null)
            {
                var l = i.Current as LocationPage;
                if (l != null) return l.MessageText;
                var c = i.Current as ComponentsPage;
                if (c != null) return c.Totals + " online=" + i.Online;
                return i.ResultMessage ?? "";
            }
            return "";
        }
    }
}
