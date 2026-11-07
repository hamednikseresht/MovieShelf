// Shared helpers of the MovieShelf setup program: language, formatting, environment, setup-file layout, the install
// manifest, shortcuts and the uninstall registry entry. C# 5 only: this is compiled with the csc.exe of .NET Framework 4.
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using System.Threading;
using Microsoft.Win32;

/// <summary>Exit codes of the setup program and the uninstaller. README.md lists them.</summary>
static class Exit
{
    public const int Ok = 0, Failed = 1, BadArguments = 2, Cancelled = 3, Requirements = 4, Damaged = 5, Partial = 6, Declined = 1223;
}

/// <summary>User stopped the work.</summary>
class CancelledException : Exception { }

/// <summary>A failure with a message meant for the user.</summary>
class SetupError : Exception
{
    public readonly int Code;
    public SetupError(int code, string message) : base(message) { Code = code; }
}

/// <summary>The language of the UI. English and Persian text sit side by side at each call.</summary>
static class L
{
    public static bool Fa;
    public static string T(string en, string fa) { return Fa ? fa : en; }
    public static string Tag { get { return Fa ? "fa" : "en"; } }
}

static class Fmt
{
    static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    /// <summary>Latin digits become Persian ones in the Persian UI.</summary>
    public static string Num(string s)
    {
        if (!L.Fa || s == null) return s;
        var b = new StringBuilder(s.Length);
        foreach (char c in s) b.Append(c >= '0' && c <= '9' ? (char)('۰' + (c - '0')) : c);
        return b.ToString();
    }

    public static string Num(long n) { return Num(n.ToString(Inv)); }

    public static string Size(long bytes)
    {
        const double KB = 1024, MB = KB * 1024, GB = MB * 1024;
        if (bytes < 0) bytes = 0;
        double v; string u;
        if (bytes >= GB) { v = bytes / GB; u = L.T("GB", "گیگابایت"); }
        else if (bytes >= MB) { v = bytes / MB; u = L.T("MB", "مگابایت"); }
        else { v = bytes / KB; u = L.T("KB", "کیلوبایت"); }
        string n = v.ToString(v >= 100 || bytes < MB ? "0" : "0.0", Inv);
        if (L.Fa) n = Num(n).Replace('.', '٫');
        return n + " " + u;
    }

    public static string Time(double seconds)
    {
        long s = (long)Math.Ceiling(seconds);
        if (s < 60) return Num(s) + " " + L.T("s", "ثانیه");
        if (s < 3600) return Num(s / 60) + " " + L.T("min", "دقیقه") + (s % 60 > 0 && s < 600 ? L.T(" ", " و ") + Num(s % 60) + " " + L.T("s", "ثانیه") : "");
        return Num(s / 3600) + " " + L.T("h", "ساعت") + L.T(" ", " و ") + Num(s / 60 % 60) + " " + L.T("min", "دقیقه");
    }

    /// <summary>Keeps a path or a Latin phrase in reading order inside right-to-left text.</summary>
    public static string Ltr(string s) { return L.Fa ? "‪" + s + "‬" : s; }

    public static string F(string format, params object[] args) { return string.Format(Inv, format, args); }
}

static class Env
{
    public static string Var(string name)
    {
        string v = Environment.GetEnvironmentVariable(name);
        return string.IsNullOrEmpty(v) ? null : v;
    }

    /// <summary>The hidden test switches: MOVIESHELF_SETUP_TEST_*.</summary>
    public static string Test(string name) { return Var("MOVIESHELF_SETUP_TEST_" + name); }

    public static string LocalAppData
    {
        get { return Var("LOCALAPPDATA") ?? Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData); }
    }

    public static string ProgramFiles
    {
        get { return Var("ProgramW6432") ?? Var("ProgramFiles") ?? Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles); }
    }

    /// <summary>The same order as the app: MOVIESHELF_HOME, the program folder with portable.txt, then %LOCALAPPDATA%\MovieShelf.</summary>
    public static string DataRoot(string installDir, bool portable)
    {
        string home = Var("MOVIESHELF_HOME");
        if (home != null) return Path.GetFullPath(home);
        if (portable && installDir != null) return installDir;
        return Path.Combine(LocalAppData, "MovieShelf");
    }

    public static string DataDir(string installDir, bool portable) { return Path.Combine(DataRoot(installDir, portable), "data"); }

    public static string DefaultDir(bool allUsers)
    {
        return allUsers ? Path.Combine(ProgramFiles, "MovieShelf") : Path.Combine(Path.Combine(LocalAppData, "Programs"), "MovieShelf");
    }

    public static bool IsAdmin
    {
        get
        {
            using (var id = WindowsIdentity.GetCurrent()) return new WindowsPrincipal(id).IsInRole(WindowsBuiltInRole.Administrator);
        }
    }

    public static string Self { get { return Process.GetCurrentProcess().MainModule.FileName; } }

    public static string Temp { get { return Path.GetTempPath(); } }

    public static string ImdbBase { get { return Test("IMDB_URL") ?? "https://datasets.imdbws.com/"; } }

    public static string FfmpegUrl { get { return Test("FFMPEG_URL") ?? "https://www.gyan.dev/ffmpeg/builds/ffmpeg-release-essentials.zip"; } }
}

static class Native
{
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct OsVersion
    {
        public uint Size, Major, Minor, Build, Platform;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string Csd;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct MemoryStatus
    {
        public uint Length, Load;
        public ulong TotalPhys, AvailPhys, TotalPage, AvailPage, TotalVirtual, AvailVirtual, AvailExtended;
    }

    [DllImport("ntdll.dll")] static extern int RtlGetVersion(ref OsVersion info);
    [DllImport("kernel32.dll", SetLastError = true)] static extern bool GlobalMemoryStatusEx(ref MemoryStatus status);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern bool GetDiskFreeSpaceEx(string dir, out ulong available, out ulong total, out ulong free);
    [DllImport("kernel32.dll", SetLastError = true)] static extern IntPtr OpenProcess(uint access, bool inherit, int pid);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern bool QueryFullProcessImageName(IntPtr process, int flags, StringBuilder name, ref int size);
    [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr handle);

    /// <summary>The real Windows build. Environment.OSVersion lies for programs without a matching manifest entry.</summary>
    public static int WindowsBuild()
    {
        string fake = Env.Test("BUILD");
        if (fake != null) return int.Parse(fake, CultureInfo.InvariantCulture);
        var v = new OsVersion();
        v.Size = (uint)Marshal.SizeOf(typeof(OsVersion));
        return RtlGetVersion(ref v) == 0 ? (int)v.Build : Environment.OSVersion.Version.Build;
    }

    public static long TotalMemory()
    {
        string fake = Env.Test("RAM_GB");
        if (fake != null) return long.Parse(fake, CultureInfo.InvariantCulture) << 30;
        var m = new MemoryStatus();
        m.Length = (uint)Marshal.SizeOf(typeof(MemoryStatus));
        return GlobalMemoryStatusEx(ref m) ? (long)m.TotalPhys : 0;
    }

    /// <summary>Free space on the drive of the path, which need not exist yet; -1 when the drive is not there.</summary>
    public static long FreeBytes(string path)
    {
        string fake = Env.Test("FREE_MB");
        if (fake != null) return long.Parse(fake, CultureInfo.InvariantCulture) << 20;
        string p = Files.NearestExisting(path);
        ulong available, total, free;
        return p != null && GetDiskFreeSpaceEx(p, out available, out total, out free) ? (long)available : -1;
    }

    public static string ProcessPath(int pid)
    {
        IntPtr h = OpenProcess(0x1000, false, pid); // PROCESS_QUERY_LIMITED_INFORMATION
        if (h == IntPtr.Zero) return null;
        try
        {
            var name = new StringBuilder(1024);
            int size = name.Capacity;
            return QueryFullProcessImageName(h, 0, name, ref size) ? name.ToString() : null;
        }
        finally { CloseHandle(h); }
    }
}

static class Files
{
    public static string NearestExisting(string path)
    {
        try
        {
            string p = Path.GetFullPath(path);
            while (p != null && !Directory.Exists(p)) p = Path.GetDirectoryName(p);
            return p;
        }
        catch (Exception) { return null; }
    }

    public static bool Same(string a, string b)
    {
        return string.Equals(a.TrimEnd('\\'), b.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase);
    }

    public static bool Inside(string path, string folder)
    {
        return path.StartsWith(folder.TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase);
    }

    public static bool CanWrite(string folder)
    {
        string p = NearestExisting(folder);
        if (p == null) return false;
        // A folder, not a file: on C:\ ordinary users may create folders but not files.
        string probe = Path.Combine(p, ".movieshelf-write-test-" + Guid.NewGuid().ToString("N"));
        try { Directory.CreateDirectory(probe); Directory.Delete(probe); return true; }
        catch (Exception) { return false; }
    }

    static bool IsLink(FileSystemInfo i) { return (i.Attributes & FileAttributes.ReparsePoint) != 0; }

    /// <summary>The size of a folder. Never follows junctions. Slow on large libraries: call it off the UI thread.</summary>
    public static long Size(string dir)
    {
        long sum = 0;
        var stack = new Stack<DirectoryInfo>();
        try { if (Directory.Exists(dir)) stack.Push(new DirectoryInfo(dir)); } catch (Exception) { }
        while (stack.Count > 0)
        {
            DirectoryInfo d = stack.Pop();
            try
            {
                foreach (FileSystemInfo i in d.EnumerateFileSystemInfos())
                {
                    if (IsLink(i)) continue;
                    var fi = i as FileInfo;
                    if (fi != null) sum += fi.Length; else stack.Push((DirectoryInfo)i);
                }
            }
            catch (Exception) { }
        }
        return sum;
    }

    /// <summary>Deletes a folder this program made, or one the user explicitly chose to delete. Junctions lose only the link: what they point at stays.</summary>
    public static void DeleteTree(string dir, List<string> failed)
    {
        if (!Directory.Exists(dir)) return;
        var info = new DirectoryInfo(dir);
        if (IsLink(info))
        {
            try { Directory.Delete(dir); } catch (Exception) { if (failed != null) failed.Add(dir); }
            return;
        }
        foreach (string sub in Directory.GetDirectories(dir)) DeleteTree(sub, failed);
        foreach (string f in Directory.GetFiles(dir))
            if (!TryDelete(f) && failed != null) failed.Add(f);
        try { Directory.Delete(dir); } catch (Exception) { if (failed != null) failed.Add(dir); }
    }

    public static bool TryDelete(string file)
    {
        for (int i = 0; i < 4; i++)
        {
            try
            {
                if (!File.Exists(file)) return true;
                File.SetAttributes(file, FileAttributes.Normal);
                File.Delete(file);
                return true;
            }
            catch (Exception) { Thread.Sleep(150); }
        }
        return false;
    }

    public static bool RemoveIfEmpty(string dir)
    {
        try
        {
            if (!Directory.Exists(dir)) return true;
            using (var e = Directory.EnumerateFileSystemEntries(dir).GetEnumerator())
                if (e.MoveNext()) return false;
            Directory.Delete(dir);
            return true;
        }
        catch (Exception) { return false; }
    }

    /// <summary>Directory.Move with retries: antivirus scanners and the indexer hold files for a moment.</summary>
    public static void MoveDir(string from, string to, Func<bool> cancelled)
    {
        for (int i = 0; ; i++)
        {
            try { Directory.Move(from, to); return; }
            catch (IOException) { if (i >= 14) throw; }
            catch (UnauthorizedAccessException) { if (i >= 14) throw; }
            if (cancelled != null && cancelled()) throw new CancelledException();
            Thread.Sleep(350);
        }
    }

    public static string Sha256(string file, Action<double> progress, Func<bool> cancelled)
    {
        using (var s = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete))
            return Sha.Hex(Sha.Hash(s, s.Length, progress, cancelled));
    }
}

static class Sha
{
    public static byte[] Hash(Stream s, long length, Action<double> progress, Func<bool> cancelled)
    {
        using (var sha = new SHA256CryptoServiceProvider())
        {
            var buf = new byte[1 << 20];
            long left = length, total = Math.Max(length, 1);
            while (left > 0)
            {
                if (cancelled != null && cancelled()) throw new CancelledException();
                int n = s.Read(buf, 0, (int)Math.Min(buf.Length, left));
                if (n <= 0) throw new EndOfStreamException();
                sha.TransformBlock(buf, 0, n, null, 0);
                left -= n;
                if (progress != null) progress(1.0 - (double)left / total);
            }
            sha.TransformFinalBlock(new byte[0], 0, 0);
            return sha.Hash;
        }
    }

    public static string Hex(byte[] b)
    {
        var s = new StringBuilder(b.Length * 2);
        foreach (byte x in b) s.Append(x.ToString("x2"));
        return s.ToString();
    }
}

/// <summary>The setup file: [setup program][tools.zip][payload.7z][footer]. uninstall.exe is the setup program part alone.</summary>
class SetupFile
{
    public const string Magic = "MDBSETU2";
    public const int FooterSize = 64; // tools length 8, payload length 8, unpacked size 8, SHA-256 32, magic 8

    public string Path_;
    public long StubLength, ToolsLength, PayloadLength, UnpackedSize;
    public byte[] Hash;

    /// <summary>Null when the file has no valid footer: uninstall.exe, or a setup file cut short.</summary>
    public static SetupFile Read(string path)
    {
        using (var s = Open(path))
        {
            if (s.Length < FooterSize) return null;
            var f = new byte[FooterSize];
            s.Seek(-FooterSize, SeekOrigin.End);
            ReadFully(s, f, FooterSize);
            if (Encoding.ASCII.GetString(f, FooterSize - 8, 8) != Magic) return null;
            var r = new SetupFile { Path_ = path };
            r.ToolsLength = BitConverter.ToInt64(f, 0);
            r.PayloadLength = BitConverter.ToInt64(f, 8);
            r.UnpackedSize = BitConverter.ToInt64(f, 16);
            r.Hash = new byte[32];
            Array.Copy(f, 24, r.Hash, 0, 32);
            r.StubLength = s.Length - FooterSize - r.ToolsLength - r.PayloadLength;
            return r.StubLength > 0 && r.ToolsLength > 0 && r.PayloadLength > 0 ? r : null;
        }
    }

    static FileStream Open(string path) { return new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 1 << 16); }

    static void ReadFully(Stream s, byte[] buf, int count)
    {
        int got = 0;
        while (got < count)
        {
            int n = s.Read(buf, got, count - got);
            if (n <= 0) throw new SetupError(Exit.Damaged, Texts.Damaged());
            got += n;
        }
    }

    /// <summary>Checks tools and payload against the SHA-256 in the footer.</summary>
    public bool Verify(Action<double> progress, Func<bool> cancelled)
    {
        using (var s = Open(Path_))
        {
            s.Seek(StubLength, SeekOrigin.Begin);
            byte[] h = Sha.Hash(s, ToolsLength + PayloadLength, progress, cancelled);
            for (int i = 0; i < 32; i++) if (h[i] != Hash[i]) return false;
            return true;
        }
    }

    /// <summary>Unpacks 7z.exe, 7z.dll and the 7-Zip licence.</summary>
    public void ExtractTools(string dir)
    {
        Directory.CreateDirectory(dir);
        var bytes = new byte[ToolsLength];
        using (var s = Open(Path_))
        {
            s.Seek(StubLength, SeekOrigin.Begin);
            ReadFully(s, bytes, bytes.Length);
        }
        using (var zip = new ZipArchive(new MemoryStream(bytes)))
            foreach (ZipArchiveEntry e in zip.Entries)
                using (Stream src = e.Open())
                using (FileStream dst = File.Create(Path.Combine(dir, e.Name))) src.CopyTo(dst);
    }

    public void CopyPayload(string target, Func<bool> cancelled) { Copy(StubLength + ToolsLength, PayloadLength, target, cancelled); }

    /// <summary>uninstall.exe: this same program without tools and payload.</summary>
    public void WriteUninstaller(string target) { Copy(0, StubLength, target, null); }

    void Copy(long start, long length, string target, Func<bool> cancelled)
    {
        using (var s = Open(Path_))
        using (FileStream dst = File.Create(target))
        {
            s.Seek(start, SeekOrigin.Begin);
            var buf = new byte[1 << 20];
            long left = length;
            while (left > 0)
            {
                if (cancelled != null && cancelled()) throw new CancelledException();
                int n = s.Read(buf, 0, (int)Math.Min(buf.Length, left));
                if (n <= 0) throw new SetupError(Exit.Damaged, Texts.Damaged());
                dst.Write(buf, 0, n);
                left -= n;
            }
        }
    }
}

static class SevenZip
{
    /// <summary>Runs 7z with -bsp1 and reports percent and current file. Returns its exit code; a 7z that was stopped raises CancelledException.</summary>
    public static int Run(string tools, string args, Action<double, string> progress, Func<bool> cancelled, List<string> output)
    {
        var psi = new ProcessStartInfo(Path.Combine(tools, "7z.exe"), args + " -bso0 -bse1 -bsp1 -sccUTF-8 -y")
        {
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true,
            WorkingDirectory = tools, StandardOutputEncoding = new UTF8Encoding(false)
        };
        using (Process p = Process.Start(psi))
        using (var finished = new ManualResetEvent(false))
        {
            var stop = new Thread(() =>
            {
                while (!finished.WaitOne(100))
                    if (cancelled != null && cancelled()) { try { p.Kill(); } catch (Exception) { } return; }
            });
            stop.IsBackground = true;
            stop.Start();

            var buf = new char[1024];
            var seg = new StringBuilder();
            int n;
            while ((n = p.StandardOutput.Read(buf, 0, buf.Length)) > 0)
                for (int i = 0; i < n; i++)
                {
                    char c = buf[i];
                    if (c != '\r' && c != '\n' && c != '\b') { seg.Append(c); continue; }
                    if (seg.Length > 0) Segment(seg.ToString(), progress, output);
                    seg.Length = 0;
                }
            if (seg.Length > 0) Segment(seg.ToString(), progress, output);
            p.WaitForExit();
            finished.Set();
            stop.Join();
            if (cancelled != null && cancelled()) throw new CancelledException();
            return p.ExitCode;
        }
    }

    static void Segment(string s, Action<double, string> progress, List<string> output)
    {
        int pct = s.IndexOf('%');
        if (pct > 0)
        {
            int start = pct;
            while (start > 0 && char.IsDigit(s[start - 1])) start--;
            int value;
            if (start < pct && int.TryParse(s.Substring(start, pct - start), out value))
            {
                int dash = s.IndexOf(" - ", pct, StringComparison.Ordinal);
                string file = dash > 0 ? s.Substring(dash + 3).Trim() : null;
                if (progress != null) progress(value / 100.0, file);
                return;
            }
        }
        string t = s.Trim();
        if (t.Length > 0 && output != null) output.Add(t);
    }
}

/// <summary>Plain key=value text, used by the elevated worker's plan.</summary>
static class Kv
{
    public static void Save(string path, Dictionary<string, string> d)
    {
        var s = new StringBuilder();
        foreach (var kv in d) s.Append(kv.Key).Append('=').Append(kv.Value ?? "").Append('\n');
        File.WriteAllText(path, s.ToString(), new UTF8Encoding(false));
    }

    public static Dictionary<string, string> Load(string path)
    {
        var d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (string line in File.ReadAllLines(path, Encoding.UTF8))
        {
            int i = line.IndexOf('=');
            if (i > 0) d[line.Substring(0, i)] = line.Substring(i + 1);
        }
        return d;
    }

    public static string Get(Dictionary<string, string> d, string key)
    {
        string v;
        return d.TryGetValue(key, out v) ? v : "";
    }

    public static bool Flag(Dictionary<string, string> d, string key) { return Get(d, key) == "1"; }
}

/// <summary>Every file and folder setup created. The uninstaller deletes only what is listed here.</summary>
class Manifest
{
    public const string FileName = "install-manifest.txt";

    public readonly Dictionary<string, string> Meta = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    public readonly List<string> Files_ = new List<string>();  // relative to the install folder
    public readonly List<string> Dirs = new List<string>();    // relative, created by setup
    public readonly List<string> Shortcuts = new List<string>();
    public readonly List<string> Parents = new List<string>(); // folders above the install folder that setup created
    public long Bytes;

    HashSet<string> index, dirIndex;

    public string this[string key] { get { return Kv.Get(Meta, key); } set { Meta[key] = value; } }

    /// <summary>Lists a folder that setup has just filled: everything in it is setup's own.</summary>
    public static Manifest FromFolder(string root)
    {
        var m = new Manifest();
        m.Walk(root, "");
        return m;
    }

    void Walk(string dir, string rel)
    {
        foreach (string f in Directory.GetFiles(dir))
        {
            Files_.Add(rel + Path.GetFileName(f));
            Bytes += new FileInfo(f).Length;
        }
        foreach (string d in Directory.GetDirectories(dir))
        {
            string r = rel + Path.GetFileName(d);
            Dirs.Add(r);
            Walk(d, r + "\\");
        }
    }

    public void Save(string path)
    {
        var s = new StringBuilder("# MovieShelf install manifest 1\n");
        foreach (var kv in Meta) s.Append("M ").Append(kv.Key).Append('=').Append(kv.Value).Append('\n');
        foreach (string x in Files_) s.Append("F ").Append(x).Append('\n');
        foreach (string x in Dirs) s.Append("D ").Append(x).Append('\n');
        foreach (string x in Shortcuts) s.Append("S ").Append(x).Append('\n');
        foreach (string x in Parents) s.Append("P ").Append(x).Append('\n');
        File.WriteAllText(path, s.ToString(), new UTF8Encoding(false));
    }

    /// <summary>Null when the file is missing or not a manifest.</summary>
    public static Manifest Load(string path)
    {
        try
        {
            if (!File.Exists(path)) return null;
            string[] lines = File.ReadAllLines(path, Encoding.UTF8);
            if (lines.Length == 0 || !lines[0].StartsWith("# MovieShelf install manifest", StringComparison.Ordinal)) return null;
            var m = new Manifest();
            foreach (string line in lines)
            {
                if (line.Length < 3) continue;
                string v = line.Substring(2);
                // A manifest is only ever read back from a folder setup owns, but it decides what gets deleted: stay inside that folder.
                switch (line[0])
                {
                    case 'M': int i = v.IndexOf('='); if (i > 0) m.Meta[v.Substring(0, i)] = v.Substring(i + 1); break;
                    case 'F': if (SafeRelative(v)) m.Files_.Add(v); break;
                    case 'D': if (SafeRelative(v)) m.Dirs.Add(v); break;
                    case 'S': m.Shortcuts.Add(v); break;
                    case 'P': m.Parents.Add(v); break;
                }
            }
            return m;
        }
        catch (Exception) { return null; }
    }

    static bool SafeRelative(string p)
    {
        if (p.Length == 0 || Path.IsPathRooted(p) || p.IndexOf(':') >= 0) return false;
        foreach (string part in p.Split('\\', '/')) if (part == ".." || part == ".") return false;
        return true;
    }

    public bool HasFile(string rel)
    {
        if (index == null)
        {
            index = new HashSet<string>(Files_, StringComparer.OrdinalIgnoreCase);
            dirIndex = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string f in Files_)
                for (int i = f.LastIndexOf('\\'); i > 0; i = f.LastIndexOf('\\', i - 1)) dirIndex.Add(f.Substring(0, i));
        }
        return index.Contains(rel);
    }

    /// <summary>True when a listed file sits somewhere under this relative folder.</summary>
    public bool HasFilesUnder(string relDir) { HasFile(""); return dirIndex.Contains(relDir); }
}

/// <summary>What is on this PC already, from the uninstall entry.</summary>
class Installed
{
    public string Dir, Version, UninstallString;
    public bool AllUsers;
    public Manifest M;

    public bool Legacy { get { return M == null; } }
    public bool Portable { get { return File.Exists(Path.Combine(Dir, "portable.txt")); } }

    public static Installed Find()
    {
        foreach (bool all in new[] { false, true })
        {
            try
            {
                using (RegistryKey k = UninstallEntry.Open(all, false))
                {
                    if (k == null) continue;
                    string dir = k.GetValue("InstallLocation") as string;
                    if (string.IsNullOrEmpty(dir) || !File.Exists(Path.Combine(dir, "MovieShelf.exe"))) continue;
                    return new Installed
                    {
                        Dir = dir.TrimEnd('\\'), AllUsers = all, Version = (k.GetValue("DisplayVersion") as string) ?? "",
                        UninstallString = k.GetValue("UninstallString") as string, M = Manifest.Load(Path.Combine(dir, Manifest.FileName))
                    };
                }
            }
            catch (Exception) { }
        }
        return null;
    }
}

/// <summary>The "Installed apps" entry.</summary>
static class UninstallEntry
{
    const string Key = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\MovieShelf";

    // Tests redirect the entry below HKCU so an all-users run needs no administrator rights.
    static RegistryKey Hive(bool all) { return all && Env.Test("REGKEY") == null ? Registry.LocalMachine : Registry.CurrentUser; }

    static string SubKey(bool all)
    {
        string test = Env.Test("REGKEY");
        return test == null ? Key : test + (all ? @"\All\MovieShelf" : @"\User\MovieShelf");
    }

    public static RegistryKey Open(bool all, bool write) { return Hive(all).OpenSubKey(SubKey(all), write); }

    /// <summary>The current values, to put back if the install is rolled back. Null when there is no entry.</summary>
    public static Dictionary<string, object> Snapshot(bool all)
    {
        using (RegistryKey k = Open(all, false))
        {
            if (k == null) return null;
            var d = new Dictionary<string, object>();
            foreach (string n in k.GetValueNames()) d[n] = k.GetValue(n);
            return d;
        }
    }

    public static void Restore(bool all, Dictionary<string, object> values)
    {
        Delete(all);
        if (values == null) return;
        using (RegistryKey k = Hive(all).CreateSubKey(SubKey(all)))
            foreach (var kv in values) k.SetValue(kv.Key, kv.Value);
    }

    public static void Write(bool all, string dir, string version, long bytes)
    {
        using (RegistryKey k = Hive(all).CreateSubKey(SubKey(all)))
        {
            string un = Path.Combine(dir, "uninstall.exe");
            k.SetValue("DisplayName", "MovieShelf");
            k.SetValue("DisplayVersion", version);
            k.SetValue("Publisher", "MovieShelf");
            k.SetValue("DisplayIcon", Path.Combine(dir, "MovieShelf.exe") + ",0");
            k.SetValue("InstallLocation", dir);
            k.SetValue("InstallDate", DateTime.Now.ToString("yyyyMMdd", CultureInfo.InvariantCulture));
            k.SetValue("EstimatedSize", (int)(bytes / 1024), RegistryValueKind.DWord);
            k.SetValue("UninstallString", "\"" + un + "\"");
            k.SetValue("QuietUninstallString", "\"" + un + "\" /S");
            k.SetValue("NoModify", 1, RegistryValueKind.DWord);
            k.SetValue("NoRepair", 1, RegistryValueKind.DWord);
        }
    }

    public static void Delete(bool all)
    {
        try { Hive(all).DeleteSubKeyTree(SubKey(all), false); } catch (Exception) { }
    }
}

[ComImport, Guid("00021401-0000-0000-C000-000000000046")]
class ShellLinkCoClass { }

[ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown), Guid("000214F9-0000-0000-C000-000000000046")]
interface IShellLinkW
{
    void GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder file, int size, IntPtr data, uint flags);
    void GetIDList(out IntPtr list);
    void SetIDList(IntPtr list);
    void GetDescription([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder name, int size);
    void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string name);
    void GetWorkingDirectory([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder dir, int size);
    void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string dir);
    void GetArguments([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder args, int size);
    void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string args);
    void GetHotkey(out short key);
    void SetHotkey(short key);
    void GetShowCmd(out int cmd);
    void SetShowCmd(int cmd);
    void GetIconLocation([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder path, int size, out int index);
    void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string path, int index);
    void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string path, uint reserved);
    void Resolve(IntPtr hwnd, uint flags);
    void SetPath([MarshalAs(UnmanagedType.LPWStr)] string path);
}

static class Shell
{
    public const string LinkName = "MovieShelf.lnk", OtherLinkName = "MovieShelf (native).lnk";

    static string TestRoot(bool all) { string r = Env.Test("SHELLROOT"); return r == null ? null : Path.Combine(r, all ? "all" : "user"); }

    public static string ProgramsDir(bool all)
    {
        string r = TestRoot(all);
        if (r != null) return Path.Combine(r, "Programs");
        return Environment.GetFolderPath(all ? Environment.SpecialFolder.CommonPrograms : Environment.SpecialFolder.Programs);
    }

    public static string DesktopDir(bool all)
    {
        string r = TestRoot(all);
        if (r != null) return Path.Combine(r, "Desktop");
        return Environment.GetFolderPath(all ? Environment.SpecialFolder.CommonDesktopDirectory : Environment.SpecialFolder.DesktopDirectory);
    }

    public static void Create(string link, string exe, string workDir)
    {
        var o = new ShellLinkCoClass();
        try
        {
            var l = (IShellLinkW)o;
            l.SetPath(exe);
            l.SetWorkingDirectory(workDir);
            l.SetDescription("MovieShelf");
            l.SetIconLocation(exe, 0);
            ((IPersistFile)o).Save(link, true);
        }
        finally { Marshal.ReleaseComObject(o); }
    }

    /// <summary>The program a shortcut opens, or null when it cannot be read.</summary>
    public static string Target(string link)
    {
        object o = null;
        try
        {
            o = new ShellLinkCoClass();
            ((IPersistFile)o).Load(link, 0);
            var s = new StringBuilder(1024);
            ((IShellLinkW)o).GetPath(s, s.Capacity, IntPtr.Zero, 0);
            return s.ToString();
        }
        catch (Exception) { return null; }
        finally { if (o != null) Marshal.ReleaseComObject(o); }
    }

    /// <summary>
    /// Where this install's shortcut goes. A MovieShelf.lnk that opens something else (the Python MovieShelf's, say) is the user's
    /// and stays as it is; ours takes the other name. Windows shows the user's and the all-users folder as one,
    /// so a shortcut in the twin folder takes the name just the same.
    /// </summary>
    public static string PathFor(string dir, string exe, Manifest previous)
    {
        string link = Path.Combine(dir, LinkName);
        if (previous != null && previous.Shortcuts.Exists(s => Files.Same(s, link))) return link;
        string twin = TwinOf(dir);
        bool taken = OpensSomethingElse(link, exe) || (twin != null && OpensSomethingElse(Path.Combine(twin, LinkName), exe));
        return taken ? Path.Combine(dir, OtherLinkName) : link;
    }

    static string TwinOf(string dir)
    {
        foreach (bool all in new[] { true, false })
        {
            if (Files.Same(dir, ProgramsDir(all))) return ProgramsDir(!all);
            if (Files.Same(dir, DesktopDir(all))) return DesktopDir(!all);
        }
        return null;
    }

    static bool OpensSomethingElse(string link, string exe)
    {
        if (!File.Exists(link)) return false;
        string target = Target(link);
        return target == null || !Files.Same(target, exe);
    }
}

/// <summary>Finds and closes a running MovieShelf. Only ever touches copies from the given folder.</summary>
static class Apps
{
    public static List<Process> Running(string folder)
    {
        var list = new List<Process>();
        foreach (Process p in Process.GetProcessesByName("MovieShelf"))
        {
            string path = Native.ProcessPath(p.Id);
            if (folder == null || (path != null && Files.Inside(path, folder))) list.Add(p);
            else p.Dispose();
        }
        return list;
    }

    /// <summary>Asks each window to close, waits, and only then ends the process. Returns false when one is still running.</summary>
    public static bool Close(string folder, int waitMs)
    {
        bool all = true;
        foreach (Process p in Running(folder))
        {
            using (p)
            {
                try
                {
                    p.CloseMainWindow();
                    if (p.WaitForExit(waitMs)) continue;
                    p.Kill();
                    if (!p.WaitForExit(4000)) all = false;
                }
                catch (InvalidOperationException) { }
                catch (Win32Exception) { all = false; }
            }
        }
        return all;
    }
}
