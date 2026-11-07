using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Data.Sqlite;

namespace MovieShelf.Core;

/// <summary>One automatic backup file in data/backups.</summary>
public sealed record BackupFile(string Name, long Size);

/// <summary>How many of each thing a personal backup held. <see cref="NeedsUpdate"/>: manual matches wait in 'forced' until the next scan applies them;
/// <see cref="Lists"/>: names the backup's lists held that no list had, written back to a list file.</summary>
public sealed record BackupCounts(int User, int Manual, int Dismissed, int Views, int Wishlist, int FaTitles, int Removed = 0, int Lists = 0)
{
    public bool NeedsUpdate => Manual > 0 || Lists > 0;
}

/// <summary>A saved view. <see cref="State"/> is opaque: the Python web UI stores {F, sort, rev, group}; the native UI may store its own object in the same table
/// (the Python UI then applies it as an empty filter, which does no harm).</summary>
public sealed class SavedView
{
    public int Id;
    public string Name = "";
    public JsonNode State = new JsonObject();
}

public static class WishStatus
{
    public const string Wanted = "wanted", Found = "found";
}

public sealed class WishItem
{
    public int Id;
    public string Title = "";
    public int? Year, Tconst;
    public string? Note, Source, Added;
    public string Status = WishStatus.Wanted;
}

/// <summary>A wishlist entry to add; only <see cref="Title"/> is required.</summary>
public sealed record WishNew(string Title, int? Year = null, int? Tconst = null, string? Note = null, string? Source = null);

/// <summary>Personal backup (favorites, watched, ratings, notes, manual matches, ignored problems, views, wishlist, own Persian titles) as a JSON file,
/// saved views and the wishlist. Same JSON keys as the Python backup.py, so either app restores the other's file.</summary>
public sealed partial class Library
{
    public const int KeepBackups = 14;   // automatic backups kept
    static readonly string[] PersonalKeys = { "user", "manual", "dismissed", "views", "wishlist", "fa_titles", "removed", "lists" };

    static readonly JsonSerializerOptions JsonCompact = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
    static readonly JsonSerializerOptions JsonIndent1 = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping, WriteIndented = true, IndentSize = 1 };
    static readonly JsonSerializerOptions JsonIndent2 = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping, WriteIndented = true, IndentSize = 2 };
    static readonly UTF8Encoding Utf8 = new(false);

    /// <summary>Local time in the format of the databases, whatever the user's culture or calendar.</summary>
    static string Stamp(string format = "yyyy-MM-dd HH:mm:ss") => DateTime.Now.ToString(format, CultureInfo.InvariantCulture);

    /// <summary>data/backups (created when asked for).</summary>
    public string BackupsDir
    {
        get
        {
            string d = Path.Combine(DataDir, "backups");
            Directory.CreateDirectory(d);
            return d;
        }
    }

    // ---------- snapshot ----------
    static JsonArray BackupRows(SqliteConnection rd, string sql)
    {
        var rows = new JsonArray();
        using var c = rd.CreateCommand();
        c.CommandText = sql;
        using var r = c.ExecuteReader();
        while (r.Read())
        {
            var o = new JsonObject();
            for (int i = 0; i < r.FieldCount; i++)
                o[r.GetName(i)] = r.IsDBNull(i) ? null : r.GetValue(i) switch
                {
                    long l => JsonValue.Create(l),
                    double d => JsonValue.Create(d),
                    string s => JsonValue.Create(s),
                    byte[] b => JsonValue.Create(Convert.ToBase64String(b)),
                    var other => JsonValue.Create(Convert.ToString(other, CultureInfo.InvariantCulture)),
                };
            rows.Add(o);
        }
        return rows;
    }

    /// <summary>Everything that only exists in the user's database (the IMDb data can always be downloaded again). Quick.</summary>
    public JsonObject MakeBackup()
    {
        using var rd = Reader();
        var dismissed = new JsonArray();
        foreach (var row in BackupRows(rd, "SELECT ikey FROM dismissed")) dismissed.Add(JsonValue.Create((string?)row!["ikey"]));
        return new JsonObject
        {
            ["app"] = "MovieShelf", ["format"] = 1, ["created"] = Stamp(),
            ["user"] = BackupRows(rd, "SELECT mkey, fav, watched, myrating, notes, watched_at FROM user"),
            ["manual"] = BackupRows(rd, "SELECT raw, tconst FROM files WHERE manual=1"),
            ["forced"] = BackupRows(rd, "SELECT raw, tconst FROM forced"),
            ["dismissed"] = dismissed,
            ["views"] = BackupRows(rd, "SELECT name, state FROM views ORDER BY id"),
            ["wishlist"] = BackupRows(rd, "SELECT title, year, tconst, note, source, status, added FROM wishlist ORDER BY id"),
            ["fa_titles"] = BackupRows(rd, "SELECT tconst, title FROM fa_titles WHERE source='user'"),
            ["removed"] = BackupRows(rd, "SELECT raw, tconst, title, removed FROM removed"),
            ["lists"] = ListFiles(),
        };
    }

    /// <summary>{file name: [movie names]} of every .txt list in the list folder: the movies the user brought in.</summary>
    JsonObject ListFiles()
    {
        var o = new JsonObject();
        string dir = LoadSettings().ListDir;
        if (!Directory.Exists(dir)) return o;
        foreach (var f in Directory.GetFiles(dir, "*.txt").OrderBy(f => f, StringComparer.Ordinal))
        {
            List<string> names;
            try { names = FolderUtil.SplitLines(Checker.ReadText(f)).Select(l => l.Trim()).Where(l => l.Length > 0).ToList(); }
            catch (IOException) { continue; }
            if (names.Count > 0) o[Path.GetFileName(f)] = new JsonArray(names.Select(n => (JsonNode?)JsonValue.Create(n)).ToArray());
        }
        return o;
    }

    /// <summary>Names the backup holds that no list has now are written back to a list file (same file name; "name (restored)" if that file exists).
    /// Lists that exist are never rewritten, and a name the user removed stays removed. Returns how many names came back.</summary>
    int RestoreLists(JsonNode? lists)
    {
        if (lists is not JsonObject lo || lo.Count == 0) return 0;
        string dest = LoadSettings().ListDir;
        Directory.CreateDirectory(dest);
        var have = new HashSet<string>();
        foreach (var f in Directory.GetFiles(dest, "*.txt"))
        {
            try { have.UnionWith(FolderUtil.SplitLines(Checker.ReadText(f)).Select(l => l.Trim())); }
            catch (IOException) { }
        }
        var gone = new HashSet<string>();
        lock (Lock)
            using (var c = Cmd("SELECT raw FROM removed"))
            using (var r = c.ExecuteReader())
                while (r.Read()) gone.Add(r.GetString(0));
        int back = 0;
        foreach (var (key, val) in lo)
        {
            string name = Path.GetFileName(key);
            if (!name.EndsWith(".txt", StringComparison.OrdinalIgnoreCase) || val is not JsonArray arr) continue;
            var missing = arr.Select(n => (Raw(n) is { } v ? Convert.ToString(v, CultureInfo.InvariantCulture) ?? "" : "").Trim())
                .Where(x => x.Length > 0 && !have.Contains(x) && !gone.Contains(x)).Distinct().ToList();
            if (missing.Count == 0) continue;
            string target = Path.Combine(dest, name);
            if (File.Exists(target)) target = Path.Combine(dest, Path.GetFileNameWithoutExtension(name) + " (restored).txt");
            File.AppendAllText(target, string.Join("\r\n", missing) + "\r\n", Utf8);   // Python's text mode: \r\n on Windows
            have.UnionWith(missing);
            back += missing.Count;
        }
        return back;
    }

    /// <summary>The file name the Python app offers for a manual personal backup.</summary>
    public static string BackupFileName() => $"MovieShelf backup {Stamp("yyyy-MM-dd")}.json";

    /// <summary>Writes a personal backup to <paramref name="path"/> (UTF-8, indent 1 like Python's "Save backup").</summary>
    public void SaveBackupFile(string path)
    {
        File.WriteAllText(path, MakeBackup().ToJsonString(JsonIndent1), Utf8);
    }

    // ---------- restore ----------
    static object? Raw(JsonNode? n)
    {
        if (n == null) return null;
        if (n is JsonValue v)
        {
            if (v.TryGetValue<long>(out var l)) return l;
            if (v.TryGetValue<double>(out var d)) return d;
            if (v.TryGetValue<string>(out var s)) return s;
            if (v.TryGetValue<bool>(out var b)) return b ? 1L : 0L;
        }
        return n.ToJsonString();
    }

    /// <summary>Python's "x or 0".</summary>
    static object OrZero(JsonNode? n) => Raw(n) switch { null => 0L, long l when l != 0 => l, double d when d != 0 => d, string { Length: > 0 } s => s, _ => 0L };

    static string Need(JsonNode? o, string key) =>
        Raw((o as JsonObject)?[key]) is { } v ? Convert.ToString(v, CultureInfo.InvariantCulture)! : throw new InvalidDataException($"The backup has an entry without '{key}'.");

    static JsonArray ArrayOf(JsonObject o, string key) => o[key] as JsonArray ?? new JsonArray();

    /// <summary>Merges a personal backup (from <see cref="MakeBackup"/> or the Python app) into the library.
    /// Manual matches wait in 'forced'; when <see cref="BackupCounts.NeedsUpdate"/> the caller should run an update (scan) so they are applied.</summary>
    public BackupCounts RestoreBackup(JsonNode? data)
    {
        if (data is not JsonObject o || !(o["app"] is JsonValue a && a.TryGetValue<string>(out var app) && app == "MovieShelf"))
            throw new InvalidDataException("This is not a MovieShelf backup file.");
        var user = ArrayOf(o, "user"); var manual = ArrayOf(o, "manual"); var forced = ArrayOf(o, "forced"); var dismissed = ArrayOf(o, "dismissed");
        var views = ArrayOf(o, "views"); var wish = ArrayOf(o, "wishlist"); var fa = ArrayOf(o, "fa_titles"); var removed = ArrayOf(o, "removed");
        lock (Lock)
        {
            using var tx = db.BeginTransaction();
            foreach (var u in user)
            {
                string mk = Need(u, "mkey");
                Exec("INSERT OR IGNORE INTO user(mkey) VALUES (?)", mk);
                Exec("UPDATE user SET fav=?, watched=?, myrating=?, notes=?, watched_at=?, updated=? WHERE mkey=?",
                    OrZero(u!["fav"]), OrZero(u["watched"]), Raw(u["myrating"]), Raw(u["notes"]), Raw(u["watched_at"]), Stamp(), mk);
            }
            foreach (var m in manual.Concat(forced))
                Exec("INSERT OR REPLACE INTO forced(raw, tconst) VALUES (?,?)", Need(m, "raw"), Raw(m!["tconst"]));
            foreach (var k in dismissed) Exec("INSERT OR IGNORE INTO dismissed(ikey) VALUES (?)", Raw(k));
            foreach (var v in views) Exec("INSERT OR REPLACE INTO views(name, state) VALUES (?,?)", Need(v, "name"), Raw(v!["state"]));
            foreach (var w in wish)
                Exec("INSERT OR IGNORE INTO wishlist(title, year, tconst, note, source, status, added) VALUES (?,?,?,?,?,?,?)",
                    Need(w, "title"), Raw(w!["year"]), Raw(w["tconst"]), Raw(w["note"]), Raw(w["source"]),
                    Raw(w["status"]) is string { Length: > 0 } st ? st : WishStatus.Wanted, Raw(w["added"]));
            foreach (var f in fa) Exec("INSERT OR REPLACE INTO fa_titles(tconst, title, source) VALUES (?,?,'user')", Raw(f!["tconst"]), Raw(f["title"]));
            foreach (var r in removed)
                Exec("INSERT OR IGNORE INTO removed(raw, tconst, title, removed) VALUES (?,?,?,?)", Need(r, "raw"), Raw(r!["tconst"]), Raw(r["title"]), Raw(r["removed"]));
            tx.Commit();
        }
        int lists = RestoreLists(o["lists"]);
        Version++;
        IssueRev++;
        DataChanged?.Invoke();
        return new BackupCounts(user.Count, manual.Count + forced.Count, dismissed.Count, views.Count, wish.Count, fa.Count, removed.Count, lists);
    }

    /// <summary>Restores one automatic backup by file name (see <see cref="ListBackups"/>).</summary>
    public BackupCounts RestoreBackup(string name) => RestoreBackup(LoadBackup(name));

    /// <summary>Restores a personal backup .json from anywhere (the "Restore from file" choice).</summary>
    public BackupCounts RestoreBackupFile(string path)
    {
        JsonNode? n;
        try { n = JsonNode.Parse(File.ReadAllText(path)); }
        catch (JsonException) { throw new InvalidDataException("That file is not a MovieShelf backup."); }
        return RestoreBackup(n);
    }

    // ---------- automatic backups ----------
    /// <summary>One backup file per day, written when the app starts (call it once at start-up; skip when <see cref="FirstRun"/>). Old ones are pruned.
    /// Returns the path, or null when today's holds the same data or there is nothing personal yet.</summary>
    public string? AutoBackup()
    {
        string path = Path.Combine(BackupsDir, $"personal-{Stamp("yyyy-MM-dd")}.json");
        var snap = MakeBackup();
        if (File.Exists(path))   // today's copy is rewritten only when something in it changed (new list names, a new favorite)
        {
            JsonObject? old = null;
            try { old = JsonNode.Parse(File.ReadAllText(path)) as JsonObject; }
            catch (Exception e) when (e is IOException or JsonException) { }
            if (old != null && SameBackup(old, snap)) return null;
        }
        if (!PersonalKeys.Any(k => snap[k] is JsonArray { Count: > 0 } or JsonObject { Count: > 0 })) return null;   // nothing personal yet
        File.WriteAllText(path, snap.ToJsonString(JsonCompact), Utf8);
        foreach (var old in Directory.GetFiles(BackupsDir, "personal-*.json").OrderBy(f => f, StringComparer.Ordinal).SkipLast(KeepBackups))
            File.Delete(old);
        return path;
    }

    /// <summary>Two backups hold the same data (the time they were made aside).</summary>
    static bool SameBackup(JsonObject a, JsonObject b)
    {
        static string Body(JsonObject o) =>
            new JsonObject(o.Where(kv => kv.Key != "created").Select(kv => KeyValuePair.Create(kv.Key, kv.Value?.DeepClone()))).ToJsonString(JsonCompact);
        return Body(a) == Body(b);
    }

    /// <summary>The automatic backups, newest first.</summary>
    public List<BackupFile> ListBackups() =>
        Directory.GetFiles(BackupsDir, "personal-*.json").OrderByDescending(f => f, StringComparer.Ordinal)
            .Select(p => new BackupFile(Path.GetFileName(p), new FileInfo(p).Length)).ToList();

    /// <summary>Reads one automatic backup by file name (no paths: only files in the backups folder).</summary>
    public JsonNode? LoadBackup(string name)
    {
        if (Path.GetFileName(name) != name || !name.StartsWith("personal-", StringComparison.Ordinal) || !name.EndsWith(".json", StringComparison.Ordinal))
            throw new ArgumentException("Unknown backup.");
        return JsonNode.Parse(File.ReadAllText(Path.Combine(BackupsDir, name)));
    }

    // ---------- saved views ----------
    /// <summary>Saved views by name (case-insensitive). Quick.</summary>
    public List<SavedView> ListViews()
    {
        var list = new List<SavedView>();
        using var rd = Reader();
        using var c = Cmd(rd, "SELECT id, name, state FROM views ORDER BY name COLLATE NOCASE", Array.Empty<object?>());
        using var r = c.ExecuteReader();
        while (r.Read())
        {
            JsonNode? state = null;
            try { string s = r.IsDBNull(2) ? "" : r.GetString(2); state = JsonNode.Parse(s.Length > 0 ? s : "{}"); }
            catch (JsonException) { }
            list.Add(new SavedView { Id = r.GetInt32(0), Name = r.IsDBNull(1) ? "" : r.GetString(1), State = state ?? new JsonObject() });
        }
        return list;
    }

    /// <summary>The first <paramref name="n"/> characters (code points, like Python's slice).</summary>
    static string Head(string s, int n)
    {
        int i = 0;
        for (int cps = 0; i < s.Length && cps < n; cps++) i += char.IsHighSurrogate(s[i]) && i + 1 < s.Length ? 2 : 1;
        return s[..i];
    }

    /// <summary>Saves (or replaces, by name) a view. The name is trimmed to 60 characters.</summary>
    public void SaveView(string? name, JsonNode? state)
    {
        name = Head((name ?? "").Trim(), 60);
        if (name.Length == 0) throw new ArgumentException("Give the view a name.");
        lock (Lock) Exec("INSERT OR REPLACE INTO views(name, state) VALUES (?,?)", name, (state ?? new JsonObject()).ToJsonString(JsonCompact));
    }

    public void DeleteView(int id)
    {
        lock (Lock) Exec("DELETE FROM views WHERE id=?", id);
    }

    // ---------- wishlist ----------
    /// <summary>All wishlist entries, 'wanted' first then 'found', newest first. Quick.</summary>
    public List<WishItem> WishlistItems()
    {
        var list = new List<WishItem>();
        using var rd = Reader();
        using var c = Cmd(rd, "SELECT id, title, year, tconst, note, source, status, added FROM wishlist ORDER BY status DESC, id DESC", Array.Empty<object?>());
        using var r = c.ExecuteReader();
        while (r.Read())
            list.Add(new WishItem
            {
                Id = r.GetInt32(0), Title = r.IsDBNull(1) ? "" : r.GetString(1), Year = r.IsDBNull(2) ? null : r.GetInt32(2),
                Tconst = r.IsDBNull(3) ? null : r.GetInt32(3), Note = r.IsDBNull(4) ? null : r.GetString(4),
                Source = r.IsDBNull(5) ? null : r.GetString(5), Status = r.IsDBNull(6) ? WishStatus.Wanted : r.GetString(6),
                Added = r.IsDBNull(7) ? null : r.GetString(7),
            });
        return list;
    }

    /// <summary>Adds entries; returns how many were new. A title+year already there is skipped (as in Python, entries without a year can repeat).</summary>
    public int WishAdd(IEnumerable<WishNew> items)
    {
        int added = 0;
        lock (Lock)
        {
            using var tx = db.BeginTransaction();
            foreach (var it in items)
            {
                string title = (it.Title ?? "").Trim();
                if (title.Length == 0) continue;
                added += Exec("INSERT OR IGNORE INTO wishlist(title, year, tconst, note, source, added) VALUES (?,?,?,?,?,?)",
                    title, it.Year, it.Tconst, it.Note, it.Source, Stamp());
            }
            tx.Commit();
        }
        return added;
    }

    /// <summary>Marks an entry <see cref="WishStatus.Wanted"/> or <see cref="WishStatus.Found"/> (anything else is ignored), or removes it.</summary>
    public void WishSet(int id, string? status = null, bool delete = false)
    {
        lock (Lock)
        {
            if (delete) Exec("DELETE FROM wishlist WHERE id=?", id);
            else if (status is WishStatus.Wanted or WishStatus.Found) Exec("UPDATE wishlist SET status=? WHERE id=?", status, id);
        }
    }
}
