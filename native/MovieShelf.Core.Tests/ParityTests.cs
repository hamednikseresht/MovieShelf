using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Data.Sqlite;
using Xunit;

namespace MovieShelf.Core.Tests;

/// <summary>A library in a temp folder with a small IMDb index. Nothing here reaches the network or the user's data.</summary>
sealed class TempLibrary : IDisposable
{
    public readonly string Root = Path.Combine(Path.GetTempPath(), "movieshelf-parity-" + Guid.NewGuid().ToString("N"));
    public string Lists => Path.Combine(Root, "lists");
    public string ListFile => Path.Combine(Lists, "a.txt");
    public string Movies => Path.Combine(Root, "movies");
    public readonly FakeHttp Http;

    public TempLibrary(FakeHttp? http = null)
    {
        Http = http ?? new FakeHttp((_, _) => throw new HttpRequestException("offline"));
        Directory.CreateDirectory(Path.Combine(Root, "data"));
        Directory.CreateDirectory(Lists);
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try { Directory.Delete(Root, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    public static void MakeIndex(string path)
    {
        using var c = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Pooling = false }.ToString());
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
INSERT INTO keys VALUES ('thegodfather', 1, 0), ('godfather', 1, 0), ('heat', 2, 0), ('breakingbad', 3, 0);";
        cmd.ExecuteNonQuery();
    }

    /// <summary>Everything online switched off; the list file holds these lines.</summary>
    public Library Open(bool index, params string[] lines)
    {
        File.WriteAllLines(ListFile, lines);
        var lib = new Library(Root) { OnlineHandler = Http, OnlineDelayScale = 0 };
        if (index) MakeIndex(lib.IndexPath);
        lib.SaveSettings(new Settings { ListDir = Lists, OnlinePosters = false, OnlineFallback = false, AutoRefresh = false });
        return lib;
    }

    public static void WaitJob(Library lib)
    {
        var t = DateTime.UtcNow.AddSeconds(30);
        while ((lib.Job.Running || lib.QueuedCount > 0) && DateTime.UtcNow < t) Thread.Sleep(10);
        Assert.False(lib.Job.Running);
    }
}

/// <summary>Holds a job open, so that what is asked for meanwhile has to wait.</summary>
sealed class HeldJob : IDisposable
{
    readonly ManualResetEventSlim gate = new();
    readonly Library lib;

    public HeldJob(Library lib, Action? then = null)
    {
        this.lib = lib;
        Assert.True(lib.Start(() => { gate.Wait(); then?.Invoke(); }));
    }

    public void Release() => gate.Set();

    public void Dispose()
    {
        lib.Stop();
        gate.Set();
        TempLibrary.WaitJob(lib);
    }
}

/// <summary>What the Python code answers, from pyfixtures\core_parity.json (made by make_core_parity.py).</summary>
public class PythonAnswers
{
    internal static readonly string Dir = Path.Combine(AppContext.BaseDirectory, "pyfixtures");
    internal static readonly JsonObject Data = JsonNode.Parse(File.ReadAllText(Path.Combine(Dir, "core_parity.json")))!.AsObject();

    [Fact]
    public void ReasonTextsAreThePythonTexts()
    {
        foreach (var r in Data["reasons"]!.AsArray())
            Assert.Equal((string?)r!["text"], Matcher.Reason((string?)r["conf"], (int?)r["fy"], (int?)r["ty"]));
    }

    [Fact]
    public void SideFilesAreThePythonSideFiles()
    {
        foreach (var r in Data["sidefiles"]!.AsArray())
            Assert.True((bool)r!["side"]! == Checker.IsSideFile((string)r["name"]!), (string?)r["name"]);
    }

    [Fact]
    public void ImageTypesAreThePythonTypes()
    {
        foreach (var r in Data["imagetypes"]!.AsArray())
            Assert.Equal((string?)r!["type"], Library.ImageType(Convert.FromHexString((string)r["hex"]!)));
    }

    [Fact]
    public void AnEmptyAltTitleStaysEmptyText()
    {
        foreach (var r in Data["alt"]!.AsArray())
        {
            var p = NameParser.Parse((string)r!["name"]!);
            Assert.Equal((string?)r["title"], p.Title);
            Assert.Equal((string?)r["alt"], p.AltTitle);
            Assert.Equal((int?)r["year"], p.Year);
        }
    }

    [Fact]
    public void SniffFindsThePythonDialect()
    {
        var bad = new List<string>();
        foreach (var r in Data["sniff"]!.AsArray())
        {
            string line = (string)r!["line"]!;
            var d = ListCsv.Sniff(line);
            var w = r["dialect"]!;
            var want = new CsvDialect(((string)w["delimiter"]!)[0], ((string)w["quote"]!)[0], (bool)w["doublequote"]!, (bool)w["skipspace"]!);
            if (d != want) bad.Add($"{line}: want {want}, got {d}");
        }
        Assert.True(bad.Count == 0, string.Join("\n", bad));
    }

    [Fact]
    public void ReaderReturnsThePythonRows()
    {
        foreach (var r in Data["rows"]!.AsArray())
        {
            var want = r!["rows"]!.AsArray().Select(row => row!.AsArray().Select(v => (string)v!).ToList()).ToList();
            Assert.Equal(want, ListCsv.Read((string)r["text"]!, ((string)r["delim"]!)[0]));
        }
    }

    [Fact]
    public void ListsReadLikeThePythonLists()
    {
        foreach (var r in Data["csv"]!.AsArray())
        {
            var got = Checker.EntriesFromText((string)r!["text"]!, (string)r["name"]!);
            var want = r["entries"]!.AsArray();
            Assert.Equal(want.Select(e => (string)e!["raw"]!), got.Select(e => e.Raw));
            Assert.Equal(want.Select(e => (int?)e!["tconst"]), got.Select(e => e.Tconst));
        }
    }
}

public class RemoveFromListTests : IDisposable
{
    readonly TempLibrary t = new();

    public void Dispose() => t.Dispose();

    static string Dump(Library lib, string sql) => ImdbFixture.Dump(ImdbFixture.Rows(lib.DbPath, sql));

    [Fact]
    public void TheTableHasThePythonSchema()
    {
        using var lib = t.Open(false, "Heat.1995.720p");
        Assert.Equal("CREATE TABLE removed(raw TEXT PRIMARY KEY, tconst INT, title TEXT, removed TEXT)",
            ImdbFixture.Rows(lib.DbPath, "SELECT sql FROM sqlite_master WHERE name='removed'").Single()[0]);
    }

    [Fact]
    public void AScanNeverBringsBackARemovalMadeByThePythonApp()
    {
        File.Copy(Path.Combine(PythonAnswers.Dir, "python_removed.db"), Path.Combine(t.Root, "data", "library.db"));
        var want = PythonAnswers.Data["removed"]!;
        using var lib = t.Open(false, want["lines"]!.AsArray().Select(l => (string)l!).ToArray());
        lib.Scan();
        lib.Scan();
        Assert.Equal(want["raws_after_scan"]!.AsArray().Select(r => (string)r!), lib.LoadMovies().SelectMany(m => m.Files).Select(f => f.Raw).Order(StringComparer.Ordinal));
        Assert.Equal(want["all_raws"]!.AsArray().Select(r => (string)r!), ImdbFixture.Rows(lib.DbPath, "SELECT raw FROM files").Select(r => (string)r[0]!).Order(StringComparer.Ordinal));
        var items = lib.RemovedItems();
        Assert.Equal(want["items"]!.AsArray().Select(i => ((string?)i!["raw"], (int?)i["tconst"], (string?)i["title"])).OrderBy(x => x.Item1, StringComparer.Ordinal),
            items.Select(i => (i.Raw, i.Tconst, i.Title)).OrderBy(x => x.Raw, StringComparer.Ordinal));
        Assert.All(items, i => Assert.Matches(@"^\d{4}-\d\d-\d\d \d\d:\d\d:\d\d$", i.Removed));
    }

    [Fact]
    public void RemovingHidesTheEntryAndNoScanBringsItBack()
    {
        using var lib = t.Open(false, "Heat.1995.720p", "Alien.1979.1080p", "Seven.1995.1080p");
        lib.Scan();
        ImdbFixture.Run(lib.DbPath, "INSERT INTO titles(tconst, title) VALUES (2, 'Heat of IMDb')");
        ImdbFixture.Run(lib.DbPath, "UPDATE files SET tconst=2 WHERE raw='Heat.1995.720p'");
        var before = File.ReadAllText(t.ListFile);
        int v = lib.Version;
        var files = lib.LoadMovies().ToDictionary(m => m.Files[0].Raw, m => m);
        Assert.Equal(2, lib.RemoveMovies(new[] { files["Heat.1995.720p"], files["Alien.1979.1080p"] }));
        Assert.True(lib.Version > v);
        Assert.Equal(new[] { "Seven.1995.1080p" }, lib.LoadMovies().SelectMany(m => m.Files).Select(f => f.Raw));
        lib.Scan();
        Assert.Equal(1, lib.GetStats().Files);
        Assert.Equal(before, File.ReadAllText(t.ListFile));   // the user's list is not touched
        Assert.Equal("Alien|Alien.1979.1080p|NULL\nHeat of IMDb|Heat.1995.720p|2", Dump(lib, "SELECT title, raw, tconst FROM removed ORDER BY raw"));
        Assert.Equal(0, lib.RemoveFromList(files["Heat.1995.720p"].Files.Select(f => f.Id)));   // already out
        Assert.Equal(0, lib.RemoveFromList(Array.Empty<int>()));
    }

    [Fact]
    public void AHugeRemoveAllWorks()
    {
        var lines = Enumerable.Range(1000, 1300).Select(i => $"Film.{i}.720p").ToArray();
        using var lib = t.Open(false, lines);
        lib.Scan();
        var ids = lib.LoadMovies().SelectMany(m => m.Files).Select(f => f.Id).ToList();
        Assert.Equal(1300, lib.RemoveFromList(ids.Concat(ids)));
        lib.Scan();
        Assert.Equal(0, lib.GetStats().Files);
        Assert.Equal(1300, lib.RestoreRemoved(lib.RemovedItems().Select(r => r.Raw)));
        Assert.Equal(1300, lib.GetStats().Files);
    }

    [Fact]
    public void RestoreBringsTheEntryBackUntilItsLineIsGone()
    {
        using var lib = t.Open(false, "Heat.1995.720p", "Alien.1979.1080p");
        lib.Scan();
        lib.RemoveFromList(lib.LoadMovies().SelectMany(m => m.Files).Select(f => f.Id));
        Assert.Equal(2, lib.RemovedItems().Count);
        Assert.Equal(1, lib.RestoreRemoved(new[] { "Heat.1995.720p" }));
        Assert.Equal(0, lib.RestoreRemoved(Array.Empty<string>()));
        Assert.Equal(1, lib.GetStats().Files);
        lib.Scan();
        Assert.Equal("Alien.1979.1080p", Assert.Single(lib.RemovedItems()).Raw);
        Assert.Equal(1, lib.GetStats().Files);
        File.WriteAllText(t.ListFile, "Alien.1979.1080p\n");
        lib.RestoreRemoved(new[] { "Alien.1979.1080p" });
        Assert.Equal(1, lib.RestoreRemoved(new[] { "Alien.1979.1080p" }));
        File.WriteAllText(t.ListFile, "Other.2000.720p\n");
        lib.Scan();
        Assert.Equal(new[] { "Other.2000.720p" }, lib.LoadMovies().SelectMany(m => m.Files).Select(f => f.Raw));   // the line is gone
    }

    [Fact]
    public void ARemovedNameIsNotReportedAsOnDisk()
    {
        using var lib = t.Open(false, "Heat.1995.720p", "Alien.1979.1080p");
        lib.SaveSettings(new Settings { ListDir = t.Lists, MediaDirs = new() { t.Movies } });
        lib.Scan();
        lib.RemoveFromList(lib.LoadMovies().Where(m => m.PTitle == "Heat").SelectMany(m => m.Files).Select(f => f.Id));
        void Video(string rel)
        {
            string p = Path.Combine(t.Movies, rel);
            Directory.CreateDirectory(Path.GetDirectoryName(p)!);
            using var f = File.Create(p);
            f.SetLength(31_000_000);
        }
        Video("HEAT.1995.720P.mkv");                    // by file name, ignoring case
        Video(@"Heat.1995.720p\movie.mkv");             // by folder
        Video("New.Film.2010.1080p.mkv");
        lib.ScanMedia();
        Assert.Empty(lib.UnlistedItems());     // the folder movie is a library entry now, and the removed name stays out
        Assert.Contains("New.Film.2010.1080p", lib.LoadMovies().SelectMany(m => m.Files).Select(f => f.Raw));
        Assert.DoesNotContain("Heat.1995.720p", lib.LoadMovies().SelectMany(m => m.Files).Select(f => f.Raw));
    }

    [Fact]
    public void AddingANameAgainUndoesItsRemoval()
    {
        using var lib = t.Open(false, "Heat.1995.720p", "Alien.1979.1080p");
        lib.Scan();
        lib.RemoveFromList(lib.LoadMovies().Where(m => m.PTitle == "Heat").SelectMany(m => m.Files).Select(f => f.Id));
        Assert.Equal(1, lib.AddFromCheck(new[] { new AddItem("  Heat.1995.720p ") }, update: false));
        Assert.Empty(lib.RemovedItems());
        lib.Scan();
        Assert.Equal(2, lib.GetStats().Files);
    }

    [Fact]
    public void ANameThatLeftTheListsCanBeAddedAgainButOneInTheLibraryCannot()
    {
        using var lib = t.Open(false, "Heat.1995.720p", "Alien.1979.1080p");
        lib.Scan();
        File.WriteAllText(t.ListFile, "Alien.1979.1080p\n");
        lib.Scan();
        Assert.Equal(1, lib.AddFromCheck(new[] { new AddItem("Heat.1995.720p"), new AddItem("Alien.1979.1080p") }, update: false));
        lib.Scan();
        Assert.Equal(2, lib.GetStats().Files);
    }

    [Fact]
    public void AddFromCheckQueuesTheFullUpdate()
    {
        using var lib = t.Open(false, "Heat.1995.720p");
        lib.Scan();
        using var job = new HeldJob(lib);
        Assert.Equal(0, lib.AddFromCheck(new[] { new AddItem("Heat.1995.720p") }));
        Assert.Equal(0, lib.QueuedCount);       // nothing new: no update
        Assert.Equal(1, lib.AddFromCheck(new[] { new AddItem("New.2001.720p") }));
        Assert.Equal(1, lib.AddFromCheck(new[] { new AddItem("Newer.2002.720p") }));
        Assert.Equal(1, lib.QueuedCount);
    }

    [Fact]
    public void SideFileLinesAreNotMovies()
    {
        using var lib = t.Open(false, "Heat.1995.720p", "Heat.1995.720p.srt", "Heat.1995.720p.nfo", "poster.jpg", "Alien.1979.1080p.mkv");
        lib.Scan();
        Assert.Equal(new[] { "Alien.1979.1080p.mkv", "Heat.1995.720p" }, lib.LoadMovies().SelectMany(m => m.Files).Select(f => f.Raw).Order(StringComparer.Ordinal));
        Assert.Empty(ImdbFixture.Rows(lib.DbPath, "SELECT 1 FROM files WHERE raw LIKE '%.srt'"));
    }

    [Fact]
    public void ASideFileAnOlderBuildStoredLeavesTheLibrary()
    {
        using var lib = t.Open(false, "Heat.1995.720p.srt");
        ImdbFixture.Run(lib.DbPath, "INSERT INTO files(raw, title, conf) VALUES ('Heat.1995.720p.srt', 'Heat', 'none')");
        Assert.Equal(1, lib.GetStats().Files);
        lib.Scan();
        Assert.Equal(0, lib.GetStats().Files);
    }
}

public class ParserUpgradeTests : IDisposable
{
    readonly TempLibrary t = new();

    public void Dispose() => t.Dispose();

    [Fact]
    public void StoredNamesAreReadAgainLikeThePythonApp()
    {
        File.Copy(Path.Combine(PythonAnswers.Dir, "python_reparse.db"), Path.Combine(t.Root, "data", "library.db"));
        var want = PythonAnswers.Data["reparse"]!;
        using var lib = t.Open(false, want["lines"]!.AsArray().Select(l => (string)l!).ToArray());
        File.WriteAllText(lib.SettingsPath, new System.Text.RegularExpressions.Regex(@"^\{").Replace(File.ReadAllText(lib.SettingsPath), "{\n  \"mystery\": 5,\n  \"parser_version\": 1,"));
        lib.Scan();
        string cols = string.Join(", ", want["cols"]!.AsArray().Select(c => (string)c!));
        var got = ImdbFixture.Rows(lib.DbPath, $"SELECT {cols} FROM files ORDER BY id");
        Assert.Equal(want["rows"]!.AsArray().Select(r => string.Join("|", r!.AsArray().Select(v => v?.ToJsonString(new JsonSerializerOptions { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }) ?? "null"))),
            got.Select(r => string.Join("|", r.Select(v => v switch { null => "null", string s => "\"" + s + "\"", _ => Convert.ToString(v) }))));
        var settings = JsonNode.Parse(File.ReadAllText(lib.SettingsPath))!;
        Assert.Equal((int)want["parser_version"]!, (int)settings["parser_version"]!);
        Assert.Equal(5, (int)settings["mystery"]!);
    }

    [Fact]
    public void ARereadHappensOncePerVersion()
    {
        using var lib = t.Open(false, "Heat.1995.720p");
        lib.Scan();
        ImdbFixture.Run(lib.DbPath, "UPDATE files SET title='Stale'");
        lib.Scan();
        Assert.Equal("Stale", ImdbFixture.Rows(lib.DbPath, "SELECT title FROM files").Single()[0]);   // parser_version is 2 now
    }
}

public class HandMadePosterTests : IDisposable
{
    static readonly byte[] Png = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 1, 2 };
    readonly TempLibrary t = new(new FakeHttp((req, _) => req.RequestUri!.AbsolutePath switch
    {
        "/poster.png" => FakeHttp.Bytes(Png),
        "/page.html" => FakeHttp.Bytes(Encoding.UTF8.GetBytes("<html>")),
        _ => FakeHttp.Status(HttpStatusCode.NotFound),
    }));

    public void Dispose() => t.Dispose();

    Library Lib()
    {
        var lib = t.Open(false, "Heat.1995.720p");
        lib.Scan();
        ImdbFixture.Run(lib.DbPath, "INSERT INTO titles(tconst, type, title) VALUES (2, 'movie', 'Heat')");
        ImdbFixture.Run(lib.DbPath, "UPDATE files SET tconst=2");
        return lib;
    }

    static string Poster(Library lib) => ImdbFixture.Dump(ImdbFixture.Rows(lib.DbPath, "SELECT poster, poster_tried FROM titles WHERE tconst=2"));

    [Fact]
    public void SetFromFileReplacesAndSurvivesADownload()
    {
        using var lib = Lib();
        string file = Path.Combine(t.Root, "my image.png");
        File.WriteAllBytes(file, Png);
        lib.SetPosterFrom(2, $" \"{file}\" ");
        Assert.Equal(Png, File.ReadAllBytes(lib.PosterPath(2)));
        Assert.False(File.Exists(lib.PosterPath(2) + ".tmp"));
        Assert.Equal("tt0000002.jpg|1", Poster(lib));
        Assert.True(lib.LoadMovies().Single().HasPoster);
        lib.SaveSettings(new Settings { ListDir = t.Lists, OnlinePosters = true });
        lib.FetchPosters(retry: true);
        Assert.Equal(0, t.Http.Count);    // it has a poster: nothing to fetch
        lib.SetPosterFrom(2, "file:///" + file.Replace('\\', '/'));
    }

    [Fact]
    public void SetFromWebAddress()
    {
        using var lib = Lib();
        Assert.Equal(Png, lib.PosterBytes("https://example.test/poster.png"));
        var e = Assert.Throws<ArgumentException>(() => lib.PosterBytes("https://example.test/page.html"));
        Assert.Equal("That is not an image (JPEG, PNG, WebP or GIF). For a web picture, copy the image address, not the page address.", e.Message);
        Assert.StartsWith("The address could not be opened: ", Assert.Throws<ArgumentException>(() => lib.PosterBytes("HTTPS://example.test/gone.png")).Message);
    }

    [Fact]
    public void BadInputGetsThePythonMessages()
    {
        using var lib = Lib();
        string M(Action a) => Assert.Throws<ArgumentException>(a).Message;
        Assert.Equal("Choose an image file, or paste a web address or a file path.", M(() => lib.PosterBytes("  ")));
        Assert.Equal("Choose an image file, or paste a web address or a file path.", M(() => lib.PosterBytes(null)));
        Assert.Equal("That is not a web address (http...) or a file on this PC.", M(() => lib.PosterBytes(@"C:\no\such\file.png")));
        string big = Path.Combine(t.Root, "big.jpg");
        using (var f = File.Create(big)) f.SetLength(Library.PosterMax + 1);
        Assert.Equal("The image is larger than 15 MB.", M(() => lib.PosterBytes(big)));
        Assert.Equal("This movie has no IMDb match yet, so it cannot have a poster.", M(() => lib.SetPoster(99, Png)));
        Assert.Equal("The image is empty.", M(() => lib.SetPoster(2, Array.Empty<byte>())));
        Assert.Equal("The image is empty.", M(() => lib.SetPoster(2, null)));
        Assert.Equal("The image is larger than 15 MB.", M(() => lib.SetPoster(2, new byte[Library.PosterMax + 1])));
        Assert.Equal("That is not an image (JPEG, PNG, WebP or GIF).", M(() => lib.SetPoster(2, new byte[] { 1, 2, 3 })));
        Assert.False(File.Exists(lib.PosterPath(2)));
    }

    [Fact]
    public void ClearRemovesTheFileAndTheAutomaticDownloadLeavesItAlone()
    {
        using var lib = Lib();
        lib.SetPoster(2, Png);
        lib.ClearPoster(2);
        Assert.False(File.Exists(lib.PosterPath(2)));
        Assert.Equal("NULL|1", Poster(lib));
        Assert.False(lib.LoadMovies().Single().HasPoster);
        lib.ClearPoster(2);    // nothing there: no error
    }

    [Fact]
    public void HasPosterComesFromTheColumnNotTheFile()
    {
        using var lib = Lib();
        File.WriteAllBytes(lib.PosterPath(2), Png);       // a file the database does not know
        Assert.False(lib.LoadMovies().Single().HasPoster);
        File.Delete(lib.PosterPath(2));
        ImdbFixture.Run(lib.DbPath, "UPDATE titles SET poster='tt0000002.jpg'");
        Assert.True(lib.LoadMovies().Single().HasPoster);
    }
}

public class JobQueueTests : IDisposable
{
    readonly TempLibrary t = new();

    public void Dispose() => t.Dispose();

    static void Tick() { }

    [Fact]
    public void AQueuedJobRunsAfterTheRunningOne()
    {
        using var lib = t.Open(false, "x");
        var order = new List<string>();
        using (var job = new HeldJob(lib, () => order.Add("first")))
        {
            Assert.False(lib.StartOrQueue(() => order.Add("second")));
            Assert.Equal(1, lib.QueuedCount);
            Assert.Contains(lib.Job.Log, l => l.Contains("Queued: will run when the current job finishes"));
            job.Release();
            TempLibrary.WaitJob(lib);
        }
        Assert.Equal(new[] { "first", "second" }, order);
        Assert.Equal(0, lib.QueuedCount);
    }

    [Fact]
    public void TheSameJobIsQueuedOnce()
    {
        using var lib = t.Open(false, "x");
        using var job = new HeldJob(lib);
        lib.StartOrQueue(Tick);
        lib.StartOrQueue(Tick);                       // the same method
        Assert.Equal(1, lib.QueuedCount);
        lib.StartOrQueue(() => { });                  // another job
        lib.StartOrQueue(() => { }, key: "k");
        lib.StartOrQueue(() => { }, key: "k");        // the same key
        Assert.Equal(3, lib.QueuedCount);
        lib.StartFullUpdate();
        lib.StartFullUpdate();
        Assert.Equal(4, lib.QueuedCount);
        lib.StartFullUpdate(forceDownload: true);     // other arguments: another job
        Assert.Equal(5, lib.QueuedCount);
        lib.StartRescan();
        lib.StartRescan();
        Assert.Equal(6, lib.QueuedCount);
    }

    [Fact]
    public void TheQueueRunsAfterAnError()
    {
        using var lib = t.Open(false, "x");
        using var ran = new ManualResetEventSlim();
        using var job = new HeldJob(lib, () => throw new InvalidOperationException("boom"));
        lib.StartOrQueue(ran.Set);
        job.Release();
        Assert.True(ran.Wait(10_000));
    }

    [Fact]
    public void StopDropsTheQueueAndAJobQueuedAfterStopStillRuns()
    {
        using var lib = t.Open(false, "x");
        bool dropped = false;
        using var ran = new ManualResetEventSlim();
        using var job = new HeldJob(lib);
        lib.StartOrQueue(() => dropped = true);
        Assert.True(lib.Stop());
        Assert.Equal(0, lib.QueuedCount);
        lib.StartOrQueue(ran.Set);
        job.Release();
        Assert.True(ran.Wait(10_000));
        Assert.False(dropped);
    }

    [Fact]
    public void AQueuedJobKeepsItsStoppableFlag()
    {
        using var lib = t.Open(false, "x");
        using var job = new HeldJob(lib);
        using var seen = new ManualResetEventSlim();
        bool? stoppable = null;
        lib.StartOrQueue(() => { stoppable = lib.Job.Stoppable; seen.Set(); }, stoppable: false);
        job.Release();
        Assert.True(seen.Wait(10_000));
        Assert.False(stoppable);
    }

    [Fact]
    public void WithNothingRunningItStartsAtOnce()
    {
        using var lib = t.Open(false, "x");
        Assert.True(lib.StartOrQueue(Tick));
        TempLibrary.WaitJob(lib);
    }
}

public class MatchActionTests : IDisposable
{
    const string Suggest = """
        {"d":[{"id":"tt0000002","l":"Heat","y":1995,"q":"feature"},
        {"id":"tt0999999","l":"Heat Wave","y":1996,"q":"TV series","s":"Some Star","i":{"imageUrl":"https://m.media-amazon.com/images/M/h._V1_.jpg"}},
        {"id":"nm0000001","l":"A Person"}]}
        """;

    readonly TempLibrary t = new(new FakeHttp((_, _) => FakeHttp.Json(Suggest)));

    public void Dispose() => t.Dispose();

    Library Lib(params string[] lines)
    {
        var lib = t.Open(true, lines);
        lib.Update();
        return lib;
    }

    static int FileId(Library lib, string raw) => Convert.ToInt32(ImdbFixture.Rows(lib.DbPath, $"SELECT id FROM files WHERE raw='{raw}'").Single()[0]);

    [Fact]
    public void LookupGivesTheIndexFirstThenOnlineResults()
    {
        using var lib = Lib("Heat.1995.720p");
        var found = lib.Lookup(FileId(lib, "Heat.1995.720p"), null);
        Assert.Equal(new[] { 2, 999999 }, found.Select(c => c.Tconst));
        Assert.Equal("key", found[0].How);
        Assert.Equal(new[] { "online" }, found.Skip(1).Select(c => c.How));
        Assert.Equal("Heat Wave", found[1].Title);
        Assert.Equal(1996, found[1].Year);
        Assert.Equal("TV series", found[1].Type);
        Assert.Equal("Some Star", found[1].Stars);
        Assert.Equal("https://m.media-amazon.com/images/M/h._V1_.jpg", found[1].Image);
        Assert.EndsWith("/h/heat%201995.json", Assert.Single(t.Http.Calls).Url);
    }

    [Fact]
    public void LookupSearchesTheTypedName()
    {
        using var lib = Lib("Heat.1995.720p");
        var found = lib.Lookup(FileId(lib, "Heat.1995.720p"), "The Godfather 1972");
        Assert.Equal(new[] { 1, 2, 999999 }, found.Select(c => c.Tconst));
        Assert.EndsWith("/t/the%20godfather%201972.json", t.Http.Calls.Single().Url);
    }

    [Fact]
    public void LookupKeepsTheIndexResultsOffline()
    {
        using var off = new TempLibrary();   // every request fails
        using var lib = off.Open(true, "Heat.1995.720p");
        lib.Update();
        Assert.Equal(new[] { 2 }, lib.Lookup(FileId(lib, "Heat.1995.720p"), null).Select(c => c.Tconst));
        Assert.Empty(lib.Lookup(987654, null));
    }

    [Fact]
    public void LookupWorksOnlineWithoutAnIndex()
    {
        using var lib = t.Open(false, "Heat.1995.720p");
        lib.Scan();
        Assert.Equal(new[] { 2, 999999 }, lib.Lookup(FileId(lib, "Heat.1995.720p"), null).Select(c => c.Tconst));
    }

    [Fact]
    public void LookupUsesTheTitleAloneWithoutAYear()
    {
        using var lib = t.Open(false, "Heat 720p");
        lib.Scan();
        lib.Lookup(FileId(lib, "Heat 720p"), null);
        Assert.EndsWith("/h/heat.json", t.Http.Calls.Single().Url);
    }

    [Fact]
    public void AcceptKeepsTheMatchAsManual()
    {
        using var lib = Lib("Heat.1995.720p", "Nothing.At.All.2001.720p");
        int heat = FileId(lib, "Heat.1995.720p"), none = FileId(lib, "Nothing.At.All.2001.720p");
        int rev = lib.IssueRev;
        lib.Accept(heat);
        lib.Accept(none);    // no IMDb id: nothing to accept
        Assert.True(lib.IssueRev > rev);
        Assert.Equal("Heat.1995.720p|manual|1|2\nNothing.At.All.2001.720p|none|0|NULL", ImdbFixture.Dump(ImdbFixture.Rows(lib.DbPath, "SELECT raw, conf, manual, tconst FROM files ORDER BY id")));
    }

    [Fact]
    public void SetMatchRefreshesATitleWithoutDetails()
    {
        using var lib = Lib("Heat.1995.720p", "Nothing.At.All.2001.720p");
        ImdbFixture.Run(lib.DbPath, "INSERT INTO titles(tconst, title) VALUES (1, 'Stale name')");   // a row, but never enriched
        using var job = new HeldJob(lib);
        lib.SetMatchAndRefresh(FileId(lib, "Nothing.At.All.2001.720p"), 1);
        Assert.Equal(1, lib.QueuedCount);
        Assert.Equal("The Godfather", ImdbFixture.Rows(lib.DbPath, "SELECT title FROM titles WHERE tconst=1").Single()[0]);   // copied from the index even so
    }

    [Fact]
    public void SetMatchSkipsTheWorkWhenTheTitleHasItsDetails()
    {
        using var lib = Lib("Heat.1995.720p", "Nothing.At.All.2001.720p");
        ImdbFixture.Run(lib.DbPath, "UPDATE titles SET title='Kept', enriched=1 WHERE tconst=2");
        using var job = new HeldJob(lib);
        lib.SetMatchAndRefresh(FileId(lib, "Nothing.At.All.2001.720p"), 2);
        Assert.Equal(0, lib.QueuedCount);
        Assert.Equal("Kept", ImdbFixture.Rows(lib.DbPath, "SELECT title FROM titles WHERE tconst=2").Single()[0]);
        Assert.Equal("manual", ImdbFixture.Rows(lib.DbPath, "SELECT conf FROM files WHERE tconst=2 AND manual=1").First()[0]);
    }

    [Fact]
    public void NotOnImdbNeedsNoRefresh()
    {
        using var lib = Lib("Heat.1995.720p");
        using var job = new HeldJob(lib);
        lib.SetMatchAndRefresh(FileId(lib, "Heat.1995.720p"), null);
        lib.SetMatchAndRefresh(FileId(lib, "Heat.1995.720p"), 0);
        Assert.Equal(0, lib.QueuedCount);
        Assert.Equal("none", ImdbFixture.Rows(lib.DbPath, "SELECT conf FROM files").Single()[0]);
        Assert.StartsWith("f", lib.LoadMovies().Single().K);
    }

    ListSource Src(string path) => new(path);

    [Fact]
    public void LoadListAddJoinsTheCurrentListAndQueuesTheRescan()
    {
        using var lib = t.Open(false, "Heat.1995.720p");
        string src = Path.Combine(t.Root, "src");
        Directory.CreateDirectory(src);
        File.WriteAllText(Path.Combine(src, "new.txt"), "Alien.1979.1080p\nAlien.1979.1080p\nSeven.1995.1080p\n");
        using var job = new HeldJob(lib);
        var r = lib.LoadList(Src(Path.Combine(src, "new.txt")), "add");
        Assert.Equal(new ListLoad(1, 2, 0), r);
        Assert.Equal(1, lib.QueuedCount);
        Assert.True(File.Exists(Path.Combine(t.Lists, "new.txt")));
        Assert.True(File.Exists(Path.Combine(t.Lists, "Heat.txt")) || Directory.GetFiles(t.Lists, "*.txt").Length == 2);
        Assert.Equal(0, lib.LoadList(Src(Path.Combine(src, "new.txt")), "add").Lists);   // every name is already in a list
        lib.Stop();
    }

    [Fact]
    public void LoadListNewPutsTheOldListsAsideAndDeletesNothing()
    {
        using var lib = t.Open(false, "Heat.1995.720p");
        string before = Directory.GetFiles(t.Lists, "*.txt").Single();
        string src = Path.Combine(t.Root, "src");
        Directory.CreateDirectory(src);
        File.WriteAllText(Path.Combine(src, "fresh.txt"), "Alien.1979.1080p\n");
        var r = lib.LoadList(Src(Path.Combine(src, "fresh.txt")), "new", rescan: false);
        Assert.Equal(1, r.Moved);
        Assert.False(File.Exists(before));
        var aside = Directory.GetFiles(Path.Combine(t.Lists, "previous lists"), Path.GetFileName(before), SearchOption.AllDirectories);
        Assert.Single(aside);
        Assert.Equal("Alien.1979.1080p", File.ReadAllText(Path.Combine(t.Lists, "fresh.txt")).Trim());
        Assert.Throws<ArgumentException>(() => lib.LoadList(Src(Path.Combine(src, "fresh.txt")), "maybe", rescan: false));
    }

    [Fact]
    public void LoadListSourcesAndThePreview()
    {
        using var lib = t.Open(false, "Heat.1995.720p");
        lib.Scan();
        string src = Path.Combine(t.Root, "src");
        Directory.CreateDirectory(src);
        File.WriteAllText(Path.Combine(src, "a.csv"), "title,year\nAlien,1979\nSeven,1995\n");
        var pv = lib.PreviewList(Src(Path.Combine(src, "a.csv")));
        Assert.Equal((1, 2, 2), (pv.Lists, pv.Names, pv.NewN));
        // the same names twice, one already in the library under its name, one under another file name
        var text = lib.PreviewList(new ListSource(Text: "Heat.1995.720p\nSeven.1995.1080p\nSeven.1995.1080p\nHeat 1995 BluRay\n", Name: "x.txt"));
        Assert.Equal((3, 1, 1, 1), (text.Names, text.RepeatedN, text.ExistingN, text.SimilarN));
        Assert.Equal(new[] { "Heat 1995 BluRay" }, text.Similar);
        // a folder of movies: the names of its video files
        string movies = Path.Combine(t.Root, "movies");
        Directory.CreateDirectory(movies);
        File.WriteAllText(Path.Combine(movies, "Brazil.1985.1080p.mkv"), "x");
        Assert.Equal(1, lib.PreviewList(Src(movies)).Names);
        Assert.Throws<ArgumentException>(() => lib.PreviewList(Src(Path.Combine(t.Root, "nothing.csv"))));
        Assert.Throws<ArgumentException>(() => lib.PreviewList(new ListSource(Text: "", Name: "e.txt")));
    }

    [Fact]
    public void LoadingARemovedNameAgainBringsItBack()
    {
        using var lib = t.Open(false, "Heat.1995.720p");
        lib.Scan();
        lib.RemoveFromList(lib.LoadMovies().SelectMany(m => m.Files).Select(f => f.Id));
        File.WriteAllText(Path.Combine(t.Root, "back.txt"), "Heat.1995.720p\n");
        lib.LoadList(Src(Path.Combine(t.Root, "back.txt")), "add", rescan: false);
        lib.Scan();
        Assert.Equal(new[] { "Heat.1995.720p" }, lib.LoadMovies().SelectMany(m => m.Files).Select(f => f.Raw));
    }
}

public class WholeUpdateTests : IDisposable
{
    readonly TempLibrary t = new(new FakeHttp((_, _) => FakeHttp.Status(HttpStatusCode.NotFound)));

    public void Dispose() => t.Dispose();

    static void Idle(Library lib)
    {
        Thread.Sleep(50);
        for (int i = 0; i < 600 && lib.Job.Running; i++) Thread.Sleep(50);
        Assert.False(lib.Job.Running);
    }

    /// <summary>Small IMDb datasets and their index; tt0000439 and tt0013442 are on the reviewed Persian list.</summary>
    Library Lib(params string[] lines)
    {
        var lib = t.Open(false, lines);
        Directory.CreateDirectory(lib.ImdbDir);
        foreach (var (name, text) in ImdbFixture.Texts)
        {
            string body = name != "title.basics" ? text : text +
                "tt0000439\tmovie\tThe Great Train Robbery\tThe Great Train Robbery\t0\t1903\t\\N\t12\tAction\n" +
                "tt0013442\tmovie\tNosferatu\tNosferatu\t0\t1922\t\\N\t94\tHorror\n";
            ImdbFixture.Gz(Path.Combine(lib.ImdbDir, name + ".tsv.gz"), body);
        }
        ImdbIndex.BuildIndex(lib.ImdbDir, lib.IndexPath, (_, _, _) => { });
        return lib;
    }

    static string Review(Library lib) => ImdbFixture.Dump(ImdbFixture.Rows(lib.DbPath, "SELECT tconst, source FROM fa_titles ORDER BY tconst"));

    [Fact]
    public void AFreshInstallGetsTheReviewedTitlesInItsFirstUpdate()
    {
        using var lib = Lib("The.Great.Train.Robbery.1903.720p");
        lib.FullUpdate();
        Assert.Equal("439|review", Review(lib));
        Assert.DoesNotContain("fa_review_version", File.ReadAllText(lib.SettingsPath));
    }

    [Fact]
    public void TitlesAddedLaterGetThemToo()
    {
        using var lib = Lib("The.Great.Train.Robbery.1903.720p");
        lib.FullUpdate();
        File.AppendAllText(t.ListFile, "Nosferatu.1922.720p\n");
        lib.FullUpdate();
        Assert.Equal("439|review\n13442|review", Review(lib));
    }

    [Fact]
    public void TheKeyAnOlderBuildSavedIsIgnoredAndDropped()
    {
        using var lib = Lib("The.Great.Train.Robbery.1903.720p");
        var o = JsonNode.Parse(File.ReadAllText(lib.SettingsPath))!.AsObject();
        o["fa_review_version"] = 2;
        o["mystery"] = 5;
        File.WriteAllText(lib.SettingsPath, o.ToJsonString());
        lib.FullUpdate();
        Assert.Equal("439|review", Review(lib));
        var after = JsonNode.Parse(File.ReadAllText(lib.SettingsPath))!.AsObject();
        Assert.False(after.ContainsKey("fa_review_version"));
        Assert.Equal(5, (int)after["mystery"]!);
    }

    [Fact]
    public void RescanMatchesNewNamesAndNeedsNoDownload()
    {
        using var lib = Lib("Heat 1995");
        lib.FullUpdate();
        File.AppendAllText(t.ListFile, "The.Matrix.1999.1080p\n");
        lib.Rescan();
        Idle(lib);                        // the details of new titles are filled by a job queued behind the rescan
        var m = lib.LoadMovies().Single(x => x.Files[0].Raw == "The.Matrix.1999.1080p");
        Assert.Equal("tt0000001", m.K);
        Assert.NotEmpty(m.Dir);          // details for the new title
    }

    [Fact]
    public void AnOfflineFailureDoesNotUndoARescan()
    {
        using var other = new TempLibrary();
        using var lib = other.Open(true, "Heat.1995.720p");   // an index, but no IMDb data files to read details from
        lib.Rescan();
        Idle(lib);
        Assert.Equal("tt0000002", lib.LoadMovies().Single().K);
        Assert.Contains(lib.Job.Log, l => l.StartsWith(l[..9] + "enrich skipped:"));
    }

    [Fact]
    public void RematchMatchesAutomaticMatchesAgainAndLeavesManualOnes()
    {
        using var lib = Lib("The.Matrix.1999.1080p", "Heat 1995");
        lib.FullUpdate();
        ImdbFixture.Run(lib.DbPath, "UPDATE files SET tconst=6, conf='review' WHERE raw LIKE 'The.Matrix%'");
        ImdbFixture.Run(lib.DbPath, "UPDATE files SET tconst=6, conf='manual', manual=1 WHERE raw LIKE 'Heat%'");
        lib.Rematch();
        Assert.Equal("Heat 1995|6|manual\nThe.Matrix.1999.1080p|1|exact", ImdbFixture.Dump(ImdbFixture.Rows(lib.DbPath, "SELECT raw, tconst, conf FROM files ORDER BY raw")));
    }

    [Fact]
    public void StartupBacksUpAndRunsTheUpdate()
    {
        using var lib = Lib("The.Matrix.1999.1080p");
        lib.SetUser(new[] { "tt0000001" }, fav: true);
        Assert.True(lib.RunStartupJobs());
        TempLibrary.WaitJob(lib);
        Assert.Null(lib.Job.Error);
        Assert.Equal(1, lib.GetStats().Matched);
        Assert.Single(lib.ListBackups());
    }

    [Fact]
    public void TheFirstStartWaitsForTheFirstRunChoice()
    {
        using var other = new TempLibrary();
        using var lib = new Library(other.Root);
        Assert.True(lib.FirstRun);
        ImdbFixture.Run(lib.DbPath, "INSERT INTO user(mkey, fav) VALUES ('tt0000001', 1)");
        Assert.False(lib.RunStartupJobs());
        Assert.False(lib.Job.Running);
        Assert.Single(lib.ListBackups());
        Assert.True(lib.FirstRun);
    }
}

public class PathsTests : IDisposable
{
    readonly string dir = Path.Combine(Path.GetTempPath(), "movieshelf-paths-" + Guid.NewGuid().ToString("N"));
    string Project => Path.Combine(dir, "project");
    string Home => Path.Combine(dir, "home");

    public PathsTests() => Directory.CreateDirectory(Project);

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try { Directory.Delete(dir, true); } catch (IOException) { }
    }

    [Fact]
    public void TheHomeFolderRuleIsPythons()
    {
        string local = Path.Combine(dir, "local");
        Assert.Equal(Path.Combine(local, "MovieShelf"), Library.ResolveRoot(null, Project, local));
        File.WriteAllText(Path.Combine(Project, "portable.txt"), "");
        Assert.Equal(Project, Library.ResolveRoot("", Project + Path.DirectorySeparatorChar, local));
        Assert.Equal(Path.GetFullPath(Home), Library.ResolveRoot(Home, Project, local));   // MOVIESHELF_HOME wins
        Assert.Equal(Path.Combine(local, "MovieShelf"), Library.ResolveRoot(null, null, local));
    }

    void OldData()
    {
        string old = Path.Combine(Project, "data");
        Directory.CreateDirectory(Path.Combine(old, "posters"));
        File.WriteAllText(Path.Combine(old, "library.db"), "db");
        File.WriteAllText(Path.Combine(old, "settings.json"), "{}");
        File.WriteAllText(Path.Combine(old, "posters", "tt0000001.jpg"), "jpg");
        File.WriteAllText(Path.Combine(old, "setup.log"), "log");
    }

    [Fact]
    public void OldDataMovesOutOfTheProjectFolder()
    {
        OldData();
        Directory.CreateDirectory(Path.Combine(Home, "data", "posters"));   // an empty folder the app made already
        Assert.True(Library.MigrateOldData(Project, Home));
        Assert.Equal("db", File.ReadAllText(Path.Combine(Home, "data", "library.db")));
        Assert.Equal("jpg", File.ReadAllText(Path.Combine(Home, "data", "posters", "tt0000001.jpg")));
        Assert.False(File.Exists(Path.Combine(Home, "data", "setup.log")));
        Assert.False(Directory.Exists(Path.Combine(Project, "data")));
    }

    [Fact]
    public void ALibraryAlreadyThereIsNeverReplaced()
    {
        OldData();
        Directory.CreateDirectory(Path.Combine(Home, "data"));
        File.WriteAllText(Path.Combine(Home, "data", "library.db"), "mine");
        Assert.False(Library.MigrateOldData(Project, Home));
        Assert.Equal("mine", File.ReadAllText(Path.Combine(Home, "data", "library.db")));
        Assert.True(File.Exists(Path.Combine(Project, "data", "library.db")));
        Assert.False(Library.MigrateOldData(Path.Combine(dir, "no such project"), Path.Combine(dir, "other")));
    }

    [Fact]
    public void NothingMovesWhileTheOldAppRuns()
    {
        OldData();
        using var open = new TcpListener(IPAddress.Loopback, 0);
        open.Start();
        File.WriteAllText(Path.Combine(Project, "data", "instance.json"), $"{{\"port\": {((IPEndPoint)open.LocalEndpoint).Port}}}");
        Assert.Throws<InvalidOperationException>(() => Library.MigrateOldData(Project, Home));
        Assert.True(File.Exists(Path.Combine(Project, "data", "library.db")));
    }
}

public class MovieFieldTests : IDisposable
{
    readonly TempLibrary t = new();

    public void Dispose() => t.Dispose();

    Library Lib()
    {
        var lib = t.Open(false, "Heat.1995.720p");
        lib.Scan();
        ImdbFixture.Run(lib.DbPath, "UPDATE files SET tconst=2");
        ImdbFixture.Run(lib.DbPath, "INSERT INTO titles(tconst, type, title, directors, cast_, crew, backdrop_url, tmdb_cast) VALUES (2, 'movie', 'Heat', '[[5, \"Mann\"]]', '[[6, \"Pacino\", \"Vincent\"]]', $p0, 'https://image.tmdb.org/t/p/w1280/b.jpg', $p1)",
            "[[7, \"Spinotti\", \"cinematographer\"], [8, \"Elliot\", \"composer\"]]",
            "[{\"name\": \"Al Pacino\", \"ch\": \"Vincent Hanna\", \"img\": \"https://image.tmdb.org/t/p/w185/a.jpg\"}, {\"name\": \"No Photo\", \"ch\": null, \"img\": null}]");
        return lib;
    }

    [Fact]
    public void CrewBackdropAndCastPhotosLoad()
    {
        using var lib = Lib();
        var m = lib.LoadMovies().Single();
        Assert.Equal("https://image.tmdb.org/t/p/w1280/b.jpg", m.Backdrop);
        Assert.Equal(new[] { "Spinotti", "Elliot" }, m.Crew.Select(p => p.Name));
        Assert.Equal(7, m.Crew[0].Id);
        Assert.Equal("cinematographer", m.Crew[0].Role);
        Assert.Same(m.Crew, m.Crew);
        Assert.Equal(new TmdbCastMember("Al Pacino", "Vincent Hanna", "https://image.tmdb.org/t/p/w185/a.jpg"), m.TmdbCastMembers[0]);
        Assert.Null(m.TmdbCastMembers[1].Img);
        Assert.Equal(new[] { "Al Pacino", "No Photo" }, m.TmdbCast);
    }

    [Fact]
    public void BadCrewJsonDoesNotStopTheLoad()
    {
        using var lib = Lib();
        ImdbFixture.Run(lib.DbPath, "UPDATE titles SET crew='not json', tmdb_cast='{}'");
        var m = lib.LoadMovies().Single();
        Assert.Empty(m.Crew);
        Assert.Empty(m.TmdbCastMembers);
    }

    [Fact]
    public void APersonIsFoundByAnyRole()
    {
        using var lib = Lib();
        var m = lib.LoadMovies().Single();
        Assert.All(new[] { 5, 6, 7 }, id => Assert.True(m.HasPerson(id)));
        Assert.False(m.HasPerson(9));
    }

    [Fact]
    public void AnEmptyTitleFallsBackLikePython()
    {
        using var lib = t.Open(false, "().2014.720p", "Heat.1995.720p");
        lib.Scan();
        Assert.Equal("().2014.720p", lib.LoadMovies().Single(m => m.Files[0].Raw.StartsWith('(')).Title);   // no title in the name: the raw name
        ImdbFixture.Run(lib.DbPath, "INSERT INTO titles(tconst, type, title) VALUES (2, 'movie', '')");
        ImdbFixture.Run(lib.DbPath, "UPDATE files SET tconst=2 WHERE raw LIKE 'Heat%'");
        Assert.Equal("Heat", lib.LoadMovies().Single(m => m.K == "tt0000002").Title);    // an empty IMDb title: the parsed one
    }

    [Fact]
    public void AudioAndSubtitleLanguagesCanBeSearched()
    {
        using var lib = Lib();
        ImdbFixture.Run(lib.DbPath, "INSERT INTO fileinfo(path, raw, size) VALUES ('C:\\m\\h.mkv', 'Heat.1995.720p', 5)");
        ImdbFixture.Run(lib.DbPath, "INSERT INTO mediainfo(path, ok, audio, subs) VALUES ('C:\\m\\h.mkv', 1, $p0, $p1)", "[\"English\", \"Persian\"]", "[\"Türkçe\"]");
        var m = lib.LoadMovies();
        int N(string q) => m.Count(SearchQuery.Parse(q).Test);
        Assert.Equal(1, N("audio:persian"));
        Assert.Equal(0, N("audio:german"));
        Assert.Equal(0, N("-audio:english"));
        Assert.Equal(1, N("sub:turkce"));
        Assert.Equal(1, N("subs:turk"));
        Assert.Equal(1, N("subtitle:\"türkçe\""));
        Assert.Equal(0, N("sub:persian"));
    }
}

/// <summary>Real pauses: run alone, so a busy thread pool during the other tests cannot look like a timeout.</summary>
[CollectionDefinition("timing", DisableParallelization = true)]
public class TimingCollection { }

[Collection("timing")]
public class OnlineTimeoutTests
{
    /// <summary>Gives a few bytes at a time with a pause before each read; a pause of -1 never answers.</summary>
    sealed class SlowStream : Stream
    {
        readonly int pauseMs;
        int left;

        public SlowStream(int reads, int pauseMs) { left = reads; this.pauseMs = pauseMs; }

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default)
        {
            await Task.Delay(pauseMs, ct);
            if (left-- <= 0) return 0;
            buffer.Span[0] = (byte)'x';
            return 1;
        }

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    static OnlineClient Client(Stream body) =>
        new(new FakeHttp((_, _) => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(body) }));

    [Fact]
    public async Task ASlowDownloadThatKeepsMovingIsNotCutOff()
    {
        var (status, body) = await Client(new SlowStream(5, 300)).SendAsync(HttpMethod.Get, "https://example.test/x", null, null, 1, default);   // 1.8 s in all
        Assert.Equal(200, status);
        Assert.Equal(5, body.Length);
    }

    [Fact]
    public async Task ADownloadThatStallsTimesOut()
    {
        await Assert.ThrowsAsync<TimeoutException>(() => Client(new SlowStream(5, -1)).SendAsync(HttpMethod.Get, "https://example.test/x", null, null, 0.2, default));
    }

    [Fact]
    public async Task StopIsNotATimeout()
    {
        using var stop = new CancellationTokenSource(100);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Client(new SlowStream(5, -1)).SendAsync(HttpMethod.Get, "https://example.test/x", null, null, 30, stop.Token));
    }
}
