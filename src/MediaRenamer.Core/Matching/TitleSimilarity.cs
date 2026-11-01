using System.Globalization;
using System.Text;

namespace MediaRenamer.Core.Matching;

/// <summary>Title similarity in [0,1] over normalized text (case, accents and punctuation ignored).</summary>
public static class TitleSimilarity
{
    public static string Normalize(string s)
    {
        var d = s.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(d.Length);
        foreach (var c in d)
        {
            var cat = CharUnicodeInfo.GetUnicodeCategory(c);
            if (cat == UnicodeCategory.NonSpacingMark) continue;
            sb.Append(char.IsLetterOrDigit(c) ? char.ToLowerInvariant(c) : ' ');
        }
        return string.Join(' ', sb.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }

    public static double Score(string a, string b)
    {
        a = Normalize(a);
        b = Normalize(b);
        if (a.Length == 0 || b.Length == 0) return 0;
        if (a == b) return 1;
        var max = Math.Max(a.Length, b.Length);
        var ratio = 1.0 - (double)Levenshtein(a, b) / max;
        // A title that merely extends the other ("Dune" / "Dune Part Two") is a weaker, not a strong, match.
        if (a.StartsWith(b + " ") || b.StartsWith(a + " ")) ratio = Math.Max(ratio, 0.75);
        return Math.Max(0, ratio);
    }

    public static double BestScore(string query, IEnumerable<string> candidates) =>
        candidates.Select(c => Score(query, c)).DefaultIfEmpty(0).Max();

    private static int Levenshtein(string a, string b)
    {
        var prev = new int[b.Length + 1];
        var cur = new int[b.Length + 1];
        for (var j = 0; j <= b.Length; j++) prev[j] = j;
        for (var i = 1; i <= a.Length; i++)
        {
            cur[0] = i;
            for (var j = 1; j <= b.Length; j++)
                cur[j] = Math.Min(Math.Min(cur[j - 1] + 1, prev[j] + 1), prev[j - 1] + (a[i - 1] == b[j - 1] ? 0 : 1));
            (prev, cur) = (cur, prev);
        }
        return prev[b.Length];
    }
}
