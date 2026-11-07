namespace MovieShelf.Core;

/// <summary>String similarity like rapidfuzz's fuzz.ratio: 100 * 2 * LCS / (len a + len b).</summary>
public static class Fuzz
{
    public static double Ratio(string a, string b)
    {
        int la = a.Length, lb = b.Length;
        if (la == 0 && lb == 0) return 100;
        if (la == 0 || lb == 0) return 0;
        return 200.0 * Lcs(a, b) / (la + lb);
    }

    static int Lcs(string a, string b)
    {
        if (a.Length < b.Length) (a, b) = (b, a);
        var prev = new int[b.Length + 1];
        var cur = new int[b.Length + 1];
        for (int i = 1; i <= a.Length; i++)
        {
            char ca = a[i - 1];
            for (int j = 1; j <= b.Length; j++)
                cur[j] = ca == b[j - 1] ? prev[j - 1] + 1 : Math.Max(prev[j], cur[j - 1]);
            (prev, cur) = (cur, prev);
        }
        return prev[b.Length];
    }

    /// <summary>Match masks of a pattern of up to 64 characters: bit i of a character's mask is set where the pattern holds it.</summary>
    sealed class Masks
    {
        readonly ulong[] low = new ulong[256];
        readonly Dictionary<char, ulong> high = new();
        public readonly int Length;

        public Masks(string pattern)
        {
            Length = pattern.Length;
            for (int i = 0; i < pattern.Length; i++)
            {
                char c = pattern[i];
                if (c < 256) low[c] |= 1UL << i;
                else high[c] = high.GetValueOrDefault(c) | (1UL << i);
            }
        }

        public ulong Of(char c) => c < 256 ? low[c] : high.GetValueOrDefault(c);
    }

    /// <summary>Length of the longest common subsequence, one machine word at a time (Hyyro's bit-parallel algorithm).</summary>
    static int LcsBits(Masks m, string text)
    {
        ulong v = ~0UL;
        foreach (char c in text)
        {
            ulong pm = m.Of(c);
            v = (v + (v & pm)) | (v & ~pm);
        }
        ulong live = m.Length == 64 ? ~0UL : (1UL << m.Length) - 1;
        return System.Numerics.BitOperations.PopCount(~v & live);
    }

    /// <summary>The best <paramref name="limit"/> choices scoring at least <paramref name="cutoff"/>: (index, score), best first.</summary>
    public static List<(int Index, double Score)> Extract(string query, IReadOnlyList<string> choices, int limit, double cutoff)
    {
        var found = new List<(int Index, double Score)>();
        int lq = query.Length;
        var masks = lq is > 0 and <= 64 ? new Masks(query) : null;
        for (int i = 0; i < choices.Count; i++)
        {
            int lc = choices[i].Length;
            // the best possible score for these lengths; skip hopeless choices before the costly comparison
            if (200.0 * Math.Min(lq, lc) / (lq + lc) < cutoff) continue;
            double s = masks != null && lc > 0 ? 200.0 * LcsBits(masks, choices[i]) / (lq + lc) : Ratio(query, choices[i]);
            if (s >= cutoff) found.Add((i, s));
        }
        found.Sort((x, y) => y.Score != x.Score ? y.Score.CompareTo(x.Score) : x.Index.CompareTo(y.Index));
        if (found.Count > limit) found.RemoveRange(limit, found.Count - limit);
        return found;
    }
}
