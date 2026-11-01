namespace MediaRenamer.Model;

public enum SeriesType { Unknown, Tv, Anime, Ova, Special, Movie }

/// <summary>Fields shared by every provider-sourced entity.</summary>
public abstract record MediaEntity
{
    public required ProviderRef Source { get; init; }
    public TitleSet Titles { get; init; } = TitleSet.Empty;
    public string? OriginalLanguage { get; init; }
    public string? Overview { get; init; }
    public Uri? PosterUri { get; init; }
    public ExternalIds Ids { get; init; } = ExternalIds.Empty;
    public DateOnly? ReleaseDate { get; init; }
    public int? Year => ReleaseDate?.Year ?? YearOverride;
    public int? YearOverride { get; init; }
    public IReadOnlyList<string> Keywords { get; init; } = Array.Empty<string>();
}

public sealed record Movie : MediaEntity
{
    public IReadOnlyList<string> Directors { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> Actors { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> Genres { get; init; } = Array.Empty<string>();
    public string? Collection { get; init; }
    public int? CollectionIndex { get; init; }
    public string? Certification { get; init; }
    public double? Rating { get; init; }
    public int? Votes { get; init; }
    public int? RuntimeMinutes { get; init; }
    public IReadOnlyList<string> Countries { get; init; } = Array.Empty<string>();
}

public sealed record Series : MediaEntity
{
    public SeriesType Type { get; init; }
    public IReadOnlyList<Season> Seasons { get; init; } = Array.Empty<Season>();
    public int SeasonCount => Seasons.Count;
    public IReadOnlyList<string> Genres { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> Countries { get; init; } = Array.Empty<string>();
    public string? Certification { get; init; }
    public IReadOnlyList<string> Actors { get; init; } = Array.Empty<string>();
    public string? Network { get; init; }
    public string? Status { get; init; }
    public double? Rating { get; init; }
    public int? Votes { get; init; }
}

public sealed record Season
{
    public required int Number { get; init; }
    public string? Name { get; init; }
    public int? Year { get; init; }
    /// <summary>Episodes in the order returned by the provider (never re-sorted).</summary>
    public IReadOnlyList<Episode> Episodes { get; init; } = Array.Empty<Episode>();
}

/// <summary>Episode numbering under the different orderings a provider may expose (airdate, DVD, absolute, ...).</summary>
public sealed record Numbering(int? Season, int? Episode, int? Absolute = null, int? Special = null)
{
    public static readonly Numbering None = new(null, null);
}

public sealed record Episode : MediaEntity
{
    public required Numbering Number { get; init; }
    /// <summary>Alternative orderings by name: "airdate", "dvd", "absolute", "production", "official", ...</summary>
    public IReadOnlyDictionary<string, Numbering> Orders { get; init; } = new Dictionary<string, Numbering>();
    public DateOnly? AirDate { get; init; }
    public int? RuntimeMinutes { get; init; }
    public ProviderRef? SeriesRef { get; init; }
    public bool IsSpecial => Number.Special is not null || Number.Season == 0;
}
