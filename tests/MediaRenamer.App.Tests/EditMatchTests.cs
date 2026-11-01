using MediaRenamer.App.Services;
using MediaRenamer.App.ViewModels;
using MediaRenamer.Model;
using MediaRenamer.PluginHost;

namespace MediaRenamer.App.Tests;

public class EditMatchTests
{
    private static (EditMatchViewModel Vm, MatchResult Current) Create()
    {
        var services = new Env().CreateServices();
        var plugin = new FakePlugin();
        services.Registry.Register(plugin, new MediaRenamer.Infrastructure.PluginContext(
            new MediaRenamer.Infrastructure.Caching.MemoryResponseCache(), TimeSpan.FromHours(1), Path.GetTempPath(), () => new HttpClientHandler()));
        var entry = services.Registry.Find("fake", ProviderMode.Episode)!;
        var series = plugin.Provider.GetSeriesAsync(new("fake", ProviderMode.Episode, "7"), "en", null!, default).Result;
        var episodes = plugin.Provider.GetEpisodesAsync(series.Source, new[] { "en" }, null!, default).Result;
        var current = new MatchResult
        {
            File = new MediaFile { Path = Path.Combine(Path.GetTempPath(), "x.mkv") },
            Series = series,
            Episode = episodes[0],
            OriginalNumbering = new Numbering(1, 2),
        };
        return (new EditMatchViewModel(services, entry, current), current);
    }

    [Fact]
    public async Task Changing_only_the_episode_keeps_the_series()
    {
        var (vm, current) = Create();
        await vm.Loading;
        Assert.Equal(2, vm.Episodes.Count);
        Assert.Equal(current.Episode!.Number, vm.SelectedEpisode!.Episode.Number);

        vm.SelectedEpisode = vm.Episodes[1];
        var match = vm.Build()!;
        Assert.Equal(AssociationMode.Manual, match.Association);
        Assert.Equal(2, match.Episode!.Number.Episode);
        Assert.Same(current.Series, match.Series);
    }

    [Fact]
    public async Task Changing_the_series_preselects_the_episode_from_the_file_name()
    {
        var (vm, _) = Create();
        await vm.Loading;
        await vm.SearchAsync();
        Assert.Single(vm.Results);
        vm.SelectedResult = vm.Results[0];
        await vm.Loading;
        Assert.Equal(2, vm.SelectedEpisode!.Episode.Number.Episode);
        Assert.True(vm.CanConfirm);
        Assert.Equal("Show", vm.SelectedName);
    }

    [Fact]
    public async Task Cannot_confirm_without_an_episode()
    {
        var (vm, _) = Create();
        await vm.Loading;
        vm.SelectedEpisode = null;
        Assert.Null(vm.Build());
    }
}
