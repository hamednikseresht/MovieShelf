using System.Text.RegularExpressions;

namespace MovieShelf.Core;

/// <summary>What "Add movie" did: the line written to the list, and whether the movie was in the library already.</summary>
public sealed record AddedMovie(int Tconst, string Raw, string Title, int? Year, bool AlreadyThere);

public sealed partial class Library
{
    /// <summary>The IMDb number in what the user typed: a link, "tt0111161" or just "111161". Null when there is none.</summary>
    public static int? ParseImdbId(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var m = Regex.Match(text, @"tt(\d{1,9})", RegexOptions.IgnoreCase);
        if (!m.Success) m = Regex.Match(text.Trim(), @"^(\d{1,9})$");
        return m.Success && int.TryParse(m.Groups[1].Value, out int id) && id > 0 ? id : null;
    }

    /// <summary>Name and year of an IMDb title: from the library, else the offline index, else IMDb's online suggestions. Blocking: call it off the UI thread.</summary>
    (string Title, int? Year)? ResolveImdb(int tconst)
    {
        lock (Lock)
        {
            using var rd = Reader();
            using var c = Cmd(rd, "SELECT coalesce(en_title, title), year FROM titles WHERE tconst=? AND title IS NOT NULL", new object?[] { tconst });
            using var r = c.ExecuteReader();
            if (r.Read() && !r.IsDBNull(0)) return (r.GetString(0), r.IsDBNull(1) ? null : r.GetInt32(1));
        }
        if (Matcher.HasIndex)
        {
            using var ix = Matcher.Open();
            using var c = Cmd(ix, "SELECT title, year FROM titles WHERE id=?", new object?[] { tconst });
            using var r = c.ExecuteReader();
            if (r.Read() && !r.IsDBNull(0)) return (r.GetString(0), r.IsDBNull(1) ? null : r.GetInt32(1));
        }
        try
        {
            var hit = ImdbOnline.SuggestAsync(Net, $"tt{tconst:0000000}").GetAwaiter().GetResult().FirstOrDefault(s => s.Tconst == tconst && !string.IsNullOrWhiteSpace(s.Title));
            return hit == null ? null : (hit.Title!, hit.Year);
        }
        catch (HttpRequestException)
        {
            throw new InvalidOperationException($"tt{tconst:0000000} is not in the offline IMDb index and IMDb could not be reached. Check your internet and try again.");
        }
    }

    /// <summary>"Add movie": puts one IMDb title in the library by its link or number. Its name goes to added.txt like any list name, the IMDb choice is
    /// kept as a manual match, and the check that follows fetches its details and poster. Blocking until the line is written; the rest is a job.</summary>
    /// <exception cref="InvalidOperationException">IMDb has no such title, or the index is missing.</exception>
    public AddedMovie AddByImdb(int tconst)
    {
        var found = ResolveImdb(tconst) ?? throw new InvalidOperationException($"IMDb has no title tt{tconst:0000000}. Check the link or number.");
        string name = Regex.Replace(found.Title, @"[\r\n\t]+", " ").Trim();
        string raw = found.Year is > 0 ? $"{name} ({found.Year})" : name;
        bool there;
        lock (Lock) there = Scalar<int>("SELECT count(*) FROM files WHERE present=1 AND tconst=?", tconst) > 0;
        if (there) return new AddedMovie(tconst, raw, found.Title, found.Year, true);
        bool taken;
        lock (Lock) taken = Scalar<int>("SELECT count(*) FROM files WHERE present=1 AND raw=?", raw) > 0;
        if (taken) raw += $" tt{tconst:0000000}";   // another movie already has this exact line: the number keeps them apart
        AddFromCheck(new[] { new AddItem(raw, true, tconst) }, update: false);
        StartRescan();
        StartOrQueue(() => RefreshTitle(tconst));
        return new AddedMovie(tconst, raw, found.Title, found.Year, false);
    }

    /// <summary>"Revise": gives every file of a movie another IMDb title (it sat on the wrong one of two look-alikes, say) and fetches that title's details.
    /// Blocking: call it off the UI thread.</summary>
    public void ReviseMovie(IEnumerable<int> fileIds, int tconst)
    {
        var ids = fileIds.ToList();
        if (ids.Count == 0) return;
        bool needs = false;
        foreach (int id in ids) needs |= SetMatch(id, tconst);
        if (needs) StartOrQueue(() => RefreshTitle(tconst));
    }
}
