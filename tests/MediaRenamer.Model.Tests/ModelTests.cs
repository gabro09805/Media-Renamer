using MediaRenamer.Model;

namespace MediaRenamer.Model.Tests;

public class ModelTests
{
    private static ProviderRef Ref(string id = "1") => new("tmdb", ProviderMode.Episode, id);

    [Fact]
    public void ExternalIds_lookup_is_case_insensitive_and_With_is_immutable()
    {
        var ids = ExternalIds.Empty.With("TMDB", "42");
        Assert.Equal("42", ids.Get("tmdb"));
        Assert.Null(ExternalIds.Empty.Get("tmdb"));
        Assert.Null(ids.Get("anidb"));
    }

    [Fact]
    public void Season_preserves_provider_episode_order()
    {
        var eps = new[] { 5, 1, 3 }.Select(n => new Episode
        {
            Source = Ref(n.ToString()),
            Number = new Numbering(1, n),
        }).ToList();

        var season = new Season { Number = 1, Episodes = eps };

        Assert.Equal(new int?[] { 5, 1, 3 }, season.Episodes.Select(e => e.Number.Episode));
    }

    [Fact]
    public void Series_SeasonCount_reflects_seasons()
    {
        var series = new Series
        {
            Source = Ref(),
            Seasons = new[] { new Season { Number = 1 }, new Season { Number = 2 } },
        };
        Assert.Equal(2, series.SeasonCount);
    }

    [Fact]
    public void Year_comes_from_release_date_else_override()
    {
        Assert.Equal(2009, new Movie { Source = Ref(), ReleaseDate = new DateOnly(2009, 12, 18) }.Year);
        Assert.Equal(1996, new Movie { Source = Ref(), YearOverride = 1996 }.Year);
    }

    [Fact]
    public void Episode_with_season_zero_is_special()
    {
        Assert.True(new Episode { Source = Ref(), Number = new Numbering(0, 1) }.IsSpecial);
        Assert.False(new Episode { Source = Ref(), Number = new Numbering(1, 1) }.IsSpecial);
    }

    [Fact]
    public void MediaFile_derives_name_and_extension()
    {
        var f = new MediaFile { Path = @"C:\x\Serie.S01E01.mkv" };
        Assert.Equal("Serie.S01E01", f.OriginalName);
        Assert.Equal("mkv", f.Extension);
    }
}
