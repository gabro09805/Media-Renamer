using MediaRenamer.App.Services;
using MediaRenamer.App.ViewModels;
using MediaRenamer.Model;
using MediaRenamer.Plugin.Abstractions;
using MediaRenamer.PluginHost;

namespace MediaRenamer.App.Tests;

internal sealed class FakeEpisodeProvider : IEpisodeProvider
{
    public int Searches { get; private set; }
    public string ProviderId => "fake";
    public string DisplayName => "Fake";
    public ProviderMode Mode => ProviderMode.Episode;
    public ProviderCapabilities Capabilities => ProviderCapabilities.SearchSeries;
    public IReadOnlyList<ConfigField> ConfigSchema => Array.Empty<ConfigField>();
    public Task<ConnectionTestResult> TestConnectionAsync(IProviderConfig config, CancellationToken ct) => Task.FromResult(new ConnectionTestResult(true, "ok"));

    private static readonly ProviderRef Ref = new("fake", ProviderMode.Episode, "7");

    public Task<IReadOnlyList<SearchResult>> SearchSeriesAsync(SearchQuery query, IProviderConfig config, CancellationToken ct)
    {
        Searches++;
        IReadOnlyList<SearchResult> hits = new[] { new SearchResult(Ref, new TitleSet(new[] { new LocalizedText("Show", "en", TitleKind.Primary) }), 2020, "series") };
        return Task.FromResult(hits);
    }

    public Task<Series> GetSeriesAsync(ProviderRef id, string language, IProviderConfig config, CancellationToken ct) =>
        Task.FromResult(new Series
        {
            Source = Ref,
            Titles = new TitleSet(new[] { new LocalizedText("Show", "en", TitleKind.Primary) }),
            ReleaseDate = new DateOnly(2020, 1, 1),
            Ids = new ExternalIds(new Dictionary<string, string> { ["tmdb"] = "7" }),
        });

    public Task<IReadOnlyList<Episode>> GetEpisodesAsync(ProviderRef id, IReadOnlyCollection<string> languages, IProviderConfig config, CancellationToken ct)
    {
        IReadOnlyList<Episode> list = new[]
        {
            new Episode { Source = Ref, Titles = new TitleSet(new[] { new LocalizedText("Pilot", "en", TitleKind.Primary) }), Number = new Numbering(1, 1, 1) },
            new Episode { Source = Ref, Titles = new TitleSet(new[] { new LocalizedText("Second", "en", TitleKind.Primary) }), Number = new Numbering(1, 2, 2) },
        };
        return Task.FromResult(list);
    }
}

internal sealed class FakePlugin : IProviderPlugin
{
    public FakeEpisodeProvider Provider { get; } = new();
    public string Id => "fake";
    public string Name => "Fake";
    public Version Version => new(1, 0);
    public int ApiVersion => PluginApi.Version;
    public IEnumerable<IMediaProvider> CreateProviders(IPluginContext context) => new IMediaProvider[] { Provider };
}

public class BindingInspectorTests
{
    private static (BindingInspectorViewModel Vm, FakeEpisodeProvider Fake, AppServices Services) Create()
    {
        var services = new Env().CreateServices();
        var plugin = new FakePlugin();
        services.Registry.Register(plugin, new MediaRenamer.Infrastructure.PluginContext(
            new MediaRenamer.Infrastructure.Caching.MemoryResponseCache(), TimeSpan.FromHours(1), Path.GetTempPath(), () => new HttpClientHandler()));
        var vm = new BindingInspectorViewModel(services);
        vm.SelectedProvider = services.Registry.Find("fake", ProviderMode.Episode);
        return (vm, plugin.Provider, services);
    }

    [Fact]
    public void Episodes_are_sorted_with_specials_last()
    {
        static Episode E(int? s, int? e) => new() { Source = new("f", ProviderMode.Episode, "1"), Number = new Numbering(s, e) };
        var sorted = BindingInspectorViewModel.Sorted(new[] { E(0, 2), E(2, 1), E(1, 10), E(0, 1), E(1, 2), E(1, 1) })
            .Select(x => $"{x.Number.Season}x{x.Number.Episode}").ToList();
        Assert.Equal(new[] { "1x1", "1x2", "1x10", "2x1", "0x1", "0x2" }, sorted);
    }

    [Fact]
    public void Only_providers_of_the_editor_kind_are_offered_and_the_title_says_which()
    {
        var services = new Env().CreateServices();
        var movies = new BindingInspectorViewModel(services, FormatKind.Movie);
        Assert.All(movies.Providers, p => Assert.Equal(ProviderMode.Movie, p.Provider.Mode));
        Assert.NotEmpty(movies.Providers);
        Assert.Contains("Movies", movies.Title);

        var series = new BindingInspectorViewModel(services, FormatKind.Series);
        Assert.All(series.Providers, p => Assert.Equal(ProviderMode.Episode, p.Provider.Mode));
        Assert.Contains("TV Series", series.Title);
    }

    [Fact]
    public void Nothing_is_searched_or_shown_until_the_user_asks()
    {
        var (vm, fake, _) = Create();
        Assert.Equal(0, fake.Searches);
        Assert.Empty(vm.Rows);
        Assert.Empty(vm.Results);
    }

    [Fact]
    public async Task Search_then_episode_then_shows_the_bindings()
    {
        var (vm, _, _) = Create();
        vm.Query = "show";
        await vm.SearchAsync();
        Assert.Equal("Show (2020)", Assert.Single(vm.Results).Label);

        vm.SelectedResult = vm.Results[0];
        vm.ConfirmSelection();
        await vm.Loading;
        Assert.True(vm.IsSeries);
        Assert.Equal(new[] { "1x01 - Pilot", "1x02 - Second" }, vm.Episodes.Select(e => e.Label));
        Assert.Empty(vm.Rows);

        vm.SelectedEpisode = vm.Episodes[1];
        string V(string n) => vm.Rows.First(r => r.Name == n).Value;
        Assert.Equal("Show", V("n"));
        Assert.Equal("Second", V("t"));
        Assert.Equal("1x02", V("sxe"));
        Assert.Equal("7", V("tmdbid"));
    }

    [Fact]
    public async Task Filter_scope_limits_the_search_to_bindings_or_values()
    {
        var (vm, _, _) = Create();
        vm.Query = "show";
        await vm.SearchAsync();
        vm.SelectedResult = vm.Results[0];
        vm.ConfirmSelection();
        await vm.Loading;
        vm.SelectedEpisode = vm.Episodes[1];

        vm.Filter = "Second";
        vm.FilterScope = "Bindings only";
        Assert.DoesNotContain(vm.Rows, r => r.Name == "t");
        vm.FilterScope = "Values only";
        Assert.Contains(vm.Rows, r => r.Name == "t");
        Assert.All(vm.Rows, r => Assert.Contains("Second", r.Value, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Changing_provider_in_the_picker_clears_results_but_keeps_the_chosen_series()
    {
        var (vm, _, services) = Create();
        vm.Query = "show";
        await vm.SearchAsync();
        vm.SelectedResult = vm.Results[0];
        vm.ConfirmSelection();
        await vm.Loading;
        vm.SelectedEpisode = vm.Episodes[0];
        vm.SelectedProvider = services.Registry.Find("tmdb", ProviderMode.Episode);
        Assert.Empty(vm.Results);
        Assert.Equal(2, vm.Episodes.Count);
        Assert.Equal("Show (2020)", vm.ChosenName);
        Assert.NotEmpty(vm.Rows);
    }
}
