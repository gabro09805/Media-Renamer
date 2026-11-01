namespace MediaRenamer.Model;

/// <summary>Role played by a title variant. Variants are never overwritten during matching.</summary>
public enum TitleKind { Primary, Original, English, Localized, Alias }

/// <summary>What to do when no title exists in the preferred language (spec 10.4). Never crosses providers.</summary>
public enum MissingTranslationBehavior { UseOriginal, UseEnglish, UseProviderDefault }

/// <summary>A title in a given language. <paramref name="Language"/> is an ISO 639 / BCP-47 tag, or null if unknown.</summary>
public sealed record LocalizedText(string Value, string? Language, TitleKind Kind = TitleKind.Localized);

/// <summary>Immutable set of all title variants known for an entity.</summary>
public sealed class TitleSet
{
    public static readonly TitleSet Empty = new(Array.Empty<LocalizedText>());

    private readonly IReadOnlyList<LocalizedText> _items;

    public TitleSet(IEnumerable<LocalizedText> items)
    {
        _items = items.Where(i => !string.IsNullOrWhiteSpace(i.Value)).ToList();
    }

    public IReadOnlyList<LocalizedText> All => _items;

    /// <summary>Every distinct variant: used for matching, independent of the output language.</summary>
    public IEnumerable<string> AllForMatching() =>
        _items.Select(i => i.Value).Distinct(StringComparer.OrdinalIgnoreCase);

    public TitleSet With(params LocalizedText[] extra) => new(_items.Concat(extra));

    /// <summary>Resolves the display title for the output language only.</summary>
    public string? Resolve(string preferredLanguage, MissingTranslationBehavior missing = MissingTranslationBehavior.UseOriginal)
    {
        var hit = Find(preferredLanguage);
        if (hit is not null) return hit.Value;

        return missing switch
        {
            MissingTranslationBehavior.UseOriginal =>
                OfKind(TitleKind.Original)?.Value ?? OfKind(TitleKind.Primary)?.Value ?? _items.FirstOrDefault()?.Value,
            MissingTranslationBehavior.UseEnglish =>
                Find("en")?.Value ?? OfKind(TitleKind.English)?.Value ?? OfKind(TitleKind.Primary)?.Value ?? _items.FirstOrDefault()?.Value,
            _ => OfKind(TitleKind.Primary)?.Value ?? _items.FirstOrDefault()?.Value,
        };
    }

    private LocalizedText? OfKind(TitleKind kind) => _items.FirstOrDefault(i => i.Kind == kind);

    private LocalizedText? Find(string language)
    {
        var lang = LanguageTag.Primary(language);
        var exact = _items.FirstOrDefault(i =>
            i.Kind != TitleKind.Alias && string.Equals(i.Language, language, StringComparison.OrdinalIgnoreCase));
        if (exact is not null || language.Contains("Latn", StringComparison.OrdinalIgnoreCase)) return exact;
        return _items.FirstOrDefault(i =>
            i.Kind != TitleKind.Alias && i.Language is not null && LanguageTag.Primary(i.Language) == lang);
    }
}

public static class LanguageTag
{
    /// <summary>"pt-BR" -> "pt".</summary>
    public static string[] WithEnglish(string tag) =>
        LanguageTag.Primary(tag) == "en" ? new[] { tag } : new[] { tag, "en" };

    public static string Primary(string tag)
    {
        var i = tag.IndexOfAny(new[] { '-', '_' });
        return (i < 0 ? tag : tag[..i]).ToLowerInvariant();
    }
}
