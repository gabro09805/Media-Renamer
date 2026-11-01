using MediaRenamer.Core.Parsing;
using Xunit;

namespace MediaRenamer.Core.Tests;

public class ParserRangeTests
{
    [Theory]
    [InlineData("Bofuri_Ep_01-02_SUB_ITA.mkv")]
    [InlineData("Bofuri Ep 01-02.mkv")]
    [InlineData("Bofuri_Ep_01-Ep_02_SUB_ITA.mkv")]
    public void Episode_range_is_detected(string name)
    {
        var i = FileNameParser.Parse(name).Info;
        Assert.Equal(new[] { 1, 2 }, i.Episodes);
        Assert.Equal("Bofuri", i.Title);
    }

    [Fact]
    public void Single_episode_still_works() =>
        Assert.Equal(new[] { 2 }, FileNameParser.Parse("Bofuri_Ep_02_SUB_ITA.mkv").Info.Episodes);
}
