using MediaRenamer.Model;

namespace MediaRenamer.Plugin.Abstractions;

/// <summary>Operations a provider mode may implement. The UI enables features from these flags only.</summary>
[Flags]
public enum ProviderCapabilities
{
    None = 0,
    SearchMovie = 1 << 0,
    MovieDetails = 1 << 1,
    SearchSeries = 1 << 2,
    SeriesDetails = 1 << 3,
    Seasons = 1 << 4,
    Episodes = 1 << 5,
    EpisodeDetails = 1 << 6,
    LocalizedTitles = 1 << 7,
    AlternativeTitles = 1 << 8,
    AbsoluteNumbering = 1 << 9,
    AlternativeOrders = 1 << 10,
}

/// <summary>Current plugin contract version. Plugins declare the version they were built against.</summary>
public static class PluginApi
{
    public const int Version = 1;
}

public sealed record SearchQuery(string Text, int? Year = null, string? Language = null);

/// <summary>A lightweight search hit, converted to the canonical model by the provider.</summary>
public sealed record SearchResult(
    ProviderRef Ref,
    TitleSet Titles,
    int? Year,
    string Kind,
    Uri? PosterUri = null);

public enum ConfigFieldType { Text, Secret, Language, Number, Toggle }

public sealed record ConfigField(string Key, string Label, ConfigFieldType Type, bool Required = false, string? DefaultValue = null);

public sealed record ConnectionTestResult(bool Success, string Message);

/// <summary>Resolved configuration passed to a provider (secrets already decrypted).</summary>
public interface IProviderConfig
{
    string? Get(string key);
}

/// <summary>
/// One (provider, mode) pair. TMDB registers two distinct instances: Movie and Episode.
/// Providers convert all results to the canonical model; they never know about bindings.
/// </summary>
public interface IMediaProvider
{
    string ProviderId { get; }
    string DisplayName { get; }
    ProviderMode Mode { get; }
    ProviderCapabilities Capabilities { get; }
    IReadOnlyList<ConfigField> ConfigSchema { get; }
    Task<ConnectionTestResult> TestConnectionAsync(IProviderConfig config, CancellationToken ct);
}

public interface IMovieProvider : IMediaProvider
{
    Task<IReadOnlyList<SearchResult>> SearchMovieAsync(SearchQuery query, IProviderConfig config, CancellationToken ct);
    Task<Movie> GetMovieAsync(ProviderRef id, string language, IProviderConfig config, CancellationToken ct);
}

public interface IEpisodeProvider : IMediaProvider
{
    Task<IReadOnlyList<SearchResult>> SearchSeriesAsync(SearchQuery query, IProviderConfig config, CancellationToken ct);
    Task<Series> GetSeriesAsync(ProviderRef id, string language, IProviderConfig config, CancellationToken ct);
    /// <summary>Episodes with titles in the requested languages, in provider order.</summary>
    Task<IReadOnlyList<Episode>> GetEpisodesAsync(ProviderRef id, IReadOnlyCollection<string> languages, IProviderConfig config, CancellationToken ct);
}

/// <summary>Entry point of a plugin assembly. Registration is the only coupling with the host.</summary>
public interface IProviderPlugin
{
    string Id { get; }
    string Name { get; }
    Version Version { get; }
    int ApiVersion { get; }
    IEnumerable<IMediaProvider> CreateProviders(IPluginContext context);
}
