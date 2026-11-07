using System.Text.Json.Nodes;
using MovieShelf.Core;
using Xunit;

namespace MovieShelf.Core.Tests;

public class FilterTests
{
    // what saveCurrentView in app.js stores in the views table
    const string PythonView = """
        {"F":{"q":"nolan year:>2000","status":["fav","watched"],"genre":["Drama","Crime"],"decade":["1990","2000"],"yMin":1990,"yMax":2005,
        "rMin":7.5,"vMin":4,"rtMin":60,"mcMin":70,"runtime":["120","150"],"res":["1080p"],"src":["BluRay"],"tech":["x265","hdr"],"type":["movie"],
        "country":"United States","lang":"English","person":{"id":634240,"name":"Christopher Nolan"}},"sort":"rating","rev":true,"group":"decade"}
        """;

    static Movie M(Action<Movie> set)
    {
        var m = new Movie { K = "tt0000001", Tconst = 1, Title = "A", Type = "movie", Files = { new MovieFile { Id = 1, Raw = "a", Res = "1080p", Src = "BluRay", Codec = "x265", Hdr = true } } };
        set(m);
        return m;
    }

    [Fact]
    public void APythonViewComesBackWhole()
    {
        var v = LibraryFilter.ReadView(JsonNode.Parse(PythonView));
        var f = v.Filter;
        Assert.Equal("nolan year:>2000", f.Q);
        Assert.Equal(new[] { "fav", "watched" }, f.Status);
        Assert.Equal(new[] { "Drama", "Crime" }, f.Genre);
        Assert.Equal(new[] { "1990", "2000" }, f.Decade);
        Assert.Equal(new[] { "120", "150" }, f.Runtime);
        Assert.Equal(new[] { "1080p" }, f.Res);
        Assert.Equal(new[] { "BluRay" }, f.Src);
        Assert.Equal(new[] { "x265", "hdr" }, f.Tech);
        Assert.Equal(new[] { "movie" }, f.Type);
        Assert.Equal((1990, 2005), (f.YMin, f.YMax));
        Assert.Equal((7.5, 4d, 60d, 70d), (f.RMin, f.VMin, f.RtMin, f.McMin));
        Assert.Equal(("United States", "English"), (f.Country, f.Lang));
        Assert.Equal((634240, "Christopher Nolan"), f.Person);
        Assert.Equal(("rating", true, "decade"), (v.Sort, v.Reverse, v.Group));
    }

    [Fact]
    public void AViewWrittenHereIsTheSameObjectPythonWrites()
    {
        var v = LibraryFilter.ReadView(JsonNode.Parse(PythonView));
        var again = v.Filter.ToView(v.Sort!, v.Reverse, v.Group!);
        Assert.True(JsonNode.DeepEquals(JsonNode.Parse(PythonView), again), again.ToJsonString());
    }

    [Fact]
    public void AnEmptyFilterHasEveryKeyPythonExpects()
    {
        var o = new LibraryFilter().ToJson();
        foreach (var k in new[] { "q", "status", "genre", "decade", "yMin", "yMax", "rMin", "vMin", "rtMin", "mcMin", "runtime", "res", "src", "tech", "type", "country", "lang", "person" })
            Assert.True(o.ContainsKey(k), k);
        Assert.Null(o["yMin"]);
        Assert.Null(o["person"]);
        Assert.Equal(0d, (double)o["rMin"]!);
        Assert.False(new LibraryFilter().ToJson(withQuery: false).ContainsKey("q"));
    }

    [Fact]
    public void AFilterCopiesThroughItsJson()
    {
        var f = LibraryFilter.ReadView(JsonNode.Parse(PythonView)).Filter;
        var copy = new LibraryFilter();
        copy.Merge(f.ToJson());
        Assert.True(JsonNode.DeepEquals(f.ToJson(), copy.ToJson()), copy.ToJson().ToJsonString());
        Assert.Equal(1990, copy.YMin);
    }

    [Fact]
    public void MergeLeavesTheKeysItIsNotGiven()
    {
        var f = new LibraryFilter { Country = "Iran", RMin = 7 };
        f.Genre.Add("Drama");
        f.Merge(JsonNode.Parse("""{"genre":["Comedy"],"decade":[1990,2000],"yMin":null}"""));
        Assert.Equal(new[] { "Comedy" }, f.Genre);
        Assert.Equal(new[] { "1990", "2000" }, f.Decade);
        Assert.Equal(("Iran", 7d), (f.Country, f.RMin));
        Assert.Null(f.YMin);
    }

    [Fact]
    public void AnOldNativeViewStillOpens()
    {
        var v = LibraryFilter.ReadView(JsonNode.Parse("""
            {"native":{"kind":"series","search":"bad","status":["fav"],"genre":[],"decade":["2000"],"res":[],"src":[],"type":[],"runtime":[],"tech":["hdr"],
            "yearMin":2000,"yearMax":null,"rating":8,"votes":3,"rt":0,"mc":0,"country":"Iran","language":"Persian","person":{"id":7,"name":"X"}},"sort":"year","rev":false,"group":"genre"}
            """));
        var f = v.Filter;
        Assert.Equal(("bad", "series"), (f.Q, v.Kind));
        Assert.Equal(new[] { "fav" }, f.Status);
        Assert.Equal(new[] { "2000" }, f.Decade);
        Assert.Equal(new[] { "hdr" }, f.Tech);
        Assert.Equal((2000, (int?)null, 8d, 3d), (f.YMin, f.YMax, f.RMin, f.VMin));
        Assert.Equal(("Iran", "Persian", (7, "X")), (f.Country, f.Lang, f.Person!.Value));
        Assert.Equal(("year-desc", true, "genre"), (v.Sort, v.Reverse, v.Group));   // oldest first
    }

    [Fact]
    public void AViewWithoutFiltersIsEmpty()
    {
        var v = LibraryFilter.ReadView(new JsonObject());
        Assert.Equal("", v.Filter.Q);
        Assert.Empty(v.Filter.Status);
        Assert.Null(v.Sort);
        Assert.False(v.Reverse);
        Assert.Null(v.Group);
    }

    [Fact]
    public void ThePassTestsWhatAppJsTests()
    {
        bool Pass(LibraryFilter f, Movie m) => f.Compile().Test(m);
        LibraryFilter F(Action<LibraryFilter> set)
        {
            var f = new LibraryFilter();
            set(f);
            return f;
        }

        var plain = M(_ => { });
        Assert.True(Pass(new LibraryFilter(), plain));
        Assert.False(Pass(F(f => f.Status.Add("fav")), plain));
        Assert.True(Pass(F(f => f.Status.Add("unwatched")), plain));
        Assert.False(Pass(F(f => f.Status.Add("review")), M(m => m.Conf = "exact")));
        Assert.True(Pass(F(f => f.Status.Add("review")), M(m => m.Conf = "fuzzy")));
        Assert.False(Pass(F(f => f.Status.Add("dupes")), plain));
        Assert.True(Pass(F(f => f.Status.Add("noposter")), plain));
        Assert.False(Pass(F(f => f.Status.Add("top")), M(m => m.Rating = 7.9)));

        var both = F(f => { f.Genre.Add("Drama"); f.Genre.Add("Crime"); });   // every genre must match
        Assert.False(Pass(both, M(m => m.Genres = new[] { "Drama" })));
        Assert.True(Pass(both, M(m => m.Genres = new[] { "Crime", "Drama", "War" })));

        var decades = F(f => { f.Decade.Add("1990"); f.Decade.Add("2000"); });   // any decade may match
        Assert.True(Pass(decades, M(m => m.Decade = 1990)));
        Assert.False(Pass(decades, M(m => m.Decade = 1980)));
        Assert.False(Pass(decades, plain));

        Assert.False(Pass(F(f => f.YMin = 2000), plain));   // no year: out of a year range
        Assert.True(Pass(F(f => { f.YMin = 1990; f.YMax = 2000; }), M(m => m.Year = 1995)));
        Assert.False(Pass(F(f => f.RMin = 8), M(m => m.Rating = 7.5)));
        Assert.False(Pass(F(f => f.VMin = 4), M(m => m.Votes = 9999)));
        Assert.True(Pass(F(f => f.VMin = 4), M(m => m.Votes = 10000)));
        Assert.False(Pass(F(f => f.RtMin = 60), plain));
        Assert.False(Pass(F(f => f.McMin = 60), M(m => m.Mc = 59)));

        var run = F(f => { f.Runtime.Add("short"); f.Runtime.Add("long"); });
        Assert.True(Pass(run, M(m => m.Runtime = 170)));
        Assert.False(Pass(run, M(m => m.Runtime = 100)));
        Assert.False(Pass(run, plain));

        Assert.True(Pass(F(f => f.Res.Add("1080p")), plain));
        Assert.False(Pass(F(f => f.Src.Add("WEB-DL")), plain));
        Assert.True(Pass(F(f => { f.Tech.Add("x265"); f.Tech.Add("hdr"); }), plain));
        Assert.False(Pass(F(f => { f.Tech.Add("x265"); f.Tech.Add("fa"); }), plain));
        Assert.True(Pass(F(f => f.Type.Add("unknown")), M(m => m.Type = null)));
        Assert.False(Pass(F(f => f.Country = "Iran"), plain));
        Assert.True(Pass(F(f => f.Lang = "Persian"), M(m => m.Langs = new[] { "Persian" })));
    }

    [Fact]
    public void ThePersonFilterSeesTheCrewToo()
    {
        var f = new LibraryFilter { Person = (42, "Y") };
        Assert.False(f.Compile().Test(M(_ => { })));
        Assert.True(f.Compile().Test(M(m => m.Wri.Add(new Person { Id = 42, Name = "Y" }))));
        Assert.True(f.Compile().Test(M(m => m.CrewJson = """[[42, "Y", "composer"]]""")));
        Assert.False(f.Compile().Test(M(m => m.CrewJson = """[[1420, "Z", "composer"]]""")));
    }

    [Theory]
    [InlineData("Schindler’s List", "schindlers list")]
    [InlineData("L'Amour", "lamour")]
    [InlineData("Amélie", "amelie")]
    [InlineData("علي", "علی")]
    [InlineData("كتاب", "کتاب")]
    [InlineData("می\u200Cروم", "میروم")]
    [InlineData("فیلم ۱۹۹۹", "فیلم 1999")]
    public void FoldIsTheAppJsFold(string text, string folded) => Assert.Equal(folded, Movie.Fold(text));
}
