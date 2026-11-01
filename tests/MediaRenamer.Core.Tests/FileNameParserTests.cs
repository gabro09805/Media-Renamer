using MediaRenamer.Core.Parsing;

namespace MediaRenamer.Core.Tests;

public class FileNameParserTests
{
    [Theory]
    [InlineData("Breaking.Bad.S02E05.720p.BluRay.x264-DEMAND.mkv", "Breaking Bad", 2, 5)]
    [InlineData("The Office (US) - 3x07 - Branch Closing.avi", "The Office (US)", 3, 7)]
    [InlineData("show_name_s01e02.mp4", "show name", 1, 2)]
    [InlineData("Show Season 2 Episode 4.mkv", "Show", 2, 4)]
    public void Parses_series_numbering(string file, string title, int s, int e)
    {
        var (info, _) = FileNameParser.Parse(file);
        Assert.Equal(title, info.Title);
        Assert.Equal(s, info.Season);
        Assert.Equal(e, info.Episodes[0]);
    }

    [Fact]
    public void Parses_ep_number_and_ignores_sub_ita_tags()
    {
        var (info, _) = FileNameParser.Parse("Bofuri_Ep_01_SUB_ITA.mkv");
        Assert.Equal("Bofuri", info.Title);
        Assert.Equal(new[] { 1 }, info.Episodes);
        Assert.Null(info.Season);
        Assert.Null(info.EpisodeTitle);
    }

    [Fact]
    public void Parses_multi_episode()
    {
        var (info, _) = FileNameParser.Parse("Show.S01E01E02.mkv");
        Assert.Equal(new[] { 1, 2 }, info.Episodes);
    }

    [Fact]
    public void Parses_movie_with_year_and_tags()
    {
        var (info, tech) = FileNameParser.Parse("Inception.2010.1080p.BluRay.x264-GRP.mkv");
        Assert.Equal("Inception", info.Title);
        Assert.Equal(2010, info.Year);
        Assert.Null(info.Season);
        Assert.Equal(1080, tech.Height);
        Assert.Equal("x264", tech.VideoCodec);
        Assert.Equal("BluRay", tech.Source);
        Assert.Equal("GRP", info.ReleaseGroup);
    }

    [Fact]
    public void Parses_anime_absolute_with_group()
    {
        var (info, _) = FileNameParser.Parse("[SubsPlease] Clevatess - 01 (1080p) [ABCD1234].mkv");
        Assert.Equal("Clevatess", info.Title);
        Assert.Equal(1, info.Absolute);
        Assert.Equal("SubsPlease", info.ReleaseGroup);
    }

    [Fact]
    public void Title_that_is_a_year_stays_title()
    {
        var (info, _) = FileNameParser.Parse("2012.2009.720p.mkv");
        Assert.Equal("2012", info.Title);
        Assert.Equal(2009, info.Year);
    }
}
