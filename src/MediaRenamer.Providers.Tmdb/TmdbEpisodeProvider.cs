using MediaRenamer.Model;
using MediaRenamer.Plugin.Abstractions;

namespace MediaRenamer.Providers.Tmdb;

/// <summary>TMDB – Episode Mode. Independent from <see cref="TmdbMovieProvider"/>; only the HTTP client is shared.</summary>
public sealed class TmdbEpisodeProvider : IEpisodeProvider
{
    private readonly TmdbClient _client;

    internal TmdbEpisodeProvider(TmdbClient client) => _client = client;

    public string ProviderId => "tmdb";
    public string DisplayName => "TMDB";
    public ProviderMode Mode => ProviderMode.Episode;

    public ProviderCapabilities Capabilities =>
        ProviderCapabilities.SearchSeries | ProviderCapabilities.SeriesDetails | ProviderCapabilities.Seasons |
        ProviderCapabilities.Episodes | ProviderCapabilities.EpisodeDetails |
        ProviderCapabilities.LocalizedTitles | ProviderCapabilities.AlternativeTitles;

    public IReadOnlyList<ConfigField> ConfigSchema => TmdbConfig.Schema;

    public Task<ConnectionTestResult> TestConnectionAsync(IProviderConfig config, CancellationToken ct) =>
        _client.TestAsync(config, ct);

    public async Task<IReadOnlyList<SearchResult>> SearchSeriesAsync(SearchQuery query, IProviderConfig config, CancellationToken ct)
    {
        var language = query.Language ?? TmdbConfig.DefaultLanguage;
        var json = await _client.GetAsync("search/tv", new Dictionary<string, string?>
        {
            ["query"] = query.Text,
            ["first_air_date_year"] = query.Year?.ToString(),
            ["language"] = language,
            ["include_adult"] = "false",
        }, config, ct);

        return json.Arr("results").Select(r =>
        {
            var d = TmdbMapper.MapSearchItem(r, movie: false, ProviderId);
            return new SearchResult(new ProviderRef(ProviderId, Mode, d.Id), d.Titles, d.Year, "Series", d.Poster);
        }).ToList();
    }

    public async Task<Series> GetSeriesAsync(ProviderRef id, string language, IProviderConfig config, CancellationToken ct)
    {
        var json = await _client.GetAsync($"tv/{Uri.EscapeDataString(id.Id)}", new Dictionary<string, string?>
        {
            ["language"] = language,
            ["append_to_response"] = "translations,alternative_titles,external_ids,content_ratings,credits,keywords",
        }, config, ct);
        return TmdbMapper.MapSeries(json, id, language);
    }

    /// <summary>
    /// One request per season and language, merged by (season, episode) so every title variant is kept.
    /// The first language is the primary one; order follows TMDB.
    /// </summary>
    public async Task<IReadOnlyList<Episode>> GetEpisodesAsync(ProviderRef id, IReadOnlyCollection<string> languages,
        IProviderConfig config, CancellationToken ct)
    {
        var langs = languages.Count > 0 ? languages.Distinct(StringComparer.OrdinalIgnoreCase).ToList()
                                       : new List<string> { TmdbConfig.DefaultLanguage };

        var series = await _client.GetAsync($"tv/{Uri.EscapeDataString(id.Id)}",
            new Dictionary<string, string?> { ["language"] = langs[0] }, config, ct);
        var seasonNumbers = series.Arr("seasons").Select(s => s.Int("season_number")).OfType<int>().ToList();

        var result = new List<Episode>();
        foreach (var season in seasonNumbers)
        {
            var merged = new List<(System.Text.Json.JsonElement First, List<LocalizedText> Titles, string? Overview)>();
            var index = new Dictionary<(int?, int?), int>();

            foreach (var lang in langs)
            {
                var json = await _client.GetAsync($"tv/{Uri.EscapeDataString(id.Id)}/season/{season}",
                    new Dictionary<string, string?> { ["language"] = lang }, config, ct);

                foreach (var ep in json.Arr("episodes"))
                {
                    var key = (ep.Int("season_number"), ep.Int("episode_number"));
                    if (!index.TryGetValue(key, out var i))
                    {
                        index[key] = i = merged.Count;
                        merged.Add((ep, new List<LocalizedText>(), null));
                    }

                    var entry = merged[i];
                    if (ep.Str("name") is { } name)
                        entry.Titles.Add(new LocalizedText(name, lang,
                            entry.Titles.Count == 0 ? TitleKind.Primary
                            : LanguageTag.Primary(lang) == "en" ? TitleKind.English : TitleKind.Localized));
                    merged[i] = entry with { Overview = entry.Overview ?? ep.Str("overview") };
                }
            }

            result.AddRange(merged.Select(m =>
                TmdbMapper.MapEpisode(m.First, id, new TitleSet(m.Titles.DistinctBy(t => (t.Value, t.Language))), m.Overview)));
        }
        return result;
    }
}
