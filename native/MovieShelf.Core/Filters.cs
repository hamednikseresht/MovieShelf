using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace MovieShelf.Core;

/// <summary>A saved view read back: the filters and what the results looked like. Sort, Group and Kind are null when the view does not say.</summary>
public sealed record ViewState(LibraryFilter Filter, string? Sort, bool Reverse, string? Group, string? Kind);

/// <summary>The search box and the filter panel of the main window. Its JSON is the Python app's F object (app.js), and a saved view is
/// {F, sort, rev, group}, so a view saved in either app opens in the other.</summary>
public sealed class LibraryFilter
{
    /// <summary>The filters that hold a set of choices, in the order the app lists them.</summary>
    public static readonly string[] SetKeys = { "status", "genre", "decade", "runtime", "res", "src", "tech", "type" };

    public string Q = "";
    public readonly HashSet<string> Status = new(), Genre = new(), Decade = new(), Runtime = new(), Res = new(), Src = new(), Tech = new(), Type = new();
    public int? YMin, YMax;
    public double RMin, VMin, RtMin, McMin;
    public string Country = "", Lang = "";
    public (int Id, string Name)? Person;

    public HashSet<string> Set(string key) => key switch
    {
        "status" => Status, "genre" => Genre, "decade" => Decade, "runtime" => Runtime, "res" => Res, "src" => Src, "tech" => Tech, "type" => Type,
        _ => throw new ArgumentOutOfRangeException(nameof(key)),
    };

    public void Clear()
    {
        foreach (var k in SetKeys) Set(k).Clear();
        Q = Country = Lang = "";
        YMin = YMax = null;
        RMin = VMin = RtMin = McMin = 0;
        Person = null;
    }

    // ---------- JSON ----------
    /// <summary>The F object. Without <paramref name="withQuery"/> it is what the app keeps across restarts: the search text starts empty.</summary>
    public JsonObject ToJson(bool withQuery = true)
    {
        var o = new JsonObject();
        if (withQuery) o["q"] = Q;
        o["status"] = Strings(Status);
        o["genre"] = Strings(Genre);
        o["decade"] = Strings(Decade);
        o["yMin"] = YMin;
        o["yMax"] = YMax;
        o["rMin"] = RMin;
        o["vMin"] = VMin;
        o["rtMin"] = RtMin;
        o["mcMin"] = McMin;
        o["runtime"] = Strings(Runtime);
        o["res"] = Strings(Res);
        o["src"] = Strings(Src);
        o["tech"] = Strings(Tech);
        o["type"] = Strings(Type);
        o["country"] = Country;
        o["lang"] = Lang;
        o["person"] = Person is { } p ? new JsonObject { ["id"] = p.Id, ["name"] = p.Name } : null;
        return o;
    }

    static JsonArray Strings(IEnumerable<string> list) => new(list.Select(x => (JsonNode?)JsonValue.Create(x)).ToArray());

    /// <summary>Takes the keys <paramref name="o"/> has and leaves the others, like applyFilterObject in app.js.</summary>
    public void Merge(JsonNode? o)
    {
        if (o is not JsonObject j) return;
        foreach (var k in SetKeys)
            if (j[k] is JsonArray a)
            {
                var set = Set(k);
                set.Clear();
                foreach (var x in a) if (Text(x) is { Length: > 0 } s) set.Add(s);
            }
        if (j.ContainsKey("q")) Q = Text(j["q"]) ?? "";
        if (j.ContainsKey("yMin")) YMin = Year(j["yMin"]);
        if (j.ContainsKey("yMax")) YMax = Year(j["yMax"]);
        if (j.ContainsKey("rMin")) RMin = Num(j["rMin"]);
        if (j.ContainsKey("vMin")) VMin = Num(j["vMin"]);
        if (j.ContainsKey("rtMin")) RtMin = Num(j["rtMin"]);
        if (j.ContainsKey("mcMin")) McMin = Num(j["mcMin"]);
        if (j.ContainsKey("country")) Country = Text(j["country"]) ?? "";
        if (j.ContainsKey("lang")) Lang = Text(j["lang"]) ?? "";
        if (j.ContainsKey("person")) Person = PersonOf(j["person"]);
    }

    /// <summary>The state to store for a saved view.</summary>
    public JsonObject ToView(string sort, bool reverse, string group) =>
        new() { ["F"] = ToJson(), ["sort"] = sort, ["rev"] = reverse, ["group"] = group };

    /// <summary>Reads a saved view of the Python app, or of an older native build, which kept its filters under "native".</summary>
    public static ViewState ReadView(JsonNode? state)
    {
        var f = new LibraryFilter();
        string? kind = null;
        if (state?["F"] is JsonObject py) f.Merge(py);
        else if (state?["native"] is JsonObject old)
        {
            kind = Text(old["kind"]);
            f.Merge(new JsonObject
            {
                ["q"] = Text(old["search"]) ?? "", ["yMin"] = old["yearMin"]?.DeepClone(), ["yMax"] = old["yearMax"]?.DeepClone(),
                ["rMin"] = old["rating"]?.DeepClone(), ["vMin"] = old["votes"]?.DeepClone(), ["rtMin"] = old["rt"]?.DeepClone(), ["mcMin"] = old["mc"]?.DeepClone(),
                ["country"] = Text(old["country"]) ?? "", ["lang"] = Text(old["language"]) ?? "", ["person"] = old["person"]?.DeepClone(),
            });
            foreach (var k in SetKeys) if (old[k] is JsonArray a) f.Merge(new JsonObject { [k] = a.DeepClone() });
        }
        string? sort = Text(state?["sort"]);
        bool rev = Truthy(state?["rev"]);
        if (sort == "year")   // oldest first, which the sort list now gives as Year reversed
        {
            sort = "year-desc";
            rev = !rev;
        }
        return new ViewState(f, sort, rev, Text(state?["group"]), kind);
    }

    static string? Text(JsonNode? n) => n is not JsonValue v ? null
        : v.TryGetValue<string>(out var s) ? s
        : v.GetValueKind() == JsonValueKind.Number ? v.ToJsonString() : null;

    static double Num(JsonNode? n) => double.TryParse(Text(n), NumberStyles.Float, CultureInfo.InvariantCulture, out var d) && !double.IsNaN(d) ? d : 0;

    static int? Year(JsonNode? n) => (int)Num(n) is > 0 and var y ? y : null;

    static bool Truthy(JsonNode? n) => n is JsonValue v && (v.TryGetValue<bool>(out var b) ? b : Num(n) != 0);

    static (int, string)? PersonOf(JsonNode? n) =>
        n is JsonObject o && (int)Num(o["id"]) is not 0 and var id ? (id, Text(o["name"]) ?? "") : null;

    // ---------- filtering ----------
    public static readonly IReadOnlyDictionary<string, Func<MovieFile, bool>> TechTests = new Dictionary<string, Func<MovieFile, bool>>
    {
        ["fa"] = f => f.Fa, ["dual"] = f => f.Dual, ["sub"] = f => f.Sub, ["x265"] = f => f.Codec == "x265", ["b10"] = f => f.B10, ["hdr"] = f => f.Hdr,
        ["51"] = f => f.Audio is "5.1" or "7.1", ["ed"] = f => !string.IsNullOrEmpty(f.Ed),
    };

    public static bool RuntimeIs(int? minutes, string key)
    {
        if (minutes is not int r || r == 0) return false;
        return key switch { "short" => r < 60, "90" => r is >= 60 and < 90, "120" => r is >= 90 and < 120, "150" => r is >= 120 and <= 150, _ => r > 150 };
    }

    /// <summary>The filters as they stand now, worked out once per pass over the library. The search text is not part of it (see <see cref="SearchQuery"/>).</summary>
    public Pass Compile() => new(this);

    public sealed class Pass
    {
        readonly bool fav, watched, unwatched, review, dupes, noPoster, top;
        readonly string[] genres, runtimes;
        readonly HashSet<int>? decades;
        readonly HashSet<string>? res, src, types;
        readonly Func<MovieFile, bool>[] tech;
        readonly int yMin, yMax, personId;
        readonly string personText;
        readonly double rMin, votes, rtMin, mcMin;
        readonly string country, lang;

        internal Pass(LibraryFilter f)
        {
            fav = f.Status.Contains("fav");
            watched = f.Status.Contains("watched");
            unwatched = f.Status.Contains("unwatched");
            review = f.Status.Contains("review");
            dupes = f.Status.Contains("dupes");
            noPoster = f.Status.Contains("noposter");
            top = f.Status.Contains("top");
            genres = f.Genre.ToArray();
            runtimes = f.Runtime.ToArray();
            decades = f.Decade.Count == 0 ? null : f.Decade.Select(d => int.TryParse(d, out var y) ? y : int.MinValue).ToHashSet();
            res = f.Res.Count == 0 ? null : f.Res;
            src = f.Src.Count == 0 ? null : f.Src;
            types = f.Type.Count == 0 ? null : f.Type;
            tech = f.Tech.Where(TechTests.ContainsKey).Select(t => TechTests[t]).ToArray();
            yMin = f.YMin ?? 0;
            yMax = f.YMax ?? 0;
            rMin = f.RMin;
            votes = f.VMin != 0 ? Math.Pow(10, f.VMin) : 0;
            rtMin = f.RtMin;
            mcMin = f.McMin;
            country = f.Country;
            lang = f.Lang;
            personId = f.Person?.Id ?? 0;
            personText = personId.ToString(CultureInfo.InvariantCulture);
        }

        static bool Any(List<MovieFile> files, HashSet<string> set, bool source)
        {
            foreach (var f in files)
                if ((source ? f.Src : f.Res) is { } v && set.Contains(v)) return true;
            return false;
        }

        static bool Any(List<MovieFile> files, Func<MovieFile, bool> test)
        {
            foreach (var f in files) if (test(f)) return true;
            return false;
        }

        /// <summary>The same tests, in the same order, as apply() in app.js.</summary>
        public bool Test(Movie m)
        {
            if (personId != 0 && !m.HasPerson(personId, personText)) return false;
            if (fav && !m.Fav || watched && !m.Watched || unwatched && m.Watched || review && !m.NeedsReview) return false;
            if (dupes && m.Files.Count < 2 || noPoster && m.HasPoster || top && !(m.Rating >= 8)) return false;
            foreach (var g in genres) if (Array.IndexOf(m.Genres, g) < 0) return false;
            if (decades != null && !(m.Decade is int d && decades.Contains(d))) return false;
            if (yMin != 0 && !(m.Year >= yMin)) return false;
            if (yMax != 0 && !(m.Year <= yMax)) return false;
            if (rMin != 0 && !(m.Rating >= rMin)) return false;
            if (votes != 0 && !(m.Votes >= votes)) return false;
            if (rtMin != 0 && !(m.Rt >= rtMin)) return false;
            if (mcMin != 0 && !(m.Mc >= mcMin)) return false;
            if (runtimes.Length > 0)
            {
                bool ok = false;
                foreach (var r in runtimes) ok |= RuntimeIs(m.Runtime, r);
                if (!ok) return false;
            }
            if (res != null && !Any(m.Files, res, false)) return false;
            if (src != null && !Any(m.Files, src, true)) return false;
            foreach (var t in tech) if (!Any(m.Files, t)) return false;
            if (types != null && !types.Contains(m.Type ?? "unknown")) return false;
            if (country.Length > 0 && Array.IndexOf(m.Countries, country) < 0) return false;
            if (lang.Length > 0 && Array.IndexOf(m.Langs, lang) < 0) return false;
            return true;
        }
    }
}
