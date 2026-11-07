using System.Globalization;
using System.Text;

namespace MovieShelf.Core;

/// <summary>CSV the way Python's csv module writes it for the web UI: minimal quoting, CRLF line ends. Files get a UTF-8 BOM so Excel shows Persian / accented titles.</summary>
public static class Csv
{
    static string Cell(object? v)
    {
        string s = v switch
        {
            null => "",
            string t => t,
            bool b => b ? "True" : "False",
            double d => PyFloat(d),
            float f => PyFloat(f),
            IFormattable i => i.ToString(null, CultureInfo.InvariantCulture),
            _ => v.ToString() ?? "",
        };
        return s.IndexOfAny(new[] { ',', '"', '\r', '\n' }) >= 0 ? "\"" + s.Replace("\"", "\"\"") + "\"" : s;
    }

    /// <summary>Python's repr of a float: 8.0 stays "8.0".</summary>
    internal static string PyFloat(double d)
    {
        string s = d.ToString("R", CultureInfo.InvariantCulture);
        return s.IndexOfAny(new[] { '.', 'E', 'N', 'I' }) < 0 ? s + ".0" : s;
    }

    public static string Row(IEnumerable<object?> fields)
    {
        var cells = fields.Select(Cell).ToList();
        // the csv module quotes a lone empty field, or the line would read as no row at all
        return cells.Count == 1 && cells[0].Length == 0 ? "\"\"\r\n" : string.Join(",", cells) + "\r\n";
    }

    public static void Save(string path, string text) => File.WriteAllText(path, text, new UTF8Encoding(true));

    static string Tt(int tconst) => $"tt{tconst:0000000}";

    /// <summary>The "Other candidates" etc. of the match-problems list (Python problems_csv).</summary>
    public static string Problems(IEnumerable<MatchProblem> problems)
    {
        var sb = new StringBuilder(Row(new object?[] { "Name", "Problem", "Current IMDb", "Current title", "Year", "Other candidates" }));
        foreach (var p in problems)
        {
            var c = p.Current;
            sb.Append(Row(new object?[]
            {
                p.Raw, p.Reason, c != null ? Tt(c.Tconst) : "", c?.Title, c?.Year,
                string.Join(" | ", p.Cands.Select(x => $"{Tt(x.Tconst)} {x.Title} ({(x.Year?.ToString(CultureInfo.InvariantCulture) ?? "None")})")),
            }));
        }
        return sb.ToString();
    }

    /// <summary>One problem kind of the Problems window (Python issues_csv). <paramref name="sub"/> "all" or a sub-kind of the rows ("close", "same", ...).</summary>
    public static string Issues(IssueReport report, string kind, string sub = "all")
    {
        var rows = report.Kinds.GetValueOrDefault(kind) ?? new List<IssueRow>();
        if (sub != "all") rows = rows.Where(r => r.Sub == sub).ToList();
        var sb = new StringBuilder(Row(new object?[] { "Title", "Year", "IMDb", "Rating", "Votes", "Problem", "Files", "Ignored" }));
        foreach (var r in rows)
            sb.Append(Row(new object?[]
            {
                r.Title, r.Year, r.Matched ? r.K : "", r.Rating, r.Votes, r.Note,
                string.Join(" | ", r.Files.Select(f => $"{f.Raw} [{f.Quality}{(string.IsNullOrEmpty(f.Verdict) ? "" : ", " + f.Verdict)}]")),
                r.Ignored ? "yes" : "",
            }));
        return sb.ToString();
    }

    /// <summary>The file name the Python app offers: problems-dupes.csv ...</summary>
    public static string IssuesFileName(string kind) => $"problems-{kind}.csv";

    /// <summary>Title,Year,Status,Source,Note,Added; every field quoted, all entries (what the web UI's "Save as CSV" writes, including its blank last line when empty).</summary>
    public static string Wishlist(IEnumerable<WishItem> items)
    {
        static string Q(object? v) => "\"" + Convert.ToString(v, CultureInfo.InvariantCulture)?.Replace("\"", "\"\"") + "\"";
        return "Title,Year,Status,Source,Note,Added\r\n" + string.Join("\r\n", items.Select(x => string.Join(",", new object?[] { x.Title, x.Year, x.Status, x.Source, x.Note, x.Added }.Select(Q)))) + "\r\n";
    }

    /// <summary>"Title (Year)" per line, as "Copy wanted" / "Save as text" write (without the final newline).</summary>
    public static string WishlistText(IEnumerable<WishItem> items) =>
        string.Join("\n", items.Select(w => w.Title + (w.Year is > 0 ? $" ({w.Year})" : "")));
}

public sealed partial class Library
{
    /// <summary>problems.csv body (no BOM). Quick without <paramref name="fuzzy"/> (misspelling search takes seconds on a big library).</summary>
    public string ProblemsCsv(bool fuzzy = true) => Csv.Problems(MatchProblems(fuzzy));

    /// <summary>problems-&lt;kind&gt;.csv body (no BOM) from a fresh <see cref="Issues.Build"/>; pass a report you already hold to skip rebuilding.</summary>
    public string IssuesCsv(string kind, string sub = "all", IssueReport? report = null) => Csv.Issues(report ?? Issues.Build(this), kind, sub);

    public void WriteProblemsCsv(string path, bool fuzzy = true) => Csv.Save(path, ProblemsCsv(fuzzy));

    public void WriteIssuesCsv(string path, string kind, string sub = "all", IssueReport? report = null) => Csv.Save(path, IssuesCsv(kind, sub, report));

    /// <summary>Saves the wishlist as "MovieShelf wishlist.txt" content: the wanted entries, one "Title (Year)" per line, UTF-8 with BOM.</summary>
    public void WriteWishlistText(string path) =>
        Csv.Save(path, Csv.WishlistText(WishlistItems().Where(w => w.Status == WishStatus.Wanted)) + "\n");

    /// <summary>Saves every wishlist entry as "MovieShelf wishlist.csv" (UTF-8 with BOM).</summary>
    public void WriteWishlistCsv(string path) => Csv.Save(path, Csv.Wishlist(WishlistItems()));
}
