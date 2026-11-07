using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace MovieShelf.Core;

/// <summary>Persian titles: spelling clean-up, a reviewed list that replaces titles written in Latin letters, and your own edits.
/// Port of fatitles.py. An edit made in the movie window is stored as an override (fa_titles, source 'user') and always wins.</summary>
public static class FaTitles
{
    internal const string OldVersionKey = "fa_review_version";   // never saved any more: the review applies on every update, as it does in Python
    const string ZW = "‌";
    const string Fa = @"[؀-ۿ]";

    static readonly Regex Persian = new(Fa, RegexOptions.Compiled | RegexOptions.CultureInvariant);
    static readonly Regex Latin = new("[A-Za-z]", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    static readonly Regex Marks = new(@"[ً-ْـ]", RegexOptions.Compiled);
    static readonly Regex NumComma = new(@"(?<=\d),(?=\d)", RegexOptions.Compiled);
    static readonly Regex PunctSpace = new(@"\s*([،؛؟!])\s*", RegexOptions.Compiled);
    static readonly Regex Colon = new(@"\s*:\s*", RegexOptions.Compiled);
    static readonly Regex ParenOpen = new(@"\(\s+", RegexOptions.Compiled);
    static readonly Regex ParenClose = new(@"\s+\)", RegexOptions.Compiled);
    static readonly Regex HalfPlural = new("(?<=" + Fa + @") (?=(?:ها|های|هایی|ترین|تر)(?:\s|$|[،؛؟!:.)]))", RegexOptions.Compiled);
    static readonly Regex HalfMi = new("(?<!" + Fa + @")(ن?می) (?=" + Fa + ")", RegexOptions.Compiled);
    static readonly Regex HalfYe = new(@"(?<=ه) (?=ی(?:\s|$|[،؛؟!:.)]))", RegexOptions.Compiled);
    static readonly Regex ZwRun = new(ZW + "{2,}", RegexOptions.Compiled);
    static readonly Regex ZwSpace = new(@"\s*" + ZW + @"\s*", RegexOptions.Compiled);
    static readonly Regex Spaces = new(@"\s{2,}", RegexOptions.Compiled);

    public static bool HasPersian(string? t) => Persian.IsMatch(t ?? "");

    public static bool IsLatinOnly(string? t) => !string.IsNullOrEmpty(t) && Latin.IsMatch(t) && !Persian.IsMatch(t);

    static string Digits(string t)
    {
        var c = t.ToCharArray();
        for (int i = 0; i < c.Length; i++)
            if (c[i] is >= '0' and <= '9') c[i] = (char)('۰' + (c[i] - '0'));
            else if (c[i] is >= '٠' and <= '٩') c[i] = (char)('۰' + (c[i] - '٠'));
        return new string(c);
    }

    /// <summary>Standard Persian spelling for a title: Persian letters and digits, punctuation, spaces and half-spaces.</summary>
    public static string? Normalize(string? t)
    {
        if (string.IsNullOrEmpty(t) || !Persian.IsMatch(t))
        {
            var s = (t ?? "").Trim();
            return s.Length == 0 ? null : s;
        }
        t = t.Replace('ي', 'ی').Replace('ى', 'ی').Replace('ك', 'ک').Replace('ة', 'ه')
            .Replace('ە', 'ه').Replace("ۀ", "ه" + ZW + "ی");
        t = Marks.Replace(t, "");                                           // vowel marks and tatweel
        t = NumComma.Replace(t, "\u0001");                                  // keep 1,000 style numbers
        t = t.Replace(",", "،").Replace("\u0001", ",").Replace("?", "؟").Replace(";", "؛");
        if (!Latin.IsMatch(t)) t = Digits(t);                               // a title that mixes in Latin words keeps its digits
        t = PunctSpace.Replace(t, "$1 ");                                   // no space before, one after
        t = Colon.Replace(t, ": ");
        t = ParenOpen.Replace(t, "(");
        t = ParenClose.Replace(t, ")");
        // half-spaces: plural and comparative endings, the verb prefix mi- / nemi-, a trailing ye
        t = HalfPlural.Replace(t, ZW);
        t = HalfMi.Replace(t, "${1}" + ZW);
        t = HalfYe.Replace(t, ZW);
        t = ZwRun.Replace(t, ZW);
        string src = t;
        t = ZwSpace.Replace(src, m => m.Index > 0 && Persian.IsMatch(src[m.Index - 1].ToString()) ? ZW : " ");
        t = Spaces.Replace(t, " ").Trim(' ', '‌', ':', '-', '–');
        return t.Length == 0 ? null : t;
    }

    static Dictionary<int, string>? reviewed;

    /// <summary>{tconst: Persian title} from the embedded fa_reviewed.json.</summary>
    public static Dictionary<int, string> Reviewed()
    {
        if (reviewed != null) return reviewed;
        using var s = typeof(FaTitles).Assembly.GetManifestResourceStream("fa_reviewed.json") ?? throw new FileNotFoundException("fa_reviewed.json is not embedded");
        var d = new Dictionary<int, string>();
        foreach (var kv in JsonSerializer.Deserialize<Dictionary<string, string>>(s)!) d[int.Parse(kv.Key)] = kv.Value;
        return reviewed = d;
    }

    /// <summary>Respell every stored Persian title and add the reviewed replacements. Runs on every update; your own edits are never touched.</summary>
    public static (int Cleaned, int Replaced) Apply(Library lib) => lib.ApplyFaTitles();
}
