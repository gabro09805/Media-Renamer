using Avalonia;
using MediaRenamer.App.Services;

namespace MediaRenamer.App.Tests;

public class PlacementMathTests
{
    private static readonly PixelRect Screen = new(0, 0, 1920, 1040);

    [Fact]
    public void A_window_fully_on_screen_is_left_alone()
    {
        var frame = new PixelRect(100, 80, 800, 600);
        Assert.Equal(frame, PlacementMath.Fit(frame, Screen));
    }

    [Fact]
    public void A_window_partly_off_screen_is_recentered_without_changing_its_size()
    {
        var fitted = PlacementMath.Fit(new PixelRect(1500, 700, 800, 600), Screen);
        Assert.Equal(new PixelRect(560, 220, 800, 600), fitted);
    }

    [Fact]
    public void A_window_bigger_than_the_screen_is_shrunk_to_fit()
    {
        var fitted = PlacementMath.Fit(new PixelRect(-50, -20, 3000, 2000), Screen);
        Assert.Equal(Screen, fitted);
    }

    [Fact]
    public void Only_the_oversized_dimension_is_reduced()
    {
        var fitted = PlacementMath.Fit(new PixelRect(0, 0, 1000, 1500), Screen);
        Assert.Equal(1000, fitted.Width);
        Assert.Equal(1040, fitted.Height);
        Assert.Equal(0, fitted.Y);
    }

    [Fact]
    public void The_screen_with_the_largest_overlap_is_chosen()
    {
        var second = new PixelRect(1920, 0, 1280, 1024);
        var areas = new[] { Screen, second };
        Assert.Equal(second, PlacementMath.ChooseArea(new PixelRect(2000, 100, 600, 400), areas));
        Assert.Equal(Screen, PlacementMath.ChooseArea(new PixelRect(9000, 9000, 600, 400), areas));
    }
}
