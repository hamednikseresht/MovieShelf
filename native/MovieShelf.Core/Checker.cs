using System.Text;
using System.Text.RegularExpressions;

namespace MovieShelf.Core;

/// <summary>One line of a list to check: a release name, optionally with the IMDb id (CSV) or the file it came from (folder).</summary>
public sealed class ListEntry
{
    public string Raw = "";
    public int? Tconst;
    public string? Path;
    public long? Size;
    public bool Episode;
}

/// <summary>The facts the quality comparison uses; made from a parsed name or a files row.</summary>
public sealed record Quality(string? Resolution, string? Source, string? Codec, bool Hdr, bool Bit10, string? Audio, bool FarsiDubbed, string? Edition)
{
    public static Quality From(ParsedName p) => new(p.Resolution, p.Source, p.Codec, p.Hdr, p.Bit10, p.Audio, p.FarsiDubbed, p.Edition);
}

/// <summary>List check helpers: read a list of movies from a .txt / .csv file or a folder of video files and rank release quality. Port of checker.py.</summary>
public static class Checker
{
    public static readonly HashSet<string> VideoExt = new()
    {
        ".mkv", ".mp4", ".avi", ".m4v", ".mov", ".wmv", ".ts", ".m2ts", ".mpg", ".mpeg", ".webm",
        ".flv", ".iso", ".vob", ".divx", ".ogm", ".rmvb", ".3gp",
    };
    // a list line that names one of these is a subtitle, picture or other side file of a movie, not a movie
    // ('.sub' is left out on purpose: 'Movie.2025.WEB-DL.Farsi.Sub' is a release name)
    public static readonly HashSet<string> SideExt = new()
    {
        ".srt", ".idx", ".ass", ".ssa", ".vtt", ".sup", ".smi", ".sami", ".nfo", ".jpg", ".jpeg", ".png",
        ".gif", ".webp", ".url", ".sfv", ".md5", ".txt", ".db", ".ini", ".exe", ".zip", ".rar", ".7z",
    };
    public static readonly HashSet<string> DiscDirs = new() { "video_ts", "bdmv" };   // DVD / Blu-ray folder structures: the parent folder is the movie
    public static readonly Regex ExtraRe = new(@"(^|[ ._\-\[(])(sample|trailer|teaser|featurette|extras?|behind[ ._]the[ ._]scenes)([ ._\-\])]|$)", RegexOptions.IgnoreCase);
    public static readonly Regex EpisodeRe = new(@"\bS\d{1,2}[ ._]?E\d{1,3}\b|\b\d{1,2}x\d{2}\b", RegexOptions.IgnoreCase);
    public static readonly Regex YearIn = new(@"(?<![0-9])(18[89][0-9]|19[0-9]{2}|20[0-4][0-9])(?![0-9])");

    static readonly Dictionary<string, int> ResRank = new() { ["2160p"] = 5, ["1080p"] = 4, ["720p"] = 3, ["576p"] = 2, ["480p"] = 1, ["SD"] = 1 };
    static readonly Dictionary<string, int> SrcRank = new() { ["BluRay"] = 5, ["WEB-DL"] = 4, ["WEBRip"] = 3, ["HDTV"] = 2, ["HDRip"] = 2, ["DVDRip"] = 1 };
    static readonly Dictionary<string, int> ChRank = new() { ["7.1"] = 3, ["5.1"] = 2, ["2.0"] = 1 };

    static Checker() => Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

    /// <summary>True for a list line like 'Heat.1995.1080p.srt': a subtitle or other file that sits beside a movie.</summary>
    public static bool IsSideFile(string name) => SideExt.Contains(FolderUtil.SplitExt(name.Trim().Trim('"')).Ext.ToLowerInvariant());

    /// <summary>The text of a list file: UTF-8 (with or without BOM), UTF-16 with BOM, else Windows Arabic (cp1256), else latin-1.</summary>
    public static string ReadText(string path)
    {
        var data = File.ReadAllBytes(path);
        try
        {
            int skip = data.Length >= 3 && data[0] == 0xEF && data[1] == 0xBB && data[2] == 0xBF ? 3 : 0;
            return new UTF8Encoding(false, true).GetString(data, skip, data.Length - skip);
        }
        catch (DecoderFallbackException) { }
        if (data.Length >= 2 && ((data[0] == 0xFF && data[1] == 0xFE) || (data[0] == 0xFE && data[1] == 0xFF)))
        {
            try { return new UnicodeEncoding(data[0] == 0xFE, false, true).GetString(data, 2, data.Length - 2); }
            catch (DecoderFallbackException) { }
        }
        try { return Encoding.GetEncoding(1256, EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback).GetString(data); }
        catch (Exception ex) when (ex is DecoderFallbackException or ArgumentException) { }
        return Encoding.Latin1.GetString(data);
    }

    /// <summary>One entry per line; a CSV gets its name / title+year / IMDb id columns detected.</summary>
    public static List<ListEntry> EntriesFromText(string text, string name = "")
    {
        var lines = FolderUtil.SplitLines(text).Where(l => l.Trim().Length > 0).ToList();
        if (lines.Count == 0) return new();
        bool isCsv = name.EndsWith(".csv", StringComparison.OrdinalIgnoreCase) || name.EndsWith(".tsv", StringComparison.OrdinalIgnoreCase);
        if (!isCsv)
        {
            string head = lines[0].ToLowerInvariant();
            isCsv = head.IndexOfAny(new[] { ',', ';', '\t' }) >= 0 && new[] { "title", "name", "imdb", "file" }.Any(head.Contains);
        }
        if (!isCsv) return lines.Where(l => !IsSideFile(l)).Select(l => new ListEntry { Raw = l.Trim().Trim('"') }).ToList();
        var rows = ListCsv.Read(string.Join("\n", lines), ListCsv.Sniff(lines[0]));
        var header = rows[0].Select(h => h.Trim().ToLowerInvariant()).ToList();
        int? Col(params string[] names)
        {
            foreach (var n in names)
                for (int i = 0; i < header.Count; i++)
                    if (header[i] == n || header[i].Replace(" ", "") == n) return i;
            foreach (var n in names)
                for (int i = 0; i < header.Count; i++)
                    if (header[i].Contains(n)) return i;
            return null;
        }
        int? cName = Col("raw", "release", "filename", "file", "name", "path");
        int? cTitle = Col("title", "movie", "english title", "original title");
        int? cYear = Col("year", "release year");
        int? cImdb = Col("imdb", "tconst", "imdb id", "imdbid", "const");
        List<List<string>> data;
        if (cName == null && cTitle == null && cImdb == null) { cName = 0; data = rows; }   // no header: first column holds the names
        else data = rows.Skip(1).ToList();
        var outList = new List<ListEntry>();
        foreach (var r in data)
        {
            string G(int? i) => i != null && i < r.Count ? r[i.Value].Trim() : "";
            string raw = G(cName);
            if (raw.Length > 0)
            {
                var (stem, ext) = FolderUtil.SplitExt(FolderUtil.BaseName(raw));
                if (VideoExt.Contains(ext.ToLowerInvariant())) raw = stem;
            }
            else if (G(cTitle).Length > 0)
                raw = G(cTitle) + (G(cYear).Length > 0 && !G(cTitle).Contains(G(cYear)) ? $" {G(cYear)}" : "");
            var m = Regex.Match(G(cImdb), @"tt\d{5,}");
            if (IsSideFile(raw)) continue;
            if (raw.Length > 0 || m.Success)
                outList.Add(new ListEntry { Raw = raw.Length > 0 ? raw : m.Value, Tconst = m.Success ? int.Parse(m.Value[2..]) : null });
        }
        return outList;
    }

    /// <summary>Video files under root (recursive). Uses the folder name when the file name is generic (no year) or the movie is a DVD / Blu-ray folder.</summary>
    public static List<ListEntry> EntriesFromFolder(string root, Action<string, long, long>? progress = null)
    {
        var outList = new List<ListEntry>();
        var seenDisc = new HashSet<string>();
        int n = 0;
        char sep = Path.DirectorySeparatorChar;
        foreach (var (dirpath, dirs, files) in FolderUtil.Walk(root))
        {
            dirs.RemoveAll(d => d.StartsWith('.') || d.StartsWith('$') || d.ToLowerInvariant() is "extras" or "featurettes" or "samples" or "sample");
            var parts = dirpath.ToLowerInvariant().Replace('\\', '/').Split('/');
            int disc = Array.FindIndex(parts, p => DiscDirs.Contains(p));
            if (disc >= 0)
            {
                string movieDir = string.Join(sep, dirpath.Replace('/', sep).Split(sep).Take(disc));
                if (seenDisc.Add(movieDir))
                    outList.Add(new ListEntry { Raw = FolderUtil.BaseName(movieDir), Path = movieDir, Size = FolderUtil.DirSize(movieDir) });
                dirs.Clear();
                continue;
            }
            foreach (var fn in files)
            {
                var (stem, ext) = FolderUtil.SplitExt(fn);
                if (!VideoExt.Contains(ext.ToLowerInvariant())) continue;
                string path = Path.Combine(dirpath, fn);
                long? size;
                try { size = new FileInfo(path).Length; }
                catch (IOException) { size = null; }
                if (ExtraRe.IsMatch(stem) && (size ?? 0) < 1_500_000_000) continue;
                string raw = stem;
                string parent = FolderUtil.BaseName(dirpath);
                if (!YearIn.IsMatch(stem) && YearIn.IsMatch(parent) && Path.TrimEndingDirectorySeparator(Path.GetFullPath(dirpath)) != Path.TrimEndingDirectorySeparator(Path.GetFullPath(root)))
                    raw = parent;   // 'movie.mkv' inside 'Heat (1995) 1080p'
                outList.Add(new ListEntry { Raw = raw, Path = path, Size = size, Episode = EpisodeRe.IsMatch(stem) });
                n++;
                if (n % 200 == 0) progress?.Invoke($"Reading folder ({n} videos)", n, 0);
            }
        }
        return outList;
    }

    /// <summary>&gt;0 when file a is better than b, &lt;0 when worse, 0 when equal or unknown. Only facts known for both files count
    /// (a name without a source is not 'worse').</summary>
    public static int CompareQuality(Quality a, Quality b)
    {
        int ra = ResRank.GetValueOrDefault(a.Resolution ?? ""), rb = ResRank.GetValueOrDefault(b.Resolution ?? "");
        if (ra == 0 || rb == 0) return 0;
        if (ra != rb) return ra - rb;
        int sa = SrcRank.GetValueOrDefault(a.Source ?? ""), sb = SrcRank.GetValueOrDefault(b.Source ?? "");
        if (sa != 0 && sb != 0 && sa != sb) return sa - sb;
        if (a.Hdr != b.Hdr) return a.Hdr ? 1 : -1;
        if (a.Bit10 != b.Bit10) return a.Bit10 ? 1 : -1;
        int ca = ChRank.GetValueOrDefault(a.Audio ?? ""), cb = ChRank.GetValueOrDefault(b.Audio ?? "");
        if (ca != 0 && cb != 0 && ca != cb) return ca - cb;
        return 0;
    }

    /// <summary>'1080p BluRay x264 HDR 10-bit 5.1 Farsi dubbed Extended' - the parts that are known.</summary>
    public static string QualityLabel(Quality f)
    {
        var bits = new[]
        {
            string.IsNullOrEmpty(f.Resolution) ? "?" : f.Resolution, f.Source, f.Codec, f.Hdr ? "HDR" : null, f.Bit10 ? "10-bit" : null,
            f.Audio, f.FarsiDubbed ? "Farsi dubbed" : null, f.Edition,
        };
        return string.Join(' ', bits.Where(b => !string.IsNullOrEmpty(b)));
    }
}
