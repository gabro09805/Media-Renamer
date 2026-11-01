using MediaRenamer.Bindings.Catalog;
using MediaRenamer.Bindings.Formats;

namespace MediaRenamer.Bindings.Tests;

public class ExtendedBindingTests
{
    private static readonly FormatRenderer Renderer = new();

    private static string R(string format, MediaBindingSource? source = null)
    {
        var result = Renderer.Render(format, source ?? TestMedia.EpisodeSource());
        Assert.True(result.Success, result.Error?.Message);
        return result.RelativePath!;
    }

    [Theory]
    [InlineData("{plex}", "TV Shows/Breaking Bad/Season 01/Breaking Bad - S01E02 - Cat's in the Bag")]
    [InlineData("{plex.year}", "TV Shows/Breaking Bad (2008)/Season 01/Breaking Bad (2008) - S01E02 - Cat's in the Bag")]
    [InlineData("{plex.id}", "TV Shows/Breaking Bad {tmdb-1396}/Season 01/Breaking Bad - S01E02 - Cat's in the Bag")]
    [InlineData("{plex.year.id}", "TV Shows/Breaking Bad (2008) {tmdb-1396}/Season 01/Breaking Bad (2008) - S01E02 - Cat's in the Bag")]
    [InlineData("{plex.name}", "Breaking Bad - S01E02 - Cat's in the Bag")]
    [InlineData("{kodi}", "TV Shows/Breaking Bad (2008)/Season 1/Breaking Bad (2008) - 1x02 - Cat's in the Bag")]
    [InlineData("{kodi.id}", "TV Shows/Breaking Bad (2008) {tmdb=1396}/Season 1/Breaking Bad (2008) - 1x02 - Cat's in the Bag")]
    [InlineData("{emby.id}", "TV Shows/Breaking Bad (2008) [tmdbid=1396]/Season 01/Breaking Bad (2008) - S01E02 - Cat's in the Bag")]
    [InlineData("{jellyfin.id}", "TV Shows/Breaking Bad (2008) [tmdbid-1396]/Season 01/Breaking Bad (2008) - S01E02 - Cat's in the Bag")]
    public void Library_layouts_keep_their_folders(string format, string expected) => Assert.Equal(expected, R(format));

    [Fact]
    public void Movie_layouts()
    {
        Assert.Equal("Movies/Inception (2010)/Inception (2010)", R("{plex}", TestMedia.MovieSource()));
    }

    [Theory]
    [InlineData("{es}", "2")]
    [InlineData("{sy}", "2008")]
    [InlineData("{decade}", "2000")]
    [InlineData("{az}", "B")]
    [InlineData("{type}", "Episode")]
    [InlineData("{startdate}", "2008-01-20")]
    [InlineData("{order.dvd.sxe}", "1x03")]
    [InlineData("{episode.title}", "Cat's in the Bag")]
    [InlineData("{series.name}", "Breaking Bad")]
    [InlineData("{localize.it.t}", "Il gatto è nel sacco")]
    [InlineData("{lang.ISO2}", "en")]
    [InlineData("{hd}", "HD")]
    public void Extended_bindings(string format, string expected) => Assert.Equal(expected, R(format));

    [Fact]
    public void Language_codes_resolve_iso3_and_subtags()
    {
        Assert.Equal("ja", LanguageCodes.Find("jpn")?.Iso2);
        Assert.Equal("ja", LanguageCodes.Find("ja-Latn")?.Iso2);
        Assert.Equal("Italian", LanguageCodes.Find("it")?.Name);
    }

    [Fact]
    public void Every_binding_resolves_without_throwing()
    {
        foreach (var source in new[] { TestMedia.EpisodeSource(), TestMedia.MovieSource() })
            Assert.NotEmpty(BindingExplorer.Rows(source));
    }
}
