using System.Globalization;
using MediaRenamer.Bindings.Expressions;
using MediaRenamer.Model;

namespace MediaRenamer.Bindings.Catalog;

/// <summary>
/// The Canonical Media Model as seen by the expression engine. Bindings are defined once in <see cref="BindingCatalog"/>
/// and read only this model: provider-specific data never reaches them, so a new provider needs no new binding.
/// </summary>
public sealed class MediaBindingSource : IBindingSource
{
    public MediaBindingSource(MediaFile? file, Movie? movie, Series? series, Episode? episode, MatchPreferences preferences, DateOnly? today = null,
        IReadOnlyList<Episode>? moreEpisodes = null)
    {
        MoreEpisodes = moreEpisodes ?? Array.Empty<Episode>();
        File = file;
        Movie = movie;
        Series = series;
        Episode = episode;
        Preferences = preferences;
        Today = today ?? DateOnly.FromDateTime(DateTime.Now);
    }

    public static MediaBindingSource From(MatchResult match, MatchPreferences preferences) =>
        new(match.File, match.Movie, match.Series, match.Episode, preferences, null, match.MoreEpisodes);

    public MediaFile? File { get; }
    public Movie? Movie { get; }
    public Series? Series { get; }
    public Episode? Episode { get; }
    /// <summary>Further episodes of a multi-episode file (after <see cref="Episode"/>).</summary>
    public IReadOnlyList<Episode> MoreEpisodes { get; }
    public MatchPreferences Preferences { get; }
    public DateOnly Today { get; }

    public string Language => Preferences.OutputLanguage;

    /// <summary>The movie or series the file belongs to.</summary>
    public MediaEntity? Subject => (MediaEntity?)Movie ?? Series;

    public IReadOnlyCollection<string> Names => BindingCatalog.Names;

    public bool TryGet(string name, out object? value)
    {
        if (BindingCatalog.TryFind(name, out var definition))
        {
            value = definition.Resolve(this);
            return true;
        }

        value = null;
        return false;
    }

    public string? Resolve(TitleSet? titles, string language) => titles?.Resolve(language, Preferences.MissingTranslation);

    /// <summary>Numbering under the episode order chosen in Match Preferences, falling back to the provider's default numbering.</summary>
    public Numbering? ChosenNumbering
    {
        get
        {
            return Episode is null ? null : NumberingOf(Episode);
        }
    }

    public Numbering NumberingOf(Episode episode)
    {
        var key = OrderKey(Preferences.Order);
        if (key is not null)
            foreach (var (k, n) in episode.Orders)
                if (string.Equals(k, key, StringComparison.OrdinalIgnoreCase)) return n;
        return episode.Number;
    }

    public IReadOnlyList<Episode> AllEpisodes => Episode is null ? Array.Empty<Episode>() : new[] { Episode }.Concat(MoreEpisodes).ToList();

    /// <summary>Episode title in a language; for a multi-episode file the titles are merged (see <see cref="MultiEpisodeTitles"/>).</summary>
    public string? EpisodeTitleIn(string language) => MoreEpisodes.Count == 0
        ? Resolve(Episode?.Titles, language)
        : MultiEpisodeTitles.Merge(AllEpisodes.Select(e => Resolve(e.Titles, language)).ToList());

    private Numbering? LastNumbering => MoreEpisodes.Count == 0 ? null : NumberingOf(MoreEpisodes[^1]);

    /// <summary>"S01E01" or, for a multi-episode file, "S01E01-02" (the end carries the season only when it differs).</summary>
    public string? SeasonEpisodeText(Func<int, int, string> full, Func<int, string> last)
    {
        if (SeasonNumber is not { } s || EpisodeNumber is not { } e) return null;
        var text = full(s, e);
        if (LastNumbering is not { Episode: { } le } end || (end.Season ?? s) == s && le == e) return text;
        return text + "-" + ((end.Season ?? s) == s ? last(le) : full(end.Season!.Value, le));
    }

    /// <summary>Two-digit episode number, or "01-02" for a multi-episode file.</summary>
    public string? EpisodeRangeText() =>
        EpisodeNumber is null ? null : LastNumbering is { Episode: { } le } && le != EpisodeNumber ? $"{EpisodeNumber:00}-{le:00}" : $"{EpisodeNumber:00}";

    public int? SeasonNumber => ChosenNumbering?.Season ?? Episode?.Number.Season;
    public int? EpisodeNumber => ChosenNumbering?.Episode ?? Episode?.Number.Episode;

    public int? AbsoluteNumber
    {
        get
        {
            if (Episode is null) return null;
            if (ChosenNumbering?.Absolute is { } chosen) return chosen;
            foreach (var (k, n) in Episode.Orders)
                if (string.Equals(k, "absolute", StringComparison.OrdinalIgnoreCase) && (n.Absolute ?? n.Episode) is { } a) return a;
            return Episode.Number.Absolute;
        }
    }

    private static string? OrderKey(EpisodeOrder order) => order switch
    {
        EpisodeOrder.Dvd => "dvd",
        EpisodeOrder.Absolute => "absolute",
        EpisodeOrder.Digital => "digital",
        EpisodeOrder.StoryArc => "storyarc",
        EpisodeOrder.Production => "production",
        EpisodeOrder.Official => "official",
        _ => null,
    };

    public string? SeriesOrMovieName(string language) =>
        Resolve(Movie?.Titles, language) ?? Resolve(Series?.Titles, language);

    public int? Year => Movie?.Year ?? Series?.Year ?? Episode?.Year;

    public object? Date => (Episode?.AirDate ?? Episode?.ReleaseDate ?? Movie?.ReleaseDate ?? Series?.ReleaseDate) is { } d ? d : null;

    public string? Original(MediaEntity? entity) =>
        entity?.Titles.All.FirstOrDefault(t => t.Kind == TitleKind.Original)?.Value;

    public IReadOnlyList<string> Genres => (IReadOnlyList<string>?)Movie?.Genres ?? Series?.Genres ?? Array.Empty<string>();

    public string? Id(string source) => (Subject?.Ids.Get(source)) ?? Episode?.Ids.Get(source);

    public SubtitleFile? Subtitle => File as SubtitleFile;

    public string? FileLanguage => Subtitle?.Language ?? File?.Detected.Language;

    public IPropertyBag Info() => new DictionaryBag(new Dictionary<string, object?>
    {
        ["Title"] = SeriesOrMovieName(Language),
        ["OriginalTitle"] = Original(Subject),
        ["EpisodeTitle"] = EpisodeTitleIn(Language),
        ["Overview"] = (Episode ?? (MediaEntity?)Movie ?? Series)?.Overview,
        ["Year"] = Year,
        ["ReleaseDate"] = Date,
        ["OriginalLanguage"] = Subject?.OriginalLanguage,
        ["Genres"] = Genres,
        ["Directors"] = Movie?.Directors,
        ["Actors"] = (IReadOnlyList<string>?)Movie?.Actors ?? Series?.Actors,
        ["Countries"] = (IReadOnlyList<string>?)Movie?.Countries ?? Series?.Countries,
        ["Rating"] = Movie?.Rating ?? Series?.Rating,
        ["Votes"] = Movie?.Votes ?? Series?.Votes,
        ["Network"] = Series?.Network,
        ["Status"] = Series?.Status,
        ["Keywords"] = Subject?.Keywords,
        ["Aliases"] = Subject?.Titles.All.Select(t => t.Value).Distinct().ToList(),
        ["Certification"] = Movie?.Certification ?? Series?.Certification,
        ["Collection"] = Movie?.Collection,
        ["Runtime"] = Movie?.RuntimeMinutes ?? Episode?.RuntimeMinutes,
        ["Provider"] = (Subject?.Source ?? Episode?.Source)?.ProviderId,
        ["Ids"] = new DictionaryBag((Subject?.Ids ?? ExternalIds.Empty).All.ToDictionary(kv => kv.Key, kv => (object?)kv.Value)),
    });
}

/// <summary><c>localize.it.n</c>: the title in a specific language, regardless of the output language.</summary>
public sealed class LocalizeBag : IPropertyBag
{
    private readonly MediaBindingSource _source;

    public LocalizeBag(MediaBindingSource source) => _source = source;

    public IReadOnlyCollection<string> PropertyNames =>
        (_source.Subject?.Titles.All ?? Array.Empty<LocalizedText>())
        .Select(t => t.Language).OfType<string>().Distinct(StringComparer.OrdinalIgnoreCase).ToList();

    public bool TryGetProperty(string name, out object? value)
    {
        value = new LocalizedView(_source, name.Length == 3 && LanguageCodes.Find(name) is { } info ? info.Iso2 : name);
        return true;
    }
}

public sealed class LocalizedView : IPropertyBag
{
    private readonly MediaBindingSource _source;
    private readonly string _language;

    public LocalizedView(MediaBindingSource source, string language)
    {
        _source = source;
        _language = language;
    }

    public IReadOnlyCollection<string> PropertyNames { get; } = new[] { "n", "t" };

    public bool TryGetProperty(string name, out object? value)
    {
        switch (name)
        {
            case "n":
                value = _source.SeriesOrMovieName(_language);
                return true;
            case "t":
                value = _source.EpisodeTitleIn(_language);
                return true;
            default:
                value = null;
                return false;
        }
    }

    public override string ToString() => _source.SeriesOrMovieName(_language) ?? "";
}

public sealed record BindingDefinition(string Name, string Category, string Description, string Example, Func<MediaBindingSource, object?> Resolve);

/// <summary>Every binding, defined exactly once. The editor's help and autocompletion are generated from this list.</summary>
public static class BindingCatalog
{
    private static string Two(int? n) => n is { } v ? v.ToString("00", CultureInfo.InvariantCulture) : "";

    private static BindingDefinition D(string name, string category, string description, string example, Func<MediaBindingSource, object?> resolve) =>
        new(name, category, description, example, resolve);

    private static readonly IReadOnlyList<BindingDefinition> Core = new[]
    {
        D("n", "Name", "Movie or series name, in the output language", "Breaking Bad", m => m.SeriesOrMovieName(m.Language)),
        D("ny", "Name", "Name followed by the year in parentheses", "Inception (2010)",
            m => m.SeriesOrMovieName(m.Language) is { } n ? (m.Year is { } y ? $"{n} ({y})" : n) : null),
        D("originalTitle", "Name", "Title in the original language", "Shingeki no Kyojin", m => m.Original(m.Subject)),
        D("t", "Name", "Episode title, in the output language", "Pilot", m => m.EpisodeTitleIn(m.Language)),
        D("y", "Name", "Year of the movie or first year of the series", "2010", m => m.Year),
        D("d", "Name", "Air date (episodes) or release date (movies)", "2008-01-20", m => m.Date),
        D("airdate", "Name", "Same as d", "2008-01-20", m => m.Date),
        D("localize", "Name", "Titles in a specific language: localize.it.n, localize['ja-Latn'].t", "Il Signore degli Anelli", m => new LocalizeBag(m)),

        D("s", "Episode", "Season number", "1", m => m.SeasonNumber),
        D("e", "Episode", "Episode number", "2", m => m.EpisodeNumber),
        D("s00", "Episode", "Season number, two digits", "01", m => m.SeasonNumber is null ? null : Two(m.SeasonNumber)),
        D("e00", "Episode", "Episode number, two digits", "02", m => m.EpisodeRangeText()),
        D("s00e00", "Episode", "Season and episode", "S01E02",
            m => m.SeasonEpisodeText((s, e) => $"S{s:00}E{e:00}", n => $"{n:00}")),
        D("sxe", "Episode", "Season x episode", "1x02",
            m => m.SeasonEpisodeText((s, e) => $"{s}x{e:00}", n => $"{n:00}")),
        D("absolute", "Episode", "Absolute episode number", "25", m => m.AbsoluteNumber),
        D("special", "Episode", "Special number, only for specials", "1", m => m.Episode?.Number.Special),
        D("sn", "Episode", "Season name", "Season 1", m => m.Series?.Seasons.FirstOrDefault(s => s.Number == m.SeasonNumber)?.Name),
        D("sc", "Episode", "Number of seasons of the series", "5", m => m.Series is { SeasonCount: > 0 } s ? s.SeasonCount : null),

        D("genre", "Metadata", "First genre", "Drama", m => m.Genres.FirstOrDefault()),
        D("genres", "Metadata", "All genres (a list)", "Action, Drama", m => m.Genres),
        D("director", "Metadata", "First director", "Christopher Nolan", m => m.Movie?.Directors.FirstOrDefault()),
        D("directors", "Metadata", "All directors (a list)", "Lana Wachowski, Lilly Wachowski", m => m.Movie?.Directors),
        D("actors", "Metadata", "Actors (a list)", "Leonardo DiCaprio, ...", m => (IReadOnlyList<string>?)m.Movie?.Actors ?? m.Series?.Actors),
        D("rating", "Metadata", "Rating", "8.4", m => m.Movie?.Rating ?? m.Series?.Rating),
        D("votes", "Metadata", "Number of votes", "12000", m => m.Movie?.Votes ?? m.Series?.Votes),
        D("certification", "Metadata", "Certification", "PG-13", m => m.Movie?.Certification ?? m.Series?.Certification),
        D("collection", "Metadata", "Collection or saga", "The Dark Knight Collection", m => m.Movie?.Collection),
        D("runtime", "Metadata", "Runtime in minutes", "148", m => m.Movie?.RuntimeMinutes ?? m.Episode?.RuntimeMinutes),
        D("overview", "Metadata", "Plot summary", "...", m => (m.Episode ?? (MediaEntity?)m.Movie ?? m.Series)?.Overview),
        D("countries", "Metadata", "Production countries (a list)", "US, GB", m => (IReadOnlyList<string>?)m.Movie?.Countries ?? m.Series?.Countries),
        D("info", "Metadata", "All metadata as an object: info.Overview, info.Genres, info.Ids.imdb ...", "info.Rating", m => m.Info()),

        D("id", "Identifiers", "Id of the movie or series in the selected provider", "1396", m => m.Subject?.Source.Id),
        D("provider", "Identifiers", "Provider the data comes from", "tmdb", m => (m.Subject?.Source ?? m.Episode?.Source)?.ProviderId),
        D("tmdbid", "Identifiers", "TMDB id", "1396", m => m.Id("tmdb")),
        D("imdbid", "Identifiers", "IMDb id", "tt0903747", m => m.Id("imdb")),
        D("tvdbid", "Identifiers", "TVDB id", "81189", m => m.Id("tvdb")),
        D("anidbid", "Identifiers", "AniDB id", "4563", m => m.Id("anidb")),

        D("original", "File", "Original file name, without extension", "Show.S01E02.720p", m => m.File?.OriginalName),
        D("fn", "File", "File name without extension", "Show.S01E02.720p", m => m.File?.OriginalName),
        D("ext", "File", "Extension, without the dot", "mkv", m => m.File?.Extension),
        D("file", "File", "Full path of the file", "C:\\Media\\a.mkv", m => m.File?.Path),
        D("folder", "File", "Folder containing the file (prints its name); also folder.name, folder.dir", "Downloads",
            m => m.File is null ? null : new ValueBag(Path.GetFileName(Path.GetDirectoryName(m.File.Path)) ?? "", new Dictionary<string, object?>
            {
                ["name"] = Path.GetFileName(Path.GetDirectoryName(m.File.Path)),
                ["dir"] = Path.GetDirectoryName(m.File.Path),
            })),
        D("size", "File", "Size in bytes", "734003200", m => m.File?.Size),
        D("vf", "File", "Video format", "1080p", m => m.File?.Technical?.VideoFormat),
        D("vc", "File", "Video codec", "x265", m => m.File?.Technical?.VideoCodec),
        D("ac", "File", "Audio codec", "AAC", m => m.File?.Technical?.AudioCodec),
        D("width", "File", "Video width", "1920", m => m.File?.Technical?.Width),
        D("height", "File", "Video height", "1080", m => m.File?.Technical?.Height),
        D("source", "File", "Source of the video", "BluRay", m => m.File?.Technical?.Source),
        D("group", "File", "Release group", "SPARKS", m => m.File?.Detected.ReleaseGroup),
        D("lang", "File", "Language of the file (subtitles) or detected language: prints the code; also lang.ISO2, .ISO3, .name", "it",
            m => ExtendedBindings.Language(m.FileLanguage)),
        D("forced", "File", "True for forced subtitles", "true", m => m.Subtitle?.Forced),
        D("sdh", "File", "True for SDH subtitles", "false", m => m.Subtitle?.Sdh),

        D("now", "Other", "Current date", "2026-10-30", m => m.Today),
        D("today", "Other", "Same as now", "2026-10-30", m => m.Today),
    };

    public static readonly IReadOnlyList<BindingDefinition> All = Core.Concat(ExtendedBindings.All).ToList();

    private static readonly IReadOnlyDictionary<string, BindingDefinition> ByName = All.ToDictionary(d => d.Name, StringComparer.Ordinal);

    public static IReadOnlyCollection<string> Names { get; } = All.Select(d => d.Name).ToList();

    public static bool TryFind(string name, out BindingDefinition definition) => ByName.TryGetValue(name, out definition!);
}
