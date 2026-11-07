// Entry point of the MovieShelf setup program. The same binary is the wizard, the silent installer, the elevated worker and,
// as uninstall.exe, the uninstaller.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;

[assembly: AssemblyTitle("MovieShelf Setup")]
[assembly: AssemblyProduct("MovieShelf")]

static class Texts
{
    public static string Damaged() { return L.T("The setup file is damaged. Download MovieShelf-Setup.exe again.", "فایل نصب آسیب دیده است. MovieShelf-Setup.exe را دوباره دانلود کنید."); }
    public static string Next() { return L.T("&Next", "بعدی"); }
    public static string Back() { return L.T("&Back", "قبلی"); }
    public static string Cancel() { return L.T("Cancel", "انصراف"); }

    /// <summary>The first 64-digit hex word of a .sha256 file, or null.</summary>
    public static string FirstHex(string text)
    {
        foreach (string word in text.Split(new[] { ' ', '\t', '\r', '\n', '*' }, StringSplitOptions.RemoveEmptyEntries))
        {
            if (word.Length != 64) continue;
            bool hex = true;
            foreach (char c in word) hex &= Uri.IsHexDigit(c);
            if (hex) return word;
        }
        return null;
    }
}

class PathCheck { public string Final, Error, Warning; }

/// <summary>The rules for an install folder. The final path always ends in a MovieShelf folder of its own.</summary>
static class PathRules
{
    static readonly string[] Reserved = { "CON", "PRN", "AUX", "NUL", "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9", "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9" };

    public static PathCheck Check(string input, bool allUsers, bool portable)
    {
        var r = new PathCheck();
        string s = (input ?? "").Trim().Trim('"').Trim();
        if (s.Length == 0) { r.Error = L.T("Choose a folder.", "یک پوشه انتخاب کنید."); return r; }
        s = Environment.ExpandEnvironmentVariables(s).Replace('/', '\\');
        if (s.StartsWith("\\\\", StringComparison.Ordinal)) { r.Error = Network(); return r; }
        if (s.Length < 2 || !char.IsLetter(s[0]) || s[1] != ':' || (s.Length > 2 && s[2] != '\\'))
        {
            r.Error = L.T("Type a full path, such as D:\\Apps.", "مسیر کامل را بنویسید، مانند D:\\Apps.");
            return r;
        }
        string rest = s.Length > 3 ? s.Substring(3) : "";
        foreach (char c in rest)
            if (c < 32 || "<>:\"|?*".IndexOf(c) >= 0) { r.Error = Fmt.F(L.T("A folder name cannot contain {0}", "نام پوشه نمی‌تواند شامل {0} باشد"), c < 32 ? "?" : c.ToString()); return r; }
        foreach (string part in rest.Split('\\'))
        {
            if (part.Length == 0) continue;
            if (part == "..") { r.Error = L.T("Type a full path, such as D:\\Apps.", "مسیر کامل را بنویسید، مانند D:\\Apps."); return r; }
            string name = part.Split('.')[0].TrimEnd(' ').ToUpperInvariant();
            if (Array.IndexOf(Reserved, name) >= 0 || part.EndsWith(".", StringComparison.Ordinal) || part.EndsWith(" ", StringComparison.Ordinal))
            {
                r.Error = Fmt.F(L.T("Windows does not allow a folder named \"{0}\".", "ویندوز پوشه‌ای با نام «{0}» را نمی‌پذیرد."), part);
                return r;
            }
        }
        string full;
        try { full = Path.GetFullPath(s.Length == 2 ? s + "\\" : s).TrimEnd('\\'); }
        catch (Exception) { r.Error = L.T("This is not a valid folder path.", "این مسیر پوشه معتبر نیست."); return r; }

        if (full.Length <= 2) { r.Error = L.T("Choose a folder, not a whole drive.", "یک پوشه انتخاب کنید، نه کل درایو."); return r; }

        DriveInfo drive = new DriveInfo(full.Substring(0, 1));
        if (drive.DriveType == DriveType.Network) { r.Error = Network(); return r; }
        if (drive.DriveType == DriveType.NoRootDirectory) { r.Error = Fmt.F(L.T("There is no drive {0}.", "درایو {0} وجود ندارد."), full.Substring(0, 2)); return r; }
        if (drive.DriveType == DriveType.CDRom) { r.Error = L.T("Setup cannot write to this drive.", "نصب‌کننده نمی‌تواند روی این درایو بنویسد."); return r; }

        string windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        if (Files.Same(full, windows) || Files.Inside(full, windows)) { r.Error = L.T("MovieShelf cannot go in the Windows folder.", "MovieShelf نمی‌تواند در پوشه ویندوز نصب شود."); return r; }
        foreach (string pf in new[] { Env.ProgramFiles, Env.Var("ProgramFiles(x86)") })
            if (pf != null && Files.Same(full, pf)) { r.Error = L.T("Choose a folder inside Program Files, not Program Files itself.", "پوشه‌ای داخل Program Files انتخاب کنید، نه خود آن را."); return r; }
        string profile = Env.Var("USERPROFILE") ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (Files.Same(full, profile) || Files.Same(full, Path.GetDirectoryName(profile))) { r.Error = L.T("Choose a folder inside your user folder, not the user folder itself.", "پوشه‌ای داخل پوشه کاربری خود انتخاب کنید، نه خود آن را."); return r; }

        string final = string.Equals(Path.GetFileName(full), "MovieShelf", StringComparison.OrdinalIgnoreCase) ? full : Path.Combine(full, "MovieShelf");
        r.Final = final;

        string data = Env.DataRoot(null, false);
        if (Files.Same(final, data) || Files.Inside(final, data)) { r.Error = L.T("This is where MovieShelf keeps its library. Choose another folder.", "این پوشه محل نگهداری کتابخانه MovieShelf است. پوشه دیگری انتخاب کنید."); return r; }
        if (final.Length > 170) { r.Error = L.T("This path is too long. Choose a shorter one.", "این مسیر خیلی طولانی است. مسیر کوتاه‌تری انتخاب کنید."); return r; }
        if (allUsers && Env.Test("NOUAC") == null && (Files.Same(final, profile) || Files.Inside(final, profile)))
        {
            r.Error = L.T("Other users cannot reach this folder. Choose one outside your user folder.", "کاربران دیگر به این پوشه دسترسی ندارند. پوشه‌ای بیرون از پوشه کاربری خود انتخاب کنید.");
            return r;
        }
        if (!allUsers && !Files.CanWrite(final))
        {
            r.Error = L.T("You cannot write to this folder without administrator rights. Choose \"For all users\", or another folder.", "بدون دسترسی مدیر سیستم نمی‌توانید در این پوشه بنویسید. «برای همه کاربران» یا پوشه دیگری را انتخاب کنید.");
            return r;
        }
        if (portable && allUsers) { r.Error = L.T("Portable needs a folder you can write to.", "حالت قابل حمل به پوشه‌ای نیاز دارد که بتوانید در آن بنویسید."); return r; }

        if (Directory.Exists(final) && !File.Exists(Path.Combine(final, "MovieShelf.exe")) && !File.Exists(Path.Combine(final, Manifest.FileName)))
        {
            bool any;
            try { any = Directory.GetFileSystemEntries(final).Length > 0; } catch (Exception) { any = false; }
            if (any) r.Warning = L.T("This folder already holds other files. They stay as they are, and MovieShelf is added beside them.", "این پوشه فایل‌های دیگری دارد. آن‌ها دست‌نخورده می‌مانند و MovieShelf کنارشان قرار می‌گیرد.");
        }
        return r;
    }

    static string Network() { return L.T("MovieShelf cannot be installed on a network folder.", "MovieShelf روی پوشه شبکه نصب نمی‌شود."); }
}

class Args
{
    public bool Silent, AllUsers, Portable, Run, Help, NoStartMenu;
    public bool? Desktop;
    public string Dir, Lang, LogPath, Components, DeleteData, Worker, From, Bad;
    public int Parent, Ppid;
    public readonly List<string> Raw = new List<string>();

    public static Args Parse(string[] argv)
    {
        var a = new Args();
        for (int i = 0; i < argv.Length; i++)
        {
            string arg = argv[i];
            string body = arg.TrimStart('/', '-');
            if (body.Length == arg.Length) { a.Bad = arg; continue; }
            int eq = body.IndexOf('=');
            string key = (eq > 0 ? body.Substring(0, eq) : body).ToUpperInvariant();
            string value = eq > 0 ? body.Substring(eq + 1).Trim('"') : null;
            if (key != "_FROM" && key != "_PPID") a.Raw.Add(arg);
            switch (key)
            {
                case "S": case "Y": case "Q": case "SILENT": case "QUIET": a.Silent = true; break;
                case "D":
                    // By NSIS custom /D= takes the rest of the line, quotes or not.
                    while (i + 1 < argv.Length && argv[i + 1].Length > 0 && argv[i + 1][0] != '/' && argv[i + 1][0] != '-') { value += " " + argv[++i]; a.Raw.Add(argv[i]); }
                    a.Dir = value; break;
                case "ALLUSERS": a.AllUsers = true; break;
                case "COMPONENTS": a.Components = value ?? ""; break;
                case "DESKTOP": a.Desktop = true; break;
                case "NODESKTOP": a.Desktop = false; break;
                case "NOSTARTMENU": a.NoStartMenu = true; break;
                case "PORTABLE": a.Portable = true; break;
                case "RUN": a.Run = true; break;
                case "LANG": a.Lang = (value ?? "").ToLowerInvariant(); if (a.Lang != "fa" && a.Lang != "en") a.Bad = arg; break;
                case "LOG": a.LogPath = value; break;
                case "DELETEDATA": a.DeleteData = value ?? ""; break;
                case "?": case "H": case "HELP": a.Help = true; break;
                case "_WORKER": a.Worker = value; break;
                case "_PARENT": int.TryParse(value, out a.Parent); break;
                case "_FROM": a.From = value; break;
                case "_PPID": int.TryParse(value, out a.Ppid); break;
                default: a.Bad = arg; break;
            }
        }
        return a;
    }
}

static class Program
{
    const string Usage =
        "MovieShelf-Setup.exe [/S] [/D=path] [/ALLUSERS] [/COMPONENTS=imdb,ffprobe] [/DESKTOP] [/NODESKTOP] [/NOSTARTMENU] [/PORTABLE] [/RUN] [/LANG=fa|en] [/LOG=path]\n" +
        "uninstall.exe [/S] [/DELETEDATA=library,backups,settings,posters,imdb,all] [/LOG=path]\n\n" +
        "/S (or -y) runs without a window. Exit codes: 0 done, 1 failed, 2 bad arguments, 3 cancelled, 4 system too old, 5 damaged setup file, 6 done but an optional download failed, 1223 administrator prompt declined.";

    [STAThread]
    static int Main(string[] argv)
    {
        // A program with no target framework attribute starts with TLS 1.0 as its default; the download hosts need 1.2.
        ServicePointManager.SecurityProtocol = (SecurityProtocolType)3072;
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);

        Args a = Args.Parse(argv);
        if (a.Worker != null) return Worker.Run(a.Worker, a.Parent);
        Application.ThreadException += (o, e) => Report(a, e.Exception);
        try { return Start(a); }
        catch (Exception e)
        {
            Report(a, e);
            return Exit.Failed;
        }
    }

    /// <summary>The last line of defence: a bug should end in a message, never in a silent crash.</summary>
    static void Report(Args a, Exception e)
    {
        try { File.AppendAllText(Path.Combine(Env.Temp, "MovieShelf-Setup-error.log"), DateTime.Now + " " + e + "\r\n"); } catch (Exception) { }
        if (!a.Silent) MessageBox.Show(e.Message, "MovieShelf Setup", MessageBoxButtons.OK, MessageBoxIcon.Error);
    }

    static int Start(Args a)
    {

        string name = Path.GetFileName(Env.Self);
        SetupFile sf = SetupFile.Read(Env.Self);
        bool uninstaller = sf == null && (a.From != null || string.Equals(name, "uninstall.exe", StringComparison.OrdinalIgnoreCase));

        if (a.Bad != null || a.Help)
        {
            if (!a.Silent) MessageBox.Show(Usage, "MovieShelf Setup", MessageBoxButtons.OK, a.Help ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
            return a.Help ? Exit.Ok : Exit.BadArguments;
        }

        string render = Env.Test("RENDER");
        if (render != null) { TestMode.Render(render, sf); return Exit.Ok; }

        if (uninstaller) return UninstallMain.Run(a);

        ChooseLanguage(a);
        if (sf == null)
        {
            if (!a.Silent) { Ui.Init(); MessageBox.Show(Texts.Damaged(), "MovieShelf Setup", MessageBoxButtons.OK, MessageBoxIcon.Error); }
            return Exit.Damaged;
        }
        if (a.Silent) return Silent.Install(a, sf);

        bool first;
        using (var only = new Mutex(true, "Local\\MovieShelf-Setup", out first))
        {
            if (!first)
            {
                MessageBox.Show(L.T("MovieShelf Setup is already running.", "نصب MovieShelf در حال اجراست."), "MovieShelf Setup", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return Exit.Cancelled;
            }
            Ui.Init();
            var w = new InstallWizard(sf);
            if (w.Prev == null)
            {
                if (a.AllUsers) { w.C.AllUsers = true; w.C.InstallDir = Env.DefaultDir(true); }
                if (a.Dir != null) w.C.InstallDir = a.Dir;
                w.C.Portable = a.Portable;
            }
            TestMode.Attach(w);
            Application.Run(w);
            if (w.Log != null && (w.ResultCode == Exit.Ok || w.ResultCode == Exit.Partial || w.ResultCode == Exit.Cancelled)) Files.TryDelete(w.Log.Path);
            return w.Code;
        }
    }

    static void ChooseLanguage(Args a)
    {
        string lang = a.Lang ?? Env.Test("LANG") ?? CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;
        L.Fa = Ui.Rtl = lang == "fa";
    }
}

/// <summary>The elevated copy of this program.</summary>
static class Worker
{
    public static int Run(string ipc, int parent)
    {
        string planFile = Path.Combine(ipc, "plan.txt"), cancel = Path.Combine(ipc, "cancel");
        var sink = new FileSink(Path.Combine(ipc, "progress.txt"));
        Func<bool> gone = () =>
        {
            try { using (Process p = Process.GetProcessById(parent)) return p.HasExited; }
            catch (ArgumentException) { return true; }
        };
        Func<bool> stopped = () => File.Exists(cancel) || gone();
        try
        {
            while (!File.Exists(planFile))
            {
                if (stopped()) return Exit.Cancelled;
                Thread.Sleep(100);
            }
            Dictionary<string, string> d = Kv.Load(planFile);
            L.Fa = Kv.Get(d, "lang") == "fa";
            if (Kv.Get(d, "mode") == "uninstall")
            {
                int pid;
                int.TryParse(Kv.Get(d, "waitpid"), out pid);
                var r = new Remover(Kv.Get(d, "dir"), Kv.Flag(d, "all"), pid, sink) { Almost = sink.Almost };
                r.Run();
                sink.Result(r.Failures, string.Join("\t", r.Left.ToArray()));
                return r.Failures;
            }
            var c = new Committer(CommitPlan.From(d), sink, stopped);
            int code = c.Run();
            sink.Result(code, c.Message ?? "");
            return code;
        }
        catch (Exception e)
        {
            sink.Log("Failed: " + e);
            sink.Result(Exit.Failed, e.Message);
            return Exit.Failed;
        }
    }
}

/// <summary>/S: the same work, no window.</summary>
static class Silent
{
    public static int Install(Args a, SetupFile sf)
    {
        var log = new Logger(a.LogPath ?? Path.Combine(Env.Temp, "MovieShelf-Setup-" + Process.GetCurrentProcess().Id + ".log"));
        var sink = new LogSink(log);
        int build = Native.WindowsBuild();
        if (build < 17763 || !Environment.Is64BitOperatingSystem || Env.Test("NOT64") != null)
        {
            log.Write("This computer does not meet the requirements: 64-bit Windows 10 build 17763 or later, found build " + build + ".");
            return Exit.Requirements;
        }

        var c = new Choices { AllUsers = a.AllUsers, Portable = a.Portable };
        Installed prev = Installed.Find();
        string requested = a.Dir;
        PathCheck check = requested != null ? PathRules.Check(requested, c.AllUsers, c.Portable) : null;
        if (prev != null && (check == null || (check.Final != null && Files.Same(check.Final, prev.Dir))))
        {
            c.Use(prev);
            check = null;
            log.Write("Upgrading the install in " + prev.Dir);
        }
        else if (prev != null)
        {
            log.Write("MovieShelf is already installed in " + prev.Dir + ". Uninstall it first, or leave out /D.");
            return Exit.BadArguments;
        }
        else if (check == null) check = PathRules.Check(Env.DefaultDir(c.AllUsers), c.AllUsers, c.Portable);
        if (check != null)
        {
            if (check.Error != null) { log.Write(check.Error); return Exit.BadArguments; }
            c.InstallDir = check.Final;
            if (check.Warning != null) log.Write(check.Warning);
        }

        if (a.Components != null)
            foreach (string item in a.Components.Split(','))
            {
                string n = item.Trim().ToLowerInvariant();
                if (n == "imdb") c.Imdb = true;
                else if (n == "ffprobe") { c.Ffprobe = !c.KeepFfprobe; }
                else if (n.Length > 0 && n != "app") { log.Write("Unknown component: " + n); return Exit.BadArguments; }
            }
        if (a.Desktop.HasValue) c.Desktop = a.Desktop.Value;
        if (a.NoStartMenu) c.StartMenu = false;

        var runner = new Runner(c, sf, sink, log);
        int code = runner.Run();
        if (runner.Message != null) log.Write(runner.Message);
        foreach (string f in runner.Failed) log.Write("Not installed: " + f);
        if ((code == Exit.Ok || code == Exit.Partial) && a.Run)
        {
            try { Process.Start(new ProcessStartInfo(Path.Combine(c.InstallDir, "MovieShelf.exe")) { WorkingDirectory = c.InstallDir, UseShellExecute = true }); }
            catch (Exception e) { log.Write("Could not start MovieShelf: " + e.Message); }
        }
        if (a.LogPath == null && (code == Exit.Ok || code == Exit.Partial)) Files.TryDelete(log.Path);
        return code;
    }
}
