using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Data.Sqlite;

namespace MovieShelf.Core;

/// <summary>What a full backup (or restore) includes. Save uses Keys, Lists, Posters, Imdb; restore uses Settings, Keys, Lists, Posters, Imdb.
/// The library and settings are always saved; on restore the library is always replaced.</summary>
public sealed class FullBackupOptions
{
    public bool Settings, Keys, Lists, Posters, Imdb;
}

public sealed record FileGroup(int N, long Bytes);

/// <summary>What a full backup would contain, with sizes, for the options window.</summary>
public sealed record FullBackupInfo(long Db, FileGroup Posters, FileGroup Lists, FileGroup Imdb, bool Keys, string ListDir);

public sealed record FullBackupHas(int Lists, int Posters, int Imdb);

/// <summary>manifest.json of a full backup, plus HasSettings and Size (of the .zip) added by <see cref="Library.InspectFullBackup"/>.</summary>
public sealed class FullBackupManifest
{
    public string App = "MovieShelf", Created = "", ListDir = "";
    public int Format;
    public bool HasKeys, HasSettings;
    public FullBackupHas Has = new(0, 0, 0);
    public List<string> MediaDirs = new();
    public long Size;
}

/// <summary>What a full restore did. Lists/Posters/Imdb are null when that part was not restored.</summary>
public sealed class FullRestoreResult
{
    public bool Library, Settings;
    public int? Lists, Posters, Imdb;
    public List<string> Warnings = new();
    public string Safety = "";   // folder holding a copy of what existed before
}

/// <summary>Full backup: one .zip with the database (every table), settings.json, lists/, posters/, people/ and, only if asked, imdb/ and imdb_index.db.
/// Same layout and manifest as the Python fullbackup.py, so either app restores the other's zip. The zip can hold API keys in plain text.</summary>
public sealed partial class Library
{
    static readonly string[] KeyFields = { "tmdb_key", "omdb_key" };
    const int FullBackupFormat = 1;

    string PeopleDirForBackup => Path.Combine(DataDir, "people");   // person photos (the Python people_dir)

    // ---------- helpers ----------
    static List<string> FilesIn(string folder, string ext = "")
    {
        if (!Directory.Exists(folder)) return new();
        return Directory.EnumerateFiles(folder)
            .Where(p => { string n = Path.GetFileName(p); return !n.StartsWith('.') && (ext.Length == 0 || n.EndsWith(ext, StringComparison.OrdinalIgnoreCase)); })
            .OrderBy(p => p, StringComparer.Ordinal).ToList();
    }

    static long SizeOf(IEnumerable<string> files) => files.Sum(p => new FileInfo(p).Length);

    static string StrOf(JsonNode? n) => n is JsonValue v && v.TryGetValue<string>(out var s) ? s : "";

    /// <summary>settings.json as the Python app sees it: the defaults, overlaid with what the file holds (keys this app does not know are kept).</summary>
    JsonObject SettingsJson()
    {
        var o = new JsonObject
        {
            ["list_dir"] = Settings.DefaultListDir(), ["tmdb_key"] = "", ["omdb_key"] = "", ["online_posters"] = true, ["online_fallback"] = true,
            ["auto_refresh"] = true, ["media_dirs"] = new JsonArray(), ["probe_media"] = false, ["ffprobe_path"] = "", ["people_auto"] = false,
            ["people_scope"] = "main",
        };
        if (File.Exists(SettingsPath) &&
            JsonNode.Parse(File.ReadAllText(SettingsPath), documentOptions: new JsonDocumentOptions { AllowTrailingCommas = true }) is JsonObject f)
            foreach (var kv in f) o[kv.Key] = kv.Value?.DeepClone();
        return o;
    }

    static bool HasKeys(JsonObject s) => KeyFields.Any(k => StrOf(s[k]).Length > 0);

    static List<string> MediaDirsOf(JsonObject s) => (s["media_dirs"] as JsonArray)?.Select(StrOf).Where(x => x.Length > 0).ToList() ?? new();

    /// <summary>Copies the open database to a new file (a consistent snapshot, also while a job writes).</summary>
    void CopyDatabase(string destPath)
    {
        using var dest = new SqliteConnection($"Data Source={destPath};Pooling=False");
        dest.Open();
        lock (Lock) db.BackupDatabase(dest);
    }

    /// <summary>Progress that cannot be cancelled (a restore replaces the library and must not stop half way).</summary>
    void StageNoStop(string stage, long done = 0, long total = 0)
    {
        Job.Stage = stage;
        Job.Done = done;
        Job.Total = total;
        Changed();
    }

    /// <summary>A zip member name that stays inside the folder it is extracted to.</summary>
    public static bool IsSafeMember(string name) =>
        name.Length > 0 && !Path.IsPathRooted(name) && !name.Replace('\\', '/').Split('/').Contains("..") && !name.Contains(':');

    static void Extract(ZipArchiveEntry e, string target)
    {
        if (!IsSafeMember(e.FullName)) throw new InvalidDataException($"Unsafe path in the backup: {e.FullName}");
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(target))!);
        using var src = e.Open();
        using var dst = new FileStream(target, FileMode.Create, FileAccess.Write);
        src.CopyTo(dst, 1 << 20);
    }

    // ---------- info, save ----------
    /// <summary>What a full backup would contain, with sizes, for the options window. Quick (lists the folders).</summary>
    public FullBackupInfo GetFullBackupInfo()
    {
        var s = SettingsJson();
        string listDir = StrOf(s["list_dir"]);
        var posters = FilesIn(PosterDir);
        var lists = FilesIn(listDir, ".txt");
        var imdb = FilesIn(ImdbDir);
        if (File.Exists(IndexPath)) imdb.Add(IndexPath);
        return new FullBackupInfo(File.Exists(DbPath) ? new FileInfo(DbPath).Length : 0, new(posters.Count, SizeOf(posters)),
            new(lists.Count, SizeOf(lists)), new(imdb.Count, SizeOf(imdb)), HasKeys(s), listDir);
    }

    /// <summary>data/backups/MovieShelf full backup yyyy-MM-dd HHmm.zip</summary>
    public string DefaultFullBackupPath() => Path.Combine(BackupsDir, $"MovieShelf full backup {Stamp("yyyy-MM-dd HHmm")}.zip");

    /// <summary>Starts <see cref="SaveFullBackup"/> as a background job (stoppable). <paramref name="path"/> null/empty = the default path.
    /// Throws ArgumentException for a path that is not a .zip in an existing folder. Returns false when another job is running.</summary>
    public (bool Started, string Path) StartSaveFullBackup(string? path, FullBackupOptions opts)
    {
        string dest = string.IsNullOrEmpty(path) ? DefaultFullBackupPath() : path;
        if (!dest.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) || !Directory.Exists(Path.GetDirectoryName(Path.GetFullPath(dest))))
            throw new ArgumentException("Choose a file name ending in .zip in a folder that exists.");
        return (Start(() => SaveFullBackup(dest, opts)), dest);
    }

    /// <summary>Writes the zip. Runs as a job (progress shows in the top bar; Stop removes the partial file). Returns the final path.</summary>
    public string SaveFullBackup(string path, FullBackupOptions opts)
    {
        path = Path.GetFullPath(path);
        if (!path.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)) path += ".zip";
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var s = SettingsJson();
        string listDir = StrOf(s["list_dir"]);
        string tmp = Path.Combine(Path.GetTempPath(), "movieshelf_backup_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tmp);
        string part = path + ".part";
        try
        {
            Progress("Backup: copying the database");
            string dbcopy = Path.Combine(tmp, "library.db");
            CopyDatabase(dbcopy);
            var outS = (JsonObject)s.DeepClone();
            if (!opts.Keys) foreach (var k in KeyFields) outS[k] = "";
            string settingsFile = Path.Combine(tmp, "settings.json");
            File.WriteAllText(settingsFile, outS.ToJsonString(JsonIndent2), Utf8);
            var entries = new List<(string Arc, string File, bool Compress)> { ("library.db", dbcopy, true), ("settings.json", settingsFile, true) };
            var lists = opts.Lists ? FilesIn(listDir, ".txt") : new();
            entries.AddRange(lists.Select(p => ($"lists/{Path.GetFileName(p)}", p, true)));
            var posters = opts.Posters ? FilesIn(PosterDir) : new();
            entries.AddRange(posters.Select(p => ($"posters/{Path.GetFileName(p)}", p, false)));   // jpegs do not compress
            if (opts.Posters) entries.AddRange(FilesIn(PeopleDirForBackup).Select(p => ($"people/{Path.GetFileName(p)}", p, false)));
            var imdb = new List<(string Arc, string File, bool Compress)>();
            if (opts.Imdb)
            {
                imdb.AddRange(FilesIn(ImdbDir).Select(p => ($"imdb/{Path.GetFileName(p)}", p, false)));
                if (File.Exists(IndexPath)) imdb.Add(("imdb_index.db", IndexPath, true));
            }
            entries.AddRange(imdb);
            var manifest = new JsonObject
            {
                ["app"] = "MovieShelf", ["format"] = FullBackupFormat, ["created"] = Stamp(),
                ["has_keys"] = opts.Keys && HasKeys(s),
                ["has"] = new JsonObject { ["lists"] = lists.Count, ["posters"] = posters.Count, ["imdb"] = imdb.Count },
                ["list_dir"] = listDir, ["media_dirs"] = new JsonArray(MediaDirsOf(s).Select(x => (JsonNode?)JsonValue.Create(x)).ToArray()),
            };
            using (var fs = new FileStream(part, FileMode.Create, FileAccess.Write))
            using (var z = new ZipArchive(fs, ZipArchiveMode.Create))
            {
                var me = z.CreateEntry("manifest.json", CompressionLevel.Optimal);
                using (var w = new StreamWriter(me.Open(), Utf8)) w.Write(manifest.ToJsonString(JsonIndent1));
                for (int i = 1; i <= entries.Count; i++)
                {
                    var (arc, fp, compress) = entries[i - 1];
                    if (i % 50 == 0 || arc.StartsWith("imdb")) Progress("Backup: writing files", i, entries.Count);
                    var e = z.CreateEntry(arc, compress ? CompressionLevel.Optimal : CompressionLevel.NoCompression);
                    try { e.LastWriteTime = File.GetLastWriteTime(fp); } catch (ArgumentOutOfRangeException) { }
                    using var dst = e.Open();
                    using var src = new FileStream(fp, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                    src.CopyTo(dst, 1 << 20);
                }
            }
            File.Move(part, path, true);
            Log($"Full backup saved: {path} ({new FileInfo(path).Length / 1e6:0} MB, {entries.Count} files)");
            return path;
        }
        finally
        {
            try { Directory.Delete(tmp, true); } catch (IOException) { }
            try { if (File.Exists(part)) File.Delete(part); } catch (IOException) { }
        }
    }

    // ---------- inspect, restore ----------
    /// <summary>The manifest of a backup file; throws InvalidDataException ("This is not a MovieShelf full backup (.zip).") when it is not one. Quick.</summary>
    public static FullBackupManifest InspectFullBackup(string path)
    {
        const string bad = "This is not a MovieShelf full backup (.zip).";
        try
        {
            using var z = ZipFile.OpenRead(path);
            var me = z.GetEntry("manifest.json") ?? throw new InvalidDataException(bad);
            JsonObject m;
            using (var rs = me.Open()) m = JsonNode.Parse(rs) as JsonObject ?? throw new InvalidDataException(bad);
            if (StrOf(m["app"]) is not ("MovieShelf" or "MovieDB") || z.GetEntry("library.db") == null) throw new InvalidDataException(bad);
            int N(JsonNode? n) => n is JsonValue v && v.TryGetValue<int>(out var i) ? i : 0;
            var has = m["has"] as JsonObject;
            return new FullBackupManifest
            {
                Format = N(m["format"]), Created = StrOf(m["created"]), HasKeys = m["has_keys"] is JsonValue hk && hk.TryGetValue<bool>(out var b) && b,
                Has = new(N(has?["lists"]), N(has?["posters"]), N(has?["imdb"])), ListDir = StrOf(m["list_dir"]),
                MediaDirs = (m["media_dirs"] as JsonArray)?.Select(StrOf).ToList() ?? new(),
                HasSettings = z.GetEntry("settings.json") != null, Size = new FileInfo(path).Length,
            };
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidDataException or JsonException or InvalidOperationException)
        {
            throw new InvalidDataException(bad);
        }
    }

    /// <summary>What the last full restore did (the Python restore_result); null before one ran or while one runs.</summary>
    public FullRestoreResult? RestoreResult { get; private set; }

    /// <summary>Starts <see cref="RestoreFullBackup"/> as a background job that cannot be stopped. Throws InvalidDataException when the file is not a backup.
    /// Returns false when another job is running. On first run the caller chains an update afterwards to fetch what the backup did not hold.</summary>
    public bool StartRestoreFullBackup(string path, FullBackupOptions opts)
    {
        InspectFullBackup(path);
        if (Job.Running) return false;
        RestoreResult = null;
        return Start(() => RestoreFullBackup(path, opts), stoppable: false);
    }

    /// <summary>Replaces the library (and, as chosen, settings, API keys, list files, posters, IMDb data) with a backup.
    /// What exists now is copied to data/backups/before-restore-&lt;time&gt; first. Runs as a job; the open database takes over the backup's pages.</summary>
    public FullRestoreResult RestoreFullBackup(string path, FullBackupOptions opts)
    {
        var m = InspectFullBackup(path);
        string safety = Path.Combine(BackupsDir, "before-restore-" + Stamp("yyyy-MM-dd_HHmmss"));
        Directory.CreateDirectory(safety);
        StageNoStop("Restore: saving what you have now");
        CopyDatabase(Path.Combine(safety, "library.db"));
        if (File.Exists(SettingsPath)) File.Copy(SettingsPath, Path.Combine(safety, "settings.json"), true);
        var cur = SettingsJson();
        foreach (var p in FilesIn(StrOf(cur["list_dir"]), ".txt"))
        {
            Directory.CreateDirectory(Path.Combine(safety, "lists"));
            File.Copy(p, Path.Combine(safety, "lists", Path.GetFileName(p)), true);
        }
        var res = new FullRestoreResult { Safety = safety };
        string tmp = Path.Combine(Path.GetTempPath(), "movieshelf_restore_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tmp);
        try
        {
            using (var z = ZipFile.OpenRead(path))
            {
                var names = z.Entries.Select(e => e.FullName).ToList();
                ZipArchiveEntry Entry(string n) => z.Entries.First(e => e.FullName == n);
                var files = (string prefix) => z.Entries.Where(e => e.FullName.StartsWith(prefix, StringComparison.Ordinal) && !e.FullName.EndsWith('/')).ToList();
                StageNoStop("Restore: reading the database");
                string dbfile = Path.Combine(tmp, "library.db");
                Extract(Entry("library.db"), dbfile);
                ReplaceDatabase(dbfile);
                res.Library = true;
                var settings = (JsonObject)cur.DeepClone();
                if (opts.Settings && names.Contains("settings.json"))
                {
                    JsonObject nw;
                    using (var rs = Entry("settings.json").Open()) nw = JsonNode.Parse(rs) as JsonObject ?? new JsonObject();
                    // an empty key in the backup never erases yours
                    if (!opts.Keys || !HasKeys(nw)) foreach (var k in KeyFields) nw[k] = cur[k]?.DeepClone() ?? "";
                    foreach (var kv in nw) settings[kv.Key] = kv.Value?.DeepClone();
                    res.Settings = true;
                }
                if (opts.Lists)
                {
                    var lists = names.Where(n => n.StartsWith("lists/", StringComparison.Ordinal) && n.EndsWith(".txt", StringComparison.Ordinal)).ToList();
                    if (lists.Count > 0)
                    {
                        string target = StrOf(settings["list_dir"]);
                        if (!Directory.Exists(target))
                        {
                            target = Settings.DefaultListDir();
                            settings["list_dir"] = target;
                            res.Warnings.Add($"The old list folder does not exist here, so the list files went to {target} and the list folder setting now points to it.");
                        }
                        foreach (var n in lists) Extract(Entry(n), Path.Combine(target, Path.GetFileName(n)));
                        res.Lists = lists.Count;
                    }
                }
                var missing = MediaDirsOf(settings).Where(d => !Directory.Exists(d)).ToList();
                if (missing.Count > 0)
                    res.Warnings.Add("Movie folders not found on this PC (change them in Settings → General): " + string.Join(", ", missing));
                if (opts.Posters)
                {
                    var posters = files("posters/");
                    for (int i = 1; i <= posters.Count; i++)
                    {
                        if (i % 100 == 0) StageNoStop("Restore: posters", i, posters.Count);
                        Extract(posters[i - 1], Path.Combine(PosterDir, Path.GetFileName(posters[i - 1].FullName)));
                    }
                    foreach (var e in files("people/")) Extract(e, Path.Combine(PeopleDirForBackup, Path.GetFileName(e.FullName)));
                    res.Posters = posters.Count;
                }
                if (opts.Imdb)
                {
                    var imdb = files("imdb/");
                    for (int i = 1; i <= imdb.Count; i++)
                    {
                        StageNoStop("Restore: IMDb data", i, imdb.Count);
                        Extract(imdb[i - 1], Path.Combine(ImdbDir, Path.GetFileName(imdb[i - 1].FullName)));
                    }
                    if (names.Contains("imdb_index.db"))
                    {
                        Extract(Entry("imdb_index.db"), IndexPath);
                        Matcher.Reset();
                    }
                    res.Imdb = imdb.Count;
                }
                File.WriteAllText(SettingsPath, settings.ToJsonString(JsonIndent2), Utf8);
            }
        }
        finally
        {
            try { Directory.Delete(tmp, true); } catch (IOException) { }
        }
        Version++;
        IssueRev++;
        RestoreResult = res;
        Log($"Full backup restored from {Path.GetFileName(path)} (made {m.Created}). A copy of the previous state is in {safety}");
        foreach (var w in res.Warnings) Log(w);
        return res;
    }

    /// <summary>Makes the open database take over the pages of <paramref name="file"/>, then re-runs <see cref="EnsureSchema"/>
    /// (a backup from an older version may lack newer tables and columns).</summary>
    void ReplaceDatabase(string file)
    {
        using var src = new SqliteConnection($"Data Source={file};Pooling=False");
        src.Open();
        lock (Lock)
        {
            try { src.BackupDatabase(db); }
            catch (SqliteException)
            {
                // e.g. a different page size cannot be copied into a WAL database: swap the file instead
                src.Close();
                SqliteConnection.ClearAllPools();
                db.Dispose();
                foreach (var suffix in new[] { "", "-wal", "-shm" })
                    if (File.Exists(DbPath + suffix)) File.Delete(DbPath + suffix);
                File.Copy(file, DbPath);
                db = new SqliteConnection($"Data Source={DbPath}");
                db.Open();
            }
            Exec("PRAGMA journal_mode=WAL");
            EnsureSchema();
        }
    }
}
