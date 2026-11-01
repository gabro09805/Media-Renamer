using System.Net;
using MediaRenamer.App.Services;
using MediaRenamer.App.ViewModels;
using MediaRenamer.Infrastructure;
using MediaRenamer.Infrastructure.Caching;
using MediaRenamer.Model;
using MediaRenamer.Platform;

namespace MediaRenamer.App.Tests;

internal sealed class FakeTransport : HttpMessageHandler
{
    public List<(Uri Uri, string? Auth)> Requests { get; } = new();

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        Requests.Add((request.RequestUri!, request.Headers.Authorization?.ToString()));
        var ok = request.Headers.Authorization?.Parameter == "good-token";
        return Task.FromResult(new HttpResponseMessage(ok ? HttpStatusCode.OK : HttpStatusCode.Unauthorized)
        {
            Content = new StringContent("{}"),
        });
    }
}

internal sealed class Env
{
    public Env()
    {
        Root = Path.Combine(Path.GetTempPath(), "mr-tests", Guid.NewGuid().ToString("N"));
        Paths = new AppPaths(Root);
        Secrets = new InMemorySecretStore();
        Transport = new FakeTransport();
    }

    public string Root { get; }
    public AppPaths Paths { get; }
    public InMemorySecretStore Secrets { get; }
    public FakeTransport Transport { get; }

    public AppServices CreateServices() => AppServices.Create(Paths, Secrets,
        new PluginContext(new MemoryResponseCache(), TimeSpan.FromHours(1), Paths.Data, () => Transport));
}

public class AppServicesTests
{
    [Fact]
    public void Registers_builtin_providers_as_distinct_mode_entries()
    {
        var s = new Env().CreateServices();
        Assert.Equal(new[] { "tmdb.movie", "tmdb.episode", "anidb.episode" }, s.Registry.All.Select(e => e.Key));
    }

    [Fact]
    public void Stored_enabled_flags_are_applied_at_startup()
    {
        var env = new Env();
        var first = env.CreateServices();
        var movie = first.Registry.Find("tmdb", ProviderMode.Movie)!;
        first.Settings.Save(movie.Provider, false, new Dictionary<string, string>());

        var second = env.CreateServices();
        Assert.False(second.Registry.Find("tmdb", ProviderMode.Movie)!.IsEnabled);
        Assert.True(second.Registry.Find("tmdb", ProviderMode.Episode)!.IsEnabled);
    }
}

public class PreferencesTests
{
    private static ProviderSettingsViewModel TmdbMovie(AppServices s) =>
        new PreferencesViewModel(s).Providers.Single(p => p.Title == "TMDB");

    [Fact]
    public void One_card_per_provider_with_the_token_asked_only_once_and_a_single_switch()
    {
        var vm = new PreferencesViewModel(new Env().CreateServices());

        Assert.Equal(new[] { "TMDB", "AniDB" }, vm.Providers.Select(p => p.Title));
        Assert.Single(vm.Providers[0].Fields);
        Assert.Equal(2, vm.Providers[0].Entries.Count);
    }

    [Fact]
    public void Providers_have_no_language_option_because_language_is_an_output_preference()
    {
        var vm = new PreferencesViewModel(new Env().CreateServices());
        Assert.DoesNotContain(vm.Providers.SelectMany(p => p.Fields), f => f.Field.Type == Plugin.Abstractions.ConfigFieldType.Language);
    }

    [Fact]
    public void Save_enables_or_disables_the_whole_provider()
    {
        var env = new Env();
        var s = env.CreateServices();
        var vm = TmdbMovie(s);
        vm.Fields.Single(f => f.IsSecret).Value = "good-token";
        vm.IsEnabled = false;

        vm.SaveCommand.Execute(null);

        Assert.False(s.Registry.Find("tmdb", ProviderMode.Movie)!.IsEnabled);
        Assert.False(s.Registry.Find("tmdb", ProviderMode.Episode)!.IsEnabled);
        Assert.Equal("good-token", env.Secrets.Get("tmdb:accessToken"));
        Assert.False(env.CreateServices().Registry.Find("tmdb", ProviderMode.Movie)!.IsEnabled);

        vm.IsEnabled = true;
        vm.SaveCommand.Execute(null);
        Assert.True(s.Registry.Find("tmdb", ProviderMode.Movie)!.IsEnabled);
        Assert.True(s.Registry.Find("tmdb", ProviderMode.Episode)!.IsEnabled);
    }

    [Fact]
    public void Save_requires_mandatory_fields_when_enabled()
    {
        var s = new Env().CreateServices();
        var vm = TmdbMovie(s);

        vm.SaveCommand.Execute(null);

        Assert.True(vm.StatusIsError);
        Assert.Contains("Required", vm.Status);
    }

    [Fact]
    public void Save_stores_secret_in_secret_store_and_both_modes_use_it()
    {
        var env = new Env();
        var s = env.CreateServices();
        var vm = TmdbMovie(s);
        vm.Fields.Single(f => f.IsSecret).Value = "good-token";

        vm.SaveCommand.Execute(null);

        Assert.False(vm.StatusIsError);
        Assert.Equal("good-token", env.Secrets.Get("tmdb:accessToken"));
        Assert.DoesNotContain("good-token", File.ReadAllText(Path.Combine(env.Paths.Config, "providers.json")));

        foreach (var mode in new[] { ProviderMode.Movie, ProviderMode.Episode })
            Assert.Equal("good-token", s.Settings.GetConfig(s.Registry.Find("tmdb", mode)!.Provider).Get("accessToken"));
    }

    [Fact]
    public void Secret_fields_are_masked()
    {
        var vm = TmdbMovie(new Env().CreateServices());
        Assert.NotEqual('\0', vm.Fields.Single(f => f.IsSecret).PasswordChar);
        Assert.All(vm.Fields.Where(f => !f.IsSecret), f => Assert.Equal('\0', f.PasswordChar));
    }

    [Fact]
    public async Task Test_connection_uses_the_unsaved_form_values()
    {
        var env = new Env();
        var vm = TmdbMovie(env.CreateServices());
        var secret = vm.Fields.Single(f => f.IsSecret);

        secret.Value = "bad-token";
        await vm.TestCommand.ExecuteAsync(null);
        Assert.True(vm.StatusIsError);
        Assert.StartsWith("✘", vm.Status);

        secret.Value = "good-token";
        await vm.TestCommand.ExecuteAsync(null);
        Assert.False(vm.StatusIsError);
        Assert.StartsWith("✔", vm.Status);
        Assert.Null(env.Secrets.Get("tmdb:accessToken"));
        Assert.False(vm.IsBusy);
    }

    [Fact]
    public async Task Test_connection_without_credentials_reports_the_error_instead_of_throwing()
    {
        var vm = TmdbMovie(new Env().CreateServices());
        await vm.TestCommand.ExecuteAsync(null);
        Assert.True(vm.StatusIsError);
    }
}

public class RenameProviderMenuTests
{
    [Fact]
    public void Lists_enabled_providers_by_mode()
    {
        var vm = new RenameViewModel(new Env().CreateServices());
        Assert.Equal(new[] { "tmdb.episode", "anidb.episode" }, vm.EpisodeProviders.Select(e => e.Key));
        Assert.Equal(new[] { "tmdb.movie" }, vm.MovieProviders.Select(e => e.Key));
        Assert.Equal("No provider selected", vm.ActiveProviderText);
    }

    [Fact]
    public void Selection_is_explicit_and_shown_to_the_user()
    {
        var s = new Env().CreateServices();
        var vm = new RenameViewModel(s);
        vm.SelectProviderCommand.Execute(s.Registry.Find("tmdb", ProviderMode.Movie));
        Assert.Equal("Provider: TMDB – Movie Mode", vm.ActiveProviderText);
    }

    [Fact]
    public void Disabling_a_provider_removes_it_and_clears_it_if_active_without_falling_back()
    {
        var s = new Env().CreateServices();
        var vm = new RenameViewModel(s);
        var tmdbEp = s.Registry.Find("tmdb", ProviderMode.Episode)!;
        vm.SelectProviderCommand.Execute(tmdbEp);

        s.Registry.SetEnabled(tmdbEp, false);

        Assert.Null(vm.ActiveProvider);
        Assert.Equal(new[] { "anidb.episode" }, vm.EpisodeProviders.Select(e => e.Key));
    }

    [Fact]
    public void ProvidersChanged_is_raised_so_the_menu_can_be_rebuilt()
    {
        var s = new Env().CreateServices();
        var vm = new RenameViewModel(s);
        var raised = 0;
        vm.ProvidersChanged += () => raised++;
        s.Registry.SetEnabled(s.Registry.Find("anidb", ProviderMode.Episode)!, false);
        Assert.True(raised > 0);
    }
}
