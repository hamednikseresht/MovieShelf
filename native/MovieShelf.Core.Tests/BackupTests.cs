using System.IO.Compression;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.Data.Sqlite;
using MovieShelf.Core;
using Xunit;

namespace MovieShelf.Core.Tests;

/// <summary>Package D: personal and full backups (also files written by the Python app), views, wishlist, statistics, CSV, first run.</summary>
public class BackupAndMoreTests : IDisposable
{
    readonly string root = Path.Combine(Path.GetTempPath(), "movieshelf-testD-" + Guid.NewGuid().ToString("N"));
    string DbFile => Path.Combine(root, "data", "library.db");

    public BackupAndMoreTests() => Directory.CreateDirectory(Path.Combine(root, "data"));

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try { Directory.Delete(root, true); } catch (IOException) { }
    }

    static void MakeIndex(string path)
    {
        using var c = new SqliteConnection($"Data Source={path};Pooling=False");
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

    string Lists => Path.Combine(root, "lists");

    Library NewLibrary(bool settings = true, params string[] lines)
    {
        Directory.CreateDirectory(Lists);
        if (lines.Length > 0) File.WriteAllLines(Path.Combine(Lists, "a.txt"), lines);
        MakeIndex(Path.Combine(root, "data", "imdb_index.db"));
        var lib = new Library(root);
        if (settings) lib.SaveSettings(new Settings { ListDir = Lists });
        return lib;
    }

    /// <summary>Runs SQL on a second connection (the library keeps its own open).</summary>
    void Sql(string sql)
    {
        using var c = new SqliteConnection($"Data Source={DbFile};Pooling=False");
        c.Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }

    T Q<T>(string sql)
    {
        using var c = new SqliteConnection($"Data Source={DbFile};Pooling=False");
        c.Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = sql;
        return (T)Convert.ChangeType(cmd.ExecuteScalar()!, typeof(T));
    }

    static string Fixture(string name) => Path.Combine(AppContext.BaseDirectory, "pyfixtures", name);

    static void Wait(Library lib)
    {
        for (int i = 0; i < 600 && lib.Job.Running; i++) Thread.Sleep(50);
        Assert.False(lib.Job.Running);
    }

    // ---------- personal backup ----------
    [Fact]
    public void PersonalBackupRoundTrip()
    {
        using var lib = NewLibrary(true, "Heat.1995.720p", "Heat.1995.1080p");
        lib.Update();
        lib.SetUser(new[] { "tt0000002" }, fav: true, rating: 9, notes: "good");
        lib.SaveView("Mine", new JsonObject { ["q"] = "x" });
        Assert.Equal(1, lib.WishAdd(new[] { new WishNew("Heat", 1995), new WishNew("Heat", 1995) }));
        lib.Dismiss("dupes:tt0000002:1-2");
        var snap = lib.MakeBackup();
        Assert.Equal(new[] { "app", "format", "created", "user", "manual", "forced", "dismissed", "views", "wishlist", "fa_titles", "removed", "lists" }, snap.Select(kv => kv.Key));
        Sql("DELETE FROM user; DELETE FROM views; DELETE FROM wishlist; DELETE FROM dismissed;");
        var n = lib.RestoreBackup(JsonNode.Parse(snap.ToJsonString()));
        Assert.Equal(new BackupCounts(1, 0, 1, 1, 1, 0), n);
        Assert.False(n.NeedsUpdate);
        Assert.Equal("good", Q<string>("SELECT notes FROM user"));
        Assert.Equal(1, Q<int>("SELECT count(*) FROM dismissed"));
        Assert.Equal("x", lib.ListViews().Single().State["q"]!.GetValue<string>());
    }

    [Fact]
    public void RestoresABackupWrittenByPython()
    {
        using var lib = NewLibrary();
        var n = lib.RestoreBackupFile(Fixture("python_backup.json"));
        Assert.Equal(new BackupCounts(2, 2, 1, 2, 2, 1), n);
        Assert.True(n.NeedsUpdate);
        Assert.Equal("Très bon – \"quoted\", café", Q<string>("SELECT notes FROM user WHERE mkey='tt0000001'"));
        Assert.Equal(9, Q<int>("SELECT myrating FROM user WHERE mkey='tt0000001'"));
        Assert.Equal("2026-10-31 09:05:58", Q<string>("SELECT watched_at FROM user WHERE mkey='tt0000001'"));
        Assert.Equal(2, Q<int>("SELECT count(*) FROM forced"));   // manual matches wait in 'forced'
        Assert.Equal(5, Q<int>("SELECT tconst FROM forced WHERE raw='Future.Movie.2030'"));
        Assert.Equal("آملی", Q<string>("SELECT title FROM fa_titles WHERE tconst=1 AND source='user'"));
        var views = lib.ListViews();
        Assert.Equal(new[] { "Dramas 90s", "پدرخوانده" }, views.Select(v => v.Name));
        Assert.Equal("heat", views.Single(v => v.Name == "Dramas 90s").State["F"]!["q"]!.GetValue<string>());
        var wish = lib.WishlistItems();
        Assert.Equal(new[] { "Alien", "Ça" }, wish.Select(w => w.Title));   // wanted first, found last
        Assert.Equal("found", wish.Single(w => w.Title == "Ça").Status);
        Assert.Equal(1396484, wish.Single(w => w.Title == "Ça").Tconst);
        // and the other way: the native file has the keys the Python restore reads
        var py = JsonNode.Parse(File.ReadAllText(Fixture("python_backup.json")))!.AsObject();
        // the fixture is an older Python backup: every key it has is in ours; the newer keys (removed, lists) come on top
        Assert.Equal(py.Select(kv => kv.Key), lib.MakeBackup().Select(kv => kv.Key).Take(py.Count));
        var mine = lib.MakeBackup();
        Assert.Equal(py["user"]!.ToJsonString(), mine["user"]!.ToJsonString());
        Assert.Equal(py["wishlist"]!.ToJsonString(), mine["wishlist"]!.ToJsonString());
        Assert.Equal(py["fa_titles"]!.ToJsonString(), mine["fa_titles"]!.ToJsonString());
        Assert.Equal(2, mine["forced"]!.AsArray().Count);   // the manual match joined the forced ones
    }

    [Fact]
    public void RejectsOtherFilesAndPaths()
    {
        using var lib = NewLibrary();
        Assert.Throws<InvalidDataException>(() => lib.RestoreBackup(JsonNode.Parse("{\"hello\":1}")));
        Assert.Throws<InvalidDataException>(() => lib.RestoreBackup((JsonNode?)null));
        Assert.Throws<ArgumentException>(() => lib.LoadBackup("../../secret.json"));
        Assert.Throws<ArgumentException>(() => lib.LoadBackup("other.json"));
        File.WriteAllText(Path.Combine(root, "x.json"), "not json");
        Assert.Throws<InvalidDataException>(() => lib.RestoreBackupFile(Path.Combine(root, "x.json")));
    }

    [Fact]
    public void AutomaticBackupOnceADayAndPruned()
    {
        using var lib = NewLibrary(true, "Heat.1995.720p");
        lib.Update();
        foreach (var f in Directory.GetFiles(Lists, "*.txt")) File.Delete(f);   // the names in the lists are personal data too
        Assert.Null(lib.AutoBackup());   // nothing personal yet
        lib.SetUser(new[] { "tt0000002" }, fav: true);
        for (int d = 1; d <= 20; d++) File.WriteAllText(Path.Combine(lib.BackupsDir, $"personal-2020-01-{d:00}.json"), "{}");
        string? p = lib.AutoBackup();
        Assert.NotNull(p);
        Assert.Null(lib.AutoBackup());
        var list = lib.ListBackups();
        Assert.Equal(14, list.Count);
        Assert.Equal(Path.GetFileName(p), list[0].Name);   // newest first
        Assert.DoesNotContain(list, b => b.Name.EndsWith("2020-01-06.json"));
        Assert.Contains(list, b => b.Name.EndsWith("2020-01-08.json"));
        Assert.Equal(1, lib.RestoreBackup(list[0].Name).User);
    }

    [Fact]
    public void TheQuickBackupKeepsTheListsAndTodaysCopyFollowsChanges()
    {
        using var lib = NewLibrary(true, "Heat.1995.720p");
        File.WriteAllLines(Path.Combine(Lists, "b.txt"), new[] { "Alien.1979.1080p", "Ronin.1998.1080p" });
        string? path = lib.AutoBackup();
        Assert.NotNull(path);
        var saved = JsonNode.Parse(File.ReadAllText(path!))!;
        Assert.Equal(new[] { "Alien.1979.1080p", "Ronin.1998.1080p" }, saved["lists"]!["b.txt"]!.AsArray().Select(n => (string)n!));
        Assert.Null(lib.AutoBackup());                                                        // nothing changed
        File.WriteAllLines(Path.Combine(Lists, "b.txt"), new[] { "Alien.1979.1080p", "Ronin.1998.1080p", "Seven.1995.1080p" });
        Assert.Equal(path, lib.AutoBackup());                                                 // a new name: today's copy is rewritten
        Assert.Equal(3, JsonNode.Parse(File.ReadAllText(path!))!["lists"]!["b.txt"]!.AsArray().Count);
        File.Delete(Path.Combine(Lists, "b.txt"));
        File.WriteAllText(Path.Combine(Lists, "a.txt"), "Other.2000.720p\n");               // a.txt changed meanwhile: it is not rewritten
        var n = lib.RestoreBackup(Path.GetFileName(path!));
        Assert.Equal(3 + 1, n.Lists);                                                         // b.txt's three names and the one of a.txt no list has now
        Assert.Equal("Alien.1979.1080p\r\nRonin.1998.1080p\r\nSeven.1995.1080p\r\n", File.ReadAllText(Path.Combine(Lists, "b.txt")));
        Assert.Equal("Other.2000.720p\n", File.ReadAllText(Path.Combine(Lists, "a.txt")));
        Assert.Contains("Heat.1995.720p", File.ReadAllText(Path.Combine(Lists, "a (restored).txt")));
    }

    // ---------- full backup ----------
    void Prepare(Library lib)
    {
        File.WriteAllLines(Path.Combine(Lists, "a.txt"), new[] { "Heat.1995.1080p" });
        File.WriteAllBytes(Path.Combine(lib.PosterDir, "tt0000001.jpg"), Encoding.ASCII.GetBytes("JPEG"));
        lib.SaveSettings(new Settings { ListDir = Lists, TmdbKey = "SECRET-T", OmdbKey = "SECRET-O" });
        Sql("INSERT INTO titles(tconst, type, title, year) VALUES (1, 'movie', 'Alpha', 2000)");
        lib.SetUser(new[] { "tt0000001" }, fav: true, notes: "kept");
        lib.Update();
    }

    [Fact]
    public void FullBackupEverythingComesBack()
    {
        using var lib = NewLibrary(true, "Heat.1995.1080p");
        Prepare(lib);
        string zip = Path.Combine(root, "b.zip");
        var all = new FullBackupOptions { Settings = true, Keys = true, Lists = true, Posters = true };
        var info = lib.GetFullBackupInfo();
        Assert.True(info.Keys);
        Assert.Equal(1, info.Lists.N);
        Assert.Equal(1, info.Posters.N);
        Assert.Equal(0, info.Imdb.N - 1);   // only the index file exists
        Assert.Equal(zip, lib.SaveFullBackup(zip, all));
        var m = Library.InspectFullBackup(zip);
        Assert.True(m.HasKeys);
        Assert.True(m.HasSettings);
        Assert.Equal(new FullBackupHas(1, 1, 0), m.Has);
        Assert.Equal(Lists, m.ListDir);
        using (var z = ZipFile.OpenRead(zip))
            Assert.Equal(new[] { "manifest.json", "library.db", "settings.json", "lists/a.txt", "posters/tt0000001.jpg" }, z.Entries.Select(e => e.FullName));
        // lose everything
        Sql("DELETE FROM files; DELETE FROM titles; DELETE FROM user;");
        lib.SaveSettings(new Settings { ListDir = Lists });
        File.Delete(Path.Combine(Lists, "a.txt"));
        File.Delete(Path.Combine(lib.PosterDir, "tt0000001.jpg"));
        int v = lib.Version;
        var res = lib.RestoreFullBackup(zip, all);
        Assert.True(res.Library);
        Assert.Equal(1, res.Lists);
        Assert.Equal(1, res.Posters);
        Assert.Null(res.Imdb);
        Assert.Empty(res.Warnings);
        Assert.True(lib.Version > v);
        Assert.Equal(res, lib.RestoreResult);
        Assert.Equal(1, Q<int>("SELECT count(*) FROM files"));
        Assert.Equal("kept", Q<string>("SELECT notes FROM user"));
        Assert.Equal("SECRET-T", lib.LoadSettings().TmdbKey);
        Assert.True(File.Exists(Path.Combine(Lists, "a.txt")));
        Assert.True(File.Exists(Path.Combine(lib.PosterDir, "tt0000001.jpg")));
        Assert.True(File.Exists(Path.Combine(res.Safety, "library.db")));   // what existed before is kept
        Assert.Single(lib.LoadMovies(), x => x.K == "tt0000002");   // the open connection sees the restored data
        lib.SetUser(new[] { "tt0000001" }, fav: false);                   // and can still write
    }

    [Fact]
    public void FullBackupKeysCanBeLeftOutAndAnEmptyKeyNeverErasesYours()
    {
        using var lib = NewLibrary(true, "Heat.1995.1080p");
        Prepare(lib);
        string zip = Path.Combine(root, "b.zip");
        lib.SaveFullBackup(zip, new FullBackupOptions());
        Assert.False(Library.InspectFullBackup(zip).HasKeys);
        using (var z = ZipFile.OpenRead(zip))
        {
            using var r = new StreamReader(z.GetEntry("settings.json")!.Open());
            Assert.DoesNotContain("SECRET", r.ReadToEnd());
            Assert.Null(z.GetEntry("lists/a.txt"));
        }
        lib.SaveSettings(new Settings { ListDir = Lists, TmdbKey = "NEW-KEY" });
        lib.RestoreFullBackup(zip, new FullBackupOptions { Settings = true, Keys = true });
        Assert.Equal("NEW-KEY", lib.LoadSettings().TmdbKey);
    }

    [Fact]
    public void FullBackupUnsafeNamesAndBadFilesAreRefused()
    {
        Assert.False(Library.IsSafeMember("../evil.txt"));
        Assert.False(Library.IsSafeMember("lists/../../evil.txt"));
        Assert.False(Library.IsSafeMember("C:/evil.txt"));
        Assert.False(Library.IsSafeMember("/abs.txt"));
        Assert.False(Library.IsSafeMember(""));
        Assert.True(Library.IsSafeMember("posters/a.jpg"));
        using var lib = NewLibrary(true, "Heat.1995.1080p");
        string notzip = Path.Combine(root, "x.zip");
        File.WriteAllText(notzip, "nope");
        var e = Assert.Throws<InvalidDataException>(() => Library.InspectFullBackup(notzip));
        Assert.Equal("This is not a MovieShelf full backup (.zip).", e.Message);
        Assert.Throws<InvalidDataException>(() => Library.InspectFullBackup(Path.Combine(root, "missing.zip")));
        // a zip with a path that climbs out of the folder
        string good = Path.Combine(root, "good.zip");
        lib.SaveFullBackup(good, new FullBackupOptions());
        string evil = Path.Combine(root, "evil.zip");
        File.Copy(good, evil);
        using (var z = ZipFile.Open(evil, ZipArchiveMode.Update))
            using (var w = new StreamWriter(z.CreateEntry("lists/../evil.txt").Open())) w.Write("x");
        Assert.Throws<InvalidDataException>(() => lib.RestoreFullBackup(evil, new FullBackupOptions { Lists = true }));
        Assert.False(File.Exists(Path.Combine(root, "evil.txt")));
    }

    static void MinimalDbZip(string zipPath, int pageSize)
    {
        string tmp = zipPath + ".db";
        using (var c = new SqliteConnection($"Data Source={tmp};Pooling=False"))
        {
            c.Open();
            using var cmd = c.CreateCommand();
            cmd.CommandText = $"PRAGMA page_size={pageSize}; CREATE TABLE files(id INTEGER PRIMARY KEY, raw TEXT UNIQUE, list_file TEXT, present INT DEFAULT 1, added TEXT, title TEXT, alt_title TEXT, year INT, resolution TEXT, source TEXT, codec TEXT, bit10 INT, hdr INT, audio TEXT, farsi_dubbed INT, dual_audio INT, softsub INT, edition TEXT, grp TEXT, tconst INT, conf TEXT, score REAL, manual INT DEFAULT 0, matched INT DEFAULT 0); INSERT INTO files(raw) VALUES ('Old.Release.1999');";
            cmd.ExecuteNonQuery();
        }
        using var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create);
        using (var w = new StreamWriter(zip.CreateEntry("manifest.json").Open())) w.Write("{\"app\":\"MovieShelf\",\"format\":1,\"created\":\"2020-01-01 00:00:00\"}");
        zip.CreateEntryFromFile(tmp, "library.db");
    }

    [Theory]
    [InlineData(4096)]
    [InlineData(8192)]   // a different page size cannot be copied into the open WAL database: the file is swapped
    public void FullBackupFromAnOlderVersionGetsNewTablesAndColumns(int pageSize)
    {
        using var lib = NewLibrary(true, "Heat.1995.1080p");
        string zip = Path.Combine(root, "old.zip");
        MinimalDbZip(zip, pageSize);
        var m = Library.InspectFullBackup(zip);
        Assert.Equal("2020-01-01 00:00:00", m.Created);
        Assert.False(m.HasSettings);
        lib.RestoreFullBackup(zip, new FullBackupOptions());
        Assert.Equal(1, lib.WishAdd(new[] { new WishNew("Heat", 1995) }));
        lib.SaveView("v", new JsonObject());
        lib.SetUser(new[] { "f1" }, notes: "n");
        Assert.Equal("Old.Release.1999", Q<string>("SELECT raw FROM files"));
        Assert.Equal("wal", Q<string>("PRAGMA journal_mode"));
        Assert.Single(lib.LoadMovies());
    }

    [Fact]
    public void RestoresAFullBackupWrittenByPython()
    {
        using var lib = NewLibrary(true, "Heat.1995.1080p");
        lib.SaveSettings(new Settings { ListDir = Lists, TmdbKey = "MINE" });
        var m = Library.InspectFullBackup(Fixture("python_full.zip"));
        Assert.Equal(new FullBackupHas(1, 1, 0), m.Has);
        Assert.True(m.HasKeys);
        Assert.Equal(new[] { "Z:\\NoSuchMovies" }, m.MediaDirs);
        // library + posters + people + list files, settings left alone
        var res = lib.RestoreFullBackup(Fixture("python_full.zip"), new FullBackupOptions { Lists = true, Posters = true });
        Assert.Equal((1, 1), (res.Lists, res.Posters));
        Assert.True(File.Exists(Path.Combine(lib.PosterDir, "tt0000001.jpg")));
        Assert.True(File.Exists(Path.Combine(root, "data", "people", "nm0000001.jpg")));
        Assert.Equal("Heat.1995.720p", File.ReadAllText(Path.Combine(Lists, "a.txt")).Trim());
        Assert.Equal("MINE", lib.LoadSettings().TmdbKey);
        Assert.Equal(3, Q<int>("SELECT count(*) FROM files"));
        Assert.Equal("Très bon – \"quoted\", café", Q<string>("SELECT notes FROM user WHERE mkey='tt0000001'"));
        Assert.Equal(3, lib.LoadMovies().Count);
        // settings + keys: unknown keys survive, missing movie folders are reported
        var res2 = lib.RestoreFullBackup(Fixture("python_full.zip"), new FullBackupOptions { Settings = true, Keys = true });
        Assert.True(res2.Settings);
        Assert.Contains(res2.Warnings, w => w.StartsWith("Movie folders not found on this PC") && w.Contains("Z:\\NoSuchMovies"));
        var json = JsonNode.Parse(File.ReadAllText(lib.SettingsPath))!;
        Assert.Equal("SECRET-T", (string?)json["tmdb_key"]);
        Assert.Equal(2, (int?)json["parser_version"]);
        Assert.Equal("Z:\\NoSuchMovies", (string?)json["media_dirs"]![0]);
    }

    [Fact]
    public void NativeFullBackupHasThePythonLayout()
    {
        using var lib = NewLibrary(true, "Heat.1995.1080p");
        Prepare(lib);
        Directory.CreateDirectory(Path.Combine(root, "data", "people"));
        File.WriteAllBytes(Path.Combine(root, "data", "people", "nm0000002.jpg"), new byte[] { 1 });
        File.WriteAllBytes(Path.Combine(lib.ImdbDir.Length > 0 ? Directory.CreateDirectory(lib.ImdbDir).FullName : "", "title.ratings.tsv.gz"), new byte[] { 1, 2 });
        string zip = Path.Combine(root, "n.zip");
        lib.SaveFullBackup(zip, new FullBackupOptions { Keys = true, Lists = true, Posters = true, Imdb = true });
        using var z = ZipFile.OpenRead(zip);
        Assert.Equal(new[] { "manifest.json", "library.db", "settings.json", "lists/a.txt", "posters/tt0000001.jpg", "people/nm0000002.jpg", "imdb/title.ratings.tsv.gz", "imdb_index.db" },
            z.Entries.Select(e => e.FullName));
        var man = JsonNode.Parse(z.GetEntry("manifest.json")!.Open())!.AsObject();
        Assert.Equal(new[] { "app", "format", "created", "has_keys", "has", "list_dir", "media_dirs" }, man.Select(kv => kv.Key));
        Assert.Equal(2, (int?)man["has"]!["imdb"]);   // the files plus the index
        // the python module opens it: manifest + library.db + settings.json are all it needs (checked in the manual cross-check, see DELIVERY.md)
    }

    [Fact]
    public void FullBackupRunsAsJobsAndRestoreCannotBeStopped()
    {
        using var lib = NewLibrary(true, "Heat.1995.1080p");
        Prepare(lib);
        string zip = Path.Combine(root, "j.zip");
        Assert.Throws<ArgumentException>(() => lib.StartSaveFullBackup(Path.Combine(root, "j.txt"), new FullBackupOptions()));
        Assert.Throws<ArgumentException>(() => lib.StartSaveFullBackup(Path.Combine(root, "nofolder", "j.zip"), new FullBackupOptions()));
        var (started, path) = lib.StartSaveFullBackup(zip, new FullBackupOptions { Lists = true });
        Assert.True(started);
        Wait(lib);
        Assert.Null(lib.Job.Error);
        Assert.Equal(zip, path);
        Assert.True(File.Exists(zip));
        Assert.False(File.Exists(zip + ".part"));
        Assert.True(lib.StartRestoreFullBackup(zip, new FullBackupOptions { Lists = true }));
        Assert.False(lib.Job.Stoppable);
        Assert.False(lib.Stop());
        Wait(lib);
        Assert.Null(lib.Job.Error);
        Assert.NotNull(lib.RestoreResult);
        Assert.Throws<InvalidDataException>(() => lib.StartRestoreFullBackup(Path.Combine(root, "nope.zip"), new FullBackupOptions()));
        Assert.StartsWith(Path.Combine(root, "data", "backups"), lib.DefaultFullBackupPath());
    }

    // ---------- views ----------
    [Fact]
    public void SavedViews()
    {
        using var lib = NewLibrary();
        Assert.Throws<ArgumentException>(() => lib.SaveView("  ", new JsonObject()));
        lib.SaveView("zebra", JsonNode.Parse("{\"F\":{\"q\":\"x\"},\"sort\":\"title\",\"native\":[1,2]}"));
        lib.SaveView("  Apple  ", new JsonObject { ["a"] = 1 });
        lib.SaveView(new string('é', 80), new JsonObject());
        lib.SaveView("Apple", new JsonObject { ["a"] = 2 });   // same name replaces
        var v = lib.ListViews();
        Assert.Equal(new[] { "Apple", "zebra", new string('é', 60) }, v.Select(x => x.Name));
        Assert.Equal(2, v[0].State["a"]!.GetValue<int>());
        Assert.Equal(2, v[1].State["native"]!.AsArray().Count);
        lib.DeleteView(v[0].Id);
        Assert.Equal(2, lib.ListViews().Count);
        Sql("INSERT INTO views(name, state) VALUES ('broken', 'not json')");   // never breaks the list
        Assert.Equal(3, lib.ListViews().Count);
    }

    // ---------- wishlist ----------
    [Fact]
    public void WishlistAddSetAndExport()
    {
        using var lib = NewLibrary();
        Assert.Equal(2, lib.WishAdd(new[] { new WishNew("Heat", 1995, null, "blu-ray", "check"), new WishNew("  "), new WishNew("Say \"Hi\", Bob", null, 7) }));
        Assert.Equal(0, lib.WishAdd(new[] { new WishNew("Heat", 1995) }));
        var items = lib.WishlistItems();
        Assert.Equal(new[] { "Say \"Hi\", Bob", "Heat" }, items.Select(i => i.Title));   // newest first
        int heat = items.Single(i => i.Title == "Heat").Id;
        lib.WishSet(heat, "found");
        lib.WishSet(heat, "bogus");   // ignored
        Assert.Equal("found", lib.WishlistItems().Single(i => i.Id == heat).Status);
        Assert.Equal("found", lib.WishlistItems().Last().Status);   // wanted first
        string txt = Path.Combine(root, "w.txt"), csv = Path.Combine(root, "w.csv");
        lib.WriteWishlistText(txt);
        lib.WriteWishlistCsv(csv);
        Assert.Equal("\uFEFFSay \"Hi\", Bob\n", Encoding.UTF8.GetString(File.ReadAllBytes(txt)));
        string[] lines = Encoding.UTF8.GetString(File.ReadAllBytes(csv)).Split("\r\n");
        Assert.Equal("\uFEFFTitle,Year,Status,Source,Note,Added", lines[0]);
        Assert.Equal("\"Say \"\"Hi\"\", Bob\",\"\",\"wanted\",\"\",\"\",\"" + Q<string>("SELECT added FROM wishlist WHERE title LIKE 'Say%'") + "\"", lines[1]);
        Assert.StartsWith("\"Heat\",\"1995\",\"found\",\"check\",\"blu-ray\",\"", lines[2]);
        Assert.Equal("", lines[3]);
        lib.WishSet(heat, delete: true);
        Assert.Single(lib.WishlistItems());
        Assert.Equal("Title,Year,Status,Source,Note,Added\r\n\r\n", Csv.Wishlist(Array.Empty<WishItem>()));   // the web UI writes this blank line too
        Assert.Equal("Heat (1995)\nAlien", Csv.WishlistText(new[] { new WishItem { Title = "Heat", Year = 1995 }, new WishItem { Title = "Alien" } }));
    }

    // ---------- statistics ----------
    static Movie M(string k, int? year, string[] genres, double? rating, int runtime, string res, string? type = "movie", params int[] people)
    {
        var m = new Movie
        {
            K = k, Title = k, Year = year, Genres = genres, Rating = rating, Runtime = runtime, Type = type, Conf = "exact",
            Files = new() { new MovieFile { Res = res, Raw = k } },
            Dir = people.Take(1).Select(i => new Person { Id = i, Name = "P" + i }).ToList(),
            Cast = people.Skip(1).Select(i => new Person { Id = i, Name = "P" + i }).ToList(),
        };
        m.Prepare();
        return m;
    }

    [Fact]
    public void CollectionStatisticsLikeTheStatsDialog()
    {
        var a = M("a", 1995, new[] { "Crime", "Drama" }, 8.0, 120, "1080p", "movie", 1, 2, 3);
        var b = M("b", 1997, new[] { "Drama" }, 6.5, 60, "720p", "movie", 1, 3);
        var c = M("c", 2005, new[] { "Comedy" }, 7.0, 30, "1080p", "tvSeries", 9, 3);
        c.Countries = new[] { "US", "UK" };
        c.Watched = true;
        var d = M("d", null, Array.Empty<string>(), null, 0, "SD");
        d.Conf = "none";
        var all = new[] { a, b, c, d };
        var s = CollectionStats.Compute(all);
        Assert.Equal((4, 4, 1, 1), (s.Movies, s.Files, s.Watched, s.NeedReview));
        Assert.Equal(7.17, s.AvgRating);
        Assert.Equal(210, s.Minutes);
        Assert.Equal((4, 0), (s.Hours, s.Days));   // 3.5 rounds up like Math.round
        Assert.Equal(new[] { "1990s", "2000s" }, s.Decades.Select(x => x.Label));
        Assert.Equal(new[] { 2, 1 }, s.Decades.Select(x => x.Count));
        Assert.Equal("1990", s.Decades[0].Key);
        Assert.Equal(new[] { "Drama", "Crime", "Comedy" }, s.Genres.Select(x => x.Label));   // ties keep first-seen order
        Assert.Equal(("P1", 2, "1"), (s.Directors[0].Label, s.Directors[0].Count, s.Directors[0].Key));
        Assert.Equal(new[] { "P3", "P2" }, s.Actors.Select(x => x.Label));
        Assert.Equal(new[] { "1080p", "720p", "SD" }, s.Quality.Select(x => x.Label));
        Assert.Equal(new[] { 2, 1, 1 }, s.Quality.Select(x => x.Count));
        Assert.Equal(new[] { "9+", "8+", "7+", "6+", "5+", "4+" }, s.Ratings.Select(x => x.Label));
        Assert.Equal(new[] { 0, 1, 1, 1, 0, 0 }, s.Ratings.Select(x => x.Count));
        Assert.Equal(new[] { "US", "UK" }, s.Countries.Select(x => x.Label));
        var series = CollectionStats.Compute(all, StatsKind.Series);   // decades / genres / countries follow the kind switch
        Assert.Equal(new[] { "2000s" }, series.Decades.Select(x => x.Label));
        Assert.Equal(new[] { "Comedy" }, series.Genres.Select(x => x.Label));
        Assert.Equal(4, series.Movies);
        Assert.Equal(new[] { "1990s" }, CollectionStats.Compute(all, StatsKind.Movies).Decades.Select(x => x.Label));
        Assert.Empty(CollectionStats.Compute(new[] { a }).Countries);
    }

    [Fact]
    public void LibraryCounters()
    {
        using var lib = NewLibrary(true, "Heat.1995.720p", "Heat.1995.1080p.BluRay", "Unknown.Thing.1999.720p");
        lib.Update();
        lib.WishAdd(new[] { new WishNew("Alien", 1979) });
        var st = lib.GetLibraryStats();
        Assert.Equal((3, 2, 1, 1, 1, 1, 1), (st.Files, st.Matched, st.Titles, st.Problems, st.Duplicates, st.Wishlist, st.Index ? 1 : 0));
        Assert.False(st.Datasets);
        Assert.Null(st.RatingsAgeDays);
        Assert.Equal(1, lib.GetStats().Duplicates);   // the existing Stats now has the duplicate count
        lib.Dismiss(Issues.Build(lib).Kinds["dupes"].Single().IKey);
        Assert.Equal(0, lib.GetLibraryStats().Duplicates);
        var cs = lib.GetCollectionStats();
        Assert.Equal((2, 3), (cs.Movies, cs.Files));
        Assert.Equal(new[] { "1990s" }, cs.Decades.Select(x => x.Label));
    }

    // ---------- CSV ----------
    [Fact]
    public void CsvFilesLikePython()
    {
        Assert.Equal("a,\"b,c\",\"d\"\"e\",8.0,,x\r\n", Csv.Row(new object?[] { "a", "b,c", "d\"e", 8.0, null, "x" }));
        using var lib = NewLibrary(true, "Heat.1995.720p", "Heat.1995.1080p.BluRay", "Unknown.Thing.1999.720p");
        lib.Update();
        string d = lib.IssuesCsv("dupes");
        string[] l = d.Split("\r\n");
        Assert.Equal("Title,Year,IMDb,Rating,Votes,Problem,Files,Ignored", l[0]);
        Assert.StartsWith("Heat,1995,tt0000002,8.3,700000,", l[1]);
        Assert.Contains("Heat.1995.720p [720p, lower] | Heat.1995.1080p.BluRay [1080p BluRay, keep]", l[1]);
        Assert.Equal("", l[2]);
        Assert.Equal(l[0] + "\r\n", lib.IssuesCsv("nonfilm"));
        Assert.Equal("problems-dupes.csv", Csv.IssuesFileName("dupes"));
        string p = lib.ProblemsCsv();
        string[] pl = p.Split("\r\n");
        Assert.Equal("Name,Problem,Current IMDb,Current title,Year,Other candidates", pl[0]);
        Assert.StartsWith("Unknown.Thing.1999.720p,", pl[1]);
        string file = Path.Combine(root, "p.csv");
        lib.WriteProblemsCsv(file);
        lib.WriteIssuesCsv(Path.Combine(root, "i.csv"), "dupes");
        Assert.Equal(new byte[] { 0xEF, 0xBB, 0xBF }, File.ReadAllBytes(file)[..3]);
        Assert.Equal(d, File.ReadAllText(Path.Combine(root, "i.csv"), new UTF8Encoding(false)).TrimStart('\uFEFF'));
        lib.Dismiss(Issues.Build(lib).Kinds["dupes"].Single().IKey);
        Assert.EndsWith(",yes\r\n", lib.IssuesCsv("dupes"));
    }

    // ---------- first run ----------
    [Fact]
    public void StartFreshNeedsAFolderOrAList()
    {
        using var lib = NewLibrary(false);
        Assert.True(lib.FirstRun);
        var e = Assert.Throws<ArgumentException>(() => lib.StartFresh());
        Assert.Equal("Give a movie folder, a list (.txt or .csv), or both: the library is built from them.", e.Message);
        string gone = Path.Combine(root, "nowhere");
        Assert.Equal($"{gone} is not a folder.", Assert.Throws<ArgumentException>(() => lib.StartFresh(null, gone)).Message);
        Assert.True(lib.FirstRun);
        string movies = Path.Combine(root, "Movies");
        Directory.CreateDirectory(movies);
        lib.StartFresh(null, "\"" + movies + "\"");                  // a movie folder alone is enough
        Assert.False(lib.FirstRun);
        Assert.Equal(new[] { movies }, lib.LoadSettings().MediaDirs);
        Assert.Throws<InvalidOperationException>(() => lib.StartFresh(null, movies));
    }

    [Fact]
    public void StartFreshLoadsTheListAndKeepsTheMovieFolder()
    {
        using var lib = NewLibrary(false);
        string list = Path.Combine(root, "mine.txt");
        File.WriteAllText(list, "Heat.1995.720p\nAlien.1979.1080p\n");
        lib.StartFresh("\"" + list + "\"", null);
        Assert.False(lib.FirstRun);
        Assert.Equal(2, File.ReadAllLines(Path.Combine(lib.LoadSettings().ListDir, "mine.txt")).Length);
    }
}
