namespace MediaRenamer.Model;

/// <summary>Provider-neutral mode. The same provider can expose several modes (e.g. TMDB Movie / Episode).</summary>
public enum ProviderMode { Movie, Episode }

/// <summary>Identifies the provider, mode and native id an entity came from.</summary>
public sealed record ProviderRef(string ProviderId, ProviderMode Mode, string Id);

/// <summary>External identifiers keyed by source name (tmdb, anidb, tvdb, imdb, ...).</summary>
public sealed class ExternalIds
{
    public static readonly ExternalIds Empty = new(new Dictionary<string, string>());

    private readonly IReadOnlyDictionary<string, string> _ids;

    public ExternalIds(IReadOnlyDictionary<string, string> ids) =>
        _ids = new Dictionary<string, string>(ids, StringComparer.OrdinalIgnoreCase);

    public IReadOnlyDictionary<string, string> All => _ids;
    public string? Get(string source) => _ids.TryGetValue(source, out var v) ? v : null;
    public ExternalIds With(string source, string id) =>
        new(new Dictionary<string, string>(_ids, StringComparer.OrdinalIgnoreCase) { [source] = id });
}
