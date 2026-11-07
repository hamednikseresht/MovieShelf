using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace MovieShelf.Core;

/// <summary>Result of testing an API key in Settings: Message is empty when it works, otherwise why it was rejected.</summary>
public sealed record KeyTest(bool Ok, string Message);

// Online services: IMDb fallback search, posters, TMDB details, critic scores. The Fetch* methods are jobs: run them with Start.
public sealed partial class Library
{
    HttpMessageHandler? onlineHandler;
    OnlineClient? onlineNet;
    double onlineDelayScale = 1;

    /// <summary>Handler for all online requests (tests plug in fake answers). Null: the real network.</summary>
    public HttpMessageHandler? OnlineHandler
    {
        get => onlineHandler;
        set { onlineHandler = value; onlineNet = null; }
    }

    /// <summary>Multiplies the pauses between retries and batches (tests set 0).</summary>
    public double OnlineDelayScale
    {
        get => onlineDelayScale;
        set { onlineDelayScale = value; onlineNet = null; }
    }

    OnlineClient Net => onlineNet ??= new OnlineClient(onlineHandler) { DelayScale = onlineDelayScale };

    List<T> OnlineQuery<T>(string sql, Func<SqliteDataReader, T> map, params object?[] args)
    {
        var list = new List<T>();
        lock (Lock)
        {
            using var c = Cmd(sql, args);
            using var r = c.ExecuteReader();
            while (r.Read()) list.Add(map(r));
        }
        return list;
    }

    /// <summary>Runs the work with at most <paramref name="degree"/> at a time; stops at the next step on Stop (OperationCanceledException).</summary>
    void OnlinePool<T>(IReadOnlyList<T> items, int degree, Func<T, CancellationToken, Task> body) =>
        Parallel.ForEachAsync(items, new ParallelOptions { MaxDegreeOfParallelism = degree, CancellationToken = StopToken },
            (item, ct) => new ValueTask(body(item, ct))).GetAwaiter().GetResult();

    // ---------- IMDb search fallback ----------
    /// <summary>Searches IMDb online for files the offline index could not match; a hit is stored with confidence 'online'.</summary>
    public void OnlineMatch(IReadOnlyList<int> fileIds)
    {
        int found = 0;
        for (int n = 0; n < fileIds.Count; n++)
        {
            Progress("Searching IMDb online for unmatched titles", n, fileIds.Count);
            var f = OnlineQuery("SELECT title, alt_title, year FROM files WHERE id=?",
                r => (Title: r.IsDBNull(0) ? null : r.GetString(0), Alt: r.IsDBNull(1) ? null : r.GetString(1), Year: r.IsDBNull(2) ? (int?)null : r.GetInt32(2)),
                fileIds[n]);
            if (f.Count == 0) continue;
            SuggestItem? best;
            try
            {
                best = ImdbOnline.FindAsync(Net, f[0].Title, f[0].Alt, f[0].Year, StopToken).GetAwaiter().GetResult();
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                Log($"Online search failed: {e.Message}");
                return;
            }
            if (best == null) continue;
            found++;
            lock (Lock) Exec("UPDATE files SET tconst=?, conf='online', score=? WHERE id=?", best.Tconst, 0, fileIds[n]);
        }
        Log($"Online search matched {found} of {fileIds.Count} more");
    }

    // ---------- posters ----------
    public string PosterPath(int tconst) => Path.Combine(PosterDir, $"tt{tconst:0000000}.jpg");

    /// <summary>Downloads missing posters (IMDb's, 400 px wide) into posters\ttNNNNNNN.jpg. Titles tried before are skipped unless
    /// <paramref name="retry"/>. Needs the 'online posters' setting. What was finished is kept when stopped.</summary>
    public void FetchPosters(bool retry = false)
    {
        if (!LoadSettings().OnlinePosters) return;
        var rows = OnlineQuery("SELECT tconst, poster_url FROM titles WHERE poster IS NULL" + (retry ? "" : " AND poster_tried=0"),
            r => (Tc: r.GetInt32(0), Url: r.IsDBNull(1) ? null : r.GetString(1)));
        if (rows.Count == 0) return;
        int done = 0, ok = 0;
        var results = new List<(int Tc, string? Url, string? File)>();
        var gate = new object();
        try
        {
            OnlinePool(rows, 10, async (row, ct) =>
            {
                ct.ThrowIfCancellationRequested();
                var (i, url) = row;
                string path = PosterPath(i);
                string? saved = null;
                try
                {
                    if (string.IsNullOrEmpty(url)) url = await ImdbOnline.PosterUrlAsync(Net, i, ct).ConfigureAwait(false);
                    if (!string.IsNullOrEmpty(url))
                    {
                        var data = await Net.GetBytesAsync(ImdbOnline.SizedAmazon(url, 400)!, ct).ConfigureAwait(false);
                        if (data.Length > 0)
                        {
                            await File.WriteAllBytesAsync(path, data, ct).ConfigureAwait(false);
                            saved = Path.GetFileName(path);
                            Interlocked.Increment(ref ok);
                        }
                    }
                }
                catch (Exception e) when (e is not OperationCanceledException) { }
                lock (gate)
                {
                    results.Add((i, url, saved));
                    if (++done % 25 == 0) Progress($"Downloading posters ({ok} saved)", done, rows.Count);
                    if (results.Count >= 100)
                    {
                        SavePosters(results);
                        results.Clear();
                    }
                }
            });
        }
        finally { SavePosters(results); }
        Log($"Posters: {ok} downloaded, {rows.Count - ok} not available");
    }

    void SavePosters(List<(int Tc, string? Url, string? File)> results)
    {
        if (results.Count == 0) return;
        lock (Lock)
        {
            using var tx = db.BeginTransaction();
            foreach (var (tc, url, file) in results)
                Exec("UPDATE titles SET poster_url=coalesce(?, poster_url), poster=?, poster_tried=1 WHERE tconst=?", url, file, tc);
            tx.Commit();
        }
        Version++;
    }

    // ---------- TMDB ----------
    /// <summary>Fetches plot, tagline, countries, languages, Persian title, cast photos and collection from TMDB for the titles not
    /// done yet (or the given ones), then retries posters for titles that have none. Needs a TMDB key in the settings.</summary>
    public void FetchTmdb(IEnumerable<int>? ids = null)
    {
        var key = LoadSettings().TmdbKey;
        if (string.IsNullOrEmpty(key)) return;
        var tm = new Tmdb(key, Net);
        var list = ids?.ToList() ?? OnlineQuery("SELECT tconst FROM titles WHERE tmdb_done=0 AND type IS NOT NULL", r => r.GetInt32(0));
        if (list.Count == 0) return;
        int done = 0, errors = 0;
        var batch = new List<(int Tc, TmdbDetails? D)>();
        var gate = new object();
        try
        {
            OnlinePool(list, 6, async (i, ct) =>
            {
                ct.ThrowIfCancellationRequested();
                TmdbDetails? d = null;
                Exception? err = null;
                try { d = await tm.DetailsAsync(i, ct: ct).ConfigureAwait(false); }
                catch (Exception e) when (e is not OperationCanceledException) { err = e; }
                lock (gate)
                {
                    if (++done % 20 == 0) Progress("Fetching plots & details from TMDB", done, list.Count);
                    if (err != null)
                    {
                        if (++errors == 1) Log($"TMDB error: {err.Message}");
                        return;
                    }
                    batch.Add((i, d));
                    if (batch.Count >= 50)
                    {
                        SaveTmdb(batch);
                        batch.Clear();
                    }
                }
            });
        }
        finally { SaveTmdb(batch); }
        Log($"TMDB details fetched for {list.Count - errors} titles" + (errors > 0 ? $" ({errors} errors)" : ""));
        FetchPosters(retry: true); // titles without an IMDb-side poster may have one on TMDB
    }

    static string JsonList<T>(IEnumerable<T> v) => JsonSerializer.Serialize(v, Tmdb.JsonOptions);

    void SaveTmdb(List<(int Tc, TmdbDetails? D)> batch)
    {
        if (batch.Count == 0) return;
        lock (Lock)
        {
            using var tx = db.BeginTransaction();
            foreach (var (tc, det) in batch)
            {
                var d = det ?? new TmdbDetails();
                Exec("UPDATE titles SET tmdb_done=1, tmdb_id=?, plot=?, tagline=?, orig_lang=?, languages=?, countries=?, " +
                     "fa_title=coalesce(?, fa_title), poster_url=coalesce(poster_url, ?), backdrop_url=?, tmdb_cast=?, collection=? WHERE tconst=?",
                    d.TmdbId, d.Plot, d.Tagline, d.OrigLang, JsonList(d.Languages), JsonList(d.Countries), FaNormalizer.Normalize(d.FaTitle),
                    d.PosterUrl, d.BackdropUrl, JsonList(d.TmdbCast), d.Collection, tc);
            }
            tx.Commit();
        }
        Version++;
    }

    // ---------- Rotten Tomatoes / Metacritic ----------
    /// <summary>Wikidata for everything (refreshed monthly unless <paramref name="force"/>), then OMDb (if a key is set) for titles still missing.</summary>
    public void FetchScores(bool force = false)
    {
        string q = "SELECT tconst FROM titles WHERE type IS NOT NULL";
        if (!force) q += " AND (scores_at IS NULL OR scores_at < datetime('now', '-30 days'))";
        var ids = OnlineQuery(q + " ORDER BY votes DESC", r => r.GetInt32(0));
        int got = 0;
        for (int n = 0; n < ids.Count; n += 250)
        {
            Progress("Getting Rotten Tomatoes & Metacritic scores (Wikidata)", n, ids.Count);
            var batch = ids.GetRange(n, Math.Min(250, ids.Count - n));
            Dictionary<int, CriticScores> res;
            try
            {
                res = CriticSources.WikidataScoresAsync(Net, batch, StopToken).GetAwaiter().GetResult();
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                Log($"Wikidata not reachable ({e.Message}); scores will be retried next update");
                break;
            }
            lock (Lock)
            {
                using var tx = db.BeginTransaction();
                foreach (var t in batch)
                {
                    res.TryGetValue(t, out var s);
                    Exec("UPDATE titles SET rt=coalesce(?, rt), mc=coalesce(?, mc), scores_at=? WHERE tconst=?", s?.Rt, s?.Mc, Now(), t);
                }
                tx.Commit();
            }
            got += res.Count;
            Version++;
            Net.Sleep(0.5, StopToken).GetAwaiter().GetResult();
        }
        if (ids.Count > 0) Log($"Critic scores: found for {got} of {ids.Count} titles on Wikidata");
        FetchOmdb();
    }

    /// <summary>Asks OMDb for the scores Wikidata lacks (needs an OMDb key; the free tier allows 1000 titles a day, the rest continue next time).</summary>
    public void FetchOmdb()
    {
        var key = LoadSettings().OmdbKey;
        if (string.IsNullOrEmpty(key)) return;
        var ids = OnlineQuery("SELECT tconst FROM titles WHERE type IS NOT NULL AND omdb_done=0 AND (rt IS NULL OR mc IS NULL) ORDER BY votes DESC", r => r.GetInt32(0));
        int got = 0;
        for (int n = 0; n < ids.Count; n++)
        {
            if (n % 10 == 0) Progress("Getting missing scores from OMDb", n, ids.Count);
            CriticScores res;
            try
            {
                res = CriticSources.OmdbScoresAsync(Net, key, ids[n], StopToken).GetAwaiter().GetResult();
            }
            catch (LimitReachedException)
            {
                Log($"OMDb daily limit reached after {n} titles; the rest continue on the next update");
                break;
            }
            catch (InvalidApiKeyException e)
            {
                Log($"OMDb: {e.Message}. Check the key in Settings.");
                break;
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                Log($"OMDb not reachable ({e.Message})");
                break;
            }
            lock (Lock) Exec("UPDATE titles SET rt=coalesce(rt, ?), mc=coalesce(mc, ?), omdb_done=1 WHERE tconst=?", res.Rt, res.Mc, ids[n]);
            if (res.Any) got++;
        }
        Version++;
        if (ids.Count > 0) Log($"OMDb added scores for {got} titles");
    }

    // ---------- key tests (Settings 'Test & save') ----------
    public async Task<KeyTest> TestTmdbKeyAsync(string key, CancellationToken ct = default)
    {
        try { return new KeyTest(await new Tmdb(key, Net).CheckAsync(ct).ConfigureAwait(false), ""); }
        catch (Exception e) when (e is not OperationCanceledException) { return new KeyTest(false, e.Message); }
    }

    public async Task<KeyTest> TestOmdbKeyAsync(string key, CancellationToken ct = default)
    {
        try
        {
            var (ok, msg) = await CriticSources.OmdbCheckAsync(Net, (key ?? "").Trim(), ct).ConfigureAwait(false);
            return new KeyTest(ok, msg);
        }
        catch (Exception e) when (e is not OperationCanceledException) { return new KeyTest(false, e.Message); }
    }

    /// <summary>Blocking; call it off the UI thread (it makes one web request).</summary>
    public KeyTest TestTmdbKey(string key) => TestTmdbKeyAsync(key).GetAwaiter().GetResult();

    /// <summary>Blocking; call it off the UI thread (it makes one web request).</summary>
    public KeyTest TestOmdbKey(string key) => TestOmdbKeyAsync(key).GetAwaiter().GetResult();
}
