// The install wizard and its pages.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Windows.Forms;

class InstallWizard : Wizard
{
    public readonly Choices C = new Choices();
    public readonly SetupFile Sf;
    public Installed Prev;
    public bool Uninstalling;
    public bool? Online;
    public bool SizesFetched;
    public Logger Log;
    public Runner Runner;
    public int ResultCode = -1;
    public string ResultMessage, LocationMessage;
    public List<string> FailedDownloads = new List<string>();
    public bool ImdbFailed;
    public bool Demo; // the render test: no real checks

    public long AppBytes { get { return Sf != null ? Sf.UnpackedSize : 170L << 20; } }
    public string DataDir { get { return Env.DataDir(C.InstallDir, C.Portable); } }

    public InstallWizard(SetupFile setup)
    {
        Sf = setup;
        C.InstallDir = Env.DefaultDir(false);
        Prev = Installed.Find();
        if (Prev != null) C.Use(Prev);
        BuildAll();
    }

    protected override string WindowTitle { get { return L.T("MovieShelf Setup", "نصب MovieShelf"); } }

    protected override void MakePages()
    {
        Pages.Add(new WelcomePage());
        Pages.Add(new SystemPage());
        Pages.Add(new LicensePage());
        Pages.Add(new LocationPage());
        Pages.Add(new ComponentsPage());
        Pages.Add(new ShortcutsPage());
        Pages.Add(new ReadyPage());
        Pages.Add(new ProgressPage());
        Pages.Add(new FinishPage());
    }

    /// <summary>Asks the servers for the real sizes, once, when the connection is known to work.</summary>
    public void FetchSizes()
    {
        if (SizesFetched || Online != true || Demo) return;
        SizesFetched = true;
        var t = new Thread(() =>
        {
            foreach (ImdbFile f in Sizes.Imdb)
            {
                long n = Downloader.Head(Env.ImdbBase + f.FileName, 8000);
                if (n > 0) f.Size = n;
            }
            long z = Downloader.Head(Env.FfmpegUrl, 8000);
            if (z > 0) Sizes.FfmpegZip = z;
            try
            {
                BeginInvoke((Action)(() =>
                {
                    var p = Current as ComponentsPage;
                    if (p != null) p.Refresh();
                }));
            }
            catch (InvalidOperationException) { }
        });
        t.IsBackground = true;
        t.Start();
    }

    public void SwitchLanguage(bool fa)
    {
        if (L.Fa == fa) return;
        L.Fa = fa;
        Ui.Rtl = fa;
        BuildAll();
    }

    protected override void OnCancelPressed()
    {
        var progress = Current as ProgressPage;
        if (progress != null)
        {
            if (Prompt.Ask(this, L.T("Cancel the installation?", "نصب لغو شود؟"),
                C.Upgrade ? L.T("Setup will stop and restore the version you had before.", "نصب متوقف می‌شود و نسخه قبلی بازگردانده می‌شود.")
                          : L.T("Setup will stop and remove everything it has written so far.", "نصب متوقف می‌شود و هر چیزی که تا اینجا نوشته شده حذف می‌شود."),
                L.T("Cancel the install", "لغو نصب"), L.T("Keep installing", "ادامه نصب"), true))
                progress.Stop();
            return;
        }
        if (Prompt.Ask(this, L.T("Cancel setup?", "نصب لغو شود؟"), L.T("MovieShelf has not been installed yet. Nothing on your computer has changed.", "MovieShelf هنوز نصب نشده است و چیزی در رایانه شما تغییر نکرده است."),
            L.T("Cancel setup", "لغو نصب"), L.T("Continue", "ادامه"), true))
        {
            CloseWithoutAsking = true;
            Code = Exit.Cancelled;
            Close();
        }
    }
}

class WelcomePage : Page
{
    InstallWizard I { get { return (InstallWizard)W; } }
    Card upgrade, uninstall;

    public override string Id { get { return "welcome"; } }
    public override string SideName { get { return L.T("Welcome", "خوش آمدید"); } }
    public override string Title { get { return L.T("Welcome to MovieShelf", "به MovieShelf خوش آمدید"); } }
    public override string Sub { get { return I.Prev != null ? L.T("MovieShelf is already on this computer.", "MovieShelf روی این رایانه نصب است.") : L.T("This wizard installs MovieShelf on your computer.", "این راهنما MovieShelf را روی رایانه شما نصب می‌کند."); } }
    public override string NextText { get { return I.Uninstalling ? L.T("&Uninstall", "حذف") : base.NextText; } }

    public override void Build()
    {
        int y = 2;
        Add(new Picture { Image = Ui.Logo }, 0, y, 76, 76);
        int ty = 4;
        Text("MovieShelf", 92, ref ty, 460, 22, true, Theme.Ink, 0);
        Text(Fmt.F(L.T("Version {0}, for Windows 10 and Windows 11", "نسخه {0}، برای ویندوز ۱۰ و ویندوز ۱۱"), Fmt.Num(BuildInfo.Version)), 92, ref ty, 460, 13, false, Theme.Muted, 2);
        y = 92;
        Text(L.T("Your movie collection in one fast window: posters, IMDb ratings, search and filters, and your own watched list and notes.",
                 "مجموعه فیلم‌های شما در یک پنجره سریع: پوستر، امتیاز IMDb، جستجو و فیلتر، و فهرست دیده‌شده‌ها و یادداشت‌های خودتان."), 0, ref y, 560, 14, false, Theme.Ink, 16);

        Text(L.T("Language", "زبان"), 0, ref y, 200, 13, true, Theme.Ink, 6);
        var lang = new List<Card>();
        Card en = Add(new Card { Title = "English", Group = lang, Compact = true }, 0, y, 170, 42);
        Card fa = Add(new Card { Title = "فارسی", Group = lang, Compact = true }, 182, y, 170, 42);
        lang.Add(en); lang.Add(fa);
        (L.Fa ? fa : en).Checked = true;
        en.Picked += (o, e) => I.SwitchLanguage(false);
        fa.Picked += (o, e) => I.SwitchLanguage(true);
        y += 58;

        if (I.Prev == null) return;
        Text(Fmt.F(L.T("Installed: version {0} in {1}\r\nThis setup: version {2}", "نصب‌شده: نسخه {0} در {1}\r\nاین نصب‌کننده: نسخه {2}"),
            Fmt.Num(I.Prev.Version.Length > 0 ? I.Prev.Version : "?"), Fmt.Ltr(I.Prev.Dir), Fmt.Num(BuildInfo.Version)), 0, ref y, 560, 13, false, Theme.Muted, 8);
        Version have, ours;
        if (Version.TryParse(I.Prev.Version, out have) && Version.TryParse(BuildInfo.Version, out ours) && have > ours)
            Text(L.T("The installed version is newer than this setup. Going on replaces it with the older one.", "نسخه نصب‌شده از این نصب‌کننده جدیدتر است. با ادامه، نسخه قدیمی‌تر جای آن را می‌گیرد."), 0, ref y, 560, 13, false, Theme.Warn, 8);
        var mode = new List<Card>();
        upgrade = Add(new Card { Title = L.T("Upgrade or repair", "به‌روزرسانی یا تعمیر"), Desc = L.T("Keeps your location, choices and library.", "محل نصب و کتابخانه حفظ می‌شود."), Group = mode }, 0, y, 276, 66);
        uninstall = Add(new Card { Title = L.T("Uninstall", "حذف MovieShelf"), Desc = L.T("You choose what stays.", "شما انتخاب می‌کنید چه چیزی بماند."), Group = mode }, 284, y, 276, 66);
        mode.Add(upgrade); mode.Add(uninstall);
        (I.Uninstalling ? uninstall : upgrade).Checked = true;
        upgrade.Picked += (o, e) => { I.Uninstalling = false; W.UpdateButtons(); };
        uninstall.Picked += (o, e) => { I.Uninstalling = true; W.UpdateButtons(); };
    }

    public override void Set(string key, string value)
    {
        if (key == "mode") (value == "uninstall" ? uninstall : upgrade).Pick();
        else base.Set(key, value);
    }

    public override bool Leave()
    {
        if (!I.Uninstalling) return true;
        string un = Path.Combine(I.Prev.Dir, "uninstall.exe");
        try
        {
            if (File.Exists(un)) Process.Start(new ProcessStartInfo(un) { UseShellExecute = true, WorkingDirectory = Env.Temp });
            else Process.Start(new ProcessStartInfo("cmd.exe", "/c " + I.Prev.UninstallString) { UseShellExecute = false, CreateNoWindow = true });
            W.Code = Exit.Ok;
            W.CloseWithoutAsking = true;
            W.Close();
        }
        catch (Exception e) { Prompt.Ask(W, L.T("Uninstall", "حذف"), e.Message, L.T("OK", "باشه"), L.T("Close", "بستن"), true); }
        return false;
    }
}

class SystemPage : Page
{
    InstallWizard I { get { return (InstallWizard)W; } }
    StatusRow winRow, bitRow, diskRow, ramRow, netRow, runRow, libRow, pyRow;
    FlatBtn closeBtn;
    int generation;

    public override string Id { get { return "system"; } }
    public override string SideName { get { return L.T("System check", "بررسی سیستم"); } }
    public override string Title { get { return L.T("System check", "بررسی سیستم"); } }
    public override string Sub { get { return L.T("Setup makes sure this computer is ready for MovieShelf.", "نصب‌کننده بررسی می‌کند این رایانه برای MovieShelf آماده باشد."); } }

    public override bool CanNext
    {
        get
        {
            foreach (StatusRow r in new[] { winRow, bitRow, diskRow, ramRow })
                if (r == null || r.State == Mark.Fail || r.State == Mark.Pending) return false;
            return true;
        }
    }

    StatusRow Row(string title, int i) { return Add(new StatusRow { Title = title }, 0, i * 42, AreaW, 40); }

    public override void Build()
    {
        winRow = Row(L.T("Windows version", "نسخه ویندوز"), 0);
        bitRow = Row(L.T("64-bit Windows", "ویندوز ۶۴ بیتی"), 1);
        diskRow = Row(L.T("Free disk space", "فضای خالی دیسک"), 2);
        ramRow = Row(L.T("Memory", "حافظه"), 3);
        netRow = Row(L.T("Internet connection", "اتصال اینترنت"), 4);
        runRow = Row(L.T("MovieShelf running", "اجرای MovieShelf"), 5);
        libRow = Row(L.T("Existing library", "کتابخانه موجود"), 6);
        pyRow = Row(L.T("Older MovieShelf (Python)", "نسخه قدیمی MovieShelf (پایتون)"), 7);
        runRow.Reserve = Ui.S(128);
        closeBtn = Add(new FlatBtn { Text = L.T("Close &MovieShelf", "بستن MovieShelf"), Visible = false }, AreaW - 120, 5 * 42 + 4, 120, 32);
        closeBtn.Click += (o, e) => CloseApp();
        var again = Add(new FlatBtn { Text = L.T("Check &again", "بررسی دوباره") }, 0, 8 * 42 + 4, 130, 32);
        again.Click += (o, e) => Run();
    }

    public override void Enter() { Run(); }

    void CloseApp()
    {
        closeBtn.Enabled = false;
        string folder = I.C.InstallDir;
        var t = new Thread(() =>
        {
            Apps.Close(folder, 6000);
            W.BeginInvoke((Action)(() => { closeBtn.Enabled = true; Run(); }));
        });
        t.IsBackground = true;
        t.Start();
    }

    public void Demo(bool bad)
    {
        generation++;
        winRow.Set(bad ? Mark.Fail : Mark.Pass, bad ? Fmt.F(L.T("MovieShelf needs build {0} or later. This computer has build {1}.", "MovieShelf به بیلد {0} یا بالاتر نیاز دارد. این رایانه بیلد {1} دارد."), Fmt.Num(17763), Fmt.Num(17134)) : DescribeWindows(26300));
        bitRow.Set(Mark.Pass, L.T("This is a 64-bit system.", "این سیستم ۶۴ بیتی است."));
        diskRow.Set(bad ? Mark.Fail : Mark.Pass, bad ? Fmt.F(L.T("{0} free, {1} needed.", "{0} آزاد است و {1} لازم است."), Fmt.Size(90L << 20), Fmt.Size(170L << 20)) : Fmt.F(L.T("{0} free, {1} needed.", "{0} آزاد است و {1} لازم است."), Fmt.Size(241L << 30), Fmt.Size(170L << 20)));
        ramRow.Set(bad ? Mark.Warn : Mark.Pass, bad ? L.T("3 GB. MovieShelf runs, but large libraries will be slow.", "۳ گیگابایت. MovieShelf اجرا می‌شود، اما کتابخانه‌های بزرگ کند خواهند بود.") : Fmt.Size(16L << 30));
        netRow.Set(bad ? Mark.Warn : Mark.Pass, bad ? NoNet() : L.T("Connected.", "متصل است."));
        runRow.Set(bad ? Mark.Warn : Mark.Pass, bad ? L.T("Running from the install folder. Setup can close it.", "از پوشه نصب اجرا شده است. نصب‌کننده می‌تواند آن را ببندد.") : L.T("Not running.", "در حال اجرا نیست."));
        closeBtn.Visible = bad;
        libRow.Set(Mark.Info, Fmt.F(L.T("{0} found. It stays as it is.", "{0} پیدا شد و دست‌نخورده می‌ماند."), Fmt.Size(3400L << 20)));
        pyRow.Set(Mark.Info, L.T("Found. Both versions use the same data folder.", "پیدا شد. هر دو نسخه از یک پوشه داده استفاده می‌کنند."));
        W.UpdateButtons();
    }

    static string NoNet() { return L.T("No connection. Installing works, the downloads do not.", "اتصالی نیست. نصب انجام می‌شود، اما دانلودها نه."); }

    static string DescribeWindows(int build)
    {
        string name = build >= 22000 ? L.T("Windows 11", "ویندوز ۱۱") : L.T("Windows 10", "ویندوز ۱۰");
        return name + L.T(", build ", "، بیلد ") + Fmt.Num(build);
    }

    public void Run()
    {
        if (I.Demo) return;
        int gen = ++generation;
        string wait = L.T("Checking…", "در حال بررسی…");
        netRow.Set(Mark.Pending, wait);
        libRow.Set(Mark.Pending, wait);
        pyRow.Set(Mark.Pending, wait);

        int build = Native.WindowsBuild();
        winRow.Set(build >= 17763 ? Mark.Pass : Mark.Fail, build >= 17763 ? DescribeWindows(build)
            : Fmt.F(L.T("MovieShelf needs build {0} or later. This computer has build {1}.", "MovieShelf به بیلد {0} یا بالاتر نیاز دارد. این رایانه بیلد {1} دارد."), Fmt.Num(17763), Fmt.Num(build)));
        bool is64 = Environment.Is64BitOperatingSystem && Env.Test("NOT64") == null;
        bitRow.Set(is64 ? Mark.Pass : Mark.Fail, is64 ? L.T("This is a 64-bit system.", "این سیستم ۶۴ بیتی است.") : L.T("MovieShelf runs on 64-bit Windows only.", "MovieShelf فقط روی ویندوز ۶۴ بیتی اجرا می‌شود."));

        long need = Sizes.OnInstallDrive(I.C, I.AppBytes, I.DataDir), free = Native.FreeBytes(I.C.InstallDir);
        diskRow.Set(free < 0 ? Mark.Fail : free < need ? Mark.Fail : Mark.Pass, free < 0 ? L.T("The drive could not be read.", "درایو خوانده نشد.")
            : Fmt.F(L.T("{0} free, {1} needed.", "{0} آزاد است و {1} لازم است."), Fmt.Size(free), Fmt.Size(need)));

        long ram = Native.TotalMemory();
        ramRow.Set(ram < (4L << 30) - (64L << 20) ? Mark.Warn : Mark.Pass, Fmt.Size(ram) + (ram < (4L << 30) - (64L << 20) ? L.T(". MovieShelf runs, but large libraries will be slow.", ". MovieShelf اجرا می‌شود، اما کتابخانه‌های بزرگ کند خواهند بود.") : ""));

        bool ours = Directory.Exists(I.C.InstallDir) && Apps.Running(I.C.InstallDir).Count > 0;
        bool other = !ours && Apps.Running(null).Count > 0;
        runRow.Set(ours ? Mark.Warn : other ? Mark.Info : Mark.Pass, ours ? L.T("Running from the install folder. Setup can close it.", "از پوشه نصب اجرا شده است. نصب‌کننده می‌تواند آن را ببندد.")
            : other ? L.T("A copy runs from another folder. Setup leaves it alone.", "نسخه‌ای از پوشه دیگری اجرا شده است و نصب‌کننده با آن کاری ندارد.") : L.T("Not running.", "در حال اجرا نیست."));
        closeBtn.Visible = ours;
        W.UpdateButtons();

        string dataDir = I.DataDir, root = Path.GetDirectoryName(dataDir);
        var t = new Thread(() =>
        {
            bool online = Env.Test("OFFLINE") == null && Downloader.Head(Env.ImdbBase + "title.ratings.tsv.gz", 6000) >= 0;
            Ui_(gen, () =>
            {
                I.Online = online;
                netRow.Set(online ? Mark.Pass : Mark.Warn, online ? L.T("Connected.", "متصل است.") : NoNet());
                I.FetchSizes();
            });
            long size = Files.Size(dataDir);
            bool hasDb = File.Exists(Path.Combine(dataDir, "library.db"));
            Ui_(gen, () =>
            {
                if (size > 0 && hasDb) libRow.Set(Mark.Info, Fmt.F(L.T("{0} found. It stays as it is.", "{0} پیدا شد و دست‌نخورده می‌ماند."), Fmt.Size(size)));
                else libRow.Set(Mark.Pass, L.T("None yet. MovieShelf creates one on first start.", "هنوز کتابخانه‌ای نیست. MovieShelf در اولین اجرا آن را می‌سازد."));
            });
            bool python = Directory.Exists(Path.Combine(root, "venv"));
            Ui_(gen, () => pyRow.Set(python ? Mark.Info : Mark.Pass, python ? L.T("Found. Both versions use the same data folder.", "پیدا شد. هر دو نسخه از یک پوشه داده استفاده می‌کنند.") : L.T("Not found.", "پیدا نشد.")));
        });
        t.IsBackground = true;
        t.Start();
    }

    void Ui_(int gen, Action a)
    {
        try { W.BeginInvoke((Action)(() => { if (gen == generation) a(); })); }
        catch (InvalidOperationException) { }
    }
}

class LicensePage : Page
{
    CheckBox accept;

    public override string Id { get { return "license"; } }
    public override string SideName { get { return L.T("License", "مجوز"); } }
    public override string Title { get { return L.T("License agreement", "توافق‌نامه مجوز"); } }
    public override string Sub { get { return L.T("Please read these terms. They include the terms of the data MovieShelf uses.", "لطفاً این شرایط را بخوانید. شرایط داده‌هایی که MovieShelf استفاده می‌کند نیز در آن آمده است."); } }
    public override bool CanNext { get { return accept != null && accept.Checked; } }

    public override void Build()
    {
        var box = new TextBox
        {
            Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, BackColor = Theme.Wash, ForeColor = Theme.Ink, BorderStyle = BorderStyle.FixedSingle,
            Font = Ui.Font(13, false), Text = Ui.Resource(L.Fa ? "license.fa.txt" : "license.en.txt").Replace("\r\n", "\n").Replace("\n", "\r\n"), TabStop = true
        };
        box.Enter += (o, e) => box.BeginInvoke((Action)box.DeselectAll);
        Add(box, 0, 0, AreaW, 348);
        accept = Check(L.T("I &accept the terms of this agreement", "شرایط این توافق‌نامه را می‌پذیرم"), 0, 358, AreaW, false);
        accept.CheckedChanged += (o, e) => W.UpdateButtons();
    }

    public override void Set(string key, string value) { if (key == "accept") accept.Checked = value == "1"; else base.Set(key, value); }
    public override bool Shown { get { return true; } }
}

class LocationPage : Page
{
    InstallWizard I { get { return (InstallWizard)W; } }
    Card just, all;
    TextBox path;
    FlatBtn browse;
    CheckBox portable;
    TextView target, space, message;
    PathCheck result;

    public override string Id { get { return "location"; } }
    public override string SideName { get { return L.T("Install location", "محل نصب"); } }
    public override string Title { get { return L.T("Install location", "محل نصب"); } }
    public override string Sub { get { return L.T("Choose who gets MovieShelf and where it goes.", "انتخاب کنید MovieShelf برای چه کسانی و کجا نصب شود."); } }
    public override bool Shown { get { return !I.C.Upgrade; } }
    public override bool CanNext { get { return result != null && result.Error == null; } }

    public override void Build()
    {
        var group = new List<Card>();
        just = Add(new Card { Title = L.T("Just for me", "فقط برای من"), Desc = L.T("No administrator rights needed.", "به دسترسی مدیر سیستم نیاز ندارد."), Group = group }, 0, 0, 276, 66);
        all = Add(new Card { Title = L.T("For all users", "برای همه کاربران"), Desc = L.T("Needs administrator rights.", "به دسترسی مدیر سیستم نیاز دارد."), Shield = true, Group = group }, 284, 0, 276, 66);
        group.Add(just); group.Add(all);
        (I.C.AllUsers ? all : just).Checked = true;
        just.Picked += (o, e) => Scope(false);
        all.Picked += (o, e) => Scope(true);

        int y = 80;
        Text(L.T("Folder", "پوشه"), 0, ref y, 200, 13, true, Theme.Ink, 4);
        path = Add(new TextBox { Font = Ui.Font(13, false), BorderStyle = BorderStyle.FixedSingle, RightToLeft = RightToLeft.No, Text = I.C.InstallDir }, 0, y + 2, 452, 26);
        browse = Add(new FlatBtn { Text = L.T("B&rowse…", "انتخاب…") }, 460, y, 100, 32);
        browse.Click += (o, e) => Browse();
        path.TextChanged += (o, e) => Validate();
        y += 40;
        target = Add(new TextView { Font = Ui.Font(12, false), ForeColor = Theme.Muted }, 0, y, AreaW, 20);
        y += 22;
        space = Add(new TextView { Font = Ui.Font(12, false), ForeColor = Theme.Muted }, 0, y, AreaW, 20);
        y += 28;
        portable = Check(L.T("&Portable: keep the library in the program folder", "قابل حمل: کتابخانه در پوشه برنامه نگه داشته شود"), 0, y, AreaW, I.C.Portable);
        portable.CheckedChanged += (o, e) => { I.C.Portable = portable.Checked; Validate(); };
        y += 34;
        message = Add(new TextView { Font = Ui.Font(13, false), ForeColor = Theme.Warn }, 0, y, AreaW, 72);
    }

    public override void Enter()
    {
        Validate();
        if (I.LocationMessage != null) { message.Text = I.LocationMessage; message.ForeColor = Theme.Fail; I.LocationMessage = null; }
    }

    void Scope(bool allUsers)
    {
        string other = Env.DefaultDir(!allUsers);
        I.C.AllUsers = allUsers;
        if (Files.Same(path.Text.Trim(), other)) path.Text = Env.DefaultDir(allUsers);
        if (allUsers) portable.Checked = false;
        Validate();
    }

    void Browse()
    {
        using (var d = new FolderBrowserDialog { Description = L.T("Choose a folder. MovieShelf gets its own folder inside it.", "پوشه‌ای انتخاب کنید. MovieShelf پوشه خودش را در آن می‌سازد."), ShowNewFolderButton = true })
        {
            string near = Files.NearestExisting(path.Text);
            if (near != null) d.SelectedPath = near;
            if (d.ShowDialog(W) == DialogResult.OK) path.Text = d.SelectedPath;
        }
    }

    public void Validate()
    {
        result = PathRules.Check(path.Text, I.C.AllUsers, I.C.Portable);
        bool writable = result.Error == null ? Files.CanWrite(result.Final) : true;
        portable.Enabled = !I.C.AllUsers && writable;
        if (result.Final != null)
        {
            I.C.InstallDir = result.Final;
            target.Text = L.T("MovieShelf goes in: ", "محل نصب MovieShelf: ") + Fmt.Ltr(result.Final);
            long need = Sizes.OnInstallDrive(I.C, I.AppBytes, I.DataDir), free = Native.FreeBytes(result.Final);
            space.Text = Fmt.F(L.T("Required: {0}    Available: {1}", "لازم: {0}    آزاد: {1}"), Fmt.Size(need), free < 0 ? "?" : Fmt.Size(free));
            if (result.Error == null && free >= 0 && free < need)
                result.Error = L.T("There is not enough free space on this drive. Choose another drive, or fewer components.", "فضای خالی این درایو کافی نیست. درایو دیگری یا اجزای کمتری انتخاب کنید.");
        }
        else { target.Text = ""; space.Text = ""; }
        message.Text = result.Error ?? result.Warning ?? "";
        message.ForeColor = result.Error != null ? Theme.Fail : Theme.Warn;
        W.UpdateButtons();
    }

    public override void Set(string key, string value)
    {
        if (key == "path") path.Text = value;
        else if (key == "scope") (value == "all" ? all : just).Pick();
        else if (key == "portable") portable.Checked = value == "1";
        else base.Set(key, value);
    }

    public string MessageText { get { return message.Text; } }
}

class ComponentsPage : Page
{
    InstallWizard I { get { return (InstallWizard)W; } }
    class Item { public CheckBox Box; public TextView Size; public string Desc; public string Id; }
    readonly List<Card> types = new List<Card>();
    readonly List<Item> items = new List<Item>();
    TextView desc, totals, offline;
    bool updating;

    public override string Id { get { return "components"; } }
    public override string SideName { get { return L.T("Components", "اجزا"); } }
    public override string Title { get { return L.T("Components", "اجزای نصب"); } }
    public override string Sub { get { return L.T("Pick a setup type, or choose the parts yourself.", "نوع نصب را انتخاب کنید یا اجزا را خودتان مشخص کنید."); } }
    public override bool Shown { get { return !I.C.Upgrade; } }

    Card Type(string id, string title, int x)
    {
        Card c = Add(new Card { Title = title, Group = types, Compact = true, Tag = id }, x, 0, 134, 42);
        types.Add(c);
        c.Picked += (o, e) => ApplyType(id);
        return c;
    }

    Item Row(string id, string title, string text, int y, bool required)
    {
        var it = new Item { Id = id, Desc = text };
        it.Box = Check(title, 0, y, 410, true);
        it.Box.Enabled = !required;
        it.Size = Add(new TextView { Font = Ui.Font(13, false), ForeColor = Theme.Muted }, 416, y + 4, 144, 20);
        it.Box.CheckedChanged += (o, e) => Changed();
        it.Box.Enter += (o, e) => desc.Text = it.Desc;
        it.Box.MouseEnter += (o, e) => desc.Text = it.Desc;
        items.Add(it);
        return it;
    }

    public override void Build()
    {
        Type("typical", L.T("Typical", "معمولی"), 0);
        Type("minimal", L.T("Minimal", "حداقلی"), 142);
        Type("full", L.T("Full", "کامل"), 284);
        Type("custom", L.T("Custom", "سفارشی"), 426);

        int y = 56;
        Row("app", L.T("MovieShelf application", "برنامه MovieShelf"), L.T("The program itself. Always installed.", "خود برنامه. همیشه نصب می‌شود."), y, true);
        Row("imdb", L.T("IMDb datasets, downloaded now", "داده‌های IMDb، همین حالا دانلود شود"), L.T("Six files from IMDb, used for matching titles, cast and ratings. Without them, MovieShelf downloads the same files the first time you press Update.", "شش فایل از IMDb برای تطبیق عنوان‌ها، بازیگران و امتیازها. اگر انتخاب نکنید، MovieShelf همین فایل‌ها را در اولین به‌روزرسانی دانلود می‌کند."), y + 32, false);
        Row("ffprobe", L.T("ffprobe, for reading video quality", "ffprobe، برای خواندن کیفیت ویدیو"), L.T("Reads the real resolution and audio languages of your video files. Downloaded from gyan.dev and checked against its SHA-256.", "وضوح و زبان‌های صوتی واقعی فایل‌های ویدیویی را می‌خواند. از gyan.dev دانلود و با SHA-256 آن بررسی می‌شود."), y + 64, false);

        Host.Controls.Add(new Panel { BackColor = Theme.Line, Bounds = new Rectangle(0, Ui.S(y + 102), Host.Width, 1) });
        int ty = y + 112;
        desc = Add(new TextView { Font = Ui.Font(13, false), ForeColor = Theme.Muted, Text = items[1].Desc }, 0, ty, AreaW, 62);
        ty += 66;
        offline = Add(new TextView { Font = Ui.Font(13, false), ForeColor = Theme.Warn, Text = "" }, 0, ty, AreaW, 42);
        ty += 46;
        totals = Add(new TextView { Font = Ui.Font(14, true), ForeColor = Theme.Ink }, 0, ty, AreaW, 24);
    }

    bool Allowed { get { return I.Online != false; } }

    public override void Enter()
    {
        updating = true;
        foreach (Card c in types) c.Checked = (string)c.Tag == I.C.SetupType;
        Item imdb = items[1], probe = items[2];
        imdb.Box.Checked = I.C.Imdb;
        probe.Box.Checked = I.C.Ffprobe;
        updating = false;
        imdb.Box.Enabled = probe.Box.Enabled = Allowed;
        foreach (Card c in types) if ((string)c.Tag == "full") c.Enabled = Allowed;
        offline.Text = Allowed ? "" : L.T("There is no internet connection, so the downloads are turned off. MovieShelf can download the IMDb data later, when you press Update.", "اتصال اینترنت برقرار نیست، بنابراین دانلودها غیرفعال است. MovieShelf می‌تواند داده‌های IMDb را بعداً هنگام به‌روزرسانی دانلود کند.");
        if (!Allowed) { I.C.Imdb = I.C.Ffprobe = false; imdb.Box.Checked = probe.Box.Checked = false; }
        Refresh();
    }

    void ApplyType(string id)
    {
        if (updating) return;
        I.C.SetupType = id;
        updating = true;
        if (id != "custom")
        {
            bool full = id == "full" && Allowed;
            items[1].Box.Checked = full;
            items[2].Box.Checked = full;
            I.C.Imdb = I.C.Ffprobe = full;
        }
        updating = false;
        Refresh();
    }

    void Changed()
    {
        if (updating) return;
        I.C.Imdb = items[1].Box.Checked;
        I.C.Ffprobe = items[2].Box.Checked;
        I.C.SetupType = "custom";
        updating = true;
        foreach (Card c in types) c.Checked = (string)c.Tag == "custom";
        updating = false;
        Refresh();
    }

    public void Refresh()
    {
        string dataDir = I.DataDir;
        items[0].Size.Text = Fmt.Size(I.AppBytes);
        long imdb = Sizes.ImdbToFetch(dataDir);
        items[1].Size.Text = imdb == 0 ? L.T("Up to date", "به‌روز است") : Fmt.Size(imdb);
        items[2].Size.Text = Fmt.Size(Sizes.FfmpegZip);
        long download = Sizes.Download(I.C, dataDir);
        long disk = I.AppBytes + (I.C.Ffprobe ? Sizes.FfprobeOnDisk : 0) + (I.C.Imdb ? imdb : 0);
        totals.Text = Fmt.F(L.T("Download: {0}     Disk space: {1}", "دانلود: {0}     فضای دیسک: {1}"), download == 0 ? L.T("none", "ندارد") : Fmt.Size(download), Fmt.Size(disk));
    }

    public string Totals { get { return totals.Text; } }

    public override void Set(string key, string value)
    {
        if (key == "type") types.Find(c => (string)c.Tag == value).Pick();
        else if (key == "imdb") items[1].Box.Checked = value == "1";
        else if (key == "ffprobe") items[2].Box.Checked = value == "1";
        else base.Set(key, value);
    }
}

class ShortcutsPage : Page
{
    InstallWizard I { get { return (InstallWizard)W; } }
    CheckBox start, desktop;
    TextView note;
    bool minimalApplied;

    public override string Id { get { return "shortcuts"; } }
    public override string SideName { get { return L.T("Shortcuts", "میانبرها"); } }
    public override string Title { get { return L.T("Shortcuts", "میانبرها"); } }
    public override string Sub { get { return L.T("Choose how you want to open MovieShelf.", "انتخاب کنید MovieShelf را چگونه باز کنید."); } }
    public override bool Shown { get { return !I.C.Upgrade; } }

    public override void Build()
    {
        int y = 0;
        start = Check(L.T("&Start menu shortcut", "میانبر در منوی Start"), 0, y, AreaW, I.C.StartMenu);
        y += 28;
        Text(L.T("MovieShelf appears in the list of apps in the Start menu.", "MovieShelf در فهرست برنامه‌های منوی Start نشان داده می‌شود."), 22, ref y, 530, 12, false, Theme.Muted, 18);
        desktop = Check(L.T("&Desktop shortcut", "میانبر روی دسکتاپ"), 0, y, AreaW, I.C.Desktop);
        y += 28;
        Text(L.T("An icon on your desktop. You can delete it at any time.", "یک نماد روی دسکتاپ شما. هر زمان بخواهید می‌توانید آن را حذف کنید."), 22, ref y, 530, 12, false, Theme.Muted, 18);
        note = Add(new TextView { Font = Ui.Font(13, false), ForeColor = Theme.Muted }, 0, y, AreaW, 66);
        start.CheckedChanged += (o, e) => I.C.StartMenu = start.Checked;
        desktop.CheckedChanged += (o, e) => I.C.Desktop = desktop.Checked;
    }

    public override void Enter()
    {
        if (I.C.SetupType == "minimal" && !minimalApplied) { minimalApplied = true; desktop.Checked = false; }
        string exe = Path.Combine(I.C.InstallDir, "MovieShelf.exe");
        bool taken = false;
        foreach (string dir in new[] { Shell.ProgramsDir(I.C.AllUsers), Shell.DesktopDir(I.C.AllUsers) })
            if (Path.GetFileName(Shell.PathFor(dir, exe, null)) == Shell.OtherLinkName) taken = true;
        note.Text = taken ? L.T("A shortcut named MovieShelf already opens something else. It stays as it is, and this one is named MovieShelf (native).", "میانبری با نام MovieShelf چیز دیگری را باز می‌کند. آن میانبر دست‌نخورده می‌ماند و نام میانبر جدید MovieShelf (native) خواهد بود.") : "";
    }

    public override void Set(string key, string value)
    {
        if (key == "desktop") desktop.Checked = value == "1";
        else if (key == "startmenu") start.Checked = value == "1";
        else base.Set(key, value);
    }
}

class ReadyPage : Page
{
    InstallWizard I { get { return (InstallWizard)W; } }
    Summary sum;
    TextView warning;
    bool enough = true;

    public override string Id { get { return "ready"; } }
    public override string SideName { get { return L.T("Ready", "آماده نصب"); } }
    public override string Title { get { return L.T("Ready to install", "آماده نصب"); } }
    public override string Sub { get { return L.T("Check your choices, then press Install.", "انتخاب‌های خود را بررسی کنید و سپس نصب را بزنید."); } }
    public override string NextText { get { return I.C.Upgrade ? L.T("&Upgrade", "به‌روزرسانی") : L.T("&Install", "نصب"); } }
    public override bool NextShield { get { return I.C.AllUsers && !Env.IsAdmin; } }
    public override bool CanNext { get { return enough; } }

    public override void Build()
    {
        sum = Add(new Summary { LabelWidth = 140 }, 0, 0, AreaW, 340);
        warning = Add(new TextView { Font = Ui.Font(13, false), ForeColor = Theme.Fail }, 0, 346, AreaW, 44);
    }

    public override void Enter()
    {
        Choices c = I.C;
        sum.Clear();
        sum.AccessibleName = "";
        string action = !c.Upgrade ? L.T("New install", "نصب جدید")
            : string.Equals(c.OldVersion, BuildInfo.Version, StringComparison.OrdinalIgnoreCase) ? Fmt.F(L.T("Repair version {0}", "تعمیر نسخه {0}"), Fmt.Num(BuildInfo.Version))
            : Fmt.F(L.T("Upgrade from {0} to {1}", "به‌روزرسانی از {0} به {1}"), Fmt.Num(c.OldVersion), Fmt.Num(BuildInfo.Version));
        sum.Add(L.T("Action", "عملیات"), action);
        sum.Add(L.T("Installed for", "نصب برای"), c.AllUsers ? L.T("All users", "همه کاربران") : L.T("Just for me", "فقط برای من"));
        sum.Add(L.T("Location", "محل نصب"), Fmt.Ltr(c.InstallDir) + (c.Portable ? L.T(" (portable)", " (قابل حمل)") : ""));
        sum.Add(L.T("Library", "کتابخانه"), Fmt.Ltr(I.DataDir));
        var parts = new List<string> { L.T("MovieShelf application", "برنامه MovieShelf") };
        if (c.Imdb) parts.Add(L.T("IMDb datasets", "داده‌های IMDb"));
        if (c.Ffprobe || c.KeepFfprobe) parts.Add("ffprobe");
        sum.Add(L.T("Components", "اجزا"), string.Join(L.T(", ", "، "), parts.ToArray()));
        var links = new List<string>();
        if (c.StartMenu) links.Add(L.T("Start menu", "منوی Start"));
        if (c.Desktop) links.Add(L.T("Desktop", "دسکتاپ"));
        sum.Add(L.T("Shortcuts", "میانبرها"), links.Count > 0 ? string.Join(L.T(", ", "، "), links.ToArray()) : L.T("None", "ندارد"));
        long dl = Sizes.Download(c, I.DataDir);
        if (dl > 0) sum.Add(L.T("Download", "دانلود"), Fmt.Size(dl));
        long need = Sizes.OnInstallDrive(c, I.AppBytes, I.DataDir), free = Native.FreeBytes(c.InstallDir);
        sum.Add(L.T("Disk space", "فضای دیسک"), Fmt.F(L.T("{0} needed, {1} free", "{0} لازم، {1} آزاد"), Fmt.Size(need), Fmt.Size(free)));
        enough = free < 0 || free >= need;
        // IMDb data goes to the library, which may be on another drive.
        if (enough && c.Imdb && !Sizes.SameDrive(c.InstallDir, I.DataDir))
        {
            long dataFree = Native.FreeBytes(I.DataDir);
            enough = dataFree < 0 || dataFree >= Sizes.ImdbToFetch(I.DataDir);
        }
        warning.Text = enough ? "" : L.T("There is not enough free disk space. Go back and choose another drive, or fewer components.", "فضای خالی دیسک کافی نیست. برگردید و درایو دیگری یا اجزای کمتری انتخاب کنید.");
    }
}

class ProgressPage : Page, ISink
{
    InstallWizard I { get { return (InstallWizard)W; } }
    Bar bar;
    TextView percent, step, detail;
    ListBox list;
    LinkLabel toggle;
    System.Windows.Forms.Timer timer;
    readonly Queue<string> lines = new Queue<string>();
    volatile string stepText = "", detailText = "";
    double value;
    volatile bool lockedFlag;
    Thread worker;
    Runner runner;

    public override string Id { get { return "progress"; } }
    public override string SideName { get { return L.T("Installing", "نصب"); } }
    public override string Title { get { return I.C.Upgrade ? L.T("Upgrading MovieShelf", "در حال به‌روزرسانی MovieShelf") : L.T("Installing MovieShelf", "در حال نصب MovieShelf"); } }
    public override string Sub { get { return L.T("This may take a moment. You can keep using your computer.", "ممکن است کمی طول بکشد. می‌توانید به کار با رایانه ادامه دهید."); } }
    public override bool CanNext { get { return false; } }
    public override bool CanBack { get { return false; } }
    public override bool CanCancel { get { return !lockedFlag; } }

    public override void Build()
    {
        step = Add(new TextView { Font = Ui.Font(15, true), ForeColor = Theme.Ink }, 0, 4, 480, 26);
        percent = Add(new TextView { Font = Ui.Font(15, true), ForeColor = Theme.Ink, Far = true }, 480, 4, 88, 26);
        bar = Add(new Bar(), 0, 36, AreaW, 10);
        detail = Add(new TextView { Font = Ui.Font(12, false), ForeColor = Theme.Muted }, 0, 54, AreaW, 38);
        toggle = Link(L.T("Show details", "نمایش جزئیات"), 0, 98, 200);
        toggle.Click += (o, e) => ToggleLog();
        list = Add(new ListBox { Font = Ui.Font(12, false), BorderStyle = BorderStyle.FixedSingle, RightToLeft = RightToLeft.No, HorizontalScrollbar = true, Visible = false, BackColor = Theme.Wash, IntegralHeight = false }, 0, 126, AreaW, 262);
        timer = new System.Windows.Forms.Timer { Interval = 100 };
        timer.Tick += (o, e) => Pump();
    }

    public void Sample(double v, string s, string d, string[] log, bool showLog)
    {
        step.Text = s; detail.Text = d; bar.Value = v; percent.Text = Percent(v);
        list.Items.Clear();
        foreach (string l in log) list.Items.Add(l);
        list.Visible = showLog;
        toggle.Text = showLog ? L.T("Hide details", "پنهان کردن جزئیات") : L.T("Show details", "نمایش جزئیات");
    }

    static string Percent(double v)
    {
        string n = Fmt.Num(((int)(v * 100)).ToString(CultureInfo.InvariantCulture));
        return L.Fa ? "٪" + n : n + "%";
    }

    void ToggleLog()
    {
        list.Visible = !list.Visible;
        toggle.Text = list.Visible ? L.T("Hide details", "پنهان کردن جزئیات") : L.T("Show details", "نمایش جزئیات");
    }

    public override void Enter()
    {
        if (I.Demo || worker != null) return;
        lockedFlag = false;
        stepText = L.T("Getting ready", "در حال آماده‌سازی");
        string logPath = Path.Combine(Env.Temp, "MovieShelf-Setup-" + Process.GetCurrentProcess().Id + ".log");
        try { File.Delete(logPath); } catch (Exception) { }
        I.Log = new Logger(logPath);
        runner = new Runner(I.C, I.Sf, this, I.Log);
        I.Runner = runner;
        timer.Start();
        worker = new Thread(Work) { IsBackground = false };
        worker.Start();
    }

    void Work()
    {
        int code;
        try { code = runner.Run(); }
        catch (Exception e)
        {
            I.Log.Write("Failed: " + e);
            runner.Message = e.Message;
            code = Exit.Failed;
        }
        try { W.BeginInvoke((Action<int>)Done, code); }
        catch (InvalidOperationException) { }
    }

    void Done(int code)
    {
        timer.Stop();
        Pump();
        worker = null;
        if (runner.UacDeclined)
        {
            I.LocationMessage = runner.Message + " " + L.T("Choose \"Just for me\", or try again.", "«فقط برای من» را انتخاب کنید یا دوباره امتحان کنید.");
            W.GoTo(I.C.Upgrade ? "welcome" : "location");
            return;
        }
        I.ResultCode = code;
        I.ResultMessage = runner.Message;
        I.FailedDownloads = runner.Failed;
        I.ImdbFailed = runner.ImdbFailed;
        W.Code = code;
        W.GoTo("finish");
    }

    public void Stop()
    {
        if (runner == null) return;
        runner.Stop();
        lockedFlag = true;
        stepText = L.T("Stopping…", "در حال توقف…");
        W.UpdateButtons();
    }

    void Pump()
    {
        step.Text = stepText;
        detail.Text = detailText;
        bar.Value = value;
        percent.Text = Percent(value);
        lock (lines)
        {
            while (lines.Count > 0)
            {
                list.Items.Add(lines.Dequeue());
                list.TopIndex = list.Items.Count - 1;
            }
        }
        W.UpdateButtons();
    }

    public void Step(string text) { stepText = text; detailText = ""; }
    public void Detail(string text) { detailText = text; }
    public void Progress(double f) { value = f; }
    public void Locked(bool locked) { lockedFlag = locked; }

    public void Log(string line)
    {
        I.Log.Write(line);
        lock (lines) lines.Enqueue(line);
    }
}

class FinishPage : Page
{
    InstallWizard I { get { return (InstallWizard)W; } }
    MarkBox mark;
    TextView head, text;
    CheckBox run;
    LinkLabel log;

    bool Installed_ { get { return I.ResultCode == Exit.Ok || I.ResultCode == Exit.Partial; } }

    public override string Id { get { return "finish"; } }
    public override string SideName { get { return L.T("Finish", "پایان"); } }
    public override string Title { get { return I.ResultCode == Exit.Ok ? L.T("MovieShelf is ready", "MovieShelf آماده است") : I.ResultCode == Exit.Partial ? L.T("Installed, with a note", "نصب شد، با یک نکته") : I.ResultCode == Exit.Cancelled ? L.T("Setup was cancelled", "نصب لغو شد") : L.T("Setup could not finish", "نصب کامل نشد"); } }
    public override string Sub { get { return Installed_ ? (I.C.Upgrade ? L.T("The upgrade is complete.", "به‌روزرسانی انجام شد.") : L.T("The installation is complete.", "نصب انجام شد.")) : ""; } }
    public override bool CanBack { get { return false; } }
    public override bool HidesCancel { get { return true; } }
    public override string NextText { get { return L.T("&Finish", "پایان"); } }

    public override void Build()
    {
        mark = Add(new MarkBox(), 0, 4, 44, 44);
        head = Add(new TextView { Font = Ui.Font(15, true), ForeColor = Theme.Ink }, 60, 12, 500, 26);
        text = Add(new TextView { Font = Ui.Font(13, false), ForeColor = Theme.Muted }, 60, 54, 500, 200);
        run = Check(L.T("&Run MovieShelf", "اجرای MovieShelf"), 60, 200, 300, true);
        log = Link(L.T("Open the install log", "باز کردن گزارش نصب"), 60, 240, 300);
        log.Click += (o, e) => OpenLog();
    }

    string LogPath
    {
        get
        {
            string own = Path.Combine(I.C.InstallDir, Committer.LogName);
            if (Installed_ && File.Exists(own)) return own;
            return I.Log != null ? I.Log.Path : null;
        }
    }

    void OpenLog()
    {
        string p = LogPath;
        if (p == null || !File.Exists(p)) return;
        try { Process.Start(new ProcessStartInfo(p) { UseShellExecute = true }); } catch (Exception) { }
    }

    public override void Enter()
    {
        mark.State = I.ResultCode == Exit.Ok ? Mark.Pass : I.ResultCode == Exit.Partial ? Mark.Warn : I.ResultCode == Exit.Cancelled ? Mark.Info : Mark.Fail;
        mark.Invalidate();
        run.Visible = Installed_;
        var b = new System.Text.StringBuilder();
        if (Installed_)
        {
            head.Text = Fmt.F(L.T("MovieShelf {0} is installed.", "MovieShelf نسخه {0} نصب شد."), Fmt.Num(BuildInfo.Version));
            b.Append(L.T("You can open it now, or at any time from the Start menu.", "اکنون می‌توانید آن را باز کنید، یا هر زمان از منوی Start."));
            if (I.FailedDownloads.Count > 0)
            {
                b.Append("\r\n\r\n").Append(L.T("These optional downloads did not complete:", "این دانلودهای اختیاری کامل نشدند:"));
                foreach (string f in I.FailedDownloads) b.Append("\r\n•  ").Append(f);
                if (I.ImdbFailed) b.Append("\r\n\r\n").Append(L.T("That is fine: MovieShelf downloads the IMDb data itself the first time you press Update.", "مشکلی نیست: MovieShelf در اولین به‌روزرسانی، داده‌های IMDb را خودش دانلود می‌کند."));
            }
        }
        else if (I.ResultCode == Exit.Cancelled)
        {
            head.Text = L.T("Nothing was changed.", "چیزی تغییر نکرد.");
            b.Append(I.C.Upgrade ? L.T("Your earlier version of MovieShelf is still in place.", "نسخه قبلی MovieShelf شما سر جای خود است.") : L.T("Everything setup had written was removed.", "هر چیزی که نصب‌کننده نوشته بود حذف شد."));
        }
        else
        {
            head.Text = I.ResultCode == Exit.Damaged ? L.T("The setup file is damaged.", "فایل نصب آسیب دیده است.") : L.T("MovieShelf was not installed.", "MovieShelf نصب نشد.");
            b.Append(I.ResultMessage ?? "");
            if (I.C.Upgrade) b.Append("\r\n\r\n").Append(L.T("Your earlier version of MovieShelf is still in place.", "نسخه قبلی MovieShelf شما سر جای خود است."));
        }
        text.Text = b.ToString();
        int h = Ui.Logical(Ui.TextHeight(text.Text, text.Font, Ui.S(500)));
        Place(text, 60, 54, 500, h);
        int y = 54 + h + 18;
        if (run.Visible) { Place(run, 60, y, 300, 26); y += 34; }
        Place(log, 60, y, 300, 24);
        log.Visible = LogPath != null;
    }

    public override bool Leave()
    {
        if (Installed_ && run.Checked)
        {
            string exe = Path.Combine(I.C.InstallDir, "MovieShelf.exe");
            try { Process.Start(new ProcessStartInfo(exe) { WorkingDirectory = I.C.InstallDir, UseShellExecute = true }); }
            catch (Exception e) { if (I.Log != null) I.Log.Write("Could not start MovieShelf: " + e.Message); }
        }
        return true;
    }

    public override void Set(string key, string value) { if (key == "run") run.Checked = value == "1"; else base.Set(key, value); }

    public void Sample(int code, List<string> failed, bool imdbFailed, string message)
    {
        I.ResultCode = code; I.FailedDownloads = failed; I.ImdbFailed = imdbFailed; I.ResultMessage = message;
    }
}
