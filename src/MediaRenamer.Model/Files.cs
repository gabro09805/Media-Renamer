namespace MediaRenamer.Model;

/// <summary>Information inferred from a file name before any provider lookup.</summary>
public sealed record DetectedInfo
{
    public string? Title { get; init; }
    public int? Year { get; init; }
    public int? Season { get; init; }
    public IReadOnlyList<int> Episodes { get; init; } = Array.Empty<int>();
    public int? Absolute { get; init; }
    public string? EpisodeTitle { get; init; }
    public string? ReleaseGroup { get; init; }
    public string? Edition { get; init; }
    public string? Language { get; init; }
}

/// <summary>Technical stream info, from the file name first and a built-in header parser afterwards.</summary>
public sealed record TechnicalInfo
{
    public string? VideoFormat { get; init; }     // vf
    public string? VideoCodec { get; init; }      // vc
    public string? AudioCodec { get; init; }      // ac
    public int? Width { get; init; }
    public int? Height { get; init; }
    public string? Source { get; init; }
    public string? Channels { get; init; }        // "5.1"
    public int? BitDepth { get; init; }
    public string? Hdr { get; init; }
    public double? Fps { get; init; }
    public int? BitrateKbps { get; init; }
    public double? DurationSeconds { get; init; }
    public IReadOnlyList<string> AudioLanguages { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> TextLanguages { get; init; } = Array.Empty<string>();
}

public enum FileRole { Video, Subtitle, Other }

public record MediaFile
{
    public required string Path { get; init; }
    public FileRole Role { get; init; } = FileRole.Video;
    public string OriginalName => System.IO.Path.GetFileNameWithoutExtension(Path);
    public string Extension => System.IO.Path.GetExtension(Path).TrimStart('.');
    public long Size { get; init; }
    public DetectedInfo Detected { get; init; } = new();
    public TechnicalInfo? Technical { get; init; }
}

public enum SubtitleFormat { Srt, Ass, Ssa, Sub, Idx, Vtt, Unknown }

public enum LinkConfidence { None, Low, High, Manual }

public sealed record SubtitleFile : MediaFile
{
    public SubtitleFormat Format { get; init; }
    public string? Language { get; init; }
    public bool Forced { get; init; }
    public bool Sdh { get; init; }
    public bool Comment { get; init; }
    public IReadOnlyList<string> OtherTags { get; init; } = Array.Empty<string>();
    /// <summary>Path of the associated video, or null when not confidently linked (user is warned).</summary>
    public string? LinkedVideoPath { get; init; }
    public LinkConfidence Link { get; init; }
}
