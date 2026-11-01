using MediaRenamer.Bindings.Catalog;
using MediaRenamer.Model;

namespace MediaRenamer.App.Services;

/// <summary>A ready-made media the format editor can preview against.</summary>
public sealed record SampleOption(string Name, FormatKind Kind, Func<MatchPreferences, MediaBindingSource> Create)
{
    public override string ToString() => Name;
}

/// <summary>Fixed sample data for the format editor, until real matches can be picked from a provider.</summary>
public static class SampleMedia
{
    public static IReadOnlyList<SampleOption> All { get; } = new[]
    {
        new SampleOption("Movie - Inception (TMDB)", FormatKind.Movie, p => new(InceptionFile(), Inception(), null, null, p)),
        new SampleOption("Series - Breaking Bad S01E02 (TMDB)", FormatKind.Series, p => new(SeriesFile(), null, BreakingBad(), BreakingBadEpisode(), p)),
        new SampleOption("Anime - Clevatess S01E01 (TMDB)", FormatKind.Series, p => new(AnimeFile(), null, Clevatess(), ClevatessEpisode(), p)),
        new SampleOption("Anime - Shingeki no Kyojin 1 (AniDB)", FormatKind.Series, p => new(SeriesFile(), null, Shingeki(), ShingekiEpisode(), p)),
        new SampleOption("Subtitle - Breaking Bad S01E02 (it, forced)", FormatKind.Series, p => new(SubtitleFile(), null, BreakingBad(), BreakingBadEpisode(), p)),
    };

    public static SampleOption DefaultFor(FormatKind kind) => All.First(s => s.Kind == kind);

    private static MediaFile InceptionFile() => new()
    {
        Path = @"C:\Downloads\Inception.2010.1080p.BluRay.x264-GRP.mkv",
        Size = 8_000_000_000,
        Detected = new DetectedInfo { ReleaseGroup = "GRP", Language = "en", Edition = "Director's Cut" },
        Technical = new TechnicalInfo
        {
            VideoFormat = "1080p", VideoCodec = "x264", AudioCodec = "AAC", Width = 1920, Height = 1080, Source = "BluRay",
            Channels = "5.1", BitDepth = 8, Fps = 23.976, BitrateKbps = 9800, DurationSeconds = 8880,
            AudioLanguages = new[] { "en" }, TextLanguages = new[] { "en", "it" },
        },
    };

    private static MediaFile SeriesFile() => new()
    {
        Path = @"C:\Downloads\Show.S01E02.720p.HDTV-GRP.mkv",
        Size = 1_200_000_000,
        Detected = new DetectedInfo { ReleaseGroup = "GRP", Language = "en" },
        Technical = new TechnicalInfo
        {
            VideoFormat = "720p", VideoCodec = "x264", AudioCodec = "AAC", Width = 1280, Height = 720, Source = "HDTV",
            Channels = "2.0", BitDepth = 8, Fps = 23.976, BitrateKbps = 3100, DurationSeconds = 2880,
            AudioLanguages = new[] { "en" },
        },
    };

    private static MediaFile AnimeFile() => new()
    {
        Path = @"D:\Anime\Clevatess.S01E01.1080p.WEB-DL.x265.10bit-GRP.mkv",
        Size = 1_450_000_000,
        Detected = new DetectedInfo { ReleaseGroup = "GRP", Language = "ja" },
        Technical = new TechnicalInfo
        {
            VideoFormat = "1080p", VideoCodec = "x265", AudioCodec = "AAC", Width = 1920, Height = 1080, Source = "WEB-DL",
            Channels = "2.0", BitDepth = 10, Hdr = "SDR", Fps = 23.976, BitrateKbps = 4300, DurationSeconds = 2712,
            AudioLanguages = new[] { "ja" }, TextLanguages = new[] { "en", "it" },
        },
    };

    private static SubtitleFile SubtitleFile() => new()
    {
        Path = @"C:\Downloads\Show.S01E02.720p.HDTV-GRP.it.forced.srt",
        Size = 42_000,
        Role = FileRole.Subtitle,
        Format = SubtitleFormat.Srt,
        Language = "it",
        Forced = true,
        Detected = new DetectedInfo { ReleaseGroup = "GRP", Language = "it" },
    };

    private static Series BreakingBad() => new()
    {
        Source = new ProviderRef("tmdb", ProviderMode.Episode, "1396"),
        Titles = new TitleSet(new[]
        {
            new LocalizedText("Breaking Bad", "en", TitleKind.Primary),
            new LocalizedText("Breaking Bad", "it"),
            new LocalizedText("Breaking Bad - Reazioni collaterali", null, TitleKind.Alias),
        }),
        OriginalLanguage = "en",
        ReleaseDate = new DateOnly(2008, 1, 20),
        Type = SeriesType.Tv,
        Genres = new[] { "Drama", "Crime" },
        Countries = new[] { "US" },
        Actors = new[] { "Bryan Cranston", "Aaron Paul", "Anna Gunn" },
        Network = "AMC",
        Status = "Ended",
        Rating = 8.9,
        Votes = 14000,
        Certification = "TV-MA",
        Keywords = new[] { "drug dealer", "cancer", "high school teacher" },
        Ids = new ExternalIds(new Dictionary<string, string> { ["tmdb"] = "1396", ["imdb"] = "tt0903747", ["tvdb"] = "81189" }),
        Seasons = new[] { new Season { Number = 1, Name = "Season 1", Year = 2008 }, new Season { Number = 2, Name = "Season 2", Year = 2009 } },
    };

    private static Episode BreakingBadEpisode() => new()
    {
        Source = new ProviderRef("tmdb", ProviderMode.Episode, "62085"),
        Titles = new TitleSet(new[]
        {
            new LocalizedText("Cat's in the Bag...", "en", TitleKind.Primary),
            new LocalizedText("Il gatto è nel sacco", "it"),
        }),
        Number = new Numbering(1, 2, 2),
        AirDate = new DateOnly(2008, 1, 27),
        RuntimeMinutes = 48,
        Orders = new Dictionary<string, Numbering> { ["dvd"] = new Numbering(1, 2) },
    };

    private static Movie Inception() => new()
    {
        Source = new ProviderRef("tmdb", ProviderMode.Movie, "27205"),
        Titles = new TitleSet(new[]
        {
            new LocalizedText("Inception", "en", TitleKind.Primary),
            new LocalizedText("Inception", "it"),
            new LocalizedText("インセプション", "ja"),
        }),
        OriginalLanguage = "en",
        ReleaseDate = new DateOnly(2010, 7, 16),
        Directors = new[] { "Christopher Nolan" },
        Actors = new[] { "Leonardo DiCaprio", "Joseph Gordon-Levitt", "Elliot Page" },
        Genres = new[] { "Action", "Science Fiction", "Adventure" },
        Countries = new[] { "US", "GB" },
        Certification = "PG-13",
        Rating = 8.4,
        Votes = 36000,
        RuntimeMinutes = 148,
        Keywords = new[] { "dream", "thief", "subconscious" },
        Ids = new ExternalIds(new Dictionary<string, string> { ["tmdb"] = "27205", ["imdb"] = "tt1375666" }),
    };

    private static Series Clevatess() => new()
    {
        Source = new ProviderRef("tmdb", ProviderMode.Episode, "258348"),
        Titles = new TitleSet(new[]
        {
            new LocalizedText("Clevatess", "en", TitleKind.Primary),
            new LocalizedText("クレバテス-魔獣の王と赤子と屍の勇者", "ja", TitleKind.Original),
            new LocalizedText("Clevatess", "de"),
            new LocalizedText("Clevatess", "it"),
            new LocalizedText("Clevatess: Majuu no Ou to Akago to Shikabane no Yuusha", "ja-Latn"),
            new LocalizedText("Clevatess: The King of Devil Beasts, The Baby and the Brave of Undead", null, TitleKind.Alias),
            new LocalizedText("클레바테스", null, TitleKind.Alias),
        }),
        OriginalLanguage = "ja",
        ReleaseDate = new DateOnly(2025, 7, 2),
        Type = SeriesType.Anime,
        Genres = new[] { "Animation", "Action & Adventure", "Sci-Fi & Fantasy", "Drama" },
        Countries = new[] { "JP" },
        Actors = new[] { "Haruka Shiraishi", "Mutsumi Tamura", "Yuichi Nakamura", "Saya Aizawa" },
        Network = "AT-X",
        Status = "Ended",
        Rating = 8.1,
        Votes = 116,
        Keywords = new[] { "revenge", "based on manga", "parenting", "dark fantasy", "seinen", "anime" },
        Ids = new ExternalIds(new Dictionary<string, string> { ["tmdb"] = "258348", ["tvdb"] = "451793", ["imdb"] = "tt32991344" }),
        Seasons = new[] { new Season { Number = 1, Name = "Season 1", Year = 2025 }, new Season { Number = 2, Name = "Season 2", Year = 2026 } },
    };

    private static Episode ClevatessEpisode() => new()
    {
        Source = new ProviderRef("tmdb", ProviderMode.Episode, "5451528"),
        Titles = new TitleSet(new[]
        {
            new LocalizedText("The Lord of Dark Beasts", "en", TitleKind.Primary),
            new LocalizedText("Der Bestienkönig", "de"),
            new LocalizedText("魔獣の王", "ja"),
            new LocalizedText("Il Signore delle Bestie Oscure", "it"),
        }),
        Number = new Numbering(1, 1, 1),
        AirDate = new DateOnly(2025, 7, 2),
        RuntimeMinutes = 45,
        Orders = new Dictionary<string, Numbering> { ["dvd"] = new Numbering(1, 1), ["absolute"] = new Numbering(null, 1, 1) },
    };

    private static Series Shingeki() => new()
    {
        Source = new ProviderRef("anidb", ProviderMode.Episode, "9541"),
        Titles = new TitleSet(new[]
        {
            new LocalizedText("Shingeki no Kyojin", "ja-Latn", TitleKind.Primary),
            new LocalizedText("Attack on Titan", "en"),
            new LocalizedText("L'attacco dei Giganti", "it"),
            new LocalizedText("進撃の巨人", "ja"),
        }),
        OriginalLanguage = "ja",
        ReleaseDate = new DateOnly(2013, 4, 7),
        Type = SeriesType.Anime,
        Ids = new ExternalIds(new Dictionary<string, string> { ["anidb"] = "9541" }),
        Seasons = new[] { new Season { Number = 1, Name = "Season 1" } },
    };

    private static Episode ShingekiEpisode() => new()
    {
        Source = new ProviderRef("anidb", ProviderMode.Episode, "1"),
        Titles = new TitleSet(new[]
        {
            new LocalizedText("To You, in 2000 Years", "en", TitleKind.Primary),
            new LocalizedText("A te, fra duemila anni", "it"),
        }),
        Number = new Numbering(1, 1, 1),
        AirDate = new DateOnly(2013, 4, 7),
        Orders = new Dictionary<string, Numbering> { ["absolute"] = new Numbering(null, 1, 1) },
    };
}
