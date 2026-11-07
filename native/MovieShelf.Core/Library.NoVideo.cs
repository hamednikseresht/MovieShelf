using System.Diagnostics;
using System.Text.Json;

namespace MovieShelf.Core;

/// <summary>A folder named like a list entry with no real video in it, as stored in table novideo.</summary>
public sealed record NoVideoFolder(string Path, string Raw, int NFiles, long Size, List<string> Names);

/// <summary>What the movie folder scan notes down to find list entries whose folder holds no real video: only junk (.nfo, .jpg, .url, a sample)
/// or nothing at all. Python's real_raws, real_dirs and named_dirs.</summary>
public sealed class NoVideoScan
{
    readonly Dictionary<string, string> wanted;
    readonly HashSet<string> realRaws = new();
    readonly HashSet<string> realDirs = new();                  // folders that hold a real video, or have one somewhere below
    readonly Dictionary<string, string> namedDirs = new();      // folders named like a list entry -> that entry

    public NoVideoScan(Dictionary<string, string> wanted) => this.wanted = wanted;

    static string Norm(string path) => Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));

    /// <summary>A video that can be the movie itself: not a tiny clip, sample, trailer or extra.</summary>
    public static bool IsRealVideo(string stem, long size) => !(size < 30_000_000 || (Checker.ExtraRe.IsMatch(stem) && size < 1_500_000_000));

    /// <summary>Call for every folder the scan enters.</summary>
    public void Folder(string root, string dirpath)
    {
        string dn = Norm(dirpath), parent = FolderUtil.BaseName(dirpath).ToLowerInvariant();
        if (dn != Norm(root) && wanted.TryGetValue(parent, out var raw)) namedDirs[dn] = raw;
    }

    /// <summary>Call for every video file the scan finds.</summary>
    public void Video(string root, string dirpath, string stem, long size)
    {
        if (!IsRealVideo(stem, size)) return;
        string rootn = Norm(root), d = Norm(dirpath);
        while (realDirs.Add(d))
        {
            var up = Path.GetDirectoryName(d);
            if (d == rootn || up == null) break;
            d = up;
        }
        if (wanted.TryGetValue(stem.ToLowerInvariant(), out var raw) || wanted.TryGetValue(FolderUtil.BaseName(dirpath).ToLowerInvariant(), out raw)) realRaws.Add(raw);
    }

    /// <summary>The list folders without a real video. Reads those folders, so call it outside the database lock.</summary>
    public List<NoVideoFolder> Result()
    {
        var junk = new List<NoVideoFolder>();
        foreach (var (d, raw) in namedDirs.OrderBy(x => x.Key, StringComparer.Ordinal))
        {
            if (realDirs.Contains(d) || realRaws.Contains(raw)) continue;
            var (n, size, names) = Contents(d);
            junk.Add(new NoVideoFolder(d, raw, n, size, names));
        }
        return junk;
    }

    /// <summary>(number of files, total bytes, first few file names) of everything inside a folder.</summary>
    static (int N, long Size, List<string> Names) Contents(string dir, int limit = 6)
    {
        int n = 0;
        long total = 0;
        var names = new List<string>();
        foreach (var (dp, _, files) in FolderUtil.Walk(dir))
        {
            files.Sort(StringComparer.Ordinal);
            foreach (var f in files)
            {
                n++;
                string full = Path.Combine(dp, f);
                try { total += new FileInfo(full).Length; }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
                if (names.Count < limit) names.Add(Path.GetRelativePath(dir, full));
            }
        }
        return (n, total, names);
    }
}

public sealed partial class Library
{
    const string NoVideoSchema = "CREATE TABLE IF NOT EXISTS novideo(path TEXT PRIMARY KEY, raw TEXT, nfiles INT, size INT, names TEXT)";

    /// <summary>Replaces table novideo; call inside the transaction of the folder scan.</summary>
    void StoreNoVideo(List<NoVideoFolder> junk)
    {
        Exec("DELETE FROM novideo");
        foreach (var j in junk)
            Exec("INSERT OR REPLACE INTO novideo(path, raw, nfiles, size, names) VALUES (?,?,?,?,?)", j.Path, j.Raw, j.NFiles, j.Size, JsonSerializer.Serialize(j.Names));
    }

    /// <summary>Opens a folder with no video in it in Explorer. False when the path is not such a folder (Reveal then treats it as a file);
    /// only folders the scan found can be opened this way.</summary>
    bool RevealNoVideoFolder(string path)
    {
        using var rd = Reader();
        using (var c = Cmd(rd, "SELECT 1 FROM fileinfo WHERE path=?", new object?[] { path }))
            if (c.ExecuteScalar() != null) return false;
        using (var c = Cmd(rd, "SELECT 1 FROM novideo WHERE path=?", new object?[] { path }))
            if (c.ExecuteScalar() == null) return false;
        if (!Directory.Exists(path)) throw new FileNotFoundException("That file is not where it was. Scan the movie folders again.");
        if (OperatingSystem.IsWindows())
            Process.Start(new ProcessStartInfo("explorer.exe") { Arguments = RevealFolderArgs(path), UseShellExecute = false });
        return true;
    }

    /// <summary>The explorer.exe arguments that open a folder.</summary>
    public static string RevealFolderArgs(string path) => $"\"{Path.TrimEndingDirectorySeparator(Path.GetFullPath(path))}\"";
}
