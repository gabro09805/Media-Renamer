using System.Text;
using System.Text.RegularExpressions;

namespace MediaRenamer.Bindings.Formats;

/// <summary>Makes text safe as a file or folder name (Windows rules, which are the strictest).</summary>
public static class PathSanitizer
{
    private static readonly HashSet<string> Reserved = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
    };

    public const int MaxSegmentLength = 240;

    /// <summary>A value produced by an expression must never create folders by itself: separators become '-'.</summary>
    public static string Value(string value) => value.Replace('/', '-').Replace('\\', '-');

    /// <summary>A lone drive designator such as "C:" (e.g. the {drive} binding) must keep its colon when it is the first segment.</summary>
    public static bool IsDriveLetter(string s) => s.Length == 2 && char.IsAsciiLetter(s[0]) && s[1] == ':';

    public static string Segment(string segment)
    {
        var s = Regex.Replace(segment, @"(?<=\d):(?=\d)", ".");
        s = Regex.Replace(s, @"\s*:\s*", " - ");
        s = s.Replace('"', '\'');

        var sb = new StringBuilder(s.Length);
        foreach (var c in s)
        {
            if (c < 32 || c is '<' or '>' or '|' or '?' or '*') continue;
            sb.Append(c);
        }

        s = Regex.Replace(sb.ToString(), @"\s{2,}", " ").Trim().TrimEnd('.', ' ');
        s = Regex.Replace(s, @"^-\s+|\s+-$", "").Trim();
        if (s.Length > MaxSegmentLength) s = s[..MaxSegmentLength].TrimEnd('.', ' ');

        var stem = s.Contains('.') ? s[..s.IndexOf('.')] : s;
        return Reserved.Contains(stem.TrimEnd()) ? "_" + s : s;
    }
}
