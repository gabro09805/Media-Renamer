namespace MediaRenamer.Model;

public enum MatchConfidence { None, Uncertain, Probable, Reliable }

public enum AssociationMode { Auto, SearchOrder, EpisodeNumber, Manual }

public enum MatchMode { Opportunistic, Strict }

public enum EpisodeOrder { Airdate, Dvd, Absolute, Digital, StoryArc, Production, Official, DateAndTitle }

/// <summary>
/// Choices of the "Match Preferences" dialog. <see cref="OutputLanguage"/> only affects the names produced;
/// matching always compares every language variant. <see cref="MissingTranslation"/> decides what is used when the output language has no title.
/// </summary>
public sealed record MatchPreferences(
    string OutputLanguage = "en",
    MatchMode Mode = MatchMode.Opportunistic,
    EpisodeOrder Order = EpisodeOrder.Airdate,
    MissingTranslationBehavior MissingTranslation = MissingTranslationBehavior.UseEnglish);

/// <summary>Link between a local file and a provider entity (Movie or Episode).</summary>
public sealed record MatchResult
{
    public required MediaFile File { get; init; }
    public Movie? Movie { get; init; }
    public Episode? Episode { get; init; }
    /// <summary>Further episodes held by the same file (e.g. S01E01-02): <see cref="Episode"/> is the first one.</summary>
    public IReadOnlyList<Episode> MoreEpisodes { get; init; } = Array.Empty<Episode>();
    public Series? Series { get; init; }
    public double Score { get; init; }
    public MatchConfidence Confidence { get; init; }
    public IReadOnlyList<string> Reasons { get; init; } = Array.Empty<string>();
    public AssociationMode Association { get; init; }
    /// <summary>Numbering found in the original file name, preserved across Change Match and Search Episodes.</summary>
    public Numbering? OriginalNumbering { get; init; }
    public IReadOnlyList<SubtitleFile> Subtitles { get; init; } = Array.Empty<SubtitleFile>();
}

public enum PlanStatus { Ok, Unchanged, Collision, TargetExists, InvalidPath, Unmatched }

public sealed record RenamePlanItem(string Source, string? Target, PlanStatus Status, string? Warning = null);
