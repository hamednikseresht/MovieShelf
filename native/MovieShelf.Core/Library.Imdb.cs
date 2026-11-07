using System.Text;
using Microsoft.Data.Sqlite;

namespace MovieShelf.Core;

/// <summary>Python json.dumps(..., ensure_ascii=False) for the small row lists stored in titles (directors, writers, cast_, crew).</summary>
static class PyJson
{
    public static string Rows(IEnumerable<object?[]> rows)
    {
        var sb = new StringBuilder("[");
        bool first = true;
        foreach (var row in rows)
        {
            if (!first) sb.Append(", ");
            first = false;
            sb.Append('[');
            for (int i = 0; i < row.Length; i++)
            {
                if (i > 0) sb.Append(", ");
                switch (row[i])
                {
                    case null: sb.Append("null"); break;
                    case string s: Str(sb, s); break;
                    default: sb.Append(Convert.ToString(row[i], System.Globalization.CultureInfo.InvariantCulture)); break;
                }
            }
            sb.Append(']');
        }
        return sb.Append(']').ToString();
    }

    static void Str(StringBuilder sb, string s)
    {
        sb.Append('"');
        foreach (char c in s)
            switch (c)
            {
                case '"': sb.Append("\\\""); break;
                case '\\': sb.Append("\\\\"); break;
                case '\n': sb.Append("\\n"); break;
                case '\r': sb.Append("\\r"); break;
                case '\t': sb.Append("\\t"); break;
                case '\b': sb.Append("\\b"); break;
                case '\f': sb.Append("\\f"); break;
                case < ' ': sb.Append("\\u").Append(((int)c).ToString("x4")); break;
                default: sb.Append(c); break;
            }
        sb.Append('"');
    }
}

public sealed partial class Library
{
    /// <summary>HTTP client for the IMDb dataset downloads; null uses a default one. Tests put a fake handler behind it.</summary>
    public HttpClient? ImdbHttp { get; set; }

    CancellationToken StopToken => cts?.Token ?? default;

    // ---------- the IMDb index part of the update ----------
    /// <summary>Download the datasets if missing (or forced), build the index if missing (or forced), refresh old ratings. Part of the update job; slow.</summary>
    public void EnsureIndex(bool forceDownload = false)
    {
        if (forceDownload || !ImdbIndex.HaveFiles(ImdbDir))
        {
            ImdbIndex.Download(ImdbDir, Progress, forceDownload, null, ImdbHttp, StopToken);
            Log("IMDb datasets downloaded");
        }
        if (forceDownload || !File.Exists(IndexPath))
        {
            Log("Building IMDb index (one-time, a few minutes)");
            ImdbIndex.BuildIndex(ImdbDir, IndexPath, Progress);
            Matcher.Reset();
            Log("IMDb index built");
            if (forceDownload) lock (Lock) Exec("UPDATE titles SET enriched=0");
        }
        else if (LoadSettings().AutoRefresh && (ImdbIndex.FileAgeDays(ImdbDir, "title.ratings") ?? 0) > 7)
        {
            try { RefreshRatings(); }
            catch (Exception e) when (e is not OperationCanceledException) { Log($"Ratings refresh skipped: {e.Message}"); } // offline: keep going with the ratings we have
        }
    }

    /// <summary>Downloads title.ratings (about 8 MB) and updates the ratings of the library titles and of the matching index.</summary>
    public void RefreshRatings()
    {
        Log("Downloading latest IMDb ratings");
        ImdbIndex.Download(ImdbDir, Progress, true, new[] { "title.ratings" }, ImdbHttp, StopToken);
        Progress("Updating ratings", 0, 1);
        var ratings = ImdbIndex.ReadRatings(ImdbDir);
        int changed = 0;
        lock (Lock)
        {
            var upd = new List<(double R, int V, int T)>();
            using (var c = Cmd("SELECT tconst, rating, votes FROM titles"))
            using (var r = c.ExecuteReader())
                while (r.Read())
                {
                    int t = r.GetInt32(0);
                    if (!ratings.TryGetValue(t, out var nw)) continue;
                    if (r.IsDBNull(1) || r.IsDBNull(2) || r.GetDouble(1) != nw.Rating || r.GetInt64(2) != nw.Votes) upd.Add((nw.Rating, nw.Votes, t));
                }
            using var tx = db.BeginTransaction();
            using (var c = Cmd("UPDATE titles SET rating=?, votes=?, updated=? WHERE tconst=?", 0.0, 0, Now(), 0))
                foreach (var (rt, v, t) in upd)
                {
                    c.Parameters["$p0"].Value = rt; c.Parameters["$p1"].Value = v; c.Parameters["$p3"].Value = t;
                    c.ExecuteNonQuery();
                }
            tx.Commit();
            changed = upd.Count;
        }
        if (File.Exists(IndexPath)) // keep the matching index in step
        {
            using var ix = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = IndexPath, Pooling = false }.ToString());
            ix.Open();
            using (var p = Cmd(ix, "PRAGMA synchronous=OFF", Array.Empty<object?>())) p.ExecuteNonQuery();
            using var tx = ix.BeginTransaction();
            using var c = Cmd(ix, "UPDATE titles SET rating=?, votes=? WHERE id=?", new object?[] { 0.0, 0, 0 });
            long n = 0;
            foreach (var (id, (rt, v)) in ratings)
            {
                if (++n % 200_000 == 0) Progress("Updating ratings", n, ratings.Count);
                c.Parameters["$p0"].Value = rt; c.Parameters["$p1"].Value = v; c.Parameters["$p2"].Value = id;
                c.ExecuteNonQuery();
            }
            tx.Commit();
        }
        Version++;
        Log($"IMDb ratings updated: {changed} of your titles changed");
    }

    // ---------- cast and crew ----------
    /// <summary>Copies directors, writers, cast, crew and people names from the IMDb datasets into the library (all titles not yet enriched when ids is null). Slow: reads the big files.</summary>
    public void Enrich(IEnumerable<int>? ids = null)
    {
        List<int> list;
        if (ids == null)
        {
            list = new();
            lock (Lock)
            using (var c = Cmd("SELECT tconst FROM titles WHERE enriched=0 AND type IS NOT NULL"))
            using (var r = c.ExecuteReader())
                while (r.Read()) list.Add(r.GetInt32(0));
        }
        else list = ids.ToList();
        if (list.Count == 0) return;
        Log($"Reading cast & crew for {list.Count} titles");
        var (credits, names, basics) = ImdbIndex.ReadPeople(ImdbDir, list, Progress);
        string Nm(int n) => names.TryGetValue(n, out var s) ? s : $"nm{n:0000000}";
        lock (Lock)
        {
            using var tx = db.BeginTransaction();
            using (var c = Cmd("INSERT OR REPLACE INTO people(nconst, name, birth, death, prof) VALUES (?,?,?,?,?)", 0, "", null, null, null))
                foreach (var (n, nm) in names)
                {
                    var b = basics.GetValueOrDefault(n, new PersonBasics(null, null, ""));
                    c.Parameters["$p0"].Value = n; c.Parameters["$p1"].Value = nm;
                    c.Parameters["$p2"].Value = (object?)b.Birth ?? DBNull.Value; c.Parameters["$p3"].Value = (object?)b.Death ?? DBNull.Value; c.Parameters["$p4"].Value = b.Prof;
                    c.ExecuteNonQuery();
                }
            foreach (var (i, cr) in credits)
                Exec("UPDATE titles SET directors=?, writers=?, cast_=?, crew=?, enriched=1 WHERE tconst=?",
                    PyJson.Rows(cr.Directors.Select(n => new object?[] { n, Nm(n) })),
                    PyJson.Rows(cr.Writers.Select(n => new object?[] { n, Nm(n) })),
                    PyJson.Rows(cr.Cast.Select(x => new object?[] { x.Nc, Nm(x.Nc), x.Char })),
                    PyJson.Rows(cr.Crew.Select(x => new object?[] { x.Nc, Nm(x.Nc), x.Job })), i);
            tx.Commit();
        }
        Log("Cast & crew saved");
    }

    /// <summary>Birth and death year and professions for people stored before they were kept (one pass over name.basics).</summary>
    public void FillPeopleBasics()
    {
        if (!File.Exists(Path.Combine(ImdbDir, "name.basics.tsv.gz"))) return;
        var ids = new List<int>();
        lock (Lock)
        using (var c = Cmd("SELECT nconst FROM people WHERE prof IS NULL"))
        using (var r = c.ExecuteReader())
            while (r.Read()) ids.Add(r.GetInt32(0));
        if (ids.Count == 0) return;
        Log($"Reading birth years and professions for {ids.Count} people");
        var basics = ImdbIndex.ReadBasics(ImdbDir, ids, Progress);
        lock (Lock)
        {
            using var tx = db.BeginTransaction();
            using (var c = Cmd("UPDATE people SET birth=?, death=?, prof=? WHERE nconst=?", null, null, "", 0))
                foreach (var n in ids)
                {
                    var b = basics.GetValueOrDefault(n, new PersonBasics(null, null, ""));
                    c.Parameters["$p0"].Value = (object?)b.Birth ?? DBNull.Value; c.Parameters["$p1"].Value = (object?)b.Death ?? DBNull.Value;
                    c.Parameters["$p2"].Value = b.Prof; c.Parameters["$p3"].Value = n;
                    c.ExecuteNonQuery();
                }
            tx.Commit();
        }
    }

    // ---------- Persian titles ----------
    /// <summary>Your own Persian title for a movie. null removes your edit (the TMDB title shows again); "" hides the Persian title.</summary>
    public void SetFaTitle(int tconst, string? title)
    {
        lock (Lock)
        {
            if (title == null) Exec("DELETE FROM fa_titles WHERE tconst=?", tconst);
            else Exec("INSERT OR REPLACE INTO fa_titles(tconst, title, source) VALUES (?,?,'user')", tconst, FaTitles.Normalize(title) ?? "");
        }
        Version++;
        DataChanged?.Invoke();
    }

    /// <summary>On every update: respells every stored Persian title and adds the reviewed replacements for titles that have none yet. Titles you edited yourself are never touched.</summary>
    public (int Cleaned, int Replaced) ApplyFaTitles()
    {
        int cleaned = 0, replaced = 0;
        lock (Lock)
        {
            var rows = new List<(int T, string Fa)>();
            using (var c = Cmd("SELECT tconst, fa_title FROM titles WHERE fa_title IS NOT NULL AND fa_title != ''"))
            using (var r = c.ExecuteReader())
                while (r.Read()) rows.Add((r.GetInt32(0), r.GetString(1)));
            var have = new HashSet<int>();
            using (var c = Cmd("SELECT tconst FROM titles"))
            using (var r = c.ExecuteReader())
                while (r.Read()) have.Add(r.GetInt32(0));
            using var tx = db.BeginTransaction();
            foreach (var (t, fa) in rows)
            {
                var nw = FaTitles.Normalize(fa);
                if (nw != fa) { Exec("UPDATE titles SET fa_title=? WHERE tconst=?", nw, t); cleaned++; }
            }
            foreach (var (t, title) in FaTitles.Reviewed())
                if (have.Contains(t))
                    replaced += Exec("INSERT OR IGNORE INTO fa_titles(tconst, title, source) VALUES (?,?,'review')", t, FaTitles.Normalize(title));
            tx.Commit();
        }
        DropOldReviewKey();
        Version++;
        Log($"Persian titles: {cleaned} respelled, {replaced} replaced by the reviewed list");
        return (cleaned, replaced);
    }

    /// <summary>Earlier builds of this app saved fa_review_version; with that key in the shared settings the Python app stops applying the review too.</summary>
    void DropOldReviewKey()
    {
        if (!File.Exists(SettingsPath)) return;
        var o = ReadSettingsJson();
        if (o.Remove(FaTitles.OldVersionKey)) WriteSettingsJson(o);
    }
}
