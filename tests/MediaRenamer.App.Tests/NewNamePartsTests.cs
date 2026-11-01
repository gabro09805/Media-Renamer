using MediaRenamer.App.ViewModels;

namespace MediaRenamer.App.Tests;

public class NewNamePartsTests
{
    [Fact]
    public void In_place_rename_shows_only_the_file_name()
    {
        var parts = FileRowViewModel.BuildParts(@"C:\in\a.mkv", @"C:\in\b.mkv", null, "");
        Assert.Equal("b.mkv", Assert.Single(parts).Text);
    }

    [Fact]
    public void Moved_file_shows_the_root_then_one_part_per_folder_with_different_colours()
    {
        var parts = FileRowViewModel.BuildParts(@"C:\in\a.mkv", @"D:\Lib\One Piece\Season 01\b.mkv", @"D:\Lib", "");
        Assert.Equal(new[] { "D:/Lib/", "One Piece/", "Season 01/", "b.mkv" }, parts.Select(p => p.Text));
        Assert.Equal(3, parts.Take(3).Select(p => p.Brush).Distinct().Count());
        Assert.True(parts[^1].IsPlain);
    }

    [Fact]
    public void Without_a_library_root_the_source_folder_is_the_base()
    {
        var parts = FileRowViewModel.BuildParts(@"C:\in\a.mkv", @"C:\in\Show\Season 1\b.mkv", null, "");
        Assert.Equal(new[] { "C:/in/", "Show/", "Season 1/", "b.mkv" }, parts.Select(p => p.Text));
    }
}
