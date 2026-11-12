using Microsoft.Data.Sqlite;
using MovieShelf.Core;
using Xunit;

namespace MovieShelf.Core.Tests;

/// <summary>Add movie by IMDb number, Revise, and the Missing data / Same name and year problem lists. Offline: no request leaves the test.</summary>
public class ManualTests : IDisposable
{
    readonly TempLibrary t = new();
    public void Dispose() => t.Dispose();

    [Theory]
    [InlineData("https://www.imdb.com/title/tt0111161/", 111161)]
    [InlineData("imdb.com/title/tt0111161/?ref_=fn_al_tt_1", 111161)]
    [InlineData("tt0000001", 1)]
    [InlineData("  111161 ", 111161)]
    [InlineData("TT0068646", 68646)]
    public void TheImdbNumberIsFoundInALinkOrTyped(string text, int expected) => Assert.Equal(expected, Library.ParseImdbId(text));

    [Theory]
    [InlineData("")]
    [InlineData("hello")]
    [InlineData("https://example.com/movie")]
    [InlineData("0")]
    public void TextWithoutAnImdbNumberGivesNull(string text) => Assert.Null(Library.ParseImdbId(text));

    [Fact]
    public void AddMovieWritesTheNameKeepsTheChoiceAndMatchesIt()
    {
        using var lib = t.Open(true, "Heat.1995.720p");
        lib.Update();
        var r = lib.AddByImdb(1);
        Assert.False(r.AlreadyThere);
        Assert.Equal("The Godfather (1972)", r.Raw);
        Assert.Contains("The Godfather (1972)", File.ReadAllText(Path.Combine(t.Lists, "added.txt")));
        TempLibrary.WaitJob(lib);
        var movie = lib.LoadMovies().Single(m => m.K == "tt0000001");
        Assert.Equal("manual", movie.Conf);
        Assert.Equal(2, lib.LoadMovies().Count);
    }

    [Fact]
    public void AddingAMovieTheLibraryHasIsReportedNotRepeated()
    {
        using var lib = t.Open(true, "Heat.1995.720p");
        lib.Update();
        var r = lib.AddByImdb(2);
        Assert.True(r.AlreadyThere);
        Assert.False(File.Exists(Path.Combine(t.Lists, "added.txt")));
    }

    [Fact]
    public void AnUnknownImdbNumberIsAnErrorNotAnEmptyMovie()
    {
        using var lib = t.Open(true, "Heat.1995.720p");
        lib.Update();
        var e = Assert.Throws<InvalidOperationException>(() => lib.AddByImdb(9999999));
        Assert.Contains("tt9999999", e.Message);
        Assert.False(File.Exists(Path.Combine(t.Lists, "added.txt")));
    }

    [Fact]
    public void ReviseGivesEveryFileOfTheMovieTheNewTitle()
    {
        using var lib = t.Open(true, "Heat.1995.720p", "Heat.1995.1080p");
        lib.Update();
        var ids = lib.LoadMovies().Single(m => m.K == "tt0000002").Files.Select(f => f.Id).ToList();
        Assert.Equal(2, ids.Count);
        lib.ReviseMovie(ids, 1);
        TempLibrary.WaitJob(lib);
        var movies = lib.LoadMovies();
        Assert.Equal(2, movies.Single(m => m.K == "tt0000001").Files.Count);
        Assert.DoesNotContain(movies, m => m.K == "tt0000002");
        Assert.All(movies.Single().Files, _ => { });
    }

    static void AddTwin(Library lib)
    {
        using var c = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = lib.IndexPath, Pooling = false }.ToString());
        c.Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "INSERT INTO titles VALUES (4, 0, 'Heat', 'Heat', 1995, NULL, 90, 'Drama', 6.1, 1200)";
        cmd.ExecuteNonQuery();
    }

    [Fact]
    public void SameNameAndYearOnImdbIsListedWithTheOtherTitle()
    {
        using var lib = t.Open(true, "Heat.1995.720p", "The.Godfather.1972.1080p");
        AddTwin(lib);
        lib.Update();
        var report = Issues.Build(lib);
        var row = Assert.Single(report.Kinds["samename"]);
        Assert.Equal("tt0000002", row.K);
        Assert.Contains("tt0000004", row.Note);
        Assert.Contains("1,200 votes", row.Note);
    }

    [Fact]
    public void NoTwinMeansNothingListed()
    {
        using var lib = t.Open(true, "Heat.1995.720p");
        lib.Update();
        Assert.Empty(Issues.Build(lib).Kinds["samename"]);
    }

    [Fact]
    public void AMovieWithoutPosterCastOrRatingIsListedAsMissingData()
    {
        using var lib = t.Open(true, "Heat.1995.720p");
        lib.Update();
        var report = Issues.Build(lib);
        var row = Assert.Single(report.Kinds["nodata"]);
        Assert.Contains("poster", row.Note);
        Assert.Contains("cast", row.Note);
        Assert.DoesNotContain("rating", row.Note);   // the index gave Heat a rating
        Assert.True(report.AreaCount("other") >= 2);
    }

    [Fact]
    public void AMovieWithEverythingIsNotListed()
    {
        using var lib = t.Open(true, "Heat.1995.720p");
        lib.Update();
        using (var c = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = lib.DbPath, Pooling = false }.ToString()))
        {
            c.Open();
            using var cmd = c.CreateCommand();
            cmd.CommandText = "UPDATE titles SET poster='x.jpg', cast_='[[1,\"A\",\"B\"]]', rating=8.3, genres='Crime', runtime=170 WHERE tconst=2";
            cmd.ExecuteNonQuery();
        }
        Assert.Empty(Issues.Build(lib).Kinds["nodata"]);
    }
}
