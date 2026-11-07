using System.IO.Enumeration;
using System.Text;
using System.Text.RegularExpressions;

namespace MovieShelf.Core;

/// <summary>Small stand-ins for the Python os / os.path / glob / csv behaviour the folder code relies on.</summary>
public static class FolderUtil
{
    /// <summary>Python os.path.splitext: ".mkv" has no extension, "a.b.mkv" has ".mkv".</summary>
    public static (string Stem, string Ext) SplitExt(string name)
    {
        int slash = Math.Max(name.LastIndexOf('/'), name.LastIndexOf('\\'));
        int dot = name.LastIndexOf('.');
        if (dot <= slash) return (name, "");
        int i = slash + 1;
        while (i < dot && name[i] == '.') i++;   // leading dots belong to the stem
        if (i >= dot) return (name, "");
        return (name[..dot], name[dot..]);
    }

    /// <summary>Python os.path.basename (both separators).</summary>
    public static string BaseName(string p)
    {
        int slash = Math.Max(p.LastIndexOf('/'), p.LastIndexOf('\\'));
        return p[(slash + 1)..];
    }

    public static string DirName(string p) => Path.GetDirectoryName(p) ?? "";

    /// <summary>Python str.splitlines.</summary>
    public static List<string> SplitLines(string s)
    {
        var l = new List<string>();
        int start = 0;
        for (int i = 0; i < s.Length; i++)
        {
            char c = s[i];
            if (c is '\n' or '\r' or '\v' or '\f' or '\x1c' or '\x1d' or '\x1e' or '\x85' or '\x2028' or '\x2029')
            {
                l.Add(s[start..i]);
                if (c == '\r' && i + 1 < s.Length && s[i + 1] == '\n') i++;
                start = i + 1;
            }
        }
        if (start < s.Length) l.Add(s[start..]);
        return l;
    }

    /// <summary>Python os.walk (top-down, no symlink following, unreadable folders skipped). The consumer may
    /// remove names from Dirs before asking for the next item to prune the walk, like with os.walk.</summary>
    public static IEnumerable<(string Dir, List<string> Dirs, List<string> Files)> Walk(string root)
    {
        var stack = new Stack<string>();
        stack.Push(root);
        while (stack.Count > 0)
        {
            string dir = stack.Pop();
            var dirs = new List<string>();
            var files = new List<string>();
            try
            {
                foreach (var e in new DirectoryInfo(dir).EnumerateFileSystemInfos())
                {
                    if ((e.Attributes & FileAttributes.Directory) != 0) dirs.Add(e.Name);
                    else files.Add(e.Name);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { continue; }
            yield return (dir, dirs, files);
            // reverse so the first folder is walked first
            for (int i = dirs.Count - 1; i >= 0; i--)
            {
                string sub = Path.Combine(dir, dirs[i]);
                try { if (new DirectoryInfo(sub).LinkTarget != null) continue; } catch (IOException) { continue; }
                stack.Push(sub);
            }
        }
    }

    public static long DirSize(string d)
    {
        long total = 0;
        foreach (var (dir, _, files) in Walk(d))
            foreach (var f in files)
            {
                try { total += new FileInfo(Path.Combine(dir, f)).Length; }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        return total;
    }

    /// <summary>Python st_mtime: seconds since 1970 as a double.</summary>
    public static double MTime(DateTime utc) => (utc.Ticks - DateTime.UnixEpoch.Ticks) / 1e7;

    /// <summary>Python glob.glob(recursive=True) for the few ffprobe install patterns: '*' inside a name, '**' = any depth.</summary>
    public static List<string> Glob(string pattern)
    {
        var parts = pattern.Split('\\', '/');
        var cur = new List<string> { parts[0] + Path.DirectorySeparatorChar };
        for (int i = 1; i < parts.Length; i++)
        {
            string seg = parts[i];
            var next = new List<string>();
            bool last = i == parts.Length - 1;
            foreach (var b in cur)
            {
                if (seg == "**")
                {
                    next.Add(b);
                    next.AddRange(AllDirs(b));
                }
                else if (seg.IndexOfAny(new[] { '*', '?' }) < 0)
                {
                    string p = Path.Combine(b, seg);
                    if (last ? File.Exists(p) : Directory.Exists(p)) next.Add(p);
                }
                else
                {
                    try
                    {
                        foreach (var e in last ? Directory.EnumerateFiles(b) : Directory.EnumerateDirectories(b))
                            if (FileSystemName.MatchesSimpleExpression(seg, Path.GetFileName(e), true)) next.Add(e);
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
                }
            }
            cur = next.Distinct().ToList();
        }
        return cur;
    }

    static IEnumerable<string> AllDirs(string root)
    {
        foreach (var (dir, dirs, _) in Walk(root))
            foreach (var d in dirs) yield return Path.Combine(dir, d);
    }

    /// <summary>Python shutil.which for a Windows-style PATH.</summary>
    public static string? Which(string name)
    {
        var exts = new List<string> { "" };
        if (OperatingSystem.IsWindows())
            exts.AddRange((Environment.GetEnvironmentVariable("PATHEXT") ?? ".COM;.EXE;.BAT;.CMD").Split(';', StringSplitOptions.RemoveEmptyEntries));
        foreach (var dir in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
            foreach (var ext in exts)
            {
                try
                {
                    string p = Path.Combine(dir.Trim('"'), name + ext);
                    if (File.Exists(p)) return p;
                }
                catch (ArgumentException) { }
            }
        return null;
    }
}

/// <summary>The parts of a Python csv dialect the list check uses. <see cref="Excel"/> is csv.excel.</summary>
public sealed record CsvDialect(char Delimiter = ',', char Quote = '"', bool DoubleQuote = true, bool SkipInitialSpace = false)
{
    public static readonly CsvDialect Excel = new();
}

/// <summary>Python csv module, as far as the list check and the rename plan need it.</summary>
public static class ListCsv
{
    /// <summary>The UTF-8 byte order mark that makes Excel read Persian / accented text correctly.</summary>
    public static readonly string Bom = ((char)0xFEFF).ToString();

    const string Word = @"\p{L}\p{N}_";   // what Python's \w means

    /// <summary>csv.Sniffer().sniff(firstLine, delimiters=',;\t'), with csv.excel as the fallback. The list check only ever sniffs one line.</summary>
    public static CsvDialect Sniff(string firstLine)
    {
        const string allowed = ",;\t";
        var (quote, doubleQuote, delim, skip) = GuessQuoteAndDelimiter(firstLine, allowed);
        if (delim == null)
        {
            // the preferred order of the Sniffer, for a line that holds several
            delim = new[] { ',', '\t', ';' }.Where(firstLine.Contains).Cast<char?>().FirstOrDefault();
            if (delim == null) return CsvDialect.Excel;
            skip = firstLine.Count(c => c == delim) == Regex.Count(firstLine, Regex.Escape(delim + " "));
        }
        return new CsvDialect(delim.Value, quote ?? '"', doubleQuote, skip);
    }

    /// <summary>Text between two identical quotes that have the same character on both sides: the quote with the most wins, the same for the delimiter.</summary>
    static (char? Quote, bool DoubleQuote, char? Delim, bool Skip) GuessQuoteAndDelimiter(string data, string allowed)
    {
        string d = $"(?<delim>[^{Word}\\n\"'])";
        MatchCollection? matches = null;
        foreach (var pat in new[]
        {
            d + "(?<space> ?)(?<quote>[\"']).*?\\k<quote>\\k<delim>",        // ,".*?",
            "(?:^|\\n)(?<quote>[\"']).*?\\k<quote>" + d + "(?<space> ?)",    //  ".*?",
            d + "(?<space> ?)(?<quote>[\"']).*?\\k<quote>(?:$|\\n)",         // ,".*?"
            "(?:^|\\n)(?<quote>[\"']).*?\\k<quote>(?:$|\\n)",                //  ".*?" (no delimiter, no space)
        })
        {
            matches = Regex.Matches(data, pat, RegexOptions.Singleline | RegexOptions.Multiline);
            if (matches.Count > 0) break;
        }
        if (matches == null || matches.Count == 0) return (null, false, null, false);
        var quotes = new List<(char Key, int N)>();
        var delims = new List<(char Key, int N)>();
        static void Count(List<(char Key, int N)> l, char k)
        {
            int i = l.FindIndex(x => x.Key == k);
            if (i < 0) l.Add((k, 1));
            else l[i] = (k, l[i].N + 1);
        }
        int spaces = 0;
        foreach (Match m in matches)
        {
            Count(quotes, m.Groups["quote"].Value[0]);
            if (!m.Groups["delim"].Success) continue;
            char k = m.Groups["delim"].Value[0];
            if (allowed.Contains(k)) Count(delims, k);
            if (m.Groups["space"].Length > 0) spaces++;
        }
        // Python's max keeps the first of equals
        char quote = quotes.First(q => q.N == quotes.Max(x => x.N)).Key;
        char? delim = null;
        bool skip = false;
        if (delims.Count > 0)
        {
            var best = delims.First(x => x.N == delims.Max(y => y.N));
            delim = best.Key;
            skip = best.N == spaces;
        }
        // an extra quote between delimiters means a double quoted format
        string e = delim == null ? "" : Regex.Escape(delim.Value.ToString());
        string dq = $"(({e})|^)[^{Word}]*{quote}[^{e}\\n]*{quote}[^{e}\\n]*{quote}[^{Word}]*(({e})|$)";
        return (quote, Regex.IsMatch(data, dq, RegexOptions.Multiline), delim, skip);
    }

    enum State { StartRecord, StartField, InField, InQuoted, QuoteInQuoted, EatNewline }

    /// <summary>csv.reader over text with the excel dialect and this delimiter.</summary>
    public static List<List<string>> Read(string text, char delim) => Read(text, CsvDialect.Excel with { Delimiter = delim });

    /// <summary>csv.reader over text (non-strict, no escape character): the state machine of Python's _csv module. A quote only opens a quoted field at its start;
    /// without double quoting a closing quote just ends the quoted part, and what follows is literal.</summary>
    public static List<List<string>> Read(string text, CsvDialect dialect)
    {
        const int Eol = -1;   // the end of a line of input, after its own newline
        var rows = new List<List<string>>();
        var row = new List<string>();
        var f = new StringBuilder();
        var state = State.StartRecord;
        void Save() { row.Add(f.ToString()); f.Clear(); }
        void EndRow() { rows.Add(row); row = new(); }
        void Step(int u)
        {
            bool newline = u is '\n' or '\r' or Eol;
            switch (state)
            {
                case State.StartRecord:
                    if (u == Eol) break;
                    if (u is '\n' or '\r') { state = State.EatNewline; break; }
                    state = State.StartField;
                    goto case State.StartField;
                case State.StartField:
                    if (newline) { Save(); state = u == Eol ? State.StartRecord : State.EatNewline; }
                    else if (u == dialect.Quote) state = State.InQuoted;
                    else if (u == ' ' && dialect.SkipInitialSpace) { }
                    else if (u == dialect.Delimiter) Save();
                    else { f.Append((char)u); state = State.InField; }
                    break;
                case State.InField:
                    if (newline) { Save(); state = u == Eol ? State.StartRecord : State.EatNewline; }
                    else if (u == dialect.Delimiter) { Save(); state = State.StartField; }
                    else f.Append((char)u);
                    break;
                case State.InQuoted:
                    if (u == Eol) break;
                    if (u == dialect.Quote) state = dialect.DoubleQuote ? State.QuoteInQuoted : State.InField;
                    else f.Append((char)u);
                    break;
                case State.QuoteInQuoted:
                    if (u == dialect.Quote) { f.Append((char)u); state = State.InQuoted; }
                    else if (u == dialect.Delimiter) { Save(); state = State.StartField; }
                    else if (newline) { Save(); state = u == Eol ? State.StartRecord : State.EatNewline; }
                    else { f.Append((char)u); state = State.InField; }
                    break;
                case State.EatNewline:
                    if (u == Eol) state = State.StartRecord;
                    else if (!newline)   // a bare \r inside a line, which Python refuses: read it as the end of the record
                    {
                        EndRow();
                        state = State.StartRecord;
                        Step(u);
                    }
                    break;
            }
        }
        for (int pos = 0; pos < text.Length;)
        {
            int nl = text.IndexOf('\n', pos);
            int end = nl < 0 ? text.Length : nl + 1;
            for (; pos < end; pos++) Step(text[pos]);
            Step(Eol);
            if (state == State.StartRecord) EndRow();
        }
        if (f.Length > 0 || state == State.InQuoted) { Save(); EndRow(); }
        return rows;
    }

    /// <summary>csv.writer: comma, \r\n, quote only when needed.</summary>
    public static string Write(IEnumerable<IEnumerable<object?>> rows)
    {
        var sb = new StringBuilder();
        foreach (var r in rows)
        {
            bool first = true;
            foreach (var v in r)
            {
                if (!first) sb.Append(',');
                first = false;
                string s = v switch { null => "", string x => x, double d => PyFloat(d), IFormattable fm => fm.ToString(null, System.Globalization.CultureInfo.InvariantCulture), _ => v.ToString() ?? "" };
                if (s.IndexOfAny(new[] { ',', '"', '\r', '\n' }) >= 0) sb.Append('"').Append(s.Replace("\"", "\"\"")).Append('"');
                else sb.Append(s);
            }
            sb.Append("\r\n");
        }
        return sb.ToString();
    }

    /// <summary>Python repr(float): shortest round-trip, with .0 for whole numbers.</summary>
    public static string PyFloat(double d)
    {
        string s = d.ToString("R", System.Globalization.CultureInfo.InvariantCulture);
        return s.Contains('.') || s.Contains('E') || s.Contains("N") ? s : s + ".0";
    }
}
