// The install work, without any UI: downloads (Runner, always in the unelevated process, so profile paths stay the
// user's), then the file and registry work (Committer, in this process or in an elevated copy of this program).
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;

/// <summary>Where progress goes: the wizard, the log of a silent run, or the file an elevated worker writes.</summary>
interface ISink
{
    void Step(string text);
    void Detail(string text);
    void Progress(double fraction);
    void Log(string line);
    /// <summary>True from the point where stopping is no longer offered.</summary>
    void Locked(bool locked);
}

/// <summary>Everything the user chose.</summary>
class Choices
{
    public bool AllUsers, Portable, Imdb, Ffprobe, KeepFfprobe, Desktop = true, StartMenu = true, Run = true, Upgrade;
    public string InstallDir, SetupType = "typical", Components = "", OldVersion;
    public Installed Previous;

    /// <summary>An upgrade or repair keeps the location and the choices made last time. Downloads are not repeated.</summary>
    public void Use(Installed p)
    {
        Previous = p;
        Upgrade = true;
        OldVersion = p.Version;
        InstallDir = p.Dir;
        AllUsers = p.AllUsers;
        Portable = p.Portable;
        if (p.M == null) { Desktop = false; return; }
        Desktop = p.M["desktop"] != "0";
        StartMenu = p.M["startmenu"] != "0";
        bool chosen = ("," + p.M["components"] + ",").Contains(",ffprobe,");
        bool there = File.Exists(Path.Combine(Path.Combine(p.Dir, "tools"), "ffprobe.exe"));
        KeepFfprobe = chosen && there;
        Ffprobe = chosen && !there;
    }
}

/// <summary>What the optional components weigh. The figures are replaced by real ones when the server answers.</summary>
class ImdbFile
{
    public string Name;
    public long Size;

    public string FileName { get { return Name + ".tsv.gz"; } }
}

static class Sizes
{
    // Measured from a full download; only used until the server reports real sizes.
    public static readonly ImdbFile[] Imdb =
    {
        new ImdbFile { Name = "name.basics", Size = 312000000 }, new ImdbFile { Name = "title.akas", Size = 520000000 },
        new ImdbFile { Name = "title.basics", Size = 229000000 }, new ImdbFile { Name = "title.crew", Size = 84000000 },
        new ImdbFile { Name = "title.principals", Size = 789000000 }, new ImdbFile { Name = "title.ratings", Size = 9000000 },
    };

    public const int FreshDays = 7;
    public static long FfmpegZip = 110L << 20;       // the download
    public const long FfprobeOnDisk = 195L << 20;    // bin\ffprobe.exe unpacked

    public static bool IsFresh(string dataDir, ImdbFile f)
    {
        string p = Path.Combine(Path.Combine(dataDir, "imdb"), f.FileName);
        return File.Exists(p) && (DateTime.UtcNow - File.GetLastWriteTimeUtc(p)).TotalDays < FreshDays;
    }

    /// <summary>The bytes the IMDb files still to fetch come to.</summary>
    public static long ImdbToFetch(string dataDir)
    {
        long sum = 0;
        foreach (ImdbFile f in Imdb) if (!IsFresh(dataDir, f)) sum += f.Size;
        return sum;
    }

    public static long Download(Choices c, string dataDir) { return (c.Imdb ? ImdbToFetch(dataDir) : 0) + (c.Ffprobe ? FfmpegZip : 0); }

    /// <summary>Space needed on the drive of the install folder.</summary>
    public static long OnInstallDrive(Choices c, long appBytes, string dataDir)
    {
        long sum = appBytes + (c.Ffprobe ? FfprobeOnDisk : 0);
        if (c.Imdb && SameDrive(c.InstallDir, dataDir)) sum += ImdbToFetch(dataDir);
        return sum;
    }

    public static bool SameDrive(string a, string b)
    {
        return a != null && b != null && a.Length > 1 && b.Length > 1 && char.ToUpperInvariant(a[0]) == char.ToUpperInvariant(b[0]) && a[1] == b[1];
    }
}

class CommitPlan
{
    public string SetupExe, InstallDir, Version, Components, FfprobeFile, ParentLog, Lang;
    public bool AllUsers, Portable, Desktop, StartMenu, KeepFfprobe, VerifySelf, OwnRoot;

    public Dictionary<string, string> ToKv()
    {
        var d = new Dictionary<string, string>();
        d["setup"] = SetupExe; d["dir"] = InstallDir; d["version"] = Version; d["components"] = Components; d["ffprobe"] = FfprobeFile;
        d["log"] = ParentLog; d["lang"] = Lang; d["all"] = AllUsers ? "1" : "0"; d["portable"] = Portable ? "1" : "0";
        d["desktop"] = Desktop ? "1" : "0"; d["startmenu"] = StartMenu ? "1" : "0"; d["keepffprobe"] = KeepFfprobe ? "1" : "0";
        d["verify"] = VerifySelf ? "1" : "0"; d["ownroot"] = OwnRoot ? "1" : "0";
        return d;
    }

    public static CommitPlan From(Dictionary<string, string> d)
    {
        return new CommitPlan
        {
            SetupExe = Kv.Get(d, "setup"), InstallDir = Kv.Get(d, "dir"), Version = Kv.Get(d, "version"), Components = Kv.Get(d, "components"),
            FfprobeFile = Kv.Get(d, "ffprobe"), ParentLog = Kv.Get(d, "log"), Lang = Kv.Get(d, "lang"), AllUsers = Kv.Flag(d, "all"),
            Portable = Kv.Flag(d, "portable"), Desktop = Kv.Flag(d, "desktop"), StartMenu = Kv.Flag(d, "startmenu"),
            KeepFfprobe = Kv.Flag(d, "keepffprobe"), VerifySelf = Kv.Flag(d, "verify"), OwnRoot = Kv.Flag(d, "ownroot")
        };
    }
}

/// <summary>
/// Puts the files in place. The new version is unpacked beside the target on the same volume, so swapping it in is a
/// rename and a failure at any point leaves the old version intact. Every step that changes something registers its undo.
/// </summary>
class Committer
{
    public const string LogName = "install.log";
    const string StagingMark = ".~movieshelf-setup-staging";

    readonly CommitPlan P;
    readonly ISink S;
    readonly Func<bool> Stopped;
    readonly List<Action> undo = new List<Action>();
    readonly StringBuilder ownLog = new StringBuilder();
    string target, staging, oldDir, mergeBackup, logHead = "";
    bool locked;

    public string Message;

    public Committer(CommitPlan plan, ISink sink, Func<bool> stopped) { P = plan; S = sink; Stopped = stopped; }

    void Log(string line)
    {
        ownLog.Append(DateTime.Now.ToString("HH:mm:ss ", CultureInfo.InvariantCulture)).Append(line).Append("\r\n");
        S.Log(line);
    }

    void Check() { if (!locked && Stopped()) throw new CancelledException(); }

    public int Run()
    {
        try
        {
            Do();
            return Exit.Ok;
        }
        catch (CancelledException)
        {
            Log("Stopped by the user. Rolling back.");
            Rollback();
            Message = L.T("Installation cancelled.", "نصب لغو شد.");
            return Exit.Cancelled;
        }
        catch (SetupError e)
        {
            Log("Failed: " + e.Message);
            Rollback();
            Message = e.Message;
            return e.Code;
        }
        catch (Exception e)
        {
            Log("Failed: " + e);
            Rollback();
            Message = e is IOException || e is UnauthorizedAccessException
                ? L.T("A file or folder could not be written: ", "نوشتن یک فایل یا پوشه ممکن نشد: ") + e.Message
                : e.Message;
            return Exit.Failed;
        }
    }

    void Rollback()
    {
        locked = true;
        S.Locked(true);
        S.Step(L.T("Undoing the changes", "در حال بازگرداندن تغییرات"));
        for (int i = undo.Count - 1; i >= 0; i--)
        {
            try { undo[i](); }
            catch (Exception e) { Log("Rollback step failed: " + e.Message); }
        }
        undo.Clear();
    }

    void Do()
    {
        target = P.InstallDir.TrimEnd('\\');
        staging = target + ".~new";
        oldDir = target + ".~old";
        if (!string.IsNullOrEmpty(P.ParentLog) && File.Exists(P.ParentLog)) logHead = ReadShared(P.ParentLog);
        Log("Installing MovieShelf " + P.Version + " to " + target + (P.AllUsers ? " for all users" : " for this user") + (P.Portable ? ", portable" : ""));

        SetupFile sf = SetupFile.Read(P.SetupExe);
        if (sf == null) throw new SetupError(Exit.Damaged, Texts.Damaged());
        if (P.VerifySelf && !sf.Verify(null, Stopped)) throw new SetupError(Exit.Damaged, Texts.Damaged());

        Recover();
        CloseApp();
        List<string> parents = MakeParents();

        // 1. Unpack beside the target.
        S.Step(L.T("Unpacking files", "در حال باز کردن فایل‌ها"));
        Directory.CreateDirectory(staging);
        undo.Add(() => Files.DeleteTree(staging, null));
        File.WriteAllBytes(Path.Combine(staging, StagingMark), new byte[0]);
        string tools = Path.Combine(staging, ".~tools"), payload = Path.Combine(staging, ".~payload.7z");
        sf.ExtractTools(tools);
        sf.CopyPayload(payload, Stopped);
        S.Progress(0.05);
        var output = new List<string>();
        int code = SevenZip.Run(tools, "x \"" + payload + "\" -o\"" + staging + "\"", (f, file) =>
        {
            S.Progress(0.05 + f * 0.75);
            if (file != null) S.Detail(file);
        }, Stopped, output);
        if (code != 0)
        {
            foreach (string line in output) Log("7z: " + line);
            throw new SetupError(Exit.Failed, L.T("Unpacking the files failed (7-Zip exit code ", "باز کردن فایل‌ها ناموفق بود (کد خطای 7-Zip: ") + code + ").");
        }
        Files.DeleteTree(tools, null);
        File.Delete(payload);
        File.Delete(Path.Combine(staging, StagingMark));
        S.Progress(0.82);

        // 2. Add what the payload does not carry.
        S.Step(L.T("Adding the extras", "در حال افزودن موارد جانبی"));
        if (P.Portable) File.WriteAllText(Path.Combine(staging, "portable.txt"), "MovieShelf keeps its library in this folder because this file is here.\r\n");
        string ffprobe = !string.IsNullOrEmpty(P.FfprobeFile) ? P.FfprobeFile : null;
        if (ffprobe == null && P.KeepFfprobe && File.Exists(ProbeIn(target))) ffprobe = ProbeIn(target);
        if (ffprobe != null)
        {
            Directory.CreateDirectory(Path.Combine(staging, "tools"));
            File.Copy(ffprobe, ProbeIn(staging), true);
            Log("ffprobe placed in tools\\");
        }
        sf.WriteUninstaller(Path.Combine(staging, "uninstall.exe"));

        Manifest m = Manifest.FromFolder(staging);
        m.Files_.Add(Manifest.FileName);
        m.Files_.Add(LogName);
        m["version"] = P.Version;
        m["scope"] = P.AllUsers ? "all" : "user";
        m["components"] = P.Components;
        m["lang"] = P.Lang;
        m["portable"] = P.Portable ? "1" : "0";
        m["desktop"] = P.Desktop ? "1" : "0";
        m["startmenu"] = P.StartMenu ? "1" : "0";
        Check();

        // 3. Swap it in. Past this point the work is short, so it is not offered for cancelling.
        locked = true;
        S.Locked(true);
        S.Step(L.T("Replacing the program files", "در حال جایگزینی فایل‌های برنامه"));
        S.Progress(0.86);
        Manifest previous = Manifest.Load(Path.Combine(target, Manifest.FileName));
        Swap(m, previous);

        // 4. Shortcuts and the entry in Installed apps.
        S.Step(L.T("Creating shortcuts", "در حال ساخت میانبرها"));
        S.Progress(0.92);
        Shortcuts(m, previous);
        foreach (string p in parents) m.Parents.Add(p);
        if (previous != null) foreach (string p in previous.Parents) if (!m.Parents.Contains(p)) m.Parents.Add(p);

        S.Step(L.T("Registering MovieShelf", "در حال ثبت MovieShelf"));
        Dictionary<string, object> entry = UninstallEntry.Snapshot(P.AllUsers);
        undo.Add(() => UninstallEntry.Restore(P.AllUsers, entry));
        UninstallEntry.Write(P.AllUsers, target, P.Version, m.Bytes);

        m.Save(Path.Combine(target, Manifest.FileName));
        S.Progress(0.97);

        // 5. Success: only now does the old version go.
        if (Directory.Exists(oldDir))
        {
            var failed = new List<string>();
            Files.DeleteTree(oldDir, failed);
            if (failed.Count > 0) Log("Could not remove all of the old version: " + failed[0]);
        }
        if (mergeBackup != null) Files.DeleteTree(mergeBackup, null);
        undo.Clear();
        Log("Installed.");
        WriteLog();
        S.Progress(1);
    }

    static string ProbeIn(string dir) { return Path.Combine(Path.Combine(dir, "tools"), "ffprobe.exe"); }

    /// <summary>What an install that was cut off (a power failure, say) left behind.</summary>
    void Recover()
    {
        if (Directory.Exists(staging))
        {
            if (!File.Exists(Path.Combine(staging, StagingMark)) && !Directory.Exists(Path.Combine(staging, ".~tools")) && !File.Exists(Path.Combine(staging, "uninstall.exe")))
                throw new SetupError(Exit.Failed, Fmt.F(L.T("The folder {0} is in the way. Move or delete it, then try again.", "پوشه {0} مانع نصب است. آن را جابه‌جا یا حذف کنید و دوباره امتحان کنید."), Fmt.Ltr(staging)));
            Log("Removing the leftovers of an earlier run: " + staging);
            Files.DeleteTree(staging, null);
        }
        if (Directory.Exists(oldDir))
        {
            if (!Directory.Exists(target))
            {
                Log("Restoring the version an earlier run left aside.");
                Directory.Move(oldDir, target);
            }
            else
            {
                // It may hold a portable library that never made it across. Keep it out of the way, never delete it.
                for (int i = 1; Directory.Exists(oldDir); i++)
                    oldDir = target + ".~old" + i.ToString(CultureInfo.InvariantCulture);
                Log("An earlier run left a folder aside. It stays as it is.");
            }
        }
    }

    void CloseApp()
    {
        if (!Directory.Exists(target) || Apps.Running(target).Count == 0) return;
        S.Step(L.T("Closing MovieShelf", "در حال بستن MovieShelf"));
        Log("MovieShelf is running from the install folder. Asking it to close.");
        if (!Apps.Close(target, 6000))
            throw new SetupError(Exit.Failed, L.T("MovieShelf is still running and could not be closed. Close it, then try again.", "MovieShelf هنوز در حال اجراست و بسته نشد. آن را ببندید و دوباره امتحان کنید."));
    }

    /// <summary>Creates the folders above the target. Returns those that did not exist, deepest first.</summary>
    List<string> MakeParents()
    {
        var made = new List<string>();
        string p = Path.GetDirectoryName(target);
        while (p != null && !Directory.Exists(p))
        {
            made.Add(p);
            p = Path.GetDirectoryName(p);
        }
        if (made.Count > 0)
        {
            Directory.CreateDirectory(made[0]);
            var list = new List<string>(made);
            undo.Add(() => { foreach (string d in list) Files.RemoveIfEmpty(d); });
        }
        return made;
    }

    void Swap(Manifest m, Manifest previous)
    {
        bool exists = Directory.Exists(target);
        bool earlier = exists && (previous != null || File.Exists(Path.Combine(target, "MovieShelf.exe")));
        if (earlier)
        {
            Log(previous != null ? "Replacing the earlier install." : "Replacing an earlier install without a manifest.");
            try { Files.MoveDir(target, oldDir, null); }
            catch (Exception e)
            {
                if (e is IOException || e is UnauthorizedAccessException)
                    throw new SetupError(Exit.Failed, Fmt.F(L.T("Another program is using files in {0}. Close it, then try again.", "برنامه دیگری از فایل‌های {0} استفاده می‌کند. آن را ببندید و دوباره امتحان کنید."), Fmt.Ltr(target)));
                throw;
            }
            undo.Add(() => Directory.Move(oldDir, target));
            Files.MoveDir(staging, target, null);
            undo.Add(() => Files.DeleteTree(target, null));
            m["ownroot"] = previous == null || previous["ownroot"] != "0" ? "1" : "0";
            CarryOver(previous, "");
        }
        else if (exists && Files.RemoveIfEmpty(target) == false)
        {
            Log("The folder holds other files. Adding to it.");
            m["ownroot"] = P.OwnRoot ? "1" : "0";
            Merge(m);
        }
        else
        {
            Files.MoveDir(staging, target, null);
            undo.Add(() => Files.DeleteTree(target, null));
            m["ownroot"] = "1";
        }
    }

    /// <summary>Moves what the old version's folder holds that no installer put there (a portable library, say) into the new folder.</summary>
    void CarryOver(Manifest previous, string rel)
    {
        string from = Path.Combine(oldDir, rel), to = Path.Combine(target, rel);
        foreach (string d in Directory.GetDirectories(from))
        {
            string name = Path.Combine(rel, Path.GetFileName(d));
            bool ours = previous != null && previous.HasFilesUnder(name);
            bool legacyProgram = previous == null && !Files.Same(name, "data");
            if (legacyProgram) continue;
            string dest = Path.Combine(target, name);
            if (!ours && !Directory.Exists(dest)) MoveBack(d, dest, true);
            else CarryOver(previous, name);
        }
        foreach (string f in Directory.GetFiles(from))
        {
            string name = Path.Combine(rel, Path.GetFileName(f));
            string dest = Path.Combine(to, Path.GetFileName(f));
            if (File.Exists(dest)) continue;
            if (previous != null ? previous.HasFile(name) : !Files.Same(name, "portable.txt")) continue;
            MoveBack(f, dest, false);
        }
    }

    void MoveBack(string from, string to, bool dir)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(to));
        if (dir) Directory.Move(from, to); else File.Move(from, to);
        undo.Add(() => { if (dir) Directory.Move(to, from); else File.Move(to, from); });
        Log("Kept " + Path.GetFileName(to));
    }

    /// <summary>The target holds files that are not ours. Add to it; a file of the same name goes back where it was on rollback.</summary>
    void Merge(Manifest m)
    {
        string backup = mergeBackup = target + ".~bak";
        var existingDirs = new List<string>(m.Dirs);
        m.Dirs.Clear();
        foreach (string d in existingDirs)
        {
            string dest = Path.Combine(target, d);
            if (Directory.Exists(dest)) continue;
            Directory.CreateDirectory(dest);
            m.Dirs.Add(d);
            string captured = dest;
            undo.Add(() => Files.RemoveIfEmpty(captured));
        }
        foreach (string rel in m.Files_)
        {
            string from = Path.Combine(staging, rel), dest = Path.Combine(target, rel);
            if (!File.Exists(from)) continue;
            if (File.Exists(dest))
            {
                string keep = Path.Combine(backup, rel);
                Directory.CreateDirectory(Path.GetDirectoryName(keep));
                File.Move(dest, keep);
                string d1 = dest, k1 = keep;
                undo.Add(() => File.Move(k1, d1));
            }
            File.Move(from, dest);
            string t1 = dest;
            undo.Add(() => File.Delete(t1));
        }
        undo.Add(() => Files.DeleteTree(backup, null));
        Files.DeleteTree(staging, null);
    }

    void Shortcuts(Manifest m, Manifest previous)
    {
        string exe = Path.Combine(target, "MovieShelf.exe");
        if (P.StartMenu) OneShortcut(m, previous, Shell.ProgramsDir(P.AllUsers), exe);
        if (P.Desktop) OneShortcut(m, previous, Shell.DesktopDir(P.AllUsers), exe);
    }

    void OneShortcut(Manifest m, Manifest previous, string dir, string exe)
    {
        Directory.CreateDirectory(dir);
        string link = Shell.PathFor(dir, exe, previous);
        byte[] before = File.Exists(link) ? File.ReadAllBytes(link) : null;
        undo.Add(() =>
        {
            if (before != null) File.WriteAllBytes(link, before);
            else File.Delete(link);
        });
        Shell.Create(link, exe, target);
        m.Shortcuts.Add(link);
        Log("Shortcut: " + link);
    }

    void WriteLog()
    {
        try
        {
            File.WriteAllText(Path.Combine(target, LogName), logHead + ownLog, new UTF8Encoding(false));
        }
        catch (Exception e) { Log("Could not write the install log: " + e.Message); }
    }

    static string ReadShared(string path)
    {
        using (var s = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
        using (var r = new StreamReader(s, Encoding.UTF8)) return r.ReadToEnd();
    }
}

class Downloader
{
    public Func<bool> Stopped;
    public Action<string> Log;
    /// <summary>Bytes so far, total (0 when unknown), bytes per second.</summary>
    public Action<long, long, double> Progress;
    public int Attempts = 4;
    public int DelayMs = 2000;

    static HttpWebRequest Request(string url, string method, int timeoutMs)
    {
        var r = (HttpWebRequest)WebRequest.Create(url);
        r.Method = method;
        r.UserAgent = "MovieShelf-Setup/" + BuildInfo.Version;
        r.Timeout = timeoutMs;
        r.ReadWriteTimeout = 60000;
        r.AllowAutoRedirect = true;
        r.AutomaticDecompression = DecompressionMethods.None;
        return r;
    }

    /// <summary>The size on the server, or -1.</summary>
    public static long Head(string url, int timeoutMs)
    {
        try
        {
            using (var resp = (HttpWebResponse)Request(url, "HEAD", timeoutMs).GetResponse())
                return resp.ContentLength;
        }
        catch (Exception) { return -1; }
    }

    public static string Text(string url)
    {
        using (var resp = (HttpWebResponse)Request(url, "GET", 30000).GetResponse())
        using (var r = new StreamReader(resp.GetResponseStream(), Encoding.ASCII)) return r.ReadToEnd();
    }

    /// <summary>Gets url into a .part file, resuming with Range after any failure. Raises IOException when it gives up; the file stays for a later resume.</summary>
    public void Get(string url, string part)
    {
        int failures = 0;
        Exception last = null;
        while (failures < Attempts)
        {
            if (Stopped()) throw new CancelledException();
            long before = File.Exists(part) ? new FileInfo(part).Length : 0;
            try
            {
                if (Attempt(url, part, before)) return;
            }
            catch (CancelledException) { throw; }
            catch (Exception e)
            {
                if (!(e is WebException || e is IOException || e is ObjectDisposedException)) throw;
                last = e;
                if (Log != null) Log(Path.GetFileName(url) + ": " + e.Message);
                if (Permanent(e)) throw new IOException(e.Message);
            }
            long after = File.Exists(part) ? new FileInfo(part).Length : 0;
            failures = after > before ? 1 : failures + 1; // a connection that keeps moving forward is worth keeping at
            if (failures >= Attempts) break;
            int wait = DelayMs << Math.Min(failures - 1, 3);
            for (int t = 0; t < wait; t += 100)
            {
                if (Stopped()) throw new CancelledException();
                Thread.Sleep(100);
            }
        }
        throw new IOException(last != null ? last.Message : "Download failed.");
    }

    /// <summary>A 404 or 403 will not get better by asking again.</summary>
    static bool Permanent(Exception e)
    {
        var we = e as WebException;
        var r = we == null ? null : we.Response as HttpWebResponse;
        if (r == null) return false;
        int code = (int)r.StatusCode;
        return code >= 400 && code < 500 && code != 408 && code != 429;
    }

    bool Attempt(string url, string part, long have)
    {
        HttpWebRequest req = Request(url, "GET", 30000);
        if (have > 0) req.AddRange(have);
        HttpWebResponse resp;
        try { resp = (HttpWebResponse)req.GetResponse(); }
        catch (WebException e)
        {
            var r = e.Response as HttpWebResponse;
            if (r == null || (int)r.StatusCode != 416) throw;
            r.Close();
            // Nothing past the end of the file: it is complete, or the file on the server changed.
            long size = Head(url, 15000);
            if (size > 0 && size == have) return true;
            File.Delete(part);
            return false;
        }
        using (resp)
        {
            bool partial = resp.StatusCode == HttpStatusCode.PartialContent;
            long total = resp.ContentLength < 0 ? 0 : resp.ContentLength + (partial ? have : 0);
            long got = partial ? have : 0;
            using (Stream src = resp.GetResponseStream())
            using (var dst = new FileStream(part, partial ? FileMode.Append : FileMode.Create, FileAccess.Write, FileShare.Read))
            {
                var buf = new byte[1 << 16];
                var clock = Stopwatch.StartNew();
                long markBytes = got; long markMs = 0, lastReport = -1000; double speed = 0;
                while (true)
                {
                    if (Stopped()) throw new CancelledException();
                    int n = src.Read(buf, 0, buf.Length);
                    if (n <= 0) break;
                    dst.Write(buf, 0, n);
                    got += n;
                    long now = clock.ElapsedMilliseconds;
                    if (now - markMs >= 1000)
                    {
                        double recent = (got - markBytes) * 1000.0 / (now - markMs);
                        speed = speed == 0 ? recent : speed * 0.7 + recent * 0.3;
                        markBytes = got; markMs = now;
                    }
                    if (now - lastReport >= 200 && Progress != null) { Progress(got, total, speed); lastReport = now; }
                }
                if (Progress != null) Progress(got, total, speed);
            }
            if (total > 0 && got < total) throw new IOException("The connection closed early, at " + got + " of " + total + " bytes.");
            return true;
        }
    }
}

/// <summary>Sends a CommitPlan to the Committer: here, or in the elevated copy of this program.</summary>
interface IWorker
{
    int Commit(CommitPlan plan, ISink sink, Func<bool> stopped, out string message);
}

class LocalWorker : IWorker
{
    public int Commit(CommitPlan plan, ISink sink, Func<bool> stopped, out string message)
    {
        var c = new Committer(plan, sink, stopped);
        int code = c.Run();
        message = c.Message;
        return code;
    }
}

/// <summary>
/// The elevated copy. It starts first, so the one UAC prompt comes right after the Install button and not after hours of
/// downloads, then waits for its plan. Files in a private folder carry the conversation: plan.txt and go one way, progress.txt the other.
/// </summary>
class ElevatedWorker : IWorker, IDisposable
{
    readonly string ipc;
    Process process;

    public ElevatedWorker(string folder) { ipc = folder; Directory.CreateDirectory(ipc); }

    /// <summary>Raises Win32Exception 1223 when the user declines the prompt.</summary>
    public void Start(string args)
    {
        if (Env.Test("UAC") == "decline") throw new Win32Exception(1223);
        var psi = new ProcessStartInfo(Env.Self, "/_WORKER=\"" + ipc + "\" /_PARENT=" + Process.GetCurrentProcess().Id + args)
        {
            UseShellExecute = true, WindowStyle = ProcessWindowStyle.Hidden
        };
        // The test switch stops here on purpose: everything else about this path runs for real.
        if (Env.Test("NOUAC") == null) psi.Verb = "runas";
        process = Process.Start(psi);
    }

    public void Send(Dictionary<string, string> plan)
    {
        string tmp = Path.Combine(ipc, "plan.tmp");
        Kv.Save(tmp, plan);
        File.Move(tmp, Path.Combine(ipc, "plan.txt"));
    }

    /// <summary>Tells the worker to stop and gives it a moment, because the folder that carries the message is about to go.</summary>
    public void Abort()
    {
        try
        {
            File.WriteAllBytes(Path.Combine(ipc, "cancel"), new byte[0]);
            if (process != null) process.WaitForExit(3000);
        }
        catch (Exception) { }
    }

    bool Exited()
    {
        try { return process == null || process.WaitForExit(0); }
        catch (Exception) { return false; }
    }

    public int Commit(CommitPlan plan, ISink sink, Func<bool> stopped, out string message)
    {
        Send(plan.ToKv());
        return Follow(sink, stopped, null, out message);
    }

    /// <summary>Reads the worker's progress until it reports a result. onAlmost runs when the worker says it only waits for the parent.</summary>
    public int Follow(ISink sink, Func<bool> stopped, Action onAlmost, out string message)
    {
        string file = Path.Combine(ipc, "progress.txt");
        long pos = 0;
        bool sentCancel = false;
        var line = new StringBuilder();
        message = null;
        while (true)
        {
            bool exited = Exited();
            if (stopped() && !sentCancel) { Abort(); sentCancel = true; }
            if (File.Exists(file))
            {
                using (var s = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                {
                    long end = s.Length;
                    var bytes = new byte[end - pos];
                    s.Seek(pos, SeekOrigin.Begin);
                    int have = 0, n;
                    while (have < bytes.Length && (n = s.Read(bytes, have, bytes.Length - have)) > 0) have += n;
                    pos += have;
                    foreach (char c in Encoding.UTF8.GetString(bytes, 0, have))
                    {
                        if (c != '\n') { line.Append(c); continue; }
                        string l = line.ToString();
                        line.Length = 0;
                        if (l.StartsWith("R|", StringComparison.Ordinal))
                        {
                            string[] p = l.Split(new[] { '|' }, 3);
                            message = p.Length > 2 ? p[2] : null;
                            return int.Parse(p[1], CultureInfo.InvariantCulture);
                        }
                        if (l == "A|") { if (onAlmost != null) onAlmost(); continue; }
                        FileSink.Replay(l, sink);
                    }
                }
            }
            if (exited)
            {
                message = L.T("The elevated part of setup stopped unexpectedly.", "بخش مدیریتی نصب به‌طور ناگهانی متوقف شد.");
                return Exit.Failed;
            }
            Thread.Sleep(120);
        }
    }

    public void Dispose() { if (process != null) process.Dispose(); }
}

/// <summary>The worker's ISink: one line per event, replayed by the wizard.</summary>
class FileSink : ISink
{
    readonly string path;
    public FileSink(string file) { path = file; }

    void Put(string line)
    {
        using (var s = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete))
        {
            byte[] b = new UTF8Encoding(false).GetBytes(line.Replace('\n', ' ').Replace('\r', ' ') + "\n");
            s.Write(b, 0, b.Length);
        }
    }

    public void Step(string text) { Put("S|" + text); }
    public void Detail(string text) { Put("D|" + text); }
    public void Progress(double f) { Put("P|" + f.ToString("0.###", CultureInfo.InvariantCulture)); }
    public void Log(string line) { Put("L|" + line); }
    public void Locked(bool locked) { Put("K|" + (locked ? "1" : "0")); }
    public void Almost() { Put("A|"); }
    public void Result(int code, string message) { Put("R|" + code.ToString(CultureInfo.InvariantCulture) + "|" + message); }

    public static void Replay(string l, ISink to)
    {
        if (l.Length < 2) return;
        string v = l.Substring(2);
        switch (l[0])
        {
            case 'S': to.Step(v); break;
            case 'D': to.Detail(v); break;
            case 'P': to.Progress(double.Parse(v, CultureInfo.InvariantCulture)); break;
            case 'L': to.Log(v); break;
            case 'K': to.Locked(v == "1"); break;
        }
    }
}

/// <summary>Maps the 0..1 of one phase onto a slice of the overall bar.</summary>
class Slice : ISink
{
    readonly ISink to;
    readonly double from, width;
    public Slice(ISink sink, double start, double end) { to = sink; from = start; width = end - start; }
    public void Step(string text) { to.Step(text); }
    public void Detail(string text) { to.Detail(text); }
    public void Progress(double f) { to.Progress(from + width * Math.Max(0, Math.Min(1, f))); }
    public void Log(string line) { to.Log(line); }
    public void Locked(bool locked) { to.Locked(locked); }
}

/// <summary>A whole install: verify, elevate, download, commit.</summary>
class Runner
{
    readonly Choices C;
    readonly SetupFile Sf;
    readonly ISink S;
    readonly Logger Logg;
    readonly string temp;
    readonly List<string> createdFiles = new List<string>();
    readonly List<string> createdDirs = new List<string>();
    volatile bool stop;

    /// <summary>Optional downloads that failed, as text for the last page.</summary>
    public readonly List<string> Failed = new List<string>();
    public string Message;
    public bool ImdbFailed;
    public bool UacDeclined;

    public Runner(Choices choices, SetupFile setup, ISink sink, Logger log)
    {
        C = choices; Sf = setup; S = sink; Logg = log;
        temp = Path.Combine(Env.Temp, "MovieShelf-Setup-" + Guid.NewGuid().ToString("N"));
    }

    public void Stop() { stop = true; }
    bool Stopped() { return stop; }

    public int Run()
    {
        Directory.CreateDirectory(temp);
        ElevatedWorker elevated = null;
        try
        {
            string dataDir = Env.DataDir(C.InstallDir, C.Portable);
            S.Log("MovieShelf setup " + BuildInfo.Version + ", Windows build " + Native.WindowsBuild() + ", language " + L.Tag);
            S.Log("Library folder: " + dataDir);

            S.Step(L.T("Checking the setup file", "در حال بررسی فایل نصب"));
            if (!Sf.Verify(S.Range(0, 0.02), Stopped)) throw new SetupError(Exit.Damaged, Texts.Damaged());

            IWorker worker = new LocalWorker();
            if (C.AllUsers && !Env.IsAdmin)
            {
                elevated = new ElevatedWorker(Path.Combine(temp, "ipc"));
                try { elevated.Start(""); }
                catch (Win32Exception e)
                {
                    if (e.NativeErrorCode != 1223) throw;
                    UacDeclined = true;
                    Message = L.T("Windows did not get administrator permission, so MovieShelf cannot be installed for all users.", "ویندوز مجوز مدیر سیستم را دریافت نکرد، بنابراین MovieShelf برای همه کاربران نصب نمی‌شود.");
                    S.Log("The administrator prompt was declined.");
                    return Exit.Declined;
                }
                worker = elevated;
            }

            long total = Sizes.Download(C, dataDir);
            double dlEnd = total > 0 ? 0.72 : 0.02;
            string ffprobe = "";
            bool rootThere = Directory.Exists(C.InstallDir);
            if (total > 0)
            {
                long imdbBytes = C.Imdb ? Sizes.ImdbToFetch(dataDir) : 0;
                double mid = 0.02 + (dlEnd - 0.02) * imdbBytes / total;
                if (C.Imdb) Imdb(dataDir, new Slice(S, 0.02, mid));
                if (C.Ffprobe) ffprobe = Ffprobe(new Slice(S, mid, dlEnd));
            }
            if (stop) throw new CancelledException();
            Space();

            var plan = new CommitPlan
            {
                SetupExe = Sf.Path_, InstallDir = C.InstallDir, Version = BuildInfo.Version, FfprobeFile = ffprobe, ParentLog = Logg.Path, Lang = L.Tag,
                AllUsers = C.AllUsers, Portable = C.Portable, Desktop = C.Desktop, StartMenu = C.StartMenu, KeepFfprobe = C.KeepFfprobe,
                VerifySelf = elevated != null, OwnRoot = !rootThere,
                Components = "app" + (C.Imdb ? ",imdb" : "") + (C.Ffprobe || C.KeepFfprobe ? ",ffprobe" : "")
            };
            string msg;
            int code = worker.Commit(plan, new Slice(S, dlEnd, 1), Stopped, out msg);
            if (code != Exit.Ok)
            {
                Message = msg;
                if (code == Exit.Cancelled) throw new CancelledException();
                UndoDownloads();
                return code;
            }
            return Failed.Count > 0 ? Exit.Partial : Exit.Ok;
        }
        catch (CancelledException)
        {
            if (elevated != null) elevated.Abort();
            S.Locked(true);
            S.Step(L.T("Undoing the changes", "در حال بازگرداندن تغییرات"));
            UndoDownloads();
            Message = L.T("Installation cancelled.", "نصب لغو شد.");
            return Exit.Cancelled;
        }
        catch (SetupError e)
        {
            if (elevated != null) elevated.Abort();
            UndoDownloads();
            Message = e.Message;
            S.Log("Failed: " + e.Message);
            return e.Code;
        }
        catch (Exception e)
        {
            if (elevated != null) elevated.Abort();
            UndoDownloads();
            Message = e.Message;
            S.Log("Failed: " + e);
            return Exit.Failed;
        }
        finally
        {
            if (elevated != null) elevated.Dispose();
            Files.DeleteTree(temp, null);
        }
    }

    void Space()
    {
        long need = Sf.UnpackedSize + Sf.PayloadLength + (C.Ffprobe ? Sizes.FfprobeOnDisk : 0);
        long free = Native.FreeBytes(C.InstallDir);
        if (free >= 0 && free < need)
            throw new SetupError(Exit.Failed, Fmt.F(L.T("There is not enough free space on the drive: {0} needed, {1} free.", "فضای خالی درایو کافی نیست: {0} لازم است و {1} آزاد است."), Fmt.Size(need), Fmt.Size(free)));
    }

    void TrackDir(string dir)
    {
        var missing = new List<string>();
        string p = dir;
        while (p != null && !Directory.Exists(p)) { missing.Add(p); p = Path.GetDirectoryName(p); }
        missing.Reverse();
        Directory.CreateDirectory(dir);
        createdDirs.AddRange(missing);
    }

    void UndoDownloads()
    {
        foreach (string f in createdFiles) Files.TryDelete(f);
        for (int i = createdDirs.Count - 1; i >= 0; i--) Files.RemoveIfEmpty(createdDirs[i]);
        createdFiles.Clear();
        createdDirs.Clear();
    }

    Downloader MakeDownloader(ISink s, string label, long doneBefore, long grandTotal)
    {
        var d = new Downloader { Stopped = Stopped, Log = S.Log };
        string test = Env.Test("RETRY_MS");
        if (test != null) d.DelayMs = int.Parse(test, CultureInfo.InvariantCulture);
        d.Progress = (got, total, speed) =>
        {
            if (grandTotal > 0) s.Progress((double)(doneBefore + got) / grandTotal);
            string text = Fmt.Ltr(label) + L.T(": ", ": ") + Fmt.Size(got) + (total > 0 ? L.T(" of ", " از ") + Fmt.Size(total) : "");
            if (speed > 0)
            {
                text += L.T(", ", "، ") + Fmt.Size((long)speed) + L.T("/s", " در ثانیه");
                if (total > got) text += L.T(", ", "، ") + Fmt.Time((total - got) / speed) + L.T(" left", " مانده");
            }
            s.Detail(text);
        };
        return d;
    }

    void Imdb(string dataDir, ISink s)
    {
        string dir = Path.Combine(dataDir, "imdb");
        TrackDir(dir);
        long total = Sizes.ImdbToFetch(dataDir), done = 0;
        var failed = new List<string>();
        int inARow = 0;
        foreach (ImdbFile f in Sizes.Imdb)
        {
            if (Sizes.IsFresh(dataDir, f)) { s.Log(f.FileName + " is less than " + Sizes.FreshDays + " days old. Skipped."); continue; }
            if (stop) throw new CancelledException();
            if (inARow >= 2) { failed.Add(f.Name); s.Log("Not trying " + f.FileName + " after two failures in a row."); continue; }
            s.Step(L.T("Downloading IMDb data", "در حال دانلود داده‌های IMDb"));
            string dest = Path.Combine(dir, f.FileName), part = dest + ".part";
            bool partExisted = File.Exists(part), destExisted = File.Exists(dest);
            if (!partExisted) createdFiles.Add(part);
            if (!destExisted) createdFiles.Add(dest);
            try
            {
                MakeDownloader(s, f.FileName, done, total).Get(Env.ImdbBase + f.FileName, part);
                if (!IsGzip(part))
                {
                    File.Delete(part);
                    throw new IOException("The server did not send a gzip file.");
                }
                if (File.Exists(dest)) File.Delete(dest);
                File.Move(part, dest);
                s.Log("Downloaded " + f.FileName);
                inARow = 0;
            }
            catch (IOException e) { failed.Add(f.Name); inARow++; s.Log("Could not download " + f.FileName + ": " + e.Message); }
            catch (UnauthorizedAccessException e) { failed.Add(f.Name); inARow++; s.Log("Could not write " + f.FileName + ": " + e.Message); }
            done += f.Size;
        }
        if (failed.Count > 0)
        {
            ImdbFailed = true;
            Failed.Add(L.T("IMDb data: ", "داده‌های IMDb: ") + Fmt.Ltr(string.Join(", ", failed.ToArray())));
        }
    }

    static bool IsGzip(string file)
    {
        using (FileStream s = File.OpenRead(file)) return s.ReadByte() == 0x1F && s.ReadByte() == 0x8B;
    }

    /// <summary>Returns the path of the unpacked ffprobe.exe, or "" when the download failed.</summary>
    string Ffprobe(ISink s)
    {
        try
        {
            s.Step(L.T("Downloading ffprobe", "در حال دانلود ffprobe"));
            string zip = Path.Combine(temp, "ffmpeg.zip");
            string expected = Texts.FirstHex(Downloader.Text(Env.FfmpegUrl + ".sha256"));
            if (expected == null) throw new IOException("The .sha256 file holds no checksum.");
            MakeDownloader(new Slice(s, 0, 0.9), "ffmpeg-release-essentials.zip", 0, Sizes.FfmpegZip).Get(Env.FfmpegUrl, zip + ".part");
            if (stop) throw new CancelledException();
            File.Move(zip + ".part", zip);

            s.Step(L.T("Checking the ffprobe download", "در حال بررسی فایل ffprobe"));
            string actual = Files.Sha256(zip, null, Stopped);
            if (!string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase))
                throw new IOException("The checksum does not match: expected " + expected + ", got " + actual + ".");
            s.Log("ffmpeg download verified: " + actual);

            string tools = Path.Combine(temp, "tools"), output = Path.Combine(temp, "ffprobe");
            Sf.ExtractTools(tools);
            Directory.CreateDirectory(output);
            var lines = new List<string>();
            int code = SevenZip.Run(tools, "e \"" + zip + "\" -o\"" + output + "\" ffprobe.exe -r", null, Stopped, lines);
            string exe = Path.Combine(output, "ffprobe.exe");
            if (code != 0 || !File.Exists(exe)) throw new IOException("bin\\ffprobe.exe was not found in the download (7-Zip exit code " + code + ").");
            s.Progress(1);
            File.Delete(zip);
            return exe;
        }
        catch (WebException e) { FfprobeFailed(s, e); }
        catch (IOException e) { FfprobeFailed(s, e); }
        return "";
    }

    void FfprobeFailed(ISink s, Exception e)
    {
        s.Log("Could not get ffprobe: " + e.Message);
        Failed.Add(L.T("ffprobe, for reading the real quality of video files", "ffprobe، برای خواندن کیفیت واقعی فایل‌های ویدیویی"));
    }
}

static class SinkExtensions
{
    public static Action<double> Range(this ISink s, double from, double to)
    {
        return f => s.Progress(from + (to - from) * f);
    }
}

class Logger
{
    readonly object gate = new object();
    public readonly string Path;
    public Logger(string file) { Path = file; }

    public void Write(string line)
    {
        lock (gate)
        {
            try { File.AppendAllText(Path, DateTime.Now.ToString("HH:mm:ss ", CultureInfo.InvariantCulture) + line + "\r\n", new UTF8Encoding(false)); }
            catch (Exception) { }
        }
    }
}
