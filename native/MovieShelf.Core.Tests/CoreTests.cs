using System.Text.Json;
using MovieShelf.Core;
using Microsoft.Data.Sqlite;
using Xunit;

namespace MovieShelf.Core.Tests;

public class ParserParity
{
    sealed class Expected
    {
        public string raw { get; set; } = "";
        public string title { get; set; } = "";
        public string? alt { get; set; }
        public int? year { get; set; }
        public string? res { get; set; }
        public string? src { get; set; }
        public string? codec { get; set; }
        public bool b10 { get; set; }
        public bool hdr { get; set; }
        public string? audio { get; set; }
        public bool fa { get; set; }
        public bool dual { get; set; }
        public bool sub { get; set; }
        public string? ed { get; set; }
        public string? grp { get; set; }
        public string norm { get; set; } = "";
        public List<string> keys { get; set; } = new();
    }

    [Fact]
    public void ParsesLikeThePythonParser()
    {
        var all = JsonSerializer.Deserialize<List<Expected>>(File.ReadAllText("parity.json"))!;
        var bad = new List<string>();
        foreach (var e in all)
        {
            var p = NameParser.Parse(e.raw);
            string got = string.Join("|", p.Title, p.AltTitle, p.Year, p.Resolution, p.Source, p.Codec, p.Bit10, p.Hdr, p.Audio, p.FarsiDubbed, p.DualAudio, p.Softsub, p.Edition, p.Group,
                TitleKeys.Norm(p.Title), string.Join(",", TitleKeys.QueryKeys(p.Title)));
            string want = string.Join("|", e.title, e.alt, e.year, e.res, e.src, e.codec, e.b10, e.hdr, e.audio, e.fa, e.dual, e.sub, e.ed, e.grp, e.norm, string.Join(",", e.keys));
            if (got != want) bad.Add($"{e.raw}\n   want {want}\n   got  {got}");
        }
        Assert.True(bad.Count == 0, $"{bad.Count} of {all.Count} differ:\n" + string.Join("\n", bad.Take(15)));
    }
}

public class MatcherParity
{
    sealed class Expected
    {
        public string raw { get; set; } = "";
        public int? tc { get; set; }
        public string conf { get; set; } = "";
    }

    [Fact]
    public void PicksTheSameTitlesAsThePythonMatcher()
    {
        string index = Path.Combine(Library.DefaultRoot(), "data", "imdb_index.db");
        if (!File.Exists(index)) return; // needs the real IMDb index of a MovieShelf install
        var all = JsonSerializer.Deserialize<List<Expected>>(File.ReadAllText("match_parity.json"))!;
        var matcher = new Matcher(index);
        using var ix = matcher.Open();
        var bad = new List<string>();
        foreach (var e in all)
        {
            var (tc, conf, _) = matcher.Pick(ix, MatchInput.From(NameParser.Parse(e.raw)));
            if (tc != e.tc || conf != e.conf) bad.Add($"{e.raw}: want {e.tc}/{e.conf}, got {tc}/{conf}");
        }
        Assert.True(bad.Count == 0, $"{bad.Count} of {all.Count} differ:\n" + string.Join("\n", bad.Take(15)));
    }
}

public class FuzzTests
{
    [Fact]
    public void RatioMatchesIndelSimilarity()
    {
        Assert.Equal(100, Fuzz.Ratio("heat", "heat"));
        Assert.Equal(0, Fuzz.Ratio("abc", "xyz"));
        // rapidfuzz.fuzz.ratio('godfather', 'thegodfather') = 85.7142...
        Assert.Equal(85.714, Fuzz.Ratio("godfather", "thegodfather"), 2);
    }

    [Fact]
    public void ExtractAgreesWithRatio()
    {
        var rnd = new Random(7);
        string Word(int n) => new string(Enumerable.Range(0, n).Select(_ => "abcdeé آب"[rnd.Next(9)]).ToArray());
        var choices = Enumerable.Range(0, 400).Select(_ => Word(rnd.Next(1, 40))).ToList();
        foreach (int lq in new[] { 4, 9, 30, 64, 65, 90 })
        {
            string q = Word(lq);
            var expected = choices.Select((c, i) => (Index: i, Score: Fuzz.Ratio(q, c))).Where(x => x.Score >= 40)
                .OrderByDescending(x => x.Score).ThenBy(x => x.Index).Take(1000).ToList();
            var got = Fuzz.Extract(q, choices, 1000, 40);
            Assert.Equal(expected.Count, got.Count);
            for (int i = 0; i < got.Count; i++)
            {
                Assert.Equal(expected[i].Index, got[i].Index);
                Assert.Equal(expected[i].Score, got[i].Score, 6);
            }
        }
    }
}

public class LibraryTests : IDisposable
{
    readonly string root = Path.Combine(Path.GetTempPath(), "movieshelf-test-" + Guid.NewGuid().ToString("N"));

    public LibraryTests() => Directory.CreateDirectory(Path.Combine(root, "data"));

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try { Directory.Delete(root, true); } catch (IOException) { }
    }

    void MakeIndex(string path)
    {
        using var c = new SqliteConnection($"Data Source={path}");
        c.Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = @"
CREATE TABLE titles(id INTEGER PRIMARY KEY, type INT, title TEXT, orig TEXT, year INT, endyear INT, runtime INT, genres TEXT, rating REAL, votes INT);
CREATE TABLE keys(k TEXT, id INT, src INT);
CREATE INDEX keys_k ON keys(k);
CREATE TABLE akas(id INT, title TEXT, region TEXT, lang TEXT, types TEXT);
CREATE TABLE meta(k TEXT PRIMARY KEY, v TEXT);
INSERT INTO titles VALUES (1, 0, 'The Godfather', 'The Godfather', 1972, NULL, 175, 'Crime,Drama', 9.2, 2000000);
INSERT INTO titles VALUES (2, 0, 'Heat', 'Heat', 1995, NULL, 170, 'Crime', 8.3, 700000);
INSERT INTO titles VALUES (3, 7, 'Breaking Bad', 'Breaking Bad', 2008, 2013, 49, 'Drama', 9.5, 2200000);
INSERT INTO keys VALUES ('thegodfather', 1, 0), ('godfather', 1, 0), ('heat', 2, 0), ('breakingbad', 3, 0);
INSERT INTO akas VALUES (1, 'Der Pate', 'DE', 'de', NULL);";
        cmd.ExecuteNonQuery();
    }

    Library NewLibrary(params string[] lines)
    {
        string lists = Path.Combine(root, "lists");
        Directory.CreateDirectory(lists);
        File.WriteAllLines(Path.Combine(lists, "a.txt"), lines);
        MakeIndex(Path.Combine(root, "data", "imdb_index.db"));
        var lib = new Library(root);
        lib.SaveSettings(new Settings { ListDir = lists });
        return lib;
    }

    [Fact]
    public void ScanMatchAndLoad()
    {
        using var lib = NewLibrary("The.Godfather.1972.1080p.BluRay", "Heat.1995.720p", "Breaking.Bad.2008.1080p", "Nothing.Like.This.2001.720p");
        lib.Update();
        var movies = lib.LoadMovies();
        Assert.Equal(4, movies.Count);
        var godfather = movies.Single(m => m.K == "tt0000001");
        Assert.Equal("exact", godfather.Conf);
        Assert.Equal("1080p", godfather.Res);
        Assert.False(godfather.IsSeries);
        var bb = movies.Single(m => m.K == "tt0000003");
        Assert.True(bb.IsSeries);
        Assert.Equal("2008–2013", bb.YearSpan);
        Assert.Equal("none", movies.Single(m => m.K.StartsWith("f")).Conf);
        Assert.Single(lib.MatchProblems());
    }

    [Fact]
    public void ArticleDroppedNameIsFlaggedAsNameDifference()
    {
        using var lib = NewLibrary("Godfather.1972.1080p.BluRay");
        lib.Update();
        var report = Issues.Build(lib);
        var row = Assert.Single(report.Kinds["namediff"]);
        Assert.Equal("close", row.Sub);
        Assert.Contains("Godfather", row.Note);
    }

    [Fact]
    public void UserDataAndFixMatchSurviveReload()
    {
        using var lib = NewLibrary("Heat.1995.720p", "Unknown.Thing.1999.720p");
        lib.Update();
        lib.SetUser(new[] { "tt0000002" }, fav: true, watched: true, rating: 9);
        var heat = lib.LoadMovies().Single(m => m.K == "tt0000002");
        Assert.True(heat.Fav);
        Assert.True(heat.Watched);
        Assert.Equal(9, heat.MyRating);
        int fileId = lib.MatchProblems().Single().FileId;
        lib.SetMatch(fileId, 1);
        Assert.Empty(lib.MatchProblems());
        Assert.Equal("manual", lib.LoadMovies().Single(m => m.K == "tt0000001").Conf);
    }

    [Fact]
    public void SearchLanguage()
    {
        using var lib = NewLibrary("The.Godfather.1972.1080p.BluRay", "Heat.1995.720p");
        lib.Update();
        var movies = lib.LoadMovies();
        Assert.Single(movies.Where(SearchQuery.Parse("godfather").Test));
        Assert.Single(movies.Where(SearchQuery.Parse("year:1990-1999").Test));
        Assert.Single(movies.Where(SearchQuery.Parse("rating:9").Test));
        Assert.Single(movies.Where(SearchQuery.Parse("-heat").Test));
        Assert.Single(movies.Where(SearchQuery.Parse("res:720p").Test));
    }

    [Fact]
    public void ExportWritesCsvAndXlsx()
    {
        using var lib = NewLibrary("Heat.1995.720p");
        lib.Update();
        var cols = Exporter.Columns.Where(c => c.Default).ToList();
        var rows = Exporter.BuildRows(lib.LoadMovies(), cols, false);
        string csv = Path.Combine(root, "o.csv"), xlsx = Path.Combine(root, "o.xlsx");
        Exporter.WriteCsv(csv, cols, rows);
        Exporter.WriteXlsx(xlsx, cols, rows);
        Assert.Contains("Heat", File.ReadAllText(csv));
        using var zip = System.IO.Compression.ZipFile.OpenRead(xlsx);
        Assert.NotNull(zip.GetEntry("xl/worksheets/sheet1.xml"));
    }
}
