using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Data.Sqlite;

namespace MovieShelf.Core;

public sealed class Settings
{
    public string ListDir = DefaultListDir();
    public string TmdbKey = "", OmdbKey = "", Language = "en";
    public bool OnlinePosters = true, OnlineFallback = true, AutoRefresh = true, ProbeMedia, PeopleAuto;
    public List<string> MediaDirs = new();
    public string FfprobePath = "", PeopleScope = "main";
    public string RenamePattern = RenamePlanner.DefaultPattern;

    public static string DefaultListDir() =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Documents", "MovieShelf", "lists");
}

public sealed class JobState
{
    public bool Running, Stopping, Stopped;
    public bool Stoppable = true;   // false for a full-backup restore: it replaces the library and cannot be cut short
    public string Stage = "";
    public long Done, Total;
    public string? Error;
    public List<string> Log = new();
}

/// <summary>The collection database. Opens the same files as the Python MovieShelf (library.db, imdb_index.db, settings.json).</summary>
public sealed partial class Library : IDisposable
{
    const int ParserVersion = 2;   // bump when Parse.cs changes so stored names are re-read; the same number and settings key as the Python app
    const string ParserVersionKey = "parser_version";

    const string Schema = @"
CREATE TABLE IF NOT EXISTS files(
    id INTEGER PRIMARY KEY, raw TEXT UNIQUE, list_file TEXT, present INT DEFAULT 1, added TEXT,
    title TEXT, alt_title TEXT, year INT, resolution TEXT, source TEXT, codec TEXT, bit10 INT, hdr INT,
    audio TEXT, farsi_dubbed INT, dual_audio INT, softsub INT, edition TEXT, grp TEXT,
    tconst INT, conf TEXT, score REAL, manual INT DEFAULT 0, matched INT DEFAULT 0);
CREATE INDEX IF NOT EXISTS files_tconst ON files(tconst);
CREATE TABLE IF NOT EXISTS titles(
    tconst INTEGER PRIMARY KEY, type TEXT, title TEXT, orig TEXT, year INT, endyear INT, runtime INT,
    genres TEXT, rating REAL, votes INT, en_title TEXT, fa_title TEXT, akas TEXT,
    directors TEXT, writers TEXT, cast_ TEXT, crew TEXT, enriched INT DEFAULT 0,
    poster TEXT, poster_url TEXT, poster_tried INT DEFAULT 0,
    plot TEXT, tagline TEXT, countries TEXT, languages TEXT, orig_lang TEXT, tmdb_id INT,
    tmdb_done INT DEFAULT 0, backdrop_url TEXT, tmdb_cast TEXT, collection TEXT, updated TEXT);
CREATE TABLE IF NOT EXISTS user(mkey TEXT PRIMARY KEY, fav INT DEFAULT 0, watched INT DEFAULT 0,
    myrating INT, notes TEXT, updated TEXT);
CREATE TABLE IF NOT EXISTS people(nconst INTEGER PRIMARY KEY, name TEXT);
CREATE TABLE IF NOT EXISTS forced(raw TEXT PRIMARY KEY, tconst INT);
CREATE TABLE IF NOT EXISTS dismissed(ikey TEXT PRIMARY KEY);
CREATE TABLE IF NOT EXISTS views(id INTEGER PRIMARY KEY AUTOINCREMENT, name TEXT UNIQUE, state TEXT);
CREATE TABLE IF NOT EXISTS wishlist(id INTEGER PRIMARY KEY AUTOINCREMENT, title TEXT, year INT, tconst INT, note TEXT,
    source TEXT, status TEXT DEFAULT 'wanted', added TEXT, UNIQUE(title, year));
CREATE TABLE IF NOT EXISTS fileinfo(path TEXT PRIMARY KEY, raw TEXT, size INT, mtime REAL);
CREATE INDEX IF NOT EXISTS fileinfo_raw ON fileinfo(raw);
CREATE TABLE IF NOT EXISTS collections(name TEXT PRIMARY KEY, tmdb_id INT, parts TEXT);
CREATE TABLE IF NOT EXISTS fa_titles(tconst INTEGER PRIMARY KEY, title TEXT, source TEXT);
CREATE TABLE IF NOT EXISTS persons(nconst INTEGER PRIMARY KEY, tmdb_id INT, data TEXT, photo INT DEFAULT 0, fetched TEXT);
CREATE TABLE IF NOT EXISTS unlisted(path TEXT PRIMARY KEY, raw TEXT, size INT, mtime REAL);
CREATE TABLE IF NOT EXISTS removed(raw TEXT PRIMARY KEY, tconst INT, title TEXT, removed TEXT);
CREATE TABLE IF NOT EXISTS mediainfo(path TEXT PRIMARY KEY, size INT, mtime REAL, ok INT, duration INT, width INT, height INT,
    vcodec TEXT, bitrate INT, audio TEXT, subs TEXT, n_audio INT, n_subs INT);
";

    static readonly (string Table, string Col, string Type)[] NewColumns =
    {
        ("titles", "rt", "INT"), ("titles", "mc", "INT"), ("titles", "scores_at", "TEXT"), ("titles", "omdb_done", "INT DEFAULT 0"),
        ("user", "watched_at", "TEXT"), ("people", "birth", "INT"), ("people", "death", "INT"), ("people", "prof", "TEXT"),
    };

    /// <summary>The folder for the database, posters, IMDb data and settings (<paramref name="appDir"/> null: the folder of the running program).</summary>
    public static string DefaultRoot(string? appDir = null) =>
        ResolveRoot(Environment.GetEnvironmentVariable("MOVIESHELF_HOME"), appDir ?? AppContext.BaseDirectory,
            Environment.GetEnvironmentVariable("LOCALAPPDATA") ?? Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData));

    /// <summary>Port of paths.home: MOVIESHELF_HOME if set, the program's own folder if it has a portable.txt, else %LOCALAPPDATA%\MovieShelf.</summary>
    public static string ResolveRoot(string? home, string? appDir, string localAppData)
    {
        if (!string.IsNullOrEmpty(home)) return Path.GetFullPath(home);
        if (!string.IsNullOrEmpty(appDir) && File.Exists(Path.Combine(appDir, "portable.txt")))
            return Path.TrimEndingDirectorySeparator(Path.GetFullPath(appDir));
        return Path.Combine(localAppData, "MovieShelf");
    }

    public readonly string Root, DataDir, ImdbDir, IndexPath, PosterDir, DbPath, SettingsPath;
    public readonly object Lock = new();
    public readonly Matcher Matcher;
    public JobState Job { get; private set; } = new();
    public int Version { get; private set; }
    public int IssueRev { get; private set; }
    public event Action? JobChanged;
    public event Action? DataChanged;

    SqliteConnection db;
    CancellationTokenSource? cts;

    public Library(string root)
    {
        Root = Path.GetFullPath(root);
        DataDir = Path.Combine(Root, "data");
        ImdbDir = Path.Combine(DataDir, "imdb");
        IndexPath = Path.Combine(DataDir, "imdb_index.db");
        PosterDir = Path.Combine(DataDir, "posters");
        DbPath = Path.Combine(DataDir, "library.db");
        SettingsPath = Path.Combine(DataDir, "settings.json");
        Directory.CreateDirectory(PosterDir);
        db = new SqliteConnection($"Data Source={DbPath}");
        db.Open();
        EnsureSchema();
        Exec("PRAGMA journal_mode=WAL");
        Matcher = new Matcher(IndexPath);
    }

    public void Dispose() => db.Dispose();

    // ---------- small database helpers ----------
    SqliteCommand Cmd(string sql, params object?[] args) => Cmd(db, sql, args);

    static SqliteCommand Cmd(SqliteConnection c, string sql, object?[] args)
    {
        var cmd = c.CreateCommand();
        var sb = new System.Text.StringBuilder();
        int n = 0;
        foreach (char ch in sql) sb.Append(ch == '?' ? "$p" + n++ : ch.ToString());
        cmd.CommandText = sb.ToString();
        for (int i = 0; i < args.Length; i++) cmd.Parameters.AddWithValue("$p" + i, args[i] ?? DBNull.Value);
        return cmd;
    }

    int Exec(string sql, params object?[] args)
    {
        using var c = Cmd(sql, args);
        return c.ExecuteNonQuery();
    }

    T? Scalar<T>(string sql, params object?[] args)
    {
        using var c = Cmd(sql, args);
        var v = c.ExecuteScalar();
        return v is null or DBNull ? default : (T)Convert.ChangeType(v, Nullable.GetUnderlyingType(typeof(T)) ?? typeof(T));
    }

    void EnsureSchema()
    {
        Exec(Schema.Replace("\r", ""));
        Exec(NoVideoSchema);
        foreach (var (table, col, type) in NewColumns)
        {
            bool have = false;
            using (var c = Cmd($"PRAGMA table_info({table})"))
            using (var r = c.ExecuteReader())
                while (r.Read()) if (r.GetString(1) == col) have = true;
            if (!have) Exec($"ALTER TABLE {table} ADD COLUMN {col} {type}");
        }
    }

    /// <summary>A separate read-only connection: WAL lets it read while a job writes.</summary>
    public SqliteConnection Reader()
    {
        var c = new SqliteConnection($"Data Source={DbPath};Mode=ReadOnly");
        c.Open();
        return c;
    }

    static string Now() => Stamp();

    // ---------- settings ----------
    public Settings LoadSettings()
    {
        var s = new Settings();
        if (!File.Exists(SettingsPath)) return s;
        var o = ReadSettingsJson();
        s.ListDir = (string?)o["list_dir"] ?? s.ListDir;
        s.TmdbKey = (string?)o["tmdb_key"] ?? "";
        s.OmdbKey = (string?)o["omdb_key"] ?? "";
        s.Language = (string?)o["native_lang"] ?? "en";
        s.OnlinePosters = (bool?)o["online_posters"] ?? true;
        s.OnlineFallback = (bool?)o["online_fallback"] ?? true;
        s.AutoRefresh = (bool?)o["auto_refresh"] ?? true;
        s.ProbeMedia = (bool?)o["probe_media"] ?? false;
        s.PeopleAuto = (bool?)o["people_auto"] ?? false;
        s.FfprobePath = (string?)o["ffprobe_path"] ?? "";
        s.PeopleScope = (string?)o["people_scope"] ?? "main";
        s.RenamePattern = (string?)o["rename_pattern"] ?? RenamePlanner.DefaultPattern;
        if (o["media_dirs"] is JsonArray md) s.MediaDirs = md.Select(x => (string?)x ?? "").Where(x => x.Length > 0).ToList();
        return s;
    }

    /// <summary>Writes the settings; keys this app does not know stay as they are.</summary>
    public void SaveSettings(Settings s)
    {
        var o = ReadSettingsJson();
        o["list_dir"] = s.ListDir;
        o["tmdb_key"] = s.TmdbKey;
        o["omdb_key"] = s.OmdbKey;
        o["native_lang"] = s.Language;
        o["online_posters"] = s.OnlinePosters;
        o["online_fallback"] = s.OnlineFallback;
        o["auto_refresh"] = s.AutoRefresh;
        o["probe_media"] = s.ProbeMedia;
        o["people_auto"] = s.PeopleAuto;
        o["ffprobe_path"] = s.FfprobePath;
        o["people_scope"] = s.PeopleScope;
        o["rename_pattern"] = s.RenamePattern;
        o["media_dirs"] = new JsonArray(s.MediaDirs.Select(x => (JsonNode?)JsonValue.Create(x)).ToArray());
        WriteSettingsJson(o);
    }

    /// <summary>settings.json as it is on disk, with the keys this app does not know; empty when there is no file.</summary>
    JsonObject ReadSettingsJson() => File.Exists(SettingsPath)
        ? JsonNode.Parse(File.ReadAllText(SettingsPath), documentOptions: new JsonDocumentOptions { AllowTrailingCommas = true }) as JsonObject ?? new JsonObject()
        : new JsonObject();

    void WriteSettingsJson(JsonObject o) => File.WriteAllText(SettingsPath, o.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));

    public bool FirstRun => !File.Exists(SettingsPath);

    // ---------- job runner ----------
    void Changed() => JobChanged?.Invoke();

    public void Progress(string stage, long done = 0, long total = 0)
    {
        cts?.Token.ThrowIfCancellationRequested();
        Job.Stage = stage;
        Job.Done = done;
        Job.Total = total;
        Changed();
    }

    public void Log(string msg)
    {
        var l = new List<string>(Job.Log) { $"{DateTime.Now:HH:mm:ss} {msg}" };
        if (l.Count > 200) l.RemoveRange(0, l.Count - 200);
        Job.Log = l;
        Changed();
    }

    /// <summary>Stops the running job at its next step. Queued jobs are dropped. A job that cannot be stopped keeps running: returns false.</summary>
    public bool Stop()
    {
        if (!Job.Running || cts == null || !Job.Stoppable) return false;
        lock (queued) queued.Clear();
        Job.Stopping = true;
        cts.Cancel();
        Log("Stopping…");
        return true;
    }

    /// <summary>Runs <paramref name="work"/> in the background. Returns false when a job is already running.</summary>
    public bool Start(Action work, bool stoppable = true)
    {
        lock (queued)
        {
            if (Job.Running) return false;
            cts = new CancellationTokenSource();
            Job = new JobState { Running = true, Stage = "Starting", Stoppable = stoppable };
        }
        Changed();
        Task.Run(() =>
        {
            try
            {
                work();
                Job.Stage = "Finished";
            }
            catch (OperationCanceledException)
            {
                Job.Stopped = true;
                Log("Stopped. What was finished is kept; the next update continues from there.");
            }
            catch (Exception e)
            {
                Job.Error = $"{e.GetType().Name}: {e.Message}";
                Log("ERROR " + e.Message);
            }
            finally
            {
                Job.Running = false;
                Version++;
                Changed();
                DataChanged?.Invoke();
                RunQueued();
            }
        });
        return true;
    }

    /// <summary>Reads the lists, matches new names with the IMDb index and copies the matched titles into the library.</summary>
    public void Update()
    {
        if (!Matcher.HasIndex)
            throw new InvalidOperationException("The IMDb index is not built yet. Run the Python MovieShelf Setup once (it builds imdb_index.db), then use this app.");
        Scan();
        MatchAll();
        Version++;
    }

    // ---------- scan ----------
    public void Scan()
    {
        var s = LoadSettings();
        var files = Directory.Exists(s.ListDir) ? Directory.GetFiles(s.ListDir, "*.txt").OrderBy(f => f, StringComparer.Ordinal).ToArray() : Array.Empty<string>();
        bool foldersSet = s.MediaDirs.Any(d => d.Length > 0);
        if (files.Length == 0 && !UseFolders())
            throw new InvalidOperationException($"No .txt list found in {s.ListDir} and no movie folder can be read. Add a movie folder in " +
                "Settings → General, or bring a list in with the Load list button.");
        var seen = new HashSet<string>();
        var srcOf = new Dictionary<string, string>();
        int added = 0;
        lock (Lock)
        {
            var existing = new HashSet<string>();
            using (var c = Cmd("SELECT raw FROM files"))
            using (var r = c.ExecuteReader())
                while (r.Read()) existing.Add(r.GetString(0));
            var removed = new HashSet<string>();   // taken out of the list by the user
            using (var c = Cmd("SELECT raw FROM removed"))
            using (var r = c.ExecuteReader())
                while (r.Read()) removed.Add(r.GetString(0));
            using var tx = db.BeginTransaction();
            foreach (var path in files)
            {
                foreach (var line in FolderUtil.SplitLines(Checker.ReadText(path)))   // UTF-8, UTF-16 (Notepad 'Unicode') or Windows-1256
                {
                    string raw = line.Trim();
                    if (raw.Length == 0 || seen.Contains(raw) || removed.Contains(raw) || Checker.IsSideFile(raw)) continue;
                    seen.Add(raw);
                    srcOf[raw] = Path.GetFileName(path);
                    if (existing.Contains(raw)) continue;
                    var p = NameParser.Parse(raw);
                    Exec("INSERT INTO files(raw, list_file, added, title, alt_title, year, resolution, source, codec, bit10, hdr, audio, farsi_dubbed, dual_audio, softsub, edition, grp) VALUES (?,?,?,?,?,?,?,?,?,?,?,?,?,?,?,?,?)",
                        raw, Path.GetFileName(path), Now(), p.Title, p.AltTitle, p.Year, p.Resolution, p.Source, p.Codec, p.Bit10 ? 1 : 0, p.Hdr ? 1 : 0,
                        p.Audio, p.FarsiDubbed ? 1 : 0, p.DualAudio ? 1 : 0, p.Softsub ? 1 : 0, p.Edition, p.Group);
                    added++;
                }
            }
            // movies that come from the movie folders are kept or dropped by ScanMedia, not by the lists (or left alone while the drive is away)
            if (foldersSet) Exec("UPDATE files SET present=0 WHERE list_file IS NOT ?", FolderSrc);
            else Exec("UPDATE files SET present=0");
            using (var up = Cmd("UPDATE files SET present=1, list_file=CASE WHEN list_file=? THEN ? ELSE list_file END WHERE raw=?", FolderSrc, "", ""))
                foreach (var raw in seen)
                {
                    up.Parameters["$p1"].Value = srcOf[raw];
                    up.Parameters["$p2"].Value = raw;
                    up.ExecuteNonQuery();
                }
            // IMDb choices made in the list check for lines that were just added
            var forced = new List<(string Raw, long? Tc)>();
            using (var c = Cmd("SELECT raw, tconst FROM forced"))
            using (var r = c.ExecuteReader())
                while (r.Read()) forced.Add((r.GetString(0), r.IsDBNull(1) ? null : r.GetInt64(1)));
            foreach (var (raw, tc) in forced.Where(f => seen.Contains(f.Raw)))
            {
                Exec("UPDATE files SET tconst=?, conf=?, manual=1, matched=1 WHERE raw=?", tc, tc is null or 0 ? "none" : "manual", raw);
                Exec("DELETE FROM forced WHERE raw=?", raw);
            }
            tx.Commit();
        }
        ReparseIfNeeded();
        int gone = Scalar<int>("SELECT count(*) FROM files WHERE present=0");
        Log($"Scanned {seen.Count} entries: {added} new, {gone} no longer in list");
    }

    /// <summary>Re-reads every stored name when the parser improved; a name whose title or year changed is matched again.</summary>
    void ReparseIfNeeded()
    {
        if (StoredParserVersion() == ParserVersion) return;
        int changed = 0;
        lock (Lock)
        {
            var rows = new List<(int Id, string Raw, bool Manual, string? Title, string? Alt, int? Year)>();
            using (var c = Cmd("SELECT id, raw, manual, title, alt_title, year FROM files"))
            using (var r = c.ExecuteReader())
                while (r.Read())
                    rows.Add((r.GetInt32(0), r.GetString(1), !r.IsDBNull(2) && r.GetInt64(2) != 0, r.IsDBNull(3) ? null : r.GetString(3),
                        r.IsDBNull(4) ? null : r.GetString(4), r.IsDBNull(5) ? null : r.GetInt32(5)));
            using var tx = db.BeginTransaction();
            foreach (var f in rows)
            {
                var p = NameParser.Parse(f.Raw);
                bool rematch = !f.Manual && (p.Title != f.Title || p.AltTitle != f.Alt || p.Year != f.Year);
                if (rematch) changed++;
                Exec("UPDATE files SET title=?, alt_title=?, year=?, resolution=?, source=?, codec=?, bit10=?, hdr=?, audio=?, farsi_dubbed=?, dual_audio=?, softsub=?, edition=?, grp=?" +
                     (rematch ? ", matched=0" : "") + " WHERE id=?",
                    p.Title, p.AltTitle, p.Year, p.Resolution, p.Source, p.Codec, p.Bit10 ? 1 : 0, p.Hdr ? 1 : 0, p.Audio, p.FarsiDubbed ? 1 : 0,
                    p.DualAudio ? 1 : 0, p.Softsub ? 1 : 0, p.Edition, p.Group, f.Id);
            }
            tx.Commit();
        }
        if (FirstRun) SaveSettings(LoadSettings());   // Python writes all its settings here, even when there was no file yet
        var o = ReadSettingsJson();
        o[ParserVersionKey] = ParserVersion;
        WriteSettingsJson(o);
        Log($"Re-read file names with improved parser ({changed} to re-match)");
    }

    int? StoredParserVersion()
    {
        if (!File.Exists(SettingsPath)) return null;
        return ReadSettingsJson()[ParserVersionKey] is JsonValue v && v.TryGetValue<int>(out int n) ? n : null;
    }

    // ---------- matching ----------
    public void MatchAll(bool onlyUnmatched = true)
    {
        using var ix = Matcher.Open();
        var rows = new List<(int Id, MatchInput P)>();
        using (var c = Cmd("SELECT id, title, alt_title, year, conf FROM files WHERE manual=0" + (onlyUnmatched ? " AND matched=0" : "")))
        using (var r = c.ExecuteReader())
            while (r.Read())
                rows.Add((r.GetInt32(0), new MatchInput
                {
                    Title = r.IsDBNull(1) ? null : r.GetString(1), AltTitle = r.IsDBNull(2) ? null : r.GetString(2),
                    Year = r.IsDBNull(3) ? null : r.GetInt32(3), Conf = r.IsDBNull(4) ? null : r.GetString(4),
                }));
        var res = new List<(int? Tc, string Conf, double? Score, int Id)>();
        for (int n = 0; n < rows.Count; n++)
        {
            if (n % 100 == 0) Progress("Matching titles with IMDb", n, rows.Count);
            var (tc, conf, sc) = Matcher.Pick(ix, rows[n].P);
            res.Add((tc, conf, sc, rows[n].Id));
        }
        lock (Lock)
        {
            using var tx = db.BeginTransaction();
            foreach (var (tc, conf, sc, id) in res)
                Exec("UPDATE files SET tconst=?, conf=?, score=?, matched=1 WHERE id=?", tc, conf, sc, id);
            tx.Commit();
        }
        Log($"Matched {res.Count(r => r.Tc != null)} of {res.Count} offline");
        if (LoadSettings().OnlineFallback)
        {
            var none = res.Where(r => r.Tc == null).Select(r => r.Id).ToList();
            if (none.Count > 0) OnlineMatch(none);
        }
        ImportTitles(ix);
    }

    /// <summary>Copies basic info for matched IMDb ids from the index into the library.</summary>
    public void ImportTitles(SqliteConnection ix, IEnumerable<int>? forceIds = null)
    {
        var ids = forceIds?.ToList() ?? new List<int>();
        if (forceIds == null)
        {
            using var c = Cmd("SELECT DISTINCT tconst FROM files WHERE tconst IS NOT NULL AND tconst NOT IN (SELECT tconst FROM titles)");
            using var r = c.ExecuteReader();
            while (r.Read()) ids.Add(r.GetInt32(0));
        }
        var rows = new List<object?[]>();
        foreach (var i in ids)
        {
            using var c = Cmd(ix, "SELECT * FROM titles WHERE id=?", new object?[] { i });
            using var r = c.ExecuteReader();
            if (!r.Read()) { rows.Add(new object?[] { i, null, null, null, null, null, null, null, null, null, null, null, null }); continue; }
            object? V(string n) { var o = r.GetOrdinal(n); return r.IsDBNull(o) ? null : r.GetValue(o); }
            string? S(string n) => V(n) as string;
            var akas = new List<(string Title, string? Region, string? Lang, string? Types)>();
            using (var ac = Cmd(ix, "SELECT title, region, lang, types FROM akas WHERE id=?", new object?[] { i }))
            using (var ar = ac.ExecuteReader())
                while (ar.Read())
                    akas.Add((ar.GetString(0), ar.IsDBNull(1) ? null : ar.GetString(1), ar.IsDBNull(2) ? null : ar.GetString(2), ar.IsDBNull(3) ? null : ar.GetString(3)));
            var (en, fa) = PickAkas(S("title") ?? "", akas);
            var alist = akas.Select(a => a.Title).Distinct().Where(t => t != S("title") && t != S("orig")).OrderBy(t => t, StringComparer.Ordinal).Take(25).ToList();
            rows.Add(new object?[]
            {
                i, Imdb.TypeNames.GetValueOrDefault(Convert.ToInt32(V("type")), ""), S("title"), S("orig"), V("year"), V("endyear"),
                V("runtime"), S("genres"), V("rating"), V("votes"), en, fa,
                JsonSerializer.Serialize(alist, new JsonSerializerOptions { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }),
            });
        }
        lock (Lock)
        {
            using var tx = db.BeginTransaction();
            foreach (var row in rows)
                Exec("INSERT INTO titles(tconst,type,title,orig,year,endyear,runtime,genres,rating,votes,en_title,fa_title,akas,updated) VALUES (?,?,?,?,?,?,?,?,?,?,?,?,?,?) " +
                     "ON CONFLICT(tconst) DO UPDATE SET type=excluded.type, title=excluded.title, orig=excluded.orig, year=excluded.year, endyear=excluded.endyear, " +
                     "runtime=excluded.runtime, genres=excluded.genres, rating=excluded.rating, votes=excluded.votes, en_title=excluded.en_title, " +
                     "fa_title=coalesce(titles.fa_title, excluded.fa_title), akas=excluded.akas, updated=excluded.updated",
                    row.Append(Now()).ToArray());
            tx.Commit();
        }
    }

    static (string? En, string? Fa) PickAkas(string title, List<(string Title, string? Region, string? Lang, string? Types)> akas)
    {
        static string? Best(List<(string Title, string? Region, string? Lang, string? Types)> c) =>
            c.OrderBy(a => (a.Types ?? "").Contains("imdbDisplay") ? 0 : 1).Select(a => a.Title).FirstOrDefault();
        string? en = null;
        foreach (var reg in new[] { "US", "GB", "CA", "AU", "XWW" })
        {
            en = Best(akas.Where(a => a.Region == reg && !(a.Types ?? "").StartsWith("working")).ToList());
            if (en != null) break;
        }
        en ??= Best(akas.Where(a => a.Lang == "en").ToList()) ?? title;
        var fa = Best(akas.Where(a => a.Region == "IR" || a.Lang == "fa").ToList());
        return (en, fa);
    }

    /// <summary>Gives a file the IMDb title the user chose (0 or null = not on IMDb). True when that title still needs its details (see <see cref="SetMatchAndRefresh"/>).</summary>
    public bool SetMatch(int fileId, int? tconst)
    {
        bool notOnImdb = tconst is null or 0, needsDetails = false;
        lock (Lock)
        {
            Exec("UPDATE files SET tconst=?, conf=?, manual=1, matched=1 WHERE id=?", tconst, notOnImdb ? "none" : "manual", fileId);
            if (!notOnImdb) needsDetails = Scalar<int>("SELECT coalesce(max(enriched), 0) FROM titles WHERE tconst=?", tconst) == 0;
        }
        if (needsDetails && Matcher.HasIndex)
        {
            using var ix = Matcher.Open();
            ImportTitles(ix, new[] { tconst!.Value });
        }
        Version++;
        IssueRev++;
        DataChanged?.Invoke();
        return needsDetails;
    }

    /// <summary>"This match is right": keep the current IMDb id as a manual match.</summary>
    public void Accept(int fileId)
    {
        lock (Lock) Exec("UPDATE files SET conf='manual', manual=1 WHERE id=? AND tconst IS NOT NULL", fileId);
        Version++;
        IssueRev++;
        DataChanged?.Invoke();
    }

    /// <summary>IMDb titles a name could mean (the Fix match window): the offline index, then IMDb's online suggestions. Blocking: call it off the UI thread.</summary>
    public List<Candidate2> Lookup(int fileId, string? query)
    {
        MatchInput f;
        using (var rd = Reader())
        using (var c = Cmd(rd, "SELECT title, alt_title, year FROM files WHERE id=?", new object?[] { fileId }))
        using (var r = c.ExecuteReader())
        {
            if (!r.Read()) return new();
            f = new MatchInput { Title = r.IsDBNull(0) ? null : r.GetString(0), AltTitle = r.IsDBNull(1) ? null : r.GetString(1), Year = r.IsDBNull(2) ? null : r.GetInt32(2) };
        }
        var found = new List<Candidate2>();
        if (Matcher.HasIndex)
        {
            var p = f;
            if (!string.IsNullOrWhiteSpace(query))
            {
                var q = NameParser.Parse(query);
                p = new MatchInput { Title = q.Title, AltTitle = q.AltTitle, Year = q.Year };
            }
            using var ix = Matcher.Open();
            found.AddRange(Matcher.ToCandidates(Matcher.Candidates(ix, p).Concat(Matcher.Fuzzy(ix, p, cutoff: 75))));
        }
        try
        {
            string text = !string.IsNullOrEmpty(query) ? query : f.Year is > 0 ? $"{f.Title} {f.Year}" : f.Title ?? "";
            var seen = found.Select(c => c.Tconst).ToHashSet();
            foreach (var s in ImdbOnline.SuggestAsync(Net, text).GetAwaiter().GetResult())
                if (seen.Add(s.Tconst))
                    found.Add(new Candidate2 { Tconst = s.Tconst, Title = s.Title ?? "", Year = s.Year, Type = s.Kind, Stars = s.Stars, Image = s.Image, How = "online" });
        }
        catch (Exception) { }   // offline: the index results stay
        return found;
    }

    // ---------- user data ----------
    public void SetUser(IEnumerable<string> mkeys, bool? fav = null, bool? watched = null, int? rating = null, bool clearRating = false, string? notes = null)
    {
        lock (Lock)
        {
            using var tx = db.BeginTransaction();
            foreach (var mk in mkeys)
            {
                Exec("INSERT OR IGNORE INTO user(mkey) VALUES (?)", mk);
                if (fav != null) Exec("UPDATE user SET fav=?, updated=? WHERE mkey=?", fav.Value ? 1 : 0, Now(), mk);
                if (watched != null)
                {
                    if (watched.Value) Exec("UPDATE user SET watched=1, watched_at=coalesce(watched_at, ?), updated=? WHERE mkey=?", Now(), Now(), mk);
                    else Exec("UPDATE user SET watched=0, watched_at=NULL, updated=? WHERE mkey=?", Now(), mk);
                }
                if (rating != null || clearRating) Exec("UPDATE user SET myrating=?, updated=? WHERE mkey=?", clearRating ? null : rating, Now(), mk);
                if (notes != null) Exec("UPDATE user SET notes=?, updated=? WHERE mkey=?", notes, Now(), mk);
            }
            tx.Commit();
        }
        Version++;
    }

    // ---------- problems ----------
    /// <param name="fuzzy">False skips the misspelling search, which takes seconds on a big library.</param>
    public List<MatchProblem> MatchProblems(bool fuzzy = true)
    {
        var list = new List<MatchProblem>();
        var titles = new Dictionary<int, Candidate2>();
        using var rd = Reader();
        using (var c = Cmd(rd, "SELECT tconst, title, year, votes, rating, type FROM titles", Array.Empty<object?>()))
        using (var r = c.ExecuteReader())
            while (r.Read())
                titles[r.GetInt32(0)] = new Candidate2
                {
                    Tconst = r.GetInt32(0), Title = r.IsDBNull(1) ? "" : r.GetString(1), Year = r.IsDBNull(2) ? null : r.GetInt32(2),
                    Votes = r.IsDBNull(3) ? 0 : r.GetInt32(3), Rating = r.IsDBNull(4) ? null : r.GetDouble(4), Type = r.IsDBNull(5) ? null : r.GetString(5),
                };
        var rows = new List<(int Id, string Raw, string? Title, string? Alt, int? Year, string Conf, int? Tc)>();
        using (var c = Cmd(rd, $"SELECT id, raw, title, alt_title, year, conf, tconst FROM files WHERE present=1 AND manual=0 AND conf IN ({string.Join(',', Matcher.Unsure.Select(u => "'" + u + "'"))}) ORDER BY title", Array.Empty<object?>()))
        using (var r = c.ExecuteReader())
            while (r.Read())
                rows.Add((r.GetInt32(0), r.GetString(1), r.IsDBNull(2) ? null : r.GetString(2), r.IsDBNull(3) ? null : r.GetString(3),
                    r.IsDBNull(4) ? null : r.GetInt32(4), r.GetString(5), r.IsDBNull(6) ? null : r.GetInt32(6)));
        var where = new Dictionary<string, List<string>>();   // list entry -> the places of its video files, for "Show in folder"
        using (var c = Cmd(rd, "SELECT raw, path FROM fileinfo ORDER BY path", Array.Empty<object?>()))
        using (var r = c.ExecuteReader())
            while (r.Read())
            {
                if (r.IsDBNull(0)) continue;
                if (!where.TryGetValue(r.GetString(0), out var l)) where[r.GetString(0)] = l = new();
                l.Add(r.GetString(1));
            }
        SqliteConnection? ix = Matcher.HasIndex ? Matcher.Open() : null;
        try
        {
            foreach (var f in rows)
            {
                Candidate2? cur = f.Tc != null && titles.TryGetValue(f.Tc.Value, out var t) ? t : null;
                var mp = new MatchProblem
                {
                    FileId = f.Id, Raw = f.Raw, Title = f.Title, Alt = f.Alt, Year = f.Year, Conf = f.Conf,
                    Reason = Matcher.Reason(f.Conf, f.Year, cur?.Year), Current = cur,
                    Paths = where.TryGetValue(f.Raw, out var ps) ? ps : new(),
                };
                if (ix != null)
                    mp.Cands = Matcher.AllCandidates(ix, new MatchInput { Title = f.Title, AltTitle = f.Alt, Year = f.Year, Conf = f.Conf },
                        f.Tc != null ? new[] { f.Tc.Value } : null, fuzzy: fuzzy);
                list.Add(mp);
            }
        }
        finally { ix?.Dispose(); }
        var order = new[] { "none", "review", "fuzzy", "noyear", "online" };
        return list.OrderBy(m => Array.IndexOf(order, m.Conf) is var i and >= 0 ? i : 9).ToList();
    }

    public void Dismiss(string ikey, bool undo = false)
    {
        IssueRev++;
        lock (Lock) Exec(undo ? "DELETE FROM dismissed WHERE ikey=?" : "INSERT OR IGNORE INTO dismissed(ikey) VALUES (?)", ikey);
    }

    // ---------- reading the library ----------
    static string? Str(SqliteDataReader r, string n) { int o = r.GetOrdinal(n); return r.IsDBNull(o) ? null : r.GetString(o); }
    static int? Int(SqliteDataReader r, string n) { int o = r.GetOrdinal(n); return r.IsDBNull(o) ? null : (int)r.GetInt64(o); }
    static double? Dbl(SqliteDataReader r, string n) { int o = r.GetOrdinal(n); return r.IsDBNull(o) ? null : r.GetDouble(o); }
    static bool Flag(SqliteDataReader r, string n) => (Int(r, n) ?? 0) != 0;

    static readonly Dictionary<string, int> ResRank = new() { ["2160p"] = 5, ["1080p"] = 4, ["720p"] = 3, ["576p"] = 2, ["480p"] = 1, ["SD"] = 1 };
    static readonly string[] ConfOrder = { "exact", "good", "noyear", "online", "review", "fuzzy", "none" };

    /// <summary>One entry per movie (files grouped by IMDb id); unmatched files stand alone.</summary>
    public List<Movie> LoadMovies()
    {
        using var rd = Reader();
        var users = new Dictionary<string, (bool Fav, bool Watched, int? Rating, string? Notes, string? At)>();
        using (var c = Cmd(rd, "SELECT * FROM user", Array.Empty<object?>()))
        using (var r = c.ExecuteReader())
            while (r.Read())
                users[Str(r, "mkey")!] = (Flag(r, "fav"), Flag(r, "watched"), Int(r, "myrating"), Str(r, "notes"), Str(r, "watched_at"));
        var fa = new Dictionary<int, string>();
        using (var c = Cmd(rd, "SELECT tconst, title FROM fa_titles", Array.Empty<object?>()))
        using (var r = c.ExecuteReader())
            while (r.Read()) if (!r.IsDBNull(1)) fa[r.GetInt32(0)] = r.GetString(1);
        var info = new Dictionary<string, List<(string Path, long Size)>>();
        using (var c = Cmd(rd, "SELECT raw, path, size FROM fileinfo ORDER BY path", Array.Empty<object?>()))
        using (var r = c.ExecuteReader())
            while (r.Read())
            {
                string raw = r.GetString(0);
                if (!info.TryGetValue(raw, out var l)) info[raw] = l = new();
                l.Add((r.GetString(1), r.IsDBNull(2) ? 0 : r.GetInt64(2)));
            }
        var media = LoadMediaRows(rd);

        var groups = new Dictionary<string, List<(MovieFile F, ParsedFields P, int? Tc)>>();
        var order = new List<string>();
        using (var c = Cmd(rd, "SELECT * FROM files WHERE present=1 ORDER BY id", Array.Empty<object?>()))
        using (var r = c.ExecuteReader())
            while (r.Read())
            {
                int? tc = Int(r, "tconst");
                if (tc == 0) tc = null;   // 'not on IMDb', as an empty id
                int id = (int)r.GetInt64(r.GetOrdinal("id"));
                string mk = tc != null ? $"tt{tc:0000000}" : $"f{id}";
                var f = new MovieFile
                {
                    Id = id, Raw = Str(r, "raw") ?? "", Res = Str(r, "resolution"), Src = Str(r, "source"), Codec = Str(r, "codec"),
                    B10 = Flag(r, "bit10"), Hdr = Flag(r, "hdr"), Audio = Str(r, "audio"), Fa = Flag(r, "farsi_dubbed"),
                    Dual = Flag(r, "dual_audio"), Sub = Flag(r, "softsub"), Ed = Str(r, "edition"), Grp = Str(r, "grp"),
                    Conf = Str(r, "conf"), List = Str(r, "list_file"),
                };
                if (info.TryGetValue(f.Raw, out var paths))
                {
                    f.Paths = paths.Select(p => p.Path).ToList();
                    f.Size = paths.Max(p => p.Size);
                    ApplyMedia(f, media);
                }
                if (!groups.TryGetValue(mk, out var g)) { groups[mk] = g = new(); order.Add(mk); }
                g.Add((f, new ParsedFields(Str(r, "title"), Str(r, "alt_title"), Int(r, "year")), tc));
            }

        var titles = new Dictionary<int, SqliteDataReaderSnapshot>();
        // parsing the JSON columns and folding the search text is the slow part, and each movie is independent
        var result = new Movie[order.Count];
        using (var c = Cmd(rd, "SELECT * FROM titles", Array.Empty<object?>()))
        using (var r = c.ExecuteReader())
        {
            var columns = SqliteDataReaderSnapshot.Columns(r);
            int tconst = r.GetOrdinal("tconst");
            while (r.Read()) titles[(int)r.GetInt64(tconst)] = new SqliteDataReaderSnapshot(r, columns);
        }

        Parallel.For(0, order.Count, i =>
        {
            string mk = order[i];
            var g = groups[mk];
            var (f0, p0, tc0) = g[0];
            var m = new Movie { K = mk, Tconst = tc0, PTitle = p0.Title ?? "", PAlt = p0.Alt, PYear = p0.Year, Files = g.Select(x => x.F).ToList() };
            m.Res = m.Files.Select(f => f.Res).Where(x => x != null).OrderByDescending(x => ResRank.GetValueOrDefault(x!, 0)).FirstOrDefault();
            var confs = m.Files.Select(f => f.Conf ?? "none").ToList();
            m.Conf = confs.Contains("manual") ? "manual"
                : confs.OrderBy(cf => Array.IndexOf(ConfOrder, cf) is var i and >= 0 ? i : 9).First();
            if (tc0 != null && titles.TryGetValue(tc0.Value, out var t))
            {
                m.Type = t.Str("type");
                m.Title = (t.Str("title") is { Length: > 0 } tt ? tt : p0.Title) ?? "";
                m.Orig = t.Str("orig");
                m.En = t.Str("en_title");
                m.FaTitle = (fa.TryGetValue(tc0.Value, out var ft) ? ft : t.Str("fa_title")) is { Length: > 0 } fts ? fts : null;
                m.Year = t.Int("year") ?? p0.Year;
                m.EndYear = t.Int("endyear");
                m.Runtime = t.Int("runtime");
                m.Genres = (t.Str("genres") ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries);
                m.Rating = t.Dbl("rating");
                m.Votes = t.Int("votes") ?? 0;
                m.Akas = Movie.ParseStrings(t.Str("akas"));
                m.Dir = Movie.ParsePeople(t.Str("directors"));
                m.Wri = Movie.ParsePeople(t.Str("writers"));
                m.Cast = Movie.ParsePeople(t.Str("cast_"));
                m.Plot = t.Str("plot");
                m.Tagline = t.Str("tagline");
                m.Countries = Movie.ParseStrings(t.Str("countries"));
                m.Langs = Movie.ParseStrings(t.Str("languages"));
                m.OrigLang = t.Str("orig_lang");
                m.TmdbCast = Movie.ParseCastNames(t.Str("tmdb_cast"));
                m.TmdbCastJson = t.Str("tmdb_cast");
                m.CrewJson = t.Str("crew");
                m.Backdrop = t.Str("backdrop_url");
                m.Collection = t.Str("collection");
                m.Rt = t.Int("rt");
                m.Mc = t.Int("mc");
                m.HasPoster = !string.IsNullOrEmpty(t.Str("poster"));
            }
            else
            {
                m.Title = string.IsNullOrEmpty(p0.Title) ? f0.Raw : p0.Title;
                m.Year = p0.Year;
            }
            if (users.TryGetValue(mk, out var u))
            {
                m.Fav = u.Fav; m.Watched = u.Watched; m.MyRating = u.Rating; m.Notes = u.Notes; m.WatchedAt = u.At;
            }
            m.Prepare();
            result[i] = m;
        });
        return result.ToList();
    }

    sealed record ParsedFields(string? Title, string? Alt, int? Year);

    /// <summary>The columns of one titles row, kept so the reader can move on.</summary>
    sealed class SqliteDataReaderSnapshot
    {
        readonly Dictionary<string, int> columns;
        readonly object?[] v;

        /// <summary>Where each wanted column sits in the row; shared by all the snapshots of one query.</summary>
        public static Dictionary<string, int> Columns(SqliteDataReader r)
        {
            var map = new Dictionary<string, int>();
            for (int i = 0; i < r.FieldCount; i++)
            {
                string n = r.GetName(i);
                if (n is "tmdb_cast" or "cast_" or "directors" or "writers" or "crew" or "akas" or "countries" or "languages" or "plot" or "tagline"
                    or "type" or "title" or "orig" or "en_title" or "fa_title" or "genres" or "orig_lang" or "collection" or "poster" or "backdrop_url"
                    or "year" or "endyear" or "runtime" or "rating" or "votes" or "rt" or "mc")
                    map[n] = map.Count;
            }
            return map;
        }

        public SqliteDataReaderSnapshot(SqliteDataReader r, Dictionary<string, int> columns)
        {
            this.columns = columns;
            v = new object?[columns.Count];
            foreach (var (name, slot) in columns)
            {
                int o = r.GetOrdinal(name);
                v[slot] = r.IsDBNull(o) ? null : r.GetValue(o);
            }
        }

        object? Get(string n) => columns.TryGetValue(n, out int slot) ? v[slot] : null;

        public string? Str(string n) => Get(n) as string;
        public int? Int(string n) => Get(n) is { } o ? Convert.ToInt32(o) : null;
        public double? Dbl(string n) => Get(n) is { } o ? Convert.ToDouble(o) : null;
    }

    public Stats GetStats()
    {
        return new Stats
        {
            Files = Scalar<int>("SELECT count(*) FROM files WHERE present=1"),
            Matched = Scalar<int>("SELECT count(*) FROM files WHERE present=1 AND tconst IS NOT NULL"),
            Titles = Scalar<int>("SELECT count(DISTINCT tconst) FROM files WHERE present=1 AND tconst IS NOT NULL"),
            Problems = Scalar<int>("SELECT count(*) FROM files WHERE present=1 AND manual=0 AND conf IN ('review','fuzzy','online','noyear','none')"),
            Duplicates = OpenDuplicates(),
            Index = Matcher.HasIndex,
        };
    }

    public string PosterPath(Movie m) => Path.Combine(PosterDir, m.K + ".jpg");
}
