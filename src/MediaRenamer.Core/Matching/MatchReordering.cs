using MediaRenamer.Model;

namespace MediaRenamer.Core.Matching;

/// <summary>
/// Reordering helpers for the Rename lists. Moving an original file keeps its match; moving only a new name shifts the
/// matches among the files (spec: the other new names change position, and incongruences are flagged, never blocked).
/// </summary>
public static class MatchReordering
{
    /// <summary>Moves the items at <paramref name="selected"/> one step up (-1) or down (+1) as a block; returns the new indices of the moved items.</summary>
    public static IReadOnlyList<int> MoveBlock<T>(IList<T> list, IEnumerable<int> selected, int delta)
    {
        var set = selected.Where(i => i >= 0 && i < list.Count).Distinct().OrderBy(i => i).ToList();
        if (set.Count == 0 || delta == 0) return set;
        var marked = new HashSet<int>(set);
        var order = delta < 0 ? set : Enumerable.Reverse(set).ToList();
        var result = new List<int>();
        foreach (var i in order)
        {
            var j = i + delta;
            if (j < 0 || j >= list.Count || marked.Contains(j)) { result.Add(i); continue; }
            (list[i], list[j]) = (list[j], list[i]);
            marked.Remove(i);
            marked.Add(j);
            result.Add(j);
        }
        return result.OrderBy(i => i).ToList();
    }

    /// <summary>
    /// Gives each file the match found at the same position of <paramref name="slots"/>. The matched series/episode/movie travels with the slot;
    /// the file-derived numbering is recalculated so incongruences can be detected.
    /// </summary>
    public static IReadOnlyList<MatchResult> Reassign(IReadOnlyList<MediaFile> files, IReadOnlyList<MatchResult> slots)
    {
        var result = new List<MatchResult>(files.Count);
        for (var i = 0; i < files.Count; i++)
        {
            var slot = slots[i];
            var matched = slot.Series is not null || slot.Movie is not null;
            result.Add(slot with
            {
                File = files[i],
                OriginalNumbering = OriginalOf(files[i]),
                Association = matched ? AssociationMode.Manual : slot.Association,
                Reasons = matched ? new[] { "Moved manually." } : slot.Reasons,
            });
        }
        return result;
    }

    public static Numbering OriginalOf(MediaFile file)
    {
        var d = file.Detected;
        return new Numbering(d.Season, d.Episodes.FirstOrDefault() is var e and > 0 ? e : null, d.Absolute);
    }
}

/// <summary>Detects an incongruence between a file and its match (name or numbering). It is a warning only: renaming is still allowed.</summary>
public static class MatchConsistency
{
    public static string? Check(MatchResult m)
    {
        var entity = (MediaEntity?)m.Movie ?? m.Series;
        if (entity is null) return null;
        var d = m.File.Detected;

        if (!string.IsNullOrWhiteSpace(d.Title)
            && TitleSimilarity.BestScore(d.Title, entity.Titles.AllForMatching()) < 0.4)
            return $"The file name (\"{d.Title}\") does not look like the matched title.";

        if (m.Episode is { } e && m.OriginalNumbering is { } o && d.Episodes.Count <= 1)
        {
            var candidates = new List<Numbering> { e.Number };
            candidates.AddRange(e.Orders.Values);
            bool? ok = null;
            if (o.Episode is { } oe)
                ok = candidates.Any(n => n.Episode == oe && (o.Season is null || n.Season == o.Season))
                     || (o.Season is null && candidates.Any(n => n.Absolute == oe));
            else if (o.Absolute is { } a)
                ok = candidates.Any(n => n.Absolute == a);

            if (ok == false)
                return $"The numbering in the file name ({Describe(o)}) does not match the episode ({Describe(e.Number)}).";
        }
        return null;
    }

    private static string Describe(Numbering n) =>
        n.Season is { } s && n.Episode is { } ep ? $"S{s:00}E{ep:00}" : n.Episode is { } only ? $"E{only:00}" : n.Absolute is { } a ? $"#{a}" : "?";
}
