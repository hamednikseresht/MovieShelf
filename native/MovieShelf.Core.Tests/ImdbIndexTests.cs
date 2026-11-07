using System.IO.Compression;
using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using MovieShelf.Core;
using Xunit;

namespace MovieShelf.Core.Tests;

/// <summary>Tiny IMDb dataset files, a temp folder, and helpers for the IMDb / Persian-title tests.</summary>
sealed class ImdbFixture : IDisposable
{
    public readonly string Dir = Path.Combine(Path.GetTempPath(), "mdb_imdb_" + Guid.NewGuid().ToString("N"));
    public string Data => Path.Combine(Dir, "imdb");

    public static readonly Dictionary<string, string> Texts = new()
    {
        ["title.ratings"] = "tconst\taverageRating\tnumVotes\n" +
            "tt0000001\t8.7\t100\n" + "tt0000005\t9.5\t2000\n" + "tt0000009\t5.0\t7\n",
        ["title.basics"] = "tconst\ttitleType\tprimaryTitle\toriginalTitle\tisAdult\tstartYear\tendYear\truntimeMinutes\tgenres\n" +
            "tt0000001\tmovie\tThe Matrix\tThe Matrix\t0\t1999\t\\N\t136\tAction,Sci-Fi\n" +
            "tt0000002\tshort\tTom & Jerry's Caf\u00e9\tTom & Jerry's Caf\u00e9\t0\t1940\t\\N\t7\tAnimation\n" +
            "tt0000003\ttvEpisode\tEpisode\tEpisode\t0\t2000\t\\N\t30\tComedy\n" +
            "tt0000004\tmovie\tAdult\tAdult\t1\t2001\t\\N\t90\t\\N\n" +
            "tt0000005\ttvSeries\tBreaking Bad\tBreaking Bad\t0\t2008\t2013\t49\tCrime,Drama\n" +
            "tt0000006\tmovie\tPi\t\u03c0\t0\t1998\t\\N\t84\tThriller\n" +
            "tt0000007\tvideoGame\tGame\tGame\t0\t2010\t\\N\t\\N\t\\N\n" +
            "tt0000009\ttvMovie\tNo Year\tNo Year\t0\t\\N\t\\N\t\\N\t\\N\n",
        ["title.akas"] = "titleId\tordering\ttitle\tregion\tlanguage\ttypes\tattributes\tisOriginalTitle\n" +
            "tt0000001\t1\tThe Matrix\tUS\t\\N\timdbDisplay\t\\N\t0\n" +
            "tt0000001\t2\t\u0645\u0627\u062a\u0631\u06cc\u06a9\u0633\tIR\tfa\t\\N\t\\N\t0\n" +
            "tt0000001\t3\tMatrix\tDE\tde\t\\N\t\\N\t0\n" +
            "tt0000001\t4\tmatrix!\tFR\t\\N\t\\N\t\\N\t0\n" +
            "tt0000001\t5\tMatrix\tXWW\t\\N\t\\N\t\\N\t0\n" +
            "tt0000001\t6\t!!!\tES\tes\t\\N\t\\N\t0\n" +
            "tt0000003\t1\tEpisode\tUS\ten\t\\N\t\\N\t0\n" +
            "tt0000002\t1\tTom und Jerry\tDE\tde\t\\N\t\\N\t0\n" +
            "tt0000002\t2\tTom & Jerry\tGB\ten\t\\N\t\\N\t0\n" +
            "tt0000006\t1\tPi: Faith in Chaos\tUS\t\\N\t\\N\t\\N\t0\n" +
            "tt0000006\t2\tPi\tUS\t\\N\t\\N\t\\N\t0\n",
        ["title.crew"] = "tconst\tdirectors\twriters\n" +
            "tt0000001\tnm0000001,nm0000002\tnm0000001,nm0000002,nm0000003,nm0000004,nm0000005,nm0000006,nm0000007,nm0000008,nm0000009,nm0000010\n" +
            "tt0000002\tnm0000003\t\\N\n" + "tt0000003\tnm0000099\t\\N\n" + "tt0000005\t\\N\t\\N\n",
        ["title.principals"] = "tconst\tordering\tnconst\tcategory\tjob\tcharacters\n" +
            "tt0000001\t1\tnm0000011\tactor\t\\N\t[\"Neo\",\"Thomas Anderson\"]\n" +
            "tt0000001\t2\tnm0000012\tactress\t\\N\t[\"Trinity\"]\n" +
            "tt0000001\t3\tnm0000001\tdirector\t\\N\t\\N\n" +
            "tt0000001\t4\tnm0000013\tcomposer\t\\N\t\\N\n" +
            "tt0000001\t5\tnm0000014\tproduction_designer\tlead designer\t\\N\n" +
            "tt0000001\t6\tnm0000015\tself\t\\N\t\\N\n" +
            "tt0000003\t1\tnm0000098\tactor\t\\N\t[\"X\"]\n" +
            "tt0000005\t1\tnm0000016\tactor\t\\N\t[\"Walter\"]\n",
        ["name.basics"] = "nconst\tprimaryName\tbirthYear\tdeathYear\tprimaryProfession\tknownForTitles\n" +
            "nm0000001\tLana Wachowski\t1965\t\\N\tdirector,writer,producer\ttt0000001\n" +
            "nm0000002\tLilly Wachowski\t1967\t\\N\tdirector\ttt0000001\n" +
            "nm0000003\tThird Writer\t\\N\t\\N\t\\N\ttt0000001\n" +
            "nm0000011\tKeanu Reeves\t1964\t\\N\tactor,producer\ttt0000001\n" +
            "nm0000012\tCarrie-Anne Moss\t1967\t2099\tactress\ttt0000001\n" +
            "nm0000013\tA Composer\tabcd\t\\N\tcomposer\ttt0000001\n" +
            "nm0000014\tA Designer\t1950\t\\N\tproduction_designer\ttt0000001\n" +
            "nm0000015\tHimself\t1900\t1999\t\\N\ttt0000001\n" +
            "nm0000016\tBryan Cranston\t1956\t\\N\tactor\ttt0000005\n" +
            "nm0000098\tNobody\t1900\t\\N\tactor\ttt0000003\n",
    };

    public ImdbFixture(bool writeFiles = true)
    {
        Directory.CreateDirectory(Data);
        if (writeFiles) foreach (var (name, text) in Texts) Gz(Path.Combine(Data, name + ".tsv.gz"), text);
    }

    public static void Gz(string path, string text) => File.WriteAllBytes(path, GzBytes(text));

    public static byte[] GzBytes(string text)
    {
        var ms = new MemoryStream();
        using (var g = new GZipStream(ms, CompressionLevel.Fastest, true)) g.Write(Encoding.UTF8.GetBytes(text));
        return ms.ToArray();
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try { Directory.Delete(Dir, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    public static List<object?[]> Rows(string db, string sql)
    {
        using var c = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = db, Pooling = false }.ToString());
        c.Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = sql;
        using var r = cmd.ExecuteReader();
        var l = new List<object?[]>();
        while (r.Read())
        {
            var row = new object?[r.FieldCount];
            for (int i = 0; i < row.Length; i++) row[i] = r.IsDBNull(i) ? null : r.GetValue(i);
            l.Add(row);
        }
        return l;
    }

    public static void Run(string db, string sql, params object?[] args)
    {
        using var c = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = db, Pooling = false }.ToString());
        c.Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = sql;
        for (int i = 0; i < args.Length; i++) cmd.Parameters.AddWithValue("$p" + i, args[i] ?? DBNull.Value);
        cmd.ExecuteNonQuery();
    }

    public static string Dump(List<object?[]> rows) => string.Join("\n", rows.Select(r => string.Join("|", r.Select(x => x is double d ? d.ToString("0.0###", System.Globalization.CultureInfo.InvariantCulture) : x?.ToString() ?? "NULL"))));
}

sealed class FakeHandler : HttpMessageHandler
{
    readonly Func<HttpRequestMessage, HttpResponseMessage> f;
    public readonly List<string> Urls = new();
    public FakeHandler(Func<HttpRequestMessage, HttpResponseMessage> f) => this.f = f;

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        Urls.Add(request.RequestUri!.ToString());
        return Task.FromResult(f(request));
    }

    /// <summary>Serves the fixture text for each dataset file.</summary>
    public static FakeHandler Datasets(Dictionary<string, string> texts) => new(req =>
    {
        string name = Path.GetFileName(req.RequestUri!.AbsolutePath).Replace(".tsv.gz", "");
        if (!texts.TryGetValue(name, out var t)) return new HttpResponseMessage(HttpStatusCode.NotFound);
        return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(ImdbFixture.GzBytes(t)) };
    });
}

/// <summary>A response body that breaks after some bytes.</summary>
sealed class BreakingStream : Stream
{
    readonly byte[] data;
    int pos;
    public BreakingStream(byte[] data) => this.data = data;
    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => throw new NotSupportedException();
    public override long Position { get => pos; set => throw new NotSupportedException(); }
    public override void Flush() { }
    public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));
    public override int Read(Span<byte> buffer)
    {
        if (pos >= data.Length) throw new IOException("connection lost");
        int n = Math.Min(buffer.Length, data.Length - pos);
        data.AsSpan(pos, n).CopyTo(buffer);
        pos += n;
        return n;
    }
    public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default) => new(Read(buffer.Span));
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}

public class ImdbBuildTests
{
    static void NoProgress(string s, long d, long t) { }

    [Fact]
    public void BuildsTheSameTablesAsPython()
    {
        using var fx = new ImdbFixture();
        string db = Path.Combine(fx.Dir, "imdb_index.db");
        var stages = new List<string>();
        ImdbIndex.BuildIndex(fx.Data, db, (s, d, t) => stages.Add(s));
        Assert.Contains("Reading ratings", stages);
        Assert.Contains("Creating search indexes", stages);
        Assert.False(File.Exists(db + ".tmp"));

        var master = ImdbFixture.Rows(db, "SELECT type, name, tbl_name FROM sqlite_master WHERE name NOT LIKE 'sqlite_%' ORDER BY type, name");
        Assert.Equal("index|akas_id|akas\nindex|keys_id|keys\nindex|keys_k|keys\nindex|titles_year|titles\ntable|akas|akas\ntable|keys|keys\ntable|meta|meta\ntable|titles|titles", ImdbFixture.Dump(master));
        Assert.Equal("id|type|title|orig|year|endyear|runtime|genres|rating|votes",
            string.Join("|", ImdbFixture.Rows(db, "SELECT name FROM pragma_table_info('titles')").Select(r => r[0])));
        Assert.Equal("k|id|src", string.Join("|", ImdbFixture.Rows(db, "SELECT name FROM pragma_table_info('keys')").Select(r => r[0])));
        Assert.Equal("id|title|region|lang|types", string.Join("|", ImdbFixture.Rows(db, "SELECT name FROM pragma_table_info('akas')").Select(r => r[0])));

        // episodes, adult titles and games are left out
        Assert.Equal(
            "1|0|The Matrix|The Matrix|1999|NULL|136|Action,Sci-Fi|8.7|100\n" +
            "2|4|Tom & Jerry's Caf\u00e9|Tom & Jerry's Caf\u00e9|1940|NULL|7|Animation|NULL|0\n" +
            "5|7|Breaking Bad|Breaking Bad|2008|2013|49|Crime,Drama|9.5|2000\n" +
            "6|0|Pi|\u03c0|1998|NULL|84|Thriller|NULL|0\n" +
            "9|1|No Year|No Year|NULL|NULL|NULL|NULL|5.0|7",
            ImdbFixture.Dump(ImdbFixture.Rows(db, "SELECT * FROM titles ORDER BY id")));
        // year / runtime are stored as integers, like the INT column of the Python build
        Assert.Equal("integer|integer|integer", string.Join("|", ImdbFixture.Rows(db, "SELECT typeof(year), typeof(runtime), typeof(type) FROM titles WHERE id=1")[0]));

        // keys in insertion order: titles first (title key, then the original's key when it differs), then new keys of the alternative titles
        Assert.Equal(
            "thematrix|1|0\ntomandjerryscafe|2|0\nbreakingbad|5|0\npi|6|0\n\u03c0|6|0\nnoyear|9|0\n" +
            "thematrix|1|1\n\u0645\u0627\u062a\u0631\u06cc\u06a9\u0633|1|1\nmatrix|1|1\ntomundjerry|2|1\ntomandjerry|2|1\npifaithinchaos|6|1\npi|6|1",
            ImdbFixture.Dump(ImdbFixture.Rows(db, "SELECT k, id, src FROM keys ORDER BY rowid")));

        // alternative titles only for the regions and languages Python keeps
        Assert.Equal(
            "1|The Matrix|US|NULL|imdbDisplay\n1|\u0645\u0627\u062a\u0631\u06cc\u06a9\u0633|IR|fa|NULL\n1|Matrix|XWW|NULL|NULL\n" +
            "2|Tom & Jerry|GB|en|NULL\n6|Pi: Faith in Chaos|US|NULL|NULL\n6|Pi|US|NULL|NULL",
            ImdbFixture.Dump(ImdbFixture.Rows(db, "SELECT * FROM akas ORDER BY rowid")));

        var meta = ImdbFixture.Rows(db, "SELECT k, v FROM meta");
        Assert.Single(meta);
        Assert.Equal("built", meta[0][0]);
        Assert.Matches(@"^\d{4}-\d\d-\d\d \d\d:\d\d$", (string)meta[0][1]!);
    }

    [Fact]
    public void ReplacesAnExistingIndexAndLeavesNoTempFile()
    {
        using var fx = new ImdbFixture();
        string db = Path.Combine(fx.Dir, "imdb_index.db");
        File.WriteAllText(db, "old");
        ImdbIndex.BuildIndex(fx.Data, db, NoProgress);
        Assert.Equal("5", ImdbFixture.Dump(ImdbFixture.Rows(db, "SELECT count(*) FROM titles")));
        Assert.False(File.Exists(db + ".tmp"));
    }

    [Fact]
    public void StopDuringTheBuildKeepsTheOldIndexAndDeletesTheTempFile()
    {
        using var fx = new ImdbFixture();
        string db = Path.Combine(fx.Dir, "imdb_index.db");
        File.WriteAllText(db, "old");
        Assert.Throws<OperationCanceledException>(() => ImdbIndex.BuildIndex(fx.Data, db, (s, d, t) =>
        {
            if (s == "Creating search indexes") throw new OperationCanceledException();
        }));
        Assert.Equal("old", File.ReadAllText(db));
        Assert.False(File.Exists(db + ".tmp"));
    }

    [Fact]
    public void FastKeyEqualsTitleKeys()
    {
        var samples = new[]
        {
            "", "The Matrix", "Tom & Jerry's Caf\u00e9", "Se7en", "AT&T: 3.5 / 4,5", "L'\u00e9t\u00e9 meurtrier", "\u03c0", "\u00c5ngstr\u00f6m",
            "  spaced   out  ", "!!!", "O'Brien's `Day\u2019s` 1.000", "\u0645\u0627\u062a\u0631\u06cc\u06a9\u0633 2", "\u6771\u4eac\u7269\u8a9e", "Stra\u00dfe", "\u0141\u00f3d\u017a", "ALL CAPS & MORE", "a\tb\u0001c",
        };
        foreach (var s in samples) Assert.Equal(TitleKeys.Key(s), ImdbIndex.FastKey(s));
        var rnd = new Random(7);
        for (int i = 0; i < 20000; i++)
        {
            var sb = new StringBuilder();
            for (int j = rnd.Next(0, 20); j > 0; j--) sb.Append((char)rnd.Next(32, 127));
            Assert.Equal(TitleKeys.Key(sb.ToString()), ImdbIndex.FastKey(sb.ToString()));
        }
    }

    [Fact]
    public void KeysUseTheFinalSigmaLikePythonLower()
    {
        // expected values from Python movieshelf.parse.key
        Assert.Equal("οδυσσειας", TitleKeys.Key("ΟΔΥΣΣΕΙΑΣ"));
        Assert.Equal("σ", TitleKeys.Key("Σ"));
        Assert.Equal("αςσας", TitleKeys.Key("ΑΣ ΣΑΣ"));
        Assert.Equal("σοςσας", TitleKeys.Key("Σ.Ο.Σ. ΣΑΣ"));
        Assert.Equal("ασας", TitleKeys.Key("ΑΣ’ΑΣ"));
        Assert.Equal("1σ", TitleKeys.Key("1Σ"));
        Assert.Equal("ος2", TitleKeys.Key("ΟΣ2"));
    }

    [Fact]
    public void HaveFilesAndFileAge()
    {
        using var fx = new ImdbFixture(writeFiles: false);
        Assert.False(ImdbIndex.HaveFiles(fx.Data));
        Assert.Null(ImdbIndex.FileAgeDays(fx.Data, "title.ratings"));
        foreach (var (name, text) in ImdbFixture.Texts) ImdbFixture.Gz(Path.Combine(fx.Data, name + ".tsv.gz"), text);
        Assert.True(ImdbIndex.HaveFiles(fx.Data));
        Assert.InRange(ImdbIndex.FileAgeDays(fx.Data, "title.ratings")!.Value, -0.01, 0.01);
        File.SetLastWriteTimeUtc(Path.Combine(fx.Data, "title.ratings.tsv.gz"), DateTime.UtcNow.AddDays(-9));
        Assert.InRange(ImdbIndex.FileAgeDays(fx.Data, "title.ratings")!.Value, 8.99, 9.01);
        File.Delete(Path.Combine(fx.Data, "name.basics.tsv.gz"));
        Assert.False(ImdbIndex.HaveFiles(fx.Data));
    }

    [Fact]
    public void ReadsRatings()
    {
        using var fx = new ImdbFixture();
        var r = ImdbIndex.ReadRatings(fx.Data);
        Assert.Equal(3, r.Count);
        Assert.Equal((8.7, 100), r[1]);
        Assert.Equal((9.5, 2000), r[5]);
    }

    [Fact]
    public void HandlesCrLfAndNoTrailingNewline()
    {
        using var fx = new ImdbFixture();
        ImdbFixture.Gz(Path.Combine(fx.Data, "title.ratings.tsv.gz"), "tconst\taverageRating\tnumVotes\r\ntt0000001\t1.5\t3\r\n\r\ntt0000002\t2.5\t4");
        var r = ImdbIndex.ReadRatings(fx.Data);
        Assert.Equal(2, r.Count);
        Assert.Equal((2.5, 4), r[2]);
    }

    [Fact]
    public void ReadsPeople()
    {
        using var fx = new ImdbFixture();
        var stages = new List<string>();
        var (credits, names, basics) = ImdbIndex.ReadPeople(fx.Data, new[] { 1, 5, 77 }, (s, d, t) => stages.Add(s));
        Assert.Equal(new[] { 1, 5, 77 }, credits.Keys.OrderBy(x => x));
        var c = credits[1];
        Assert.Equal(new[] { 1, 2 }, c.Directors);
        Assert.Equal(new[] { 1, 2, 3, 4, 5, 6, 7, 8 }, c.Writers); // only the first 8
        Assert.Equal(new (int, string?)[] { (11, "Neo / Thomas Anderson"), (12, "Trinity"), (15, null) }, c.Cast.Select(x => (x.Nc, x.Char)).ToArray());
        Assert.Equal(new[] { (13, "composer"), (14, "lead designer") }, c.Crew.Select(x => (x.Nc, x.Job)).ToArray());
        Assert.Empty(credits[5].Directors);
        Assert.Equal(new[] { (16, "Walter") }, credits[5].Cast.Select(x => (x.Nc, x.Char)).ToArray());
        Assert.Empty(credits[77].Cast);
        // titles outside the list (tt3) add nobody
        Assert.False(names.ContainsKey(99));
        Assert.False(names.ContainsKey(98));
        Assert.Equal("Lana Wachowski", names[1]);
        Assert.False(names.ContainsKey(9)); // writer 9 and 10 beyond the first 8 are not read
        Assert.Equal(new PersonBasics(1965, null, "director,writer,producer"), basics[1]);
        Assert.Equal(new PersonBasics(null, null, ""), basics[3]);
        Assert.Equal(new PersonBasics(1967, 2099, "actress"), basics[12]);
        Assert.Equal(new PersonBasics(null, null, "composer"), basics[13]);
        Assert.Equal(new PersonBasics(1950, null, "production designer"), basics[14]);
    }

    [Fact]
    public void ReadsBasics()
    {
        using var fx = new ImdbFixture();
        var b = ImdbIndex.ReadBasics(fx.Data, new[] { 1, 16, 12345 });
        Assert.Equal(2, b.Count);
        Assert.Equal(new PersonBasics(1956, null, "actor"), b[16]);
    }
}

public class ImdbDownloadTests
{
    static void NoProgress(string s, long d, long t) { }

    [Fact]
    public void DownloadsMissingFilesWithProgressAndSkipsExistingOnes()
    {
        using var fx = new ImdbFixture(writeFiles: false);
        ImdbFixture.Gz(Path.Combine(fx.Data, "title.ratings.tsv.gz"), "keep me");
        var h = FakeHandler.Datasets(ImdbFixture.Texts);
        var texts = new List<string>();
        ImdbIndex.Download(fx.Data, (s, d, t) => texts.Add(s), http: new HttpClient(h));
        Assert.Equal(5, h.Urls.Count);
        Assert.DoesNotContain(h.Urls, u => u.Contains("title.ratings"));
        Assert.All(h.Urls, u => Assert.StartsWith("https://datasets.imdbws.com/", u));
        Assert.True(ImdbIndex.HaveFiles(fx.Data));
        Assert.Empty(Directory.GetFiles(fx.Data, "*.part"));
        Assert.Contains("Downloading title.basics", texts);
        Assert.Contains(texts, s => s.StartsWith("Downloading title.basics (0/0 MB)"));
    }

    [Fact]
    public void ForceDownloadsAgainAndReplacesTheFile()
    {
        using var fx = new ImdbFixture();
        var h = FakeHandler.Datasets(new() { ["title.ratings"] = "tconst\taverageRating\tnumVotes\ntt0000042\t7.7\t77\n" });
        ImdbIndex.Download(fx.Data, NoProgress, force: true, names: new[] { "title.ratings" }, http: new HttpClient(h));
        Assert.Single(h.Urls);
        Assert.Equal((7.7, 77), ImdbIndex.ReadRatings(fx.Data)[42]);
        Assert.Empty(Directory.GetFiles(fx.Data, "*.part"));
    }

    [Fact]
    public void SendsTheUserAgent()
    {
        using var fx = new ImdbFixture(writeFiles: false);
        string? ua = null;
        var h = new FakeHandler(req => { ua = req.Headers.UserAgent.ToString(); return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(new byte[] { 1 }) }; });
        ImdbIndex.Download(fx.Data, NoProgress, names: new[] { "title.crew" }, http: new HttpClient(h));
        Assert.Equal("MovieShelf/1.0 (personal collection manager)", ua);
    }

    [Fact]
    public void AHttpErrorLeavesNoFile()
    {
        using var fx = new ImdbFixture(writeFiles: false);
        var h = new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
        Assert.Throws<HttpRequestException>(() => ImdbIndex.Download(fx.Data, NoProgress, names: new[] { "title.crew" }, http: new HttpClient(h)));
        Assert.Empty(Directory.GetFiles(fx.Data));
    }

    [Fact]
    public void ABrokenConnectionDeletesThePartFile()
    {
        using var fx = new ImdbFixture(writeFiles: false);
        var h = new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(new BreakingStream(new byte[5000])) });
        Assert.Throws<IOException>(() => ImdbIndex.Download(fx.Data, NoProgress, names: new[] { "title.crew" }, http: new HttpClient(h)));
        Assert.Empty(Directory.GetFiles(fx.Data));
    }

    [Fact]
    public void StopDeletesThePartFileAndKeepsFinishedFiles()
    {
        using var fx = new ImdbFixture(writeFiles: false);
        var h = FakeHandler.Datasets(ImdbFixture.Texts);
        Assert.Throws<OperationCanceledException>(() => ImdbIndex.Download(fx.Data, (s, d, t) =>
        {
            if (s.StartsWith("Downloading title.basics (")) throw new OperationCanceledException(); // after the first chunk was written
        }, http: new HttpClient(h)));
        Assert.Equal(new[] { "title.ratings.tsv.gz" }, Directory.GetFiles(fx.Data).Select(Path.GetFileName).ToArray());
    }

    [Fact]
    public void StopTokenCancelsTheRequest()
    {
        using var fx = new ImdbFixture(writeFiles: false);
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var h = FakeHandler.Datasets(ImdbFixture.Texts);
        Assert.ThrowsAny<OperationCanceledException>(() => ImdbIndex.Download(fx.Data, NoProgress, http: new HttpClient(h), ct: cts.Token));
        Assert.Empty(Directory.GetFiles(fx.Data));
    }
}

public class LibraryImdbTests
{
    static Library NewLib(ImdbFixture fx, FakeHandler? h = null)
    {
        var lib = new Library(Path.Combine(fx.Dir, "root"));
        if (h != null) lib.ImdbHttp = new HttpClient(h);
        return lib;
    }

    static void CopyData(Library lib, ImdbFixture fx)
    {
        Directory.CreateDirectory(lib.ImdbDir);
        foreach (var f in Directory.GetFiles(fx.Data)) File.Copy(f, Path.Combine(lib.ImdbDir, Path.GetFileName(f)), true);
    }

    static void Titles(Library lib, params (int T, string Type)[] rows)
    {
        foreach (var (t, type) in rows)
            ImdbFixture.Run(lib.DbPath, "INSERT INTO titles(tconst, type, title, rating, votes) VALUES ($p0,$p1,'t',1.0,1)", t, type);
    }

    [Fact]
    public void EnsureIndexDownloadsBuildsAndSkipsWhatExists()
    {
        using var fx = new ImdbFixture(writeFiles: false);
        var h = FakeHandler.Datasets(ImdbFixture.Texts);
        using var lib = NewLib(fx, h);
        Titles(lib, (1, "movie"));
        ImdbFixture.Run(lib.DbPath, "UPDATE titles SET enriched=1");
        lib.EnsureIndex();
        Assert.Equal(6, h.Urls.Count);
        Assert.True(File.Exists(lib.IndexPath));
        Assert.True(lib.Matcher.HasIndex);
        Assert.Equal("1", ImdbFixture.Dump(ImdbFixture.Rows(lib.DbPath, "SELECT enriched FROM titles"))); // not forced: stays
        Assert.Contains(lib.Job.Log, l => l.EndsWith("IMDb index built"));

        lib.EnsureIndex(); // everything there, ratings fresh: nothing to do
        Assert.Equal(6, h.Urls.Count);

        lib.EnsureIndex(forceDownload: true); // all six again, rebuilt, enriched reset
        Assert.Equal(12, h.Urls.Count);
        Assert.Equal("0", ImdbFixture.Dump(ImdbFixture.Rows(lib.DbPath, "SELECT enriched FROM titles")));
        Assert.Equal("5", ImdbFixture.Dump(ImdbFixture.Rows(lib.IndexPath, "SELECT count(*) FROM titles")));
    }

    [Fact]
    public void EnsureIndexRefreshesOldRatingsWhenAutoRefreshIsOn()
    {
        using var fx = new ImdbFixture();
        var h = FakeHandler.Datasets(new() { ["title.ratings"] = "tconst\taverageRating\tnumVotes\ntt0000001\t6.5\t321\n" });
        using var lib = NewLib(fx, h);
        CopyData(lib, fx);
        ImdbIndex.BuildIndex(lib.ImdbDir, lib.IndexPath, (s, d, t) => { });
        Titles(lib, (1, "movie"));
        File.SetLastWriteTimeUtc(Path.Combine(lib.ImdbDir, "title.ratings.tsv.gz"), DateTime.UtcNow.AddDays(-10));

        lib.EnsureIndex(); // auto_refresh defaults to on
        Assert.Single(h.Urls);
        Assert.Equal("6.5|321", ImdbFixture.Dump(ImdbFixture.Rows(lib.DbPath, "SELECT rating, votes FROM titles WHERE tconst=1")));
        Assert.Equal("6.5|321", ImdbFixture.Dump(ImdbFixture.Rows(lib.IndexPath, "SELECT rating, votes FROM titles WHERE id=1")));
        Assert.Equal("9.5|2000", ImdbFixture.Dump(ImdbFixture.Rows(lib.IndexPath, "SELECT rating, votes FROM titles WHERE id=5"))); // not in the new file: unchanged

        // fresh file now: no second download
        lib.EnsureIndex();
        Assert.Single(h.Urls);
    }

    [Fact]
    public void EnsureIndexSkipsTheRefreshWhenOffline()
    {
        using var fx = new ImdbFixture();
        var h = new FakeHandler(_ => throw new HttpRequestException("no network"));
        using var lib = NewLib(fx, h);
        CopyData(lib, fx);
        ImdbIndex.BuildIndex(lib.ImdbDir, lib.IndexPath, (s, d, t) => { });
        File.SetLastWriteTimeUtc(Path.Combine(lib.ImdbDir, "title.ratings.tsv.gz"), DateTime.UtcNow.AddDays(-10));
        lib.EnsureIndex();
        Assert.Contains(lib.Job.Log, l => l.Contains("Ratings refresh skipped: no network"));
    }

    [Fact]
    public void RefreshRatingsUpdatesOnlyChangedTitlesAndTheIndex()
    {
        using var fx = new ImdbFixture();
        var h = FakeHandler.Datasets(new() { ["title.ratings"] = "tconst\taverageRating\tnumVotes\ntt0000001\t8.7\t100\ntt0000005\t9.0\t2500\ntt0000009\t5.0\t7\n" });
        using var lib = NewLib(fx, h);
        CopyData(lib, fx);
        ImdbIndex.BuildIndex(lib.ImdbDir, lib.IndexPath, (s, d, t) => { });
        Titles(lib, (1, "movie"), (5, "tvSeries"), (9, "tvMovie"), (777, "movie"));
        ImdbFixture.Run(lib.DbPath, "UPDATE titles SET rating=8.7, votes=100, updated='x' WHERE tconst=1");
        ImdbFixture.Run(lib.DbPath, "UPDATE titles SET rating=5.0, votes=7, updated='x' WHERE tconst=9");
        int v = lib.Version;
        lib.RefreshRatings();
        Assert.Equal("1|8.7|100|x\n5|9.0|2500|" + ImdbFixture.Rows(lib.DbPath, "SELECT updated FROM titles WHERE tconst=5")[0][0] + "\n9|5.0|7|x\n777|1.0|1|NULL",
            ImdbFixture.Dump(ImdbFixture.Rows(lib.DbPath, "SELECT tconst, rating, votes, updated FROM titles ORDER BY tconst")));
        Assert.Equal("9.0|2500", ImdbFixture.Dump(ImdbFixture.Rows(lib.IndexPath, "SELECT rating, votes FROM titles WHERE id=5")));
        Assert.Equal(v + 1, lib.Version);
        Assert.Contains(lib.Job.Log, l => l.EndsWith("IMDb ratings updated: 1 of your titles changed"));
        Assert.Empty(Directory.GetFiles(lib.ImdbDir, "*.part"));
    }

    [Fact]
    public void EnrichCopiesCreditsAndPeopleLikePython()
    {
        using var fx = new ImdbFixture();
        using var lib = NewLib(fx);
        CopyData(lib, fx);
        Titles(lib, (1, "movie"), (5, "tvSeries"));
        ImdbFixture.Run(lib.DbPath, "INSERT INTO titles(tconst, type) VALUES (6, NULL)"); // no type: not enriched by default
        ImdbFixture.Run(lib.DbPath, "UPDATE titles SET enriched=1 WHERE tconst=5");
        lib.Enrich();
        var row = ImdbFixture.Rows(lib.DbPath, "SELECT directors, writers, cast_, crew, enriched FROM titles WHERE tconst=1")[0];
        Assert.Equal("[[1, \"Lana Wachowski\"], [2, \"Lilly Wachowski\"]]", row[0]);
        Assert.Equal("[[1, \"Lana Wachowski\"], [2, \"Lilly Wachowski\"], [3, \"Third Writer\"], [4, \"nm0000004\"], [5, \"nm0000005\"], [6, \"nm0000006\"], [7, \"nm0000007\"], [8, \"nm0000008\"]]", row[1]);
        Assert.Equal("[[11, \"Keanu Reeves\", \"Neo / Thomas Anderson\"], [12, \"Carrie-Anne Moss\", \"Trinity\"], [15, \"Himself\", null]]", row[2]);
        Assert.Equal("[[13, \"A Composer\", \"composer\"], [14, \"A Designer\", \"lead designer\"]]", row[3]);
        Assert.Equal(1L, row[4]);
        Assert.Equal("NULL", ImdbFixture.Dump(ImdbFixture.Rows(lib.DbPath, "SELECT directors FROM titles WHERE tconst=5")));
        Assert.Equal("0", ImdbFixture.Dump(ImdbFixture.Rows(lib.DbPath, "SELECT enriched FROM titles WHERE tconst=6")));
        Assert.Equal("1|Lana Wachowski|1965|NULL|director,writer,producer\n3|Third Writer|NULL|NULL|\n12|Carrie-Anne Moss|1967|2099|actress",
            ImdbFixture.Dump(ImdbFixture.Rows(lib.DbPath, "SELECT nconst, name, birth, death, prof FROM people WHERE nconst IN (1,3,12) ORDER BY nconst")));

        // an explicit id list works on titles that are already enriched, and an empty list does nothing
        lib.Enrich(new[] { 5 });
        Assert.Equal("[]", ImdbFixture.Dump(ImdbFixture.Rows(lib.DbPath, "SELECT directors FROM titles WHERE tconst=5")));
        Assert.Equal("[[16, \"Bryan Cranston\", \"Walter\"]]", ImdbFixture.Dump(ImdbFixture.Rows(lib.DbPath, "SELECT cast_ FROM titles WHERE tconst=5")));
        lib.Enrich(Array.Empty<int>());
    }

    [Fact]
    public void EnrichEscapesJsonLikePython()
    {
        using var fx = new ImdbFixture();
        using var lib = NewLib(fx);
        CopyData(lib, fx);
        ImdbFixture.Gz(Path.Combine(lib.ImdbDir, "name.basics.tsv.gz"), ImdbFixture.Texts["name.basics"].Replace("Keanu Reeves", "Keanu \"K\" Re\\eves \u00e9"));
        Titles(lib, (1, "movie"));
        lib.Enrich();
        Assert.Contains("[11, \"Keanu \\\"K\\\" Re\\\\eves \u00e9\", \"Neo / Thomas Anderson\"]", (string)ImdbFixture.Rows(lib.DbPath, "SELECT cast_ FROM titles")[0][0]!);
        // the library reads it back
        var cast = JsonSerializer.Deserialize<List<List<JsonElement>>>((string)ImdbFixture.Rows(lib.DbPath, "SELECT cast_ FROM titles")[0][0]!)!;
        Assert.Equal(3, cast.Count);
    }

    [Fact]
    public void FillPeopleBasicsFillsOnlyPeopleWithoutProfessions()
    {
        using var fx = new ImdbFixture();
        using var lib = NewLib(fx);
        CopyData(lib, fx);
        ImdbFixture.Run(lib.DbPath, "INSERT INTO people(nconst, name) VALUES (1, 'Lana'), (16, 'Bryan'), (555, 'Unknown')");
        ImdbFixture.Run(lib.DbPath, "INSERT INTO people(nconst, name, prof) VALUES (2, 'Lilly', 'kept')");
        lib.FillPeopleBasics();
        Assert.Equal("1|1965|NULL|director,writer,producer\n2|NULL|NULL|kept\n16|1956|NULL|actor\n555|NULL|NULL|",
            ImdbFixture.Dump(ImdbFixture.Rows(lib.DbPath, "SELECT nconst, birth, death, prof FROM people ORDER BY nconst")));
        File.Delete(Path.Combine(lib.ImdbDir, "name.basics.tsv.gz"));
        ImdbFixture.Run(lib.DbPath, "INSERT INTO people(nconst, name) VALUES (7, 'New')");
        lib.FillPeopleBasics(); // no file: nothing happens
        Assert.Equal("NULL", ImdbFixture.Dump(ImdbFixture.Rows(lib.DbPath, "SELECT prof FROM people WHERE nconst=7")));
    }

    [Fact]
    public void EnsureIndexResetsTheMatcherAndTheMatcherSeesTheNewIndex()
    {
        using var fx = new ImdbFixture();
        using var lib = NewLib(fx);
        CopyData(lib, fx);
        lib.EnsureIndex();
        using (var ix = lib.Matcher.Open())
        {
            using var c = ix.CreateCommand();
            c.CommandText = "SELECT count(*) FROM titles";
            Assert.Equal(5L, c.ExecuteScalar());
        }
        // the connection above went back to the pool; rebuilding must still be able to replace the file
        ImdbFixture.Gz(Path.Combine(lib.ImdbDir, "title.basics.tsv.gz"), "h\n" + string.Join("\n", ImdbFixture.Texts["title.basics"].Split('\n').Skip(1).Take(2)) + "\n");
        ImdbIndex.BuildIndex(lib.ImdbDir, lib.IndexPath, (s, d, t) => { });
        lib.Matcher.Reset();
        using (var ix = lib.Matcher.Open())
        {
            using var c = ix.CreateCommand();
            c.CommandText = "SELECT count(*) FROM titles";
            Assert.Equal(2L, c.ExecuteScalar());
        }
    }
}

public class FaTitlesTests
{
    sealed class Case { public string? @in { get; set; } public string? @out { get; set; } }

    [Fact]
    public void NormalizesLikeThePythonVersion()
    {
        var all = JsonSerializer.Deserialize<List<Case>>(File.ReadAllText("fa_parity.json"))!;
        Assert.True(all.Count > 100);
        var bad = all.Where(c => FaTitles.Normalize(c.@in) != c.@out).Select(c => $"{c.@in} want {c.@out} got {FaTitles.Normalize(c.@in)}").ToList();
        Assert.True(bad.Count == 0, $"{bad.Count} of {all.Count} differ:\n" + string.Join("\n", bad.Take(15)));
    }

    [Fact]
    public void NormalizeCases()
    {
        Assert.Null(FaTitles.Normalize(null));
        Assert.Null(FaTitles.Normalize("   "));
        Assert.Equal("Hello", FaTitles.Normalize("  Hello  ")); // no Persian: only trimmed
        Assert.Equal("\u0633\u0644\u0627\u0645\u060c \u062f\u0646\u06cc\u0627\u061b \u0686\u0637\u0648\u0631\u06cc\u061f", FaTitles.Normalize("\u0633\u0644\u0627\u0645 ,\u062f\u0646\u06cc\u0627 ;\u0686\u0637\u0648\u0631\u06cc ?"));
        Assert.Equal("\u0641\u06cc\u0644\u0645 \u06f1\u06f2\u06f3 \u06f4\u06f5\u06f6", FaTitles.Normalize("\u0641\u06cc\u0644\u0645 123 \u0664\u0665\u0666")); // digits become Persian digits
        Assert.Equal("\u0641\u06cc\u0644\u0645 The 123", FaTitles.Normalize("\u0641\u06cc\u0644\u0645 The 123")); // a Latin word keeps ASCII digits
        Assert.Equal("\u06cc\u0627\u062f \u06a9\u0631\u062f", FaTitles.Normalize("\u064a\u0627\u062f \u0643\u0631\u062f")); // Arabic yeh / kaf
        Assert.Equal("\u062e\u0627\u0646\u0647\u200c\u0647\u0627", FaTitles.Normalize("\u062e\u0627\u0646\u0647 \u0647\u0627")); // half-space before the plural
        Assert.Equal("\u0646\u0645\u06cc\u200c\u062e\u0648\u0627\u0647\u0645", FaTitles.Normalize("\u0646\u0645\u06cc \u062e\u0648\u0627\u0647\u0645"));
        Assert.Equal("\u0633\u0644\u0627\u0645: \u062f\u0646\u06cc\u0627", FaTitles.Normalize("\u0633\u0644\u0627\u0645  :\u062f\u0646\u06cc\u0627"));
        Assert.Equal("\u0633\u0644\u0627\u0645", FaTitles.Normalize(" - \u0633\u0644\u0627\u0645 : "));
    }

    [Fact]
    public void LatinAndPersianTests()
    {
        Assert.True(FaTitles.HasPersian("abc \u0633"));
        Assert.False(FaTitles.HasPersian("abc"));
        Assert.False(FaTitles.HasPersian(null));
        Assert.True(FaTitles.IsLatinOnly("Serghate Bozorge Ghatar"));
        Assert.False(FaTitles.IsLatinOnly("\u0633\u0644\u0627\u0645 abc"));
        Assert.False(FaTitles.IsLatinOnly("123"));
        Assert.False(FaTitles.IsLatinOnly(""));
        Assert.False(FaTitles.IsLatinOnly(null));
    }

    [Fact]
    public void ReviewedListIsEmbedded()
    {
        var r = FaTitles.Reviewed();
        Assert.True(r.Count > 100);
        Assert.Equal("\u0633\u0631\u0642\u062a \u0628\u0632\u0631\u06af \u0642\u0637\u0627\u0631", r[439]);
    }

    [Fact]
    public void ApplyRespellsAddsReviewedTitlesKeepsUserEditsAndRunsEveryTime()
    {
        using var fx = new ImdbFixture(writeFiles: false);
        using var lib = new Library(Path.Combine(fx.Dir, "root"));
        File.WriteAllText(lib.SettingsPath, "{\n  \"list_dir\": \"x\",\n  \"mystery\": 5\n}");
        ImdbFixture.Run(lib.DbPath, "INSERT INTO titles(tconst, type, title, fa_title) VALUES (439,'movie','The Great Train Robbery',$p0), (1,'movie','a',$p1), (2,'movie','b',NULL), (13442,'movie','Nosferatu',NULL)",
            "\u064a\u0627\u062f", "\u0633\u0644\u0627\u0645 \u0647\u0627");
        ImdbFixture.Run(lib.DbPath, "INSERT INTO fa_titles(tconst, title, source) VALUES (13442, 'my own', 'user')");
        var (cleaned, replaced) = lib.ApplyFaTitles();
        Assert.Equal(2, cleaned);
        Assert.Equal(1, replaced); // 13442 keeps the user's edit
        Assert.Equal("1|\u0633\u0644\u0627\u0645\u200c\u0647\u0627\n439|\u06cc\u0627\u062f", ImdbFixture.Dump(ImdbFixture.Rows(lib.DbPath, "SELECT tconst, fa_title FROM titles WHERE fa_title IS NOT NULL ORDER BY tconst")));
        Assert.Equal("439|\u0633\u0631\u0642\u062a \u0628\u0632\u0631\u06af \u0642\u0637\u0627\u0631|review\n13442|my own|user", ImdbFixture.Dump(ImdbFixture.Rows(lib.DbPath, "SELECT * FROM fa_titles ORDER BY tconst")));
        var json = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(File.ReadAllText(lib.SettingsPath))!;
        Assert.False(json.ContainsKey("fa_review_version")); // like Python: nothing is saved, so the next update applies it again
        Assert.Equal(5, json["mystery"].GetInt32());
        Assert.Equal("x", json["list_dir"].GetString());
        Assert.Equal((0, 0), FaTitles.Apply(lib)); // nothing left to do

        // a title stored later is respelled by the next run, a key saved by an older build changes nothing and goes
        File.WriteAllText(lib.SettingsPath, "{\"fa_review_version\": 2, \"mystery\": 5}");
        ImdbFixture.Run(lib.DbPath, "UPDATE titles SET fa_title=$p0 WHERE tconst=2", "\u0643\u062a\u0627\u0628");
        ImdbFixture.Run(lib.DbPath, "INSERT INTO titles(tconst, type, title) VALUES (10, 'movie', 'new')");
        Assert.Equal((1, 0), lib.ApplyFaTitles());
        Assert.DoesNotContain("fa_review_version", File.ReadAllText(lib.SettingsPath));
        Assert.Contains("mystery", File.ReadAllText(lib.SettingsPath));
    }

    [Fact]
    public void ApplyWithoutASettingsFileDoesNotCreateIt()
    {
        using var fx = new ImdbFixture(writeFiles: false);
        using var lib = new Library(Path.Combine(fx.Dir, "root"));
        lib.ApplyFaTitles();
        Assert.False(File.Exists(lib.SettingsPath));
        Assert.True(lib.FirstRun);
    }

    [Fact]
    public void SetFaTitleStoresAnOverrideAndRemovesIt()
    {
        using var fx = new ImdbFixture(writeFiles: false);
        using var lib = new Library(Path.Combine(fx.Dir, "root"));
        int v = lib.Version;
        lib.SetFaTitle(5, "\u0633\u0644\u0627\u0645 \u0647\u0627");
        Assert.Equal("5|\u0633\u0644\u0627\u0645\u200c\u0647\u0627|user", ImdbFixture.Dump(ImdbFixture.Rows(lib.DbPath, "SELECT * FROM fa_titles")));
        lib.SetFaTitle(5, "  ");   // hides the Persian title
        Assert.Equal("5||user", ImdbFixture.Dump(ImdbFixture.Rows(lib.DbPath, "SELECT * FROM fa_titles")));
        lib.SetFaTitle(5, null);
        Assert.Empty(ImdbFixture.Rows(lib.DbPath, "SELECT * FROM fa_titles"));
        Assert.Equal(v + 3, lib.Version);
    }
}
