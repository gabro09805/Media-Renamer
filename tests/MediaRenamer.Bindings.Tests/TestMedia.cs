using MediaRenamer.Bindings.Catalog;
using MediaRenamer.Model;

namespace MediaRenamer.Bindings.Tests;

internal static class TestMedia
{
    public static Series BreakingBad(string provider = "tmdb", string id = "1396") => new()
    {
        Source = new ProviderRef(provider, ProviderMode.Episode, id),
        Titles = new TitleSet(new[]
        {
            new LocalizedText("Breaking Bad", "en", TitleKind.Primary),
            new LocalizedText("Breaking Bad", "it"),
            new LocalizedText("ブレイキング・バッド", "ja"),
            new LocalizedText("Burei Kingu Baddo", "ja-Latn"),
        }),
        ReleaseDate = new DateOnly(2008, 1, 20),
        Type = SeriesType.Tv,
        Genres = new[] { "Drama", "Crime" },
        Ids = new ExternalIds(new Dictionary<string, string> { ["tmdb"] = "1396", ["imdb"] = "tt0903747" }),
        Seasons = new[] { new Season { Number = 1, Name = "Season 1" } },
    };

    public static Episode Episode(string provider = "tmdb") => new()
    {
        Source = new ProviderRef(provider, ProviderMode.Episode, "62085"),
        Titles = new TitleSet(new[]
        {
            new LocalizedText("Cat's in the Bag", "en", TitleKind.Primary),
            new LocalizedText("Il gatto è nel sacco", "it"),
        }),
        Number = new Numbering(1, 2, 2),
        AirDate = new DateOnly(2008, 1, 27),
        Orders = new Dictionary<string, Numbering> { ["dvd"] = new Numbering(1, 3), ["absolute"] = new Numbering(null, 12) },
    };

    public static MediaFile VideoFile() => new()
    {
        Path = @"C:\Downloads\Breaking.Bad.S01E02.720p.HDTV-GRP.mkv",
        Size = 1234,
        Detected = new DetectedInfo { ReleaseGroup = "GRP", Language = "en" },
        Technical = new TechnicalInfo { VideoFormat = "720p", VideoCodec = "x264", AudioCodec = "AAC", Width = 1280, Height = 720, Source = "HDTV" },
    };

    public static Movie Inception() => new()
    {
        Source = new ProviderRef("tmdb", ProviderMode.Movie, "27205"),
        Titles = new TitleSet(new[]
        {
            new LocalizedText("Inception", "en", TitleKind.Primary),
            new LocalizedText("Inception", "it"),
            new LocalizedText("インセプション", "ja"),
        }),
        ReleaseDate = new DateOnly(2010, 7, 16),
        Directors = new[] { "Christopher Nolan" },
        Genres = new[] { "Action", "Science Fiction" },
        Rating = 8.4,
        RuntimeMinutes = 148,
        Collection = null,
    };

    public static MediaBindingSource EpisodeSource(MatchPreferences? prefs = null, Series? series = null, Episode? episode = null, MediaFile? file = null) =>
        new(file ?? VideoFile(), null, series ?? BreakingBad(), episode ?? Episode(), prefs ?? new MatchPreferences("en"), new DateOnly(2026, 10, 30));

    public static MediaBindingSource MovieSource(MatchPreferences? prefs = null) =>
        new(new MediaFile { Path = @"C:\Downloads\Inception.2010.mkv" }, Inception(), null, null, prefs ?? new MatchPreferences("en"), new DateOnly(2026, 10, 30));
}

