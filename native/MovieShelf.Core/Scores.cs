using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace MovieShelf.Core;

/// <summary>Rotten Tomatoes (Tomatometer, percent) and Metacritic (Metascore, of 100) for a title. Null: not known.</summary>
public sealed record CriticScores(int? Rt = null, int? Mc = null)
{
    public bool Any => Rt != null || Mc != null;
}

/// <summary>OMDb's daily limit is used up.</summary>
public sealed class LimitReachedException : Exception
{
    public LimitReachedException(string message) : base(message) { }
}

/// <summary>OMDb rejects the key (Python's PermissionError).</summary>
public sealed class InvalidApiKeyException : Exception
{
    public InvalidApiKeyException(string message) : base(message) { }
}

/// <summary>Critic scores. Primary source is Wikidata (free, no key, bulk queries by IMDb id). The optional OMDb key
/// (omdbapi.com, free tier 1000 requests/day) fills titles Wikidata lacks.</summary>
public static class CriticSources
{
    public const string Wikidata = "https://query.wikidata.org/sparql";
    public const string Omdb = "https://www.omdbapi.com/";
    const string RtId = "Q105584", McId = "Q150248";   // review score by: Rotten Tomatoes, Metacritic
    const string Tomatometer = "Q108403393";           // determination method: Tomatometer score

    static readonly Regex Pct = new(@"\A(\d{1,3})\s*%\z", RegexOptions.Compiled);
    static readonly Regex Of100 = new(@"\A(\d{1,3})\s*/\s*100\z", RegexOptions.Compiled);
    static readonly Regex Plain = new(@"\A(\d{1,3})\z", RegexOptions.Compiled);
    static readonly Regex OmdbPct = new(@"\A(\d+)%", RegexOptions.Compiled);

    /// <summary>Scores for a batch of IMDb ids (keep batches at 300 or fewer). Ids Wikidata has no score for are missing from the result.</summary>
    public static async Task<Dictionary<int, CriticScores>> WikidataScoresAsync(OnlineClient http, IEnumerable<int> tconsts, CancellationToken ct = default)
    {
        string ids = string.Join(" ", tconsts.Select(t => $"\"tt{t:0000000}\""));
        string q = $@"SELECT ?imdb ?score ?by ?method ?date WHERE {{
      VALUES ?imdb {{ {ids} }}
      ?item wdt:P345 ?imdb ; p:P444 ?st .
      ?st ps:P444 ?score ; pq:P447 ?by .
      OPTIONAL {{ ?st pq:P459 ?method }}
      OPTIONAL {{ ?st pq:P585 ?date }}
      FILTER(?by IN (wd:{RtId}, wd:{McId}))
    }}";
        var form = new FormUrlEncodedContent(new[] { new KeyValuePair<string, string>("query", q), new KeyValuePair<string, string>("format", "json") });
        var hdr = new[] { new KeyValuePair<string, string>("Accept", "application/sparql-results+json") };
        var (status, body) = await http.SendAsync(HttpMethod.Post, Wikidata, hdr, form, 90, ct).ConfigureAwait(false);
        if (status >= 400) throw new HttpStatusException(status, Encoding.UTF8.GetString(body));
        var rows = J.Arr(J.Get(JsonNode.Parse(body), "results"), "bindings");
        var best = new Dictionary<(int, string), (string Date, int Val)>(); // (tconst, kind) -> (date, value)
        foreach (var b in J.Items(rows))
        {
            string V(string k) => J.Str(J.Get(b, k), "value") ?? "";
            string imdb = V("imdb");
            if (imdb.Length < 3 || !int.TryParse(imdb.AsSpan(2), out int t)) continue;
            string by = V("by")[(V("by").LastIndexOf('/') + 1)..], method = V("method")[(V("method").LastIndexOf('/') + 1)..], score = V("score").Trim();
            string kind;
            int val;
            if (by == RtId)
            {
                var m = Pct.Match(score);
                if (!m.Success || (method.Length > 0 && method != Tomatometer)) continue; // skip audience score / average rating
                kind = "rt";
                val = int.Parse(m.Groups[1].Value);
            }
            else
            {
                var m = Of100.Match(score);
                if (!m.Success) m = Plain.Match(score);
                if (!m.Success) continue;
                kind = "mc";
                val = int.Parse(m.Groups[1].Value);
            }
            if (val > 100) continue;
            string date = V("date");
            if (!best.TryGetValue((t, kind), out var cur) || string.CompareOrdinal(date, cur.Date) > 0) best[(t, kind)] = (date, val);
        }
        var outMap = new Dictionary<int, CriticScores>();
        foreach (var ((t, kind), (_, val)) in best)
        {
            outMap.TryGetValue(t, out var s);
            outMap[t] = (s ?? new CriticScores()) with { Rt = kind == "rt" ? val : s?.Rt, Mc = kind == "mc" ? val : s?.Mc };
        }
        return outMap;
    }

    /// <summary>OMDb's Rotten Tomatoes and Metascore for one title (empty when OMDb has none).
    /// Throws <see cref="LimitReachedException"/> at the daily limit, <see cref="InvalidApiKeyException"/> for a bad key.</summary>
    public static async Task<CriticScores> OmdbScoresAsync(OnlineClient http, string key, int tconst, CancellationToken ct = default)
    {
        JsonNode? d;
        try
        {
            d = await http.GetJsonAsync($"{Omdb}?i=tt{tconst:0000000}&apikey={Uri.EscapeDataString(key)}", retries: 1, ct: ct).ConfigureAwait(false);
        }
        catch (HttpStatusException e) when (e.Status == 401) // OMDb answers 401 for a bad key and for the daily limit
        {
            if (e.Body.ToLowerInvariant().Contains("limit")) throw new LimitReachedException(e.Body);
            throw new InvalidApiKeyException("Invalid OMDb API key");
        }
        if (J.Str(d, "Response") == "False")
        {
            string err = J.Str(d, "Error") ?? "";
            if (err.Contains("limit", StringComparison.OrdinalIgnoreCase)) throw new LimitReachedException(err);
            if (err.Contains("invalid api key", StringComparison.OrdinalIgnoreCase)) throw new InvalidApiKeyException(err);
            return new CriticScores();
        }
        int? rt = null, mc = null;
        foreach (var r in J.Items(J.Arr(d, "Ratings")))
            if (J.Str(r, "Source") == "Rotten Tomatoes" && OmdbPct.Match(J.Str(r, "Value") ?? "") is { Success: true } m)
                rt = int.Parse(m.Groups[1].Value);
        string? ms = J.Str(d, "Metascore");
        if (!string.IsNullOrEmpty(ms) && ms.All(char.IsAsciiDigit)) mc = int.Parse(ms);
        return new CriticScores(rt, mc);
    }

    /// <summary>Tests an OMDb key with a known title. Throws on anything but a rejected key.</summary>
    public static async Task<(bool Ok, string Message)> OmdbCheckAsync(OnlineClient http, string key, CancellationToken ct = default)
    {
        JsonNode? d;
        try
        {
            d = await http.GetJsonAsync($"{Omdb}?i=tt0111161&apikey={Uri.EscapeDataString(key)}", retries: 0, ct: ct).ConfigureAwait(false);
        }
        catch (HttpStatusException e) when (e.Status == 401)
        {
            return (false, "Invalid API key");
        }
        if (J.Str(d, "Response") == "True") return (true, "");
        return (false, J.Str(d, "Error") ?? "Key rejected");
    }
}
