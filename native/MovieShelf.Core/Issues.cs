using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Data.Sqlite;

namespace MovieShelf.Core;

public sealed class IssueFile
{
    public int Id;
    public string Raw = "", Quality = "";
    public string? Verdict;
    public long? Size;
    public List<string> Paths = new();
}

/// <summary>A folder named like a list entry that holds no real video.</summary>
public sealed class IssueJunk
{
    public int Id, NFiles;
    public string Raw = "", Path = "";
    public long Size;
    public List<string> Names = new();
}

public sealed class IssueRow
{
    public string Kind = "", IKey = "", Note = "", K = "", Title = "";
    public string? Sub, Tt;
    public int? Year, Votes;
    public double? Rating;
    public long Wasted;
    public bool Ignored, Manual;
    public double Sim;
    public List<IssueFile> Files = new();
    public List<int>? JunkIds;
    public List<IssueJunk> Junk = new();
    // Note is the English text the CSV uses; the window translates NoteFormat and fills in NoteArgs
    public string NoteFormat = "";
    public string[] NoteArgs = Array.Empty<string>();
    public int? FirstFileId => Files.Count > 0 ? Files[0].Id : null;
    public bool Matched => Tt != null;
    public int? Tconst => Tt != null && int.TryParse(Tt.AsSpan(2), NumberStyles.None, CultureInfo.InvariantCulture, out int tc) ? tc : null;

    /// <summary>The file a Fix match on this row is for: in Duplicates the first copy that is not kept, else the first file.</summary>
    public int FixFileId(bool inDuplicates) => (inDuplicates ? Files.FirstOrDefault(f => f.Verdict is not (null or "keep")) : null)?.Id ?? Files[0].Id;

    /// <summary>The list entries Remove from list takes away: only those with the empty folder when there are any, else all.</summary>
    public IReadOnlyList<int> RemoveIds => JunkIds ?? Files.Select(f => f.Id).ToList();

    public bool HasCopiesToRemove => Sub is not ("mixed" or "copies") && Files.Any(f => f.Verdict is "lower" or "same");
    public bool RemovalsHavePaths => Files.Any(f => f.Paths.Count > 0);

    /// <summary>What the copy buttons put on the clipboard: the extra paths of a file seen twice, or the paths (else names) of the copies not worth keeping.</summary>
    public List<string> CopyList() => Sub == "copies"
        ? Files.Where(f => f.Paths.Count > 1).SelectMany(f => f.Paths.Skip(1)).ToList()
        : Files.Where(f => f.Verdict is "lower" or "same").SelectMany(f => f.Paths.Count > 0 ? f.Paths : new List<string> { f.Raw }).ToList();
}

public sealed class IssueReport
{
    public Dictionary<string, List<IssueRow>> Kinds = new();
    public long Wasted;
    public int Ignored;

    List<IssueRow> Of(string kind) => Kinds.TryGetValue(kind, out var l) ? l : new();

    public int OpenCount(string kind) => Of(kind).Count(r => !r.Ignored);

    /// <summary>Duplicate items not ignored. Series are listed in their own tab, not counted.</summary>
    public List<IssueRow> OpenDupes() => Of("dupes").Where(r => !r.Ignored && r.Sub != "series").ToList();

    /// <summary>The number on a tab: Duplicates, Quality (Dubbed only is left out, as in the Python app) or Other.</summary>
    public int AreaCount(string area) => area switch
    {
        "dupes" => OpenDupes().Count,
        "quality" => new[] { "lowq", "unknownq", "realq" }.Sum(OpenCount),
        "other" => new[] { "novideo", "namediff", "yeardiff", "runtime", "fatitle", "nonfilm", "noposter" }.Sum(OpenCount),
        _ => 0,
    };

    /// <summary>The number on a sub-tab chip.</summary>
    public int SubCount(string area, string sub) => area != "dupes" ? OpenCount(sub)
        : sub == "all" ? OpenDupes().Count : Of("dupes").Count(r => !r.Ignored && r.Sub == sub);

    /// <summary>The rows of a sub-tab, ignored ones included: all = series left out, any other sub of Duplicates filters on it.</summary>
    public List<IssueRow> Rows(string area, string sub)
    {
        if (area != "dupes") return Of(sub);
        return sub == "all" ? Of("dupes").Where(r => r.Sub != "series").ToList() : Of("dupes").Where(r => r.Sub == sub).ToList();
    }
}

/// <summary>Library problems beyond name matching: list entries whose folder has no video file, duplicates, low or unknown quality,
/// dubbed-only movies, year, name and length differences, TV titles in a movie list, missing posters and Persian titles in Latin letters.
/// Read-only; the user can ignore an item. With movie folders set, file sizes and locations are known: the better copy of equal quality is
/// the bigger one, the same file in two folders is reported, and wasted space is added up.</summary>
public static class Issues
{
    public static readonly string[] Kinds = { "novideo", "dupes", "lowq", "unknownq", "dubonly", "realq", "yeardiff", "nonfilm", "noposter", "runtime", "fatitle", "namediff" };

    static readonly HashSet<string> SeriesTypes = new() { "tvSeries", "tvMiniSeries", "tvEpisode" };   // several files of these are episodes, not copies
    static readonly Dictionary<string, string> TvTypes = new() { ["tvEpisode"] = "TV episode", ["tvSpecial"] = "TV special", ["tvShort"] = "TV short" };
    public const double NameClose = 70;
    static readonly Dictionary<string, int> ResRank = new() { ["2160p"] = 5, ["1080p"] = 4, ["720p"] = 3, ["576p"] = 2, ["480p"] = 1, ["SD"] = 1 };
    static readonly Dictionary<string, int> SrcRank = new() { ["BluRay"] = 5, ["WEB-DL"] = 4, ["WEBRip"] = 3, ["HDTV"] = 2, ["HDRip"] = 2, ["DVDRip"] = 1 };
    static readonly Dictionary<string, int> Channels = new() { ["7.1"] = 3, ["5.1"] = 2, ["2.0"] = 1 };
    static readonly int Low = ResRank["576p"];
    static readonly Regex ExtraWords = new(@"\b(extras?|bonus|featurette|trailer|sample)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    sealed class F
    {
        public int Id, Tconst;
        public string Raw = "";
        public string? Title, AltTitle, Resolution, Source, Codec, Audio, Edition, Conf;
        public int? Year, Mw, Mh;
        public double? Dur;
        public bool Bit10, Hdr, FarsiDubbed, Manual;
        public long? Size;
        public List<string> Paths = new();
        public long CopiesSize;
        public List<Junk> Junk = NoJunk;
    }

    sealed record Junk(string Path, int NFiles, long Size, List<string> Names);
    static readonly List<Junk> NoJunk = new();

    sealed class T
    {
        public string? Type, Title, Orig, EnTitle, Fa;
        public int? Year, Runtime;
        public double? Rating;
        public int? Votes;
        public bool Poster;
    }

    /// <summary>&gt;0 when file a is better than b, &lt;0 when worse, 0 when equal or unknown.</summary>
    static int Compare(F a, F b)
    {
        if (a.Resolution == null || b.Resolution == null || !ResRank.TryGetValue(a.Resolution, out int ra) || !ResRank.TryGetValue(b.Resolution, out int rb)) return 0;
        if (ra != rb) return ra - rb;
        if (a.Source != null && b.Source != null && SrcRank.TryGetValue(a.Source, out int sa) && SrcRank.TryGetValue(b.Source, out int sb) && sa != sb) return sa - sb;
        if (a.Hdr != b.Hdr) return a.Hdr ? 1 : -1;
        if (a.Bit10 != b.Bit10) return a.Bit10 ? 1 : -1;
        if (a.Audio != null && b.Audio != null && Channels.TryGetValue(a.Audio, out int ca) && Channels.TryGetValue(b.Audio, out int cb) && ca != cb) return ca - cb;
        return 0;
    }

    public static string QualityLabel(string? res, string? src, string? codec, bool hdr, bool bit10, string? audio, bool fa, string? ed) =>
        string.Join(' ', new[] { string.IsNullOrEmpty(res) ? "?" : res, src, codec, hdr ? "HDR" : null, bit10 ? "10-bit" : null, audio, fa ? "Farsi dubbed" : null, ed }.Where(x => !string.IsNullOrEmpty(x)));

    static string Label(F f) => QualityLabel(f.Resolution, f.Source, f.Codec, f.Hdr, f.Bit10, f.Audio, f.FarsiDubbed, f.Edition);

    static long Size(F f) => f.Size ?? 0;

    static int Better(F a, F b) => Compare(a, b) is var c && c != 0 ? c : Size(a).CompareTo(Size(b));

    /// <summary>Python's "a or b": an empty text counts as missing.</summary>
    static string? Or(string? a, string? b) => string.IsNullOrEmpty(a) ? b : a;

    /// <summary>Verdict per file of one movie: keep, lower, same or unsure. Sub: same | lower | versions.</summary>
    static (Dictionary<int, string> Verdict, string Sub) Analyse(List<F> fs)
    {
        var verdict = new Dictionary<int, string>();
        foreach (var g in fs.GroupBy(f => f.FarsiDubbed))
        {
            var list = g.ToList();
            F best = list[0];
            foreach (var f in list.Skip(1)) if (Better(f, best) > 0) best = f;
            foreach (var f in list)
            {
                if (f == best) verdict[f.Id] = "keep";
                else if (string.IsNullOrEmpty(f.Resolution) || string.IsNullOrEmpty(best.Resolution)) verdict[f.Id] = "unsure";
                else verdict[f.Id] = Compare(f, best) < 0 ? "lower" : "same";
            }
        }
        var vs = verdict.Values.ToHashSet();
        string sub = vs.Contains("same") || vs.Contains("unsure") ? "same" : vs.Contains("lower") ? "lower" : "versions";
        return (verdict, sub);
    }

    static string Nums(string? t) => string.Join(",", Regex.Matches(Regex.Replace(t ?? "", @"(?<=\d)\.(?=\d)", ""), @"\d+").Select(m => m.Value));

    /// <summary>A note as format and arguments, so the window can translate what the CSV keeps in English.</summary>
    static (string Format, string[] Args) Say(string format, params object?[] args) =>
        (format, args.Select(a => Convert.ToString(a, CultureInfo.InvariantCulture) ?? "").ToArray());

    const string NotDuplicates = ": probably different movies matched to one IMDb title, not duplicates";

    /// <summary>Why files matched to one IMDb title are probably different movies (or bonus material); null if they look alike.</summary>
    static (string Format, string[] Args)? MixedReason(List<F> fs)
    {
        var titles = fs.Select(f => f.Title).ToList();
        if (titles.Select(Nums).Distinct().Count() > 1)
            return Say("The titles differ by a number ({0})" + NotDuplicates, string.Join(" / ", titles.Distinct().Select(t => t ?? "None")));
        var ex = titles.Select(t => ExtraWords.IsMatch(t ?? "")).ToList();
        if (ex.Any(x => x) && !ex.All(x => x)) return Say("One file looks like bonus material, not the movie" + NotDuplicates);
        return null;
    }

    /// <summary>Why several files of one title are episodes of a series, not duplicate copies; null if they are not. The user's choice wins over the guess.</summary>
    static string? SeriesReason(List<F> fs, T? t, string mk, HashSet<string> ignored)
    {
        if (ignored.Contains($"series:{mk}")) return "You marked this as a series";
        if (ignored.Contains($"noseries:{mk}")) return null;
        if (fs.Count(f => Checker.EpisodeRe.IsMatch(f.Raw)) >= 2) return "The file names have episode numbers (like S01E02): a series, not copies";
        if (t?.Type != null && SeriesTypes.Contains(t.Type)) return "IMDb lists this as a TV series: the files are episodes, not copies";
        return null;
    }

    static string Plain(string? name) => string.Concat(TitleKeys.EditionWords.Replace(TitleKeys.Norm(name), " ").Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    /// <summary>Compares texts as Python does: by code point, not by UTF-16 unit.</summary>
    static int CompareCodePoints(string a, string b)
    {
        int i = 0, j = 0;
        while (i < a.Length && j < b.Length)
        {
            int ca = char.ConvertToUtf32(a, i), cb = char.ConvertToUtf32(b, j);
            if (ca != cb) return ca < cb ? -1 : 1;
            i += char.IsSurrogatePair(a, i) ? 2 : 1;
            j += char.IsSurrogatePair(b, j) ? 2 : 1;
        }
        return (a.Length - i).CompareTo(b.Length - j);
    }

    static int[] CodePoints(string s)
    {
        var l = new List<int>(s.Length);
        for (int i = 0; i < s.Length; i += char.IsSurrogatePair(s, i) ? 2 : 1) l.Add(char.ConvertToUtf32(s, i));
        return l.ToArray();
    }

    /// <summary>rapidfuzz's fuzz.ratio, to the last bit: it counts code points and computes 100 * (1 - distance / total), which is not always the
    /// same double as 200 * common / total. The similarity decides the close/far tab at exactly 70 and the order of equal names.</summary>
    static double Ratio(string a, string b)
    {
        int[] x = CodePoints(a), y = CodePoints(b);
        int total = x.Length + y.Length;
        if (total == 0) return 100;
        var prev = new int[y.Length + 1];
        var cur = new int[y.Length + 1];
        foreach (int c in x)
        {
            for (int j = 1; j <= y.Length; j++)
                cur[j] = c == y[j - 1] ? prev[j - 1] + 1 : Math.Max(prev[j], cur[j - 1]);
            (prev, cur) = (cur, prev);
        }
        int distance = total - 2 * prev[y.Length];
        return (1.0 - (double)distance / total) * 100;
    }

    /// <summary>(file name, IMDb name, similarity) of the file whose name is furthest from the real film name, or null when every file is named like
    /// the film. Files that are an unsure match are left to Matching. Within a file the highest tuple wins, as Python's max does: equal scores
    /// are settled by the names themselves.</summary>
    static (string File, string Real, double Sim)? NameDiff(List<F> fs, T t)
    {
        var real = new[] { t.EnTitle, t.Title, t.Orig, t.Fa }.Distinct().Where(n => !string.IsNullOrEmpty(n) && Plain(n).Length > 0).Select(n => n!).ToList();
        var plain = real.Select(Plain).ToHashSet();
        (string, string, double)? worst = null;
        foreach (var f in fs)
        {
            if (f.Conf != null && Matcher.Unsure.Contains(f.Conf) && !f.Manual) continue;
            var names = new[] { f.Title, f.AltTitle }.Where(n => !string.IsNullOrEmpty(n) && Plain(n).Length > 0).Select(n => n!).ToList();
            if (names.Count == 0 || real.Count == 0 || names.Any(n => plain.Contains(Plain(n)))) continue;
            (double Sim, string F, string R) best = (-1, "", "");
            foreach (var n in names)
                foreach (var r in real)
                {
                    double s = Ratio(Plain(n), Plain(r));
                    bool higher = s > best.Sim;
                    if (s == best.Sim)
                    {
                        int byName = CompareCodePoints(n, best.F);
                        higher = byName > 0 || (byName == 0 && CompareCodePoints(r, best.R) > 0);
                    }
                    if (higher) best = (s, n, r);
                }
            if (worst == null || best.Sim < worst.Value.Item3) worst = (best.F, best.R, best.Sim);
        }
        return worst;
    }

    public static IssueReport Build(Library lib)
    {
        using var db = lib.Reader();
        var titles = new Dictionary<int, T>();
        using (var c = db.CreateCommand())
        {
            c.CommandText = "SELECT t.tconst, t.type, t.title, t.orig, t.en_title, t.year, t.runtime, t.rating, t.votes, t.poster, coalesce(f.title, t.fa_title) FROM titles t LEFT JOIN fa_titles f ON f.tconst = t.tconst";
            using var r = c.ExecuteReader();
            while (r.Read())
            {
                string? S(int i) => r.IsDBNull(i) ? null : r.GetString(i);
                int? I(int i) => r.IsDBNull(i) ? null : (int)r.GetInt64(i);
                titles[r.GetInt32(0)] = new T
                {
                    Type = S(1), Title = S(2), Orig = S(3), EnTitle = S(4), Year = I(5), Runtime = I(6), Rating = r.IsDBNull(7) ? null : r.GetDouble(7), Votes = I(8),
                    Poster = !r.IsDBNull(9) && !(r.GetValue(9) is "" or 0L), Fa = S(10),
                };
            }
        }
        var info = new Dictionary<string, List<(string P, long S)>>();
        using (var c = db.CreateCommand())
        {
            c.CommandText = "SELECT raw, path, size FROM fileinfo ORDER BY path";
            using var r = c.ExecuteReader();
            while (r.Read())
            {
                if (!info.TryGetValue(r.GetString(0), out var l)) info[r.GetString(0)] = l = new();
                l.Add((r.GetString(1), r.IsDBNull(2) ? 0 : r.GetInt64(2)));
            }
        }
        var media = new Dictionary<string, (int? W, int? H, double? D)>();
        using (var c = db.CreateCommand())
        {
            c.CommandText = "SELECT path, width, height, duration FROM mediainfo WHERE ok=1";
            using var r = c.ExecuteReader();
            while (r.Read())
                media[r.GetString(0)] = (r.IsDBNull(1) ? null : (int)r.GetInt64(1), r.IsDBNull(2) ? null : (int)r.GetInt64(2), r.IsDBNull(3) ? null : r.GetDouble(3));
        }
        var junk = new Dictionary<string, List<Junk>>();
        using (var c = db.CreateCommand())
        {
            c.CommandText = "SELECT path, raw, nfiles, size, names FROM novideo ORDER BY path";
            using var r = c.ExecuteReader();
            while (r.Read())
            {
                if (r.IsDBNull(1)) continue;
                if (!junk.TryGetValue(r.GetString(1), out var l)) junk[r.GetString(1)] = l = new();
                var names = r.IsDBNull(4) ? null : JsonSerializer.Deserialize<List<string>>(r.GetString(4));
                l.Add(new Junk(r.GetString(0), r.IsDBNull(2) ? 0 : r.GetInt32(2), r.IsDBNull(3) ? 0 : r.GetInt64(3), names ?? new()));
            }
        }
        var ignored = new HashSet<string>();
        using (var c = db.CreateCommand())
        {
            c.CommandText = "SELECT ikey FROM dismissed";
            using var r = c.ExecuteReader();
            while (r.Read()) ignored.Add(r.GetString(0));
        }
        var groups = new Dictionary<long, List<F>>();
        using (var c = db.CreateCommand())
        {
            c.CommandText = "SELECT id, raw, title, alt_title, year, resolution, source, codec, audio, edition, conf, tconst, bit10, hdr, farsi_dubbed, manual FROM files WHERE present=1 ORDER BY id";
            using var r = c.ExecuteReader();
            while (r.Read())
            {
                string? S(int i) => r.IsDBNull(i) ? null : r.GetString(i);
                int? I(int i) => r.IsDBNull(i) ? null : (int)r.GetInt64(i);
                var f = new F
                {
                    Id = r.GetInt32(0), Raw = r.GetString(1), Title = S(2), AltTitle = S(3), Year = I(4), Resolution = S(5), Source = S(6), Codec = S(7),
                    Audio = S(8), Edition = S(9), Conf = S(10), Tconst = I(11) ?? 0, Bit10 = (I(12) ?? 0) != 0, Hdr = (I(13) ?? 0) != 0,
                    FarsiDubbed = (I(14) ?? 0) != 0, Manual = (I(15) ?? 0) != 0,
                };
                if (info.TryGetValue(f.Raw, out var here))
                {
                    f.Paths = here.Select(x => x.P).ToList();
                    f.Size = here.Max(x => x.S);
                    f.CopiesSize = here.Skip(1).Sum(x => x.S);
                    foreach (var (p, _) in here)
                        if (media.TryGetValue(p, out var m))
                        {
                            (f.Mw, f.Mh, f.Dur) = m;
                            break;
                        }
                }
                if (junk.TryGetValue(f.Raw, out var bad)) f.Junk = bad;
                long gk = f.Tconst != 0 ? f.Tconst : -f.Id;
                if (!groups.TryGetValue(gk, out var g)) groups[gk] = g = new();
                g.Add(f);
            }
        }

        var report = new IssueReport();
        foreach (var k in Kinds) report.Kinds[k] = new();
        foreach (var fs in groups.Values)
        {
            var f0 = fs[0];
            T? t = f0.Tconst != 0 && titles.TryGetValue(f0.Tconst, out var tt) ? tt : null;
            string mk = f0.Tconst != 0 ? $"tt{f0.Tconst:0000000}" : $"f{f0.Id}";
            var (verdict, sub) = fs.Count > 1 && t != null ? Analyse(fs) : (new Dictionary<int, string>(), "");
            var files = fs.Select(f => new IssueFile { Id = f.Id, Raw = f.Raw, Quality = Label(f), Verdict = verdict.GetValueOrDefault(f.Id), Size = f.Size, Paths = f.Paths }).ToList();

            IssueRow Row(string kind, (string Format, string[] Args) note, string? subKind = null, string? ikey = null, long wasted = 0, double sim = 0)
            {
                ikey ??= $"{kind}:{mk}";
                return new IssueRow
                {
                    Kind = kind, K = mk, Tt = t != null ? mk : null, Title = (t != null ? Or(t.EnTitle, t.Title) : Or(f0.Title, f0.Raw)) ?? "",
                    Year = t?.Year is int y && y != 0 ? y : f0.Year, Rating = t?.Rating, Votes = t?.Votes, IKey = ikey,
                    Note = string.Format(CultureInfo.InvariantCulture, note.Format, note.Args), NoteFormat = note.Format, NoteArgs = note.Args,
                    Sub = subKind, Wasted = wasted, Ignored = ignored.Contains(ikey), Sim = sim, Files = files,
                };
            }
            void Add(IssueRow r) => report.Kinds[r.Kind].Add(r);

            var bad = fs.Where(f => f.Junk.Count > 0).ToList();
            if (bad.Count > 0)
            {
                var where = bad.SelectMany(f => f.Junk).ToList();
                var row = Row("novideo", where.All(j => j.NFiles == 0) ? Say("The folder is empty: no video file")
                    : Say("The folder has no video file, only other files ({0})", Cut(string.Join(", ", where.SelectMany(j => j.Names.Take(3)).Distinct()), 160)));
                row.JunkIds = bad.Select(f => f.Id).ToList();
                row.Junk = bad.SelectMany(f => f.Junk.Select(j => new IssueJunk { Id = f.Id, Raw = f.Raw, Path = j.Path, NFiles = j.NFiles, Size = j.Size, Names = j.Names })).ToList();
                Add(row);
            }
            string? episodes = fs.Count > 1 ? SeriesReason(fs, t, mk, ignored) : null;
            if (episodes != null)
            {
                var row = Row("dupes", Say(episodes), "series", $"dupes:{mk}:series");
                row.Manual = ignored.Contains($"series:{mk}");
                Add(row);
            }
            else if (verdict.Count > 0)
            {
                string ids = string.Join("-", fs.Select(f => f.Id));
                var why = MixedReason(fs);
                if (why != null)
                    Add(Row("dupes", why.Value, "mixed", $"dupes:{mk}:{ids}"));
                else
                {
                    string note = sub switch
                    {
                        "same" => "Two copies of the same quality",
                        "lower" => "A lower-quality copy sits beside a better one",
                        _ => "Dubbed and original: not redundant, probably on purpose",
                    };
                    if (sub == "same" && verdict.Values.Contains("unsure"))
                        note = "Several copies; at least one has no resolution in its name, so they cannot be compared";
                    long waste = sub != "versions" ? fs.Where(f => verdict[f.Id] is "lower" or "same").Sum(Size) : 0;
                    Add(Row("dupes", Say(note), sub, $"dupes:{mk}:{ids}", waste));
                }
            }
            var extra = fs.Where(f => f.Paths.Count > 1).ToList();
            if (extra.Count > 0 && episodes == null)
                Add(Row("dupes", Say("The same file is in {0} folders", extra[0].Paths.Count), "copies", $"dupes:{mk}:copies:" + string.Join("-", extra.Select(f => f.Id)), extra.Sum(f => f.CopiesSize)));

            var ress = fs.Select(f => f.Resolution).ToList();
            if (ress.All(r => !string.IsNullOrEmpty(r)) && ress.Max(r => ResRank.GetValueOrDefault(r!, 0)) <= Low)
                Add(Row("lowq", Say("Best copy is {0}", Label(fs.OrderByDescending(f => ResRank.GetValueOrDefault(f.Resolution ?? "", 0)).First()))));
            if (ress.All(string.IsNullOrEmpty)) Add(Row("unknownq", Say("No resolution in the file name")));
            if (fs.All(f => f.FarsiDubbed)) Add(Row("dubonly", Say("Only a Farsi-dubbed copy")));
            foreach (var f in fs)   // the file name claims more than the picture really is
            {
                string? real = Probe.RealHeightClass(f.Mw, f.Mh);
                if (real != null && f.Resolution != null && ResRank.TryGetValue(f.Resolution, out int named) && ResRank[real] < named)
                {
                    Add(Row("realq", Say("Named {0}, but the picture is {1}x{2}", f.Resolution, f.Mw, f.Mh)));
                    break;
                }
            }
            if (t != null)
            {
                var durs = fs.Where(f => f.Dur is > 0 or < 0).Select(f => f.Dur!.Value).ToList();
                if (durs.Count > 0 && t.Runtime is int rt && rt != 0)
                {
                    double mins = durs.Max() / 60;
                    string shown = Math.Round(mins, MidpointRounding.ToEven).ToString("0", CultureInfo.InvariantCulture);
                    if (mins < rt * 0.8 && rt - mins > 15)
                        Add(Row("runtime", Say("The file is {0} min, IMDb says {1} min: cut short, or a different movie", shown, rt)));
                    else if (mins > rt * 1.35 && mins - rt > 20)
                        Add(Row("runtime", Say("The file is {0} min, IMDb says {1} min: a different cut, or a different movie", shown, rt)));
                }
                int? fy = fs.Select(f => f.Year).FirstOrDefault(y => y is not (null or 0));
                if (fy != null && t.Year is not (null or 0) && Math.Abs(fy.Value - t.Year.Value) >= 2 && !fs.Any(f => (f.Conf != null && Matcher.Unsure.Contains(f.Conf)) || f.Manual))
                    Add(Row("yeardiff", Say("The file name says {0}, IMDb says {1}", fy, t.Year)));
                var nd = NameDiff(fs, t);
                if (nd != null)
                    Add(Row("namediff", Say("The file name says “{0}”, IMDb calls it “{1}”", nd.Value.File, nd.Value.Real), nd.Value.Sim >= NameClose ? "close" : "far", sim: nd.Value.Sim));
                if (t.Type != null && TvTypes.TryGetValue(t.Type, out var tvName)) Add(Row("nonfilm", Say("IMDb lists this as a " + tvName)));
                if (!t.Poster) Add(Row("noposter", Say("No poster found")));
                if (FaTitles.IsLatinOnly(t.Fa)) Add(Row("fatitle", Say("The Persian title is written in Latin letters: {0}", t.Fa)));
            }
        }
        foreach (var k in new[] { "lowq", "dubonly" })
            report.Kinds[k] = report.Kinds[k].OrderByDescending(r => r.Votes ?? 0).ThenBy(r => r.Title, StringComparer.Ordinal).ToList();
        report.Kinds["dupes"] = report.Kinds["dupes"].OrderByDescending(r => r.Wasted).ThenBy(r => r.Title.ToLowerInvariant(), StringComparer.Ordinal).ToList();
        foreach (var k in new[] { "novideo", "unknownq", "realq", "yeardiff", "nonfilm", "noposter", "runtime", "fatitle" })
            report.Kinds[k] = report.Kinds[k].OrderBy(r => r.Title.ToLowerInvariant(), StringComparer.Ordinal).ToList();
        report.Kinds["namediff"] = report.Kinds["namediff"].OrderBy(r => r.Sim).ThenBy(r => r.Title.ToLowerInvariant(), StringComparer.Ordinal).ToList(); // the most different names first
        report.Ignored = Kinds.Sum(k => report.Kinds[k].Count(r => r.Ignored));
        report.Wasted = report.Kinds["dupes"].Where(r => !r.Ignored).Sum(r => r.Wasted);
        return report;
    }

    /// <summary>The first n characters, a pair of surrogates counting as one (Python's text[:n]).</summary>
    static string Cut(string s, int n)
    {
        int i = 0;
        while (i < s.Length && n-- > 0) i += char.IsSurrogatePair(s, i) ? 2 : 1;
        return s[..i];
    }

    // ---------- CSV export ----------
    static string CsvField(string? s)
    {
        s ??= "";
        return s.IndexOfAny(new[] { ',', '"', '\r', '\n' }) >= 0 ? "\"" + s.Replace("\"", "\"\"") + "\"" : s;
    }

    /// <summary>A byte order mark and Python's csv.writer layout: quotes only where needed, CRLF line ends.</summary>
    static string Csv(IEnumerable<string?[]> rows)
    {
        var sb = new StringBuilder("﻿");
        foreach (var r in rows) sb.Append(string.Join(',', r.Select(CsvField))).Append("\r\n");
        return sb.ToString();
    }

    static string? Num(int? n) => n?.ToString(CultureInfo.InvariantCulture);

    /// <summary>A number as Python writes a float: 8.0, not 8.</summary>
    static string? Num(double? d)
    {
        if (d is not double v) return null;
        string s = v.ToString("R", CultureInfo.InvariantCulture);
        return s.Contains('.') || s.Contains('E') ? s : s + ".0";
    }

    /// <summary>The Export list (CSV) file of an issue tab (/api/issues.csv). sub "all" means every row of the kind, ignored and series ones too.</summary>
    public static string ToCsv(IssueReport report, string kind, string sub)
    {
        var rows = report.Kinds.TryGetValue(kind, out var l) ? l : new();
        if (sub != "all") rows = rows.Where(r => r.Sub == sub).ToList();
        var lines = new List<string?[]> { new[] { "Title", "Year", "IMDb", "Rating", "Votes", "Problem", "Files", "Ignored" } };
        foreach (var r in rows)
            lines.Add(new[]
            {
                r.Title, Num(r.Year), r.Tt ?? "", Num(r.Rating), Num(r.Votes), r.Note,
                string.Join(" | ", r.Files.Select(f => $"{f.Raw} [{f.Quality}{(string.IsNullOrEmpty(f.Verdict) ? "" : ", " + f.Verdict)}]")),
                r.Ignored ? "yes" : "",
            });
        return Csv(lines);
    }

    /// <summary>The Export list (CSV) file of the Matching tab (/api/problems.csv).</summary>
    public static string ToCsv(IEnumerable<MatchProblem> problems)
    {
        var lines = new List<string?[]> { new[] { "Name", "Problem", "Current IMDb", "Current title", "Year", "Other candidates" } };
        foreach (var p in problems)
            lines.Add(new[]
            {
                p.Raw, p.Reason, p.Current != null ? $"tt{p.Current.Tconst:0000000}" : "", p.Current?.Title, Num(p.Current?.Year),
                string.Join(" | ", p.Cands.Select(c => $"tt{c.Tconst:0000000} {c.Title} ({Num(c.Year) ?? "None"})")),
            });
        return Csv(lines);
    }
}
