using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Xml;
using MovieShelf.Core;
using Xunit;

namespace MovieShelf.Core.Tests;

/// <summary>The export against files and values written by exporter.py; see pyfixtures/make_export_fixture.py.</summary>
public sealed class ExportParity : IDisposable
{
    static readonly string Dir = Path.Combine(AppContext.BaseDirectory, "pyfixtures");
    static readonly JsonDocument Fx = JsonDocument.Parse(File.ReadAllText(Path.Combine(Dir, "export_fixture.json")));
    static JsonElement Root => Fx.RootElement;

    readonly string tmp = Path.Combine(Path.GetTempPath(), "mdb-export-" + Guid.NewGuid().ToString("N"));

    public ExportParity() => Directory.CreateDirectory(tmp);

    public void Dispose() => Directory.Delete(tmp, true);

    static string? Str(JsonElement e, string k) => e.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
    static int? Int(JsonElement e, string k) => e.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetInt32() : null;
    static long? Long(JsonElement e, string k) => e.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetInt64() : null;
    static double? Dbl(JsonElement e, string k) => e.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetDouble() : null;
    static bool Flag(JsonElement e, string k) => (Int(e, k) ?? 0) != 0;
    static string[] Strs(JsonElement e, string k) => e.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.Array ? v.EnumerateArray().Select(x => x.GetString()!).ToArray() : Array.Empty<string>();
    static string Json(JsonElement e, string k) => e.TryGetProperty(k, out var v) ? v.GetRawText() : "[]";

    static MovieFile ToFile(JsonElement f) => new()
    {
        Id = Int(f, "id") ?? 0, Raw = Str(f, "raw") ?? "", Res = Str(f, "res"), Src = Str(f, "src"), Codec = Str(f, "codec"), Audio = Str(f, "audio"),
        Ed = Str(f, "ed"), Grp = Str(f, "grp"), Conf = Str(f, "conf"), List = Str(f, "list"), B10 = Flag(f, "b10"), Hdr = Flag(f, "hdr"), Fa = Flag(f, "fa"),
        Dual = Flag(f, "dual"), Sub = Flag(f, "sub"), Size = Long(f, "size"), Paths = Strs(f, "paths").ToList(), Dur = Int(f, "dur"), W = Int(f, "w"), H = Int(f, "h"),
        ALang = Strs(f, "alang"), SLang = Strs(f, "slang"),
    };

    static Movie ToMovie(JsonElement e)
    {
        string k = Str(e, "k")!;
        var m = new Movie
        {
            K = k, Tconst = e.TryGetProperty("tt", out _) ? int.Parse(k[2..]) : null, Type = Str(e, "type"), Title = Str(e, "title") ?? "", En = Str(e, "en"),
            Orig = Str(e, "orig"), FaTitle = Str(e, "fa_t"), Year = Int(e, "year"), Runtime = Int(e, "runtime"), Genres = Strs(e, "genres"), Countries = Strs(e, "countries"),
            Langs = Strs(e, "langs"), Collection = Str(e, "coll"), Plot = Str(e, "plot"), Tagline = Str(e, "tagline"), Rating = Dbl(e, "rating"), Votes = Int(e, "votes") ?? 0,
            Rt = Int(e, "rt"), Mc = Int(e, "mc"), Dir = Movie.ParsePeople(Json(e, "dir")), Wri = Movie.ParsePeople(Json(e, "wri")), Cast = Movie.ParsePeople(Json(e, "cast")),
            Res = Str(e, "res"), Conf = Str(e, "conf") ?? "none", Fav = Flag(e, "fav"), Watched = Flag(e, "watched"), MyRating = Int(e, "myrating"), Notes = Str(e, "notes"),
            Files = e.GetProperty("files").EnumerateArray().Select(ToFile).ToList(),
        };
        m.Prepare();
        return m;
    }

    static List<Movie> Movies() => Root.GetProperty("movies").EnumerateArray().Select(ToMovie).ToList();

    static ExportRequest Request(string name, List<Movie> all)
    {
        var r = Root.GetProperty("requests").GetProperty(name);
        var pick = (IEnumerable<string> keys) => keys.Select(k => all.First(m => m.K == k)).ToList();
        var req = new ExportRequest
        {
            Movies = all, Columns = Strs(r, "columns"), PerFile = Str(r, "rows") == "file", Format = Str(r, "format") ?? "xlsx", Now = new DateTime(2026, 1, 2, 3, 4, 0),
        };
        if (r.TryGetProperty("info", out var info))
        {
            req.Scope = Str(info, "scope") ?? "";
            req.Filters = Strs(info, "filters");
        }
        if (r.TryGetProperty("sheets", out var sheets))
            req.Sheets = sheets.EnumerateArray().Select(s => new ExportSheet(Str(s, "name") ?? "", pick(Strs(s, "keys")))).ToList();
        return req;
    }

    static string Show(object? v) => v switch
    {
        null => "null",
        string s => "s:" + s,
        double d => "n:" + d.ToString("R", System.Globalization.CultureInfo.InvariantCulture),
        _ => "n:" + Convert.ToDouble(v).ToString("R", System.Globalization.CultureInfo.InvariantCulture),
    };

    static string Show(JsonElement v) => v.ValueKind switch
    {
        JsonValueKind.Null => "null",
        JsonValueKind.String => "s:" + v.GetString(),
        _ => "n:" + v.GetDouble().ToString("R", System.Globalization.CultureInfo.InvariantCulture),
    };

    [Fact]
    public void ColumnTableIsTheSameAsPython()
    {
        var want = Root.GetProperty("columns").EnumerateArray().ToList();
        var widths = Root.GetProperty("widths").EnumerateArray().Select(x => x.GetInt32()).ToList();
        var kinds = Root.GetProperty("kinds").EnumerateArray().Select(x => x.GetString()).ToList();
        Assert.Equal(47, want.Count);
        Assert.Equal(want.Count, Exporter.Columns.Length);
        for (int i = 0; i < want.Count; i++)
        {
            var c = Exporter.Columns[i];
            Assert.Equal(Str(want[i], "key"), c.Key);
            Assert.Equal(Str(want[i], "label"), c.Label);
            Assert.Equal(Str(want[i], "group"), c.Group);
            Assert.Equal(want[i].GetProperty("default").GetBoolean(), c.Default);
            Assert.Equal(widths[i], c.Width);
            Assert.Equal(kinds[i], c.Kind);
        }
    }

    [Theory]
    [InlineData("rows_movie", false)]
    [InlineData("rows_file", true)]
    public void CellValuesAreTheSameAsPython(string fixture, bool perFile)
    {
        var want = Root.GetProperty(fixture).EnumerateArray().Select(r => r.EnumerateArray().ToList()).ToList();
        var got = Exporter.BuildRows(Movies(), Exporter.Columns, perFile).ToList();
        Assert.Equal(want.Count, got.Count);
        var bad = new List<string>();
        for (int r = 0; r < want.Count; r++)
            for (int c = 0; c < Exporter.Columns.Length; c++)
                if (Show(want[r][c]) != Show(got[r][c])) bad.Add($"row {r} {Exporter.Columns[c].Key}: want {Show(want[r][c])} got {Show(got[r][c])}");
        Assert.True(bad.Count == 0, string.Join("\n", bad.Take(20)));
    }

    [Fact]
    public void SizesAndMinutesRoundLikePython()
    {
        var size = Exporter.ByKey["size"];
        foreach (var p in Root.GetProperty("sizes").EnumerateArray())
        {
            long n = p[0].GetInt64();
            Assert.Equal(p[1].GetDouble(), size.Get(new Movie(), new MovieFile { Size = n }));
        }
        var dur = Exporter.ByKey["dur"];
        foreach (var p in Root.GetProperty("minutes").EnumerateArray())
        {
            object? want = p[1].ValueKind == JsonValueKind.Null ? null : p[1].GetInt64();
            Assert.Equal(want, dur.Get(new Movie(), new MovieFile { Dur = p[0].GetInt32() }));
        }
    }

    [Fact]
    public void SheetNamesFollowPython()
    {
        var p = Root.GetProperty("sheet_names");
        Assert.Equal(p[1].EnumerateArray().Select(x => x.GetString()!), Exporter.SheetNames(p[0].EnumerateArray().Select(x => x.GetString()!)));
    }

    [Theory]
    [InlineData("default_movie")]
    [InlineData("all_movie")]
    [InlineData("all_file")]
    [InlineData("sheets")]
    public void WorkbookPartsAreByteForByteThePythonOnes(string name)
    {
        string path = Path.Combine(tmp, name + ".xlsx");
        int rows = Exporter.Build(path, Request(name, Movies()));
        Assert.Equal(Root.GetProperty("requests").GetProperty(name).GetProperty("_rows").GetInt32(), rows);
        using var want = ZipFile.OpenRead(Path.Combine(Dir, $"export_{name}.xlsx"));
        using var got = ZipFile.OpenRead(path);
        Assert.Equal(want.Entries.Select(e => e.FullName), got.Entries.Select(e => e.FullName));
        foreach (var e in want.Entries)
        {
            string a = Read(e), b = Read(got.GetEntry(e.FullName)!);
            Assert.True(a == b, $"{e.FullName} differs at {FirstDiff(a, b)}: {Around(a, b)}");
            new XmlDocument().LoadXml(b);
        }
    }

    [Theory]
    [InlineData("one_column")]
    [InlineData("all_csv")]
    [InlineData("all_file_csv")]
    public void CsvIsByteForByteThePythonOne(string name)
    {
        string path = Path.Combine(tmp, name + ".csv");
        Exporter.Build(path, Request(name, Movies()));
        Assert.Equal(File.ReadAllBytes(Path.Combine(Dir, $"export_{name}.csv")), File.ReadAllBytes(path));
    }

    [Fact]
    public void FileNameIsThePythonOne()
    {
        var now = new DateTime(2026, 1, 2, 3, 4, 0);
        Assert.Equal(Str(Root.GetProperty("requests").GetProperty("all_movie"), "_fname"), Exporter.FileName("xlsx", now));
        Assert.Equal(Str(Root.GetProperty("requests").GetProperty("all_csv"), "_fname"), Exporter.FileName("csv", now));
    }

    [Fact]
    public void GroupsAreTheOnesOfTheWebApp()
    {
        var movies = Movies();
        using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(Dir, "export_groups.json")));
        foreach (var c in doc.RootElement.EnumerateArray())
        {
            string group = c.GetProperty("group").GetString()!, sort = c.GetProperty("sort").GetString()!;
            bool rev = c.GetProperty("rev").GetBoolean();
            var want = c.GetProperty("sheets").EnumerateArray().Select(s => $"{Str(s, "label")}: {string.Join(' ', Strs(s, "keys"))}");
            var got = Exporter.GroupList(movies, group, sort, rev).Select(s => $"{s.Name}: {string.Join(' ', s.Movies.Select(m => m.K))}");
            Assert.Equal(want, got);
        }
    }

    [Fact]
    public void CellsDropWhatXmlCannotCarry()
    {
        var m = new Movie { K = "f1", Title = "a\u0001b￾c\uD83D", Files = { new MovieFile { Raw = "x￿" } } };
        string path = Path.Combine(tmp, "bad.xlsx");
        Exporter.Build(path, new ExportRequest { Movies = new[] { m } });
        using var zip = ZipFile.OpenRead(path);
        string sheet = Read(zip.GetEntry("xl/worksheets/sheet1.xml")!);
        new XmlDocument().LoadXml(sheet);
        Assert.Contains(">abc<", sheet);
    }

    [Fact]
    public void StoppedExportLeavesNoFile()
    {
        var movies = Enumerable.Range(0, 500).Select(i => new Movie { K = $"f{i}", Title = "t", Files = { new MovieFile { Raw = "r" } } }).ToList();
        string path = Path.Combine(tmp, "stopped.xlsx");
        using var stop = new CancellationTokenSource();
        stop.Cancel();
        Assert.Throws<OperationCanceledException>(() => Exporter.Build(path, new ExportRequest { Movies = movies }, null, stop.Token));
        Assert.False(File.Exists(path));
    }

    [Fact]
    public void ProgressCountsRows()
    {
        var movies = Enumerable.Range(0, 450).Select(i => new Movie { K = $"f{i}", Title = "t", Files = { new MovieFile { Raw = "r" } } }).ToList();
        var seen = new List<int>();
        Exporter.Build(Path.Combine(tmp, "p.csv"), new ExportRequest { Movies = movies, Format = "csv" }, new Sync(seen));
        Assert.Equal(new[] { 100, 200, 300, 400 }, seen);
    }

    sealed class Sync(List<int> seen) : IProgress<int>
    {
        public void Report(int value) => seen.Add(value);
    }

    [Fact]
    public void NarrowFiltersLikeTheDialog()
    {
        var all = Movies();
        Assert.Equal(new[] { "tt0133093" }, Exporter.Narrow(all, new ExportFilter(Query: "matrix")).Select(m => m.K));
        Assert.Equal(new[] { "tt0133093", "tt0000002", "f7" }, Exporter.Narrow(all, new ExportFilter(YearMin: 1990, YearMax: 2005)).Select(m => m.K));
        Assert.Equal(new[] { "tt0133093", "tt0000002", "tt0000006" }, Exporter.Narrow(all, new ExportFilter(RatingMin: 8)).Select(m => m.K));
        Assert.Equal(new[] { "tt0000005" }, Exporter.Narrow(all, new ExportFilter(Genre: "Comedy")).Select(m => m.K));
        Assert.Equal(new[] { "tt0133093" }, Exporter.Narrow(all, new ExportFilter(Res: "1080p")).Select(m => m.K));
        Assert.Equal(new[] { "tt0133093" }, Exporter.Narrow(all, new ExportFilter(Status: "fav")).Select(m => m.K));
        Assert.Equal(new[] { "search: x", "year 1990–…", "IMDb rating ≥ 7.5", "genre: Drama", "quality: 720p", "not watched" },
            Exporter.FilterLines(new ExportFilter("x", 1990, 0, 7.5, "Drama", "720p", "unwatched")));
        Assert.Equal(new[] { "year …–2000" }, Exporter.FilterLines(new ExportFilter(YearMax: 2000)));
        Assert.Empty(Exporter.FilterLines(new ExportFilter()));
    }

    [Fact]
    public void SearchTagsAreTheFieldedAndNegatedTerms()
    {
        Assert.Equal(new[] { "year:1999", "-zombie", "genre:\"sci-fi\"" }, Exporter.SearchTags("matrix year:1999 -zombie genre:\"sci-fi\" \"exact phrase\""));
    }

    static string Read(ZipArchiveEntry e)
    {
        using var r = new StreamReader(e.Open(), new UTF8Encoding(false));
        return r.ReadToEnd();
    }

    static int FirstDiff(string a, string b)
    {
        int i = 0;
        while (i < a.Length && i < b.Length && a[i] == b[i]) i++;
        return i;
    }

    static string Around(string a, string b)
    {
        int i = Math.Max(0, FirstDiff(a, b) - 40);
        return $"\nwant {a[i..Math.Min(a.Length, i + 160)]}\ngot  {b[i..Math.Min(b.Length, i + 160)]}";
    }
}
