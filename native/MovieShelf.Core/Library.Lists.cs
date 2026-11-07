using System.Text;

namespace MovieShelf.Core;

/// <summary>Where Load list reads names from: a path (a list file, a folder of lists or a folder of movies), or the text of a list the user picked.</summary>
public sealed record ListSource(string? Path = null, string? Text = null, string? Name = null);

/// <summary>What loading a list would do, before anything changes. The name lists hold the first 15; the counts hold all.</summary>
public sealed record ListPreview(int Lists, int Names, List<string> Repeated, int RepeatedN, List<string> Existing, int ExistingN,
    List<string> Similar, int SimilarN, int NewN);

/// <summary>What Load list did: list files written, names in the loaded list, old list files put aside ("Start a new list").</summary>
public sealed record ListLoad(int Lists, int Names, int Moved);

public sealed partial class Library
{
    static readonly string[] ListExt = { ".txt", ".csv", ".tsv" };
    /// <summary>files.list_file of a movie that comes from a movie folder, not from a list.</summary>
    public const string FolderSrc = "(movie folders)";
    const string NoNames = "No movie names found there. A list is a .txt file with one name per line, or a .csv with a name, title or IMDb id column.";

    /// <summary>Can the movie folders be read now? They are always a source of movies.</summary>
    public bool UseFolders() => LoadSettings().MediaDirs.Any(d => d.Length > 0 && Directory.Exists(d));

    static bool IsListFile(string p) => ListExt.Any(e => p.EndsWith(e, StringComparison.OrdinalIgnoreCase));

    /// <summary>[(name, entries)] from a list file (.txt .csv .tsv), a folder of such lists, or a folder of movies (the names of its video files);
    /// or from the text of a list the user picked.</summary>
    List<(string Name, List<ListEntry> Entries)> ListSources(ListSource src)
    {
        if (src.Text != null)
        {
            string name = Path.GetFileName(string.IsNullOrEmpty(src.Name) ? "list.txt" : src.Name);
            string stem = Path.GetFileNameWithoutExtension(name);
            return new() { (stem.Length > 0 ? stem : "list", Checker.EntriesFromText(src.Text, name)) };
        }
        string given = (src.Path ?? "").Trim().Trim('"');
        if (given.Length == 0) throw new ArgumentException("Choose a list file or a folder first.");
        string p = Path.GetFullPath(given);
        if (File.Exists(p))
        {
            if (!IsListFile(p)) throw new ArgumentException($"{p} is not a list. Use a .txt or .csv file, or a folder.");
            return new() { (Path.GetFileNameWithoutExtension(p), Checker.EntriesFromText(Checker.ReadText(p), p)) };
        }
        if (Directory.Exists(p))
        {
            var files = Directory.GetFiles(p).Where(f => IsListFile(f) && !Path.GetFileName(f).StartsWith('.')).OrderBy(f => f, StringComparer.Ordinal).ToList();
            if (files.Count > 0)
                return files.Select(f => (Path.GetFileNameWithoutExtension(f), Checker.EntriesFromText(Checker.ReadText(f), f))).ToList();
            string folder = FolderUtil.BaseName(p);
            return new() { (folder.Length > 0 ? folder : "folder", Checker.EntriesFromFolder(p).Where(e => !e.Episode).ToList()) };
        }
        throw new ArgumentException($"{p} was not found.");
    }

    /// <summary>What loading this list would do, before anything changes: how many names, which are repeated inside the list, which are already in
    /// the library (same name), and which look like a movie the library already has under another file name (same title and year).</summary>
    public ListPreview PreviewList(ListSource src)
    {
        var sources = ListSources(src);
        var raws = new List<string>();
        var seen = new HashSet<string>();
        var repeated = new List<string>();
        foreach (var (_, es) in sources)
            foreach (var e in es)
            {
                string r = e.Raw.Trim();
                if (r.Length == 0) continue;
                if (!seen.Add(r)) { if (!repeated.Contains(r)) repeated.Add(r); }
                else raws.Add(r);
            }
        var mine = new Dictionary<string, (string? Title, int? Year)>();
        using (var rd = Reader())
        using (var c = Cmd(rd, "SELECT raw, title, year FROM files WHERE present=1", Array.Empty<object?>()))
        using (var r = c.ExecuteReader())
            while (r.Read()) mine[r.GetString(0)] = (r.IsDBNull(1) ? null : r.GetString(1), r.IsDBNull(2) ? null : r.GetInt32(2));
        var lower = mine.Keys.Select(k => k.ToLowerInvariant()).ToHashSet();
        var sameName = raws.Where(r => lower.Contains(r.ToLowerInvariant())).ToList();
        var movies = mine.Values.Where(v => !string.IsNullOrEmpty(v.Title)).Select(v => (v.Title!.ToLowerInvariant(), v.Year)).ToHashSet();
        var otherFile = new List<string>();
        foreach (var r in raws)
        {
            if (lower.Contains(r.ToLowerInvariant())) continue;
            var p = NameParser.Parse(r);
            if (!string.IsNullOrEmpty(p.Title) && movies.Contains((p.Title.ToLowerInvariant(), p.Year))) otherFile.Add(r);
        }
        if (!sources.Any(s => s.Entries.Count > 0)) throw new ArgumentException(NoNames);
        return new ListPreview(sources.Count, raws.Count, repeated.Take(15).ToList(), repeated.Count, sameName.Take(15).ToList(), sameName.Count,
            otherFile.Take(15).ToList(), otherFile.Count, raws.Count - sameName.Count);
    }

    /// <summary>The one way to bring a list in ("Load list"). Mode "add": the names join the lists the library already has. Mode "new": the lists it
    /// has now are put aside (moved to "previous lists" in the list folder, never deleted) and this one replaces them; movies that come from the
    /// movie folders stay. With <paramref name="rescan"/> the lists and folders are read again (queued behind a running job).</summary>
    public ListLoad LoadList(ListSource src, string mode = "add", bool rescan = true)
    {
        if (mode is not ("add" or "new")) throw new ArgumentException("Choose whether to add to the current list or start a new one.");
        var sources = ListSources(src).Select(s => (s.Name, Entries: s.Entries.Where(e => e.Raw.Length > 0).ToList())).Where(s => s.Entries.Count > 0).ToList();
        if (sources.Count == 0) throw new ArgumentException(NoNames);
        string dest = LoadSettings().ListDir;
        Directory.CreateDirectory(dest);
        int moved = 0;
        if (mode == "new")
        {
            var old = Directory.GetFiles(dest, "*.txt");
            if (old.Length > 0)
            {
                string aside = Path.Combine(dest, "previous lists", Stamp("yyyy-MM-dd HH-mm-ss"));
                Directory.CreateDirectory(aside);
                foreach (var f in old) File.Move(f, Path.Combine(aside, Path.GetFileName(f)));
                moved = old.Length;
            }
        }
        var have = Directory.GetFiles(dest, "*.txt").Select(f => FolderUtil.SplitLines(Checker.ReadText(f)).Select(l => l.Trim()).ToHashSet()).ToList();
        int written = 0;
        var names = new HashSet<string>();
        foreach (var (stem, entries) in sources)
        {
            var raws = entries.Select(e => e.Raw.Trim()).Where(r => r.Length > 0).Distinct().ToList();
            names.UnionWith(raws);
            if (have.Any(h => raws.All(h.Contains))) continue;   // every name is already in a list the library reads
            string target = Path.Combine(dest, stem + ".txt");
            for (int n = 2; File.Exists(target); n++) target = Path.Combine(dest, $"{stem} ({n}).txt");
            File.WriteAllText(target, string.Join("\r\n", raws) + "\r\n", new UTF8Encoding(false));   // Python's text mode: \r\n on Windows
            have.Add(raws.ToHashSet());
            written++;
        }
        lock (Lock)
        {
            using var tx = db.BeginTransaction();
            foreach (var r in names) Exec("DELETE FROM removed WHERE raw=?", r);   // loading a name again undoes its removal
            foreach (var (_, es) in sources)
                foreach (var e in es.Where(e => e.Tconst != null))
                    Exec("INSERT OR REPLACE INTO forced(raw, tconst) VALUES (?,?)", e.Raw.Trim(), e.Tconst);
            tx.Commit();
        }
        IssueRev++;
        if (rescan) StartRescan();
        return new ListLoad(written, names.Count, moved);
    }
}
