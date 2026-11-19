using System.Net;
using System.Text.Json.Nodes;
using Microsoft.Data.Sqlite;
using MovieShelf.Core;
using Xunit;

namespace MovieShelf.Core.Tests;

/// <summary>Settings → AI: the request each provider gets, how its answer is read, AI search and translation. Offline: a fake handler answers.</summary>
public class AiTests : IDisposable
{
    readonly TempLibrary t;
    readonly Queue<string> answers = new();
    readonly FakeHttp http;

    public AiTests()
    {
        http = new FakeHttp((_, _) => FakeHttp.Json(answers.Count > 0 ? answers.Dequeue() : "{}"));
        t = new TempLibrary(http);
    }

    public void Dispose() => t.Dispose();

    Library Lib(string provider, string model = "", string key = "k-1", string url = "")
    {
        var lib = t.Open(true, "Heat.1995.720p", "The.Godfather.1972.1080p");
        lib.Update();
        var s = lib.LoadSettings();
        s.AiProvider = provider; s.AiKey = key; s.AiModel = model; s.AiUrl = url;
        lib.SaveSettings(s);
        return lib;
    }

    static string Header(HttpRequestMessage r, string name) => r.Headers.TryGetValues(name, out var v) ? string.Join(",", v) : "";

    [Fact]
    public void AnthropicGetsTheMessagesApiShape()
    {
        using var lib = Lib("anthropic");
        answers.Enqueue("""{"stop_reason":"end_turn","content":[{"type":"text","text":"OK"}]}""");
        var r = lib.TestAiAsync("anthropic", "k-1", "", "").Result;
        Assert.True(r.Ok, r.Message);
        var (_, url, body, req) = http.Calls.Last();
        Assert.Equal("https://api.anthropic.com/v1/messages", url);
        Assert.Equal("k-1", Header(req, "x-api-key"));
        Assert.Equal("2026-10-21", Header(req, "anthropic-version"));
        Assert.Equal("server-side-fallback-2026-07-01", Header(req, "anthropic-beta"));
        var o = JsonNode.Parse(body)!;
        Assert.Equal("claude-opus-5-5", (string?)o["model"]);
        Assert.Equal("default", (string?)o["fallbacks"]);
        Assert.Equal("low", (string?)o["output_config"]?["effort"]);
        Assert.Equal("user", (string?)o["messages"]?[0]?["role"]);
        Assert.Null(o["temperature"]);
    }

    [Fact]
    public void AClaudeRefusalIsAnErrorNotAnAnswer()
    {
        using var lib = Lib("anthropic");
        answers.Enqueue("""{"stop_reason":"refusal","content":[]}""");
        var r = lib.TestAiAsync("anthropic", "k-1", "", "").Result;
        Assert.False(r.Ok);
        Assert.Contains("declined", r.Message);
    }

    [Theory]
    [InlineData("openai", "https://api.openai.com/v1/chat/completions")]
    [InlineData("deepseek", "https://api.deepseek.com/chat/completions")]
    public void OpenAiStyleProvidersGetChatCompletions(string provider, string expected)
    {
        using var lib = Lib(provider, "m-1");
        answers.Enqueue("""{"choices":[{"message":{"content":"OK"}}]}""");
        var r = lib.TestAiAsync(provider, "k-1", "m-1", "").Result;
        Assert.True(r.Ok, r.Message);
        var (_, url, body, req) = http.Calls.Last();
        Assert.Equal(expected, url);
        Assert.Equal("Bearer k-1", Header(req, "Authorization"));
        Assert.Equal("system", (string?)JsonNode.Parse(body)!["messages"]?[0]?["role"]);
    }

    [Fact]
    public void GoogleGetsGenerateContentWithTheKeyInAHeader()
    {
        using var lib = Lib("google", "gemini-x");
        answers.Enqueue("""{"candidates":[{"content":{"parts":[{"text":"O"},{"text":"K"}]}}]}""");
        var r = lib.TestAiAsync("google", "k-1", "gemini-x", "").Result;
        Assert.True(r.Ok, r.Message);
        Assert.Equal("gemini-x: OK", r.Message);
        var (_, url, _, req) = http.Calls.Last();
        Assert.Equal("https://generativelanguage.googleapis.com/v1beta/models/gemini-x:generateContent", url);
        Assert.Equal("k-1", Header(req, "x-goog-api-key"));
        Assert.DoesNotContain("key=", url);
    }

    [Fact]
    public void OllamaNeedsNoKeyAndUsesTheGivenAddress()
    {
        using var lib = Lib("ollama", "llama-x", key: "", url: "http://box:11434/");
        answers.Enqueue("""{"message":{"content":"OK"}}""");
        var r = lib.TestAiAsync("ollama", "", "llama-x", "http://box:11434/").Result;
        Assert.True(r.Ok, r.Message);
        Assert.Equal("http://box:11434/api/chat", http.Calls.Last().Url);
        Assert.False((bool?)JsonNode.Parse(http.Calls.Last().Body)!["stream"] ?? true);
    }

    [Fact]
    public void AMissingKeyOrModelSaysWhatToDo()
    {
        using var lib = Lib("openai", "m");
        Assert.Contains("API key", lib.TestAiAsync("openai", "", "m", "").Result.Message);
        Assert.Contains("model", lib.TestAiAsync("openai", "k", "", "").Result.Message);
        Assert.Equal(0, http.CountOf("chat/completions"));
    }

    [Fact]
    public void AProviderErrorShowsItsMessage()
    {
        var bad = new FakeHttp((_, _) => FakeHttp.Json("""{"error":{"message":"invalid x-api-key"}}""", HttpStatusCode.Unauthorized));
        using var t2 = new TempLibrary(bad);
        using var lib = t2.Open(true, "Heat.1995.720p");
        var r = lib.TestAiAsync("anthropic", "nope", "", "").Result;
        Assert.False(r.Ok);
        Assert.Contains("401", r.Message);
        Assert.Contains("invalid x-api-key", r.Message);
    }

    [Fact]
    public void ModelsAreListedPerProvider()
    {
        using var lib = Lib("ollama", key: "");
        answers.Enqueue("""{"models":[{"name":"llama3:8b"},{"name":"qwen:7b"}]}""");
        Assert.Equal(new[] { "llama3:8b", "qwen:7b" }, lib.AiModelsAsync("ollama", "", "").Result);
        answers.Enqueue("""{"models":[{"name":"models/gemini-a","supportedGenerationMethods":["generateContent"]},{"name":"models/embed","supportedGenerationMethods":["embedContent"]}]}""");
        Assert.Equal(new[] { "gemini-a" }, lib.AiModelsAsync("google", "k", "").Result);
    }

    [Fact]
    public void AiSearchReadsTheJsonEvenInsideACodeFence()
    {
        using var lib = Lib("deepseek");
        answers.Enqueue("""{"choices":[{"message":{"content":"Here:\n```json\n{\"query\": \"genre:crime year:1990-1999\", \"imdb\": [\"tt113277\", \"bad\", \"tt0068646\"], \"note\": \"90s crime\"}\n```"}}]}""");
        var r = lib.AiSearchAsync("90s crime films", new[] { "Crime" }).Result;
        Assert.Equal("genre:crime year:1990-1999", r.Query);
        Assert.Equal(new[] { "tt0113277", "tt0068646" }, r.Imdb);
        Assert.Equal("90s crime", r.Note);
        Assert.Contains("Genres in this library: Crime", http.Calls.Last().Body);
    }

    [Fact]
    public void AListOfIdsFindsThoseMovies()
    {
        using var lib = Lib("deepseek");
        var movies = lib.LoadMovies();
        var found = movies.Where(SearchQuery.Parse("id:tt0000002,tt0000001,tt9999999").Test).Select(m => m.K).OrderBy(k => k).ToList();
        Assert.Equal(new[] { "tt0000001", "tt0000002" }, found);
        Assert.Single(movies.Where(SearchQuery.Parse("id:tt0000002").Test));
    }

    [Fact]
    public void ATranslationIsAskedOnceThenKept()
    {
        using var lib = Lib("deepseek");
        using (var c = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = lib.DbPath, Pooling = false }.ToString()))
        {
            c.Open();
            using var cmd = c.CreateCommand();
            cmd.CommandText = "UPDATE titles SET plot='A crew plans a heist.', tagline='A Los Angeles crime saga' WHERE tconst=2";
            cmd.ExecuteNonQuery();
        }
        answers.Enqueue("""{"choices":[{"message":{"content":"{\"plot\": \"گروهی سرقتی را برنامه می‌ریزند.\", \"tagline\": \"حماسه‌ای جنایی\"}"}}]}""");
        var (plot, tag) = lib.TranslateAsync(2).Result;
        Assert.Equal("گروهی سرقتی را برنامه می‌ریزند.", plot);
        Assert.Equal("حماسه‌ای جنایی", tag);
        Assert.Contains("into Persian", http.Calls.Last().Body);
        int asked = http.CountOf("chat/completions");
        Assert.Equal(plot, lib.TranslateAsync(2).Result.Plot);
        Assert.Equal(asked, http.CountOf("chat/completions"));   // from the cache
    }

    [Fact]
    public void NothingToTranslateIsSaid()
    {
        using var lib = Lib("deepseek");
        var e = Assert.Throws<AggregateException>(() => lib.TranslateAsync(1).Result);
        Assert.Contains("no plot", e.InnerException!.Message);
    }

    [Fact]
    public void AiIsReadyOnlyWithAProviderAndItsKey()
    {
        using var lib = Lib("", key: "");
        Assert.False(lib.AiReady());
        var s = lib.LoadSettings(); s.AiProvider = "openai"; lib.SaveSettings(s);
        Assert.False(lib.AiReady());
        s.AiKey = "k"; lib.SaveSettings(s);
        Assert.True(lib.AiReady());
        s.AiProvider = "ollama"; s.AiKey = ""; lib.SaveSettings(s);
        Assert.True(lib.AiReady());
    }
}
