using MediaRenamer.Model;

namespace MediaRenamer.Core.Matching;

/// <summary>Pairing computed for the "Send to Rename" preview. <see cref="Problem"/> blocks the transfer until the selection is fixed.</summary>
public sealed record AssociationPlan(IReadOnlyList<MatchResult> Matches, string? Problem);

/// <summary>
/// Associates local files with episodes chosen in Search Episodes (spec §8.3).
/// Search Order: the n-th file gets the n-th selected episode and file-name numbering is ignored.
/// Episode Number: file-name numbering is matched with the selected episodes only.
/// </summary>
public static class EpisodeAssociator
{
    public static AssociationPlan BySearchOrder(IReadOnlyList<MediaFile> files, Series series, IReadOnlyList<Episode> selected)
    {
        var results = new List<MatchResult>();
        for (var i = 0; i < files.Count; i++)
        {
            var file = files[i];
            var original = OriginalOf(file);
            if (i < selected.Count)
            {
                results.Add(MatchService.Manual(file, series, selected[i], null, original) with
                {
                    Association = AssociationMode.SearchOrder,
                    Reasons = new[] { $"Search order: file {i + 1} of {files.Count}." },
                });
            }
            else
            {
                results.Add(new MatchResult
                {
                    File = file, Confidence = MatchConfidence.None, OriginalNumbering = original,
                    Reasons = new[] { "No episode selected for this file." },
                });
            }
        }

        string? problem = files.Count == selected.Count ? null
            : $"{files.Count} file(s) but {selected.Count} episode(s) selected: adjust the selection so the counts match.";
        return new AssociationPlan(results, problem);
    }

    public static AssociationPlan ByEpisodeNumber(IReadOnlyList<MediaFile> files, Series series, IReadOnlyList<Episode> selected, MatchPreferences prefs)
    {
        var results = new List<MatchResult>();
        var used = new HashSet<Episode>();
        foreach (var file in files)
        {
            var original = OriginalOf(file);
            var episode = MatchService.FindEpisode(selected, file.Detected, prefs, out var how);
            if (episode is not null && !used.Add(episode))
            {
                results.Add(new MatchResult
                {
                    File = file, Series = series, Confidence = MatchConfidence.Uncertain, OriginalNumbering = original,
                    Reasons = new[] { "Another file already uses this episode." },
                });
                continue;
            }

            results.Add(episode is null
                ? new MatchResult
                {
                    File = file, Series = series, Confidence = MatchConfidence.Uncertain, OriginalNumbering = original,
                    Reasons = new[] { how },
                }
                : MatchService.Manual(file, series, episode, null, original) with
                {
                    Association = AssociationMode.EpisodeNumber,
                    Reasons = new[] { how },
                });
        }

        var matched = results.Count(r => r.Episode is not null);
        return new AssociationPlan(results, matched == 0 && files.Count > 0 ? "No file name matches the selected episodes." : null);
    }

    private static Numbering OriginalOf(MediaFile file)
    {
        var d = file.Detected;
        return new Numbering(d.Season, d.Episodes.FirstOrDefault() is var e and > 0 ? e : null, d.Absolute);
    }
}
