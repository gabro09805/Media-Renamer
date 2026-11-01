using System.Globalization;
using MediaRenamer.Bindings.Expressions;
using MediaRenamer.Model;

namespace MediaRenamer.Bindings.Catalog;

/// <summary>Bindings that complete the FileBot-compatible set. Same rule as the rest: only canonical data is read.</summary>
internal static class ExtendedBindings
{
    private static BindingDefinition D(string name, string category, string description, string example, Func<MediaBindingSource, object?> resolve) =>
        new(name, category, description, example, resolve);

    private static string Two(int? n) => n is { } v ? v.ToString("00", CultureInfo.InvariantCulture) : "";

    public static ValueBag? Language(string? tag)
    {
        if (string.IsNullOrWhiteSpace(tag)) return null;
        var info = LanguageCodes.Find(tag);
        return new ValueBag(tag, new Dictionary<string, object?>
        {
            ["ISO2"] = info?.Iso2 ?? tag,
            ["ISO3"] = info?.Iso3,
            ["name"] = info?.Name ?? tag,
        });
    }

    private static ValueBag OriginalLanguage(MediaBindingSource m)
    {
        var info = LanguageCodes.Find(m.Subject?.OriginalLanguage);
        return new ValueBag(info?.Iso3 ?? m.Subject!.OriginalLanguage!, new Dictionary<string, object?>
        {
            ["ISO2"] = info?.Iso2 ?? m.Subject!.OriginalLanguage,
            ["ISO3"] = info?.Iso3,
            ["name"] = info?.Name ?? m.Subject!.OriginalLanguage,
        });
    }

    private static ValueBag? NumberingBag(MediaBindingSource m, Numbering? n)
    {
        if (n is null) return null;
        var s = n.Season ?? m.Episode?.Number.Season;
        var e = n.Episode;
        return new ValueBag("", new Dictionary<string, object?>
        {
            ["s"] = s,
            ["e"] = e,
            ["s00"] = s is null ? null : Two(s),
            ["e00"] = e is null ? null : Two(e),
            ["sxe"] = s is null || e is null ? null : $"{s}x{Two(e)}",
            ["s00e00"] = s is null || e is null ? null : $"S{Two(s)}E{Two(e)}",
            ["absolute"] = n.Absolute,
        });
    }

    private static string EpisodeLine(MediaBindingSource m)
    {
        var sxe = m.SeasonEpisodeText((s, e) => $"{s}x{e:00}", n => $"{n:00}");
        return string.Join(" - ", new[] { m.SeriesOrMovieName(m.Language), sxe, m.EpisodeTitleIn(m.Language) }.Where(p => !string.IsNullOrEmpty(p)));
    }

    private static IReadOnlyList<string> Aliases(MediaBindingSource m)
    {
        var main = m.SeriesOrMovieName(m.Language);
        return (m.Subject?.Titles.All ?? Array.Empty<LocalizedText>()).Select(t => t.Value)
            .Where(v => !string.Equals(v, main, StringComparison.Ordinal)).Distinct().ToList();
    }

    private static TechnicalInfo? Tech(MediaBindingSource m) => m.File?.Technical;

    private static object? Duration(MediaBindingSource m, Func<double, object> pick) =>
        Tech(m)?.DurationSeconds is { } d ? pick(d) : null;

    public static readonly IReadOnlyList<BindingDefinition> All = new[]
    {
        D("startdate", "Name", "Start date of the series, or release date of the movie", "2025-07-02",
            m => m.Series?.ReleaseDate ?? m.Movie?.ReleaseDate),
        D("primaryTitle", "Name", "Primary title as given by the provider (romanized for anime)", "Shingeki no Kyojin",
            m => m.Subject?.Titles.All.FirstOrDefault(t => t.Kind == TitleKind.Primary)?.Value),
        D("alias", "Name", "Other titles and aliases (a list)", "Attack on Titan, ...", m => Aliases(m)),
        D("az", "Name", "First letter of the name, for A-Z folders (# for non letters)", "B",
            m => m.SeriesOrMovieName(m.Language) is { Length: > 0 } n ? (char.IsLetter(n[0]) ? char.ToUpperInvariant(n[0]).ToString() : "#") : null),
        D("decade", "Name", "Decade of the year", "2020", m => m.Year is { } y ? y / 10 * 10 : null),
        D("age", "Name", "Days between the air/release date and today", "485",
            m => m.Date is DateOnly d ? (long)(m.Today.DayNumber - d.DayNumber) : null),

        D("es", "Episode", "Episode numbers (a list)", "[1]", m => m.EpisodeNumber is { } e ? m.AllEpisodes.Select(x => m.NumberingOf(x).Episode).Where(n => n is not null).Select(n => (object?)(long)n!.Value).ToList() is { Count: > 0 } l ? l : new List<object?> { (long)e } : null),
        D("sy", "Episode", "Years of the season (a list)", "[2025]",
            m => (m.Series?.Seasons.FirstOrDefault(s => s.Number == m.SeasonNumber)?.Year ?? m.Year) is { } y ? new List<object?> { (long)y } : null),
        D("episode", "Episode", "The episode as text; also episode.title, .season, .number, .absolute, .airdate", "Show - 1x01 - Pilot",
            m => m.Episode is null ? null : new ValueBag(EpisodeLine(m), new Dictionary<string, object?>
            {
                ["title"] = m.EpisodeTitleIn(m.Language),
                ["season"] = m.SeasonNumber,
                ["number"] = m.EpisodeNumber,
                ["absolute"] = m.AbsoluteNumber,
                ["airdate"] = m.Episode.AirDate,
            })),
        D("series", "Episode", "The series as text; also series.name, .database, .actors, .aliases, .network, .status", "Breaking Bad",
            m => m.Series is null ? null : new ValueBag(m.SeriesOrMovieName(m.Language) ?? "", new Dictionary<string, object?>
            {
                ["name"] = m.SeriesOrMovieName(m.Language),
                ["database"] = m.Series.Source.ProviderId,
                ["actors"] = m.Series.Actors,
                ["aliases"] = Aliases(m),
                ["network"] = m.Series.Network,
                ["status"] = m.Series.Status,
            })),
        D("movie", "Name", "The movie as text; also movie.collection, .alternativeTitles", "Inception",
            m => m.Movie is null ? null : new ValueBag(m.SeriesOrMovieName(m.Language) ?? "", new Dictionary<string, object?>
            {
                ["collection"] = m.Movie.Collection,
                ["alternativeTitles"] = Aliases(m),
            })),
        D("order", "Episode", "Numbering in a given episode order: order.dvd.sxe, order.airdate.s00e00, order.absolute.absolute", "1x03",
            m => m.Episode is null ? null : new ValueBag("", new[] { ("airdate", m.Episode.Number) }
                .Concat(m.Episode.Orders.Select(kv => (kv.Key.ToLowerInvariant(), kv.Value)))
                .GroupBy(t => t.Item1).ToDictionary(g => g.Key, g => (object?)NumberingBag(m, g.Last().Item2)))),

        D("type", "Episode", "Episode or Movie", "Episode", m => m.Episode is not null ? "Episode" : m.Movie is not null ? "Movie" : null),
        D("seriesType", "Episode", "Series type (Tv, Anime, Ova, Special, Movie)", "Anime", m => m.Series?.Type.ToString()),
        D("anime", "Episode", "True when the series is an anime", "true", m => m.Series is null ? null : m.Series.Type == SeriesType.Anime),
        D("regular", "Episode", "True for regular episodes, false for specials", "true", m => m.Episode is null ? null : !m.Episode.IsSpecial),

        D("keywords", "Metadata", "Keywords (a list)", "dark fantasy, anime", m => m.Subject?.Keywords),
        D("ci", "Metadata", "Position of the movie in its collection", "2", m => m.Movie?.CollectionIndex),
        D("language", "Metadata", "Original language: prints ISO 639-2 (jpn); also .ISO2, .ISO3, .name", "jpn",
            m => m.Subject?.OriginalLanguage is null ? null : OriginalLanguage(m)),
        D("languages", "Metadata", "Original languages as ISO 639-2 codes (a list)", "jpn",
            m => m.Subject?.OriginalLanguage is null ? null : new List<object?> { OriginalLanguage(m).ToString() }),
        D("country", "Metadata", "First production country", "JP",
            m => ((IReadOnlyList<string>?)m.Movie?.Countries ?? m.Series?.Countries)?.FirstOrDefault()),

        D("f", "File", "The file; also f.name, .nameWithoutExtension, .extension, .parent, .path", "Show.S01E01.mkv",
            m => FileBag(m)),
        D("mediaFile", "File", "Same as f", "Show.S01E01.mkv", m => FileBag(m)),
        D("drive", "File", "Drive of the file", "C:", m => m.File is null ? null : Path.GetPathRoot(m.File.Path)?.TrimEnd('\\', '/')),
        D("root", "File", "Root of the file path", "C:\\", m => m.File is null ? null : Path.GetPathRoot(m.File.Path)),
        D("bytes", "File", "Size in bytes", "734003200", m => m.File is null || m.File.Size == 0 ? null : m.File.Size),
        D("duration", "File", "Duration as h:mm:ss", "0:45:12",
            m => Duration(m, d => TimeSpan.FromSeconds(Math.Round(d)).ToString(@"h\:mm\:ss", CultureInfo.InvariantCulture))),
        D("seconds", "File", "Duration in seconds", "2712", m => Duration(m, d => (long)Math.Round(d))),
        D("minutes", "File", "Duration in minutes", "45", m => Duration(m, d => (long)Math.Round(d / 60))),
        D("hours", "File", "Duration in hours", "0.75", m => Duration(m, d => Math.Round(d / 3600, 2))),
        D("resolution", "File", "Width x height", "1920x1080",
            m => Tech(m) is { Width: { } w, Height: { } h } ? $"{w}x{h}" : null),
        D("hd", "File", "UHD, HD or SD according to the height", "HD",
            m => Tech(m)?.Height is { } h ? (h >= 2160 ? "UHD" : h >= 720 ? "HD" : "SD") : null),
        D("channels", "File", "Audio channels", "5.1", m => Tech(m)?.Channels),
        D("bitdepth", "File", "Video bit depth", "10", m => Tech(m)?.BitDepth),
        D("hdr", "File", "HDR format", "HDR10", m => Tech(m)?.Hdr),
        D("fps", "File", "Frames per second", "23.976", m => Tech(m)?.Fps),
        D("bitrate", "File", "Overall bitrate in kbps", "5200", m => Tech(m)?.BitrateKbps),
        D("edition", "File", "Edition detected in the file name", "Director's Cut", m => m.File?.Detected.Edition),
        D("audioLanguages", "File", "Audio languages (a list)", "ja", m => Tech(m)?.AudioLanguages is { Count: > 0 } l ? l : null),
        D("textLanguages", "File", "Subtitle languages inside the file (a list)", "en, it", m => Tech(m)?.TextLanguages is { Count: > 0 } l ? l : null),

        D("plex", "Library", "Plex layout (relative path); also plex.year, plex.id, plex.year.id, plex.name", "TV Shows\\Show\\Season 01\\Show - S01E01 - Pilot",
            m => LibraryNaming.Build(m, LibraryNaming.Style.Plex)),
        D("kodi", "Library", "Kodi layout; also kodi.id, kodi.name", "TV Shows\\Show (2020)\\Season 1\\Show (2020) - 1x01 - Pilot",
            m => LibraryNaming.Build(m, LibraryNaming.Style.Kodi)),
        D("emby", "Library", "Emby layout; also emby.id, emby.name", "TV Shows\\Show (2020)\\Season 01\\Show (2020) - S01E01 - Pilot",
            m => LibraryNaming.Build(m, LibraryNaming.Style.Emby)),
        D("jellyfin", "Library", "Jellyfin layout; also jellyfin.id, jellyfin.name", "TV Shows\\Show (2020)\\Season 01\\Show (2020) - S01E01 - Pilot",
            m => LibraryNaming.Build(m, LibraryNaming.Style.Jellyfin)),
    };

    private static ValueBag? FileBag(MediaBindingSource m)
    {
        if (m.File is null) return null;
        var path = m.File.Path;
        return new ValueBag(path, new Dictionary<string, object?>
        {
            ["name"] = Path.GetFileName(path),
            ["nameWithoutExtension"] = m.File.OriginalName,
            ["extension"] = m.File.Extension,
            ["parent"] = Path.GetDirectoryName(path),
            ["path"] = path,
        });
    }
}
