using System.Text.Json;
using System.Text.RegularExpressions;

namespace MovieShelf.Core;

/// <summary>A movie of the library that belongs to a collection.</summary>
public sealed record CollectionMovie(string K, string? Title, int? Year);

/// <summary>A part TMDB lists for a collection that the library does not have.</summary>
public sealed record MissingPart(string? Title, int? Year);

/// <summary>A collection (franchise) found in the library, with the parts TMDB lists once they were fetched.</summary>
public sealed class CollectionInfo
{
    public string Name = "";
    public List<CollectionMovie> Have = new();
    public List<MissingPart> Missing = new();
    /// <summary>Number of parts TMDB lists (null: not fetched yet).</summary>
    public int? Total;
    public bool Fetched;
}

public sealed partial class Library
{
    static readonly Regex NotAlnum = new("[^a-z0-9]+", RegexOptions.Compiled);

    static string CollNorm(string? s) => NotAlnum.Replace((s ?? "").ToLowerInvariant(), "");

    /// <summary>Collections found in the library, with the parts TMDB lists when they were fetched. Those with missing movies come first.</summary>
    public List<CollectionInfo> Collections()
    {
        var byColl = new Dictionary<string, List<CollectionMovie>>();
        var known = new Dictionary<string, HashSet<int?>>(); // normalised title -> years of every movie in the library
        var parts = new Dictionary<string, List<CollectionPart>>();
        using (var rd = Reader())
        {
            using (var c = Cmd(rd, "SELECT tconst, title, en_title, orig, year, collection FROM titles WHERE tconst IN (SELECT tconst FROM files WHERE present=1)", Array.Empty<object?>()))
            using (var r = c.ExecuteReader())
                while (r.Read())
                {
                    string? S(int i) => r.IsDBNull(i) ? null : r.GetString(i);
                    int? year = r.IsDBNull(4) ? null : (int)r.GetInt64(4);
                    foreach (var t in new[] { S(1), S(2), S(3) })
                    {
                        if (string.IsNullOrEmpty(t)) continue;
                        string k = CollNorm(t);
                        if (!known.TryGetValue(k, out var ys)) known[k] = ys = new();
                        ys.Add(year);
                    }
                    if (!string.IsNullOrEmpty(S(5)))
                    {
                        if (!byColl.TryGetValue(S(5)!, out var l)) byColl[S(5)!] = l = new();
                        l.Add(new CollectionMovie($"tt{r.GetInt64(0):0000000}", S(2) is { Length: > 0 } en ? en : S(1), year));
                    }
                }
            using (var c = Cmd(rd, "SELECT name, parts FROM collections", Array.Empty<object?>()))
            using (var r = c.ExecuteReader())
                while (r.Read())
                    parts[r.GetString(0)] = r.IsDBNull(1) ? new() : JsonSerializer.Deserialize<List<CollectionPart>>(r.GetString(1), Tmdb.JsonOptions) ?? new();
        }
        var outList = new List<CollectionInfo>();
        foreach (var (name, haveRaw) in byColl)
        {
            var have = haveRaw.OrderBy(m => m.Year ?? 9999).ToList();
            var missing = new List<MissingPart>();
            if (parts.TryGetValue(name, out var plist))
                foreach (var p in plist)
                {
                    var years = known.GetValueOrDefault(CollNorm(p.Title)) ?? known.GetValueOrDefault(CollNorm(p.Orig)) ?? new HashSet<int?>();
                    if (!years.Any(y => y is > 0 && p.Year is > 0 && Math.Abs(y.Value - p.Year.Value) <= 1)) missing.Add(new MissingPart(p.Title, p.Year));
                }
            outList.Add(new CollectionInfo
            {
                Name = name, Have = have, Missing = missing, Total = parts.TryGetValue(name, out var pl) ? pl.Count : null, Fetched = parts.ContainsKey(name),
            });
        }
        return outList.OrderBy(c => c.Missing.Count == 0).ThenByDescending(c => c.Have.Count).ThenBy(c => c.Name.ToLowerInvariant(), StringComparer.Ordinal).ToList();
    }

    /// <summary>Job: asks TMDB for the parts of every collection in the library. Needs a TMDB key.</summary>
    public void FetchCollections()
    {
        var key = LoadSettings().TmdbKey;
        if (string.IsNullOrEmpty(key)) throw new InvalidOperationException("Add a TMDB key in Settings first.");
        var tm = new Tmdb(key, Net);
        var names = OnlineQuery("SELECT DISTINCT collection FROM titles WHERE collection IS NOT NULL AND tconst IN (SELECT tconst FROM files WHERE present=1)", r => r.GetString(0));
        for (int i = 0; i < names.Count; i++)
        {
            Progress("Fetching collections from TMDB", i + 1, names.Count);
            string name = names[i];
            int cid;
            List<CollectionPart> parts;
            try
            {
                int? found = tm.SearchCollectionAsync(name, StopToken).GetAwaiter().GetResult();
                if (found == null || found == 0) continue;
                cid = found.Value;
                parts = tm.CollectionAsync(cid, StopToken).GetAwaiter().GetResult();
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                Log($"Collection {name}: {e.Message}");
                continue;
            }
            lock (Lock) Exec("INSERT OR REPLACE INTO collections(name, tmdb_id, parts) VALUES (?,?,?)", name, cid, JsonSerializer.Serialize(parts, Tmdb.JsonOptions));
        }
        Version++;
        Log($"Collections: checked {names.Count}");
    }
}
