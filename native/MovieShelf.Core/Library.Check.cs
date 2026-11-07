using System.Text;

namespace MovieShelf.Core;

/// <summary>What to check: a file or folder path, or pasted text (with a name, whose .csv / .tsv ending selects CSV reading).</summary>
public sealed record CheckSource(string? Path = null, string? Text = null, string? Name = null);

/// <summary>The IMDb title an item was matched to.</summary>
public sealed class CheckImdb
{
    public string Title = "";
    public string? Orig;
    public int? Year, Votes;
    public double? Rating;
    public string? Type;
}

/// <summary>A library file of the same movie, shown under an item that is already in the library.</summary>
public sealed class CheckLibFile
{
    public int Id;
    public string Raw = "", Quality = "";
}

/// <summary>One entry of the checked list.
/// Status: new | better | version | worse | duplicate | have | unmatched. Reason is set when the match is unsure (then Cands lists the choices).</summary>
public sealed class CheckItem
{
    public string Raw = "";
    public string? Path;
    public long? Size;
    public int Copies = 1;                  // the same name more than once in the list
    public bool Episode;
    public string Title = "";
    public int? Year, Tconst;
    public string Conf = "";
    public string Quality = "";
    public bool Fa;                         // Farsi dubbed
    public CheckImdb? Imdb;
    public string Status = "";
    public string? Reason;
    public List<Candidate2>? Cands;
    public List<CheckLibFile>? Lib;
    public string? LibTitle;
    public List<string>? Twins;             // other names in this list that are the same movie

    /// <summary>Whether the UI offers the add checkbox (everything but names already in the lists).</summary>
    public bool Addable => Status != "have";
    /// <summary>The statuses the Python UI preselects and may send to the wishlist.</summary>
    public bool Wanted => Status is "new" or "better" or "version";
}

/// <summary>A movie of the library that is not in the checked list.</summary>
public sealed class CheckMissing
{
    public int Tconst;
    public string? Title;
    public int? Year;
    public List<string> Files = new();
}

/// <summary>An item for the wishlist (package D: wishlist.Add). The Python UI sends title/year/tconst/source 'List check'/note = status label.</summary>
public sealed record CheckWishItem(string Title, int? Year, int? Tconst, string Source, string Note);

public sealed class CheckResult
{
    public string Source = "";
    public int Entries;
    public List<CheckItem> Items = new();
    /// <summary>Per status, plus 'unsure' (items with a Reason) and 'twins'.</summary>
    public Dictionary<string, int> Counts = new();
    public List<CheckMissing> Missing = new();
    public int MissingCount;
    public string At = "";

    public int Count(string key) => Counts.GetValueOrDefault(key);

    /// <summary>Status -> (label, tooltip) the Python UI shows.</summary>
    public static readonly Dictionary<string, (string Label, string Tip)> StatusText = new()
    {
        ["new"] = ("New", "Not in your library yet"), ["better"] = ("Better quality", "You have this movie in lower quality"),
        ["version"] = ("Other version", "You have this movie, but the audio (dubbed / original) differs"),
        ["worse"] = ("Lower quality", "You already have a better copy"), ["duplicate"] = ("Already have", "Same movie, same quality"),
        ["have"] = ("Same file", "This exact name is already in your list"), ["unmatched"] = ("Not found", "Could not be found on IMDb"),
    };

    /// <summary>The wishlist entries for the chosen items (only new / better / version count, as in the UI). Hand them to the wishlist add call.</summary>
    public static List<CheckWishItem> WishlistItems(IEnumerable<CheckItem> chosen) =>
        chosen.Where(it => it.Wanted)
            .Select(it => new CheckWishItem(it.Imdb?.Title ?? it.Raw, it.Imdb?.Year, it.Tconst, "List check", StatusText[it.Status].Label)).ToList();

    /// <summary>The items matching a UI tab: all | new | better | version | worse | duplicate | have | unmatched | unsure | twins. ('missing' is <see cref="Missing"/>.)</summary>
    public IEnumerable<CheckItem> Tab(string tab) => tab switch
    {
        "all" => Items,
        "unsure" => Items.Where(i => i.Reason != null),
        "twins" => Items.Where(i => i.Twins != null),
        _ => Items.Where(i => i.Status == tab),
    };

    /// <summary>The report the Python app exports (/api/check.csv), UTF-8 with BOM so Excel reads it.</summary>
    public byte[] ToCsv()
    {
        var rows = new List<List<object?>>
        {
            new() { "Status", "Name", "Quality", "IMDb", "IMDb title", "IMDb year", "Match", "Problem", "In library", "Also in this list", "Path", "Size GB" },
        };
        foreach (var it in Items)
            rows.Add(new()
            {
                it.Status, it.Raw, it.Quality, it.Tconst != null ? $"tt{it.Tconst:0000000}" : "", it.Imdb?.Title, it.Imdb?.Year, it.Conf, it.Reason ?? "",
                string.Join(" | ", (it.Lib ?? new()).Select(f => $"{f.Raw} ({f.Quality})")), string.Join(" | ", it.Twins ?? new()), it.Path ?? "",
                it.Size is > 0 ? (object)Math.Round(it.Size.Value / 1e9, 2) : "",
            });
        foreach (var m in Missing)
            rows.Add(new() { "only in library", string.Join(" | ", m.Files), "", $"tt{m.Tconst:0000000}", m.Title, m.Year, "", "", "", "", "", "" });
        return Encoding.UTF8.GetBytes(ListCsv.Bom + ListCsv.Write(rows));
    }
}

/// <summary>The list check job, as the UI polls it (/api/check).</summary>
public sealed class CheckState
{
    public bool Running;
    public string Stage = "Reading list";
    public long Done, Total;
    public string? Error;
    public CheckResult? Result;
    public bool HasResult => Result != null;
}

public sealed partial class Library
{
    CheckState check = new();

    /// <summary>The current / last list check. A new instance per StartCheck, so a snapshot read stays consistent.</summary>
    public CheckState Check => check;
    /// <summary>Raised while a check runs and when it ends (progress, result, error).</summary>
    public event Action? CheckChanged;

    /// <summary>Online IMDb search for names the offline index cannot find (the Python online fallback). Wire it to the online matcher;
    /// the check calls it only when the setting OnlineFallback is on, for at most 150 names, and stops asking after the first exception.</summary>
    public Func<ParsedName, int?>? CheckOnlineFind { get; set; }

    /// <summary>Compare a list file / folder / pasted text with the library in the background; the result lands in <see cref="Check"/>.
    /// Returns false when a check is already running. Does not use the library job (lib.Start), exactly like the Python app. Not stoppable.</summary>
    public bool StartCheck(CheckSource src)
    {
        if (check.Running) return false;
        if (!Matcher.HasIndex)
            throw new InvalidOperationException("The IMDb index is not built yet. Run the Python MovieShelf Setup once (it builds imdb_index.db), then use this app.");
        var st = check = new CheckState { Running = true };
        CheckChanged?.Invoke();
        Task.Run(() =>
        {
            try { st.Result = CheckList(src, (stage, done, total) => { st.Stage = stage; st.Done = done; st.Total = total; CheckChanged?.Invoke(); }); }
            catch (Exception e)
            {
                string kind = e switch { FileNotFoundException => "FileNotFoundError", ArgumentException => "ValueError", _ => e.GetType().Name };
                st.Error = $"{kind}: {e.Message}";
            }
            finally
            {
                st.Running = false;
                CheckChanged?.Invoke();
            }
        });
        return true;
    }

    /// <summary>Compare a list file / folder / pasted text with the library: new, better / worse quality, other version, duplicates, not found,
    /// and what only the library has. Read-only and synchronous (StartCheck wraps it); progress(stage, done, total) reports how far it got.</summary>
    public CheckResult CheckList(CheckSource src, Action<string, long, long>? progress = null)
    {
        progress ??= (_, _, _) => { };
        string path = (src.Path ?? "").Trim().Trim('"');
        List<ListEntry> entries;
        string label;
        if (path.Length > 0)
        {
            if (Directory.Exists(path)) { entries = Checker.EntriesFromFolder(path, progress); label = path; }
            else if (File.Exists(path)) { entries = Checker.EntriesFromText(Checker.ReadText(path), path); label = path; }
            else throw new FileNotFoundException($"Not found: {path}");
        }
        else
        {
            string nm = src.Name ?? "";
            entries = Checker.EntriesFromText(src.Text ?? "", nm);
            label = nm.Length > 0 ? nm : "pasted list";
        }
        if (entries.Count == 0)
            throw new ArgumentException("No movies found in that list. A .txt needs one name per line; a folder needs video files.");

        using var ix = Matcher.Open();
        var haveRaw = new HashSet<string>();
        var mineByTc = new Dictionary<int, List<(int Id, string Raw, Quality Q)>>();
        var ltitles = new Dictionary<int, (string? Title, string? En, int? Year)>();
        using (var rd = Reader())
        {
            using (var c = Cmd(rd, "SELECT id, raw, tconst, resolution, source, codec, hdr, bit10, audio, farsi_dubbed, edition, present FROM files WHERE present=1", Array.Empty<object?>()))
            using (var r = c.ExecuteReader())
                while (r.Read())
                {
                    string raw = r.GetString(1);
                    haveRaw.Add(raw);
                    if (r.IsDBNull(2)) continue;
                    var q = new Quality(Str(r, "resolution"), Str(r, "source"), Str(r, "codec"), Flag(r, "hdr"), Flag(r, "bit10"), Str(r, "audio"), Flag(r, "farsi_dubbed"), Str(r, "edition"));
                    int tc = (int)r.GetInt64(2);
                    if (!mineByTc.TryGetValue(tc, out var l)) mineByTc[tc] = l = new();
                    l.Add(((int)r.GetInt64(0), raw, q));
                }
            using (var c = Cmd(rd, "SELECT tconst, title, en_title, year FROM titles", Array.Empty<object?>()))
            using (var r = c.ExecuteReader())
                while (r.Read()) ltitles[(int)r.GetInt64(0)] = (Str(r, "title"), Str(r, "en_title"), Int(r, "year"));
        }
        bool onlineOk = LoadSettings().OnlineFallback && CheckOnlineFind != null;
        int onlineLeft = 150;   // keep a big unknown folder from taking forever
        var rows = new List<CheckItem>();
        var seenRaw = new Dictionary<string, CheckItem>();
        for (int n = 0; n < entries.Count; n++)
        {
            var e = entries[n];
            if (n % 50 == 0) progress("Matching with IMDb", n, entries.Count);
            string raw = e.Raw;
            if (seenRaw.TryGetValue(raw, out var prev)) { prev.Copies++; continue; }
            var p = NameParser.Parse(raw);
            var mi = MatchInput.From(p);
            int? tc = e.Tconst;
            string conf = "imdb id";
            if (tc == null) (tc, conf, _) = Matcher.Pick(ix, mi);
            if (tc == null && onlineOk && onlineLeft > 0 && !e.Episode)
            {
                onlineLeft--;
                try
                {
                    var found = CheckOnlineFind!(p);
                    if (found != null) { tc = found; conf = "online"; }
                }
                catch (Exception ex) when (ex is not OperationCanceledException) { onlineOk = false; }
            }
            IxTitle? t = tc != null ? IndexTitle(ix, tc.Value) : null;
            var q0 = Quality.From(p);
            var item = new CheckItem
            {
                Raw = raw, Path = e.Path, Size = e.Size, Episode = e.Episode, Title = p.Title, Year = p.Year, Tconst = tc, Conf = conf,
                Quality = Checker.QualityLabel(q0), Fa = p.FarsiDubbed,
                Imdb = t == null ? null : new CheckImdb
                {
                    Title = t.Title, Orig = t.Orig != t.Title ? t.Orig : null, Year = t.Year, Rating = t.Rating, Votes = t.Votes, Type = t.TypeName,
                },
            };
            if (e.Episode && tc == null) item.Reason = "TV episode — only movies and whole series are matched";
            else if (Matcher.Unsure.Contains(conf) && !haveRaw.Contains(raw))
            {
                item.Reason = Matcher.Reason(conf, p.Year, t?.Year);
                item.Cands = Matcher.AllCandidates(ix, mi, tc != null ? new[] { tc.Value } : null);
            }
            // compare with the library
            if (haveRaw.Contains(raw)) item.Status = "have";
            else if (tc == null) item.Status = "unmatched";
            else if (mineByTc.TryGetValue(tc.Value, out var mine))
            {
                var best = mine[0];
                foreach (var f in mine) if (Checker.CompareQuality(f.Q, best.Q) > 0) best = f;   // first of the best, like Python max()
                int cmp = Checker.CompareQuality(q0, best.Q);
                item.Lib = mine.Select(f => new CheckLibFile { Id = f.Id, Raw = f.Raw, Quality = Checker.QualityLabel(f.Q) }).ToList();
                if (ltitles.TryGetValue(tc.Value, out var lt)) item.LibTitle = !string.IsNullOrEmpty(lt.En) ? lt.En : lt.Title;
                if (string.IsNullOrEmpty(p.Resolution) && !p.FarsiDubbed) item.Status = "duplicate";   // a bare title (no quality info): you already have it
                else if (!mine.Any(f => f.Q.FarsiDubbed == p.FarsiDubbed)) item.Status = "version";    // same movie, other audio (dubbed vs original)
                else if (cmp > 0) item.Status = "better";
                else if (cmp < 0) item.Status = "worse";
                else item.Status = "duplicate";
            }
            else item.Status = "new";
            seenRaw[raw] = item;
            rows.Add(item);
        }
        // the same movie more than once inside the checked list
        var byTc = new Dictionary<int, List<CheckItem>>();
        foreach (var it in rows)
            if (it.Tconst is int t2 && t2 != 0)
            {
                if (!byTc.TryGetValue(t2, out var l)) byTc[t2] = l = new();
                l.Add(it);
            }
        foreach (var its in byTc.Values.Where(l => l.Count > 1))
            foreach (var it in its) it.Twins = its.Where(o => !ReferenceEquals(o, it)).Select(o => o.Raw).ToList();
        var missing = mineByTc.Where(kv => !byTc.ContainsKey(kv.Key)).Select(kv =>
        {
            ltitles.TryGetValue(kv.Key, out var lt);
            bool known = ltitles.ContainsKey(kv.Key);
            return new CheckMissing { Tconst = kv.Key, Title = known ? (!string.IsNullOrEmpty(lt.En) ? lt.En : lt.Title) : null, Year = known ? lt.Year : null, Files = kv.Value.Select(f => f.Raw).ToList() };
        }).OrderBy(m => (m.Title ?? "").ToLowerInvariant(), StringComparer.Ordinal).ToList();
        var counts = new Dictionary<string, int>();
        foreach (var it in rows) counts[it.Status] = counts.GetValueOrDefault(it.Status) + 1;
        counts["unsure"] = rows.Count(it => it.Reason != null);
        counts["twins"] = rows.Count(it => it.Twins != null);
        progress("Done", entries.Count, entries.Count);
        return new CheckResult { Source = label, Entries = entries.Count, Items = rows, Counts = counts, Missing = missing, MissingCount = missing.Count, At = Now() };
    }

    /// <summary>One row of the offline IMDb index.</summary>
    static IxTitle? IndexTitle(Microsoft.Data.Sqlite.SqliteConnection ix, int id)
    {
        using var c = Cmd(ix, "SELECT id, type, title, orig, year, endyear, runtime, rating, votes FROM titles WHERE id=?", new object?[] { id });
        using var r = c.ExecuteReader();
        if (!r.Read()) return null;
        return new IxTitle
        {
            Id = (int)r.GetInt64(0), Type = r.IsDBNull(1) ? 0 : (int)r.GetInt64(1), Title = r.IsDBNull(2) ? "" : r.GetString(2), Orig = r.IsDBNull(3) ? "" : r.GetString(3),
            Year = r.IsDBNull(4) ? null : (int)r.GetInt64(4), EndYear = r.IsDBNull(5) ? null : (int)r.GetInt64(5), Runtime = r.IsDBNull(6) ? null : (int)r.GetInt64(6),
            Rating = r.IsDBNull(7) ? null : r.GetDouble(7), Votes = r.IsDBNull(8) ? 0 : (int)r.GetInt64(8),
        };
    }
}
