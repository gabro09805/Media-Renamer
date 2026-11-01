using MediaRenamer.Bindings.Catalog;
using MediaRenamer.Core.Parsing;
using MediaRenamer.Model;

namespace MediaRenamer.Core.Subtitles;

public static class SubtitleDetector
{
    public static readonly string[] Extensions = { "srt", "ass", "ssa", "sub", "idx", "vtt" };

    private static readonly HashSet<string> ForcedTags = new(StringComparer.OrdinalIgnoreCase) { "forced", "forzati", "forzato" };
    private static readonly HashSet<string> SdhTags = new(StringComparer.OrdinalIgnoreCase) { "sdh", "hi", "cc" };
    private static readonly HashSet<string> CommentTags = new(StringComparer.OrdinalIgnoreCase) { "commentary", "comment", "commento" };

    public static bool IsSubtitle(string path) =>
        Extensions.Contains(Path.GetExtension(path).TrimStart('.'), StringComparer.OrdinalIgnoreCase);

    public static SubtitleFormat FormatOf(string ext) => ext.ToLowerInvariant() switch
    {
        "srt" => SubtitleFormat.Srt,
        "ass" => SubtitleFormat.Ass,
        "ssa" => SubtitleFormat.Ssa,
        "sub" => SubtitleFormat.Sub,
        "idx" => SubtitleFormat.Idx,
        "vtt" => SubtitleFormat.Vtt,
        _ => SubtitleFormat.Unknown,
    };

    /// <summary>Splits the name into the base (shared with the video) and the trailing language/flag tokens.</summary>
    public static (string Base, SubtitleFile Subtitle) Detect(string path, long size = 0)
    {
        var stem = Path.GetFileNameWithoutExtension(path);
        var tokens = stem.Split('.').ToList();
        string? language = null;
        bool forced = false, sdh = false, comment = false;
        var other = new List<string>();

        // Consume recognised tokens from the end; stop at the first one that is not a tag.
        while (tokens.Count > 1)
        {
            var t = tokens[^1];
            if (ForcedTags.Contains(t)) forced = true;
            else if (SdhTags.Contains(t)) sdh = true;
            else if (CommentTags.Contains(t)) comment = true;
            else if (language is null && IsLanguage(t)) language = Normalize(t);
            else break;
            tokens.RemoveAt(tokens.Count - 1);
        }

        var baseName = string.Join('.', tokens);
        var (info, _) = FileNameParser.Parse(baseName + ".x");
        var sub = new SubtitleFile
        {
            Path = path,
            Role = FileRole.Subtitle,
            Size = size,
            Detected = info,
            Format = FormatOf(Path.GetExtension(path).TrimStart('.')),
            Language = language,
            Forced = forced,
            Sdh = sdh,
            Comment = comment,
            OtherTags = other,
        };
        return (baseName, sub);
    }

    private static bool IsLanguage(string token)
    {
        var primary = token.Split('-', '_')[0];
        return primary.Length is 2 or 3 && LanguageCodes.Find(token) is not null
            || LanguageCodes.All.Any(l => l.Name.Equals(token, StringComparison.OrdinalIgnoreCase));
    }

    private static string Normalize(string token)
    {
        var byName = LanguageCodes.All.FirstOrDefault(l => l.Name.Equals(token, StringComparison.OrdinalIgnoreCase));
        return (byName ?? LanguageCodes.Find(token))!.Iso3;
    }
}
