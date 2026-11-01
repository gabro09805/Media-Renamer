using MediaRenamer.Model;

namespace MediaRenamer.Model.Tests;

public class TitleSetTests
{
    private static readonly TitleSet Titles = new(new[]
    {
        new LocalizedText("Detective Conan", "en", TitleKind.English),
        new LocalizedText("Meitantei Conan", "ja-Latn", TitleKind.Original),
        new LocalizedText("Detective Conan IT", "it"),
        new LocalizedText("Case Closed", "en", TitleKind.Alias),
    });

    [Fact]
    public void Resolve_uses_preferred_language_when_available()
        => Assert.Equal("Detective Conan IT", Titles.Resolve("it"));

    [Fact]
    public void Resolve_matches_regional_tag_by_primary_language()
        => Assert.Equal("Detective Conan IT", Titles.Resolve("it-IT"));

    [Fact]
    public void Resolve_missing_translation_uses_original()
        => Assert.Equal("Meitantei Conan", Titles.Resolve("fr", MissingTranslationBehavior.UseOriginal));

    [Fact]
    public void Resolve_missing_translation_uses_english()
        => Assert.Equal("Detective Conan", Titles.Resolve("fr", MissingTranslationBehavior.UseEnglish));

    [Fact]
    public void Aliases_are_never_used_for_output_but_are_used_for_matching()
    {
        Assert.NotEqual("Case Closed", Titles.Resolve("en"));
        Assert.Contains("Case Closed", Titles.AllForMatching());
    }

    [Fact]
    public void Changing_language_does_not_mutate_the_set()
    {
        var before = Titles.All.Count;
        _ = Titles.Resolve("it");
        _ = Titles.Resolve("en");
        Assert.Equal(before, Titles.All.Count);
    }
}
