using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace MovieShelf.Core;

/// <summary>What a release-style movie name says: title, year and quality.</summary>
public sealed class ParsedName
{
    public string Raw = "";
    public string Title = "";
    public string? AltTitle;
    public int? Year;
    public string? Resolution, Source, Codec, Audio, Edition, Group;
    public bool Bit10, Hdr, FarsiDubbed, DualAudio, Softsub;
}

/// <summary>Parses names like '13.Minutes.(Elser).2015.720p.Bluray.Ganool' (same rules as the Python parse.py).</summary>
public static class NameParser
{
    static readonly Regex YearRe = new(@"(?<![0-9])(18[89][0-9]|19[0-9]{2}|20[0-4][0-9])(?![0-9]|p\b)", RegexOptions.Compiled);
    static readonly Regex QualityStart = new(@"(?<![a-z0-9])(2160p|1080p?|720p?|480p|576p|dvdrip|web|bluray|brrip|hdtv|x26[45])", RegexOptions.Compiled);

    static readonly (Regex Rx, string Label)[] ResMap =
    {
        (E(@"2160p|4k|uhd"), "2160p"),
        (E(@"1080p?|10800p|fullhd"), "1080p"),
        (E(@"720p?"), "720p"),
        (E(@"576p"), "576p"),
        (E(@"480p"), "480p"),
    };

    static readonly (string Key, string Label)[] SourceMap =
    {
        ("bluray", "BluRay"), ("blu-ray", "BluRay"), ("bdrip", "BluRay"), ("brrip", "BluRay"),
        ("web-dl", "WEB-DL"), ("webdl", "WEB-DL"), ("web.dl", "WEB-DL"), ("web dl", "WEB-DL"),
        ("webrip", "WEBRip"), ("hdtv", "HDTV"), ("dvdrip", "DVDRip"), ("dvdscr", "DVDRip"),
        ("hdrip", "HDRip"), ("web", "WEB-DL"),
    };

    static readonly (Regex Rx, string Label)[] Editions =
    {
        (E(@"director'?s[ .]?cut|\bdc\b"), "Director's Cut"), (E("extended"), "Extended"),
        (E("remastered"), "Remastered"), (E("restored"), "Restored"), (E("criterion"), "Criterion"),
        (E("unrated"), "Unrated"), (E("uncut"), "Uncut"), (E("imax"), "IMAX"),
        (E(@"special[ .]edition"), "Special Edition"), (E(@"colori[sz]ed"), "Colorized"),
        (E("redux"), "Redux"), (E("theatrical"), "Theatrical"), (E(@"final[ .]cut"), "Final Cut"),
    };

    static Regex E(string p) => new(p, RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // Tokens that mark the end of the title when no year is present
    static readonly HashSet<string> StopTokens = new()
    {
        "2160p", "1080p", "720p", "576p", "480p", "1080", "720", "farsi", "dubbed", "dvdrip", "bluray",
        "brrip", "webrip", "web-dl", "webdl", "hdtv", "x264", "x265", "hevc", "10bit", "web", "hdrip",
        "bdrip", "extended", "remastered", "unrated", "dual", "softsub", "yify", "tinymoviez", "ganool",
        "512kb", "repack", "proper", "limited", "fixed", "subbed",
    };

    static readonly Regex Dots = new(@"\.\.+", RegexOptions.Compiled);
    static readonly Regex DigitDot = new(@"(?<=\d)\.(?=\d)", RegexOptions.Compiled);
    static readonly Regex InitialDot = new(@"(?<=\b[A-Za-z])\.(?=[A-Za-z]\b)", RegexOptions.Compiled);
    static readonly Regex Spaces = new(@"\s+", RegexOptions.Compiled);
    static readonly Regex ParenGroup = new(@"\(([^()]*)\)", RegexOptions.Compiled);
    static readonly Regex AkaSplit = new(@"\s+(?:AKA|aka|a\.k\.a)\s+", RegexOptions.Compiled);
    static readonly Regex GroupWord = new(@"[A-Za-z0-9&]+", RegexOptions.Compiled);
    static readonly Regex BadGroup = new(
        @"^(?:\d+p?|\d|\d?ch|x26[45]|dl|web|dvdrip|bluray|brrip|webrip|hdtv|hdrip|bdrip|dubbed|farsi|10bit|hevc|ac3|aac|dts|softsub|subbed|remastered|extended|repack|proper|fixed|cut|edition|unrated|imax|hdr)$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);
    static readonly Regex Separators = new(@"[._()\[\]]", RegexOptions.Compiled);
    static readonly Regex SdWords = new("dvd|480|xvid|divx|512kb", RegexOptions.Compiled);
    static readonly Regex X265 = new(@"x265|hevc|h\.?265", RegexOptions.Compiled);
    static readonly Regex X264 = new(@"x264|h\.?264|avc", RegexOptions.Compiled);
    static readonly Regex HdrRx = new(@"\bhdr", RegexOptions.Compiled);
    static readonly Regex Ch71 = new(@"8ch|(?<![0-9])7\.1(?![0-9])", RegexOptions.Compiled);
    static readonly Regex Ch51 = new(@"6ch|(?<![0-9])5\.1(?![0-9])|dts|ac3|dd5", RegexOptions.Compiled);
    static readonly Regex FarsiRx = new(@"farsi|persian|duble|dooble|\bdubbed", RegexOptions.Compiled);
    static readonly Regex DualRx = new("dual|2audio", RegexOptions.Compiled);
    static readonly Regex SoftRx = new("softsub|subbed", RegexOptions.Compiled);

    static readonly char[] TitleTrim = { ' ', '-', ':' };

    static string CleanTitle(string s)
    {
        s = s.Replace('_', ' ');
        s = Dots.Replace(s, ": ");
        s = DigitDot.Replace(s, "\u0000");
        s = InitialDot.Replace(s, "\u0001");
        s = s.Replace('.', ' ').Replace('\u0000', '.').Replace('\u0001', '.');
        return Spaces.Replace(s, " ").Trim(TitleTrim);
    }

    // Python re.split(r'(?=[.\s(])', s): a piece starts at every '.', whitespace or '('.
    static List<string> SplitBefore(string s)
    {
        var parts = new List<string>();
        int start = 0;
        for (int i = 0; i < s.Length; i++)
        {
            char c = s[i];
            if (i > 0 && (c == '.' || c == '(' || char.IsWhiteSpace(c)))
            {
                parts.Add(s[start..i]);
                start = i;
            }
        }
        parts.Add(s[start..]);
        return parts;
    }

    // Python re.split(r'(?<=[.\s])', s): a piece ends after every '.' or whitespace (a last empty piece when s ends so).
    static List<string> SplitAfter(string s)
    {
        var parts = new List<string>();
        int start = 0;
        for (int i = 0; i < s.Length; i++)
        {
            char c = s[i];
            if (c == '.' || char.IsWhiteSpace(c))
            {
                parts.Add(s[start..(i + 1)]);
                start = i + 1;
            }
        }
        parts.Add(s[start..]);
        return parts;
    }

    public static ParsedName Parse(string raw)
    {
        string name = raw.Trim();
        var info = new ParsedName { Raw = name };
        var years = YearRe.Matches(name).ToList();
        var later = years.Where(m => m.Index > 0).ToList();
        if (later.Count > 0) years = later;
        Match? yearM = null;
        if (years.Count > 0)
        {
            string low = name.ToLowerInvariant();
            int qpos = name.Length;
            foreach (Match m in QualityStart.Matches(low))
            {
                if (m.Index > 0) { qpos = m.Index; break; }
            }
            var before = years.Where(m => m.Index < qpos).ToList();
            yearM = (before.Count > 0 ? before : years)[^1];
        }
        string head, tail;
        if (yearM != null && yearM.Index > 0)
        {
            head = name[..yearM.Index];
            tail = name[(yearM.Index + yearM.Length)..];
            info.Year = int.Parse(yearM.Groups[1].Value, CultureInfo.InvariantCulture);
        }
        else
        {
            int cut = name.Length, pos = 0;
            foreach (var t in SplitBefore(name))
            {
                string tt = t.Trim('.', ' ', '(').ToLowerInvariant();
                if (StopTokens.Contains(tt) && pos > 0) { cut = pos; break; }
                pos += t.Length;
            }
            head = name[..cut];
            tail = name[cut..];
            info.Year = yearM != null ? int.Parse(yearM.Groups[1].Value, CultureInfo.InvariantCulture) : null;
        }

        // quality tags placed before the year: 'Title.Farsi.Dubbed.1998' / 'Title.720p.1957'
        var parts = SplitAfter(head);
        while (parts.Count > 1)
        {
            string last = parts[^1].Trim('.', ' ').ToLowerInvariant();
            if (last.Length == 0 || StopTokens.Contains(last)) parts.RemoveAt(parts.Count - 1);
            else break;
        }
        head = string.Concat(parts);
        var alts = ParenGroup.Matches(head);
        string headNoParen = ParenGroup.Replace(head, " ");
        info.Title = CleanTitle(headNoParen);
        info.AltTitle = alts.Count > 0 ? CleanTitle(alts[0].Groups[1].Value) : null;
        var aka = AkaSplit.Split(info.Title, 2);
        if (aka.Length == 2 && aka[0].Length > 0 && aka[1].Length > 0)
        {
            string a1 = aka[1].Trim();
            info.Title = aka[0].Trim();
            info.AltTitle = string.IsNullOrEmpty(info.AltTitle) ? a1 : info.AltTitle;
        }
        if (info.Title.Length == 0 && !string.IsNullOrEmpty(info.AltTitle))
        {
            info.Title = info.AltTitle;
            info.AltTitle = null;
        }

        string rest = name[head.Length..];
        string q = (rest.Length > 0 ? rest : name).ToLowerInvariant();
        string qn = " " + Separators.Replace(q, " ") + " ";
        foreach (var (rx, label) in ResMap)
        {
            if (rx.IsMatch(q)) { info.Resolution = label; break; }
        }
        if (info.Resolution == null && SdWords.IsMatch(q)) info.Resolution = "SD";
        foreach (var (key, label) in SourceMap)
        {
            if (q.Contains(key, StringComparison.Ordinal)) { info.Source = label; break; }
        }
        info.Codec = X265.IsMatch(q) ? "x265" : X264.IsMatch(q) ? "x264" : null;
        info.Bit10 = q.Contains("10bit", StringComparison.Ordinal);
        info.Hdr = HdrRx.IsMatch(qn);
        string? ch = null;
        if (Ch71.IsMatch(q)) ch = "7.1";
        else if (Ch51.IsMatch(q)) ch = "5.1";
        else if (q.Contains("2ch", StringComparison.Ordinal)) ch = "2.0";
        info.Audio = ch;
        info.FarsiDubbed = FarsiRx.IsMatch(qn) || q.Contains("dubbed", StringComparison.Ordinal);
        info.DualAudio = DualRx.IsMatch(q);
        info.Softsub = SoftRx.IsMatch(q);
        var eds = new List<string>();
        foreach (var (rx, label) in Editions)
        {
            if (rx.IsMatch(q) && !eds.Contains(label)) eds.Add(label);
        }
        info.Edition = eds.Count > 0 ? string.Join(", ", eds) : null;
        var grp = GroupWord.Matches(tail);
        info.Group = grp.Count > 0 && !BadGroup.IsMatch(grp[^1].Value) ? grp[^1].Value : null;
        return info;
    }
}

/// <summary>Normalization for title matching (same as parse.py norm / key / query_keys).</summary>
public static class TitleKeys
{
    static readonly Regex Apos = new("[’'`´]", RegexOptions.Compiled);
    static readonly Regex NumSep = new(@"(?<=\d)[.,:](?=\d)", RegexOptions.Compiled);
    static readonly Regex NonAlnum = new("[^0-9a-z؀-ۿЀ-ӿͰ-Ͽ぀-ヿ一-鿿가-힯]+", RegexOptions.Compiled);

    public static readonly Regex EditionWords = new(
        @"\b(theatrical|directors cut|director s cut|dircut|dc|uncut|unrated|redux|extended|remastered|" +
        @"special edition|criterion collection|criterion|colorized|final cut|imax|edition|cut)\b", RegexOptions.Compiled);

    static readonly HashSet<string> Articles = new() { "the", "a", "an", "le", "la", "les", "il", "el", "der", "die", "das" };

    public static string StripAccents(string s)
    {
        var d = s.Normalize(NormalizationForm.FormKD);
        var sb = new StringBuilder(d.Length);
        foreach (var c in d)
        {
            var cat = CharUnicodeInfo.GetUnicodeCategory(c);
            if (cat == UnicodeCategory.NonSpacingMark || cat == UnicodeCategory.SpacingCombiningMark || cat == UnicodeCategory.EnclosingMark) continue;
            sb.Append(c);
        }
        return sb.ToString().Replace("ß", "ss").Replace("æ", "ae").Replace("ø", "o").Replace("œ", "oe").Replace("ł", "l").Replace("đ", "d");
    }

    static bool Cased(char c) => char.GetUnicodeCategory(c) is UnicodeCategory.UppercaseLetter or UnicodeCategory.LowercaseLetter or UnicodeCategory.TitlecaseLetter;

    static bool Ignorable(char c) =>
        char.GetUnicodeCategory(c) is UnicodeCategory.NonSpacingMark or UnicodeCategory.EnclosingMark or UnicodeCategory.Format or UnicodeCategory.ModifierLetter or UnicodeCategory.ModifierSymbol
        || "'.:··‘’․‧︓﹒﹕＇．：".Contains(c);

    /// <summary>Like Python str.lower(): a capital sigma ending a word becomes the final sigma, not the medial one.</summary>
    static string Lower(string s)
    {
        string low = s.ToLowerInvariant();
        if (!s.Contains('Σ')) return low;
        var c = low.ToCharArray();
        for (int i = 0; i < s.Length; i++)
        {
            if (s[i] != 'Σ') continue;
            int a = i - 1;
            while (a >= 0 && Ignorable(s[a])) a--;
            int b = i + 1;
            while (b < s.Length && Ignorable(s[b])) b++;
            if (a >= 0 && Cased(s[a]) && !(b < s.Length && Cased(s[b]))) c[i] = 'ς';
        }
        return new string(c);
    }

    static string Squash(string s) => string.Join(' ', s.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    /// <summary>Spaced normalized form: "Zatoichi's Cane-Sword" -> "zatoichis cane sword".</summary>
    public static string Norm(string? s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        s = Lower(StripAccents(s)).Replace("&", " and ");
        s = Apos.Replace(s, "");
        s = NumSep.Replace(s, "");
        s = NonAlnum.Replace(s, " ");
        return Squash(s);
    }

    /// <summary>Compact match key without spaces.</summary>
    public static string Key(string? s) => Norm(s).Replace(" ", "");

    /// <summary>All keys worth trying for a parsed title.</summary>
    public static List<string> QueryKeys(string? title)
    {
        var keys = new List<string>();
        string n = Norm(title);
        if (n.Length == 0) return keys;
        keys.Add(n.Replace(" ", ""));
        string stripped = Squash(EditionWords.Replace(n, " "));
        if (stripped.Length > 0 && stripped != n)
        {
            keys.Add(stripped.Replace(" ", ""));
            n = stripped;
        }
        var w = n.Split(' ');
        if (w.Length > 1 && Articles.Contains(w[0])) keys.Add(string.Concat(w.Skip(1)));
        if ((" " + n + " ").Contains(" and ")) keys.Add(n.Replace(" and ", " ").Replace(" ", ""));
        var outList = new List<string>();
        foreach (var k in keys)
            if (k.Length > 0 && !outList.Contains(k)) outList.Add(k);
        return outList;
    }
}
