using MediaRenamer.Bindings.Catalog;
using MediaRenamer.Core.Matching;
using MediaRenamer.Model;
using MediaRenamer.Plugin.Abstractions;

namespace MediaRenamer.Core.Tests;

public class MultilingualTests
{
    private static Series Bofuri() => new()
    {
        Source = new ProviderRef("fake", ProviderMode.Episode, "1"),
        Titles = new TitleSet(new[]
        {
            new LocalizedText("痛いのは嫌なので防御力に極振りしたいと思います。", "ja", TitleKind.Original),
            new LocalizedText("Bofuri: I Don't Want to Get Hurt, so I'll Max Out My Defense.", "en", TitleKind.English),
        }),
    };

    private static string? Name(MissingTranslationBehavior behavior)
    {
        var prefs = new MatchPreferences("it", MatchMode.Opportunistic, EpisodeOrder.Airdate, behavior);
        return new MediaBindingSource(null, null, Bofuri(), null, prefs).SeriesOrMovieName("it");
    }

    [Fact]
    public void Missing_translation_uses_english_when_chosen() =>
        Assert.StartsWith("Bofuri", Name(MissingTranslationBehavior.UseEnglish));

    [Fact]
    public void Missing_translation_uses_original_when_chosen() =>
        Assert.StartsWith("痛い", Name(MissingTranslationBehavior.UseOriginal));

    [Fact]
    public void Episode_title_chain_is_preferred_then_english_then_primary()
    {
        var prefs = new MatchPreferences("it");
        var series = Bofuri();
        Episode Ep(params LocalizedText[] t) => new() { Source = new("fake", ProviderMode.Episode, "e"), Titles = new TitleSet((IEnumerable<LocalizedText>)t), Number = new Numbering(1, 1) };
        string? T(Episode e) => new MediaBindingSource(null, null, series, e, prefs).Resolve(e.Titles, "it");

        Assert.Equal("Pilota", T(Ep(new LocalizedText("Pilota", "it"), new LocalizedText("Pilot", "en", TitleKind.English))));
        Assert.Equal("Pilot", T(Ep(new LocalizedText("パイロット", "ja", TitleKind.Primary), new LocalizedText("Pilot", "en", TitleKind.English))));
        Assert.Equal("パイロット", T(Ep(new LocalizedText("パイロット", "ja", TitleKind.Primary))));
    }

    [Fact]
    public void English_is_always_requested_alongside_the_output_language()
    {
        Assert.Equal(new[] { "it", "en" }, LanguageTag.WithEnglish("it"));
        Assert.Equal(new[] { "en-US" }, LanguageTag.WithEnglish("en-US"));
    }

    [Fact]
    public void Splits_run_together_words()
    {
        Assert.Equal("One Piece", MatchService.SplitJoinedWords("OnePiece"));
        Assert.Null(MatchService.SplitJoinedWords("One Piece"));
        Assert.Null(MatchService.SplitJoinedWords("Naruto"));
    }

    [Fact]
    public async Task Matches_a_title_written_without_spaces()
    {
        var provider = new SpacedOnlySeries();
        var matches = await new MatchService().MatchAsync(new[] { new MediaFile { Path = "OnePiece_Ep_02_SUB_ITA.mp4",
            Detected = Parsing.FileNameParser.Parse("OnePiece_Ep_02_SUB_ITA.mp4").Info } }, ProviderMode.Episode,
            null, provider, new NoConfig(), new NoConfig(), new MatchPreferences(), default);
        Assert.NotNull(matches[0].Series);
        Assert.Equal(2, matches[0].Episode?.Number.Episode);
    }

    [Fact]
    public async Task Changing_language_keeps_the_match_and_refreshes_the_series()
    {
        var service = new MatchService();
        var matches = await service.MatchAsync(new[] { new MediaFile { Path = "Attack on Titan S01E02.mkv",
            Detected = Parsing.FileNameParser.Parse("Attack on Titan S01E02.mkv").Info } }, ProviderMode.Episode,
            null, new FakeSeries(), new NoConfig(), new NoConfig(), new MatchPreferences(), default);

        var relocalized = await service.RelocalizeAsync(matches, null, new FakeSeries(), new NoConfig(), new NoConfig(),
            new MatchPreferences("it"));

        Assert.Equal(matches.Count, relocalized.Count);
        Assert.Equal(matches[0].Episode!.Number, relocalized[0].Episode!.Number);
        Assert.Equal(matches[0].File.Path, relocalized[0].File.Path);
    }
}
