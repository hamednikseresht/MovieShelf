using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace MovieShelf.Core;

/// <summary>One AI provider the user can choose in Settings → AI. The same keys and request shapes as the Python app (movieshelf/ai.py).</summary>
public sealed record AiProvider(string Key, string Label, string DefaultModel, bool NeedsKey, string DefaultUrl);

/// <summary>What AI search made of a request: a query in the search language, IMDb ids of films it named (only the ones in the library show), a short note.</summary>
public sealed record AiSearchResult(string Query, List<string> Imdb, string Note);

/// <summary>Talks to the chosen AI provider over plain HTTPS: Anthropic (Messages API), OpenAI and DeepSeek (chat completions), Google (Gemini generateContent)
/// and Ollama on this PC (/api/chat). No SDK: one small request per feature, and the app has no extra dependency.</summary>
public sealed class AiClient
{
    public static readonly AiProvider[] Providers =
    {
        new("anthropic", "Anthropic (Claude)", "claude-opus-5-5", true, "https://api.anthropic.com"),
        new("openai", "OpenAI", "", true, "https://api.openai.com/v1"),
        new("deepseek", "DeepSeek", "deepseek-chat", true, "https://api.deepseek.com"),
        new("google", "Google (Gemini)", "", true, "https://generativelanguage.googleapis.com/v1beta"),
        new("ollama", "Ollama (on this PC)", "", false, "http://localhost:11434"),
    };

    public static AiProvider? Find(string? key) => Providers.FirstOrDefault(p => p.Key == key);

    readonly OnlineClient net;
    readonly AiProvider provider;
    readonly string key, model, url;

    public AiClient(OnlineClient net, string providerKey, string apiKey, string model, string url)
    {
        this.net = net;
        provider = Find(providerKey) ?? throw new InvalidOperationException("Choose an AI provider in Settings → AI first.");
        key = (apiKey ?? "").Trim();
        this.model = (model ?? "").Trim().Length > 0 ? model!.Trim() : provider.DefaultModel;
        this.url = ((url ?? "").Trim().Length > 0 ? url!.Trim() : provider.DefaultUrl).TrimEnd('/');
        if (provider.NeedsKey && key.Length == 0) throw new InvalidOperationException($"Add your {provider.Label} API key in Settings → AI.");
    }

    public string Model => model;

    static StringContent Json(JsonObject o) => new(o.ToJsonString(), Encoding.UTF8, "application/json");

    // Claude models that take output_config.effort and the server-side refusal fallback ("default" picks the fallback by refusal category)
    static bool CurrentClaude(string m) => Regex.IsMatch(m, @"^claude-(opus|sonnet|fable|mythos|haiku)-5");
    static bool FallbackClaude(string m) => Regex.IsMatch(m, @"^claude-(opus-5|sonnet-5-5|fable-5-[12])");

    async Task<JsonNode> PostAsync(string address, JsonObject body, List<KeyValuePair<string, string>> headers, CancellationToken ct, double timeout = 120)
    {
        var (status, bytes) = await net.SendAsync(HttpMethod.Post, address, headers, Json(body), timeout, ct).ConfigureAwait(false);
        string text = Encoding.UTF8.GetString(bytes);
        if (status >= 400) throw new InvalidOperationException($"{provider.Label}: HTTP {status}{ErrorText(text)}");
        return JsonNode.Parse(text) ?? throw new InvalidOperationException($"{provider.Label} sent an empty answer.");
    }

    static string ErrorText(string body)
    {
        try
        {
            var n = JsonNode.Parse(body);
            var e = n?["error"];
            string? m = e is JsonObject ? (string?)e["message"] : e is JsonValue ? (string?)e : (string?)n?["message"];
            return string.IsNullOrWhiteSpace(m) ? "" : " – " + m;
        }
        catch (JsonException) { return body.Length > 0 ? " – " + body[..Math.Min(200, body.Length)] : ""; }
    }

    /// <summary>One question, one answer (text). Blocking for the network: call it off the UI thread.</summary>
    public async Task<string> AskAsync(string system, string user, int maxTokens = 4000, CancellationToken ct = default)
    {
        if (model.Length == 0) throw new InvalidOperationException($"Choose a model for {provider.Label} in Settings → AI (press “List models”).");
        var h = new List<KeyValuePair<string, string>> { new("Accept", "application/json") };
        switch (provider.Key)
        {
            case "anthropic":
            {
                h.Add(new("x-api-key", key));
                h.Add(new("anthropic-version", "2026-10-21"));
                var body = new JsonObject
                {
                    ["model"] = model, ["max_tokens"] = maxTokens, ["system"] = system,
                    ["messages"] = new JsonArray(new JsonObject { ["role"] = "user", ["content"] = user }),
                };
                if (CurrentClaude(model)) body["output_config"] = new JsonObject { ["effort"] = "low" };   // short, simple tasks: quick answers
                if (FallbackClaude(model))
                {
                    h.Add(new("anthropic-beta", "server-side-fallback-2026-07-01"));
                    body["fallbacks"] = "default";
                }
                var r = await PostAsync(url + "/v1/messages", body, h, ct).ConfigureAwait(false);
                if ((string?)r["stop_reason"] == "refusal") throw new InvalidOperationException("Claude declined this request.");
                var text = string.Concat((r["content"] as JsonArray ?? new()).Where(b => (string?)b?["type"] == "text").Select(b => (string?)b!["text"]));
                return text;
            }
            case "openai":
            case "deepseek":
            {
                h.Add(new("Authorization", "Bearer " + key));
                var body = new JsonObject
                {
                    ["model"] = model,
                    ["messages"] = new JsonArray(new JsonObject { ["role"] = "system", ["content"] = system }, new JsonObject { ["role"] = "user", ["content"] = user }),
                };
                var r = await PostAsync(url + "/chat/completions", body, h, ct).ConfigureAwait(false);
                return (string?)r["choices"]?[0]?["message"]?["content"] ?? "";
            }
            case "google":
            {
                h.Add(new("x-goog-api-key", key));
                var body = new JsonObject
                {
                    ["systemInstruction"] = new JsonObject { ["parts"] = new JsonArray(new JsonObject { ["text"] = system }) },
                    ["contents"] = new JsonArray(new JsonObject { ["role"] = "user", ["parts"] = new JsonArray(new JsonObject { ["text"] = user }) }),
                };
                string m = model.StartsWith("models/", StringComparison.Ordinal) ? model[7..] : model;
                var r = await PostAsync($"{url}/models/{Uri.EscapeDataString(m)}:generateContent", body, h, ct).ConfigureAwait(false);
                var parts = r["candidates"]?[0]?["content"]?["parts"] as JsonArray ?? new();
                return string.Concat(parts.Select(p => (string?)p?["text"]));
            }
            default:   // ollama
            {
                var body = new JsonObject
                {
                    ["model"] = model, ["stream"] = false,
                    ["messages"] = new JsonArray(new JsonObject { ["role"] = "system", ["content"] = system }, new JsonObject { ["role"] = "user", ["content"] = user }),
                };
                var r = await PostAsync(url + "/api/chat", body, h, ct, timeout: 300).ConfigureAwait(false);
                return (string?)r["message"]?["content"] ?? "";
            }
        }
    }

    /// <summary>The models this key can use, as the provider lists them.</summary>
    public async Task<List<string>> ModelsAsync(CancellationToken ct = default)
    {
        var h = new List<KeyValuePair<string, string>>();
        string address;
        switch (provider.Key)
        {
            case "anthropic": address = url + "/v1/models?limit=100"; h.Add(new("x-api-key", key)); h.Add(new("anthropic-version", "2026-10-21")); break;
            case "openai": case "deepseek": address = url + "/models"; h.Add(new("Authorization", "Bearer " + key)); break;
            case "google": address = url + "/models?pageSize=200"; h.Add(new("x-goog-api-key", key)); break;
            default: address = url + "/api/tags"; break;
        }
        var (status, bytes) = await net.SendAsync(HttpMethod.Get, address, h, null, 30, ct).ConfigureAwait(false);
        string text = Encoding.UTF8.GetString(bytes);
        if (status >= 400) throw new InvalidOperationException($"{provider.Label}: HTTP {status}{ErrorText(text)}");
        var n = JsonNode.Parse(text);
        IEnumerable<string?> names = provider.Key switch
        {
            "google" => (n?["models"] as JsonArray ?? new()).Where(m => (m?["supportedGenerationMethods"] as JsonArray)?.Any(x => (string?)x == "generateContent") != false)
                .Select(m => ((string?)m?["name"])?.Replace("models/", "")),
            "ollama" => (n?["models"] as JsonArray ?? new()).Select(m => (string?)m?["name"]),
            _ => (n?["data"] as JsonArray ?? new()).Select(m => (string?)m?["id"]),
        };
        return names.Where(x => !string.IsNullOrEmpty(x)).Select(x => x!).Distinct().ToList();
    }

    /// <summary>The first JSON object in a model's answer (some wrap it in a code fence or a sentence).</summary>
    public static JsonObject? JsonIn(string text)
    {
        int a = text.IndexOf('{'), b = text.LastIndexOf('}');
        if (a < 0 || b <= a) return null;
        try { return JsonNode.Parse(text[a..(b + 1)]) as JsonObject; }
        catch (JsonException) { return null; }
    }
}

public sealed partial class Library
{
    const string AiSchema = "CREATE TABLE IF NOT EXISTS ai_text(tconst INTEGER, lang TEXT, plot TEXT, tagline TEXT, made TEXT, PRIMARY KEY(tconst, lang))";

    /// <summary>True when an AI provider is set up (Settings → AI).</summary>
    public bool AiReady()
    {
        var s = LoadSettings();
        var p = AiClient.Find(s.AiProvider);
        return p != null && (!p.NeedsKey || s.AiKey.Length > 0);
    }

    AiClient Ai(Settings? s = null)
    {
        s ??= LoadSettings();
        return new AiClient(Net, s.AiProvider, s.AiKey, s.AiModel, s.AiUrl);
    }

    /// <summary>Settings → AI → Test: a tiny question with the given choices (not yet saved). The answer, or why it failed.</summary>
    public async Task<KeyTest> TestAiAsync(string provider, string key, string model, string url, CancellationToken ct = default)
    {
        try
        {
            var c = new AiClient(Net, provider, key, model, url);
            string a = await c.AskAsync("You check that an API connection works.", "Reply with the single word: OK", 50, ct).ConfigureAwait(false);
            return new KeyTest(a.Trim().Length > 0, a.Trim().Length > 0 ? $"{c.Model}: {a.Trim()}" : "The model sent an empty answer.");
        }
        catch (Exception e) when (e is not OperationCanceledException) { return new KeyTest(false, e.Message); }
    }

    public Task<List<string>> AiModelsAsync(string provider, string key, string url, CancellationToken ct = default) =>
        new AiClient(Net, provider, key, "x", url).ModelsAsync(ct);

    public const string AiSearchPrompt = """
        You turn a request for a personal movie library into the library's search language. Reply with one JSON object only:
        {"query": "...", "imdb": ["tt0000000", ...], "note": "..."}

        The search language (all parts must match):
        - free words match titles, people and file names; "exact phrase"; -word leaves it out
        - year:1999  year:1990-1999  year:>2010   runtime:<100 (minutes)   rating:>8 (IMDb)   votes:>50k   rt:>90 (Rotten Tomatoes)   mc:>70 (Metacritic)   myrating:>=8
        - genre:comedy  dir:nolan  writer:kaufman  actor:pitt  person:name  country:france  lang:persian  collection:"star wars"  type:series
        - res:4k res:1080p  src:bluray  codec:x265  audio:dts  sub:persian
        - is:fav is:watched is:unwatched is:dubbed is:dual is:hdr is:4k is:top is:noposter   has:poster has:plot has:rt has:cast has:notes
        Field values are words, not regular expressions. Use only these fields; put anything else as free words or leave it out.

        Rules:
        - "query": the search that fits the request, using the fields above. An empty string when the request only names films or a theme.
        - "imdb": when the request names films, or asks for a theme, plot, mood or "films like X" that the fields cannot express, list up to 40 IMDb ids
          (tt numbers) of well-known films that fit, only ids you are sure of. Otherwise an empty list.
        - "note": one short sentence, in the language of the request, saying what you searched for.
        """;

    /// <summary>AI search: a request in plain words becomes a search (and, for a theme or named films, IMDb ids). Blocking: call it off the UI thread.</summary>
    public async Task<AiSearchResult> AiSearchAsync(string request, IEnumerable<string>? genres = null, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(request)) throw new InvalidOperationException("Type what you are looking for.");
        string extra = genres == null ? "" : "\nGenres in this library: " + string.Join(", ", genres.Take(60));
        string answer = await Ai().AskAsync(AiSearchPrompt + extra, request.Trim(), 4000, ct).ConfigureAwait(false);
        var o = AiClient.JsonIn(answer) ?? throw new InvalidOperationException("The AI did not answer in the expected form. Try again or rephrase.");
        var ids = (o["imdb"] as JsonArray ?? new()).Select(x => (string?)x).Where(x => x != null && Regex.IsMatch(x, @"^tt\d{5,9}$"))
            .Select(x => $"tt{int.Parse(x![2..], CultureInfo.InvariantCulture):0000000}").Distinct().Take(40).ToList();
        return new AiSearchResult(((string?)o["query"] ?? "").Trim(), ids, ((string?)o["note"] ?? "").Trim());
    }

    /// <summary>The plot and tagline of a title in the translation language (Settings → AI), from the cache or the AI. Blocking: call it off the UI thread.</summary>
    public async Task<(string Plot, string Tagline)> TranslateAsync(int tconst, bool again = false, CancellationToken ct = default)
    {
        var s = LoadSettings();
        string lang = s.AiLang.Trim().Length > 0 ? s.AiLang.Trim() : "Persian";
        string? plot, tag;
        using (var rd = Reader())
        {
            if (!again)
                using (var c = Cmd(rd, "SELECT plot, tagline FROM ai_text WHERE tconst=? AND lang=?", new object?[] { tconst, lang }))
                using (var r = c.ExecuteReader())
                    if (r.Read()) return (r.IsDBNull(0) ? "" : r.GetString(0), r.IsDBNull(1) ? "" : r.GetString(1));
            using (var c = Cmd(rd, "SELECT plot, tagline, coalesce(en_title, title) FROM titles WHERE tconst=?", new object?[] { tconst }))
            using (var r = c.ExecuteReader())
            {
                if (!r.Read()) throw new InvalidOperationException("This title is not in the library.");
                plot = r.IsDBNull(0) ? null : r.GetString(0);
                tag = r.IsDBNull(1) ? null : r.GetString(1);
            }
        }
        if (string.IsNullOrWhiteSpace(plot) && string.IsNullOrWhiteSpace(tag)) throw new InvalidOperationException("This movie has no plot to translate yet (add a TMDB key in Settings → Online data).");
        string system = $"You translate movie descriptions into {lang}. Keep names of people and films as they are usually written in {lang}. " +
                        "Reply with one JSON object only: {\"plot\": \"...\", \"tagline\": \"...\"} (an empty string for a part that was empty).";
        string user = JsonSerializer.Serialize(new { plot = plot ?? "", tagline = tag ?? "" });
        var o = AiClient.JsonIn(await Ai(s).AskAsync(system, user, 8000, ct).ConfigureAwait(false))
                ?? throw new InvalidOperationException("The AI did not answer in the expected form. Try again.");
        string p2 = ((string?)o["plot"] ?? "").Trim(), t2 = ((string?)o["tagline"] ?? "").Trim();
        lock (Lock) Exec("INSERT OR REPLACE INTO ai_text(tconst, lang, plot, tagline, made) VALUES (?,?,?,?,?)", tconst, lang, p2, t2, Now());
        return (p2, t2);
    }
}
