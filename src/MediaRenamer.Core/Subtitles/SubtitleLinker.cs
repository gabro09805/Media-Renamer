using MediaRenamer.Model;

namespace MediaRenamer.Core.Subtitles;

/// <summary>Associates subtitles with videos; ambiguous cases get a low confidence instead of a guess.</summary>
public static class SubtitleLinker
{
    private static readonly string[] SubFolders = { "subs", "sub", "subtitles", "sottotitoli" };

    public static IReadOnlyList<SubtitleFile> Link(IReadOnlyList<MediaFile> videos, IReadOnlyList<(string Base, SubtitleFile Sub)> subtitles)
    {
        var result = new List<SubtitleFile>();
        foreach (var (baseName, sub) in subtitles)
        {
            var dir = Path.GetDirectoryName(sub.Path) ?? "";
            var dirs = new List<string> { dir };
            if (SubFolders.Contains(Path.GetFileName(dir), StringComparer.OrdinalIgnoreCase) && Path.GetDirectoryName(dir) is { } parent)
                dirs.Add(parent);

            var candidates = videos.Where(v => dirs.Contains(Path.GetDirectoryName(v.Path) ?? "", StringComparer.OrdinalIgnoreCase)).ToList();

            var exact = candidates.Where(v => string.Equals(v.OriginalName, baseName, StringComparison.OrdinalIgnoreCase)).ToList();
            if (exact.Count == 1) { result.Add(sub with { LinkedVideoPath = exact[0].Path, Link = LinkConfidence.High }); continue; }

            var d = sub.Detected;
            if (d.Season is not null && d.Episodes.Count > 0)
            {
                var byNumber = candidates.Where(v => v.Detected.Season == d.Season && v.Detected.Episodes.Intersect(d.Episodes).Any()).ToList();
                if (byNumber.Count == 1) { result.Add(sub with { LinkedVideoPath = byNumber[0].Path, Link = LinkConfidence.High }); continue; }
            }
            else if (d.Absolute is not null)
            {
                var byAbs = candidates.Where(v => v.Detected.Absolute == d.Absolute).ToList();
                if (byAbs.Count == 1) { result.Add(sub with { LinkedVideoPath = byAbs[0].Path, Link = LinkConfidence.High }); continue; }
            }

            // A lone video next to the subtitle is likely, but not certain, its owner.
            if (candidates.Count == 1) { result.Add(sub with { LinkedVideoPath = candidates[0].Path, Link = LinkConfidence.Low }); continue; }

            result.Add(sub with { LinkedVideoPath = null, Link = LinkConfidence.None });
        }
        return result;
    }
}
