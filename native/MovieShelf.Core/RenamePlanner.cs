using System.Text;
using System.Text.RegularExpressions;

namespace MovieShelf.Core;

/// <summary>One proposed rename: a movie's video file ("file"), a subtitle or other file beside it with the same name ("side"), or the movie's own
/// folder ("folder"). Conflict = two items would get the same new path, or the new name is taken on disk (<see cref="Why"/> says which);
/// Same = it already has that name.</summary>
public sealed class RenameRow
{
    public string Kind = "file", K = "", Path = "", Old = "", New = "", NewStem = "", Target = "", Why = "";
    public bool Same, Conflict;
    /// <summary>The Status column of the CSV.</summary>
    public string Status => Same ? "already named" : Conflict ? "conflict: " + Why : "rename";
}

/// <summary>The counts and the first 300 renames the Rename plan dialog shows (/api/rename-plan). <see cref="Folders"/>: folders among the renames.</summary>
public sealed record RenameSummary(int Total, int Todo, int Conflicts, int Same, List<RenameRow> Sample, int Folders = 0);

/// <summary>A file the plan produces: name to offer in the save dialog, content type and bytes.</summary>
public sealed record PlanFileData(string Name, string ContentType, byte[] Data);

/// <summary>The rename plan: proposed names from the user's pattern (by default `Title (Year) [1080p BluRay]`) for the files whose location is known,
/// their subtitles and their folders, written out as a batch file, a CSV and a list of new names. All read-only: nothing here renames a file.
/// Port of the rename part of extras.py.</summary>
public static class RenamePlanner
{
    public const string DefaultPattern = "{title} ({year}) [{res} {src}]";

    /// <summary>The fields a pattern can use, with what each one gives (shown on the field buttons).</summary>
    public static readonly (string Key, string Tip)[] Fields =
    {
        ("title", "the movie title (English if known)"), ("year", "release year"), ("res", "resolution, like 1080p"),
        ("src", "source, like BluRay"), ("codec", "video codec, like x265"), ("dub", "\"Farsi dubbed\" when it is"),
        ("edition", "edition, like Extended"), ("group", "release group"), ("imdb", "IMDb id, like tt0113277"),
    };

    // subtitle and side files that travel with a video ('.sub' too: here it is a file ending)
    static readonly HashSet<string> SideExt = new(Checker.SideExt) { ".sub" };
    static readonly Regex BadName = new(@"[<>:""/\\|?*\x00-\x1f]");
    static readonly Regex DigitColon = new(@"(?<=\d):(?=\d)");
    static readonly Regex Seps = new(@"\s*[:/\\]\s*");
    static readonly Regex Spaces = new(@"\s+");
    static readonly Regex Field = new(@"\{([^{}]*)\}");
    static readonly Regex InnerSpace = new(@"([(\[])\s+|\s+([)\]])");
    static readonly Regex EmptyBrackets = new(@"\(\s*\)|\[\s*\]");
    static readonly Regex EdgeDashes = new(@"(\s*[-_.]+\s*)+$|^(\s*[-_.]+\s*)+");
    static readonly Regex NotAlnum = new(@"[^a-z0-9]+");

    /// <summary>A safe Windows file name: '3:10 to Yuma' -> '3.10 to Yuma', 'Alien: Covenant' -> 'Alien - Covenant'.</summary>
    public static string Clean(string? s)
    {
        s = DigitColon.Replace(s ?? "", ".");
        s = Seps.Replace(s, " - ");
        return Spaces.Replace(BadName.Replace(s, ""), " ").Trim().Trim(' ', '.');
    }

    static string Norm(string? s) => NotAlnum.Replace((s ?? "").ToLowerInvariant(), "");

    /// <summary>Python os.path.normcase(os.path.normpath(p)): the form two paths are compared in.</summary>
    static string Low(string p)
    {
        if (p.Length == 0) return "";
        string s = p.Replace('/', '\\');
        try { if (System.IO.Path.IsPathFullyQualified(s)) s = System.IO.Path.GetFullPath(s); }
        catch (Exception e) when (e is ArgumentException or NotSupportedException or PathTooLongException) { }
        return System.IO.Path.TrimEndingDirectorySeparator(s).ToLowerInvariant();
    }

    /// <summary>"" when the pattern is usable, else what is wrong with it (shown to the user).</summary>
    public static string PatternProblem(string? pattern)
    {
        pattern = (pattern ?? "").Trim();
        if (pattern.Length == 0) return "The pattern is empty.";
        var names = Field.Matches(pattern).Select(m => m.Groups[1].Value).ToList();
        var bad = names.Where(n => !Fields.Any(f => f.Key == n)).Distinct().ToList();
        if (bad.Count > 0)
            return "Unknown: " + string.Join(", ", bad.Select(n => "{" + n + "}")) + ". Use " + string.Join(", ", Fields.Select(f => "{" + f.Key + "}")) + ".";
        if (!names.Contains("title") && !names.Contains("imdb")) return "The pattern needs {title} (or {imdb}), or every movie would get the same name.";
        string rest = Field.Replace(pattern, "");
        if (rest.Contains('{') || rest.Contains('}')) return "A { or } is not closed.";
        return "";
    }

    static string Cut150(string s) => s.Length > 150 ? s[..(char.IsHighSurrogate(s[149]) ? 149 : 150)] : s;   // Python cuts at 150 characters

    /// <summary>One movie file's new name (no extension) from the user's pattern; empty fields vanish with their brackets.</summary>
    static string NameOf(string pattern, Movie m, MovieFile f, string title)
    {
        var vals = new Dictionary<string, string>
        {
            ["title"] = title, ["year"] = m.Year is { } y and not 0 ? y.ToString() : "", ["res"] = f.Res ?? "", ["src"] = f.Src ?? "",
            ["codec"] = f.Codec ?? "", ["dub"] = f.Fa ? "Farsi dubbed" : "", ["edition"] = f.Ed ?? "", ["group"] = f.Grp ?? "",
            ["imdb"] = m.K.StartsWith("tt", StringComparison.Ordinal) ? m.K : "",
        };
        string s = Field.Replace(pattern, x => Clean(vals.GetValueOrDefault(x.Groups[1].Value, "")));
        s = InnerSpace.Replace(s, x => x.Groups[1].Value + x.Groups[2].Value);   // '[1080p ]' -> '[1080p]'
        s = EmptyBrackets.Replace(s, "");
        s = EdgeDashes.Replace(Spaces.Replace(s, " ").Trim(), "");
        return Cut150(Clean(s)).Trim(' ', '.');
    }

    /// <summary>(video stems, other file names) directly in folder d, or null when it cannot be read.</summary>
    static (List<string> Vids, List<string> Other)? Listing(string d, Dictionary<string, (List<string>, List<string>)?> cache)
    {
        if (!cache.TryGetValue(d, out var v))
        {
            try
            {
                var names = Directory.GetFiles(d).Select(n => System.IO.Path.GetFileName(n)).ToList();
                bool IsVid(string n) => Checker.VideoExt.Contains(FolderUtil.SplitExt(n).Ext.ToLowerInvariant());
                v = (names.Where(IsVid).Select(n => FolderUtil.SplitExt(n).Stem).ToList(), names.Where(n => !IsVid(n)).ToList());
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException) { v = null; }
            cache[d] = v;
        }
        return v;
    }

    /// <summary>Rows for every movie of the loaded movies (Library.LoadMovies()) whose location is known: the video file, the subtitle / nfo files
    /// beside it with the same name, and the movie's own folder (a folder named like the movie that holds that one movie; never a movie folder
    /// the user listed, never a disc folder). <paramref name="roots"/>: the movie folders of the settings.</summary>
    public static List<RenameRow> Plan(IEnumerable<Movie> movies, IEnumerable<string>? roots = null, string pattern = DefaultPattern)
    {
        var rootSet = (roots ?? Array.Empty<string>()).Where(r => r.Length > 0).Select(Low).ToHashSet();
        var rows = new List<RenameRow>();
        var cache = new Dictionary<string, (List<string>, List<string>)?>();
        var folders = new Dictionary<string, RenameRow>();   // folder path -> its row, so a folder is planned once
        foreach (var m in movies)
        {
            string title = Clean(string.IsNullOrEmpty(m.En) ? m.Title : m.En);
            if (title.Length == 0) continue;
            foreach (var f in m.Files)
                foreach (var path in f.Paths)
                {
                    string folder = FolderUtil.DirName(path), bname = FolderUtil.BaseName(path);
                    var (stem, ext) = FolderUtil.SplitExt(bname);
                    if (folder.Split('\\', '/').Any(x => Checker.DiscDirs.Contains(x.ToLowerInvariant()))) continue;   // a DVD / Blu-ray structure keeps its names
                    string nw = NameOf(pattern, m, f, title);
                    if (nw.Length == 0) continue;
                    bool same = string.Equals(stem, nw, StringComparison.OrdinalIgnoreCase);
                    rows.Add(new RenameRow { Kind = "file", K = m.K, Path = path, Old = bname, New = nw + ext, NewStem = nw, Target = System.IO.Path.Combine(folder, nw + ext), Same = same });
                    var lst = Listing(folder, cache);
                    if (lst is { } l)   // subtitles and other side files of this video: same name, longer ending (Movie.en.srt)
                    {
                        string pre = stem.ToLowerInvariant() + ".";
                        var longer = l.Vids.Where(v => v.Length > stem.Length && v.ToLowerInvariant().StartsWith(pre, StringComparison.Ordinal)).ToList();
                        foreach (var name in l.Other)
                        {
                            string ln = name.ToLowerInvariant();
                            if (SideExt.Contains(FolderUtil.SplitExt(name).Ext.ToLowerInvariant()) && name.Length > stem.Length + 1 && ln.StartsWith(pre, StringComparison.Ordinal)
                                && !longer.Any(v => ln.StartsWith(v.ToLowerInvariant() + ".", StringComparison.Ordinal)))
                            {
                                string suffix = name[stem.Length..];
                                rows.Add(new RenameRow
                                {
                                    Kind = "side", K = m.K, Path = System.IO.Path.Combine(folder, name), Old = name, New = nw + suffix, NewStem = nw,
                                    Target = System.IO.Path.Combine(folder, nw + suffix), Same = same,
                                });
                            }
                        }
                    }
                    string lf = Low(folder);
                    string? up = folder.Length > 0 ? System.IO.Path.GetDirectoryName(folder) : null;
                    if (folder.Length == 0 || up == null || rootSet.Contains(lf) || lf == Low(up) || (folders.TryGetValue(lf, out var had) && had.NewStem == nw))
                        continue;   // a movie folder the user listed, a drive root, or a folder already planned
                    string fname = FolderUtil.BaseName(folder);
                    bool alone = lst is { } l2 ? l2.Vids.Count == 1 : rows.Count(r => r.Kind == "file" && FolderUtil.DirName(r.Path) == folder) == 1;
                    string fn = Norm(NameParser.Parse(fname).Title);
                    bool sameName = string.Equals(f.Raw, fname, StringComparison.OrdinalIgnoreCase)
                        || (alone && (fn == Norm(NameParser.Parse(stem).Title) || fn == Norm(title)));
                    if (!sameName) continue;
                    if (folders.TryGetValue(lf, out var taken))   // two movies claim one folder: neither can have it
                    {
                        taken.Conflict = true;
                        taken.Why = "two movies share this folder";
                        continue;
                    }
                    var row = new RenameRow
                    {
                        Kind = "folder", K = m.K, Path = folder, Old = fname, New = nw, NewStem = nw, Target = System.IO.Path.Combine(up, nw),
                        Same = string.Equals(fname, nw, StringComparison.OrdinalIgnoreCase),
                    };
                    folders[lf] = row;
                    rows.Add(row);
                }
        }
        foreach (var g in rows.GroupBy(r => Low(r.Target)))
            if (g.Count() > 1 && g.Select(r => Low(r.Path)).Distinct().Count() > 1)
                foreach (var r in g.Where(r => !r.Same))
                {
                    r.Conflict = true;
                    r.Why = "two items would get this name";
                }
        foreach (var r in rows)
            if (!r.Same && !r.Conflict && Low(r.Target) != Low(r.Path) && System.IO.Path.Exists(r.Target))
            {
                r.Conflict = true;
                r.Why = "that name is already taken";
            }
        return rows;
    }

    /// <summary>What the Rename plan dialog shows first.</summary>
    public static RenameSummary Summary(List<RenameRow> rows)
    {
        var todo = rows.Where(r => !r.Same && !r.Conflict).ToList();
        return new RenameSummary(rows.Count, todo.Count, rows.Count(r => r.Conflict), rows.Count(r => r.Same), todo.Take(300).ToList(), todo.Count(r => r.Kind == "folder"));
    }

    static string BatEsc(string s) => s.Replace("%", "%%");

    /// <summary>A Windows batch file with one `ren` per item: side files and videos first, then the folders, innermost first (a folder's path changes
    /// when it is renamed). `if exist` keeps it from overwriting anything. Nothing runs until the user opens it.</summary>
    public static byte[] Bat(List<RenameRow> rows)
    {
        var o = new List<string> { "@echo off", "chcp 65001 >nul", "rem MovieShelf rename plan. Read it first, then run it. Lines starting with rem are skipped." };
        var todo = rows.Where(r => r.Kind != "folder").Concat(rows.Where(r => r.Kind == "folder")
            .OrderByDescending(r => System.IO.Path.TrimEndingDirectorySeparator(r.Path.Replace('/', '\\')).Split('\\').Length));
        foreach (var r in todo)
        {
            if (r.Same) continue;
            if (r.Conflict) { o.Add($"rem SKIPPED ({r.Why}): {BatEsc(r.Path)}"); continue; }
            o.Add($"if exist \"{BatEsc(r.Target)}\" (echo SKIPPED - the new name is taken) else ren \"{BatEsc(r.Path)}\" \"{BatEsc(r.New)}\"");
        }
        o.Add("echo Done.");
        o.Add("pause");
        return new UTF8Encoding(false).GetBytes(string.Join("\r\n", o) + "\r\n");
    }

    static readonly Dictionary<string, string> KindName = new() { ["file"] = "File", ["side"] = "Subtitle / side file", ["folder"] = "Folder" };

    public static byte[] Csv(List<RenameRow> rows)
    {
        var all = new List<List<object?>> { new() { "Type", "Folder", "Current name", "New name", "Status" } };
        foreach (var r in rows) all.Add(new() { KindName[r.Kind], FolderUtil.DirName(r.Path), r.Old, r.New, r.Status });
        return new UTF8Encoding(false).GetBytes(ListCsv.Bom + ListCsv.Write(all));
    }

    /// <summary>The new movie names (no extension), one per line: put these in your list in place of the old names. A movie whose folder is renamed
    /// is listed too, even when its file already had the right name.</summary>
    public static byte[] NameList(List<RenameRow> rows)
    {
        var names = rows.Where(r => r.Kind != "side" && !r.Same && !r.Conflict).Select(r => r.NewStem).Distinct();
        return new UTF8Encoding(false).GetBytes(ListCsv.Bom + string.Join("\r\n", names) + "\r\n");
    }

    /// <summary>The plan file for a kind: 'bat' | 'csv' | 'list' (/api/rename-plan/file). Null for an unknown kind.</summary>
    public static PlanFileData? PlanFile(string? kind, List<RenameRow> rows) => kind switch
    {
        "bat" => new("MovieShelf rename plan.bat", "application/octet-stream", Bat(rows)),
        "csv" => new("MovieShelf rename plan.csv", "text/csv; charset=utf-8", Csv(rows)),
        "list" => new("MovieShelf new names.txt", "text/plain; charset=utf-8", NameList(rows)),
        _ => null,
    };

    /// <summary>Write a plan file into a folder the user chose (the Python app hands it to the browser to download). Returns the path.</summary>
    public static string Save(string? kind, List<RenameRow> rows, string folder)
    {
        var f = PlanFile(kind, rows) ?? throw new ArgumentException("unknown file");
        string p = Path.Combine(folder, f.Name);
        System.IO.File.WriteAllBytes(p, f.Data);
        return p;
    }
}
