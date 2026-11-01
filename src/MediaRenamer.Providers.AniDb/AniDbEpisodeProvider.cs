using System.Globalization;
using MediaRenamer.Model;
using MediaRenamer.Plugin.Abstractions;

namespace MediaRenamer.Providers.AniDb;

/// <summary>AniDB – Episode Mode. One AniDB entry (aid) is exposed as one series.</summary>
public sealed class AniDbEpisodeProvider : IEpisodeProvider
{
    private const int MaxSearchResults = 25;

    private readonly AniDbClient _client;
    private readonly AniDbTitleIndex _index;

    internal AniDbEpisodeProvider(AniDbClient client, AniDbTitleIndex index)
    {
        _client = client;
        _index = index;
    }

    public string ProviderId => "anidb";
    public string DisplayName => "AniDB";
    public ProviderMode Mode => ProviderMode.Episode;

    public ProviderCapabilities Capabilities =>
        ProviderCapabilities.SearchSeries | ProviderCapabilities.SeriesDetails | ProviderCapabilities.Seasons |
        ProviderCapabilities.Episodes | ProviderCapabilities.EpisodeDetails |
        ProviderCapabilities.LocalizedTitles | ProviderCapabilities.AlternativeTitles | ProviderCapabilities.AbsoluteNumbering;

    public IReadOnlyList<ConfigField> ConfigSchema => AniDbConfig.Schema;

    public async Task<ConnectionTestResult> TestConnectionAsync(IProviderConfig config, CancellationToken ct)
    {
        try
        {
            await _client.GetAnimeAsync(1, config, ct);
            return new ConnectionTestResult(true, "Connected to AniDB.");
        }
        catch (ProviderException ex)
        {
            return new ConnectionTestResult(false, ex.Message);
        }
    }

    public async Task<IReadOnlyList<SearchResult>> SearchSeriesAsync(SearchQuery query, IProviderConfig config, CancellationToken ct)
    {
        var entries = await _index.GetAsync(ct);
        return AniDbTitleIndex.Search(entries, query.Text, MaxSearchResults)
            .Select(x => new SearchResult(
                new ProviderRef(ProviderId, Mode, x.Entry.Aid.ToString(CultureInfo.InvariantCulture)),
                AniDbMapper.MapTitles(x.Entry.Titles),
                Year: null,
                Kind: "Anime"))
            .ToList();
    }

    public async Task<Series> GetSeriesAsync(ProviderRef id, string language, IProviderConfig config, CancellationToken ct)
    {
        var anime = await _client.GetAnimeAsync(ParseAid(id), config, ct);
        return AniDbMapper.MapSeries(anime, id);
    }

    /// <summary>AniDB returns all episode title languages in one response, so <paramref name="languages"/> is not needed.</summary>
    public async Task<IReadOnlyList<Episode>> GetEpisodesAsync(ProviderRef id, IReadOnlyCollection<string> languages,
        IProviderConfig config, CancellationToken ct)
    {
        var anime = await _client.GetAnimeAsync(ParseAid(id), config, ct);
        return AniDbMapper.Episodes(anime).ToList();
    }

    private static int ParseAid(ProviderRef id) =>
        int.TryParse(id.Id, NumberStyles.None, CultureInfo.InvariantCulture, out var aid)
            ? aid
            : throw new ProviderException(ProviderErrorKind.InvalidResponse, $"Invalid AniDB id '{id.Id}'.");
}
