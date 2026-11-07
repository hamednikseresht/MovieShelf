using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;

namespace MovieShelf.Core;

/// <summary>What the export dialog asks for, the fields the web page sends to exporter.build.</summary>
public sealed class ExportRequest
{
    public IReadOnlyList<Movie> Movies = Array.Empty<Movie>();
    public IReadOnlyList<string> Columns = Array.Empty<string>();  // keys; empty means the columns on by default
    public bool PerFile;
    public string Format = "xlsx";                                 // "xlsx" or "csv"
    public IReadOnlyList<ExportSheet>? Sheets;                     // one sheet per group; null means one "Movies" sheet
    public string Scope = "";
    public IReadOnlyList<string> Filters = Array.Empty<string>();  // the lines of the "Export info" sheet
    public DateTime? Now;
}

public sealed record ExportSheet(string Name, IReadOnlyList<Movie> Movies);

/// <summary>The "narrow down further" controls of the export dialog. Numbers are 0 when empty.</summary>
public sealed record ExportFilter(string Query = "", double YearMin = 0, double YearMax = 0, double RatingMin = 0, string Genre = "", string Res = "", string Status = "");

/// <summary>Export movies to Excel (.xlsx, written with the framework only) or CSV: one row per movie, or per file.
/// Files are streamed to disk, so a whole library never sits in memory as one string.</summary>
public static class Exporter
{
    public sealed record Column(string Key, string Label, string Group, int Width, string Kind, bool Default, Func<Movie, MovieFile?, object?> Get);

    static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    static readonly Dictionary<string, string> TypeLabel = new()
    {
        ["movie"] = "Movie", ["tvMovie"] = "TV movie", ["video"] = "Video", ["short"] = "Short", ["tvShort"] = "TV short",
        ["tvSpecial"] = "TV special", ["tvMiniSeries"] = "Mini-series", ["tvSeries"] = "TV series",
    };

    static readonly Dictionary<string, string> ConfLabel = new()
    {
        ["exact"] = "Exact match", ["good"] = "Good match", ["manual"] = "Set by you", ["online"] = "Online match (check)",
        ["noyear"] = "No year in name (check)", ["review"] = "Ambiguous (check)", ["fuzzy"] = "Fuzzy match (check)",
        ["none"] = "Not found on IMDb",
    };

    static string Names(IEnumerable<Person> l, int? limit = null) =>
        string.Join(", ", (limit == null ? l : l.Take(limit.Value)).Select(p => p.Name).Where(n => n.Length > 0));

    static string Yn(bool v) => v ? "Yes" : "No";

    static string Uniq(Movie m, Func<MovieFile, string?> g) => string.Join(", ", m.Files.Select(g).Where(x => !string.IsNullOrEmpty(x)).Distinct());

    static string Quality(MovieFile f) => string.Join(' ', new[]
    {
        f.Res, f.Src, f.Codec, f.B10 ? "10-bit" : null, f.Hdr ? "HDR" : null, f.Audio, f.Fa ? "Farsi dubbed" : null,
        f.Dual ? "Dual audio" : null, f.Sub ? "Softsub" : null, f.Ed,
    }.Where(x => !string.IsNullOrEmpty(x)));

    // A file field: that file's value per file row, the distinct values joined per movie row.
    static Func<Movie, MovieFile?, object?> FVal(Func<MovieFile, string?> g) => (m, f) => f != null ? g(f) : Uniq(m, g);

    static Func<Movie, MovieFile?, object?> FFlag(Func<MovieFile, bool> g) => (m, f) => Yn(f != null ? g(f) : m.Files.Any(g));

    static object? Minutes(int? sec) => sec is null or 0 ? null : (long)Math.Round(sec.Value / 60.0, MidpointRounding.ToEven);

    static object? Picture(MovieFile? f) => f is { W: not (null or 0), H: not (null or 0) } ? $"{f.W}x{f.H}" : null;

    static object? Langs(Movie m, MovieFile? f, Func<MovieFile, string[]> g)
    {
        var s = string.Join(", ", (f != null ? new[] { f } : m.Files.AsEnumerable()).SelectMany(g).Distinct());
        return s.Length == 0 ? null : s;
    }

    /// <summary>Python's round(x, 2): the exact binary value decides, and a true tie such as 0.125 goes to the even side.</summary>
    internal static double Round2(double x)
    {
        double eighths = x * 8;
        return eighths == Math.Floor(eighths) ? Math.Round(x * 100, MidpointRounding.ToEven) / 100 : double.Parse(x.ToString("F2", Inv), Inv);
    }

    static object? Gb(long n) => n == 0 ? null : Round2(n / 1e9);

    static string? Orr(string? a, string? b) => string.IsNullOrEmpty(a) ? b : a;

    /// <summary>Movie has no nullable votes field: matched titles report their count, and a title whose votes are NULL in the database comes out as 0, where Python leaves the cell empty.</summary>
    static object? VotesOrNull(Movie m) => m.Tconst == null ? null : m.Votes;

    // key, label, group, width, kind, on by default, getter(movie, file or null); the same table as exporter.py
    public static readonly Column[] Columns =
    {
        new("en", "Title", "Basic", 34, "text", true, (m, f) => Orr(m.En, m.Title)),
        new("year", "Year", "Basic", 8, "int", true, (m, f) => m.Year),
        new("orig", "Original title", "Basic", 30, "text", true, (m, f) => m.Orig),
        new("fa_t", "Persian title", "Basic", 26, "text", false, (m, f) => m.FaTitle),
        new("imdb_title", "IMDb title", "Basic", 30, "text", false, (m, f) => m.Title),
        new("imdb", "IMDb id (link)", "Basic", 13, "link", true, (m, f) => m.Tconst != null && m.K.StartsWith("tt", StringComparison.Ordinal) ? m.K : null),
        new("type", "Type", "Basic", 12, "text", false, (m, f) => m.Type == null ? null : TypeLabel.GetValueOrDefault(m.Type, m.Type)),
        new("runtime", "Runtime (min)", "Basic", 10, "int", false, (m, f) => m.Runtime),
        new("genres", "Genres", "Basic", 28, "text", true, (m, f) => string.Join(", ", m.Genres)),
        new("countries", "Countries", "Basic", 24, "text", false, (m, f) => string.Join(", ", m.Countries)),
        new("langs", "Languages", "Basic", 20, "text", false, (m, f) => string.Join(", ", m.Langs)),
        new("coll", "Collection", "Basic", 26, "text", false, (m, f) => m.Collection),
        new("plot", "Plot", "Basic", 70, "wrap", false, (m, f) => m.Plot),
        new("tagline", "Tagline", "Basic", 36, "text", false, (m, f) => m.Tagline),
        new("rating", "IMDb rating", "Ratings", 11, "dec", true, (m, f) => m.Rating),
        new("votes", "IMDb votes", "Ratings", 12, "int", false, (m, f) => VotesOrNull(m)),
        new("rt", "Rotten Tomatoes %", "Ratings", 12, "int", false, (m, f) => m.Rt),
        new("mc", "Metacritic", "Ratings", 11, "int", false, (m, f) => m.Mc),
        new("dir", "Directors", "People", 28, "text", true, (m, f) => Names(m.Dir)),
        new("wri", "Writers", "People", 28, "text", false, (m, f) => Names(m.Wri)),
        new("cast", "Cast (top 10)", "People", 46, "text", false, (m, f) => Names(m.Cast, 10)),
        new("res", "Resolution", "Files and quality", 11, "text", true, (m, f) => f != null ? f.Res : m.Res),
        new("quality", "Quality details", "Files and quality", 34, "wrap", false, (m, f) => f != null ? Quality(f) : string.Join("\n", m.Files.Select(Quality))),
        new("src", "Source", "Files and quality", 12, "text", false, FVal(x => x.Src)),
        new("codec", "Codec", "Files and quality", 9, "text", false, FVal(x => x.Codec)),
        new("b10", "10-bit", "Files and quality", 8, "text", false, FFlag(x => x.B10)),
        new("hdr", "HDR", "Files and quality", 7, "text", false, FFlag(x => x.Hdr)),
        new("audio", "Audio", "Files and quality", 9, "text", false, FVal(x => x.Audio)),
        new("fa", "Farsi dubbed", "Files and quality", 12, "text", false, FFlag(x => x.Fa)),
        new("dual", "Dual audio", "Files and quality", 11, "text", false, FFlag(x => x.Dual)),
        new("sub", "Softsub", "Files and quality", 9, "text", false, FFlag(x => x.Sub)),
        new("ed", "Edition", "Files and quality", 14, "text", false, FVal(x => x.Ed)),
        new("grp", "Release group", "Files and quality", 16, "text", false, FVal(x => x.Grp)),
        new("nfiles", "Files", "Files and quality", 7, "int", false, (m, f) => m.Files.Count),
        new("fname", "File name", "Files and quality", 60, "wrap", true, (m, f) => f != null ? f.Raw : string.Join("\n", m.Files.Select(x => x.Raw))),
        new("size", "Size (GB)", "Files and quality", 10, "gb", false, (m, f) => Gb(f != null ? f.Size ?? 0 : m.Files.Sum(x => x.Size ?? 0))),
        new("path", "Folder path", "Files and quality", 60, "wrap", false, (m, f) => string.Join("\n", f != null ? f.Paths : m.Files.SelectMany(x => x.Paths))),
        new("dur", "Length (min)", "Files and quality", 11, "int", false, (m, f) => Minutes(f != null ? f.Dur : m.Files.Max(x => x.Dur ?? 0))),
        new("real", "Picture size", "Files and quality", 12, "text", false, (m, f) => Picture(f ?? m.Files.FirstOrDefault(x => x.W is not (null or 0)))),
        new("audio_l", "Audio languages", "Files and quality", 22, "text", false, (m, f) => Langs(m, f, x => x.ALang)),
        new("subs_l", "Subtitles", "Files and quality", 22, "text", false, (m, f) => Langs(m, f, x => x.SLang)),
        new("listfile", "List file", "Files and quality", 22, "text", false, FVal(x => x.List)),
        new("fav", "Favorite", "Personal", 9, "text", false, (m, f) => Yn(m.Fav)),
        new("watched", "Watched", "Personal", 9, "text", false, (m, f) => Yn(m.Watched)),
        new("myrating", "My rating", "Personal", 10, "int", false, (m, f) => m.MyRating is null or 0 ? null : m.MyRating),
        new("notes", "Notes", "Personal", 40, "wrap", false, (m, f) => string.IsNullOrEmpty(m.Notes) ? null : m.Notes),
        new("conf", "Match", "Matching", 22, "text", false, (m, f) => ConfLabel.GetValueOrDefault(m.Conf, m.Conf)),
    };

    public static readonly Dictionary<string, Column> ByKey = Columns.ToDictionary(c => c.Key);

    /// <summary>The cells of the movies in their order, made as they are read.</summary>
    public static IEnumerable<object?[]> BuildRows(IEnumerable<Movie> movies, IReadOnlyList<Column> cols, bool perFile)
    {
        foreach (var m in movies)
        {
            if (perFile)
                foreach (var f in m.Files) yield return Cells(m, f, cols);
            else
                yield return Cells(m, null, cols);
        }
    }

    static object?[] Cells(Movie m, MovieFile? f, IReadOnlyList<Column> cols)
    {
        var row = new object?[cols.Count];
        for (int i = 0; i < row.Length; i++) row[i] = cols[i].Get(m, f);
        return row;
    }

    public static int RowCount(IEnumerable<Movie> movies, bool perFile) => perFile ? movies.Sum(m => m.Files.Count) : movies.Count();

    // ---------- what the dialog works out before it exports ----------

    /// <summary>The narrow-down filters over <paramref name="source"/>, keeping its order.</summary>
    public static List<Movie> Narrow(IEnumerable<Movie> source, ExportFilter f)
    {
        var q = f.Query.Length > 0 ? SearchQuery.Parse(f.Query) : null;
        Func<Movie, bool>? status = f.Status switch
        {
            "fav" => m => m.Fav, "watched" => m => m.Watched, "unwatched" => m => !m.Watched, "review" => m => m.NeedsReview, _ => null,
        };
        return source.Where(m => (q == null || q.Test(m)) && (f.YearMin == 0 || m.Year >= f.YearMin) && (f.YearMax == 0 || m.Year <= f.YearMax)
            && (f.RatingMin == 0 || m.Rating >= f.RatingMin) && (f.Genre.Length == 0 || m.Genres.Contains(f.Genre))
            && (f.Res.Length == 0 || m.Files.Any(x => x.Res == f.Res)) && (status == null || status(m))).ToList();
    }

    static string Js(double v) => v.ToString("R", Inv);

    /// <summary>The narrow-down filters as readable lines (they go on the "Export info" sheet).</summary>
    public static List<string> FilterLines(ExportFilter f)
    {
        var l = new List<string>();
        if (f.Query.Length > 0) l.Add($"search: {f.Query}");
        if (f.YearMin != 0 || f.YearMax != 0) l.Add($"year {(f.YearMin != 0 ? Js(f.YearMin) : "…")}–{(f.YearMax != 0 ? Js(f.YearMax) : "…")}");
        if (f.RatingMin != 0) l.Add($"IMDb rating ≥ {Js(f.RatingMin)}");
        if (f.Genre.Length > 0) l.Add($"genre: {f.Genre}");
        if (f.Res.Length > 0) l.Add($"quality: {f.Res}");
        string? status = f.Status switch { "fav" => "favorites", "watched" => "watched", "unwatched" => "not watched", "review" => "needs review", _ => null };
        if (status != null) l.Add(status);
        return l;
    }

    static readonly Regex QueryToken = new(@"(-?)(?:([a-z0-9]+):)?(?:""([^""]*)""|(\S+))", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>The search terms the app lists as active filters: field:value terms and left-out words, the plain words are not listed.</summary>
    public static List<string> SearchTags(string? q)
    {
        var tags = new List<string>();
        foreach (Match t in QueryToken.Matches(q ?? ""))
        {
            bool empty = (t.Groups[3].Success ? t.Groups[3].Value : t.Groups[4].Value).Length == 0;
            if (!empty && SearchQuery.Parse(t.Value).Plain.Length == 0) tags.Add(t.Value);
        }
        return tags;
    }

    static readonly string[] ResOrder = { "2160p", "1080p", "720p", "576p", "480p", "SD" };

    // each grouper gives a heading and where it sits: a number, or a text for the groups ordered by name
    static (string Label, double Num, string Text) GroupOf(Movie m, string group)
    {
        switch (group)
        {
            case "decade": return m.Decade is int d and not 0 ? (d + "s", -d, "") : ("Unknown year", 1e9, "");
            case "year": return m.Year is int y and not 0 ? (y.ToString(Inv), -y, "") : ("Unknown year", 1e9, "");
            case "genre": return m.Genres.Length > 0 ? (m.Genres[0], 0, m.Genres[0]) : ("No genre", 0, "zzzzzz");
            case "rating":
                if (m.Rating is not double r || r == 0) return ("Unrated", 99, "");
                int b = Math.Min(Math.Max((int)Math.Floor(r), 5), 9);
                return b >= 9 ? ("9.0 and up", -9, "") : b <= 5 ? ("Below 6.0", -5, "") : ($"{b}.0 – {b}.9", -b, "");
            case "res": return !string.IsNullOrEmpty(m.Res) ? (m.Res == "2160p" ? "4K (2160p)" : m.Res, Array.IndexOf(ResOrder, m.Res), "") : ("Unknown quality", 99, "");
            case "type":
                string? t = m.Type == null ? null : TypeLabel.GetValueOrDefault(m.Type);
                return (t ?? "Unmatched", 0, t ?? "zzzzzz");
            case "letter":
                string key = Movie.Fold(Orr(m.En, m.Title));
                string c = key.Length > 0 ? key[..1].ToUpperInvariant() : "#";
                return c[0] is >= 'A' and <= 'Z' ? (c, 0, c) : ("#", 0, "0");
            default: return m.Watched ? ("Watched", 1, "") : ("Not watched yet", 0, "");
        }
    }

    /// <summary>The groups of a list in display order (the web app's groupList). The English headings become the sheet names.</summary>
    public static List<ExportSheet> GroupList(IReadOnlyList<Movie> list, string group, string sort, bool reverse)
    {
        if (group is not ("decade" or "year" or "genre" or "rating" or "res" or "type" or "letter" or "seen"))
            return new List<ExportSheet> { new("Movies", list) };
        var order = new List<(string Label, double Num, string Text, List<Movie> Items)>();
        var at = new Dictionary<string, int>();
        foreach (var m in list)
        {
            var (label, num, text) = GroupOf(m, group);
            if (!at.TryGetValue(label, out int i))
            {
                at[label] = i = order.Count;
                order.Add((label, num, text, new List<Movie>()));
            }
            order[i].Items.Add(m);
        }
        var gs = order.OrderBy(e => e.Num).ThenBy(e => e.Text, StringComparer.Ordinal).ToList();
        if (reverse && ((sort == "year-desc" && group is "decade" or "year") || (sort == "title" && group == "letter"))) gs.Reverse();
        // unknown and unrated groups stay at the end
        bool Tail((string Label, double Num, string Text, List<Movie> Items) e) => e.Num == 1e9 || e.Num == 99 || e.Text == "zzzzzz";
        return gs.Where(e => !Tail(e)).Concat(gs.Where(Tail)).Select(e => new ExportSheet(e.Label, e.Items)).ToList();
    }

    // ---------- entry point ----------

    public static string FileName(string format, DateTime now) => $"MovieShelf export {now.ToString("yyyy-MM-dd", Inv)}.{(format == "csv" ? "csv" : "xlsx")}";

    /// <summary>Writes the export to <paramref name="path"/> and returns its row count. Progress reports the rows written so far.
    /// A failed or stopped export leaves no file behind.</summary>
    public static int Build(string path, ExportRequest req, IProgress<int>? progress = null, CancellationToken stop = default)
    {
        var cols = req.Columns.Where(ByKey.ContainsKey).Select(k => ByKey[k]).ToList();
        if (cols.Count == 0) cols = Columns.Where(c => c.Default).ToList();
        int done = 0;
        void Tick()
        {
            stop.ThrowIfCancellationRequested();
            if (++done % 100 == 0) progress?.Report(done);
        }

        try
        {
            if (req.Format == "csv")
            {
                WriteCsv(path, cols, BuildRows(req.Movies, cols, req.PerFile), Tick);
                return done;
            }
            var groups = req.Sheets ?? new[] { new ExportSheet("Movies", req.Movies) };
            var sheets = groups.Select(s => new SheetRows(s.Name.Length > 0 ? s.Name : "Movies", BuildRows(s.Movies, cols, req.PerFile), RowCount(s.Movies, req.PerFile))).ToList();
            int total = sheets.Sum(s => s.Count);
            var now = req.Now ?? DateTime.Now;
            var about = new List<(string, string)>
            {
                ("Exported", now.ToString("yyyy-MM-dd HH:mm", Inv)),
                ("Rows", $"{total.ToString("N0", Inv)} ({(req.PerFile ? "one per file" : "one per movie")})"),
                ("Scope", req.Scope),
                ("Filters", req.Filters.Count > 0 ? string.Join("\n", req.Filters) : "none"),
                ("Columns", string.Join(", ", cols.Select(c => c.Label))),
            };
            WriteXlsx(path, cols, sheets, about, Tick);
            return total;
        }
        catch
        {
            try { File.Delete(path); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
            throw;
        }
    }

    // ---------- csv ----------

    public static void WriteCsv(string path, IReadOnlyList<Column> cols, IEnumerable<object?[]> rows, Action? tick = null)
    {
        using var w = new StreamWriter(path, false, new UTF8Encoding(true), 1 << 16); // BOM so Excel reads Persian titles
        w.Write(Csv.Row(cols.Select(c => (object?)c.Label)));
        foreach (var r in rows)
        {
            w.Write(Csv.Row(r));
            tick?.Invoke();
        }
    }

    // ---------- xlsx ----------

    sealed record SheetRows(string Name, IEnumerable<object?[]> Rows, int Count);

    const string Decl = "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>\n";
    const string MainNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    const string RelNs = "http://schemas.openxmlformats.org/package/2006/relationships";
    const string OfficeNs = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";

    static string ColName(int i)
    {
        string s = "";
        for (i++; i > 0; i = (i - 1) / 26) s = (char)('A' + (i - 1) % 26) + s;
        return s;
    }

    /// <summary>The text XML 1.0 allows, capped at 32000 characters. Python strips the control characters and caps at the same length;
    /// lone surrogates and U+FFFE/U+FFFF also go, because Excel would ask to repair the file.</summary>
    static string Clean(string s)
    {
        StringBuilder? sb = null;
        int kept = 0;
        for (int i = 0; i < s.Length; i++)
        {
            char c = s[i];
            bool pair = char.IsHighSurrogate(c) && i + 1 < s.Length && char.IsLowSurrogate(s[i + 1]);
            bool bad = !pair && ((c < ' ' && c is not ('\t' or '\n' or '\r')) || c is '￾' or '￿' || char.IsSurrogate(c));
            if (kept == 32000 || bad)
            {
                sb ??= new StringBuilder(s, 0, i, Math.Min(s.Length, 32768));
                if (kept == 32000) break;
                continue;
            }
            sb?.Append(c);
            if (pair) sb?.Append(s[++i]);
            kept++;
        }
        return sb?.ToString() ?? s;
    }

    static string Text(object? v)
    {
        string s = Clean(v as string ?? Convert.ToString(v, Inv) ?? "");
        return s.AsSpan().IndexOfAny('&', '<', '>') < 0 ? s : s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");
    }

    // cell styles (see Styles): 0 default, 1 header, 2 wrapped text, 3 link, 4 integer, 5 one decimal, 6 text, 7 two decimals
    static int StyleOf(string kind) => kind switch { "wrap" => 2, "link" => 3, "int" => 4, "dec" => 5, "gb" => 7, _ => 6 };

    static readonly string Styles = """
        <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
        <styleSheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">
        <numFmts count="1"><numFmt numFmtId="164" formatCode="0.0"/></numFmts>
        <fonts count="3"><font><sz val="11"/><name val="Calibri"/></font><font><b/><sz val="11"/><name val="Calibri"/></font><font><u/><sz val="11"/><color rgb="FF1D4ED8"/><name val="Calibri"/></font></fonts>
        <fills count="3"><fill><patternFill patternType="none"/></fill><fill><patternFill patternType="gray125"/></fill><fill><patternFill patternType="solid"><fgColor rgb="FFF4F4F5"/><bgColor indexed="64"/></patternFill></fill></fills>
        <borders count="2"><border><left/><right/><top/><bottom/><diagonal/></border><border><left/><right/><top/><bottom style="thin"><color rgb="FFA1A1AA"/></bottom><diagonal/></border></borders>
        <cellStyleXfs count="1"><xf numFmtId="0" fontId="0" fillId="0" borderId="0"/></cellStyleXfs>
        <cellXfs count="8">
        <xf numFmtId="0" fontId="0" fillId="0" borderId="0" xfId="0"/>
        <xf numFmtId="0" fontId="1" fillId="2" borderId="1" xfId="0" applyFont="1" applyFill="1" applyBorder="1" applyAlignment="1"><alignment vertical="center"/></xf>
        <xf numFmtId="0" fontId="0" fillId="0" borderId="0" xfId="0" applyAlignment="1"><alignment vertical="top" wrapText="1"/></xf>
        <xf numFmtId="0" fontId="2" fillId="0" borderId="0" xfId="0" applyFont="1" applyAlignment="1"><alignment vertical="top"/></xf>
        <xf numFmtId="3" fontId="0" fillId="0" borderId="0" xfId="0" applyNumberFormat="1" applyAlignment="1"><alignment vertical="top" horizontal="right"/></xf>
        <xf numFmtId="164" fontId="0" fillId="0" borderId="0" xfId="0" applyNumberFormat="1" applyAlignment="1"><alignment vertical="top" horizontal="right"/></xf>
        <xf numFmtId="0" fontId="0" fillId="0" borderId="0" xfId="0" applyAlignment="1"><alignment vertical="top"/></xf>
        <xf numFmtId="2" fontId="0" fillId="0" borderId="0" xfId="0" applyNumberFormat="1" applyAlignment="1"><alignment vertical="top" horizontal="right"/></xf>
        </cellXfs>
        <cellStyles count="1"><cellStyle name="Normal" xfId="0" builtinId="0"/></cellStyles>
        </styleSheet>
        """.ReplaceLineEndings("\n");

    static void WriteCell(TextWriter w, string reference, string kind, object v)
    {
        switch (kind)
        {
            case "int":
                w.Write($"<c r=\"{reference}\" s=\"4\"><v>{Convert.ToInt64(v, Inv).ToString(Inv)}</v></c>");
                return;
            case "dec" or "gb":
                w.Write($"<c r=\"{reference}\" s=\"{StyleOf(kind)}\"><v>{Csv.PyFloat(Convert.ToDouble(v, Inv))}</v></c>");
                return;
            case "link":
                string t = Text(v);
                w.Write($"<c r=\"{reference}\" t=\"str\" s=\"3\"><f>HYPERLINK(\"https://www.imdb.com/title/{t}/\",\"{t}\")</f><v>{t}</v></c>");
                return;
        }
        w.Write($"<c r=\"{reference}\" t=\"inlineStr\" s=\"{StyleOf(kind)}\"><is><t xml:space=\"preserve\">");
        w.Write(Text(v));
        w.Write("</t></is></c>");
    }

    static void WriteSheet(TextWriter w, IReadOnlyList<string> headers, IEnumerable<object?[]> rows, int count, IReadOnlyList<string> kinds, IReadOnlyList<int> widths, Action? tick)
    {
        int n = headers.Count;
        var names = Enumerable.Range(0, n).Select(ColName).ToArray();
        string last = $"{ColName(Math.Max(n, 1) - 1)}{count + 1}";
        w.Write($"{Decl}<worksheet xmlns=\"{MainNs}\"><dimension ref=\"A1:{last}\"/>");
        w.Write("<sheetViews><sheetView workbookViewId=\"0\"><pane ySplit=\"1\" topLeftCell=\"A2\" activePane=\"bottomLeft\" state=\"frozen\"/>" +
                "<selection pane=\"bottomLeft\"/></sheetView></sheetViews><sheetFormatPr defaultRowHeight=\"15\"/>");
        if (n > 0)
        {
            w.Write("<cols>");
            for (int i = 0; i < n; i++) w.Write($"<col min=\"{i + 1}\" max=\"{i + 1}\" width=\"{widths[i]}\" customWidth=\"1\"/>");
            w.Write("</cols>");
        }
        w.Write("<sheetData><row r=\"1\" ht=\"22\" customHeight=\"1\">");
        for (int i = 0; i < n; i++) w.Write($"<c r=\"{names[i]}1\" t=\"inlineStr\" s=\"1\"><is><t>{Text(headers[i])}</t></is></c>");
        w.Write("</row>");
        int r = 1;
        foreach (var row in rows)
        {
            r++;
            w.Write($"<row r=\"{r}\">");
            for (int i = 0; i < row.Length; i++)
            {
                var v = row[i];
                if (v is null || (v is string s && s.Length == 0)) continue;
                WriteCell(w, $"{names[i]}{r}", kinds[i], v);
            }
            w.Write("</row>");
            tick?.Invoke();
        }
        w.Write("</sheetData>");
        if (n > 0 && count > 0) w.Write($"<autoFilter ref=\"A1:{last}\"/>");
        w.Write("</worksheet>");
    }

    /// <summary>Names Excel accepts: no forbidden characters, 31 at most, each one different.</summary>
    public static List<string> SheetNames(IEnumerable<string> names)
    {
        var used = new HashSet<string>();
        var out_ = new List<string>();
        int i = 0;
        foreach (var n in names)
        {
            i++;
            string b = Regex.Replace(n, @"[\[\]:*?/\\]", "-").Trim().Trim('\'');
            if (b.Length > 31) b = b[..(char.IsHighSurrogate(b[30]) ? 30 : 31)];
            if (b.Length == 0) b = $"Sheet{i}";
            string name = b;
            for (int k = 2; used.Contains(name.ToLowerInvariant()); k++)
            {
                string suffix = $" ({k})";
                name = b[..Math.Min(b.Length, 31 - suffix.Length)] + suffix;
            }
            used.Add(name.ToLowerInvariant());
            out_.Add(name);
        }
        return out_;
    }

    static void Part(ZipArchive zip, string name, Action<TextWriter> write)
    {
        var e = zip.CreateEntry(name, CompressionLevel.Optimal);
        using var w = new StreamWriter(e.Open(), new UTF8Encoding(false), 1 << 16);
        write(w);
    }

    /// <summary>One "Movies" sheet of the given rows, without the info sheet.</summary>
    public static void WriteXlsx(string path, IReadOnlyList<Column> cols, IEnumerable<object?[]> rows)
    {
        var list = rows as IReadOnlyList<object?[]> ?? rows.ToList();
        WriteXlsx(path, cols, new[] { new SheetRows("Movies", list, list.Count) }, null, null);
    }

    static void WriteXlsx(string path, IReadOnlyList<Column> cols, IReadOnlyList<SheetRows> sheets, IReadOnlyList<(string What, string Value)>? about, Action? tick)
    {
        var names = SheetNames(sheets.Select(s => s.Name).Concat(about != null ? new[] { "Export info" } : Array.Empty<string>()));
        int count = names.Count;
        var headers = cols.Select(c => c.Label).ToList();
        var kinds = cols.Select(c => c.Kind).ToList();
        var widths = cols.Select(c => c.Width).ToList();
        using var file = new FileStream(path, FileMode.Create, FileAccess.ReadWrite, FileShare.None, 1 << 16);
        using var zip = new ZipArchive(file, ZipArchiveMode.Create);
        Part(zip, "[Content_Types].xml", w =>
        {
            w.Write(Decl + "<Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\">" +
                    "<Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/>" +
                    "<Default Extension=\"xml\" ContentType=\"application/xml\"/>" +
                    "<Override PartName=\"/xl/workbook.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml\"/>" +
                    "<Override PartName=\"/xl/styles.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml\"/>");
            for (int i = 1; i <= count; i++)
                w.Write($"<Override PartName=\"/xl/worksheets/sheet{i}.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml\"/>");
            w.Write("</Types>");
        });
        Part(zip, "_rels/.rels", w => w.Write($"{Decl}<Relationships xmlns=\"{RelNs}\"><Relationship Id=\"rId1\" Type=\"{OfficeNs}/officeDocument\" Target=\"xl/workbook.xml\"/></Relationships>"));
        Part(zip, "xl/workbook.xml", w =>
        {
            w.Write($"{Decl}<workbook xmlns=\"{MainNs}\" xmlns:r=\"{OfficeNs}\"><sheets>");
            for (int i = 0; i < count; i++)
                w.Write($"<sheet name=\"{Text(names[i]).Replace("\"", "&quot;")}\" sheetId=\"{i + 1}\" r:id=\"rId{i + 1}\"/>");
            w.Write("</sheets></workbook>");
        });
        Part(zip, "xl/_rels/workbook.xml.rels", w =>
        {
            w.Write($"{Decl}<Relationships xmlns=\"{RelNs}\">");
            for (int i = 1; i <= count; i++)
                w.Write($"<Relationship Id=\"rId{i}\" Type=\"{OfficeNs}/worksheet\" Target=\"worksheets/sheet{i}.xml\"/>");
            w.Write($"<Relationship Id=\"rId{count + 1}\" Type=\"{OfficeNs}/styles\" Target=\"styles.xml\"/></Relationships>");
        });
        Part(zip, "xl/styles.xml", w => w.Write(Styles));
        for (int i = 0; i < sheets.Count; i++)
        {
            var s = sheets[i];
            Part(zip, $"xl/worksheets/sheet{i + 1}.xml", w => WriteSheet(w, headers, s.Rows, s.Count, kinds, widths, tick));
        }
        if (about != null)
            Part(zip, $"xl/worksheets/sheet{count}.xml", w => WriteSheet(w, new[] { "What", "Value" }, about.Select(a => new object?[] { a.What, a.Value }), about.Count, new[] { "text", "wrap" }, new[] { 22, 90 }, null));
    }
}
