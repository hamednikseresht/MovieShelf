using System.Globalization;
using System.IO.Compression;
using System.Text;
using Microsoft.Data.Sqlite;

namespace MovieShelf.Core;

/// <summary>Cast and crew ids of one title, from title.crew and title.principals.</summary>
public sealed class TitleCredits
{
    public List<int> Directors = new(), Writers = new();
    public List<(int Nc, string? Char)> Cast = new();
    public List<(int Nc, string Job)> Crew = new();
}

public readonly record struct PersonBasics(int? Birth, int? Death, string Prof);

public sealed record PeopleData(Dictionary<int, TitleCredits> Credits, Dictionary<int, string> Names, Dictionary<int, PersonBasics> Basics);

/// <summary>Forward-only reader of a gzip'd IMDb .tsv: fields are spans into one reused buffer, no allocation per line.</summary>
sealed class TsvReader : IDisposable
{
    readonly StreamReader sr;
    char[] buf = new char[1 << 17];
    int pos, end, count;
    int[] st = new int[12], en = new int[12];
    bool eof;

    TsvReader(StreamReader r) => sr = r;

    public static TsvReader Open(string path)
    {
        var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 16, FileOptions.SequentialScan);
        try
        {
            var t = new TsvReader(new StreamReader(new GZipStream(fs, CompressionMode.Decompress), new UTF8Encoding(false), false, 1 << 16));
            t.Next(); // header
            return t;
        }
        catch { fs.Dispose(); throw; }
    }

    public void Dispose() => sr.Dispose();

    public int Count => count;

    /// <summary>Field i of the current line; empty when the line is shorter. Valid until the next call to Next.</summary>
    public ReadOnlySpan<char> this[int i] => i < count ? buf.AsSpan(st[i], en[i] - st[i]) : default;

    public bool Next()
    {
        while (true)
        {
            int nl = buf.AsSpan(pos, end - pos).IndexOf('\n');
            int stop;
            if (nl >= 0) stop = pos + nl;
            else if (!eof) { Fill(); continue; }
            else if (pos < end) stop = end;
            else return false;
            int ls = pos;
            pos = nl >= 0 ? stop + 1 : end;
            if (stop > ls && buf[stop - 1] == '\r') stop--;
            if (stop == ls) continue;
            Split(ls, stop);
            return true;
        }
    }

    void Fill()
    {
        if (pos > 0) { Array.Copy(buf, pos, buf, 0, end - pos); end -= pos; pos = 0; }
        if (end == buf.Length) Array.Resize(ref buf, buf.Length * 2);
        int n = sr.Read(buf, end, buf.Length - end);
        if (n == 0) eof = true; else end += n;
    }

    void Split(int a, int b)
    {
        count = 0;
        int s = a;
        while (true)
        {
            int t = buf.AsSpan(s, b - s).IndexOf('\t');
            if (count == st.Length) { Array.Resize(ref st, count * 2); Array.Resize(ref en, count * 2); }
            st[count] = s;
            if (t < 0) { en[count++] = b; break; }
            en[count++] = s + t;
            s += t + 1;
        }
    }
}

/// <summary>The official IMDb datasets: download, a compact movie-only SQLite index (same tables as the Python imdb_index.py), cast and crew reading.</summary>
public static class ImdbIndex
{
    public const string BaseUrl = "https://datasets.imdbws.com/";
    public const string UserAgent = "MovieShelf/1.0 (personal collection manager)";
    public static readonly string[] Files = { "title.ratings", "title.basics", "title.akas", "title.crew", "title.principals", "name.basics" };

    // titleType -> small int (see TypeOf). Everything else (episodes, games) is skipped.
    public static readonly Dictionary<string, int> Types = new()
    {
        ["movie"] = 0, ["tvMovie"] = 1, ["video"] = 2, ["tvSpecial"] = 3, ["short"] = 4, ["tvShort"] = 5, ["tvMiniSeries"] = 6, ["tvSeries"] = 7,
    };
    public static readonly HashSet<string> AkaRegions = new() { "XWW", "US", "GB", "IR", "CA", "AU" };
    public static readonly HashSet<string> AkaLangs = new() { "en", "fa" };

    static HttpClient? defaultHttp;

    static string P(string dir, string name) => Path.Combine(dir, name + ".tsv.gz");

    static bool IsNull(ReadOnlySpan<char> s) => s.Length == 2 && s[0] == '\\' && s[1] == 'N';

    /// <summary>tt0111161 -> 111161</summary>
    static int Tid(ReadOnlySpan<char> s) => int.Parse(s.Slice(2), NumberStyles.None, CultureInfo.InvariantCulture);

    static int TypeOf(ReadOnlySpan<char> s) => s switch
    {
        "movie" => 0, "tvMovie" => 1, "video" => 2, "tvSpecial" => 3, "short" => 4, "tvShort" => 5, "tvMiniSeries" => 6, "tvSeries" => 7, _ => -1,
    };

    // ---------- download ----------
    /// <summary>Downloads the dataset files that are missing (all of them with <paramref name="force"/>). A stopped or failed download leaves no half file.</summary>
    public static void Download(string dataDir, Action<string, long, long> progress, bool force = false, IEnumerable<string>? names = null,
        HttpClient? http = null, CancellationToken ct = default)
    {
        Directory.CreateDirectory(dataDir);
        var list = (names ?? Files).ToList();
        http ??= defaultHttp ??= new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
        for (int i = 0; i < list.Count; i++)
        {
            string name = list[i], path = P(dataDir, name);
            if (File.Exists(path) && !force) continue;
            progress($"Downloading {name}", i, list.Count);
            string tmp = path + ".part";
            try
            {
                using var req = new HttpRequestMessage(HttpMethod.Get, BaseUrl + name + ".tsv.gz");
                req.Headers.TryAddWithoutValidation("User-Agent", UserAgent);
                using var resp = http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct).GetAwaiter().GetResult();
                resp.EnsureSuccessStatusCode();
                long total = resp.Content.Headers.ContentLength ?? 0, got = 0;
                using (var src = resp.Content.ReadAsStreamAsync(ct).GetAwaiter().GetResult())
                using (var f = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 16))
                {
                    var chunk = new byte[1 << 20];
                    while (true)
                    {
                        int n = ReadChunk(src, chunk, ct);
                        if (n == 0) break;
                        f.Write(chunk, 0, n);
                        got += n;
                        progress(total > 0 ? $"Downloading {name} ({got >> 20}/{total >> 20} MB)" : $"Downloading {name} ({got >> 20} MB)", got, total);
                    }
                }
            }
            catch // stopped by the user, or the connection failed: no half file is left behind
            {
                try { File.Delete(tmp); } catch (IOException) { }
                throw;
            }
            File.Move(tmp, path, true);
        }
    }

    /// <summary>Fills the buffer (or reaches the end); gives up with a TimeoutException when the server sends nothing for 60 s.</summary>
    static int ReadChunk(Stream s, byte[] buf, CancellationToken ct)
    {
        using var to = CancellationTokenSource.CreateLinkedTokenSource(ct);
        to.CancelAfter(TimeSpan.FromSeconds(60));
        try { return s.ReadAtLeastAsync(buf, buf.Length, false, to.Token).AsTask().GetAwaiter().GetResult(); }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { throw new TimeoutException("The download stalled (no data for 60 seconds)."); }
    }

    public static double? FileAgeDays(string dataDir, string name)
    {
        string path = P(dataDir, name);
        return File.Exists(path) ? (DateTime.UtcNow - File.GetLastWriteTimeUtc(path)).TotalDays : null;
    }

    public static bool HaveFiles(string dataDir) => Files.All(n => File.Exists(P(dataDir, n)));

    public static Dictionary<int, (double Rating, int Votes)> ReadRatings(string dataDir)
    {
        var d = new Dictionary<int, (double, int)>(1_600_000);
        using var r = TsvReader.Open(P(dataDir, "title.ratings"));
        while (r.Next())
            d[Tid(r[0])] = (double.Parse(r[1], NumberStyles.Float, CultureInfo.InvariantCulture), int.Parse(r[2], NumberStyles.Integer, CultureInfo.InvariantCulture));
        return d;
    }

    // ---------- building the index ----------
    sealed class Bits
    {
        ulong[] w = new ulong[1 << 20];

        public void Set(int i)
        {
            int k = i >> 6;
            if (k >= w.Length) Array.Resize(ref w, Math.Max(k + 1, w.Length * 2));
            w[k] |= 1UL << (i & 63);
        }

        public bool Get(int i) { int k = i >> 6; return k < w.Length && (w[k] >> (i & 63) & 1) != 0; }
    }

    /// <summary>Same as TitleKeys.Key, but plain ASCII titles (nearly all of them) skip the regex and Unicode work and allocate nothing.</summary>
    sealed class KeyMaker
    {
        char[] buf = new char[256];

        public ReadOnlySpan<char> Make(ReadOnlySpan<char> s)
        {
            if (buf.Length < s.Length * 3) buf = new char[s.Length * 3 + 64];
            int n = 0;
            foreach (char c in s)
            {
                if (c >= 128) return TitleKeys.Key(new string(s)).AsSpan();
                if (c is >= 'a' and <= 'z' or >= '0' and <= '9') buf[n++] = c;
                else if (c is >= 'A' and <= 'Z') buf[n++] = (char)(c + 32);
                else if (c == '&') { buf[n++] = 'a'; buf[n++] = 'n'; buf[n++] = 'd'; }
            }
            return buf.AsSpan(0, n);
        }
    }

    /// <summary>The key of a title exactly as TitleKeys.Key computes it (exposed for tests).</summary>
    public static string FastKey(string s) => new string(new KeyMaker().Make(s));

    static SqliteConnection OpenWritable(string path)
    {
        var c = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Pooling = false }.ToString());
        c.Open();
        return c;
    }

    static void Run(SqliteConnection db, string sql)
    {
        using var c = db.CreateCommand();
        c.CommandText = sql;
        c.ExecuteNonQuery();
    }

    static (SqliteCommand Cmd, SqliteParameter[] P) Insert(SqliteConnection db, string table, int n)
    {
        var c = db.CreateCommand();
        c.CommandText = $"INSERT INTO {table} VALUES ({string.Join(',', Enumerable.Range(0, n).Select(i => "$p" + i))})";
        var ps = new SqliteParameter[n];
        for (int i = 0; i < n; i++) c.Parameters.Add(ps[i] = new SqliteParameter("$p" + i, DBNull.Value));
        return (c, ps);
    }

    /// <summary>NULL for \N, an integer when the text is one (the INT column would convert it anyway), else the text.</summary>
    static object NumOrNull(ReadOnlySpan<char> s)
    {
        if (IsNull(s)) return DBNull.Value;
        if (s.Length is > 0 and < 10 && s.IndexOfAnyExceptInRange('0', '9') < 0) return (long)int.Parse(s, NumberStyles.None, CultureInfo.InvariantCulture);
        return new string(s);
    }

    static object TextOrNull(ReadOnlySpan<char> s) => IsNull(s) ? DBNull.Value : new string(s);

    static bool IsAkaRegion(ReadOnlySpan<char> s) => s is "XWW" or "US" or "GB" or "IR" or "CA" or "AU";
    static bool IsAkaLang(ReadOnlySpan<char> s) => s is "en" or "fa";

    const string CreateTables = @"
        PRAGMA journal_mode=OFF; PRAGMA synchronous=OFF; PRAGMA cache_size=-200000;
        CREATE TABLE titles(id INTEGER PRIMARY KEY, type INT, title TEXT, orig TEXT, year INT,
                            endyear INT, runtime INT, genres TEXT, rating REAL, votes INT);
        CREATE TABLE keys(k TEXT, id INT, src INT);
        CREATE TABLE akas(id INT, title TEXT, region TEXT, lang TEXT, types TEXT);
        CREATE TABLE meta(k TEXT PRIMARY KEY, v TEXT);
    ";

    /// <summary>Builds imdb_index.db from the downloaded datasets (movie-like titles only) and swaps it in. Takes minutes for the full IMDb.</summary>
    public static void BuildIndex(string dataDir, string dbPath, Action<string, long, long> progress)
    {
        string tmp = dbPath + ".tmp";
        if (File.Exists(tmp)) File.Delete(tmp);
        bool ok = false;
        try
        {
            using (var db = OpenWritable(tmp))
            {
                Run(db, CreateTables.Replace("\r", ""));
                var km = new KeyMaker();

                progress("Reading ratings", 0, 1);
                var ratings = ReadRatings(dataDir);

                var kept = new Bits();
                long n = 0;
                using (var tx = db.BeginTransaction())
                {
                    var (ct, tp) = Insert(db, "titles", 10);
                    var (ck, kp) = Insert(db, "keys", 3);
                    using (ct) using (ck)
                    using (var r = TsvReader.Open(P(dataDir, "title.basics")))
                    {
                        while (r.Next())
                        {
                            n++;
                            if (n % 500_000 == 0) progress($"Indexing titles ({n / 1000}k)", n, 12_000_000);
                            int t = TypeOf(r[1]);
                            if (t < 0 || r[4] is "1") continue;
                            int i = Tid(r[0]);
                            kept.Set(i);
                            tp[0].Value = (long)i;
                            tp[1].Value = (long)t;
                            tp[2].Value = new string(r[2]);
                            tp[3].Value = r[3].SequenceEqual(r[2]) ? tp[2].Value : new string(r[3]);
                            tp[4].Value = NumOrNull(r[5]);
                            tp[5].Value = NumOrNull(r[6]);
                            tp[6].Value = NumOrNull(r[7]);
                            tp[7].Value = TextOrNull(r[8]);
                            if (ratings.TryGetValue(i, out var rt)) { tp[8].Value = rt.Rating; tp[9].Value = (long)rt.Votes; }
                            else { tp[8].Value = DBNull.Value; tp[9].Value = 0L; }
                            ct.ExecuteNonQuery();
                            string k1 = new string(km.Make(r[2]));
                            kp[0].Value = k1; kp[1].Value = (long)i; kp[2].Value = 0L;
                            ck.ExecuteNonQuery();
                            var k2 = km.Make(r[3]);
                            if (!k2.SequenceEqual(k1))
                            {
                                kp[0].Value = new string(k2);
                                ck.ExecuteNonQuery();
                            }
                        }
                    }
                    tx.Commit();
                }
                ratings = null;

                // akas: alternative titles (English, Persian, festival names...)
                n = 0;
                using (var tx = db.BeginTransaction())
                {
                    var (ck, kp) = Insert(db, "keys", 3);
                    var (ca, ap) = Insert(db, "akas", 5);
                    using (ck) using (ca)
                    using (var r = TsvReader.Open(P(dataDir, "title.akas")))
                    {
                        int curId = -1;
                        var curKeys = new HashSet<string>();
                        var lookup = curKeys.GetAlternateLookup<ReadOnlySpan<char>>();
                        while (r.Next())
                        {
                            n++;
                            if (n % 1_000_000 == 0) progress($"Indexing alternative titles ({n / 1_000_000}M)", n, 55_000_000);
                            int i = Tid(r[0]);
                            if (!kept.Get(i)) continue;
                            if (i != curId) { curId = i; curKeys.Clear(); }
                            var title = r[2];
                            var k = km.Make(title);
                            if (k.Length > 0 && !lookup.Contains(k))
                            {
                                string ks = new string(k);
                                curKeys.Add(ks);
                                kp[0].Value = ks; kp[1].Value = (long)i; kp[2].Value = 1L;
                                ck.ExecuteNonQuery();
                            }
                            var region = r[3];
                            var lang = r[4];
                            if (IsAkaRegion(region) || IsAkaLang(lang))
                            {
                                ap[0].Value = (long)i;
                                ap[1].Value = new string(title);
                                ap[2].Value = TextOrNull(region);
                                ap[3].Value = TextOrNull(lang);
                                ap[4].Value = TextOrNull(r[5]);
                                ca.ExecuteNonQuery();
                            }
                        }
                    }
                    tx.Commit();
                }

                string[] ix =
                {
                    "CREATE INDEX keys_k ON keys(k)", "CREATE INDEX keys_id ON keys(id)",
                    "CREATE INDEX akas_id ON akas(id)", "CREATE INDEX titles_year ON titles(year)",
                };
                for (int j = 0; j < ix.Length; j++)
                {
                    progress("Creating search indexes", j, ix.Length);
                    Run(db, ix[j]);
                }
                using (var c = db.CreateCommand())
                {
                    c.CommandText = "INSERT INTO meta VALUES ('built', $v)";
                    c.Parameters.AddWithValue("$v", DateTime.Now.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture));
                    c.ExecuteNonQuery();
                }
            }
            SqliteConnection.ClearAllPools(); // a pooled connection to the old index would block replacing it
            if (File.Exists(dbPath)) File.Delete(dbPath);
            File.Move(tmp, dbPath);
            ok = true;
        }
        finally
        {
            if (!ok) try { File.Delete(tmp); } catch (IOException) { }
        }
    }

    // ---------- cast and crew ----------
    /// <summary>Streams crew, principals and names for the given title ids.</summary>
    public static PeopleData ReadPeople(string dataDir, IEnumerable<int> ids, Action<string, long, long> progress)
    {
        var idset = ids as HashSet<int> ?? new HashSet<int>(ids);
        var credits = new Dictionary<int, TitleCredits>();
        foreach (var i in idset) credits[i] = new TitleCredits();
        var need = new HashSet<int>();
        long n = 0;
        using (var r = TsvReader.Open(P(dataDir, "title.crew")))
            while (r.Next())
            {
                n++;
                if (n % 1_000_000 == 0) progress($"Reading directors/writers ({n / 1_000_000}M)", n, 12_000_000);
                int i = Tid(r[0]);
                if (!idset.Contains(i)) continue;
                var c = credits[i];
                if (!IsNull(r[1]))
                {
                    c.Directors = new List<int>();
                    foreach (var part in r[1].Split(',')) c.Directors.Add(Tid(r[1][part]));
                }
                if (!IsNull(r[2]))
                {
                    c.Writers = new List<int>();
                    foreach (var part in r[2].Split(',')) c.Writers.Add(Tid(r[2][part]));
                    if (c.Writers.Count > 8) c.Writers.RemoveRange(8, c.Writers.Count - 8);
                }
                need.UnionWith(c.Directors);
                need.UnionWith(c.Writers);
            }
        n = 0;
        using (var r = TsvReader.Open(P(dataDir, "title.principals")))
            while (r.Next())
            {
                n++;
                if (n % 2_000_000 == 0) progress($"Reading cast ({n / 1_000_000}M)", n, 95_000_000);
                int i = Tid(r[0]);
                if (!idset.Contains(i)) continue;
                var cat = r[3];
                int nc = Tid(r[2]);
                need.Add(nc);
                if (cat is "actor" or "actress" or "self")
                {
                    string? ch = null;
                    if (!IsNull(r[5])) ch = r[5].ToString().Trim('[', ']').Replace("\",\"", " / ").Trim('"');
                    credits[i].Cast.Add((nc, ch));
                }
                else if (cat is not ("director" or "writer"))
                    credits[i].Crew.Add((nc, !IsNull(r[4]) ? r[4].ToString() : cat.ToString().Replace('_', ' ')));
            }
        var names = new Dictionary<int, string>();
        var basics = new Dictionary<int, PersonBasics>();
        n = 0;
        using (var r = TsvReader.Open(P(dataDir, "name.basics")))
            while (r.Next())
            {
                n++;
                if (n % 1_000_000 == 0) progress($"Reading names ({n / 1_000_000}M)", n, 15_000_000);
                int i = Tid(r[0]);
                if (!need.Contains(i)) continue;
                names[i] = r[1].ToString();
                basics[i] = BasicsOf(r);
            }
        return new PeopleData(credits, names, basics);
    }

    static int? Year(ReadOnlySpan<char> v) =>
        !IsNull(v) && v.Length is > 0 and < 10 && v.IndexOfAnyExceptInRange('0', '9') < 0 ? int.Parse(v, NumberStyles.None, CultureInfo.InvariantCulture) : null;

    /// <summary>(birth year, death year, professions) from a name.basics row.</summary>
    static PersonBasics BasicsOf(TsvReader r) =>
        new(Year(r[2]), Year(r[3]), r.Count > 4 && !IsNull(r[4]) ? r[4].ToString().Replace('_', ' ') : "");

    /// <summary>Birth year, death year and professions for the given people ids, from name.basics.</summary>
    public static Dictionary<int, PersonBasics> ReadBasics(string dataDir, IEnumerable<int> ids, Action<string, long, long>? progress = null)
    {
        var idset = ids as HashSet<int> ?? new HashSet<int>(ids);
        var d = new Dictionary<int, PersonBasics>();
        long n = 0;
        using var r = TsvReader.Open(P(dataDir, "name.basics"));
        while (r.Next())
        {
            n++;
            if (n % 1_000_000 == 0) progress?.Invoke($"Reading people details ({n / 1_000_000}M)", n, 15_000_000);
            int i = Tid(r[0]);
            if (idset.Contains(i)) d[i] = BasicsOf(r);
        }
        return d;
    }
}
