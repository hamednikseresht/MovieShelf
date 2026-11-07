using Microsoft.Data.Sqlite;

namespace MovieShelf.Core;

/// <summary>A title of the offline IMDb index.</summary>
public sealed class IxTitle
{
    public int Id, Type, Votes, Src, KLen;
    public string Title = "", Orig = "", Genres = "", Hit = "";
    public int? Year, EndYear, Runtime;
    public double? Rating;

    public string TypeName => Imdb.TypeNames.GetValueOrDefault(Type, "");
}

public static class Imdb
{
    public static readonly Dictionary<int, string> TypeNames = new()
    {
        [0] = "movie", [1] = "tvMovie", [2] = "video", [3] = "tvSpecial", [4] = "short", [5] = "tvShort", [6] = "tvMiniSeries", [7] = "tvSeries",
    };
}

/// <summary>What the matcher needs of a file name.</summary>
public sealed class MatchInput
{
    public string? Title, AltTitle, Conf;
    public int? Year;

    public static MatchInput From(ParsedName p) => new() { Title = p.Title, AltTitle = p.AltTitle, Year = p.Year };
}

public sealed record Candidate(double Score, IxTitle Row, string How);

public sealed class Candidate2
{
    public int Tconst { get; init; }
    public string Title { get; init; } = "";
    public string? Orig { get; init; }
    public int? Year { get; init; }
    public string? Type { get; init; }
    public int Votes { get; init; }
    public double? Rating { get; init; }
    public string How { get; init; } = "";
    public string? Stars { get; init; }   // online results only: main actors, and the poster image address
    public string? Image { get; init; }
}

/// <summary>Matches parsed names to IMDb titles with the offline index: exact title keys, spelling within the year,
/// English and original names glued together. Same rules as the Python matcher.py.</summary>
public sealed class Matcher
{
    public static readonly string[] Unsure = { "review", "fuzzy", "online", "noyear", "none" };

    readonly string indexPath;
    readonly Dictionary<int, (List<string> Keys, List<int> Ids)> fuzzyCache = new();

    public Matcher(string indexPath) => this.indexPath = indexPath;

    public bool HasIndex => File.Exists(indexPath);

    /// <summary>Forget cached index data and release pooled connections (after the index is rebuilt).</summary>
    public void Reset()
    {
        fuzzyCache.Clear();
        SqliteConnection.ClearAllPools();
    }

    public SqliteConnection Open()
    {
        var c = new SqliteConnection($"Data Source={indexPath};Mode=ReadOnly");
        c.Open();
        return c;
    }

    public static string Reason(string? conf, int? fyear, int? tyear) => conf switch
    {
        "none" => "Not found on IMDb — the name may be misspelled, or the year in the name is wrong",
        "fuzzy" => "Name is spelled differently on IMDb",
        "noyear" => "No year in the name — several movies may share this title",
        "online" => "Only found by online search",
        "review" => fyear != null && tyear != null && fyear != tyear
            ? $"Year in the name ({fyear}) differs from IMDb ({tyear})"
            : "Several titles share this name and year",
        _ => "",
    };

    static IxTitle Read(SqliteDataReader r)
    {
        int O(string n) => r.GetOrdinal(n);
        bool Has(string n) { for (int i = 0; i < r.FieldCount; i++) if (r.GetName(i) == n) return true; return false; }
        int? I(string n) => r.IsDBNull(O(n)) ? null : r.GetInt32(O(n));
        string S(string n) => r.IsDBNull(O(n)) ? "" : r.GetString(O(n));
        var t = new IxTitle
        {
            Id = r.GetInt32(O("id")), Type = r.GetInt32(O("type")), Title = S("title"), Orig = S("orig"),
            Year = I("year"), EndYear = I("endyear"), Runtime = I("runtime"), Genres = S("genres"),
            Rating = r.IsDBNull(O("rating")) ? null : r.GetDouble(O("rating")), Votes = I("votes") ?? 0,
        };
        if (Has("src")) t.Src = I("src") ?? 0;
        if (Has("hit")) t.Hit = S("hit");
        if (Has("klen")) t.KLen = I("klen") ?? 0;
        return t;
    }

    static double Score(IxTitle row, int? year, int src)
    {
        double s = 0;
        if (year != null && row.Year != null)
        {
            int d = Math.Abs(year.Value - row.Year.Value);
            s += d switch { 0 => 60, 1 => 35, 2 => 5, _ => -200 };
        }
        else if (year != null && row.Year == null) s -= 20;
        s += row.Type switch { 0 => 25, 1 => 15, 2 => 8, 3 => 8, 6 => 2, _ => 0 };
        if (row.Type == 4 && year != null && year < 1915) s += 25; // early cinema is mostly shorts
        s += src == 0 ? 12 : 0;
        s += Math.Log10(row.Votes + 1) * 6;
        return s;
    }

    static string In(int n) => string.Join(',', Enumerable.Repeat("?", n));

    static SqliteCommand Cmd(SqliteConnection ix, string sql, IEnumerable<object> args)
    {
        var c = ix.CreateCommand();
        c.CommandText = sql;
        int i = 0;
        foreach (var a in args) c.Parameters.AddWithValue("$p" + i++, a);
        // positional '?' to named
        int n = 0;
        var sb = new System.Text.StringBuilder();
        foreach (char ch in sql) sb.Append(ch == '?' ? "$p" + n++ : ch.ToString());
        c.CommandText = sb.ToString();
        return c;
    }

    public List<Candidate> Candidates(SqliteConnection ix, MatchInput p, int limit = 8)
    {
        var keys = new List<string>();
        foreach (var t in new[] { p.Title, p.AltTitle })
            if (!string.IsNullOrEmpty(t))
                foreach (var k in TitleKeys.QueryKeys(t))
                    if (!keys.Contains(k)) keys.Add(k);
        if (keys.Count == 0) return new();
        var full = new HashSet<string>();
        foreach (var t in new[] { p.Title, p.AltTitle })
        {
            if (string.IsNullOrEmpty(t)) continue;
            var qk = TitleKeys.QueryKeys(t);
            if (qk.Count > 0) full.Add(qk[0]);
        }
        var rows = new List<IxTitle>();
        using (var c = Cmd(ix, $"SELECT t.*, min(k.src) AS src, group_concat(k.k) AS hit FROM keys k JOIN titles t ON t.id = k.id WHERE k.k IN ({In(keys.Count)}) GROUP BY t.id", keys.Cast<object>()))
        using (var r = c.ExecuteReader())
            while (r.Read()) rows.Add(Read(r));
        // the exact full title beats a variant (article dropped, edition words removed)
        var outList = rows.Select(r => new Candidate(Score(r, p.Year, r.Src) + (full.Overlaps(r.Hit.Split(',')) ? 15 : 0), r, "key")).ToList();
        return outList.OrderByDescending(c => c.Score).Take(limit).ToList();
    }

    /// <summary>Names like 'Great.Freedom.Grosse.Freiheit.2021' glue the English and original title together:
    /// try word prefixes / suffixes / ':' parts with an exact year.</summary>
    public List<Candidate> SplitCandidates(SqliteConnection ix, MatchInput p)
    {
        if (p.Year == null || string.IsNullOrEmpty(p.Title)) return new();
        var parts = p.Title.Split(':').Where(s => s.Trim().Length > 0).ToList();
        var words = TitleKeys.Norm(p.Title).Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var keys = new HashSet<string>();
        if (parts.Count > 1) foreach (var s in parts) keys.Add(TitleKeys.Key(s));
        for (int i = 1; i < words.Length; i++)
        {
            string pre = string.Concat(words.Take(i)), suf = string.Concat(words.Skip(i));
            if (pre.Length >= 5) keys.Add(pre);
            if (suf.Length >= 5) keys.Add(suf);
        }
        keys.Remove("");
        if (keys.Count == 0) return new();
        var args = keys.Cast<object>().Append(p.Year.Value).ToList();
        var rows = new List<IxTitle>();
        // '+t.year' makes SQLite look up by key, not scan the whole year
        using (var c = Cmd(ix, $"SELECT t.*, min(k.src) AS src, max(length(k.k)) AS klen FROM keys k JOIN titles t ON t.id = k.id WHERE k.k IN ({In(keys.Count)}) AND +t.year = ? AND t.type <= 6 GROUP BY t.id", args))
        using (var r = c.ExecuteReader())
            while (r.Read()) rows.Add(Read(r));
        int total = string.Concat(words).Length;
        var outList = rows.Select(r => new Candidate(Score(r, p.Year, r.Src) - 30 * (1 - (double)r.KLen / total), r, "split")).ToList();
        return outList.OrderByDescending(c => c.Score).Take(5).ToList();
    }

    (List<string> Keys, List<int> Ids) FuzzyBucket(SqliteConnection ix, int year)
    {
        if (!fuzzyCache.TryGetValue(year, out var b))
        {
            b = (new List<string>(), new List<int>());
            using var c = Cmd(ix, "SELECT k.k, k.id FROM titles t JOIN keys k ON k.id = t.id WHERE t.year = ? AND t.type <= 6", new object[] { year });
            using var r = c.ExecuteReader();
            while (r.Read()) { b.Keys.Add(r.GetString(0)); b.Ids.Add(r.GetInt32(1)); }
            fuzzyCache[year] = b;
            if (fuzzyCache.Count > 400) fuzzyCache.Remove(fuzzyCache.Keys.First());
        }
        return b;
    }

    IxTitle? Get(SqliteConnection ix, int id)
    {
        using var c = Cmd(ix, "SELECT *, 1 AS src FROM titles WHERE id=?", new object[] { id });
        using var r = c.ExecuteReader();
        return r.Read() ? Read(r) : null;
    }

    public List<Candidate> Fuzzy(SqliteConnection ix, MatchInput p, int limit = 5, double cutoff = 84)
    {
        if (p.Year == null) return new();
        var qkeys = new List<string>();
        foreach (var t in new[] { p.Title, p.AltTitle })
        {
            if (string.IsNullOrEmpty(t)) continue;
            var k = TitleKeys.QueryKeys(t);
            if (k.Count > 0) qkeys.Add(k[0]);
        }
        var found = new Dictionary<int, double>();
        foreach (int y in new[] { p.Year.Value, p.Year.Value - 1, p.Year.Value + 1 })
        {
            var (keys, ids) = FuzzyBucket(ix, y);
            if (keys.Count == 0) continue;
            foreach (var qk in qkeys)
            {
                if (qk.Length < 4) continue;
                foreach (var (idx, sc) in Fuzz.Extract(qk, keys, limit, cutoff))
                    found[ids[idx]] = Math.Max(found.GetValueOrDefault(ids[idx]), sc);
            }
        }
        var outList = new List<Candidate>();
        foreach (var (id, sc) in found)
        {
            var r = Get(ix, id);
            if (r != null) outList.Add(new Candidate(Score(r, p.Year, 1) + (sc - 100) * 2, r, $"fuzzy {sc:0}"));
        }
        return outList.OrderByDescending(c => c.Score).Take(limit).ToList();
    }

    /// <summary>The best IMDb id for a name and how sure the match is: exact, good, noyear, review, fuzzy or none.</summary>
    public (int? Tconst, string Conf, double? Score) Pick(SqliteConnection ix, MatchInput p)
    {
        var c = Candidates(ix, p);
        if (c.Count > 0 && (p.Year == null || c[0].Score > 0))
        {
            var best = c[0];
            var r = best.Row;
            string conf;
            if (p.Year == null) conf = "noyear";
            else if (r.Year == p.Year && r.Src == 0) conf = "exact";
            else conf = "good";
            // an ambiguous tie between two strong candidates needs review
            if (c.Count > 1 && c[1].Score > best.Score - 3 && c[1].Row.Year == r.Year) conf = "review";
            return (r.Id, conf, best.Score);
        }
        var f = Fuzzy(ix, p);
        if (f.Count > 0 && f[0].Score > 0) return (f[0].Row.Id, "fuzzy", f[0].Score);
        var s = SplitCandidates(ix, p);
        if (s.Count > 0 && s[0].Score > 40) return (s[0].Row.Id, "fuzzy", s[0].Score);
        // exact title but IMDb's year differs a lot (IMDb dates some films by production year)
        if (p.Year != null)
        {
            var near = c.Where(x => x.Row.Year != null && Math.Abs(x.Row.Year.Value - p.Year.Value) <= 6 && x.Row.Votes >= 1000)
                .OrderByDescending(x => x.Row.Votes).ToList();
            if (near.Count > 0) return (near[0].Row.Id, "review", near[0].Score);
        }
        return (null, "none", null);
    }

    public static List<Candidate2> ToCandidates(IEnumerable<Candidate> rows, IEnumerable<int>? skip = null)
    {
        var seen = new HashSet<int>(skip ?? Array.Empty<int>());
        var outList = new List<Candidate2>();
        foreach (var c in rows)
        {
            if (!seen.Add(c.Row.Id)) continue;
            outList.Add(new Candidate2
            {
                Tconst = c.Row.Id, Title = c.Row.Title, Orig = c.Row.Orig != c.Row.Title ? c.Row.Orig : null,
                Year = c.Row.Year, Type = c.Row.TypeName, Votes = c.Row.Votes, Rating = c.Row.Rating, How = c.How,
            });
        }
        return outList;
    }

    /// <summary>Other titles this name could mean, for the Fix match window.</summary>
    public List<Candidate2> AllCandidates(SqliteConnection ix, MatchInput p, IEnumerable<int>? skip = null, int n = 4, bool fuzzy = true)
    {
        var rows = Candidates(ix, p).Concat(SplitCandidates(ix, p)).ToList();
        if (fuzzy && p.Conf is null or "none" or "fuzzy") rows.AddRange(Fuzzy(ix, p, cutoff: 78)); // fuzzy search is the slow part: only where spelling is the issue
        rows = rows.OrderByDescending(c => c.Score).ToList();
        return ToCandidates(rows, skip).Take(n).ToList();
    }
}
