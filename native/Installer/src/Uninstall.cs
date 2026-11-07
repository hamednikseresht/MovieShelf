// Uninstall mode: uninstall.exe is this same program without tools and payload. It copies itself to %TEMP% and runs from
// there so it can delete the install folder. Only what the manifest lists is deleted; data goes only on an explicit choice.
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using System.Windows.Forms;

/// <summary>One kind of user data, as named on the "What to keep" page and in /DELETEDATA.</summary>
class DataItem
{
    public string Id, Title;
    public string[] Names; // inside the data folder
    public long Size = -1;
    public bool Keep = true;

    public static List<DataItem> Make()
    {
        return new List<DataItem>
        {
            new DataItem { Id = "library", Title = L.T("Library database and your marks", "پایگاه داده کتابخانه و علامت‌های شما"), Names = new[] { "library.db", "library.db-wal", "library.db-shm" } },
            new DataItem { Id = "backups", Title = L.T("Backups", "پشتیبان‌ها"), Names = new[] { "backups" } },
            new DataItem { Id = "settings", Title = L.T("Settings and API keys", "تنظیمات و کلیدهای API"), Names = new[] { "settings.json" } },
            new DataItem { Id = "posters", Title = L.T("Posters and photos", "پوسترها و عکس‌ها"), Names = new[] { "posters", "people" } },
            new DataItem { Id = "imdb", Title = L.T("IMDb data", "داده‌های IMDb"), Names = new[] { "imdb", "imdb_index.db" } },
        };
    }

    public long Measure(string dataDir)
    {
        long sum = 0;
        foreach (string n in Names)
        {
            string p = Path.Combine(dataDir, n);
            if (File.Exists(p)) sum += new FileInfo(p).Length;
            else sum += Files.Size(p);
        }
        return sum;
    }

    public void Delete(string dataDir, ISink s, List<string> failed)
    {
        foreach (string n in Names)
        {
            string p = Path.Combine(dataDir, n);
            if (File.Exists(p)) { if (!Files.TryDelete(p)) failed.Add(p); }
            else Files.DeleteTree(p, failed);
        }
        s.Log("Deleted user data: " + Id);
    }
}

/// <summary>Removes what the manifest lists. Runs in the wizard's process, or elevated for an all-users install.</summary>
class Remover
{
    readonly string dir;
    readonly bool allUsers;
    readonly int waitPid;
    readonly ISink S;

    public readonly List<string> Left = new List<string>();
    public Action Almost;
    public int Failures;

    public Remover(string folder, bool all, int pid, ISink sink) { dir = folder.TrimEnd('\\'); allUsers = all; waitPid = pid; S = sink; }

    bool Waiting
    {
        get
        {
            if (waitPid <= 0) return false;
            try { using (Process p = Process.GetProcessById(waitPid)) return !p.HasExited; }
            catch (ArgumentException) { return false; }
        }
    }

    public void Run()
    {
        Manifest m = Manifest.Load(Path.Combine(dir, Manifest.FileName));
        if (m == null)
        {
            S.Log("No install manifest in " + dir + ". No files are removed.");
            Entry();
            Left.Add(dir);
            Failures++;
            if (Almost != null) Almost();
            return;
        }

        S.Step(L.T("Removing shortcuts", "در حال حذف میانبرها"));
        string exe = Path.Combine(dir, "MovieShelf.exe");
        foreach (string link in m.Shortcuts)
        {
            if (!File.Exists(link)) continue;
            string t = Shell.Target(link);
            if (!string.IsNullOrEmpty(t) && !Files.Same(t, exe)) { S.Log("Left a shortcut that now opens something else: " + link); continue; }
            if (Files.TryDelete(link)) S.Log("Removed " + link); else { Left.Add(link); Failures++; }
        }

        S.Step(L.T("Removing files", "در حال حذف فایل‌ها"));
        var later = new List<string>();
        int done = 0, total = Math.Max(1, m.Files_.Count);
        foreach (string rel in m.Files_)
        {
            done++;
            if (Files.Same(rel, Manifest.FileName) || Files.Same(rel, "uninstall.exe")) { later.Add(rel); continue; }
            S.Detail(rel);
            if (!Files.TryDelete(Path.Combine(dir, rel))) { Failures++; S.Log("Could not delete " + rel); }
            if (done % 8 == 0) S.Progress(0.85 * done / total);
        }
        S.Progress(0.85);
        Folders(m);

        S.Step(L.T("Removing the entry from Installed apps", "در حال حذف از فهرست برنامه‌های نصب‌شده"));
        Entry();
        S.Progress(0.9);
        if (Almost != null) Almost();
        for (int i = 0; i < 200 && Waiting; i++) Thread.Sleep(100);

        foreach (string rel in later)
            if (!Files.TryDelete(Path.Combine(dir, rel))) { Failures++; S.Log("Could not delete " + rel); }
        Folders(m);
        if (m["ownroot"] != "0") Files.RemoveIfEmpty(dir);
        foreach (string p in m.Parents) Files.RemoveIfEmpty(p);

        if (Directory.Exists(dir))
            foreach (string e in Directory.GetFileSystemEntries(dir)) Left.Add(e);
        S.Log(Left.Count == 0 ? "Everything setup installed was removed." : "Left behind: " + string.Join("; ", Left.ToArray()));
        S.Progress(1);
    }

    void Folders(Manifest m)
    {
        var dirs = new List<string>(m.Dirs);
        dirs.Sort((a, b) => b.Length.CompareTo(a.Length));
        foreach (string d in dirs) Files.RemoveIfEmpty(Path.Combine(dir, d));
    }

    void Entry()
    {
        try
        {
            string where = null;
            using (Microsoft.Win32.RegistryKey k = UninstallEntry.Open(allUsers, false))
                if (k != null) where = k.GetValue("InstallLocation") as string;
            // The entry may belong to another copy: a portable folder can have been moved.
            if (where != null && Files.Same(where, dir)) UninstallEntry.Delete(allUsers);
        }
        catch (Exception e) { S.Log("Could not remove the registry entry: " + e.Message); Failures++; }
    }
}

/// <summary>One uninstall from start to end, for the wizard and for /S.</summary>
class UninstallJob
{
    public string Dir, DataDir;
    public bool AllUsers;
    public int WaitPid;
    public List<DataItem> Delete = new List<DataItem>();
    public Action Almost;
    public ISink S;

    public readonly List<string> Left = new List<string>();
    public string Message;
    public bool Declined;

    public int Run()
    {
        ElevatedWorker elevated = null;
        string ipc = null;
        try
        {
            if (AllUsers && !Env.IsAdmin)
            {
                ipc = Path.Combine(Env.Temp, "MovieShelf-Uninstall-" + Guid.NewGuid().ToString("N"));
                elevated = new ElevatedWorker(ipc);
                try { elevated.Start(""); }
                catch (Win32Exception e)
                {
                    if (e.NativeErrorCode != 1223) throw;
                    Declined = true;
                    Message = L.T("Windows did not get administrator permission, so nothing was removed.", "ویندوز مجوز مدیر سیستم را دریافت نکرد، بنابراین چیزی حذف نشد.");
                    return Exit.Declined;
                }
            }

            if (Apps.Running(Dir).Count > 0)
            {
                S.Step(L.T("Closing MovieShelf", "در حال بستن MovieShelf"));
                if (!Apps.Close(Dir, 6000))
                {
                    Message = L.T("MovieShelf is still running and could not be closed. Close it, then try again.", "MovieShelf هنوز در حال اجراست و بسته نشد. آن را ببندید و دوباره امتحان کنید.");
                    if (elevated != null) elevated.Abort();
                    return Exit.Failed;
                }
            }

            var failed = new List<string>();
            if (Delete.Count > 0)
            {
                S.Step(L.T("Deleting the data you chose", "در حال حذف داده‌های انتخابی شما"));
                foreach (DataItem d in Delete) d.Delete(DataDir, S, failed);
                Files.RemoveIfEmpty(DataDir);
                Files.RemoveIfEmpty(Path.GetDirectoryName(DataDir));
            }
            Left.AddRange(failed);
            S.Progress(0.2);

            var sink = new Slice(S, 0.2, 1);
            int failures;
            if (elevated != null)
            {
                var plan = new Dictionary<string, string>();
                plan["mode"] = "uninstall"; plan["dir"] = Dir; plan["all"] = "1"; plan["lang"] = L.Tag;
                plan["waitpid"] = WaitPid.ToString(CultureInfo.InvariantCulture);
                elevated.Send(plan);
                string msg;
                failures = elevated.Follow(sink, () => false, Almost, out msg);
                if (!string.IsNullOrEmpty(msg)) Left.AddRange(msg.Split(new[] { '\t' }, StringSplitOptions.RemoveEmptyEntries));
            }
            else
            {
                var r = new Remover(Dir, AllUsers, WaitPid, sink) { Almost = Almost };
                r.Run();
                Left.AddRange(r.Left);
                failures = r.Failures;
            }
            return failures > 0 || failed.Count > 0 ? Exit.Partial : Exit.Ok;
        }
        catch (Exception e)
        {
            S.Log("Failed: " + e);
            Message = e.Message;
            return Exit.Failed;
        }
        finally
        {
            if (elevated != null) elevated.Dispose();
            if (ipc != null) Files.DeleteTree(ipc, null);
        }
    }
}

class LogSink : ISink
{
    readonly Logger log;
    public LogSink(Logger l) { log = l; }
    public void Step(string text) { log.Write("-- " + text); }
    public void Detail(string text) { }
    public void Progress(double f) { }
    public void Log(string line) { log.Write(line); }
    public void Locked(bool locked) { }
}

static class UninstallMain
{
    static readonly string[] AllItems = { "library", "backups", "settings", "posters", "imdb" };

    public static int Run(Args a)
    {
        if (a.From == null) return Relocate(a);

        string self = Path.GetDirectoryName(Env.Self);
        if (!a.Silent) WaitFor(a.Ppid, 6000);
        int code;
        try { code = a.Silent ? Silent(a, self) : Interactive(a); }
        finally { CleanUp(self); }
        return code;
    }

    /// <summary>Copies this exe to %TEMP% and hands over. In a silent run this copy stays until the work is done, for the exit code.</summary>
    static int Relocate(Args a)
    {
        string dir = Path.GetDirectoryName(Env.Self);
        string tmp = Path.Combine(Env.Temp, "MovieShelf-Uninstall-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tmp);
        string copy = Path.Combine(tmp, "uninstall.exe");
        File.Copy(Env.Self, copy);
        var args = new StringBuilder("/_FROM=\"" + dir + "\" /_PPID=" + Process.GetCurrentProcess().Id.ToString(CultureInfo.InvariantCulture));
        foreach (string x in a.Raw) args.Append(" \"").Append(x).Append('"');
        using (Process p = Process.Start(new ProcessStartInfo(copy, args.ToString()) { UseShellExecute = false, WorkingDirectory = tmp }))
        {
            if (!a.Silent) return Exit.Ok;
            Environment.CurrentDirectory = tmp;
            string result = Path.Combine(tmp, "result");
            while (!File.Exists(result) && !p.HasExited) Thread.Sleep(100);
            if (File.Exists(result))
            {
                for (int i = 0; i < 20; i++)
                {
                    try { return int.Parse(File.ReadAllText(result).Trim(), CultureInfo.InvariantCulture); }
                    catch (Exception) { Thread.Sleep(50); }
                }
            }
            return p.HasExited ? p.ExitCode : Exit.Failed;
        }
    }

    static void WaitFor(int pid, int ms)
    {
        if (pid <= 0) return;
        try { using (Process p = Process.GetProcessById(pid)) p.WaitForExit(ms); }
        catch (ArgumentException) { }
    }

    static void ChooseLanguage(Args a, Manifest m)
    {
        string lang = a.Lang ?? (m != null ? m["lang"] : "");
        if (lang.Length == 0) lang = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;
        L.Fa = Ui.Rtl = lang == "fa";
    }

    static int Interactive(Args a)
    {
        Manifest m = Manifest.Load(Path.Combine(a.From, Manifest.FileName));
        ChooseLanguage(a, m);
        Ui.Init();
        var w = new UninstallWizard(a.From, m);
        if (TestMode.Attach(w)) { }
        Application.Run(w);
        return w.Code;
    }

    static int Silent(Args a, string self)
    {
        Manifest m = Manifest.Load(Path.Combine(a.From, Manifest.FileName));
        ChooseLanguage(a, m);
        var log = new Logger(a.LogPath ?? Path.Combine(self, "uninstall.log"));
        var sink = new LogSink(log);
        log.Write("MovieShelf uninstall from " + a.From);
        string resultFile = Path.Combine(self, "result");
        bool told = false;
        Action<int> tell = c =>
        {
            if (told) return;
            told = true;
            string tmp = resultFile + ".tmp";
            File.WriteAllText(tmp, c.ToString(CultureInfo.InvariantCulture));
            File.Move(tmp, resultFile);
        };
        bool portable = File.Exists(Path.Combine(a.From, "portable.txt"));
        var job = new UninstallJob
        {
            Dir = a.From, DataDir = Env.DataDir(a.From, portable), AllUsers = m != null && m["scope"] == "all", WaitPid = a.Ppid, S = sink
        };
        List<DataItem> items = DataItem.Make();
        if (a.DeleteData != null)
            foreach (string id in a.DeleteData.Split(','))
            {
                string name = id.Trim().ToLowerInvariant();
                if (name == "all") { job.Delete = items; break; }
                DataItem it = items.Find(x => x.Id == name);
                if (it == null) { log.Write("Unknown data name: " + name); tell(Exit.BadArguments); return Exit.BadArguments; }
                job.Delete.Add(it);
            }
        job.Almost = () => tell(Exit.Ok);
        int code = job.Run();
        if (job.Message != null) log.Write(job.Message);
        tell(code);
        return code;
    }

    /// <summary>The copy in %TEMP% cannot delete itself while it runs, so a short-lived cmd does it.</summary>
    static void CleanUp(string self)
    {
        if (!Path.GetFileName(self).StartsWith("MovieShelf-Uninstall-", StringComparison.Ordinal)) return;
        Process.Start(new ProcessStartInfo("cmd.exe", "/c ping -n 3 127.0.0.1 >nul & rmdir /s /q \"" + self + "\"")
        {
            UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = Env.Temp
        });
    }
}

class UninstallWizard : Wizard
{
    public readonly string Dir;
    public readonly Manifest M;
    public readonly bool AllUsers;
    public readonly string DataDir;
    public readonly List<DataItem> Items;
    public readonly bool Python;
    public long AppSize = -1;
    public bool Demo;
    public int ResultCode = -1;
    public string ResultMessage, ReadyMessage;
    public List<string> Remaining = new List<string>();

    public UninstallWizard(string dir, Manifest m)
    {
        Dir = dir.TrimEnd('\\');
        M = m;
        AllUsers = m != null && m["scope"] == "all";
        bool portable = File.Exists(Path.Combine(Dir, "portable.txt"));
        DataDir = Env.DataDir(Dir, portable);
        Python = Directory.Exists(Path.Combine(Path.GetDirectoryName(DataDir), "venv"));
        Items = DataItem.Make();
        BuildAll();
        var t = new Thread(Measure) { IsBackground = true };
        t.Start();
    }

    void Measure()
    {
        AppSize = Files.Size(Dir);
        foreach (DataItem d in Items) d.Size = d.Measure(DataDir);
        try { BeginInvoke((Action)(() => { var p = Current as ISizeAware; if (p != null) p.SizesChanged(); })); }
        catch (InvalidOperationException) { }
    }

    protected override string WindowTitle { get { return L.T("Uninstall MovieShelf", "حذف MovieShelf"); } }

    protected override void MakePages()
    {
        Pages.Add(new UWelcomePage());
        Pages.Add(new UKeepPage());
        Pages.Add(new UReadyPage());
        Pages.Add(new URemovePage());
        Pages.Add(new UFinishPage());
    }

    protected override void OnCancelPressed()
    {
        if (Prompt.Ask(this, L.T("Cancel the uninstall?", "حذف لغو شود؟"), L.T("MovieShelf stays on your computer as it is.", "MovieShelf همان‌طور که هست روی رایانه شما می‌ماند."),
            L.T("Cancel uninstall", "لغو حذف"), L.T("Continue", "ادامه"), true))
        {
            CloseWithoutAsking = true;
            Code = Exit.Cancelled;
            Close();
        }
    }
}

interface ISizeAware { void SizesChanged(); }

class UWelcomePage : Page, ISizeAware
{
    UninstallWizard U { get { return (UninstallWizard)W; } }
    Summary sum;
    StatusRow run;
    FlatBtn close;

    public override string Id { get { return "uwelcome"; } }
    public override string SideName { get { return L.T("Welcome", "خوش آمدید"); } }
    public override string Title { get { return L.T("Uninstall MovieShelf", "حذف MovieShelf"); } }
    public override string Sub { get { return L.T("This removes the program. Your library stays unless you say otherwise.", "این کار برنامه را حذف می‌کند. کتابخانه شما می‌ماند، مگر اینکه خودتان بخواهید."); } }

    public override void Build()
    {
        sum = Add(new Summary { LabelWidth = 130 }, 0, 0, AreaW, 164);
        run = Add(new StatusRow { Title = L.T("MovieShelf running", "اجرای MovieShelf"), Reserve = Ui.S(128) }, 0, 172, AreaW, 40);
        close = Add(new FlatBtn { Text = L.T("Close &MovieShelf", "بستن MovieShelf"), Visible = false }, AreaW - 120, 176, 120, 32);
        close.Click += (o, e) =>
        {
            close.Enabled = false;
            string dir = U.Dir;
            var t = new Thread(() => { Apps.Close(dir, 6000); W.BeginInvoke((Action)(() => { close.Enabled = true; Check(); })); }) { IsBackground = true };
            t.Start();
        };
        if (U.M == null)
        {
            int y = 226;
            Text(L.T("The list of installed files is missing, so no program files can be removed safely. Only the entry in Installed apps will go.", "فهرست فایل‌های نصب‌شده پیدا نشد، بنابراین فایل‌های برنامه با اطمینان حذف نمی‌شوند. فقط ورودی برنامه در فهرست برنامه‌های نصب‌شده پاک می‌شود."), 0, ref y, AreaW, 13, false, Theme.Warn, 0);
        }
    }

    public override void Enter() { Fill(); Check(); }
    public void SizesChanged() { Fill(); }

    void Fill()
    {
        sum.Clear();
        sum.AccessibleName = "";
        sum.Add(L.T("Program", "برنامه"), "MovieShelf " + Fmt.Num(U.M != null ? U.M["version"] : ""));
        sum.Add(L.T("Location", "محل نصب"), Fmt.Ltr(U.Dir));
        sum.Add(L.T("Size", "حجم"), U.AppSize >= 0 ? Fmt.Size(U.AppSize) : L.T("Measuring…", "در حال محاسبه…"));
        if (U.M != null) sum.Add(L.T("Also removed", "موارد دیگر"), L.T("Shortcuts and the entry in Installed apps", "میانبرها و ورودی برنامه در ویندوز"));
    }

    void Check()
    {
        if (U.Demo) { run.Set(Mark.Pass, L.T("Not running.", "در حال اجرا نیست.")); return; }
        bool on = Apps.Running(U.Dir).Count > 0;
        run.Set(on ? Mark.Warn : Mark.Pass, on ? L.T("Running. Close it now, or setup closes it when you continue.", "در حال اجراست. آن را ببندید، وگرنه هنگام ادامه بسته می‌شود.") : L.T("Not running.", "در حال اجرا نیست."));
        close.Visible = on;
    }
}

class UKeepPage : Page, ISizeAware
{
    UninstallWizard U { get { return (UninstallWizard)W; } }
    readonly List<CheckBox> boxes = new List<CheckBox>();
    readonly List<TextView> sizes = new List<TextView>();
    TextView warning;

    public override string Id { get { return "keep"; } }
    public override string SideName { get { return L.T("What to keep", "چه چیزی بماند"); } }
    public override string Title { get { return L.T("What to keep", "چه چیزی بماند"); } }
    public override string Sub { get { return L.T("Everything is kept. Untick only what you want deleted.", "همه چیز حفظ می‌شود. فقط چیزی را که می‌خواهید حذف شود بردارید."); } }
    public override bool Shown { get { return true; } }

    public override void Build()
    {
        int y = 0;
        Text(L.T("Your data folder: ", "پوشه داده‌های شما: ") + Fmt.Ltr(U.DataDir), 0, ref y, AreaW, 12, false, Theme.Muted, 10);
        foreach (DataItem d in U.Items)
        {
            DataItem item = d;
            CheckBox box = Check(L.T("Keep: ", "نگه داشتن: ") + d.Title, 0, y, 420, d.Keep);
            box.CheckedChanged += (o, e) => { item.Keep = box.Checked; Warn(); };
            boxes.Add(box);
            sizes.Add(Add(new TextView { Font = Ui.Font(13, false), ForeColor = Theme.Muted }, 426, y + 4, 134, 20));
            y += 34;
        }
        y += 8;
        warning = Add(new TextView { Font = Ui.Font(13, true), ForeColor = Theme.Fail }, 0, y, AreaW, 100);
    }

    public override void Enter() { SizesChanged(); Warn(); }

    public void SizesChanged()
    {
        for (int i = 0; i < sizes.Count; i++)
        {
            long n = U.Items[i].Size;
            sizes[i].Text = n < 0 ? "…" : n == 0 ? L.T("Empty", "خالی") : Fmt.Size(n);
        }
    }

    void Warn()
    {
        var names = U.Items.FindAll(d => !d.Keep);
        var b = new StringBuilder();
        if (names.Count > 0)
        {
            b.Append(L.T("Deleted for good, and not in the Recycle Bin: ", "برای همیشه حذف می‌شود و به سطل بازیافت نمی‌رود: "));
            b.Append(string.Join(L.T(", ", "، "), names.ConvertAll(d => d.Title).ToArray())).Append(L.T(".", "."));
            if (U.Python) b.Append("\r\n").Append(L.T("The older Python MovieShelf uses the same data, so it loses it too.", "نسخه قدیمی پایتونی MovieShelf هم از همین داده‌ها استفاده می‌کند و آن‌ها را از دست می‌دهد."));
        }
        warning.Text = b.ToString();
    }

    public override void Set(string key, string value)
    {
        int i = U.Items.FindIndex(d => d.Id == key);
        if (i < 0) base.Set(key, value);
        boxes[i].Checked = value == "1";
    }
}

class UReadyPage : Page
{
    UninstallWizard U { get { return (UninstallWizard)W; } }
    Summary sum;
    TextView message;

    public override string Id { get { return "uready"; } }
    public override string SideName { get { return L.T("Ready", "آماده حذف"); } }
    public override string Title { get { return L.T("Ready to uninstall", "آماده حذف"); } }
    public override string Sub { get { return L.T("Check this list, then press Uninstall.", "این فهرست را بررسی کنید و سپس حذف را بزنید."); } }
    public override string NextText { get { return L.T("&Uninstall", "حذف"); } }
    public override bool NextShield { get { return U.AllUsers && !Env.IsAdmin; } }

    public override void Build()
    {
        sum = Add(new Summary { LabelWidth = 130 }, 0, 0, AreaW, 300);
        message = Add(new TextView { Font = Ui.Font(13, false), ForeColor = Theme.Fail }, 0, 310, AreaW, 60);
    }

    public override void Enter()
    {
        sum.Clear();
        sum.AccessibleName = "";
        sum.Add(L.T("Removed", "حذف می‌شود"), L.T("MovieShelf, its shortcuts and its entry in Installed apps", "MovieShelf، میانبرها و ورودی آن در برنامه‌های نصب‌شده"));
        var kept = U.Items.FindAll(d => d.Keep).ConvertAll(d => d.Title);
        var gone = U.Items.FindAll(d => !d.Keep).ConvertAll(d => d.Title);
        sum.Add(L.T("Kept", "می‌ماند"), kept.Count > 0 ? string.Join(L.T(", ", "، "), kept.ToArray()) : L.T("Nothing", "هیچ"));
        if (gone.Count > 0) sum.Add(L.T("Deleted for good", "حذف برای همیشه"), string.Join(L.T(", ", "، "), gone.ToArray()));
        message.Text = U.ReadyMessage ?? "";
        U.ReadyMessage = null;
    }

    public override bool Leave()
    {
        if (U.Items.TrueForAll(d => d.Keep)) return true;
        return Prompt.Ask(W, L.T("Delete your data?", "داده‌های شما حذف شود؟"), L.T("The items you unticked will be deleted for good. This cannot be undone.", "مواردی که برداشته‌اید برای همیشه حذف می‌شوند و این کار قابل بازگشت نیست."),
            L.T("Delete and uninstall", "حذف داده و برنامه"), L.T("Go back", "بازگشت"), true);
    }
}

class URemovePage : Page, ISink
{
    UninstallWizard U { get { return (UninstallWizard)W; } }
    Bar bar;
    TextView step, detail;
    System.Windows.Forms.Timer timer;
    volatile string stepText = "", detailText = "";
    double value;
    Thread worker;
    UninstallJob job;

    public override string Id { get { return "removing"; } }
    public override string SideName { get { return L.T("Removing", "حذف"); } }
    public override string Title { get { return L.T("Removing MovieShelf", "در حال حذف MovieShelf"); } }
    public override string Sub { get { return L.T("This takes a moment.", "کمی طول می‌کشد."); } }
    public override bool CanNext { get { return false; } }
    public override bool CanBack { get { return false; } }
    public override bool CanCancel { get { return false; } }

    public override void Build()
    {
        step = Add(new TextView { Font = Ui.Font(15, true), ForeColor = Theme.Ink }, 0, 4, AreaW, 26);
        bar = Add(new Bar(), 0, 36, AreaW, 10);
        detail = Add(new TextView { Font = Ui.Font(12, false), ForeColor = Theme.Muted }, 0, 54, AreaW, 24);
        timer = new System.Windows.Forms.Timer { Interval = 100 };
        timer.Tick += (o, e) => Pump();
    }

    public void Sample(double v, string s, string d) { bar.Value = v; step.Text = s; detail.Text = d; }

    public override void Enter()
    {
        if (U.Demo || worker != null) return;
        job = new UninstallJob { Dir = U.Dir, DataDir = U.DataDir, AllUsers = U.AllUsers, Delete = U.Items.FindAll(d => !d.Keep), S = this };
        timer.Start();
        worker = new Thread(() =>
        {
            int code = job.Run();
            try { W.BeginInvoke((Action<int>)Done, code); } catch (InvalidOperationException) { }
        });
        worker.Start();
    }

    void Done(int code)
    {
        timer.Stop();
        Pump();
        worker = null;
        if (job.Declined)
        {
            U.ReadyMessage = job.Message;
            W.GoTo("uready");
            return;
        }
        U.ResultCode = code;
        U.ResultMessage = job.Message;
        U.Remaining = job.Left;
        W.Code = code;
        W.GoTo("ufinish");
    }

    void Pump() { step.Text = stepText; detail.Text = detailText; bar.Value = value; }

    public void Step(string text) { stepText = text; }
    public void Detail(string text) { detailText = text; }
    public void Progress(double f) { value = f; }
    public void Log(string line) { }
    public void Locked(bool locked) { }
}

class UFinishPage : Page
{
    UninstallWizard U { get { return (UninstallWizard)W; } }
    MarkBox mark;
    TextView head, text;

    bool Good { get { return U.ResultCode == Exit.Ok; } }

    public override string Id { get { return "ufinish"; } }
    public override string SideName { get { return L.T("Finish", "پایان"); } }
    public override string Title { get { return Good ? L.T("MovieShelf was removed", "MovieShelf حذف شد") : U.ResultCode == Exit.Partial ? L.T("Mostly removed", "تا حد زیادی حذف شد") : L.T("Uninstall did not finish", "حذف کامل نشد"); } }
    public override string Sub { get { return ""; } }
    public override bool CanBack { get { return false; } }
    public override bool HidesCancel { get { return true; } }
    public override string NextText { get { return L.T("&Finish", "پایان"); } }

    public override void Build()
    {
        mark = Add(new MarkBox(), 0, 4, 44, 44);
        head = Add(new TextView { Font = Ui.Font(15, true), ForeColor = Theme.Ink }, 60, 12, 500, 26);
        text = Add(new TextView { Font = Ui.Font(13, false), ForeColor = Theme.Muted }, 60, 54, 500, 300);
    }

    public override void Enter()
    {
        mark.State = Good ? Mark.Pass : U.ResultCode == Exit.Partial ? Mark.Warn : Mark.Fail;
        mark.Invalidate();
        head.Text = Good ? L.T("Thanks for using MovieShelf.", "از همراهی شما با MovieShelf سپاسگزاریم.") : L.T("Some items could not be removed.", "برخی موارد حذف نشدند.");
        var b = new StringBuilder();
        if (U.ResultMessage != null) b.Append(U.ResultMessage).Append("\r\n\r\n");
        if (U.Items.Exists(d => d.Keep) && Directory.Exists(U.DataDir))
            b.Append(L.T("Your library data stays in: ", "داده‌های کتابخانه شما اینجا می‌ماند: ")).Append(Fmt.Ltr(U.DataDir));
        if (U.Remaining.Count > 0)
        {
            if (b.Length > 0) b.Append("\r\n\r\n");
            b.Append(L.T("Left behind:", "این موارد باقی ماند:"));
            int n = 0;
            foreach (string l in U.Remaining)
            {
                if (++n > 8) { b.Append("\r\n•  …"); break; }
                b.Append("\r\n•  ").Append(Fmt.Ltr(l));
            }
        }
        text.Text = b.ToString();
    }

    public override bool Leave() { W.Code = U.ResultCode; return true; }
}
