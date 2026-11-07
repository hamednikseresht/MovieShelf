namespace MovieShelf.Core;

/// <summary>A list entry the user took out of the library (table removed, shared with the Python app).</summary>
public sealed record RemovedItem(string Raw, int? Tconst, string? Title, string? Removed);

// Remove from the list: the entry is hidden and every later scan skips it. The list files are not touched.
public sealed partial class Library
{
    const int InChunk = 500;   // ids per IN (...) list

    /// <summary>Takes files out of the library; use it for one file, a selection or "remove all". Only entries still in the library count. Returns how many were removed.</summary>
    public int RemoveFromList(IEnumerable<int> fileIds)
    {
        var ids = fileIds.Distinct().Order().ToList();
        if (ids.Count == 0) return 0;
        int n = 0;
        string when = Now();
        lock (Lock)
        {
            using var tx = db.BeginTransaction();
            for (int i = 0; i < ids.Count; i += InChunk)
            {
                var part = ids.GetRange(i, Math.Min(InChunk, ids.Count - i)).Cast<object?>().ToArray();
                string q = string.Join(',', part.Select(_ => "?"));
                var rows = new List<(string Raw, long? Tc, string Name)>();
                using (var c = Cmd("SELECT f.raw, f.tconst, coalesce(t.title, f.title, f.raw) FROM files f " +
                                   $"LEFT JOIN titles t ON t.tconst=f.tconst WHERE f.id IN ({q}) AND f.present=1", part))
                using (var r = c.ExecuteReader())
                    while (r.Read()) rows.Add((r.GetString(0), r.IsDBNull(1) ? null : r.GetInt64(1), r.GetString(2)));
                foreach (var (raw, tc, name) in rows)
                    Exec("INSERT OR REPLACE INTO removed(raw, tconst, title, removed) VALUES (?,?,?,?)", raw, tc, name, when);
                Exec($"UPDATE files SET present=0 WHERE id IN ({q})", part);
                n += rows.Count;
            }
            tx.Commit();
        }
        Version++;
        IssueRev++;
        Log($"Removed {n} entries from the list");
        DataChanged?.Invoke();
        return n;
    }

    /// <summary>Takes whole movies out of the library: every file of each one. Returns how many files were removed.</summary>
    public int RemoveMovies(IEnumerable<Movie> movies) => RemoveFromList(movies.SelectMany(m => m.Files).Select(f => f.Id));

    /// <summary>The entries taken out of the library, newest first.</summary>
    public List<RemovedItem> RemovedItems()
    {
        var list = new List<RemovedItem>();
        using var rd = Reader();
        using var c = Cmd(rd, "SELECT raw, tconst, title, removed FROM removed ORDER BY removed DESC, raw", Array.Empty<object?>());
        using var r = c.ExecuteReader();
        while (r.Read())
            list.Add(new RemovedItem(r.GetString(0), r.IsDBNull(1) ? null : r.GetInt32(1), r.IsDBNull(2) ? null : r.GetString(2), r.IsDBNull(3) ? null : r.GetString(3)));
        return list;
    }

    /// <summary>Undoes a removal by name. The next scan drops an entry again if its line is no longer in a list file. Returns how many came back.</summary>
    public int RestoreRemoved(IEnumerable<string> raws)
    {
        var names = raws.ToList();
        if (names.Count == 0) return 0;
        int n = 0;
        lock (Lock)
        {
            using var tx = db.BeginTransaction();
            for (int i = 0; i < names.Count; i += InChunk)
            {
                var part = names.GetRange(i, Math.Min(InChunk, names.Count - i)).Cast<object?>().ToArray();
                string q = string.Join(',', part.Select(_ => "?"));
                Exec($"DELETE FROM removed WHERE raw IN ({q})", part);
                n += Exec($"UPDATE files SET present=1 WHERE raw IN ({q})", part);
            }
            tx.Commit();
        }
        Version++;
        IssueRev++;
        DataChanged?.Invoke();
        return n;
    }
}
