using System.Globalization;
using System.Text.Json;

namespace MovieShelf.Core;

/// <summary>What the person window shows apart from the person's movies in the library.</summary>
public sealed class PersonInfo
{
    public int Nconst;
    public string? Name;
    public int? Birth, Death;
    public List<string> Prof = new();
    /// <summary>A TMDB key is set.</summary>
    public bool Tmdb;
    /// <summary>The TMDB profile (null: no key, not fetched, or TMDB does not know the person).</summary>
    public TmdbPerson? Profile;
    /// <summary>A photo file exists: <see cref="Library.PersonPhotoPath"/>.</summary>
    public bool Photo;
    /// <summary>Why TMDB could not be reached, when it could not.</summary>
    public string? Error;
}

/// <summary>How many TMDB people profiles are fetched and how many a scan of each scope would still fetch.</summary>
public sealed record PeopleStatusInfo(bool Tmdb, int Fetched, int Photos, Dictionary<string, int> Todo);

public sealed partial class Library
{
    public string PeopleDir => Path.Combine(DataDir, "people");

    public string PersonPhotoPath(int nconst) => Path.Combine(PeopleDir, $"nm{nconst:0000000}.jpg");

    sealed record PersonRow(string? Data, bool Photo, string? Fetched);

    /// <summary>Asks TMDB for one person, keeps the photo file and the profile (table persons).</summary>
    async Task<PersonRow> FetchPersonAsync(Tmdb tm, int nconst, CancellationToken ct)
    {
        var p = await tm.PersonAsync(nconst, ct).ConfigureAwait(false);
        bool photo = false;
        if (!string.IsNullOrEmpty(p?.PhotoPath))
        {
            var data = await Net.GetBytesAsync(Tmdb.Img + "w342" + p!.PhotoPath, ct).ConfigureAwait(false);
            if (data.Length > 0)
            {
                Directory.CreateDirectory(PeopleDir);
                await File.WriteAllBytesAsync(PersonPhotoPath(nconst), data, ct).ConfigureAwait(false);
                photo = true;
            }
        }
        string? json = p == null ? null : JsonSerializer.Serialize(p, Tmdb.JsonOptions);
        string now = Now();
        lock (Lock) Exec("INSERT OR REPLACE INTO persons(nconst, tmdb_id, data, photo, fetched) VALUES (?,?,?,?,?)", nconst, p?.TmdbId, json, photo ? 1 : 0, now);
        return new PersonRow(json, photo, now);
    }

    /// <summary>Fetches (or refreshes) the TMDB profile and photo of one person and returns it (null: TMDB does not know the person).
    /// Blocking; needs a TMDB key.</summary>
    public TmdbPerson? FetchPerson(int nconst)
    {
        var key = LoadSettings().TmdbKey;
        if (string.IsNullOrEmpty(key)) throw new InvalidOperationException("Add a TMDB key in Settings first.");
        var row = FetchPersonAsync(new Tmdb(key, Net), nconst, StopToken).GetAwaiter().GetResult();
        return row.Data == null ? null : JsonSerializer.Deserialize<TmdbPerson>(row.Data, Tmdb.JsonOptions);
    }

    static IEnumerable<int> PeopleIds(string? json, int take = int.MaxValue)
    {
        if (string.IsNullOrEmpty(json)) yield break;
        using var doc = JsonDocument.Parse(json);
        if (doc.RootElement.ValueKind != JsonValueKind.Array) yield break;
        int n = 0;
        foreach (var e in doc.RootElement.EnumerateArray())
        {
            if (n++ >= take) yield break;
            if (e.ValueKind == JsonValueKind.Array && e.GetArrayLength() > 0 && e[0].TryGetInt32(out int id)) yield return id;
        }
    }

    /// <summary>People of the library without a fetched profile, by scope: 'directors' (directors and writers), 'main' (+ the first five
    /// billed actors), 'all' (everyone with a credit on your movies).</summary>
    public List<int> PeopleToScan(string scope)
    {
        var ids = new List<int>();
        var seen = new HashSet<int>();
        using var rd = Reader();
        using (var c = Cmd(rd, "SELECT directors, writers, cast_, crew FROM titles WHERE tconst IN (SELECT tconst FROM files WHERE present=1)", Array.Empty<object?>()))
        using (var r = c.ExecuteReader())
            while (r.Read())
            {
                string? S(int i) => r.IsDBNull(i) ? null : r.GetString(i);
                var groups = new List<IEnumerable<int>> { PeopleIds(S(0)), PeopleIds(S(1)) };
                if (scope is "main" or "all") groups.Add(PeopleIds(S(2), scope == "all" ? int.MaxValue : 5));
                if (scope == "all") groups.Add(PeopleIds(S(3)));
                foreach (var g in groups)
                    foreach (var id in g)
                        if (seen.Add(id)) ids.Add(id);
            }
        var done = new HashSet<int>();
        using (var c = Cmd(rd, "SELECT nconst FROM persons", Array.Empty<object?>()))
        using (var r = c.ExecuteReader())
            while (r.Read()) done.Add(r.GetInt32(0));
        return ids.Where(i => !done.Contains(i)).ToList();
    }

    /// <summary>Job: fetches TMDB profiles and photos for the people of your library, so profile windows open at once. Needs a TMDB key.</summary>
    public void FetchPeople(string scope = "main")
    {
        var key = LoadSettings().TmdbKey;
        if (string.IsNullOrEmpty(key)) throw new InvalidOperationException("Add a TMDB key in Settings first.");
        var ids = PeopleToScan(scope);
        if (ids.Count == 0)
        {
            Log("People: all profiles are already fetched");
            return;
        }
        var tm = new Tmdb(key, Net);
        Log($"People: fetching {ids.Count} profiles");
        int done = 0, errors = 0;
        var gate = new object();
        // a person takes two API calls and a photo; Tmdb paces all threads to the 50 a second limit
        OnlinePool(ids, 24, async (i, ct) =>
        {
            ct.ThrowIfCancellationRequested();
            Exception? err = null;
            try { await FetchPersonAsync(tm, i, ct).ConfigureAwait(false); }
            catch (Exception e) when (e is not OperationCanceledException) { err = e; }
            lock (gate)
            {
                if (err != null && ++errors == 1) Log($"TMDB error (people): {err.Message}");
                if (++done % 10 == 0) Progress("Fetching people profiles from TMDB", done, ids.Count);
            }
        });
        Version++;
        Log($"People: {ids.Count - errors} profiles fetched" + (errors > 0 ? $", {errors} failed" : ""));
    }

    public PeopleStatusInfo PeopleStatus()
    {
        int fetched, photos;
        using (var rd = Reader())
        {
            using (var c = Cmd(rd, "SELECT count(*) FROM persons WHERE data IS NOT NULL", Array.Empty<object?>())) fetched = Convert.ToInt32(c.ExecuteScalar());
            using (var c = Cmd(rd, "SELECT count(*) FROM persons WHERE photo=1", Array.Empty<object?>())) photos = Convert.ToInt32(c.ExecuteScalar());
        }
        return new PeopleStatusInfo(!string.IsNullOrEmpty(LoadSettings().TmdbKey), fetched, photos,
            new[] { "directors", "main", "all" }.ToDictionary(sc => sc, sc => PeopleToScan(sc).Count));
    }

    /// <summary>What the profile window shows apart from the person's movies in the library: IMDb facts from the offline data and, with a
    /// TMDB key, photo, biography, places and the full filmography (fetched once, kept 90 days; <paramref name="refresh"/> fetches again).
    /// Blocking when it has to ask TMDB.</summary>
    public PersonInfo PersonProfile(int nconst, bool refresh = false) => PersonProfileAsync(nconst, refresh, StopToken).GetAwaiter().GetResult();

    public async Task<PersonInfo> PersonProfileAsync(int nconst, bool refresh = false, CancellationToken ct = default)
    {
        var info = new PersonInfo { Nconst = nconst };
        PersonRow? cached = null;
        using (var rd = Reader())
        {
            using (var c = Cmd(rd, "SELECT name, birth, death, prof FROM people WHERE nconst=?", new object?[] { nconst }))
            using (var r = c.ExecuteReader())
                if (r.Read())
                {
                    info.Name = r.IsDBNull(0) ? null : r.GetString(0);
                    info.Birth = r.IsDBNull(1) ? null : (int)r.GetInt64(1);
                    info.Death = r.IsDBNull(2) ? null : (int)r.GetInt64(2);
                    info.Prof = (r.IsDBNull(3) ? "" : r.GetString(3)).Split(',', StringSplitOptions.RemoveEmptyEntries).ToList();
                }
            using (var c = Cmd(rd, "SELECT data, photo, fetched FROM persons WHERE nconst=?", new object?[] { nconst }))
            using (var r = c.ExecuteReader())
                if (r.Read()) cached = new PersonRow(r.IsDBNull(0) ? null : r.GetString(0), !r.IsDBNull(1) && r.GetInt64(1) != 0, r.IsDBNull(2) ? null : r.GetString(2));
        }
        var key = LoadSettings().TmdbKey;
        info.Tmdb = !string.IsNullOrEmpty(key);
        bool stale = cached == null || refresh
            || !DateTime.TryParseExact(cached.Fetched, "yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture, DateTimeStyles.None, out var at)
            || (DateTime.Now - at).TotalDays > 90;
        if (info.Tmdb && stale)
        {
            try { cached = await FetchPersonAsync(new Tmdb(key, Net), nconst, ct).ConfigureAwait(false); }
            catch (Exception e) when (e is not OperationCanceledException) { info.Error = $"{e.GetType().Name}: {e.Message}"; }
        }
        if (cached?.Data != null)
        {
            try { info.Profile = JsonSerializer.Deserialize<TmdbPerson>(cached.Data, Tmdb.JsonOptions); }
            catch (JsonException) { }
            info.Photo = cached.Photo && File.Exists(PersonPhotoPath(nconst));
        }
        return info;
    }
}
