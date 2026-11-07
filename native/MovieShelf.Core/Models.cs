using System.Globalization;
using System.Text;
using System.Text.Json;

namespace MovieShelf.Core;

public sealed class MovieFile
{
    public int Id;
    public string Raw = "";
    public string? Res, Src, Codec, Audio, Ed, Grp, Conf, List;
    public bool B10, Hdr, Fa, Dual, Sub;
    public long? Size;
    public List<string> Paths = new();
    // ffprobe facts of the first scanned path (Python: dur, w, h, vc, br, alang, slang); empty when the file was not probed
    public int? Dur, W, H;               // seconds, picture size
    public string? VCodec;
    public long? Br;                     // bits per second
    public string[] ALang = Array.Empty<string>(), SLang = Array.Empty<string>();
}

public sealed class Person
{
    public int Id;
    public string Name = "";
    public string? Role;
}

/// <summary>One movie (or series) of the library: its files grouped by IMDb id, with IMDb and user data.</summary>
public sealed class Movie
{
    public string K = "";                // 'tt0000001' for a matched movie, 'f12' for an unmatched file
    public int? Tconst;
    public string? Type;
    public string Title = "", PTitle = "";
    public string? En, Orig, FaTitle, PAlt, Plot, Tagline, OrigLang, Collection, Notes, WatchedAt;
    public int? Year, EndYear, Runtime, PYear, Rt, Mc, MyRating;
    public double? Rating;
    public int Votes;
    public string[] Genres = Array.Empty<string>(), Akas = Array.Empty<string>(), Countries = Array.Empty<string>(), Langs = Array.Empty<string>();
    public List<Person> Dir = new(), Wri = new(), Cast = new();
    public string[] TmdbCast = Array.Empty<string>();    // names only: the search needs them
    public string? Backdrop;             // TMDB backdrop image address
    public string? CrewJson, TmdbCastJson;   // the stored columns; only read by Crew and TmdbCastMembers, so loading the library stays fast
    public List<MovieFile> Files = new();
    public string? Res;                  // best resolution
    public string Conf = "none";
    public bool HasPoster, Fav, Watched;

    // search helpers, filled by Prepare()
    public string Hay = "", TKey = "", FDir = "", FWri = "", FCast = "", FTitle = "", FFile = "";
    public int? Decade;

    public bool IsSeries => Type is "tvSeries" or "tvMiniSeries";
    public string Display => En ?? Title;
    public string PosterKey => K;
    public string YearSpan => Year == null ? "" : IsSeries && EndYear != null && EndYear != Year ? $"{Year}–{EndYear}" : Year.ToString()!;
    public bool NeedsReview => Matcher.Unsure.Contains(Conf);

    List<Person>? crew;
    List<TmdbCastMember>? tmdbMembers;
    string? fAudio, fSubs;

    /// <summary>Everyone else on the film; <see cref="Person.Role"/> is the job. Parsed on first use.</summary>
    public List<Person> Crew => crew ??= ParsePeople(CrewJson);

    /// <summary>The TMDB cast with character and photo address (<see cref="TmdbCastMember.Img"/>). Parsed on first use.</summary>
    public List<TmdbCastMember> TmdbCastMembers => tmdbMembers ??= ParseCastMembers(TmdbCastJson);

    /// <summary>Folded audio languages of all files (the audio: search field). Built on first use.</summary>
    public string FAudio => fAudio ??= Fold(string.Join(" | ", Files.SelectMany(f => f.ALang)));

    /// <summary>Folded subtitle languages of all files (the sub: search field). Built on first use.</summary>
    public string FSubs => fSubs ??= Fold(string.Join(" | ", Files.SelectMany(f => f.SLang)));

    /// <summary>True when the person directed, wrote, acted in or worked on this movie (the Python app's pids).
    /// The crew column is only parsed when <paramref name="idText"/>, the id as text, is in it: a pass over the library stays fast and leaves the crews unparsed.</summary>
    public bool HasPerson(int nconst, string? idText = null)
    {
        foreach (var p in Dir) if (p.Id == nconst) return true;
        foreach (var p in Wri) if (p.Id == nconst) return true;
        foreach (var p in Cast) if (p.Id == nconst) return true;
        return CrewJson != null && CrewJson.Contains(idText ?? nconst.ToString(), StringComparison.Ordinal) && Crew.Exists(p => p.Id == nconst);
    }

    /// <summary>The app.js fold: no accents or apostrophes, one spelling of the Persian letters ye and kaf, no half-space, Latin digits.</summary>
    public static string Fold(string? s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        var d = s.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(d.Length);
        foreach (var c in d)
        {
            if (c is '’' or '\'' or '`' or '´' or '\u200C' || CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark) continue;
            sb.Append(c switch
            {
                'ي' => 'ی',
                'ك' => 'ک',
                >= '۰' and <= '۹' => (char)('0' + (c - '۰')),
                _ => c,
            });
        }
        return sb.ToString().ToLowerInvariant();
    }

    static string Names(IEnumerable<Person> l) => Fold(string.Join(" | ", l.Select(p => p.Name)));

    public void Prepare()
    {
        string people = string.Join(' ', Dir.Concat(Wri).Concat(Cast).Select(p => p.Name));
        string chars = string.Join(' ', Cast.Select(p => p.Role ?? ""));
        Hay = Fold(string.Join(" | ", new[]
        {
            Title, En, Orig, FaTitle, string.Join(' ', Akas), PTitle, PAlt, Year?.ToString(), Tconst == null ? null : K,
            people, chars, string.Join(' ', TmdbCast), string.Join(' ', Files.Select(f => f.Raw)),
            string.Join(' ', Genres), string.Join(' ', Countries), Collection,
        }.Select(x => x ?? "")));
        TKey = Fold(En ?? Title);
        FDir = Names(Dir);
        FWri = Names(Wri);
        FCast = Fold(string.Join(" | ", Cast.Select(p => p.Name).Concat(TmdbCast)));
        FTitle = Fold(string.Join(" | ", new[] { Title, En, Orig, FaTitle }.Concat(Akas).Select(x => x ?? "")));
        FFile = Fold(string.Join(" | ", Files.Select(f => f.Raw)));
        Decade = Year == null ? null : Year / 10 * 10;
    }

    public static List<Person> ParsePeople(string? json)
    {
        var list = new List<Person>();
        if (string.IsNullOrEmpty(json)) return list;
        try
        {
            using var doc = JsonDocument.Parse(json);
            foreach (var e in doc.RootElement.EnumerateArray())
            {
                if (e.ValueKind != JsonValueKind.Array || e.GetArrayLength() < 2) continue;
                list.Add(new Person
                {
                    Id = e[0].ValueKind == JsonValueKind.Number ? e[0].GetInt32() : 0,
                    Name = e[1].ValueKind == JsonValueKind.String ? e[1].GetString()! : "",
                    Role = e.GetArrayLength() > 2 && e[2].ValueKind == JsonValueKind.String ? e[2].GetString() : null,
                });
            }
        }
        catch (Exception e) when (e is JsonException or InvalidOperationException) { }   // text that is no list must not stop the library loading
        return list;
    }

    public static string[] ParseStrings(string? json)
    {
        if (string.IsNullOrEmpty(json)) return Array.Empty<string>();
        try
        {
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.EnumerateArray().Where(e => e.ValueKind == JsonValueKind.String).Select(e => e.GetString()!).ToArray();
        }
        catch (Exception e) when (e is JsonException or InvalidOperationException) { return Array.Empty<string>(); }
    }

    public static string[] ParseCastNames(string? json)
    {
        if (string.IsNullOrEmpty(json)) return Array.Empty<string>();
        try
        {
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.EnumerateArray()
                .Where(e => e.ValueKind == JsonValueKind.Object && e.TryGetProperty("name", out var n) && n.ValueKind == JsonValueKind.String)
                .Select(e => e.GetProperty("name").GetString()!).ToArray();
        }
        catch (Exception e) when (e is JsonException or InvalidOperationException) { return Array.Empty<string>(); }
    }

    public static List<TmdbCastMember> ParseCastMembers(string? json)
    {
        if (string.IsNullOrEmpty(json)) return new();
        try { return JsonSerializer.Deserialize<List<TmdbCastMember>>(json, Tmdb.JsonOptions) ?? new(); }
        catch (JsonException) { return new(); }
    }
}

/// <summary>A match that needs a human check.</summary>
public sealed class MatchProblem
{
    public int FileId;
    public string Raw = "", Conf = "", Reason = "";
    public string? Title, Alt;
    public int? Year;
    public Candidate2? Current;
    public List<Candidate2> Cands = new();
    /// <summary>Where the video files of this list entry are, for "Show in folder".</summary>
    public List<string> Paths = new();
}

public sealed class Stats
{
    public int Files, Matched, Titles, Problems, Duplicates;
    public bool Index;
}
