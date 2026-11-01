namespace MediaRenamer.Bindings.Catalog;

public sealed record LanguageInfo(string Iso2, string Iso3, string Name);

/// <summary>ISO 639 codes and English names of the supported languages.</summary>
public static class LanguageCodes
{
    public static readonly IReadOnlyList<LanguageInfo> All = new LanguageInfo[]
    {
        new("en", "eng", "English"), new("it", "ita", "Italian"), new("de", "deu", "German"), new("fr", "fra", "French"),
        new("es", "spa", "Spanish"), new("pt", "por", "Portuguese"), new("ru", "rus", "Russian"), new("zh", "zho", "Chinese"),
        new("ja", "jpn", "Japanese"), new("ko", "kor", "Korean"), new("ar", "ara", "Arabic"), new("bg", "bul", "Bulgarian"),
        new("ca", "cat", "Catalan"), new("hr", "hrv", "Croatian"), new("cs", "ces", "Czech"), new("da", "dan", "Danish"),
        new("nl", "nld", "Dutch"), new("fi", "fin", "Finnish"), new("el", "ell", "Greek"), new("he", "heb", "Hebrew"),
        new("hi", "hin", "Hindi"), new("hu", "hun", "Hungarian"), new("id", "ind", "Indonesian"), new("no", "nor", "Norwegian"),
        new("pl", "pol", "Polish"), new("ro", "ron", "Romanian"), new("sk", "slk", "Slovak"), new("sv", "swe", "Swedish"),
        new("th", "tha", "Thai"), new("tr", "tur", "Turkish"), new("uk", "ukr", "Ukrainian"), new("vi", "vie", "Vietnamese"),
    };

    /// <summary>Finds a language from a tag such as "it", "ita", "pt-BR" or "ja-Latn" (only the primary subtag counts).</summary>
    public static LanguageInfo? Find(string? tag)
    {
        if (string.IsNullOrWhiteSpace(tag)) return null;
        var primary = tag.Split('-', '_')[0].ToLowerInvariant();
        return All.FirstOrDefault(l => l.Iso2 == primary || l.Iso3 == primary);
    }
}
