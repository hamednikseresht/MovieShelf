using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace MovieShelf.Core;

/// <summary>An HTTP answer of 400 or more (port of urllib's HTTPError).</summary>
public sealed class HttpStatusException : HttpRequestException
{
    public int Status { get; }
    public string Body { get; }

    public HttpStatusException(int status, string body = "") : base($"HTTP {status}", null, (HttpStatusCode)status)
    {
        Status = status;
        Body = body;
    }
}

/// <summary>HTTP for the online helpers. Takes any handler, so tests run offline with fake answers.</summary>
public sealed class OnlineClient
{
    public const string UA = "MovieShelf/1.0 (personal collection manager)";

    /// <summary>Multiplies every retry pause (tests set 0).</summary>
    public double DelayScale = 1;

    readonly HttpClient http;

    public OnlineClient(HttpMessageHandler? handler = null)
    {
        http = handler == null ? new HttpClient() : new HttpClient(handler, disposeHandler: false);
        http.Timeout = Timeout.InfiniteTimeSpan;
    }

    public async Task Sleep(double seconds, CancellationToken ct)
    {
        var d = TimeSpan.FromSeconds(seconds * DelayScale);
        if (d > TimeSpan.Zero) await Task.Delay(d, ct).ConfigureAwait(false);
        else ct.ThrowIfCancellationRequested();
    }

    /// <summary>One request; the status and body whatever the status is. As with a Python socket, the timeout is for each step, not the whole request:
    /// a slow download goes on as long as data keeps coming. A timeout is a <see cref="TimeoutException"/>, Stop an OperationCanceledException.</summary>
    public async Task<(int Status, byte[] Body)> SendAsync(HttpMethod method, string url, IEnumerable<KeyValuePair<string, string>>? headers,
        HttpContent? content, double timeout, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(method, url) { Content = content };
        req.Headers.TryAddWithoutValidation("User-Agent", UA);
        foreach (var h in headers ?? Array.Empty<KeyValuePair<string, string>>()) req.Headers.TryAddWithoutValidation(h.Key, h.Value);
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var step = TimeSpan.FromSeconds(timeout);
        cts.CancelAfter(step);
        try
        {
            using var resp = await http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, cts.Token).ConfigureAwait(false);
            await using var stream = await resp.Content.ReadAsStreamAsync(cts.Token).ConfigureAwait(false);
            using var body = new MemoryStream();
            var buffer = new byte[81920];
            while (true)
            {
                cts.CancelAfter(step);
                int n = await stream.ReadAsync(buffer, cts.Token).ConfigureAwait(false);
                if (n == 0) return ((int)resp.StatusCode, body.ToArray());
                body.Write(buffer, 0, n);
            }
        }
        catch (Exception e) when ((e is OperationCanceledException || (e is IOException && cts.IsCancellationRequested)) && !ct.IsCancellationRequested)
        {
            throw new TimeoutException($"Timed out after {timeout:0}s: {url}");
        }
    }

    static readonly KeyValuePair<string, string> JsonAccept = new("Accept", "application/json");

    /// <summary>GET and parse JSON. A 404 gives null; 429 and other failures are retried <paramref name="retries"/> times.</summary>
    public async Task<JsonNode?> GetJsonAsync(string url, IEnumerable<KeyValuePair<string, string>>? headers = null, double timeout = 20,
        int retries = 2, CancellationToken ct = default)
    {
        var h = new List<KeyValuePair<string, string>> { JsonAccept };
        if (headers != null) h.AddRange(headers);
        for (int attempt = 0; attempt <= retries; attempt++)
        {
            try
            {
                var (status, body) = await SendAsync(HttpMethod.Get, url, h, null, timeout, ct).ConfigureAwait(false);
                if (status == 404) return null;
                if (status >= 400) throw new HttpStatusException(status, Encoding.UTF8.GetString(body));
                return JsonNode.Parse(body);
            }
            catch (HttpStatusException e)
            {
                if (e.Status == 429 && attempt < retries)
                {
                    await Sleep(2 + attempt * 3, ct).ConfigureAwait(false);
                    continue;
                }
                if (attempt == retries) throw;
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                if (attempt == retries) throw;
            }
            await Sleep(1 + attempt, ct).ConfigureAwait(false);
        }
        return null;
    }

    public async Task<byte[]> GetBytesAsync(string url, CancellationToken ct = default, double timeout = 30)
    {
        var (status, body) = await SendAsync(HttpMethod.Get, url, null, null, timeout, ct).ConfigureAwait(false);
        if (status >= 400) throw new HttpStatusException(status);
        return body;
    }
}

/// <summary>JSON reading helpers: a missing key or a wrong type is null, never an exception (the services change their answers).</summary>
static class J
{
    public static JsonNode? Get(JsonNode? n, string key) => n is JsonObject o && o.TryGetPropertyValue(key, out var v) ? v : null;
    public static JsonArray? Arr(JsonNode? n, string key) => Get(n, key) as JsonArray;

    public static string? Str(JsonNode? n, string key)
    {
        var v = Get(n, key);
        if (v is JsonValue jv)
        {
            if (jv.TryGetValue<string>(out var s)) return s;
            if (jv.TryGetValue<long>(out var l)) return l.ToString();
        }
        return null;
    }

    public static long? Long(JsonNode? n, string key)
    {
        if (Get(n, key) is JsonValue jv)
        {
            if (jv.TryGetValue<long>(out var l)) return l;
            if (jv.TryGetValue<double>(out var d)) return (long)d;
            if (jv.TryGetValue<string>(out var s) && long.TryParse(s, out var p)) return p;
        }
        return null;
    }

    public static int? Int(JsonNode? n, string key) => Long(n, key) is { } l ? (int)l : null;

    /// <summary>Python's "x or None": empty text and 0 are none.</summary>
    public static string? Or(string? s) => string.IsNullOrEmpty(s) ? null : s;

    public static IEnumerable<JsonNode> Items(JsonArray? a) => a == null ? Enumerable.Empty<JsonNode>() : a.Where(x => x != null).Select(x => x!);
}

/// <summary>A result of the IMDb suggestion search.</summary>
public sealed record SuggestItem(int Tconst, string? Title, int? Year, string? Kind, string? Stars, string? Image);

/// <summary>IMDb public search suggestions: titles for the fallback matcher, posters.</summary>
public static class ImdbOnline
{
    public const string Suggest = "https://v3.sg.media-imdb.com/suggestion/{c}/{q}.json";

    /// <summary>Python's urllib.parse.quote: everything but letters, digits and _.-~/ is %XX.</summary>
    static string Quote(string s)
    {
        var sb = new StringBuilder();
        foreach (byte b in Encoding.UTF8.GetBytes(s))
        {
            char c = (char)b;
            if (char.IsAsciiLetterOrDigit(c) || c is '_' or '.' or '-' or '~' or '/') sb.Append(c);
            else sb.Append('%').Append(b.ToString("X2"));
        }
        return sb.ToString();
    }

    static string SuggestUrl(string c, string q) => Suggest.Replace("{c}", c).Replace("{q}", q);

    public static async Task<List<SuggestItem>> SuggestAsync(OnlineClient http, string query, CancellationToken ct = default)
    {
        string trimmed = query.Trim().ToLowerInvariant();
        if (trimmed.Length > 80) trimmed = trimmed[..80];
        string q = Quote(trimmed);
        var outList = new List<SuggestItem>();
        if (q.Length == 0) return outList;
        var d = await http.GetJsonAsync(SuggestUrl(char.IsAsciiLetterOrDigit(q[0]) ? q[0].ToString() : "x", q), ct: ct).ConfigureAwait(false);
        foreach (var x in J.Items(J.Arr(d, "d")))
        {
            string id = J.Str(x, "id") ?? "";
            if (!id.StartsWith("tt") || !int.TryParse(id.AsSpan(2), out int tc)) continue;
            outList.Add(new SuggestItem(tc, J.Str(x, "l"), J.Int(x, "y"), J.Str(x, "q"), J.Str(x, "s"), J.Str(J.Get(x, "i"), "imageUrl")));
        }
        return outList;
    }

    /// <summary>The poster image address IMDb's suggestion search holds for a title, or null.</summary>
    public static async Task<string?> PosterUrlAsync(OnlineClient http, int tconst, CancellationToken ct = default)
    {
        string tt = $"tt{tconst:0000000}";
        var d = await http.GetJsonAsync(SuggestUrl("t", tt), ct: ct).ConfigureAwait(false);
        foreach (var x in J.Items(J.Arr(d, "d")))
            if (J.Str(x, "id") == tt && J.Get(x, "i") is JsonObject { Count: > 0 } img)
                return J.Str(img, "imageUrl");
        return null;
    }

    /// <summary>m.media-amazon.com images can be resized via the URL suffix.</summary>
    public static string? SizedAmazon(string? url, int width = 400)
    {
        if (!string.IsNullOrEmpty(url) && url.Contains("._V1_")) return url.Split("._V1_")[0] + $"._V1_QL80_UX{width}_.jpg";
        return url;
    }

    /// <summary>IMDb suggestion search for one parsed name (same rules as Matcher.online_find); throws when offline.</summary>
    public static async Task<SuggestItem?> FindAsync(OnlineClient http, string? title, string? altTitle, int? year, CancellationToken ct = default)
    {
        var tries = new List<(string T, string Q)>();
        foreach (var t in new[] { title, altTitle })
        {
            if (string.IsNullOrEmpty(t)) continue;
            if (year is > 0) tries.Add((t, $"{t} {year}"));
            tries.Add((t, t));
        }
        foreach (var (t, q) in tries)
        {
            foreach (var r in await SuggestAsync(http, q, ct).ConfigureAwait(false))
            {
                if (r.Kind is "TV episode" or "video game" or "podcastSeries") continue;
                string rk = TitleKeys.Key(r.Title ?? ""), tk = TitleKeys.Key(t);
                double sim = Fuzz.Ratio(rk, tk);
                bool prefix = tk.Length >= 8 && rk.StartsWith(tk, StringComparison.Ordinal); // 'Lady and the Tramp II' -> '...: Scamp's Adventure'
                bool ok = year is > 0
                    ? r.Year != null && Math.Abs(r.Year.Value - year.Value) <= 1 && (sim >= 82 || prefix)
                    : sim >= 90;
                if (ok) return r;
            }
            await http.Sleep(0.15, ct).ConfigureAwait(false);
        }
        return null;
    }
}

public sealed record TmdbCastMember(
    [property: JsonPropertyName("name")] string? Name,
    [property: JsonPropertyName("ch")] string? Ch,
    [property: JsonPropertyName("img")] string? Img);

/// <summary>What TMDB knows about a title (Tmdb.Details). <see cref="TmdbId"/> null: TMDB does not know it.</summary>
public sealed class TmdbDetails
{
    public int? TmdbId;
    public string? Kind, Plot, Tagline, OrigLang, FaTitle, PosterUrl, BackdropUrl, Collection;
    public List<string?> Languages = new(), Countries = new();
    public List<TmdbCastMember> TmdbCast = new();
    public long? Budget, Revenue;
}

/// <summary>One movie credit of a person. Stored in persons.data, so the names are the Python app's.</summary>
public sealed record TmdbCredit(
    [property: JsonPropertyName("id")] int? Id,
    [property: JsonPropertyName("title")] string? Title,
    [property: JsonPropertyName("year")] int? Year,
    [property: JsonPropertyName("role")] string Role,
    [property: JsonPropertyName("detail")] string? Detail,
    [property: JsonPropertyName("poster")] string? Poster,
    [property: JsonPropertyName("votes")] int Votes);

/// <summary>A person from TMDB. Stored as JSON in persons.data (same keys as the Python app).</summary>
public sealed class TmdbPerson
{
    [JsonPropertyName("tmdb_id")] public int? TmdbId { get; set; }
    [JsonPropertyName("name")] public string? Name { get; set; }
    [JsonPropertyName("biography")] public string? Biography { get; set; }
    [JsonPropertyName("birthday")] public string? Birthday { get; set; }
    [JsonPropertyName("deathday")] public string? Deathday { get; set; }
    [JsonPropertyName("place")] public string? Place { get; set; }
    [JsonPropertyName("department")] public string? Department { get; set; }
    [JsonPropertyName("aka")] public List<string> Aka { get; set; } = new();
    [JsonPropertyName("homepage")] public string? Homepage { get; set; }
    [JsonPropertyName("photo_path")] public string? PhotoPath { get; set; }
    [JsonPropertyName("credits")] public List<TmdbCredit> Credits { get; set; } = new();
}

/// <summary>A part of a collection (franchise) as TMDB lists it. Stored in collections.parts.</summary>
public sealed record CollectionPart(
    [property: JsonPropertyName("title")] string? Title,
    [property: JsonPropertyName("orig")] string? Orig,
    [property: JsonPropertyName("year")] int? Year);

/// <summary>The optional TMDB API: plot, countries, languages, cast photos, people, collections.</summary>
public sealed class Tmdb
{
    public const string Api = "https://api.themoviedb.org/3";
    public const string Img = "https://image.tmdb.org/t/p/";

    readonly string? bearer, key;
    readonly OnlineClient http;

    /// <summary>A key longer than 40 characters is a v4 read access token (sent as Bearer), a shorter one a v3 api key.</summary>
    public Tmdb(string? apiKey, OnlineClient? client = null)
    {
        string k = (apiKey ?? "").Trim();
        bearer = k.Length > 40 ? k : null;
        key = bearer != null ? null : k;
        http = client ?? new OnlineClient();
    }

    public static readonly JsonSerializerOptions JsonOptions = new() { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    // ---- pacing: all threads together stay under about 50 requests a second, TMDB's limit
    static readonly object Gate = new();
    static double nextSlot;

    static async Task PaceAsync(CancellationToken ct)
    {
        double wait;
        lock (Gate)
        {
            double t = System.Diagnostics.Stopwatch.GetTimestamp() / (double)System.Diagnostics.Stopwatch.Frequency;
            wait = nextSlot - t;
            nextSlot = Math.Max(t, nextSlot) + 0.02;
        }
        if (wait > 0) await Task.Delay(TimeSpan.FromSeconds(wait), ct).ConfigureAwait(false);
    }

    /// <summary>GET a TMDB path. 404 gives null; 429 and 5xx are retried twice.</summary>
    public async Task<JsonNode?> GetAsync(string path, CancellationToken ct, params (string Key, string Value)[] args)
    {
        var ps = args.ToList();
        if (key != null) ps.Add(("api_key", key));
        string url = $"{Api}{path}?" + string.Join("&", ps.Select(p => WebUtility.UrlEncode(p.Key) + "=" + WebUtility.UrlEncode(p.Value)));
        var hdr = new List<KeyValuePair<string, string>> { new("Accept", "application/json") };
        if (bearer != null) hdr.Add(new("Authorization", $"Bearer {bearer}"));
        for (int attempt = 0; attempt < 3; attempt++)
        {
            await PaceAsync(ct).ConfigureAwait(false);
            var (status, body) = await http.SendAsync(HttpMethod.Get, url, hdr, null, 20, ct).ConfigureAwait(false);
            if (status == 404) return null;
            if (status == 429 || status >= 500)
            {
                if (attempt == 2) throw new HttpStatusException(status);
                await http.Sleep(1 + attempt * 2, ct).ConfigureAwait(false);
                continue;
            }
            if (status >= 400) throw new HttpStatusException(status);
            return JsonNode.Parse(body);
        }
        return null;
    }

    /// <summary>True when the key works. Throws on a rejected key (HTTP 401) or no network.</summary>
    public async Task<bool> CheckAsync(CancellationToken ct = default) => await GetAsync("/configuration", ct).ConfigureAwait(false) != null;

    /// <summary>Roles of a person, from TMDB's credit lists.</summary>
    public static string Role(string? job, string? dept)
    {
        string j = (job ?? "").ToLowerInvariant();
        if (j == "director") return "Director";
        if (dept == "Writing" || j is "writer" or "screenplay" or "story" or "novel" or "author") return "Writer";
        if (j.Contains("producer")) return "Producer";
        if (j.Contains("composer") || j is "music" or "songs" or "original music composer") return "Composer";
        if (j.Contains("photography") || j.Contains("cinematograph")) return "Cinematographer";
        if (j.Contains("editor")) return "Editor";
        return J.Or(job) ?? J.Or(dept) ?? "Other";
    }

    static TmdbCredit Credit(JsonNode c, string role, string? detail)
    {
        string date = J.Str(c, "release_date") ?? "";
        string y = date.Length >= 4 ? date[..4] : date;
        return new TmdbCredit(J.Int(c, "id"), J.Or(J.Str(c, "title")) ?? J.Str(c, "original_title"),
            y.Length > 0 && y.All(char.IsAsciiDigit) ? int.Parse(y) : null, role, J.Or(detail), J.Str(c, "poster_path"), J.Int(c, "vote_count") ?? 0);
    }

    /// <summary>A person by IMDb id: photo, biography, dates and every movie credit. Null when TMDB does not know the person.</summary>
    public async Task<TmdbPerson?> PersonAsync(int nconst, CancellationToken ct = default)
    {
        var found = await GetAsync($"/find/nm{nconst:0000000}", ct, ("external_source", "imdb_id")).ConfigureAwait(false);
        var res = J.Arr(found, "person_results");
        if (res == null || res.Count == 0) return null;
        int? pid = J.Int(res[0], "id");
        if (pid == null) return null;
        var d = await GetAsync($"/person/{pid}", ct, ("language", "en-US"), ("append_to_response", "combined_credits")).ConfigureAwait(false);
        if (d is not JsonObject { Count: > 0 }) return null;
        var credits = new List<TmdbCredit>();
        var cc = J.Get(d, "combined_credits");
        foreach (var c in J.Items(J.Arr(cc, "cast")))
            if (J.Str(c, "media_type") == "movie") credits.Add(Credit(c, "Actor", J.Str(c, "character")));
        foreach (var c in J.Items(J.Arr(cc, "crew")))
            if (J.Str(c, "media_type") == "movie") credits.Add(Credit(c, Role(J.Str(c, "job"), J.Str(c, "department")), J.Str(c, "job")));
        var seen = new HashSet<(int?, string)>();
        var rows = credits.Where(c => seen.Add((c.Id, c.Role))).OrderByDescending(c => c.Year ?? 0).ToList(); // one row per movie and role
        return new TmdbPerson
        {
            TmdbId = pid, Name = J.Str(d, "name"), Biography = J.Or(J.Str(d, "biography")), Birthday = J.Str(d, "birthday"),
            Deathday = J.Str(d, "deathday"), Place = J.Str(d, "place_of_birth"), Department = J.Str(d, "known_for_department"),
            Aka = J.Items(J.Arr(d, "also_known_as")).Select(x => x is JsonValue v && v.TryGetValue<string>(out var s) ? s : x.ToString()).Take(6).ToList(),
            Homepage = J.Str(d, "homepage"), PhotoPath = J.Str(d, "profile_path"), Credits = rows,
        };
    }

    /// <summary>TMDB id of the collection with this name, or null.</summary>
    public async Task<int?> SearchCollectionAsync(string name, CancellationToken ct = default)
    {
        var res = J.Items(J.Arr(await GetAsync("/search/collection", ct, ("query", name)).ConfigureAwait(false), "results")).ToList();
        var exact = res.Where(r => (J.Str(r, "name") ?? "").ToLowerInvariant() == name.ToLowerInvariant()).ToList();
        var pick = exact.FirstOrDefault() ?? res.FirstOrDefault();
        return pick == null ? null : J.Int(pick, "id");
    }

    /// <summary>The parts of a collection in release order.</summary>
    public async Task<List<CollectionPart>> CollectionAsync(int cid, CancellationToken ct = default)
    {
        var d = await GetAsync($"/collection/{cid}", ct).ConfigureAwait(false);
        var parts = J.Items(J.Arr(d, "parts")).Select(p =>
        {
            string rd = J.Str(p, "release_date") ?? "";
            return new CollectionPart(J.Str(p, "title"), J.Str(p, "original_title"),
                rd.Length >= 4 && int.TryParse(rd.AsSpan(0, 4), out int y) ? y : null);
        });
        return parts.OrderBy(p => p.Year ?? 9999).ToList();
    }

    /// <summary>Plot, countries, languages, Persian title and cast of a title by IMDb id.
    /// Null when the TMDB record cannot be read; an object with a null TmdbId when TMDB does not know the title.</summary>
    public async Task<TmdbDetails?> DetailsAsync(int tconst, string lang = "en-US", CancellationToken ct = default)
    {
        string tt = $"tt{tconst:0000000}";
        var found = await GetAsync($"/find/{tt}", ct, ("external_source", "imdb_id")).ConfigureAwait(false);
        foreach (var (kind, res) in new[] { ("movie", J.Arr(found, "movie_results")), ("tv", J.Arr(found, "tv_results")) })
        {
            if (res == null || res.Count == 0) continue;
            int? id = J.Int(res[0], "id");
            var d = await GetAsync($"/{kind}/{id}", ct, ("language", lang), ("append_to_response", "credits,translations")).ConfigureAwait(false);
            if (d is not JsonObject { Count: > 0 }) return null;
            return Shape(kind, d);
        }
        return new TmdbDetails();
    }

    public static TmdbDetails Shape(string kind, JsonNode d)
    {
        string? fa = null;
        foreach (var t in J.Items(J.Arr(J.Get(d, "translations"), "translations")))
            if (J.Str(t, "iso_639_1") == "fa")
            {
                var data = J.Get(t, "data");
                fa = J.Or(J.Str(data, "title")) ?? J.Str(data, "name");
                break;
            }
        var cast = new List<TmdbCastMember>();
        foreach (var c in J.Items(J.Arr(J.Get(d, "credits"), "cast")).Take(20))
            cast.Add(new TmdbCastMember(J.Str(c, "name"), J.Str(c, "character"),
                J.Or(J.Str(c, "profile_path")) is { } pp ? Img + "w185" + pp : null));
        var countries = J.Items(J.Arr(d, "production_countries")).Select(c => J.Str(c, "name")).ToList();
        if (countries.Count == 0) countries = J.Items(J.Arr(d, "origin_country")).Select(c => c.GetValueKind() == JsonValueKind.String ? c.GetValue<string>() : null).ToList();
        string? poster = J.Or(J.Str(d, "poster_path")), back = J.Or(J.Str(d, "backdrop_path"));
        return new TmdbDetails
        {
            TmdbId = J.Int(d, "id"), Kind = kind, Plot = J.Or(J.Str(d, "overview")), Tagline = J.Or(J.Str(d, "tagline")),
            OrigLang = J.Str(d, "original_language"),
            Languages = J.Items(J.Arr(d, "spoken_languages")).Select(l => J.Or(J.Str(l, "english_name")) ?? J.Str(l, "name")).ToList(),
            Countries = countries, FaTitle = fa,
            PosterUrl = poster != null ? Img + "w500" + poster : null, BackdropUrl = back != null ? Img + "w1280" + back : null,
            TmdbCast = cast, Collection = J.Str(J.Get(d, "belongs_to_collection"), "name"),
            Budget = J.Long(d, "budget") is > 0 and var b ? b : null, Revenue = J.Long(d, "revenue") is > 0 and var rv ? rv : null,
        };
    }
}

/// <summary>Standard Persian spelling for a title (port of fatitles.normalize; TMDB's Persian titles are written by volunteers).</summary>
public static class FaNormalizer
{
    const string ZW = "\u200c";
    const string Pers = "[\u0600-\u06ff]";
    static readonly Regex Persian = new(Pers, RegexOptions.Compiled);
    static readonly Regex Latin = new("[A-Za-z]", RegexOptions.Compiled);
    static readonly Regex Marks = new("[\u064b-\u0652\u0640]", RegexOptions.Compiled);
    static readonly Regex Thousands = new(@"(?<=\d),(?=\d)", RegexOptions.Compiled);
    static readonly Regex PunctSpace = new("\\s*([\u060c\u061b\u061f!])\\s*", RegexOptions.Compiled);
    static readonly Regex Colon = new(@"\s*:\s*", RegexOptions.Compiled);
    static readonly Regex OpenParen = new(@"\(\s+", RegexOptions.Compiled);
    static readonly Regex CloseParen = new(@"\s+\)", RegexOptions.Compiled);
    static readonly Regex Plural = new($"(?<={Pers}) (?=(?:\u0647\u0627|\u0647\u0627\u06cc|\u0647\u0627\u06cc\u06cc|\u062a\u0631\u06cc\u0646|\u062a\u0631)(?:\\s|$|[\u060c\u061b\u061f!:.)]))", RegexOptions.Compiled);
    static readonly Regex Mi = new($"(?<!{Pers})(\u0646?\u0645\u06cc) (?={Pers})", RegexOptions.Compiled);
    static readonly Regex Ye = new("(?<=\u0647) (?=\u06cc(?:\\s|$|[\u060c\u061b\u061f!:.)]))", RegexOptions.Compiled);
    static readonly Regex ZwRun = new(ZW + "{2,}", RegexOptions.Compiled);
    static readonly Regex ZwSpace = new("\\s*" + ZW + "\\s*", RegexOptions.Compiled);
    static readonly Regex Spaces = new(@"\s{2,}", RegexOptions.Compiled);
    static readonly Regex PersianChar = new(Pers, RegexOptions.Compiled);

    public static string? Normalize(string? t)
    {
        if (string.IsNullOrEmpty(t) || !Persian.IsMatch(t)) return string.IsNullOrEmpty(t) ? null : (t.Trim() is { Length: > 0 } s ? s : null);
        var sb = new StringBuilder(t.Length);
        foreach (char c in t)
            sb.Append(c switch
            {
                '\u064a' or '\u0649' => "\u06cc", '\u0643' => "\u06a9", '\u0629' or '\u06d5' => "\u0647", '\u06c0' => "\u0647" + ZW + "\u06cc", _ => c.ToString(),
            });
        t = Marks.Replace(sb.ToString(), "");
        t = Thousands.Replace(t, "\u0001");
        t = t.Replace(",", "\u060c").Replace("\u0001", ",").Replace("?", "\u061f").Replace(";", "\u061b");
        if (!Latin.IsMatch(t)) // a title that mixes in Latin words keeps its digits
        {
            sb.Clear();
            foreach (char c in t)
                sb.Append(c is >= '0' and <= '9' ? (char)(c - '0' + '\u06f0') : c is >= '\u0660' and <= '\u0669' ? (char)(c - '\u0660' + '\u06f0') : c);
            t = sb.ToString();
        }
        t = PunctSpace.Replace(t, "$1 ");
        t = Colon.Replace(t, ": ");
        t = OpenParen.Replace(t, "(");
        t = CloseParen.Replace(t, ")");
        // half-spaces: plural and comparative endings, the verb prefix mi- / nemi-, a trailing ye
        t = Plural.Replace(t, ZW);
        t = Mi.Replace(t, "$1" + ZW);
        t = Ye.Replace(t, ZW);
        t = ZwRun.Replace(t, ZW);
        string src = t;
        t = ZwSpace.Replace(src, m => m.Index > 0 && PersianChar.IsMatch(src[m.Index - 1].ToString()) ? ZW : " ");
        t = Spaces.Replace(t, " ").Trim(' ', '\u200c', ':', '-', '\u2013');
        return t.Length == 0 ? null : t;
    }
}
