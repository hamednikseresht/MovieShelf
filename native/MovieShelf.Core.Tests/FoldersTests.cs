using System.Diagnostics;
using System.Text;
using Microsoft.Data.Sqlite;
using Xunit;

namespace MovieShelf.Core.Tests;

public class ProbeTests
{
    const string Json = @"{
  ""streams"": [
    {""codec_type"": ""video"", ""codec_name"": ""mjpeg"", ""width"": 600, ""height"": 900, ""disposition"": {""attached_pic"": 1}},
    {""codec_type"": ""video"", ""codec_name"": ""hevc"", ""width"": 1920, ""height"": 800, ""disposition"": {""attached_pic"": 0}},
    {""codec_type"": ""audio"", ""tags"": {""language"": ""eng""}},
    {""codec_type"": ""audio"", ""tags"": {""language"": ""fas""}},
    {""codec_type"": ""audio"", ""tags"": {""language"": ""eng""}},
    {""codec_type"": ""audio"", ""tags"": {""language"": ""und""}},
    {""codec_type"": ""subtitle"", ""tags"": {""language"": ""ger""}},
    {""codec_type"": ""subtitle"", ""tags"": {""language"": ""xyz""}},
    {""codec_type"": ""subtitle""}
  ],
  ""format"": {""duration"": ""7261.504000"", ""bit_rate"": ""4500123""}
}";

    [Fact]
    public void ParsesFfprobeJson()
    {
        var p = Probe.Parse(Json);
        Assert.Equal(7261, p.Duration);
        Assert.Equal(1920, p.Width);
        Assert.Equal(800, p.Height);
        Assert.Equal("hevc", p.VCodec);
        Assert.Equal(4500123, p.Bitrate);
        Assert.Equal(new[] { "English", "Persian" }, p.Audio);
        Assert.Equal(new[] { "German", "XYZ" }, p.Subs);
        Assert.Equal(4, p.NAudio);
        Assert.Equal(3, p.NSubs);
    }

    [Fact]
    public void ParsesOddJson()
    {
        // a zero / N/A duration is "unknown"; the stream duration is used when the format has none
        Assert.Null(Probe.Parse(@"{""format"":{""duration"":""N/A"",""bit_rate"":""N/A""}}").Duration);
        Assert.Null(Probe.Parse(@"{""format"":{""duration"":""0.000000""}}").Duration);
        var p = Probe.Parse(@"{""streams"":[{""codec_type"":""video"",""duration"":""12.9"",""width"":640}],""format"":{}}");
        Assert.Equal(12, p.Duration);
        Assert.Null(p.Bitrate);
        Assert.Null(p.Height);
        var empty = Probe.Parse("{}");
        Assert.Null(empty.Width);
        Assert.Empty(empty.Audio);
    }

    [Fact]
    public void LanguageNamesAndHeightClass()
    {
        Assert.Equal("Persian", Probe.LangName(" FAS "));
        Assert.Equal("English", Probe.LangName("en"));
        Assert.Null(Probe.LangName("und"));
        Assert.Null(Probe.LangName(null));
        Assert.Equal("ABC", Probe.LangName("abc"));
        Assert.Equal("1080p", Probe.RealHeightClass(1920, 800));
        Assert.Equal("2160p", Probe.RealHeightClass(3840, 2160));
        Assert.Equal("720p", Probe.RealHeightClass(1280, 536));
        Assert.Equal("576p", Probe.RealHeightClass(1024, 576));
        Assert.Equal("SD", Probe.RealHeightClass(720, 480));
        Assert.Null(Probe.RealHeightClass(null, null));
    }

    [Fact]
    public void FindFfprobeUsesTheSettingsPathFirst()
    {
        string f = Path.GetTempFileName();
        try { Assert.Equal(f, Probe.FindFfprobe(new Settings { FfprobePath = f })); }
        finally { File.Delete(f); }
    }

    [Fact]
    public void ProbesWithAFakeFfprobe()
    {
        if (!OperatingSystem.IsWindows()) return;
        string dir = Path.Combine(Path.GetTempPath(), "movieshelf-ff-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            File.WriteAllText(Path.Combine(dir, "out.json"), Json);
            string ok = Path.Combine(dir, "ffprobe.cmd"), bad = Path.Combine(dir, "bad.cmd");
            File.WriteAllText(ok, "@type \"%~dp0out.json\"\r\n");
            File.WriteAllText(bad, "@exit /b 1\r\n");
            var info = Probe.ProbeFile(ok, "whatever.mkv");
            Assert.NotNull(info);
            Assert.Equal(1920, info!.Width);
            Assert.Null(Probe.ProbeFile(bad, "whatever.mkv"));
            Assert.Null(Probe.ProbeFile(Path.Combine(dir, "missing.exe"), "whatever.mkv"));
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void ProbesARealFileWhenFfmpegIsInstalled()
    {
        var ff = Probe.FindFfprobe();
        if (ff == null) return;
        string ffmpeg = Path.Combine(Path.GetDirectoryName(ff)!, OperatingSystem.IsWindows() ? "ffmpeg.exe" : "ffmpeg");
        if (!File.Exists(ffmpeg)) return;
        string dir = Path.Combine(Path.GetTempPath(), "movieshelf-real-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            string file = Path.Combine(dir, "tiny.mkv");
            var psi = new ProcessStartInfo(ffmpeg) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true, RedirectStandardOutput = true };
            foreach (var a in new[] { "-v", "error", "-f", "lavfi", "-i", "testsrc=duration=2:size=320x240:rate=5", "-f", "lavfi", "-i", "sine=duration=2",
                "-metadata:s:a:0", "language=eng", "-c:v", "mpeg4", "-c:a", "aac", "-shortest", file }) psi.ArgumentList.Add(a);
            using (var p = Process.Start(psi)!) { p.StandardError.ReadToEnd(); p.WaitForExit(); }
            Assert.True(File.Exists(file));
            var info = Probe.ProbeFile(ff, file);
            Assert.NotNull(info);
            Assert.Equal(320, info!.Width);
            Assert.Equal(240, info.Height);
            Assert.Equal(new[] { "English" }, info.Audio);
            Assert.Equal(1, info.NAudio);
            Assert.InRange(info.Duration ?? 0, 1, 3);
            Assert.Null(Probe.ProbeFile(ff, Path.Combine(dir, "nope.mkv")));
        }
        finally { Directory.Delete(dir, true); }
    }
}

public class CheckerTests : IDisposable
{
    readonly string root = Path.Combine(Path.GetTempPath(), "movieshelf-chk-" + Guid.NewGuid().ToString("N"));

    public CheckerTests() => Directory.CreateDirectory(root);

    public void Dispose() { try { Directory.Delete(root, true); } catch (IOException) { } }

    [Fact]
    public void ReadsTextInSeveralEncodings()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        string a = Path.Combine(root, "a.txt"), b = Path.Combine(root, "b.txt"), c = Path.Combine(root, "c.txt"), d = Path.Combine(root, "d.txt");
        File.WriteAllBytes(a, new byte[] { 0xEF, 0xBB, 0xBF }.Concat(Encoding.UTF8.GetBytes("Amélie 2001")).ToArray());
        File.WriteAllBytes(b, new byte[] { 0xFF, 0xFE }.Concat(Encoding.Unicode.GetBytes("Heat 1995\r\nAmélie 2001")).ToArray());
        File.WriteAllBytes(c, Encoding.GetEncoding(1256).GetBytes("فيلم 1999"));
        File.WriteAllBytes(d, Encoding.UTF8.GetBytes("Amélie 2001"));
        Assert.Equal("Amélie 2001", Checker.ReadText(a));
        Assert.Equal("Heat 1995\r\nAmélie 2001", Checker.ReadText(b));
        Assert.Equal("فيلم 1999", Checker.ReadText(c));
        Assert.Equal("Amélie 2001", Checker.ReadText(d));
    }

    [Fact]
    public void TextEntriesOnePerLine()
    {
        var e = Checker.EntriesFromText("\"Heat 1995\"\r\n\r\n  Alien.1979.1080p  \nSeven 1995\n");
        Assert.Equal(new[] { "Heat 1995", "Alien.1979.1080p", "Seven 1995" }, e.Select(x => x.Raw));
        Assert.Empty(Checker.EntriesFromText("  \n\n"));
    }

    [Fact]
    public void CsvColumnsAreDetected()
    {
        var byTitle = Checker.EntriesFromText("Title,Year,Rating\nHeat,1995,8.3\n\"Amélie, Le fabuleux\",2001,8.3\nSeven 1995,1995,8.6\n", "list.csv");
        Assert.Equal(new[] { "Heat 1995", "Amélie, Le fabuleux 2001", "Seven 1995" }, byTitle.Select(x => x.Raw));
        var byId = Checker.EntriesFromText("Const;Title\ntt0113277;Heat\ntt0000003;\n", "x.csv");
        Assert.Equal(new int?[] { 113277, 3 }, byId.Select(x => x.Tconst));
        Assert.Equal(new[] { "Heat", "tt0000003" }, byId.Select(x => x.Raw));
        var byFile = Checker.EntriesFromText("File name\tSize\nHeat.1995.1080p.mkv\t100\nsub\\Alien.1979.avi\t2\nnoext\t3\n", "x.tsv");
        Assert.Equal(new[] { "Heat.1995.1080p", "Alien.1979", "noext" }, byFile.Select(x => x.Raw));
        // a headerless csv uses the first column; a txt with a csv-looking header line is detected too
        var headerless = Checker.EntriesFromText("Heat 1995,a\nAlien 1979,b\n", "p.csv");
        Assert.Equal(new[] { "Heat 1995", "Alien 1979" }, headerless.Select(x => x.Raw));
        var sniffed = Checker.EntriesFromText("name,year\nHeat,1995\n", "pasted.txt");
        Assert.Equal("Heat", Assert.Single(sniffed).Raw);   // a name column wins; the year column is only added to a title column
    }

    void Touch(string rel, long size = 10)
    {
        string p = Path.Combine(root, rel);
        Directory.CreateDirectory(Path.GetDirectoryName(p)!);
        using var f = File.Create(p);
        f.SetLength(size);
    }

    [Fact]
    public void FolderEntries()
    {
        Touch(@"movies\Heat.1995.1080p.BluRay.mkv");
        Touch(@"movies\Heat (1995) 1080p\movie.mkv");
        Touch(@"movies\Heat (1995) 1080p\movie.sample.mkv");
        Touch(@"movies\Heat (1995) 1080p\readme.txt");
        Touch(@"movies\Series.S01E02.720p.mkv");
        Touch(@"movies\Alien (1979)\VIDEO_TS\VTS_01_1.VOB", 1000);
        Touch(@"movies\Alien (1979)\VIDEO_TS\VIDEO_TS.IFO", 500);
        Touch(@"movies\Extras\bonus.mkv");
        Touch(@"movies\.hidden\x.mkv");
        Touch(@"movies\Trailer.mp4");
        var e = Checker.EntriesFromFolder(Path.Combine(root, "movies"));
        var raws = e.Select(x => x.Raw).OrderBy(x => x, StringComparer.Ordinal).ToList();
        Assert.Equal(new[] { "Alien (1979)", "Heat (1995) 1080p", "Heat.1995.1080p.BluRay", "Series.S01E02.720p" }, raws);
        Assert.True(e.Single(x => x.Raw == "Series.S01E02.720p").Episode);
        var disc = e.Single(x => x.Raw == "Alien (1979)");
        Assert.Equal(1500, disc.Size);
        Assert.EndsWith("Alien (1979)", disc.Path);
    }

    [Fact]
    public void CompareAndLabelQuality()
    {
        Quality Q(string? res, string? src = null, bool hdr = false, bool b10 = false, string? audio = null, bool fa = false) => new(res, src, null, hdr, b10, audio, fa, null);
        Assert.True(Checker.CompareQuality(Q("1080p"), Q("720p")) > 0);
        Assert.True(Checker.CompareQuality(Q("720p", "BluRay"), Q("1080p", "WEBRip")) < 0);
        Assert.True(Checker.CompareQuality(Q("1080p", "BluRay"), Q("1080p", "WEB-DL")) > 0);
        Assert.Equal(0, Checker.CompareQuality(Q("1080p", "BluRay"), Q("1080p")));    // unknown source is not worse
        Assert.Equal(0, Checker.CompareQuality(Q(null), Q("1080p")));
        Assert.True(Checker.CompareQuality(Q("1080p", "BluRay", hdr: true), Q("1080p", "BluRay")) > 0);
        Assert.True(Checker.CompareQuality(Q("1080p", "BluRay", b10: false), Q("1080p", "BluRay", b10: true)) < 0);
        Assert.True(Checker.CompareQuality(Q("1080p", audio: "5.1"), Q("1080p", audio: "2.0")) > 0);
        Assert.Equal("1080p BluRay x264 HDR 10-bit 5.1 Farsi dubbed Extended",
            Checker.QualityLabel(new Quality("1080p", "BluRay", "x264", true, true, "5.1", true, "Extended")));
        Assert.Equal("?", Checker.QualityLabel(Q(null)));
    }
}

public class FoldersLibraryTests : IDisposable
{
    readonly string root = Path.Combine(Path.GetTempPath(), "movieshelf-fold-" + Guid.NewGuid().ToString("N"));
    string Movies => Path.Combine(root, "movies");
    string Lists => Path.Combine(root, "lists");

    public FoldersLibraryTests() => Directory.CreateDirectory(Path.Combine(root, "data"));

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try { Directory.Delete(root, true); } catch (IOException) { }
    }

    static void MakeIndex(string path)
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
INSERT INTO keys VALUES ('thegodfather', 1, 0), ('godfather', 1, 0), ('heat', 2, 0), ('breakingbad', 3, 0);";
        cmd.ExecuteNonQuery();
    }

    Library NewLibrary(bool index, params string[] lines)
    {
        Directory.CreateDirectory(Lists);
        File.WriteAllLines(Path.Combine(Lists, "a.txt"), lines);
        if (index) MakeIndex(Path.Combine(root, "data", "imdb_index.db"));
        var lib = new Library(root);
        lib.SaveSettings(new Settings { ListDir = Lists, MediaDirs = new() { Movies } });
        return lib;
    }

    void Touch(string rel, long size = 10)
    {
        string p = Path.Combine(Movies, rel);
        Directory.CreateDirectory(Path.GetDirectoryName(p)!);
        using var f = File.Create(p);
        f.SetLength(size);
    }

    [Fact]
    public void ScanMediaLinksFilesAndFindsUnlisted()
    {
        using var lib = NewLibrary(false, "Heat.1995.720p", "Other Movie 2001", "Alien.1979.1080p");
        lib.Scan();
        Touch("Heat.1995.720p.mkv", 1234);                       // by file name
        Touch(@"Other Movie 2001\video.mkv", 500);           // by folder name
        Touch(@"Alien.1979.1080p\CD1\Alien.1979.1080p.avi", 77); // deeper
        Touch("HEAT.1995.720P.mp4", 4);                          // name matches ignoring case
        Touch("New.Film.2010.1080p.mkv", 31_000_000);            // in no list
        Touch(@"Cool Film (2005) 720p\cool.mkv", 31_000_000);    // in no list, named by the folder
        Touch("Tiny.Film.2012.mkv", 1000);                       // too small
        Touch("Some.Show.S01E03.mkv", 31_000_000);               // episode
        Touch("Making.Of.Trailer.mkv", 31_000_000);             // trailer
        Touch(@"$RECYCLE.BIN\Deleted.Film.2000.mkv", 31_000_000);
        Touch("notes.txt", 31_000_000);
        lib.ScanMedia();

        var movies = lib.LoadMovies();
        var heat = movies.Single(m => m.PTitle == "Heat");
        Assert.Equal(2, heat.Files[0].Paths.Count);
        Assert.Equal(1234, heat.Files[0].Size);
        Assert.Single(movies.Single(m => m.PTitle == "Other Movie").Files[0].Paths);
        Assert.Single(movies.Single(m => m.PTitle == "Alien").Files[0].Paths);

        // the movie folders are a source of movies: videos in no list became library entries, so nothing waits as "on disk"
        Assert.Empty(lib.UnlistedItems());
        var taken = movies.SelectMany(m => m.Files).Where(f => f.List == Library.FolderSrc).Select(f => f.Raw).OrderBy(r => r, StringComparer.Ordinal).ToList();
        Assert.Equal(new[] { "Cool Film (2005) 720p", "New.Film.2010.1080p" }, taken);
        Assert.All(movies.SelectMany(m => m.Files).Where(f => f.List == Library.FolderSrc), f => Assert.Equal(31_000_000, f.Size));

        // a second scan replaces the first: remove a file and it is gone
        File.Delete(Path.Combine(Movies, "HEAT.1995.720P.mp4"));
        lib.ScanMedia();
        Assert.Single(lib.LoadMovies().Single(m => m.PTitle == "Heat").Files[0].Paths);
    }

    [Fact]
    public void AMovieFromAFolderGoesAwayWhenItsVideoDoesAndNeverTouchesTheLists()
    {
        using var lib = NewLibrary(false, "Heat.1995.720p");
        lib.Scan();
        Touch("New.Film.2010.1080p.mkv", 31_000_000);
        Touch("Another.Film.2011.720p.mkv", 31_000_000);
        lib.ScanMedia();
        Assert.Equal(3, lib.GetStats().Files);
        Assert.False(File.Exists(Path.Combine(Lists, "added.txt")));   // nothing is written to the lists
        lib.Scan();                                                   // a list scan leaves them alone
        Assert.Equal(3, lib.GetStats().Files);
        File.Delete(Path.Combine(Movies, "Another.Film.2011.720p.mkv"));
        lib.ScanMedia();
        Assert.Equal(2, lib.GetStats().Files);
        Assert.Equal(new[] { "Heat.1995.720p", "New.Film.2010.1080p" }, lib.LoadMovies().SelectMany(m => m.Files).Select(f => f.Raw).OrderBy(r => r, StringComparer.Ordinal));
    }

    [Fact]
    public void MoviesOfAFolderThatIsAwayStay()
    {
        using var lib = NewLibrary(false);
        Touch("New.Film.2010.1080p.mkv", 31_000_000);
        lib.ScanMedia();
        Assert.Equal(1, lib.GetStats().Files);
        Directory.Delete(Movies, true);                                // the drive is not there
        lib.ScanMedia();
        lib.Scan();
        Assert.Equal(1, lib.GetStats().Files);
    }

    [Fact]
    public void AddFromCheckAppendsAfterAMissingNewlineAndRemembersChoices()
    {
        using var lib = NewLibrary(false, "Heat.1995.720p");
        File.WriteAllText(Path.Combine(Lists, "added.txt"), "Old Line");
        lib.Scan();
        Assert.Equal(1, lib.AddFromCheck(new[] { new AddItem("Pick Me 2010 720p", true, 3), new AddItem("Heat.1995.720p") }, false));   // the second is already in the lists
        Assert.Equal(1, lib.AddFromCheck(new[] { new AddItem("No Imdb 2011", true, null) }, false));
        var lines = File.ReadAllLines(Path.Combine(Lists, "added.txt"));
        Assert.Equal(new[] { "Old Line", "Pick Me 2010 720p", "No Imdb 2011" }, lines);
        lib.Scan();
        var movies = lib.LoadMovies();
        var pick = movies.Single(m => m.Files[0].Raw == "Pick Me 2010 720p");
        Assert.Equal("tt0000003", pick.K);
        Assert.Equal("manual", pick.Conf);
        var none = movies.Single(m => m.Files[0].Raw == "No Imdb 2011");
        Assert.Equal("none", none.Files[0].Conf);   // chosen as not on IMDb
        Assert.StartsWith("f", none.K);
    }

    [Fact]
    public void ProbeMediaFillsMediaInfoAndMovieFiles()
    {
        if (!OperatingSystem.IsWindows()) return;
        using var lib = NewLibrary(false, "Heat.1995.720p", "Alien.1979.1080p");
        lib.Scan();
        Touch("Heat.1995.720p.mkv", 100);
        Touch("Alien.1979.1080p.mkv", 100);
        string ff = Path.Combine(root, "ffprobe.cmd"), ffBad = Path.Combine(root, "bad.cmd");
        File.WriteAllText(Path.Combine(root, "out.json"), @"{""streams"":[{""codec_type"":""video"",""codec_name"":""h264"",""width"":1280,""height"":536},{""codec_type"":""audio"",""tags"":{""language"":""eng""}},{""codec_type"":""subtitle"",""tags"":{""language"":""fas""}}],""format"":{""duration"":""600.2"",""bit_rate"":""1000""}}");
        File.WriteAllText(ff, "@echo off\r\nfor %%A in (%*) do set last=%%~A\r\nif \"%last:Alien=%\"==\"%last%\" (type \"%~dp0out.json\") else (exit /b 1)\r\n");
        lib.SaveSettings(new Settings { ListDir = Lists, MediaDirs = new() { Movies }, FfprobePath = ff });
        lib.ScanMedia();
        var before = lib.ProbeStatus();
        Assert.Equal(ff, before.Ffprobe);
        Assert.Equal(2, before.Files);
        Assert.Equal(0, before.Read);

        lib.ProbeMedia();
        var st = lib.ProbeStatus();
        Assert.Equal(1, st.Read);
        Assert.Equal(1, st.Failed);
        var movies = lib.LoadMovies();
        var f = movies.Single(m => m.PTitle == "Heat").Files[0];
        Assert.Equal(600, f.Dur);
        Assert.Equal(1280, f.W);
        Assert.Equal(536, f.H);
        Assert.Equal("h264", f.VCodec);
        Assert.Equal(1000, f.Br);
        Assert.Equal(new[] { "English" }, f.ALang);
        Assert.Equal(new[] { "Persian" }, f.SLang);
        var failed = movies.Single(m => m.PTitle == "Alien").Files[0];
        Assert.Null(failed.Dur);
        Assert.Empty(failed.ALang);

        // unchanged files are not read again; a changed file is
        File.WriteAllText(Path.Combine(root, "out.json"), @"{""streams"":[{""codec_type"":""video"",""width"":1920,""height"":800}],""format"":{}}");
        lib.ProbeMedia();
        Assert.Equal(600, lib.LoadMovies().Single(m => m.PTitle == "Heat").Files[0].Dur);
        File.AppendAllText(Path.Combine(Movies, "Heat.1995.720p.mkv"), "more");
        lib.ScanMedia();
        lib.ProbeMedia();
        Assert.Equal(1920, lib.LoadMovies().Single(m => m.PTitle == "Heat").Files[0].W);
    }

    [Fact]
    public void ProbeMediaNeedsFfprobe()
    {
        using var lib = NewLibrary(false, "Heat.1995.720p");
        lib.SaveSettings(new Settings { ListDir = Lists, FfprobePath = "" });
        if (Probe.FindFfprobe() != null) return;   // this PC has one on its PATH
        Assert.Contains("ffprobe was not found", Assert.Throws<InvalidOperationException>(lib.ProbeMedia).Message);
    }

    [Fact]
    public void ScanMediaRunsAsALibraryJob()
    {
        if (!OperatingSystem.IsWindows()) return;
        using var lib = NewLibrary(false, "Heat.1995.720p");
        lib.Scan();
        Touch("Heat.1995.720p.mkv", 100);
        lib.ScanMedia();
        Assert.True(lib.Start(lib.ScanMedia));
        for (int i = 0; i < 200 && lib.Job.Running; i++) Thread.Sleep(25);
        Assert.False(lib.Job.Running);
        Assert.Null(lib.Job.Error);
    }

    [Fact]
    public void RevealOnlyOpensScannedFiles()
    {
        using var lib = NewLibrary(false, "Heat.1995.720p");
        lib.Scan();
        Touch("Heat.1995.720p.mkv", 100);
        Assert.Throws<FileNotFoundException>(() => lib.Reveal(Path.Combine(Movies, "Heat.1995.720p.mkv")));   // not scanned yet
        Assert.Throws<FileNotFoundException>(() => lib.Reveal(@"C:\Windows\notepad.exe"));
        Assert.Equal("/select,\"C:\\a b\\c.mkv\"", Library.RevealArgs(@"C:\a b\c.mkv"));
    }

    // ---------- list check ----------
    Library CheckLibrary()
    {
        var lib = NewLibrary(true, "The.Godfather.1972.1080p.BluRay", "Heat.1995.720p", "Heat.1995.1080p.WEB-DL");
        lib.Update();
        return lib;
    }

    [Fact]
    public void CheckComparesWithTheLibrary()
    {
        using var lib = CheckLibrary();
        var r = lib.CheckList(new CheckSource(Name: "pasted", Text: string.Join('\n',
            "The.Godfather.1972.720p.BluRay",       // worse
            "Heat.1995.2160p.BluRay",               // better (library best is 1080p WEB-DL)
            "Heat.1995.720p",                       // same raw as a library entry
            "Breaking.Bad.2008.1080p",              // new
            "Unknown.Movie.2020.1080p",             // unmatched
            "Breaking.Bad.2008.1080p",              // same name twice
            "Breaking.Bad.2008.720p",               // same movie, other name -> twin
            "Heat.1995.Farsi.Dubbed",               // other audio version
            "Godfather 1972")));                    // bare title, no quality info

        string St(string raw) => r.Items.Single(i => i.Raw == raw).Status;
        Assert.Equal("worse", St("The.Godfather.1972.720p.BluRay"));
        Assert.Equal("better", St("Heat.1995.2160p.BluRay"));
        Assert.Equal("have", St("Heat.1995.720p"));
        Assert.Equal("new", St("Breaking.Bad.2008.1080p"));
        Assert.Equal("unmatched", St("Unknown.Movie.2020.1080p"));
        Assert.Equal("version", St("Heat.1995.Farsi.Dubbed"));
        Assert.Equal("duplicate", St("Godfather 1972"));
        Assert.Equal(8, r.Items.Count);
        Assert.Equal(9, r.Entries);
        Assert.Equal(2, r.Items.Single(i => i.Raw == "Breaking.Bad.2008.1080p").Copies);
        Assert.Equal("pasted", r.Source);
        Assert.Equal("pasted list", lib.CheckList(new CheckSource(Text: "Heat 1995")).Source);
        Assert.Equal(1, r.Count("worse"));
        Assert.Equal(2, r.Count("new"));
        Assert.Equal(7, r.Count("twins"));   // every item whose movie is in the list more than once

        var heat = r.Items.Single(i => i.Raw == "Heat.1995.2160p.BluRay");
        Assert.Equal("2160p BluRay", heat.Quality);
        Assert.Equal(2, heat.Lib!.Count);
        Assert.Contains(heat.Lib, f => f.Raw == "Heat.1995.1080p.WEB-DL" && f.Quality == "1080p WEB-DL");
        Assert.Equal("Heat", heat.LibTitle);
        Assert.Equal("Heat", heat.Imdb!.Title);
        Assert.Equal(1995, heat.Imdb.Year);
        Assert.Equal(2, heat.Tconst);
        Assert.Equal(new[] { "Breaking.Bad.2008.720p" }, r.Items.Single(i => i.Raw == "Breaking.Bad.2008.1080p").Twins);

        // the library has the Godfather in the list, Heat is in the list too; only the Godfather is absent from... both are present: Godfather checked
        Assert.Empty(r.Missing);
        Assert.Equal(0, r.MissingCount);

        // wishlist actions: new, better and other versions only
        var wish = CheckResult.WishlistItems(r.Items);
        Assert.Equal(new[] { "Breaking Bad", "Breaking Bad", "Heat", "Heat" }, wish.Select(w => w.Title).OrderBy(x => x));
        Assert.All(wish, w => Assert.Equal("List check", w.Source));
        Assert.Contains(wish, w => w.Note == "Better quality" && w.Tconst == 2 && w.Year == 1995);
        Assert.Equal(4, r.Tab("all").Count(i => i.Wanted));
        Assert.Equal(7, r.Tab("all").Count(i => i.Addable));
        Assert.Single(r.Tab("unmatched"));

        // report
        string csv = Encoding.UTF8.GetString(r.ToCsv());
        Assert.StartsWith("\uFEFFStatus,Name,Quality,IMDb,IMDb title,IMDb year,Match,Problem,In library,Also in this list,Path,Size GB\r\n", csv);
        Assert.Contains("worse,The.Godfather.1972.720p.BluRay,720p BluRay,tt0000001,The Godfather,1972,", csv);
        Assert.Contains("Heat.1995.720p (720p) | Heat.1995.1080p.WEB-DL (1080p WEB-DL)", csv);
    }

    [Fact]
    public void CheckListsWhatOnlyTheLibraryHas()
    {
        using var lib = CheckLibrary();
        var r = lib.CheckList(new CheckSource(Text: "Breaking.Bad.2008.1080p"));
        Assert.Equal(2, r.MissingCount);
        Assert.Equal(new[] { "Heat", "The Godfather" }, r.Missing.Select(m => m.Title));
        Assert.Equal(new[] { "Heat.1995.720p", "Heat.1995.1080p.WEB-DL" }, r.Missing[0].Files);
        string csv = Encoding.UTF8.GetString(r.ToCsv());
        Assert.Contains("only in library,Heat.1995.720p | Heat.1995.1080p.WEB-DL,,tt0000002,Heat,1995,,,,,,", csv);
    }

    [Fact]
    public void CheckReadsFilesFoldersAndImdbIds()
    {
        using var lib = CheckLibrary();
        string csv = Path.Combine(root, "mine.csv");
        File.WriteAllText(csv, "Title,Year,IMDb\nHeat,1995,tt0000002\nBreaking Bad,2008,\nNobody,1801,tt0000003\n", new UTF8Encoding(true));
        var r = lib.CheckList(new CheckSource(Path: csv));
        Assert.Equal(csv, r.Source);
        Assert.Equal("imdb id", r.Items[0].Conf);
        Assert.Equal("duplicate", r.Items[0].Status);       // 'Heat 1995': bare title, already in the library
        Assert.Equal("new", r.Items[1].Status);
        Assert.Equal(3, r.Items[2].Tconst);                  // the id column wins over the name
        Assert.Equal("imdb id", r.Items[2].Conf);

        string folder = Path.Combine(root, "drive");
        Directory.CreateDirectory(Path.Combine(folder, "Heat (1995) 1080p"));
        File.WriteAllBytes(Path.Combine(folder, "Heat (1995) 1080p", "x.mkv"), new byte[5]);
        File.WriteAllBytes(Path.Combine(folder, "Some.Show.S02E05.mkv"), new byte[5]);
        var progress = new List<string>();
        var f = lib.CheckList(new CheckSource(Path: folder), (s, d, t) => progress.Add(s));
        Assert.Equal(2, f.Entries);
        Assert.Equal("Done", progress[^1]);
        var heat = f.Items.Single(i => i.Raw == "Heat (1995) 1080p");
        Assert.Equal(5, heat.Size);
        Assert.EndsWith("x.mkv", heat.Path);
        Assert.NotEqual("new", heat.Status);
        var ep = f.Items.Single(i => i.Episode);
        Assert.Equal("unmatched", ep.Status);
        Assert.StartsWith("TV episode", ep.Reason);
        Assert.Equal(1, f.Count("unsure"));

        Assert.Throws<FileNotFoundException>(() => lib.CheckList(new CheckSource(Path: Path.Combine(root, "nope"))));
        Assert.Throws<ArgumentException>(() => lib.CheckList(new CheckSource(Text: "  \n ")));
    }

    [Fact]
    public void UnsureMatchesComeWithChoices()
    {
        using var lib = CheckLibrary();
        var r = lib.CheckList(new CheckSource(Text: "Heat"));   // no year: ambiguity flagged
        var it = Assert.Single(r.Items);
        Assert.Equal("noyear", it.Conf);
        Assert.NotNull(it.Reason);
        Assert.NotNull(it.Cands);
        Assert.Equal(1, r.Count("unsure"));
    }

    [Fact]
    public void OnlineHookIsUsedForUnknownNamesOnly()
    {
        using var lib = CheckLibrary();
        var asked = new List<string>();
        lib.CheckOnlineFind = p => { asked.Add(p.Title); return p.Title == "Unknown Movie" ? 3 : null; };
        var r = lib.CheckList(new CheckSource(Text: "Unknown.Movie.2020.1080p\nHeat.1995.720p"));
        Assert.Equal(new[] { "Unknown Movie" }, asked);
        var u = r.Items[0];
        Assert.Equal("online", u.Conf);
        Assert.Equal(3, u.Tconst);
        Assert.NotNull(u.Reason);
        Assert.Equal("new", u.Status);
    }

    [Fact]
    public void StartCheckRunsInTheBackground()
    {
        using var lib = CheckLibrary();
        Assert.True(lib.StartCheck(new CheckSource(Text: "Breaking.Bad.2008.1080p")));
        for (int i = 0; i < 400 && lib.Check.Running; i++) Thread.Sleep(25);
        Assert.False(lib.Check.Running);
        Assert.Null(lib.Check.Error);
        Assert.True(lib.Check.HasResult);
        Assert.Equal("new", lib.Check.Result!.Items[0].Status);

        Assert.True(lib.StartCheck(new CheckSource(Path: Path.Combine(root, "missing.txt"))));
        for (int i = 0; i < 400 && lib.Check.Running; i++) Thread.Sleep(25);
        Assert.StartsWith("FileNotFoundError: Not found:", lib.Check.Error);
        Assert.False(lib.Check.HasResult);
    }

    [Fact]
    public void StartCheckNeedsTheIndex()
    {
        using var lib = NewLibrary(false, "Heat.1995.720p");
        Assert.Contains("IMDb index", Assert.Throws<InvalidOperationException>(() => lib.StartCheck(new CheckSource(Text: "Heat"))).Message);
    }

    // ---------- rename plan ----------
    static Movie MovieOf(string k, string title, string? en, int? year, params (string Path, string? Res, string? Src, bool Fa)[] files)
    {
        var m = new Movie { K = k, Title = title, En = en, Year = year };
        foreach (var f in files) m.Files.Add(new MovieFile { Res = f.Res, Src = f.Src, Fa = f.Fa, Paths = new() { f.Path } });
        return m;
    }

    [Fact]
    public void CleanMakesSafeFileNames()
    {
        Assert.Equal("3.10 to Yuma", RenamePlanner.Clean("3:10 to Yuma"));
        Assert.Equal("Alien - Covenant", RenamePlanner.Clean("Alien: Covenant"));
        Assert.Equal("AC - DC", RenamePlanner.Clean("AC/DC"));
        Assert.Equal("What", RenamePlanner.Clean("What?*<>\"|  "));
        Assert.Equal("Dots", RenamePlanner.Clean("..Dots.. "));
        Assert.Equal("a b", RenamePlanner.Clean("a\u0001   b"));
    }

    [Fact]
    public void RenamePlanProposesNamesAndFindsConflicts()
    {
        var movies = new List<Movie>
        {
            MovieOf("tt0000001", "Alien Covenant", "Alien: Covenant", 2017, (@"D:\m\alien.cov.mkv", "1080p", "BluRay", false)),
            MovieOf("tt0000002", "Yuma", "3:10 to Yuma", 2007, (@"D:\m\Yuma.mkv", "720p", null, true)),
            MovieOf("tt0000003", "Heat", null, 1995, (@"D:\m\Heat (1995) [1080p WEB-DL].mkv", "1080p", "WEB-DL", false)),
            MovieOf("tt0000004", "Dup", null, 2001, (@"D:\a\one.mkv", "720p", null, false), (@"D:\a\two.mkv", "720p", null, false), (@"D:\b\one.mkv", "720p", null, false)),
            MovieOf("f9", "Unknown", null, null, (@"E:\x\unk.avi", null, null, false)),
            MovieOf("f10", "", null, 2000, (@"E:\x\empty.avi", null, null, false)),
            MovieOf("tt0000005", "No path", null, 2000),
        };
        var rows = RenamePlanner.Plan(movies);
        Assert.Equal(7, rows.Count);

        var alien = rows[0];
        Assert.Equal("Alien - Covenant (2017) [1080p BluRay].mkv", alien.New);
        Assert.Equal("alien.cov.mkv", alien.Old);
        Assert.Equal(@"D:\m\Alien - Covenant (2017) [1080p BluRay].mkv", alien.Target);
        Assert.False(alien.Same);
        Assert.Equal("3.10 to Yuma (2007) [720p].mkv", rows[1].New);   // the default pattern has no {dub}
        Assert.True(rows[2].Same);
        Assert.Equal("rename", alien.Status);
        Assert.Equal("already named", rows[2].Status);
        // 'Dup' one.mkv and two.mkv in D:\a collide; D:\b\one.mkv is another folder
        Assert.True(rows[3].Conflict && rows[4].Conflict);
        Assert.False(rows[5].Conflict);
        Assert.Equal("Unknown.avi", rows[6].New);

        var s = RenamePlanner.Summary(rows);
        Assert.Equal(7, s.Total);
        Assert.Equal(2, s.Conflicts);
        Assert.Equal(1, s.Same);
        Assert.Equal(4, s.Todo);
        Assert.Equal(4, s.Sample.Count);
    }

    [Fact]
    public void PlanFilesAreWrittenLikeThePythonOnes()
    {
        var rows = RenamePlanner.Plan(new[]
        {
            MovieOf("tt0000001", "Alien", "Alien: Covenant", 2017, (@"D:\m\alien.cov.mkv", "1080p", "BluRay", false)),
            MovieOf("tt0000003", "Heat", null, 1995, (@"D:\m\Heat (1995).mkv", null, null, false)),
            MovieOf("tt0000004", "Dup", null, 2001, (@"D:\a\one.mkv", null, null, false), (@"D:\a\two.mkv", null, null, false)),
        });
        string bat = Encoding.UTF8.GetString(RenamePlanner.Bat(rows));
        Assert.Equal(string.Join("\r\n", new[]
        {
            "@echo off", "chcp 65001 >nul", "rem MovieShelf rename plan. Read it first, then run it. Lines starting with rem are skipped.",
            @"if exist ""D:\m\Alien - Covenant (2017) [1080p BluRay].mkv"" (echo SKIPPED - the new name is taken) else ren ""D:\m\alien.cov.mkv"" ""Alien - Covenant (2017) [1080p BluRay].mkv""",
            @"rem SKIPPED (two items would get this name): D:\a\one.mkv",
            @"rem SKIPPED (two items would get this name): D:\a\two.mkv",
            "echo Done.", "pause", "",
        }), bat);
        Assert.Equal((byte)'@', RenamePlanner.Bat(rows)[0]);

        var csvBytes = RenamePlanner.Csv(rows);
        Assert.Equal(new byte[] { 0xEF, 0xBB, 0xBF }, csvBytes.Take(3));
        string csv = Encoding.UTF8.GetString(csvBytes, 3, csvBytes.Length - 3);
        Assert.Equal("Type,Folder,Current name,New name,Status\r\n" +
                     "File,D:\\m,alien.cov.mkv,Alien - Covenant (2017) [1080p BluRay].mkv,rename\r\n" +
                     "File,D:\\m,Heat (1995).mkv,Heat (1995).mkv,already named\r\n" +
                     "File,D:\\a,one.mkv,Dup (2001).mkv,conflict: two items would get this name\r\n" +
                     "File,D:\\a,two.mkv,Dup (2001).mkv,conflict: two items would get this name\r\n", csv);

        var listBytes = RenamePlanner.NameList(rows);
        Assert.Equal(new byte[] { 0xEF, 0xBB, 0xBF }, listBytes.Take(3));
        Assert.Equal("Alien - Covenant (2017) [1080p BluRay]\r\n", Encoding.UTF8.GetString(listBytes, 3, listBytes.Length - 3));

        Assert.Equal("MovieShelf rename plan.bat", RenamePlanner.PlanFile("bat", rows)!.Name);
        Assert.Equal("text/csv; charset=utf-8", RenamePlanner.PlanFile("csv", rows)!.ContentType);
        Assert.Equal("MovieShelf new names.txt", RenamePlanner.PlanFile("list", rows)!.Name);
        Assert.Null(RenamePlanner.PlanFile("zip", rows));

        string saved = RenamePlanner.Save("bat", rows, root);
        Assert.Equal(Path.Combine(root, "MovieShelf rename plan.bat"), saved);
        Assert.Equal(bat, File.ReadAllText(saved));
    }

    [Fact]
    public void RenamePlanWorksOnLoadedMovies()
    {
        using var lib = NewLibrary(true, "The.Godfather.1972.1080p.BluRay");
        lib.Update();
        Touch("The.Godfather.1972.1080p.BluRay.mkv", 10);
        lib.ScanMedia();
        var rows = RenamePlanner.Plan(lib.LoadMovies());
        var r = Assert.Single(rows);
        Assert.Equal("The Godfather (1972) [1080p BluRay].mkv", r.New);
        Assert.False(r.Same);
        Assert.True(File.Exists(Path.Combine(Movies, "The.Godfather.1972.1080p.BluRay.mkv")));   // nothing was renamed
    }
}
