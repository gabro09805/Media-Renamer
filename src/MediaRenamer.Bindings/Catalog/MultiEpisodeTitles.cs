using System.Text.RegularExpressions;

namespace MediaRenamer.Bindings.Catalog;

/// <summary>Merges the titles of a multi-episode file: "A + B", or "X Parte 1 + Parte 2" when they differ only by a part number.</summary>
public static class MultiEpisodeTitles
{
    private static readonly Regex Part = new(
        @"^(?<base>.*?)[\s\-–:,(]*(?<part>(?:parte|part|pt\.?)\s*\d+)\)?\s*$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    public static string? Merge(IReadOnlyList<string?> titles)
    {
        var list = titles.Where(t => !string.IsNullOrWhiteSpace(t)).Select(t => t!.Trim()).ToList();
        if (list.Count == 0) return null;
        if (list.Count == 1) return list[0];

        var parsed = list.Select(t => Part.Match(t)).ToList();
        if (parsed.All(m => m.Success && m.Groups["base"].Value.Trim().Length > 0)
            && parsed.Select(m => m.Groups["base"].Value.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).Count() == 1)
            return parsed[0].Groups["base"].Value.Trim() + " " + string.Join(" + ", parsed.Select(m => m.Groups["part"].Value));

        return string.Join(" + ", list.Distinct(StringComparer.OrdinalIgnoreCase));
    }
}
