using MediaRenamer.App.ViewModels;
using MediaRenamer.Model;
using MediaRenamer.PluginHost;

namespace MediaRenamer.App.Tests;

public class SearchEpisodesTests
{
    private static SearchEpisodesViewModel Create()
    {
        var services = new Env().CreateServices();
        services.Registry.Register(new FakePlugin(), new MediaRenamer.Infrastructure.PluginContext(
            new MediaRenamer.Infrastructure.Caching.MemoryResponseCache(), TimeSpan.FromHours(1), Path.GetTempPath(), () => new HttpClientHandler()));
        return new SearchEpisodesViewModel(services, new RenameViewModel(services));
    }

    [Fact]
    public async Task Selecting_a_result_lists_seasons_and_selection_follows_the_season_checkbox()
    {
        var vm = Create();
        vm.SelectedProvider = vm.Providers.First(p => p.Provider.ProviderId == "fake");
        vm.Query = "x";
        await vm.SearchAsync();
        Assert.NotEmpty(vm.Results);

        vm.SelectedResult = vm.Results[0];
        for (var i = 0; i < 100 && vm.Seasons.Count == 0; i++) await Task.Delay(20);
        Assert.NotEmpty(vm.Seasons);

        vm.Seasons[0].IsSelected = true;
        Assert.Equal(vm.Seasons[0].Episodes.Count, vm.SelectedCount);
        vm.Seasons[0].Episodes[0].IsSelected = false;
        Assert.False(vm.Seasons[0].IsSelected);
        Assert.Equal(vm.Seasons[0].Episodes.Count - 1, vm.SelectedCount);
    }
}
