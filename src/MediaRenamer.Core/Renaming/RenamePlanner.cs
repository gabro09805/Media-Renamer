using MediaRenamer.Bindings.Catalog;
using MediaRenamer.Bindings.Formats;
using MediaRenamer.Model;

namespace MediaRenamer.Core.Renaming;

public enum PlanState
{
    Ready,
    Unchanged,
    NoMatch,
    NeedsReview,
    FormatError,
    InvalidPath,
    Collision,
    TargetExists,
}

public sealed record PlanItem(string Source, string? Target, bool IsSubtitle, PlanState Status, string? Message = null, MatchResult? Match = null)
{
    public bool CanRename => Status == PlanState.Ready;
}

public sealed record RenameOptions(string MovieFormat, string SeriesFormat, string? LibraryRoot = null);

/// <summary>Computes the new path of every file without touching the disk (except existence checks).</summary>
public sealed class RenamePlanner
{
    private const int MaxPath = 259;
    private readonly FormatRenderer _renderer;

    public RenamePlanner(FormatRenderer? renderer = null) => _renderer = renderer ?? new FormatRenderer();

    public IReadOnlyList<PlanItem> Plan(
        IReadOnlyList<MatchResult> matches,
        IReadOnlyList<SubtitleFile> subtitles,
        MatchPreferences prefs,
        RenameOptions options)
    {
        var items = new List<PlanItem>();
        var videoTargets = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var m in matches)
        {
            var src = m.File.Path;
            var standalone = m.File as SubtitleFile;
            var isSub = standalone is not null;
            if (m.Movie is null && m.Episode is null)
            {
                items.Add(new PlanItem(src, null, isSub, PlanState.NoMatch, m.Reasons.FirstOrDefault() ?? "No match.", m));
                continue;
            }

            var format = m.Movie is not null ? options.MovieFormat : options.SeriesFormat;
            var render = _renderer.Render(format, MediaBindingSource.From(m, prefs));
            if (!render.Success)
            {
                items.Add(new PlanItem(src, null, isSub, PlanState.FormatError, render.Error!.Message, m));
                continue;
            }

            var root = options.LibraryRoot ?? Path.GetDirectoryName(Path.GetFullPath(src))!;
            // A subtitle matched on its own keeps its language/flag tags, e.g. ".it.forced.srt".
            var ext = standalone is not null ? SubtitleSuffix(standalone) : m.File.Extension.Length > 0 ? "." + m.File.Extension : "";
            var target = BuildPath(root, render.Segments, ext, out var error);
            if (target is null)
            {
                items.Add(new PlanItem(src, null, isSub, PlanState.InvalidPath, error, m));
                continue;
            }

            if (!isSub) videoTargets[Path.GetFullPath(src)] = target;
            items.Add(new PlanItem(src, target, isSub, PlanState.Ready, string.Join(" ", render.Warnings), m));
        }

        var matchedAlone = matches.Where(m => m.File is SubtitleFile).Select(m => Path.GetFullPath(m.File.Path)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var s in subtitles)
        {
            if (matchedAlone.Contains(Path.GetFullPath(s.Path))) continue;
            if (s.LinkedVideoPath is null || s.Link is LinkConfidence.None)
            {
                items.Add(new PlanItem(s.Path, null, true, PlanState.NeedsReview, "Subtitle not associated with a video."));
                continue;
            }
            if (s.Link == LinkConfidence.Low)
            {
                items.Add(new PlanItem(s.Path, null, true, PlanState.NeedsReview, "Low-confidence association: confirm it manually."));
                continue;
            }
            if (!videoTargets.TryGetValue(Path.GetFullPath(s.LinkedVideoPath), out var videoTarget))
            {
                items.Add(new PlanItem(s.Path, null, true, PlanState.NoMatch, "The linked video has no match."));
                continue;
            }

            var target = Path.Combine(Path.GetDirectoryName(videoTarget)!, Path.GetFileNameWithoutExtension(videoTarget) + SubtitleSuffix(s));
            items.Add(new PlanItem(s.Path, target, true, target.Length > MaxPath ? PlanState.InvalidPath : PlanState.Ready,
                target.Length > MaxPath ? "Path too long." : null));
        }

        return Validate(items);
    }

    public static string SubtitleSuffix(SubtitleFile s)
    {
        var parts = new List<string>();
        if (!string.IsNullOrEmpty(s.Language)) parts.Add(s.Language!);
        if (s.Forced) parts.Add("forced");
        if (s.Sdh) parts.Add("sdh");
        if (s.Comment) parts.Add("comment");
        parts.AddRange(s.OtherTags);
        return (parts.Count > 0 ? "." + string.Join('.', parts) : "") + "." + s.Extension.ToLowerInvariant();
    }

    private static string? BuildPath(string root, IReadOnlyList<string> segments, string ext, out string? error)
    {
        error = null;
        if (segments.Any(string.IsNullOrWhiteSpace)) { error = "The format produced an empty path segment."; return null; }
        var parts = segments.ToArray();
        // A format starting with a drive ("{drive}/Media/...") is an absolute destination chosen by the user.
        var absolute = OperatingSystem.IsWindows() && PathSanitizer.IsDriveLetter(parts[0]);
        if (absolute && parts.Length == 1) { error = "A drive alone is not a file name: add folders and a name after it."; return null; }
        parts[^1] += ext;

        string full;
        try
        {
            full = absolute
                ? Path.GetFullPath(Path.Combine(parts[0] + Path.DirectorySeparatorChar, Path.Combine(parts[1..])))
                : Path.GetFullPath(Path.Combine(new[] { root }.Concat(parts).ToArray()));
        }
        catch (Exception ex) { error = ex.Message; return null; }

        var rootFull = Path.GetFullPath(root);
        var rootDir = Path.TrimEndingDirectorySeparator(rootFull) + Path.DirectorySeparatorChar;
        if (!absolute && !full.StartsWith(rootDir, StringComparison.OrdinalIgnoreCase)) { error = "The path escapes the destination folder."; return null; }
        if (full.Length > MaxPath) { error = $"Path too long ({full.Length} characters)."; return null; }
        return full;
    }

    private static IReadOnlyList<PlanItem> Validate(List<PlanItem> items)
    {
        var sources = items.Select(i => Path.GetFullPath(i.Source)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var seen = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var result = new List<PlanItem>();

        foreach (var item in items)
        {
            if (!item.CanRename || item.Target is null) { result.Add(item); continue; }

            var src = Path.GetFullPath(item.Source);
            if (string.Equals(src, item.Target, StringComparison.Ordinal))
            {
                result.Add(item with { Status = PlanState.Unchanged, Message = "Already has the right name." });
                continue;
            }
            if (!seen.TryAdd(item.Target, 1))
            {
                result.Add(item with { Status = PlanState.Collision, Message = "Another file would get the same name." });
                continue;
            }
            // A target that is also a source is freed by the executor; only foreign existing files block.
            var isCaseOnly = string.Equals(src, item.Target, StringComparison.OrdinalIgnoreCase);
            if (!isCaseOnly && !sources.Contains(item.Target) && (File.Exists(item.Target) || Directory.Exists(item.Target)))
            {
                result.Add(item with { Status = PlanState.TargetExists, Message = "A file with this name already exists (never overwritten)." });
                continue;
            }
            result.Add(item);
        }
        return result;
    }
}
