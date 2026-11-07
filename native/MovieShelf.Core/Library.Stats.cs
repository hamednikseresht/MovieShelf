using System.Globalization;

namespace MovieShelf.Core;

/// <summary>Which movies the statistics cover (the "All / Movies / Series" switch of the web UI).</summary>
public enum StatsKind { All, Movies, Series }

/// <summary>One bar of the statistics dialog. Key (when set): decade number or person id; the label doubles as the filter value for genre, quality and country.</summary>
public sealed record StatBar(string Label, int Count, string? Key = null);

/// <summary>The numbers of the Collection statistics dialog (app.js openStats). Decades, Genres and Countries follow the kind switch;
/// everything else covers all movies.</summary>
public sealed class CollectionStats
{
    public int Movies, Files, Watched, NeedReview;
    public double AvgRating;            // mean IMDb rating of the rated movies, 2 decimals
    public long Minutes;                // total runtime
    public int Hours => (int)Math.Round(Minutes / 60.0, MidpointRounding.AwayFromZero);
    public int Days => (int)Math.Round(Minutes / 60.0 / 24, MidpointRounding.AwayFromZero);
    public List<StatBar> Decades = new();      // oldest first; Key = decade (1990), Label "1990s"
    public List<StatBar> Genres = new();       // top 15
    public List<StatBar> Directors = new();    // top 15; Key = person id
    public List<StatBar> Actors = new();       // top 15; Key = person id
    public List<StatBar> Quality = new();      // 2160p .. SD, only those present
    public List<StatBar> Ratings = new();      // "9+", "8+", ... "4+"
    public List<StatBar> Countries = new();    // top 15; empty when no movie has countries

    static readonly string[] ResOrder = { "2160p", "1080p", "720p", "576p", "480p", "SD" };

    /// <summary>Most frequent values; ties keep first-seen order (as the stable sort in the web UI).</summary>
    static List<StatBar> TopBars(IEnumerable<string?> values, int n = 15)
    {
        var c = new Dictionary<string, int>();
        var order = new List<string>();
        foreach (var v in values)
        {
            if (string.IsNullOrEmpty(v)) continue;
            if (c.TryAdd(v, 1)) order.Add(v); else c[v]++;
        }
        return order.Select(k => new StatBar(k, c[k])).OrderByDescending(b => b.Count).Take(n).ToList();
    }

    static List<StatBar> TopPeople(IEnumerable<Movie> all, Func<Movie, List<Person>> get)
    {
        var c = new Dictionary<int, int>();
        var order = new List<int>();
        var names = new Dictionary<int, string>();
        foreach (var m in all)
            foreach (var p in get(m))
            {
                if (c.TryAdd(p.Id, 1)) order.Add(p.Id); else c[p.Id]++;
                names[p.Id] = p.Name;
            }
        return order.Select(id => new StatBar(names[id], c[id], id.ToString(CultureInfo.InvariantCulture))).OrderByDescending(b => b.Count).Take(15).ToList();
    }

    public static CollectionStats Compute(IReadOnlyList<Movie> all, StatsKind kind = StatsKind.All)
    {
        var pool = all.Where(m => kind == StatsKind.All || (kind == StatsKind.Series) == m.IsSeries).ToList();
        var rated = all.Where(m => m.Rating is > 0).ToList();
        var s = new CollectionStats
        {
            Movies = all.Count, Files = all.Sum(m => m.Files.Count), Watched = all.Count(m => m.Watched), NeedReview = all.Count(m => m.NeedsReview),
            AvgRating = Math.Round(rated.Sum(m => m.Rating!.Value) / Math.Max(rated.Count, 1), 2, MidpointRounding.AwayFromZero),
            Minutes = all.Sum(m => (long)(m.Runtime ?? 0)),
        };
        s.Decades = pool.Where(m => m.Decade != null).GroupBy(m => m.Decade!.Value).OrderBy(g => g.Key)
            .Select(g => new StatBar(g.Key + "s", g.Count(), g.Key.ToString(CultureInfo.InvariantCulture))).ToList();
        s.Genres = TopBars(pool.SelectMany(m => m.Genres));
        s.Directors = TopPeople(all, m => m.Dir);
        s.Actors = TopPeople(all, m => m.Cast);
        s.Quality = ResOrder.Select(r => new StatBar(r, all.Count(m => m.Files.Any(f => f.Res == r)))).Where(b => b.Count > 0).ToList();
        s.Ratings = new[] { 9, 8, 7, 6, 5, 4 }.Select(r => new StatBar(r + "+", all.Count(m => m.Rating >= r && m.Rating < r + 1))).ToList();
        if (all.Any(m => m.Countries.Length > 0)) s.Countries = TopBars(pool.SelectMany(m => m.Countries));
        return s;
    }
}

/// <summary>The counters of the Settings "Library" panel, the header badges and the wishlist count (Python library.stats()).</summary>
public sealed class LibraryStats
{
    public int Files, Matched, Titles, Posters, Enriched, Tmdb, Scores, Problems, Duplicates, Novideo, Wishlist, Unlisted;
    public double? RatingsAgeDays;      // age of title.ratings.tsv.gz, null when not downloaded
    public bool Index, Datasets;        // IMDb index built / all IMDb dataset files present
    public string ImdbDir = "";
}

public sealed partial class Library
{
    static readonly string[] StatsDatasetNames = { "title.ratings", "title.basics", "title.akas", "title.crew", "title.principals", "name.basics" };
    (int Version, int Rev) dupKey = (-1, -1);
    int dupCount, novideoCount;

    void CountOpenIssues()
    {
        var key = (Version, IssueRev);
        if (dupKey == key) return;
        var report = Issues.Build(this);
        dupCount = report.OpenDupes().Count;
        novideoCount = report.OpenCount("novideo");
        dupKey = key;
    }

    /// <summary>Duplicate items not ignored, series left out (the header badge adds it to the matching problems). 'mixed' items count too: they
    /// are wrong matches in disguise. Cached per data version.</summary>
    public int OpenDuplicates()
    {
        CountOpenIssues();
        return dupCount;
    }

    /// <summary>List entries with a folder without any video file, not ignored (the header badge counts them too). Cached per data version.</summary>
    public int OpenNovideo()
    {
        CountOpenIssues();
        return novideoCount;
    }

    /// <summary>All library counters in one call. Quick (a few COUNT queries; duplicates are cached).</summary>
    public LibraryStats GetLibraryStats()
    {
        string ratings = Path.Combine(ImdbDir, "title.ratings.tsv.gz");
        return new LibraryStats
        {
            Files = Scalar<int>("SELECT count(*) FROM files WHERE present=1"),
            Matched = Scalar<int>("SELECT count(*) FROM files WHERE present=1 AND tconst IS NOT NULL"),
            Titles = Scalar<int>("SELECT count(DISTINCT tconst) FROM files WHERE present=1 AND tconst IS NOT NULL"),
            Posters = Scalar<int>("SELECT count(*) FROM titles WHERE poster IS NOT NULL"),
            Enriched = Scalar<int>("SELECT count(*) FROM titles WHERE enriched=1"),
            Tmdb = Scalar<int>("SELECT count(*) FROM titles WHERE tmdb_done=1 AND tmdb_id IS NOT NULL"),
            Scores = Scalar<int>("SELECT count(*) FROM titles WHERE rt IS NOT NULL OR mc IS NOT NULL"),
            Problems = Scalar<int>("SELECT count(*) FROM files WHERE present=1 AND manual=0 AND conf IN ('review','fuzzy','online','noyear','none')"),
            Duplicates = OpenDuplicates(),
            Novideo = OpenNovideo(),
            Wishlist = Scalar<int>("SELECT count(*) FROM wishlist WHERE status='wanted'"),
            Unlisted = Scalar<int>("SELECT count(*) FROM unlisted"),
            RatingsAgeDays = File.Exists(ratings) ? (DateTime.Now - File.GetLastWriteTime(ratings)).TotalDays : null,
            Index = Matcher.HasIndex,
            Datasets = StatsDatasetNames.All(n => File.Exists(Path.Combine(ImdbDir, n + ".tsv.gz"))),
            ImdbDir = ImdbDir,
        };
    }

    /// <summary>The Collection statistics dialog. Loads the movies (can take a moment on a big library: run it off the UI thread),
    /// or call <see cref="CollectionStats.Compute"/> with a list the UI already holds.</summary>
    public CollectionStats GetCollectionStats(StatsKind kind = StatsKind.All) => CollectionStats.Compute(LoadMovies(), kind);
}
