using MediaRenamer.Model;
using MediaRenamer.Plugin.Abstractions;

namespace MediaRenamer.Providers.Tmdb;

/// <summary>TMDB – Movie Mode. Independent from <see cref="TmdbEpisodeProvider"/>; only the HTTP client is shared.</summary>
public sealed class TmdbMovieProvider : IMovieProvider
{
    private readonly TmdbClient _client;

    internal TmdbMovieProvider(TmdbClient client) => _client = client;

    public string ProviderId => "tmdb";
    public string DisplayName => "TMDB";
    public ProviderMode Mode => ProviderMode.Movie;

    public ProviderCapabilities Capabilities =>
        ProviderCapabilities.SearchMovie | ProviderCapabilities.MovieDetails |
        ProviderCapabilities.LocalizedTitles | ProviderCapabilities.AlternativeTitles;

    public IReadOnlyList<ConfigField> ConfigSchema => TmdbConfig.Schema;

    public Task<ConnectionTestResult> TestConnectionAsync(IProviderConfig config, CancellationToken ct) =>
        _client.TestAsync(config, ct);

    public async Task<IReadOnlyList<SearchResult>> SearchMovieAsync(SearchQuery query, IProviderConfig config, CancellationToken ct)
    {
        var language = query.Language ?? TmdbConfig.DefaultLanguage;
        var json = await _client.GetAsync("search/movie", new Dictionary<string, string?>
        {
            ["query"] = query.Text,
            ["year"] = query.Year?.ToString(),
            ["language"] = language,
            ["include_adult"] = "false",
        }, config, ct);

        return json.Arr("results").Select(r =>
        {
            var d = TmdbMapper.MapSearchItem(r, movie: true, ProviderId);
            return new SearchResult(new ProviderRef(ProviderId, Mode, d.Id), d.Titles, d.Year, "Movie", d.Poster);
        }).ToList();
    }

    public async Task<Movie> GetMovieAsync(ProviderRef id, string language, IProviderConfig config, CancellationToken ct)
    {
        var json = await _client.GetAsync($"movie/{Uri.EscapeDataString(id.Id)}", new Dictionary<string, string?>
        {
            ["language"] = language,
            ["append_to_response"] = "translations,alternative_titles,credits,release_dates,keywords",
        }, config, ct);
        return TmdbMapper.MapMovie(json, id, language);
    }
}
