using System.Globalization;
using System.Text.RegularExpressions;

namespace MovieShelf.Core;

/// <summary>The search language of the box above the results:
/// free words must all match, "exact phrase", -word leaves it out, field:value narrows;
/// numbers: 8 (at least, for scores), &gt;8, &lt;=1980, 1990-1999, 5k.</summary>
public sealed class SearchQuery
{
    public Func<Movie, bool> Test { get; private set; } = _ => true;
    /// <summary>The plain words, used to float title matches to the top.</summary>
    public string Plain { get; private set; } = "";

    static readonly Regex Token = new(@"(-?)(?:([a-z0-9]+):)?(?:""([^""]*)""|(\S+))", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    static readonly Regex NumRx = new(@"^(\d+(?:\.\d+)?)([km])?$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    static readonly Regex CmpRx = new(@"^(>=|<=|>|<|=)(.+)$", RegexOptions.Compiled);
    static readonly Regex RangeRx = new(@"^([\d.]+[km]?)-([\d.]+[km]?)$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    static readonly Regex TtRx = new(@"^tt\d+$", RegexOptions.Compiled);

    static double? NumVal(string s)
    {
        var m = NumRx.Match(s);
        if (!m.Success) return null;
        double x = double.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
        return m.Groups[2].Value.ToLowerInvariant() switch { "k" => x * 1e3, "m" => x * 1e6, _ => x };
    }

    static Func<double, bool>? NumSpec(string v, bool bareAtLeast)
    {
        v = v.Replace(",", "");
        var m = CmpRx.Match(v);
        if (m.Success)
        {
            var x = NumVal(m.Groups[2].Value);
            if (x == null) return null;
            double xv = x.Value;
            return m.Groups[1].Value switch
            {
                ">" => a => a > xv, ">=" => a => a >= xv, "<" => a => a < xv, "<=" => a => a <= xv, _ => a => a == xv,
            };
        }
        m = RangeRx.Match(v);
        if (m.Success)
        {
            var lo = NumVal(m.Groups[1].Value);
            var hi = NumVal(m.Groups[2].Value);
            return lo == null || hi == null ? null : a => a >= lo && a <= hi;
        }
        var n = NumVal(v);
        if (n == null) return null;
        double nv = n.Value;
        return bareAtLeast ? a => a >= nv : a => a == nv;
    }

    static readonly Dictionary<string, (Func<Movie, double?> Get, bool Bare)> NumFields = new()
    {
        ["year"] = (m => m.Year, false), ["runtime"] = (m => m.Runtime, false), ["files"] = (m => m.Files.Count, false),
        ["rating"] = (m => m.Rating, true), ["imdb"] = (m => m.Rating, true), ["votes"] = (m => m.Votes, true),
        ["rt"] = (m => m.Rt, true), ["mc"] = (m => m.Mc, true), ["myrating"] = (m => m.MyRating, true),
    };

    static string F(IEnumerable<string?> xs) => Movie.Fold(string.Join(" | ", xs.Where(x => x != null)));

    static readonly Dictionary<string, Func<Movie, string>> TxtFields = new()
    {
        ["genre"] = m => F(m.Genres),
        ["dir"] = m => m.FDir, ["director"] = m => m.FDir, ["writer"] = m => m.FWri,
        ["actor"] = m => m.FCast, ["cast"] = m => m.FCast, ["star"] = m => m.FCast,
        ["person"] = m => m.FDir + " | " + m.FWri + " | " + m.FCast,
        ["country"] = m => F(m.Countries), ["lang"] = m => F(m.Langs), ["language"] = m => F(m.Langs),
        ["type"] = m => Movie.Fold((m.Type ?? "unmatched") + " " + (m.Type ?? "")),
        ["res"] = m => F(m.Files.Select(f => f.Res)), ["quality"] = m => F(m.Files.Select(f => f.Res)),
        ["src"] = m => F(m.Files.Select(f => f.Src)), ["source"] = m => F(m.Files.Select(f => f.Src)),
        ["codec"] = m => F(m.Files.Select(f => f.Codec)),
        ["audio"] = m => m.FAudio, ["sub"] = m => m.FSubs, ["subs"] = m => m.FSubs, ["subtitle"] = m => m.FSubs,
        ["title"] = m => m.FTitle, ["file"] = m => m.FFile, ["path"] = m => m.FFile,
        ["collection"] = m => Movie.Fold(m.Collection), ["id"] = m => Movie.Fold(m.Tconst == null ? "" : m.K),
    };

    static readonly Dictionary<string, Func<Movie, bool>> IsFlags = new()
    {
        ["fav"] = m => m.Fav, ["favorite"] = m => m.Fav, ["watched"] = m => m.Watched, ["seen"] = m => m.Watched, ["unwatched"] = m => !m.Watched,
        ["review"] = m => m.NeedsReview, ["multiple"] = m => m.Files.Count > 1, ["dupes"] = m => m.Files.Count > 1, ["noposter"] = m => !m.HasPoster,
        ["top"] = m => m.Rating >= 8, ["dubbed"] = m => m.Files.Any(f => f.Fa), ["dual"] = m => m.Files.Any(f => f.Dual),
        ["hdr"] = m => m.Files.Any(f => f.Hdr), ["4k"] = m => m.Res == "2160p",
    };

    static readonly Dictionary<string, Func<Movie, bool>> HasFlags = new()
    {
        ["poster"] = m => m.HasPoster, ["plot"] = m => !string.IsNullOrEmpty(m.Plot), ["rt"] = m => m.Rt != null, ["mc"] = m => m.Mc != null,
        ["cast"] = m => m.Cast.Count > 0, ["rating"] = m => m.Rating != null, ["notes"] = m => !string.IsNullOrEmpty(m.Notes),
    };

    public static SearchQuery Parse(string? q)
    {
        var preds = new List<Func<Movie, bool>>();
        var plain = new List<string>();
        foreach (Match mt in Token.Matches(q ?? ""))
        {
            bool neg = mt.Groups[1].Value == "-";
            string key = mt.Groups[2].Value.ToLowerInvariant();
            string val = mt.Groups[3].Success ? mt.Groups[3].Value : mt.Groups[4].Value;
            Func<Movie, bool>? fn = null;
            if (key.Length > 0 && val.Length > 0)
            {
                if (NumFields.TryGetValue(key, out var nf))
                {
                    var t = NumSpec(val, nf.Bare);
                    if (t != null) fn = m => nf.Get(m) is { } v && t(v);
                }
                else if (TxtFields.TryGetValue(key, out var get))
                {
                    string v = Movie.Fold(Regex.IsMatch(key, "^(res|quality)$") && val.Equals("4k", StringComparison.OrdinalIgnoreCase) ? "2160p" : val);
                    fn = m => get(m).Contains(v, StringComparison.Ordinal);
                }
                else if (key == "is" && IsFlags.TryGetValue(val.ToLowerInvariant(), out var isf)) fn = isf;
                else if (key == "has" && HasFlags.TryGetValue(val.ToLowerInvariant(), out var hasf)) fn = hasf;
            }
            if (fn != null)
            {
                var f = fn;
                preds.Add(neg ? m => !f(m) : f);
                continue;
            }
            // an unknown "key:" is plain text ("mission:impossible" finds Mission: Impossible)
            bool phrase = mt.Groups[3].Success && key.Length == 0;
            var words = phrase
                ? new[] { Movie.Fold(val).Trim() }.Where(w => w.Length > 0).ToArray()
                : Movie.Fold(key.Length > 0 ? key + " " + val : val).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (words.Length == 0) continue;
            Func<Movie, bool> wf = m => words.All(w => TtRx.IsMatch(w) ? m.K == "tt" + w[2..].PadLeft(7, '0') : m.Hay.Contains(w, StringComparison.Ordinal));
            preds.Add(neg ? m => !wf(m) : wf);
            if (!neg) plain.Add(string.Join(' ', words));
        }
        var tests = preds.ToArray();
        return new SearchQuery
        {
            Test = tests.Length == 0 ? _ => true : m =>
            {
                foreach (var t in tests) if (!t(m)) return false;
                return true;
            },
            Plain = string.Join(' ', plain),
        };
    }
}
