using System.Globalization;
using System.Xml.Linq;
using MediaRenamer.Model;

namespace MediaRenamer.Providers.AniDb;

/// <summary>Converts AniDB XML into the Canonical Media Model.</summary>
internal static class AniDbMapper
{
    private const string ImageBase = "https://cdn.anidb.net/images/main/";

    public static TitleSet MapTitles(IEnumerable<AniDbTitle> titles) => new(titles.Select(t => MapTitle(t.Type, t.Language, t.Text)));

    private static LocalizedText MapTitle(string type, string lang, string text)
    {
        var language = lang.Length == 0 ? null : lang;
        return type switch
        {
            "main" => new LocalizedText(text, language, TitleKind.Original),
            "official" when lang == "en" => new LocalizedText(text, language, TitleKind.English),
            "official" => new LocalizedText(text, language, TitleKind.Localized),
            _ => new LocalizedText(text, language, TitleKind.Alias),
        };
    }

    public static Series MapSeries(XElement anime, ProviderRef source)
    {
        var titles = anime.Element("titles")?.Elements("title").Select(t =>
            MapTitle((string?)t.Attribute("type") ?? "", (string?)t.Attribute(AniDbClient.Lang) ?? "", t.Value)) ?? Enumerable.Empty<LocalizedText>();

        var start = ParseDate((string?)anime.Element("startdate"));
        var episodes = Episodes(anime).ToList();
        var seasons = new List<Season>();
        if (episodes.Any(e => e.Number.Season == 0)) seasons.Add(new Season { Number = 0, Name = "Specials" });
        seasons.Add(new Season { Number = 1 });

        var picture = (string?)anime.Element("picture");
        return new Series
        {
            Source = source,
            Titles = new TitleSet(titles),
            Overview = (string?)anime.Element("description"),
            ReleaseDate = start.Date,
            YearOverride = start.Year,
            PosterUri = string.IsNullOrEmpty(picture) ? null : new Uri(ImageBase + picture),
            Ids = ExternalIds.Empty.With("anidb", source.Id),
            Type = MapType((string?)anime.Element("type")),
            Seasons = seasons,
        };
    }

    /// <summary>
    /// Regular episodes (type 1) become season 1 with the episode number also stored as absolute number;
    /// specials (type 2) become season 0. Credits, trailers, parodies and others are not mapped yet.
    /// Document order is preserved.
    /// </summary>
    public static IEnumerable<Episode> Episodes(XElement anime)
    {
        var seriesRef = new ProviderRef("anidb", ProviderMode.Episode, (string?)anime.Attribute("id") ?? "");
        foreach (var e in anime.Element("episodes")?.Elements("episode") ?? Enumerable.Empty<XElement>())
        {
            var epno = e.Element("epno");
            var type = (string?)epno?.Attribute("type") ?? "1";
            var number = ParseNumber((string?)epno);
            if (number is null || type is not ("1" or "2")) continue;

            var titles = e.Elements("title").Select(t =>
            {
                var lang = (string?)t.Attribute(AniDbClient.Lang) ?? "";
                return new LocalizedText(t.Value, lang.Length == 0 ? null : lang,
                    lang == "en" ? TitleKind.English : TitleKind.Localized);
            }).Where(t => t.Value.Length > 0);

            var date = ParseDate((string?)e.Element("airdate")).Date;
            _ = int.TryParse((string?)e.Element("length"), out var length);

            yield return new Episode
            {
                Source = new ProviderRef("anidb", ProviderMode.Episode, (string?)e.Attribute("id") ?? ""),
                SeriesRef = seriesRef,
                Titles = new TitleSet(titles),
                Overview = (string?)e.Element("summary"),
                AirDate = date,
                ReleaseDate = date,
                RuntimeMinutes = length > 0 ? length : null,
                Number = type == "1"
                    ? new Numbering(1, number, number)
                    : new Numbering(0, number, null, number),
            };
        }
    }

    private static SeriesType MapType(string? type) => type?.ToLowerInvariant() switch
    {
        "tv series" or "web" => SeriesType.Anime,
        "ova" => SeriesType.Ova,
        "tv special" or "other" => SeriesType.Special,
        "movie" => SeriesType.Movie,
        _ => SeriesType.Anime,
    };

    private static int? ParseNumber(string? epno)
    {
        if (epno is null) return null;
        var digits = new string(epno.Where(char.IsDigit).ToArray());
        return int.TryParse(digits, out var n) ? n : null;
    }

    /// <summary>AniDB dates may be partial ("2020" or "2020-04").</summary>
    private static (DateOnly? Date, int? Year) ParseDate(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return (null, null);
        if (DateOnly.TryParseExact(s, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)) return (d, d.Year);
        return int.TryParse(s.AsSpan(0, Math.Min(4, s.Length)), out var y) && s.Length >= 4 ? (null, y) : (null, null);
    }
}
