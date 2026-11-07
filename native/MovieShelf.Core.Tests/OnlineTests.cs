using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Data.Sqlite;
using MovieShelf.Core;
using Xunit;

namespace MovieShelf.Core.Tests;

/// <summary>Answers requests from a function; remembers every request (method, url, body).</summary>
sealed class FakeHttp : HttpMessageHandler
{
    public readonly List<(string Method, string Url, string Body, HttpRequestMessage Req)> Calls = new();
    readonly Func<HttpRequestMessage, string, HttpResponseMessage> answer;

    public FakeHttp(Func<HttpRequestMessage, string, HttpResponseMessage> answer) => this.answer = answer;

    public int Count { get { lock (Calls) return Calls.Count; } }
    public int CountOf(string part) { lock (Calls) return Calls.Count(c => c.Url.Contains(part)); }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        string body = request.Content == null ? "" : await request.Content.ReadAsStringAsync(ct);
        lock (Calls) Calls.Add((request.Method.Method, request.RequestUri!.AbsoluteUri, body, request));
        return answer(request, body);
    }

    public static HttpResponseMessage Json(string json, HttpStatusCode code = HttpStatusCode.OK) =>
        new(code) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    public static HttpResponseMessage Bytes(byte[] data) => new(HttpStatusCode.OK) { Content = new ByteArrayContent(data) };

    public static HttpResponseMessage Status(HttpStatusCode code, string body = "") => new(code) { Content = new StringContent(body) };
}

public class OnlineTests : IDisposable
{
    readonly string root = Path.Combine(Path.GetTempPath(), "movieshelf-online-" + Guid.NewGuid().ToString("N"));
    static readonly byte[] Jpeg = { 0xFF, 0xD8, 0xFF, 0xE0, 1, 2, 3, 4 };

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try { Directory.Delete(root, true); } catch { }
    }

    Library NewLib(FakeHttp http, string tmdbKey = "tmdbkey", string omdbKey = "")
    {
        Directory.CreateDirectory(Path.Combine(root, "data"));
        var lib = new Library(root) { OnlineHandler = http, OnlineDelayScale = 0 };
        lib.SaveSettings(new Settings { TmdbKey = tmdbKey, OmdbKey = omdbKey });
        return lib;
    }

    void Sql(string sql, params object?[] args)
    {
        using var c = new SqliteConnection($"Data Source={Path.Combine(root, "data", "library.db")}");
        c.Open();
        using var cmd = c.CreateCommand();
        int n = 0;
        cmd.CommandText = string.Concat(sql.Select(ch => ch == '?' ? "$p" + n++ : ch.ToString()));
        for (int i = 0; i < args.Length; i++) cmd.Parameters.AddWithValue("$p" + i, args[i] ?? DBNull.Value);
        cmd.ExecuteNonQuery();
    }

    T? Value<T>(string sql, params object?[] args)
    {
        using var c = new SqliteConnection($"Data Source={Path.Combine(root, "data", "library.db")}");
        c.Open();
        using var cmd = c.CreateCommand();
        int n = 0;
        cmd.CommandText = string.Concat(sql.Select(ch => ch == '?' ? "$p" + n++ : ch.ToString()));
        for (int i = 0; i < args.Length; i++) cmd.Parameters.AddWithValue("$p" + i, args[i] ?? DBNull.Value);
        var v = cmd.ExecuteScalar();
        return v is null or DBNull ? default : (T)Convert.ChangeType(v, Nullable.GetUnderlyingType(typeof(T)) ?? typeof(T));
    }

    static void WaitJob(Library lib)
    {
        var t = DateTime.UtcNow.AddSeconds(20);
        while (lib.Job.Running && DateTime.UtcNow < t) Thread.Sleep(20);
        Assert.False(lib.Job.Running);
    }

    // ---------------------------------------------------------------- IMDb suggest
    [Fact]
    public async Task SuggestParsesTitlesAndSkipsPeople()
    {
        string json = """
            {"d":[{"id":"tt0133093","l":"The Matrix","y":1999,"q":"feature","s":"Keanu Reeves","i":{"imageUrl":"https://m.media-amazon.com/images/M/x._V1_.jpg"}},
            {"id":"nm0000206","l":"Keanu Reeves"},{"id":"tt0234215","l":"The Matrix Reloaded","y":2003}]}
            """;
        var http = new FakeHttp((_, _) => FakeHttp.Json(json));
        var items = await ImdbOnline.SuggestAsync(new OnlineClient(http), "  The Matrix 1999 ");
        Assert.Equal(2, items.Count);
        Assert.Equal(new SuggestItem(133093, "The Matrix", 1999, "feature", "Keanu Reeves", "https://m.media-amazon.com/images/M/x._V1_.jpg"), items[0]);
        Assert.Null(items[1].Image);
        Assert.Equal("https://v3.sg.media-imdb.com/suggestion/t/the%20matrix%201999.json", http.Calls[0].Url);
        Assert.Contains("MovieShelf/1.0", http.Calls[0].Req.Headers.UserAgent.ToString());
    }

    [Fact]
    public async Task SuggestUsesXForNonAsciiAndNothingForEmpty()
    {
        var http = new FakeHttp((_, _) => FakeHttp.Json("""{"d":[]}"""));
        var c = new OnlineClient(http);
        Assert.Empty(await ImdbOnline.SuggestAsync(c, "   "));
        Assert.Equal(0, http.Count);
        await ImdbOnline.SuggestAsync(c, "ما");
        Assert.StartsWith("https://v3.sg.media-imdb.com/suggestion/x/%D9%85", http.Calls[0].Url);
    }

    [Fact]
    public async Task PosterUrlAndSizedAmazon()
    {
        var http = new FakeHttp((_, _) => FakeHttp.Json("""{"d":[{"id":"tt0000009","l":"Other","i":{"imageUrl":"u9"}},{"id":"tt0133093","l":"M","i":{"imageUrl":"https://m.media-amazon.com/images/M/abc._V1_UX100_.jpg"}}]}"""));
        string? url = await ImdbOnline.PosterUrlAsync(new OnlineClient(http), 133093);
        Assert.Equal("https://m.media-amazon.com/images/M/abc._V1_UX100_.jpg", url);
        Assert.EndsWith("/suggestion/t/tt0133093.json", http.Calls[0].Url);
        Assert.Equal("https://m.media-amazon.com/images/M/abc._V1_QL80_UX400_.jpg", ImdbOnline.SizedAmazon(url));
        Assert.Equal("https://x/y.jpg", ImdbOnline.SizedAmazon("https://x/y.jpg"));
        Assert.Null(ImdbOnline.SizedAmazon(null));
    }

    [Fact]
    public async Task GetJsonRetriesTooManyRequestsAndReturnsNullOn404()
    {
        int n = 0;
        var http = new FakeHttp((_, _) => ++n == 1 ? FakeHttp.Status((HttpStatusCode)429) : FakeHttp.Json("""{"a":1}"""));
        var c = new OnlineClient(http) { DelayScale = 0 };
        var j = await c.GetJsonAsync("https://x/y");
        Assert.Equal(1, (int)j!["a"]!);
        Assert.Equal(2, http.Count);
        Assert.Null(await new OnlineClient(new FakeHttp((_, _) => FakeHttp.Status(HttpStatusCode.NotFound))).GetJsonAsync("https://x/y"));
        var bad = new FakeHttp((_, _) => FakeHttp.Status(HttpStatusCode.InternalServerError));
        var ex = await Assert.ThrowsAsync<HttpStatusException>(() => new OnlineClient(bad) { DelayScale = 0 }.GetJsonAsync("https://x/y"));
        Assert.Equal(500, ex.Status);
        Assert.Equal(3, bad.Count); // 1 try + 2 retries
    }

    // ---------------------------------------------------------------- TMDB client
    static string MatrixJson => """
        {"id":603,"overview":"A hacker learns the truth.","tagline":"Free your mind","original_language":"en",
         "spoken_languages":[{"english_name":"English","name":"English"},{"english_name":"","name":"Persian"}],
         "production_countries":[{"name":"United States of America"}],"poster_path":"/p.jpg","backdrop_path":"/b.jpg",
         "belongs_to_collection":{"name":"The Matrix Collection"},"budget":63000000,"revenue":0,
         "credits":{"cast":[{"name":"Keanu Reeves","character":"Neo","profile_path":"/k.jpg"},{"name":"Extra","character":"Y","profile_path":null}]},
         "translations":{"translations":[{"iso_639_1":"de","data":{"title":"Matrix"}},{"iso_639_1":"fa","data":{"title":"ماتریکس","name":""}}]}}
        """;

    static HttpResponseMessage TmdbRoutes(HttpRequestMessage r)
    {
        string path = r.RequestUri!.AbsolutePath;
        if (path == "/3/find/tt0000603") return FakeHttp.Json("""{"movie_results":[{"id":603}],"tv_results":[]}""");
        if (path == "/3/find/tt0000999") return FakeHttp.Json("""{"movie_results":[],"tv_results":[]}""");
        if (path == "/3/find/tt0000777") return FakeHttp.Json("""{"movie_results":[],"tv_results":[{"id":55}]}""");
        if (path == "/3/movie/603") return FakeHttp.Json(MatrixJson);
        if (path == "/3/tv/55") return FakeHttp.Json("""{"id":55,"name":"Show","overview":"","origin_country":["US","GB"],"spoken_languages":[],"translations":{"translations":[{"iso_639_1":"fa","data":{"name":"سریال"}}]}}""");
        if (path == "/3/configuration") return FakeHttp.Json("""{"images":{}}""");
        if (path.EndsWith(".jpg")) return FakeHttp.Bytes(Jpeg);
        return FakeHttp.Status(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task TmdbDetailsAreShaped()
    {
        var http = new FakeHttp((r, _) => TmdbRoutes(r));
        var tm = new Tmdb("shortkey", new OnlineClient(http));
        var d = (await tm.DetailsAsync(603))!;
        Assert.Equal(603, d.TmdbId);
        Assert.Equal("movie", d.Kind);
        Assert.Equal("A hacker learns the truth.", d.Plot);
        Assert.Equal("Free your mind", d.Tagline);
        Assert.Equal("en", d.OrigLang);
        Assert.Equal(new List<string?> { "English", "Persian" }, d.Languages);
        Assert.Equal(new List<string?> { "United States of America" }, d.Countries);
        Assert.Equal("ماتریکس", d.FaTitle);
        Assert.Equal("https://image.tmdb.org/t/p/w500/p.jpg", d.PosterUrl);
        Assert.Equal("https://image.tmdb.org/t/p/w1280/b.jpg", d.BackdropUrl);
        Assert.Equal("The Matrix Collection", d.Collection);
        Assert.Equal(63000000, d.Budget);
        Assert.Null(d.Revenue);
        Assert.Equal(new TmdbCastMember("Keanu Reeves", "Neo", "https://image.tmdb.org/t/p/w185/k.jpg"), d.TmdbCast[0]);
        Assert.Null(d.TmdbCast[1].Img);
        // short key: api_key in the query; the detail request carries the language and appended parts
        var detail = http.Calls.Last().Url;
        Assert.Contains("api_key=shortkey", detail);
        Assert.Contains("language=en-US", detail);
        Assert.Contains("append_to_response=credits%2Ctranslations", detail);
        Assert.False(http.Calls.Last().Req.Headers.Contains("Authorization"));
    }

    [Fact]
    public async Task TmdbTvFallbackUnknownTitleAndBearerToken()
    {
        var http = new FakeHttp((r, _) => TmdbRoutes(r));
        string token = new('a', 41);
        var tm = new Tmdb(token, new OnlineClient(http));
        var tv = (await tm.DetailsAsync(777))!;
        Assert.Equal("tv", tv.Kind);
        Assert.Equal("سریال", tv.FaTitle);
        Assert.Equal(new List<string?> { "US", "GB" }, tv.Countries);
        Assert.Null(tv.Plot);
        var none = (await tm.DetailsAsync(999))!;
        Assert.Null(none.TmdbId);
        Assert.All(http.Calls, c =>
        {
            Assert.DoesNotContain("api_key", c.Url);
            Assert.Equal($"Bearer {token}", c.Req.Headers.Authorization!.ToString());
        });
    }

    [Fact]
    public async Task TmdbRetriesTooManyRequestsThenGivesUp()
    {
        int n = 0;
        var http = new FakeHttp((_, _) => ++n < 3 ? FakeHttp.Status((HttpStatusCode)429) : FakeHttp.Json("""{"images":{}}"""));
        var tm = new Tmdb("k", new OnlineClient(http) { DelayScale = 0 });
        Assert.True(await tm.CheckAsync());
        Assert.Equal(3, http.Count);

        var down = new FakeHttp((_, _) => FakeHttp.Status(HttpStatusCode.ServiceUnavailable));
        var ex = await Assert.ThrowsAsync<HttpStatusException>(() => new Tmdb("k", new OnlineClient(down) { DelayScale = 0 }).CheckAsync());
        Assert.Equal(503, ex.Status);
        Assert.Equal(3, down.Count);

        var denied = new FakeHttp((_, _) => FakeHttp.Status(HttpStatusCode.Unauthorized));
        await Assert.ThrowsAsync<HttpStatusException>(() => new Tmdb("k", new OnlineClient(denied)).CheckAsync());
        Assert.Equal(1, denied.Count);
        Assert.False(await new Tmdb("k", new OnlineClient(new FakeHttp((_, _) => FakeHttp.Status(HttpStatusCode.NotFound)))).CheckAsync());
    }

    [Fact]
    public async Task TmdbPacesRequestsAcrossThreads()
    {
        var http = new FakeHttp((_, _) => FakeHttp.Json("{}"));
        var tm = new Tmdb("k", new OnlineClient(http));
        var sw = System.Diagnostics.Stopwatch.StartNew();
        await Task.WhenAll(Enumerable.Range(0, 16).Select(_ => tm.GetAsync("/configuration", default)));
        Assert.True(sw.Elapsed.TotalSeconds >= 0.25, $"16 requests took {sw.Elapsed.TotalSeconds:0.000}s"); // 15 gaps of 20 ms
    }

    [Fact]
    public void RoleMapping()
    {
        Assert.Equal("Director", Tmdb.Role("Director", "Directing"));
        Assert.Equal("Writer", Tmdb.Role("Screenplay", "Writing"));
        Assert.Equal("Writer", Tmdb.Role("Dialogue", "Writing"));
        Assert.Equal("Producer", Tmdb.Role("Executive Producer", "Production"));
        Assert.Equal("Composer", Tmdb.Role("Original Music Composer", "Sound"));
        Assert.Equal("Cinematographer", Tmdb.Role("Director of Photography", "Camera"));
        Assert.Equal("Editor", Tmdb.Role("Editor", "Editing"));
        Assert.Equal("Gaffer", Tmdb.Role("Gaffer", "Lighting"));
        Assert.Equal("Camera", Tmdb.Role("", "Camera"));
        Assert.Equal("Other", Tmdb.Role(null, null));
    }

    [Fact]
    public async Task CollectionSearchAndParts()
    {
        var http = new FakeHttp((r, _) => r.RequestUri!.AbsolutePath switch
        {
            "/3/search/collection" => FakeHttp.Json("""{"results":[{"id":1,"name":"Matrix Collection Extra"},{"id":2,"name":"matrix collection"}]}"""),
            "/3/collection/2" => FakeHttp.Json("""{"parts":[{"title":"Three","original_title":"3o","release_date":"2003-11-05"},{"title":"One","original_title":"One","release_date":"1999-03-31"},{"title":"Later","release_date":""}]}"""),
            _ => FakeHttp.Status(HttpStatusCode.NotFound),
        });
        var tm = new Tmdb("k", new OnlineClient(http));
        Assert.Equal(2, await tm.SearchCollectionAsync("Matrix Collection")); // exact name wins over the first hit
        var parts = await tm.CollectionAsync(2);
        Assert.Equal(new[] { "One", "Three", "Later" }, parts.Select(p => p.Title));
        Assert.Equal(new int?[] { 1999, 2003, null }, parts.Select(p => p.Year));
        Assert.Equal("3o", parts[1].Orig);
        Assert.Contains("query=Matrix+Collection", http.Calls[0].Url);
    }

    [Fact]
    public async Task PersonCombinesCreditsPerMovieAndRole()
    {
        var http = new FakeHttp((r, _) => PersonRoutes(r));
        var p = (await new Tmdb("k", new OnlineClient(http)).PersonAsync(1))!;
        Assert.Equal(77, p.TmdbId);
        Assert.Equal("Jane Doe", p.Name);
        Assert.Equal(6, p.Aka.Count);
        Assert.Equal("/pp.jpg", p.PhotoPath);
        Assert.Equal(new[] { ("B", "Director", 2005), ("A", "Actor", 1999), ("A", "Writer", 1999) }, p.Credits.Select(c => (c.Title!, c.Role, c.Year!.Value)));
        Assert.Equal("Neo", p.Credits[1].Detail);
        Assert.Equal(5, p.Credits[1].Votes);
        Assert.Null(await new Tmdb("k", new OnlineClient(new FakeHttp((_, _) => FakeHttp.Json("""{"person_results":[]}""")))).PersonAsync(2));
    }

    static HttpResponseMessage PersonRoutes(HttpRequestMessage r)
    {
        string path = r.RequestUri!.AbsolutePath;
        if (path == "/3/find/nm0000001") return FakeHttp.Json("""{"person_results":[{"id":77}]}""");
        if (path == "/3/find/nm0000003") return FakeHttp.Json("""{"person_results":[{"id":78}]}""");
        if (path == "/3/person/77")
            return FakeHttp.Json("""
                {"name":"Jane Doe","biography":"Born somewhere.","birthday":"1960-01-02","deathday":null,"place_of_birth":"Paris","known_for_department":"Acting",
                 "also_known_as":["a","b","c","d","e","f","g"],"homepage":null,"profile_path":"/pp.jpg",
                 "combined_credits":{"cast":[{"id":10,"media_type":"movie","title":"A","release_date":"1999-05-01","character":"Neo","vote_count":5,"poster_path":"/a.jpg"},
                   {"id":11,"media_type":"tv","name":"Show"},{"id":10,"media_type":"movie","title":"A","release_date":"1999-05-01","character":"Dup"}],
                  "crew":[{"id":12,"media_type":"movie","title":"B","release_date":"2005-01-01","job":"Director","department":"Directing"},
                   {"id":10,"media_type":"movie","title":"A","release_date":"1999-05-01","job":"Screenplay","department":"Writing"}]}}
                """);
        if (path == "/3/person/78") return FakeHttp.Json("""{"name":"Writer Three","combined_credits":{}}""");
        if (path.EndsWith(".jpg")) return FakeHttp.Bytes(Jpeg);
        return FakeHttp.Status(HttpStatusCode.NotFound);
    }

    // ---------------------------------------------------------------- Persian titles
    [Theory]
    [InlineData("سال 2001 ,  شهر ها ?", "سال ۲۰۰۱، شهر‌ها؟")]
    [InlineData("می روم کتاب های بزرگتر : قسمت 2", "می‌روم کتاب‌های بزرگتر: قسمت ۲")]
    [InlineData("كتابَ ۀ ( نگاه )", "کتاب ه‌ی (نگاه)")]
    [InlineData("Plain Latin ", "Plain Latin")]
    [InlineData("فیلم Alien 3 ها", "فیلم Alien 3 ها")]
    [InlineData(" ", null)]
    [InlineData(null, null)]
    public void PersianTitleNormalizingMatchesPython(string? input, string? expected) => Assert.Equal(expected, FaNormalizer.Normalize(input));

    // ---------------------------------------------------------------- critic scores
    const string WikidataRows = """
        {"results":{"bindings":[
         {"imdb":{"value":"tt0000001"},"score":{"value":"88%"},"by":{"value":"http://www.wikidata.org/entity/Q105584"},"method":{"value":"http://www.wikidata.org/entity/Q108403393"},"date":{"value":"2019-01-01T00:00:00Z"}},
         {"imdb":{"value":"tt0000001"},"score":{"value":"93 %"},"by":{"value":"http://www.wikidata.org/entity/Q105584"},"method":{"value":"http://www.wikidata.org/entity/Q108403393"},"date":{"value":"2020-01-01T00:00:00Z"}},
         {"imdb":{"value":"tt0000001"},"score":{"value":"75%"},"by":{"value":"http://www.wikidata.org/entity/Q105584"},"method":{"value":"http://www.wikidata.org/entity/Q999"},"date":{"value":"2021-01-01T00:00:00Z"}},
         {"imdb":{"value":"tt0000001"},"score":{"value":"82/100"},"by":{"value":"http://www.wikidata.org/entity/Q150248"}},
         {"imdb":{"value":"tt0000002"},"score":{"value":"61"},"by":{"value":"http://www.wikidata.org/entity/Q150248"}},
         {"imdb":{"value":"tt0000002"},"score":{"value":"7.5/10"},"by":{"value":"http://www.wikidata.org/entity/Q105584"}},
         {"imdb":{"value":"tt0000003"},"score":{"value":"150"},"by":{"value":"http://www.wikidata.org/entity/Q150248"}}
        ]}}
        """;

    [Fact]
    public async Task WikidataScoresAreParsedPerKind()
    {
        var http = new FakeHttp((_, _) => FakeHttp.Json(WikidataRows));
        var res = await CriticSources.WikidataScoresAsync(new OnlineClient(http), new[] { 1, 2, 3 });
        Assert.Equal(new CriticScores(93, 82), res[1]); // newest Tomatometer wins, audience score skipped
        Assert.Equal(new CriticScores(null, 61), res[2]);
        Assert.False(res.ContainsKey(3)); // over 100 is junk
        var call = http.Calls[0];
        Assert.Equal("POST", call.Method);
        Assert.Equal("https://query.wikidata.org/sparql", call.Url);
        string body = Uri.UnescapeDataString(call.Body.Replace('+', ' '));
        Assert.Contains("\"tt0000001\" \"tt0000002\" \"tt0000003\"", body);
        Assert.Contains("format=json", body);
        Assert.Contains("sparql-results+json", call.Req.Headers.Accept.ToString());
    }

    [Fact]
    public async Task OmdbScoresAndErrors()
    {
        var ok = new FakeHttp((_, _) => FakeHttp.Json("""{"Response":"True","Metascore":"73","Ratings":[{"Source":"Internet Movie Database","Value":"8.0/10"},{"Source":"Rotten Tomatoes","Value":"91%"}]}"""));
        Assert.Equal(new CriticScores(91, 73), await CriticSources.OmdbScoresAsync(new OnlineClient(ok), "key", 133093));
        Assert.Contains("i=tt0133093&apikey=key", ok.Calls[0].Url);

        var na = new FakeHttp((_, _) => FakeHttp.Json("""{"Response":"True","Metascore":"N/A","Ratings":[]}"""));
        Assert.False((await CriticSources.OmdbScoresAsync(new OnlineClient(na), "key", 1)).Any);
        var missing = new FakeHttp((_, _) => FakeHttp.Json("""{"Response":"False","Error":"Incorrect IMDb ID."}"""));
        Assert.False((await CriticSources.OmdbScoresAsync(new OnlineClient(missing), "key", 1)).Any);

        var limit = new FakeHttp((_, _) => FakeHttp.Json("""{"Response":"False","Error":"Request limit reached!"}"""));
        await Assert.ThrowsAsync<LimitReachedException>(() => CriticSources.OmdbScoresAsync(new OnlineClient(limit), "key", 1));
        var limit401 = new FakeHttp((_, _) => FakeHttp.Json("""{"Response":"False","Error":"Request limit reached!"}""", HttpStatusCode.Unauthorized));
        await Assert.ThrowsAsync<LimitReachedException>(() => CriticSources.OmdbScoresAsync(new OnlineClient(limit401) { DelayScale = 0 }, "key", 1));
        var badKey = new FakeHttp((_, _) => FakeHttp.Json("""{"Response":"False","Error":"Invalid API key!"}"""));
        await Assert.ThrowsAsync<InvalidApiKeyException>(() => CriticSources.OmdbScoresAsync(new OnlineClient(badKey), "key", 1));
        var bad401 = new FakeHttp((_, _) => FakeHttp.Json("""{"Response":"False","Error":"Invalid API key!"}""", HttpStatusCode.Unauthorized));
        await Assert.ThrowsAsync<InvalidApiKeyException>(() => CriticSources.OmdbScoresAsync(new OnlineClient(bad401) { DelayScale = 0 }, "key", 1));
    }

    [Fact]
    public async Task OmdbCheck()
    {
        Assert.Equal((true, ""), await CriticSources.OmdbCheckAsync(new OnlineClient(new FakeHttp((_, _) => FakeHttp.Json("""{"Response":"True"}"""))), "k"));
        Assert.Equal((false, "Invalid API key"), await CriticSources.OmdbCheckAsync(new OnlineClient(new FakeHttp((_, _) => FakeHttp.Status(HttpStatusCode.Unauthorized))), "k"));
        Assert.Equal((false, "Invalid API key!"), await CriticSources.OmdbCheckAsync(new OnlineClient(new FakeHttp((_, _) => FakeHttp.Json("""{"Response":"False","Error":"Invalid API key!"}"""))), "k"));
    }

    [Fact]
    public void KeyTestsReportOkAndReason()
    {
        using var lib = NewLib(new FakeHttp((r, _) => r.RequestUri!.Host == "api.themoviedb.org"
            ? (r.RequestUri.Query.Contains("api_key=good") ? FakeHttp.Json("{}") : FakeHttp.Status(HttpStatusCode.Unauthorized))
            : FakeHttp.Json(r.RequestUri.Query.Contains("apikey=good") ? """{"Response":"True"}""" : """{"Response":"False","Error":"Invalid API key!"}""")));
        Assert.Equal(new KeyTest(true, ""), lib.TestTmdbKey(" good "));
        var bad = lib.TestTmdbKey("bad");
        Assert.False(bad.Ok);
        Assert.Contains("401", bad.Message);
        Assert.Equal(new KeyTest(true, ""), lib.TestOmdbKey(" good "));
        Assert.Equal(new KeyTest(false, "Invalid API key!"), lib.TestOmdbKey("bad"));
    }

    // ---------------------------------------------------------------- online match
    [Fact]
    public void OnlineMatchTakesTheCloseSuggestionOnly()
    {
        var http = new FakeHttp((r, _) => FakeHttp.Json("""
            {"d":[{"id":"tt0999991","l":"Unknown Film","y":2010,"q":"TV episode"},{"id":"tt0999992","l":"Unknown Film","y":1980,"q":"feature"},
                  {"id":"tt0123456","l":"Unknown Film","y":2011,"q":"feature"}]}
            """));
        using var lib = NewLib(http);
        Sql("INSERT INTO files(id, raw, title, year, conf, tconst) VALUES (1, 'Unknown.Film.2010', 'Unknown Film', 2010, 'none', NULL)");
        Sql("INSERT INTO files(id, raw, title, year, conf, tconst) VALUES (2, 'Other.Thing.2001', 'Other Thing', 2001, 'none', NULL)");
        Sql("INSERT INTO files(id, raw, title, year, conf, tconst) VALUES (3, 'Nothing', 'Nothing', NULL, 'none', NULL)");
        lib.OnlineMatch(new[] { 1, 2, 3 });
        Assert.Equal(123456, Value<int>("SELECT tconst FROM files WHERE id=1"));
        Assert.Equal("online", Value<string>("SELECT conf FROM files WHERE id=1"));
        Assert.Null(Value<int?>("SELECT tconst FROM files WHERE id=2"));
        Assert.Equal("none", Value<string>("SELECT conf FROM files WHERE id=2"));
    }

    [Fact]
    public void OnlineMatchStopsQuietlyWhenOffline()
    {
        using var lib = NewLib(new FakeHttp((_, _) => throw new HttpRequestException("offline")));
        Sql("INSERT INTO files(id, raw, title, year, conf) VALUES (1, 'A.Film.2010', 'A Film', 2010, 'none')");
        lib.OnlineMatch(new[] { 1 });
        Assert.Contains(lib.Job.Log, l => l.Contains("Online search failed: offline"));
        Assert.Null(Value<int?>("SELECT tconst FROM files WHERE id=1"));
    }

    // ---------------------------------------------------------------- posters
    void AddTitle(int tc, string title, int year, string? posterUrl = null, int votes = 0)
    {
        Sql("INSERT INTO titles(tconst, type, title, en_title, year, votes, poster_url) VALUES (?, 'movie', ?, ?, ?, ?, ?)", tc, title, title, year, votes, posterUrl);
        Sql("INSERT INTO files(raw, tconst, present) VALUES (?, ?, 1)", $"{title}.{year}", tc);
    }

    [Fact]
    public void FetchPostersWritesFilesAndRemembersFailures()
    {
        var http = new FakeHttp((r, _) =>
        {
            string url = r.RequestUri!.AbsoluteUri;
            if (url.Contains("/suggestion/t/tt0000002.json")) return FakeHttp.Json("""{"d":[{"id":"tt0000002","l":"B","i":{"imageUrl":"https://m.media-amazon.com/images/M/b._V1_.jpg"}}]}""");
            if (url.Contains("/suggestion/t/")) return FakeHttp.Json("""{"d":[]}""");
            if (url == "https://m.media-amazon.com/images/M/a._V1_QL80_UX400_.jpg" || url == "https://m.media-amazon.com/images/M/b._V1_QL80_UX400_.jpg") return FakeHttp.Bytes(Jpeg);
            return FakeHttp.Status(HttpStatusCode.NotFound);
        });
        using var lib = NewLib(http);
        AddTitle(1, "A", 2000, "https://m.media-amazon.com/images/M/a._V1_.jpg");
        AddTitle(2, "B", 2001);              // url found through the suggestion search
        AddTitle(3, "C", 2002);              // nothing anywhere
        AddTitle(4, "D", 2003, "https://m.media-amazon.com/images/M/missing._V1_.jpg"); // download fails
        lib.FetchPosters();
        Assert.Equal(Jpeg, File.ReadAllBytes(Path.Combine(lib.PosterDir, "tt0000001.jpg")));
        Assert.True(File.Exists(lib.PosterPath(2)));
        Assert.False(File.Exists(lib.PosterPath(3)));
        Assert.Equal("tt0000001.jpg", Value<string>("SELECT poster FROM titles WHERE tconst=1"));
        Assert.Equal("https://m.media-amazon.com/images/M/b._V1_.jpg", Value<string>("SELECT poster_url FROM titles WHERE tconst=2"));
        Assert.Null(Value<string>("SELECT poster FROM titles WHERE tconst=3"));
        Assert.Equal(4, Value<int>("SELECT count(*) FROM titles WHERE poster_tried=1"));
        Assert.Contains(lib.Job.Log, l => l.Contains("Posters: 2 downloaded, 2 not available"));

        // tried titles are skipped, unless retry
        int calls = http.Count;
        lib.FetchPosters();
        Assert.Equal(calls, http.Count);
        lib.FetchPosters(retry: true);
        Assert.True(http.Count > calls);
        Assert.Equal(2, lib.LoadMovies().Count(m => m.HasPoster));
    }

    [Fact]
    public void FetchPostersDoesNothingWhenSwitchedOff()
    {
        var http = new FakeHttp((_, _) => FakeHttp.Status(HttpStatusCode.NotFound));
        using var lib = NewLib(http);
        lib.SaveSettings(new Settings { OnlinePosters = false });
        AddTitle(1, "A", 2000, "https://x/a._V1_.jpg");
        lib.FetchPosters();
        Assert.Equal(0, http.Count);
        Assert.Equal(0, Value<int>("SELECT poster_tried FROM titles"));
    }

    [Fact]
    public void StopKeepsTheFinishedPosters()
    {
        Library? lib = null;
        int served = 0;
        var http = new FakeHttp((r, _) =>
        {
            if (Interlocked.Increment(ref served) == 8) lib!.Stop();
            return FakeHttp.Bytes(Jpeg);
        });
        lib = NewLib(http);
        for (int i = 1; i <= 60; i++) AddTitle(i, "T" + i, 2000, $"https://m.media-amazon.com/images/M/{i}._V1_.jpg");
        using (lib)
        {
            Assert.True(lib.Start(() => lib.FetchPosters()));
            WaitJob(lib);
            Assert.True(lib.Job.Stopped);
            int saved = Value<int>("SELECT count(*) FROM titles WHERE poster IS NOT NULL");
            Assert.InRange(saved, 1, 59);
            Assert.True(Directory.GetFiles(lib.PosterDir, "*.jpg").Length >= saved);
            Assert.Equal(saved, Value<int>("SELECT count(*) FROM titles WHERE poster_tried=1"));
        }
    }

    // ---------------------------------------------------------------- TMDB job
    [Fact]
    public void FetchTmdbSavesDetailsAndThenFetchesMissingPosters()
    {
        var http = new FakeHttp((r, _) => TmdbRoutes(r));
        using var lib = NewLib(http);
        AddTitle(603, "The Matrix", 1999);
        AddTitle(999, "Unknown", 2001);
        AddTitle(777, "Show", 2010);
        Sql("UPDATE titles SET fa_title='keep me' WHERE tconst=777");
        lib.FetchTmdb();
        Assert.Equal(603, Value<int>("SELECT tmdb_id FROM titles WHERE tconst=603"));
        Assert.Equal("A hacker learns the truth.", Value<string>("SELECT plot FROM titles WHERE tconst=603"));
        Assert.Equal("Free your mind", Value<string>("SELECT tagline FROM titles WHERE tconst=603"));
        Assert.Equal("en", Value<string>("SELECT orig_lang FROM titles WHERE tconst=603"));
        Assert.Equal("""["English","Persian"]""", Value<string>("SELECT languages FROM titles WHERE tconst=603"));
        Assert.Equal("""["United States of America"]""", Value<string>("SELECT countries FROM titles WHERE tconst=603"));
        Assert.Equal("ماتریکس", Value<string>("SELECT fa_title FROM titles WHERE tconst=603"));
        Assert.Equal("The Matrix Collection", Value<string>("SELECT collection FROM titles WHERE tconst=603"));
        Assert.Equal("https://image.tmdb.org/t/p/w1280/b.jpg", Value<string>("SELECT backdrop_url FROM titles WHERE tconst=603"));
        var cast = JsonNode.Parse(Value<string>("SELECT tmdb_cast FROM titles WHERE tconst=603")!)!.AsArray();
        Assert.Equal("Keanu Reeves", (string)cast[0]!["name"]!);
        Assert.Equal("Neo", (string)cast[0]!["ch"]!);
        Assert.Equal("https://image.tmdb.org/t/p/w185/k.jpg", (string)cast[0]!["img"]!);
        // TMDB does not know title 999: still marked done, so it is not asked again
        Assert.Equal(1, Value<int>("SELECT tmdb_done FROM titles WHERE tconst=999"));
        Assert.Null(Value<int?>("SELECT tmdb_id FROM titles WHERE tconst=999"));
        // a TV record fills the same columns; TMDB's Persian title replaces the old one
        Assert.Equal("سریال", Value<string>("SELECT fa_title FROM titles WHERE tconst=777"));
        Assert.Equal("""["US","GB"]""", Value<string>("SELECT countries FROM titles WHERE tconst=777"));
        // posters came from TMDB's poster_url
        Assert.Equal("tt0000603.jpg", Value<string>("SELECT poster FROM titles WHERE tconst=603"));
        Assert.True(File.Exists(lib.PosterPath(603)));
        int calls = http.Count;
        lib.FetchTmdb();
        Assert.Equal(calls, http.Count);
        var m = lib.LoadMovies().Single(x => x.K == "tt0000603");
        Assert.Equal("A hacker learns the truth.", m.Plot);
        Assert.Equal("The Matrix Collection", m.Collection);
    }

    [Fact]
    public void FetchTmdbNeedsAKeyAndReportsErrorsOnce()
    {
        var http = new FakeHttp((_, _) => FakeHttp.Status(HttpStatusCode.Unauthorized));
        using var lib = NewLib(http, tmdbKey: "");
        AddTitle(1, "A", 2000);
        lib.FetchTmdb();
        Assert.Equal(0, http.Count);
        lib.SaveSettings(new Settings { TmdbKey = "k" });
        AddTitle(2, "B", 2001);
        lib.FetchTmdb();
        Assert.Equal(0, Value<int>("SELECT count(*) FROM titles WHERE tmdb_done=1"));
        Assert.Single(lib.Job.Log, l => l.StartsWith("TMDB error") || l.Contains(" TMDB error"));
        Assert.Contains(lib.Job.Log, l => l.Contains("(2 errors)"));
    }

    // ---------------------------------------------------------------- scores jobs
    [Fact]
    public void FetchScoresUsesWikidataThenOmdbUntilTheLimit()
    {
        var http = new FakeHttp((r, body) =>
        {
            if (r.RequestUri!.Host == "query.wikidata.org") return FakeHttp.Json(WikidataRows);
            string q = r.RequestUri.Query;
            if (q.Contains("i=tt0000002")) return FakeHttp.Json("""{"Response":"True","Metascore":"N/A","Ratings":[{"Source":"Rotten Tomatoes","Value":"70%"}]}""");
            return FakeHttp.Json("""{"Response":"False","Error":"Request limit reached!"}""");
        });
        using var lib = NewLib(http, omdbKey: "ok");
        AddTitle(1, "One", 2000, votes: 300);
        AddTitle(2, "Two", 2001, votes: 200);
        AddTitle(3, "Three", 2002, votes: 100);
        lib.FetchScores();
        Assert.Equal(93, Value<int>("SELECT rt FROM titles WHERE tconst=1"));
        Assert.Equal(82, Value<int>("SELECT mc FROM titles WHERE tconst=1"));
        Assert.Equal(61, Value<int>("SELECT mc FROM titles WHERE tconst=2"));
        Assert.Equal(70, Value<int>("SELECT rt FROM titles WHERE tconst=2")); // from OMDb
        Assert.Equal(1, Value<int>("SELECT omdb_done FROM titles WHERE tconst=2"));
        Assert.Equal(0, Value<int>("SELECT omdb_done FROM titles WHERE tconst=3")); // limit hit: continues next time
        Assert.Equal(0, Value<int>("SELECT omdb_done FROM titles WHERE tconst=1")); // had both scores
        Assert.NotNull(Value<string>("SELECT scores_at FROM titles WHERE tconst=3"));
        Assert.Contains(lib.Job.Log, l => l.Contains("found for 2 of 3 titles on Wikidata"));
        Assert.Contains(lib.Job.Log, l => l.Contains("OMDb daily limit reached after 1 titles"));
        // refreshed monthly: a second run asks Wikidata nothing, only OMDb for the leftover
        int wikidata = http.CountOf("query.wikidata.org");
        lib.FetchScores();
        Assert.Equal(wikidata, http.CountOf("query.wikidata.org"));
        lib.FetchScores(force: true);
        Assert.Equal(wikidata + 1, http.CountOf("query.wikidata.org"));
    }

    [Fact]
    public void FetchScoresSurvivesWikidataBeingDown()
    {
        using var lib = NewLib(new FakeHttp((_, _) => FakeHttp.Status(HttpStatusCode.BadGateway)));
        AddTitle(1, "One", 2000);
        lib.FetchScores();
        Assert.Null(Value<string>("SELECT scores_at FROM titles"));
        Assert.Contains(lib.Job.Log, l => l.Contains("Wikidata not reachable"));
    }

    // ---------------------------------------------------------------- people
    [Fact]
    public void PersonProfileFetchesOnceKeepsPhotoAndRefreshes()
    {
        var http = new FakeHttp((r, _) => PersonRoutes(r));
        using var lib = NewLib(http);
        Sql("INSERT INTO people(nconst, name, birth, death, prof) VALUES (1, 'Jane Doe', 1960, NULL, 'actress,writer')");
        var info = lib.PersonProfile(1);
        Assert.True(info.Tmdb);
        Assert.Equal("Jane Doe", info.Name);
        Assert.Equal(1960, info.Birth);
        Assert.Null(info.Death);
        Assert.Equal(new[] { "actress", "writer" }, info.Prof);
        Assert.Null(info.Error);
        Assert.Equal("Born somewhere.", info.Profile!.Biography);
        Assert.Equal("Paris", info.Profile.Place);
        Assert.Equal(3, info.Profile.Credits.Count);
        Assert.True(info.Photo);
        Assert.Equal(Jpeg, File.ReadAllBytes(lib.PersonPhotoPath(1)));
        Assert.EndsWith("nm0000001.jpg", lib.PersonPhotoPath(1));
        Assert.Equal(77, Value<int>("SELECT tmdb_id FROM persons WHERE nconst=1"));
        Assert.Equal(1, Value<int>("SELECT photo FROM persons WHERE nconst=1"));
        // the stored json uses the Python app's keys
        var stored = JsonNode.Parse(Value<string>("SELECT data FROM persons WHERE nconst=1")!)!;
        Assert.Equal(77, (int)stored["tmdb_id"]!);
        Assert.Equal("/pp.jpg", (string)stored["photo_path"]!);
        Assert.Equal("Director", (string)stored["credits"]![0]!["role"]!);

        int calls = http.Count;
        var again = lib.PersonProfile(1);
        Assert.Equal(calls, http.Count); // served from the library
        Assert.Equal("Jane Doe", again.Profile!.Name);
        Assert.True(again.Photo);
        lib.PersonProfile(1, refresh: true);
        Assert.True(http.Count > calls);
        // stale after 90 days
        Sql("UPDATE persons SET fetched='2000-01-01 00:00:00' WHERE nconst=1");
        calls = http.Count;
        lib.PersonProfile(1);
        Assert.True(http.Count > calls);
    }

    [Fact]
    public void PersonProfileReadsRowsWrittenByThePythonApp()
    {
        var http = new FakeHttp((_, _) => FakeHttp.Status(HttpStatusCode.NotFound));
        using var lib = NewLib(http);
        string pyJson = "{\"tmdb_id\": 5, \"name\": \"P\", \"biography\": null, \"birthday\": \"1950-01-01\", \"deathday\": null, \"place\": null, \"department\": \"Directing\", " +
            "\"aka\": [\"X\"], \"homepage\": null, \"photo_path\": null, \"credits\": [{\"id\": 1, \"title\": \"M\", \"year\": null, \"role\": \"Director\", \"detail\": null, \"poster\": null, \"votes\": 0}]}";
        Sql("INSERT INTO persons(nconst, tmdb_id, data, photo, fetched) VALUES (9, 5, ?, 0, ?)", pyJson, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
        var info = lib.PersonProfile(9);
        Assert.Equal("P", info.Profile!.Name);
        Assert.Null(info.Profile.Credits[0].Year);
        Assert.False(info.Photo);
        Assert.Equal(0, http.Count);
    }

    [Fact]
    public void PersonProfileWithoutKeyOrWhenTmdbFails()
    {
        using var lib = NewLib(new FakeHttp((_, _) => FakeHttp.Status(HttpStatusCode.Unauthorized)), tmdbKey: "");
        Sql("INSERT INTO people(nconst, name) VALUES (1, 'Jane')");
        var info = lib.PersonProfile(1);
        Assert.False(info.Tmdb);
        Assert.Null(info.Profile);
        Assert.Equal("Jane", info.Name);
        Assert.Throws<InvalidOperationException>(() => lib.FetchPerson(1));
        lib.SaveSettings(new Settings { TmdbKey = "k" });
        info = lib.PersonProfile(1);
        Assert.True(info.Tmdb);
        Assert.Contains("HttpStatusException", info.Error);
        Assert.Contains("401", info.Error);
        Assert.Equal(0, Value<int>("SELECT count(*) FROM persons"));
    }

    [Fact]
    public void UnknownPersonIsCachedAsEmpty()
    {
        using var lib = NewLib(new FakeHttp((_, _) => FakeHttp.Json("""{"person_results":[]}""")));
        Assert.Null(lib.FetchPerson(5));
        Assert.Equal(1, Value<int>("SELECT count(*) FROM persons WHERE data IS NULL"));
        Assert.Null(lib.PersonProfile(5).Profile);
    }

    void AddCredits(int tc, string directors, string writers, string cast, string crew)
    {
        AddTitle(tc, "M" + tc, 2000);
        Sql("UPDATE titles SET directors=?, writers=?, cast_=?, crew=? WHERE tconst=?", directors, writers, cast, crew, tc);
    }

    [Fact]
    public void PeopleToScanByScope()
    {
        using var lib = NewLib(new FakeHttp((_, _) => FakeHttp.Status(HttpStatusCode.NotFound)));
        AddCredits(1, """[[1,"Dir"],[2,"Dir2"]]""", """[[3,"Wri"]]""",
            """[[4,"a","x"],[5,"b","x"],[6,"c","x"],[7,"d","x"],[8,"e","x"],[9,"f","x"],[10,"g","x"]]""", """[[20,"Crew","grip"],[1,"Dir","director"]]""");
        // a movie that is no longer in the lists is not scanned
        AddCredits(2, """[[99,"Gone"]]""", "[]", "[]", "[]");
        Sql("UPDATE files SET present=0 WHERE tconst=2");
        Sql("INSERT INTO persons(nconst, tmdb_id, data, photo, fetched) VALUES (2, 1, NULL, 0, '2020-01-01 00:00:00')");
        Assert.Equal(new[] { 1, 3 }, lib.PeopleToScan("directors"));
        Assert.Equal(new[] { 1, 3, 4, 5, 6, 7, 8 }, lib.PeopleToScan("main"));
        Assert.Equal(new[] { 1, 3, 4, 5, 6, 7, 8, 9, 10, 20 }, lib.PeopleToScan("all"));
        var st = lib.PeopleStatus();
        Assert.True(st.Tmdb);
        Assert.Equal(0, st.Fetched); // the cached row has no data
        Assert.Equal(0, st.Photos);
        Assert.Equal(new Dictionary<string, int> { ["directors"] = 2, ["main"] = 7, ["all"] = 10 }, st.Todo);
    }

    [Fact]
    public void FetchPeopleFetchesTheScopeWithPhotosAndCounts()
    {
        var http = new FakeHttp((r, _) => PersonRoutes(r));
        using var lib = NewLib(http);
        AddCredits(1, """[[1,"Jane"]]""", """[[3,"Writer"]]""", "[]", "[]");
        lib.FetchPeople("directors");
        Assert.Equal(2, Value<int>("SELECT count(*) FROM persons WHERE data IS NOT NULL"));
        Assert.True(File.Exists(lib.PersonPhotoPath(1)));
        Assert.False(File.Exists(lib.PersonPhotoPath(3)));
        var st = lib.PeopleStatus();
        Assert.Equal((2, 1), (st.Fetched, st.Photos));
        Assert.Equal(0, st.Todo["directors"]);
        Assert.Contains(lib.Job.Log, l => l.Contains("People: 2 profiles fetched"));
        int calls = http.Count;
        lib.FetchPeople("directors");
        Assert.Equal(calls, http.Count);
        Assert.Contains(lib.Job.Log, l => l.Contains("all profiles are already fetched"));
    }

    [Fact]
    public void FetchPeopleNeedsAKeyAndCountsFailures()
    {
        using var lib = NewLib(new FakeHttp((_, _) => FakeHttp.Status(HttpStatusCode.Unauthorized)), tmdbKey: "");
        Assert.Throws<InvalidOperationException>(() => lib.FetchPeople());
        lib.SaveSettings(new Settings { TmdbKey = "k" });
        AddCredits(1, """[[1,"Jane"],[2,"Bob"]]""", "[]", "[]", "[]");
        lib.FetchPeople("directors");
        Assert.Contains(lib.Job.Log, l => l.Contains("People: 0 profiles fetched, 2 failed"));
        Assert.Single(lib.Job.Log, l => l.Contains("TMDB error (people)"));
    }

    // ---------------------------------------------------------------- collections
    [Fact]
    public void CollectionsViewListsWhatIsMissing()
    {
        using var lib = NewLib(new FakeHttp((_, _) => FakeHttp.Status(HttpStatusCode.NotFound)));
        AddTitle(1, "The Matrix", 1999);
        AddTitle(2, "The Matrix Reloaded", 2003);
        AddTitle(3, "Heat", 1995);
        AddTitle(4, "Heat 2", 2025);
        AddTitle(5, "Solo", 2000);
        AddTitle(6, "Gone", 2000);
        Sql("UPDATE titles SET collection='Matrix Collection' WHERE tconst IN (1, 2)");
        Sql("UPDATE titles SET collection='Heat Collection' WHERE tconst IN (3, 4)");
        Sql("UPDATE titles SET collection='Solo Collection' WHERE tconst=5");
        Sql("UPDATE titles SET collection='Old Collection' WHERE tconst=6");
        Sql("UPDATE files SET present=0 WHERE tconst=6");
        Sql("UPDATE titles SET orig='Heat' WHERE tconst=3");
        Sql("INSERT INTO collections(name, tmdb_id, parts) VALUES ('Matrix Collection', 2, ?)",
            """[{"title":"The Matrix","orig":"The Matrix","year":1999},{"title":"The Matrix Reloaded","orig":null,"year":2002},{"title":"The Matrix Revolutions","orig":null,"year":2003},{"title":"Animatrix","orig":null,"year":null}]""");
        Sql("INSERT INTO collections(name, tmdb_id, parts) VALUES ('Heat Collection', 3, ?)",
            """[{"title":"Heat","orig":"Heat","year":1995},{"title":"Heat 2","orig":null,"year":2025}]""");
        var all = lib.Collections();
        Assert.Equal(new[] { "Matrix Collection", "Heat Collection", "Solo Collection" }, all.Select(c => c.Name)); // missing first, then bigger, then by name
        var matrix = all[0];
        Assert.True(matrix.Fetched);
        Assert.Equal(4, matrix.Total);
        Assert.Equal(new[] { ("tt0000001", 1999), ("tt0000002", 2003) }, matrix.Have.Select(h => (h.K, h.Year!.Value)));
        Assert.Equal("The Matrix Reloaded", matrix.Have[1].Title);
        // Reloaded 2002 vs 2003 is within a year; Revolutions is not owned; a part without a year can never match
        Assert.Equal(new[] { "The Matrix Revolutions", "Animatrix" }, matrix.Missing.Select(m => m.Title));
        Assert.Empty(all[1].Missing);
        Assert.Equal(2, all[1].Total);
        Assert.False(all[2].Fetched);
        Assert.Null(all[2].Total);
    }

    [Fact]
    public void FetchCollectionsStoresPartsAndSkipsFailures()
    {
        var http = new FakeHttp((r, _) =>
        {
            string path = r.RequestUri!.AbsolutePath, q = r.RequestUri.Query;
            if (path == "/3/search/collection") return q.Contains("Matrix") ? FakeHttp.Json("""{"results":[{"id":2,"name":"Matrix Collection"}]}""")
                : q.Contains("Broken") ? FakeHttp.Status(HttpStatusCode.Unauthorized) : FakeHttp.Json("""{"results":[]}""");
            if (path == "/3/collection/2") return FakeHttp.Json("""{"parts":[{"title":"Reloaded","original_title":"R","release_date":"2003-05-15"},{"title":"The Matrix","original_title":"The Matrix","release_date":"1999-03-31"}]}""");
            return FakeHttp.Status(HttpStatusCode.NotFound);
        });
        using var lib = NewLib(http);
        AddTitle(1, "The Matrix", 1999);
        AddTitle(2, "Broken Thing", 2000);
        AddTitle(3, "Nobody", 2001);
        Sql("UPDATE titles SET collection='Matrix Collection' WHERE tconst=1");
        Sql("UPDATE titles SET collection='Broken Collection' WHERE tconst=2");
        Sql("UPDATE titles SET collection='Unknown Collection' WHERE tconst=3");
        lib.FetchCollections();
        Assert.Equal(1, Value<int>("SELECT count(*) FROM collections"));
        Assert.Equal(2, Value<int>("SELECT tmdb_id FROM collections WHERE name='Matrix Collection'"));
        var parts = JsonNode.Parse(Value<string>("SELECT parts FROM collections")!)!.AsArray();
        Assert.Equal("The Matrix", (string)parts[0]!["title"]!);
        Assert.Equal("R", (string)parts[1]!["orig"]!);
        Assert.Equal(2003, (int)parts[1]!["year"]!);
        Assert.Contains(lib.Job.Log, l => l.Contains("Collection Broken Collection:"));
        Assert.Contains(lib.Job.Log, l => l.Contains("Collections: checked 3"));
        var m = lib.Collections().Single(c => c.Name == "Matrix Collection");
        Assert.Equal(new[] { "Reloaded" }, m.Missing.Select(x => x.Title));
        lib.SaveSettings(new Settings { TmdbKey = "" });
        Assert.Throws<InvalidOperationException>(() => lib.FetchCollections());
    }
}
