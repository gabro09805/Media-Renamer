using MediaRenamer.Core.Parsing;
using MediaRenamer.Model;

namespace MediaRenamer.Core.Import;

public sealed record ImportResult(IReadOnlyList<MediaFile> Videos, IReadOnlyList<SubtitleFile> Subtitles);

/// <summary>Collects video and subtitle files from files and folders (recursive) and links subtitles to videos.</summary>
public static class FileImporter
{
    public static readonly string[] VideoExtensions =
        { "mkv", "mp4", "avi", "mov", "wmv", "m4v", "mpg", "mpeg", "ts", "m2ts", "webm", "flv", "ogm" };

    /// <summary>Expands folders recursively; only existing files are returned.</summary>
    public static IReadOnlyList<string> Expand(IEnumerable<string> paths)
    {
        var files = new List<string>();
        foreach (var p in paths)
        {
            if (Directory.Exists(p))
                files.AddRange(Directory.EnumerateFiles(p, "*", SearchOption.AllDirectories));
            else if (File.Exists(p))
                files.Add(p);
        }
        return files;
    }

    /// <summary>The file name gives no real title (e.g. "video", "a1b2c3", "00123"): the container tag is worth trying.</summary>
    public static bool HasWeakTitle(DetectedInfo info) =>
        !System.Text.RegularExpressions.Regex.IsMatch(info.Title ?? "", @"\p{L}{3,}") ||
        System.Text.RegularExpressions.Regex.IsMatch(info.Title ?? "", @"^(video|movie|film|untitled|output|new)\b", System.Text.RegularExpressions.RegexOptions.IgnoreCase);

    public static ImportResult Import(IEnumerable<string> paths)
    {
        var files = Expand(paths);

        var videos = new List<MediaFile>();
        var subs = new List<(string, SubtitleFile)>();
        foreach (var f in files.Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
        {
            var ext = Path.GetExtension(f).TrimStart('.');
            long size = new FileInfo(f).Length;
            if (VideoExtensions.Contains(ext, StringComparer.OrdinalIgnoreCase))
            {
                var (info, tech) = FileNameParser.Parse(f);
                if (HasWeakTitle(info) && ContainerTitleReader.ReadTitle(f) is { } embedded)
                {
                    var (fromTag, _) = FileNameParser.Parse(embedded + Path.GetExtension(f));
                    info = fromTag with { Season = info.Season ?? fromTag.Season, Episodes = info.Episodes.Count > 0 ? info.Episodes : fromTag.Episodes };
                }
                videos.Add(new MediaFile { Path = f, Role = FileRole.Video, Size = size, Detected = info, Technical = tech });
            }
            else if (Subtitles.SubtitleDetector.IsSubtitle(f))
            {
                subs.Add(Subtitles.SubtitleDetector.Detect(f, size));
            }
        }

        return new ImportResult(videos, Subtitles.SubtitleLinker.Link(videos, subs));
    }
}
