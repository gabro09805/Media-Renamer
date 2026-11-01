using MediaRenamer.Plugin.Abstractions;
using System.Globalization;
using System.Text.Json;
using MediaRenamer.Model;

namespace MediaRenamer.Providers.Tmdb;

internal static class Json
{
    public static JsonElement? Get(this JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind != JsonValueKind.Null ? v : null;

    public static string? Str(this JsonElement e, string name)
    {
        var v = e.Get(name);
        return v is { ValueKind: JsonValueKind.String } s && !string.IsNullOrWhiteSpace(s.GetString()) ? s.GetString() : null;
    }

    public static int? Int(this JsonElement e, string name) =>
        e.Get(name) is { ValueKind: JsonValueKind.Number } n && n.TryGetInt32(out var i) ? i : null;

    public static double? Dbl(this JsonElement e, string name) =>
        e.Get(name) is { ValueKind: JsonValueKind.Number } n ? n.GetDouble() : null;

    public static IEnumerable<JsonElement> Arr(this JsonElement e, string name) =>
        e.Get(name) is { ValueKind: JsonValueKind.Array } a ? a.EnumerateArray() : Enumerable.Empty<JsonElement>();

    public static DateOnly? Date(this JsonElement e, string name) =>
        DateOnly.TryParseExact(e.Str(name), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d : null;
}

/// <summary>Converts TMDB JSON into the Canonical Media Model. The only place that knows TMDB's schema.</summary>
internal static class TmdbMapper
{
    private const string ImageBase = "https://image.tmdb.org/t/p/w185";

    public static Uri? Poster(JsonElement e) =>
        e.Str("poster_path") is { } p ? new Uri(ImageBase + p) : null;

    public static SearchResultData MapSearchItem(JsonElement e, bool movie, string providerId, string? language = null, string? englishTitle = null)
    {
        var titleField = movie ? "title" : "name";
        var origField = movie ? "original_title" : "original_name";
        var titles = new List<LocalizedText>();
        if (e.Str(titleField) is { } t) titles.Add(new(t, null, TitleKind.Primary));
        if (e.Str(origField) is { } o) titles.Add(new(o, e.Str("original_language"), TitleKind.Original));
        var date = e.Date(movie ? "release_date" : "first_air_date");
        return new(e.Int("id")?.ToString(CultureInfo.InvariantCulture) ?? "", new TitleSet(titles), date?.Year, Poster(e));
    }

    public static async Task<Dictionary<string, string>> EnglishTitlesAsync(TmdbClient client, string path, string query, string? year, string yearKey,
        string language, IProviderConfig config, CancellationToken ct)
    {
        var map = new Dictionary<string, string>();
        if (LanguageTag.Primary(language) == "en") return map;
        var json = await client.GetAsync(path, new Dictionary<string, string?>
        {
            ["query"] = query, [yearKey] = year, ["language"] = "en-US", ["include_adult"] = "false",
        }, config, ct);
        foreach (var r in json.Arr("results"))
            if (r.Int("id")?.ToString(CultureInfo.InvariantCulture) is { } id && (r.Str("title") ?? r.Str("name")) is { } n) map[id] = n;
        return map;
    }

    public static Movie MapMovie(JsonElement m, ProviderRef source, string language)
    {
        var translations = Translations(m, "title");
        var titles = BuildTitles(m.Str("title"), m.Str("original_title"), m.Str("original_language"), language, translations,
            AliasTitles(m.Get("alternative_titles"), "titles"));

        var crew = m.Get("credits")?.Arr("crew") ?? Enumerable.Empty<JsonElement>();
        var cast = m.Get("credits")?.Arr("cast") ?? Enumerable.Empty<JsonElement>();

        return new Movie
        {
            Source = source,
            Titles = titles,
            OriginalLanguage = m.Str("original_language"),
            Overview = m.Str("overview"),
            PosterUri = Poster(m),
            ReleaseDate = m.Date("release_date"),
            Ids = Ids(m, "tmdb", m.Str("imdb_id")),
            Directors = crew.Where(c => c.Str("job") == "Director").Select(c => c.Str("name")!).Where(n => n is not null).Distinct().ToList(),
            Actors = cast.Take(10).Select(c => c.Str("name")!).Where(n => n is not null).ToList(),
            Genres = Genres(m),
            Collection = m.Get("belongs_to_collection")?.Str("name"),
            Certification = MovieCertification(m, language),
            Rating = m.Dbl("vote_average"),
            Votes = m.Int("vote_count"),
            RuntimeMinutes = m.Int("runtime"),
            Keywords = (m.Get("keywords")?.Arr("keywords") ?? Enumerable.Empty<JsonElement>())
                .Select(k => k.Str("name")!).Where(n => n is not null).ToList(),
            Countries = m.Arr("production_countries").Select(c => c.Str("iso_3166_1")!).Where(c => c is not null).ToList(),
        };
    }

    public static Series MapSeries(JsonElement s, ProviderRef source, string language)
    {
        var translations = Translations(s, "name");
        var titles = BuildTitles(s.Str("name"), s.Str("original_name"), s.Str("original_language"), language, translations,
            AliasTitles(s.Get("alternative_titles"), "results"));

        var genres = Genres(s);
        var countries = s.Arr("origin_country").Select(c => c.GetString()!).Where(c => c is not null).ToList();
        var anime = genres.Contains("Animation", StringComparer.OrdinalIgnoreCase) && countries.Contains("JP");

        var ids = Ids(s, "tmdb", s.Get("external_ids")?.Str("imdb_id"));
        if (s.Get("external_ids")?.Int("tvdb_id") is { } tvdb) ids = ids.With("tvdb", tvdb.ToString(CultureInfo.InvariantCulture));

        return new Series
        {
            Source = source,
            Titles = titles,
            OriginalLanguage = s.Str("original_language"),
            Overview = s.Str("overview"),
            PosterUri = Poster(s),
            ReleaseDate = s.Date("first_air_date"),
            Ids = ids,
            Type = anime ? SeriesType.Anime : SeriesType.Tv,
            Genres = genres,
            Countries = countries,
            Certification = TvCertification(s, language),
            Actors = (s.Get("credits")?.Arr("cast") ?? Enumerable.Empty<JsonElement>()).Take(10)
                .Select(c => c.Str("name")!).Where(n => n is not null).ToList(),
            Network = s.Arr("networks").Select(n => n.Str("name")).FirstOrDefault(n => n is not null),
            Status = s.Str("status"),
            Rating = s.Dbl("vote_average"),
            Votes = s.Int("vote_count"),
            Keywords = (s.Get("keywords")?.Arr("results") ?? Enumerable.Empty<JsonElement>())
                .Select(k => k.Str("name")!).Where(n => n is not null).ToList(),
            Seasons = s.Arr("seasons").Select(se => new Season
            {
                Number = se.Int("season_number") ?? 0,
                Name = se.Str("name"),
                Year = se.Date("air_date")?.Year,
            }).ToList(),
        };
    }

    public static Episode MapEpisode(JsonElement e, ProviderRef seriesRef, TitleSet titles, string? overview) => new()
    {
        Source = new ProviderRef(seriesRef.ProviderId, ProviderMode.Episode, e.Int("id")?.ToString(CultureInfo.InvariantCulture) ?? ""),
        SeriesRef = seriesRef,
        Titles = titles,
        Overview = overview,
        AirDate = e.Date("air_date"),
        ReleaseDate = e.Date("air_date"),
        RuntimeMinutes = e.Int("runtime"),
        Number = new Numbering(e.Int("season_number"), e.Int("episode_number")),
        PosterUri = e.Str("still_path") is { } p ? new Uri(ImageBase + p) : null,
    };

    /// <summary>
    /// TMDB silently falls back to another language when a translation is missing, so the main title is only
    /// tagged with the requested language when the translations list confirms it exists.
    /// </summary>
    private static TitleSet BuildTitles(string? main, string? original, string? originalLang, string language,
        List<(string Tag, string Value)> translations, IEnumerable<LocalizedText> aliases)
    {
        var items = new List<LocalizedText>();
        var primaryLang = LanguageTag.Primary(language);
        var confirmed = translations.Any(t => LanguageTag.Primary(t.Tag) == primaryLang);

        if (main is not null) items.Add(new(main, confirmed ? language : null, TitleKind.Primary));
        if (original is not null) items.Add(new(original, originalLang, TitleKind.Original));
        foreach (var (tag, value) in translations)
            items.Add(new(value, tag, LanguageTag.Primary(tag) == "en" ? TitleKind.English : TitleKind.Localized));
        items.AddRange(aliases);

        return new TitleSet(items.DistinctBy(i => (i.Value, i.Language, i.Kind)));
    }

    private static List<(string Tag, string Value)> Translations(JsonElement e, string dataField)
    {
        var list = new List<(string, string)>();
        foreach (var t in e.Get("translations")?.Arr("translations") ?? Enumerable.Empty<JsonElement>())
        {
            var value = t.Get("data")?.Str(dataField);
            var lang = t.Str("iso_639_1");
            if (value is null || lang is null) continue;
            var country = t.Str("iso_3166_1");
            list.Add((country is null ? lang : $"{lang}-{country}", value));
        }
        return list;
    }

    private static IEnumerable<LocalizedText> AliasTitles(JsonElement? alt, string arrayName) =>
        alt is { } a
            ? a.Arr(arrayName).Where(x => x.Str("title") is not null).Select(x => new LocalizedText(x.Str("title")!, null, TitleKind.Alias))
            : Enumerable.Empty<LocalizedText>();

    private static ExternalIds Ids(JsonElement e, string source, string? imdb)
    {
        var ids = ExternalIds.Empty;
        if (e.Int("id") is { } id) ids = ids.With(source, id.ToString(CultureInfo.InvariantCulture));
        if (imdb is not null) ids = ids.With("imdb", imdb);
        return ids;
    }

    private static List<string> Genres(JsonElement e) =>
        e.Arr("genres").Select(g => g.Str("name")!).Where(n => n is not null).ToList();

    private static string? MovieCertification(JsonElement m, string language)
    {
        var results = m.Get("release_dates")?.Arr("results").ToList() ?? new();
        foreach (var region in RegionPreference(language))
        {
            var cert = results.FirstOrDefault(r => r.Str("iso_3166_1") == region)
                .Arr("release_dates").Select(r => r.Str("certification")).FirstOrDefault(c => c is not null);
            if (cert is not null) return cert;
        }
        return null;
    }

    private static string? TvCertification(JsonElement s, string language)
    {
        var results = s.Get("content_ratings")?.Arr("results").ToList() ?? new();
        foreach (var region in RegionPreference(language))
            if (results.FirstOrDefault(r => r.Str("iso_3166_1") == region).Str("rating") is { } rating) return rating;
        return null;
    }

    private static IEnumerable<string> RegionPreference(string language)
    {
        var i = language.IndexOfAny(new[] { '-', '_' });
        if (i > 0) yield return language[(i + 1)..].ToUpperInvariant();
        yield return "US";
    }
}

internal sealed record SearchResultData(string Id, TitleSet Titles, int? Year, Uri? Poster);
