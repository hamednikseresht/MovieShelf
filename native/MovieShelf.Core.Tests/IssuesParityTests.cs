using System.Globalization;
using System.Text.Json;
using MovieShelf.Core;
using Xunit;

namespace MovieShelf.Core.Tests;

/// <summary>Compares the Problems report with what the Python app says on the same database. pyfixtures/issues.db holds one case for every branch
/// of movieshelf/issues.py and pyfixtures/issues.json the Python output; make_issues_fixture.py writes both.</summary>
public class IssuesParity : IDisposable
{
    static readonly string Dir = Path.Combine(AppContext.BaseDirectory, "pyfixtures");
    static readonly JsonElement Fixture = JsonDocument.Parse(File.ReadAllText(Path.Combine(Dir, "issues.json"))).RootElement;

    readonly string root = Path.Combine(Path.GetTempPath(), "movieshelf-issues-" + Guid.NewGuid().ToString("N"));
    readonly Library lib;

    public IssuesParity()
    {
        Directory.CreateDirectory(Path.Combine(root, "data"));
        File.Copy(Path.Combine(Dir, "issues.db"), Path.Combine(root, "data", "library.db"));
        lib = new Library(root);
    }

    public void Dispose()
    {
        lib.Dispose();
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { Directory.Delete(root, true); } catch (IOException) { }
    }

    static string N(double? d) => d?.ToString("R", CultureInfo.InvariantCulture) ?? "null";

    static string Dump(IssueRow r) => string.Join("\n", new[]
    {
        $"ikey {r.IKey}", $"k {r.K}", $"tt {r.Tt}", $"title {r.Title}", $"year {r.Year}", $"rating {N(r.Rating)}", $"votes {r.Votes}", $"note {r.Note}", $"sub {r.Sub}",
        $"wasted {r.Wasted}", $"ignored {r.Ignored}", $"manual {r.Manual}", $"sim {N(r.Sim)}", "junk_ids " + string.Join(",", r.JunkIds ?? new()),
    }.Concat(r.Files.Select(f => $"file {f.Id}|{f.Raw}|{f.Quality}|{f.Verdict}|{f.Size}|{string.Join(";", f.Paths)}"))
     .Concat(r.Junk.Select(j => $"junk {j.Id}|{j.Raw}|{j.Path}|{j.NFiles}|{j.Size}|{string.Join(";", j.Names)}")));

    static string? Text(JsonElement e, string name) => e.TryGetProperty(name, out var v) && v.ValueKind != JsonValueKind.Null ? v.ToString() : null;

    static string Dump(JsonElement r)
    {
        double? Dbl(string name) => r.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetDouble() : null;
        bool Flag(string name) => r.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.True;
        return string.Join("\n", new[]
        {
            $"ikey {Text(r, "ikey")}", $"k {Text(r, "k")}", $"tt {Text(r, "tt")}", $"title {Text(r, "title")}", $"year {Text(r, "year")}", $"rating {N(Dbl("rating"))}",
            $"votes {Text(r, "votes")}", $"note {Text(r, "note")}", $"sub {Text(r, "sub")}", $"wasted {Text(r, "wasted")}", $"ignored {Flag("ignored")}", $"manual {Flag("manual")}",
            $"sim {N(Dbl("sim") ?? 0)}", "junk_ids " + (r.TryGetProperty("junk_ids", out var ids) ? string.Join(",", ids.EnumerateArray()) : ""),
        }.Concat(r.GetProperty("files").EnumerateArray().Select(f =>
            $"file {Text(f, "id")}|{Text(f, "raw")}|{Text(f, "quality")}|{Text(f, "verdict")}|{Text(f, "size")}|{string.Join(";", f.GetProperty("paths").EnumerateArray())}"))
         .Concat((r.TryGetProperty("junk", out var junk) ? junk.EnumerateArray() : default).Select(j =>
            $"junk {Text(j, "id")}|{Text(j, "raw")}|{Text(j, "path")}|{Text(j, "nfiles")}|{Text(j, "size")}|{string.Join(";", j.GetProperty("names").EnumerateArray())}")));
    }

    static void Same(string kind, IssueReport report, JsonElement expected)
    {
        var want = expected.GetProperty(kind).EnumerateArray().Select(Dump).ToList();
        var got = report.Kinds[kind].Select(Dump).ToList();
        var wrong = new List<string>();
        for (int i = 0; i < Math.Max(want.Count, got.Count); i++)
        {
            string w = i < want.Count ? want[i] : "(nothing)", g = i < got.Count ? got[i] : "(nothing)";
            if (w != g) wrong.Add($"row {i}\n--- Python\n{w}\n--- native\n{g}");
        }
        Assert.True(wrong.Count == 0, $"{kind}: {wrong.Count} of {want.Count} rows differ\n" + string.Join("\n", wrong.Take(3)));
    }

    /// <summary>Problem lists added in the Windows app after this fixture was made; the Python app does not have them (yet).</summary>
    static readonly string[] NativeOnly = { "nodata", "samename" };

    public static TheoryData<string> Kinds()
    {
        var d = new TheoryData<string>();
        foreach (var k in Issues.Kinds.Except(NativeOnly)) d.Add(k);
        return d;
    }

    [Theory]
    [MemberData(nameof(Kinds))]
    public void EveryKindMatchesPython(string kind)
    {
        var expected = Fixture.GetProperty("report");
        Assert.NotEmpty(expected.GetProperty(kind).EnumerateArray());
        Same(kind, Issues.Build(lib), expected);
    }

    [Fact]
    public void FixtureReachesEveryDuplicateKind()
    {
        var subs = Issues.Build(lib).Kinds["dupes"].Select(r => r.Sub).ToHashSet();
        foreach (var s in new[] { "same", "lower", "versions", "mixed", "copies", "series" }) Assert.Contains(s, subs);
    }

    [Fact]
    public void TotalsMatchPython()
    {
        var report = Issues.Build(lib);
        var want = Fixture.GetProperty("report");
        Assert.Equal(want.GetProperty("ignored").GetInt32(), report.Ignored);
        Assert.Equal(want.GetProperty("wasted").GetInt64(), report.Wasted);
    }

    [Fact]
    public void BadgeCountsMatchPythonAndLeaveSeriesOut()
    {
        var want = Fixture.GetProperty("counts");
        Assert.Equal(want.GetProperty("duplicates").GetInt32(), lib.OpenDuplicates());
        Assert.Equal(want.GetProperty("novideo").GetInt32(), lib.OpenNovideo());
        var stats = lib.GetLibraryStats();
        Assert.Equal(lib.OpenDuplicates(), stats.Duplicates);
        Assert.Equal(lib.OpenNovideo(), stats.Novideo);
        var report = Issues.Build(lib);
        Assert.True(report.Kinds["dupes"].Count(r => !r.Ignored) > stats.Duplicates);
    }

    [Fact]
    public void CsvFilesMatchPython()
    {
        var report = Issues.Build(lib);
        foreach (var p in Fixture.GetProperty("csv").EnumerateObject())
        {
            var part = p.Name.Split('/');
            Assert.Equal(p.Value.GetString(), Issues.ToCsv(report, part[0], part[1]));
        }
    }

    [Fact]
    public void MatchingCsvHasPythonsLayout()
    {
        var p = new MatchProblem
        {
            Raw = "A, B.2000", Reason = "Name is spelled differently on IMDb", Current = new Candidate2 { Tconst = 12, Title = "A \"B\"", Year = 2001 },
            Cands = new() { new Candidate2 { Tconst = 3, Title = "C" }, new Candidate2 { Tconst = 44, Title = "D", Year = 1999 } },
        };
        Assert.Equal("\uFEFFName,Problem,Current IMDb,Current title,Year,Other candidates\r\n"
            + "\"A, B.2000\",Name is spelled differently on IMDb,tt0000012,\"A \"\"B\"\"\",2001,tt0000003 C (None) | tt0000044 D (1999)\r\n", Issues.ToCsv(new[] { p }));
    }

    [Fact]
    public void AreaAndChipCountsFollowTheWebPage()
    {
        var r = Issues.Build(lib);
        Assert.Equal(r.OpenCount("lowq") + r.OpenCount("unknownq") + r.OpenCount("realq"), r.AreaCount("quality"));
        Assert.Equal(new[] { "novideo", "namediff", "yeardiff", "runtime", "fatitle", "nonfilm", "noposter", "nodata", "samename" }.Sum(r.OpenCount), r.AreaCount("other"));
        Assert.Equal(r.OpenDupes().Count, r.SubCount("dupes", "all"));
        Assert.True(r.SubCount("dupes", "series") > 0);
        Assert.DoesNotContain(r.Rows("dupes", "all"), x => x.Sub == "series");
        Assert.All(r.Rows("dupes", "series"), x => Assert.Equal("series", x.Sub));
        Assert.Contains(r.Rows("dupes", "all"), x => x.Ignored);
    }

    [Fact]
    public void CopyButtonsPickTheRightFiles()
    {
        var dupes = Issues.Build(lib).Kinds["dupes"];
        var twice = dupes.Single(r => r.Sub == "copies" && r.Title == "Two Folders");
        Assert.Equal(new[] { @"E:\m\two.mkv", @"F:\m\two.mkv" }, twice.CopyList().OrderBy(x => x));
        var paths = dupes.Single(r => r.Title == "Same Quality");
        Assert.True(paths.HasCopiesToRemove && paths.RemovalsHavePaths);
        Assert.Equal(new[] { @"D:\m\same b.mkv" }, paths.CopyList());
        var names = dupes.Single(r => r.Title == "Lower Source");
        Assert.False(names.RemovalsHavePaths);
        Assert.Equal(new[] { "Lower.Source.2000.1080p.WEBRip.mkv" }, names.CopyList());
        Assert.False(dupes.Single(r => r.Sub == "mixed" && r.Title == "Star Wars").HasCopiesToRemove);
    }

    [Fact]
    public void FixMatchAndRemoveTargetTheRightEntries()
    {
        var report = Issues.Build(lib);
        var lower = report.Kinds["dupes"].Single(r => r.Title == "Lower Copy");
        Assert.Equal(lower.Files.First(f => f.Verdict == "lower").Id, lower.FixFileId(true));
        Assert.Equal(lower.Files[0].Id, lower.FixFileId(false));
        Assert.Equal(lower.Files.Select(f => f.Id), lower.RemoveIds);
        var two = report.Kinds["novideo"].Single(r => r.Title == "Two Junk");
        Assert.Equal(two.JunkIds, two.RemoveIds);
    }

    [Fact]
    public void NotesKeepAFormatToTranslate()
    {
        foreach (var r in Issues.Build(lib).Kinds.Values.SelectMany(x => x))
            Assert.Equal(r.Note, string.Format(CultureInfo.InvariantCulture, r.NoteFormat, r.NoteArgs));
    }

    [Fact]
    public void DismissingASeriesChoiceMovesTheRow()
    {
        var report = Issues.Build(lib);
        var row = report.Kinds["dupes"].Single(r => r.Title == "Lower Copy");
        lib.Dismiss("series:" + row.K);
        var again = Issues.Build(lib).Kinds["dupes"].Where(r => r.K == row.K).ToList();
        var series = Assert.Single(again);
        Assert.Equal("series", series.Sub);
        Assert.True(series.Manual);
        lib.Dismiss("series:" + row.K, undo: true);
        Assert.NotEqual("series", Issues.Build(lib).Kinds["dupes"].Single(r => r.K == row.K).Sub);
    }

    [Fact]
    public void NoVideoRowIsReadFromAPythonDatabaseWithPaths()
    {
        var row = Issues.Build(lib).Kinds["novideo"].Single(r => r.Title == "Junk Folder");
        Assert.Equal(@"D:\m\Junk.Folder.2000.1080p.mkv", Assert.Single(row.Junk).Path);
        Assert.Equal(8, row.Junk[0].NFiles);
    }

    [Fact]
    public void FolderScanFindsTheSameEmptyFoldersAsPython()
    {
        var scan = Fixture.GetProperty("scan");
        string home = Path.Combine(root, "scan"), movies = Path.Combine(home, "movies"), lists = Path.Combine(home, "lists");
        Directory.CreateDirectory(lists);
        foreach (var e in scan.GetProperty("tree").EnumerateArray())
        {
            string full = Path.Combine(movies, e[0].GetString()!.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(full)!);
            using var f = File.Create(full);
            f.SetLength(e[1].GetInt64());
        }
        foreach (var d in scan.GetProperty("empty").EnumerateArray()) Directory.CreateDirectory(Path.Combine(movies, d.GetString()!));
        File.WriteAllLines(Path.Combine(lists, "a.txt"), scan.GetProperty("lists").EnumerateArray().Select(x => x.GetString()!));
        using var l = new Library(home);
        var s = l.LoadSettings();
        s.ListDir = lists;
        s.MediaDirs = new() { movies };
        l.SaveSettings(s);
        l.Scan();
        l.ScanMedia();

        var got = new List<string>();
        using (var rd = l.Reader())
        using (var c = rd.CreateCommand())
        {
            c.CommandText = "SELECT path, raw, nfiles, size, names FROM novideo ORDER BY path";
            using var r = c.ExecuteReader();
            while (r.Read())
                got.Add($"{Path.GetRelativePath(movies, r.GetString(0))}|{r.GetString(1)}|{r.GetInt32(2)}|{r.GetInt64(3)}|{string.Join(";", JsonSerializer.Deserialize<List<string>>(r.GetString(4))!)}");
        }
        var want = scan.GetProperty("novideo").EnumerateArray()
            .Select(e => $"{e.GetProperty("path").GetString()}|{e.GetProperty("raw").GetString()}|{e.GetProperty("nfiles").GetInt32()}|{e.GetProperty("size").GetInt64()}|{string.Join(";", e.GetProperty("names").EnumerateArray())}").ToList();
        Assert.Equal(want, got);
        Assert.Equal(want.Count, l.GetLibraryStats().Novideo);
    }

    [Fact]
    public void RevealOpensOnlyFoldersTheScanFound()
    {
        Assert.Equal("\"C:\\a b\"", Library.RevealFolderArgs(@"C:\a b\"));
        Assert.Throws<FileNotFoundException>(() => lib.Reveal(@"Z:\no\such\folder"));
        Assert.Throws<FileNotFoundException>(() => lib.Reveal(@"D:\m\Empty.Folder.2000.1080p"));    // known to the table, gone from the disk
    }

    /// <summary>Set MOVIESHELF_PARITY_DB and MOVIESHELF_PARITY_JSON to compare a real library with a dump of issues.build() of the same data.</summary>
    [Fact]
    public void RealLibraryMatchesPythonWhenGiven()
    {
        string? dbPath = Environment.GetEnvironmentVariable("MOVIESHELF_PARITY_DB"), json = Environment.GetEnvironmentVariable("MOVIESHELF_PARITY_JSON");
        if (string.IsNullOrEmpty(dbPath) || string.IsNullOrEmpty(json)) return;
        string home = Path.Combine(root, "real");
        Directory.CreateDirectory(Path.Combine(home, "data"));
        File.Copy(dbPath, Path.Combine(home, "data", "library.db"));
        using var l = new Library(home);
        var expected = JsonDocument.Parse(File.ReadAllText(json)).RootElement;
        var report = Issues.Build(l);
        foreach (var k in Issues.Kinds.Except(NativeOnly)) Same(k, report, expected);
    }
}
