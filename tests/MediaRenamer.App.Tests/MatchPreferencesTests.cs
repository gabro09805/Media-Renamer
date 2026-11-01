using MediaRenamer.App.ViewModels;
using MediaRenamer.Infrastructure.Settings;
using MediaRenamer.Model;

namespace MediaRenamer.App.Tests;

public class MatchPreferencesTests
{
    private static string TempFile() =>
        Path.Combine(Path.GetTempPath(), "mr-tests", Guid.NewGuid().ToString("N"), "match-preferences.json");

    [Fact]
    public void Defaults_match_the_filebot_dialog()
    {
        var p = new MatchPreferencesStore(TempFile()).Current;
        Assert.Equal("en", p.OutputLanguage);
        Assert.Equal(MatchMode.Opportunistic, p.Mode);
        Assert.Equal(EpisodeOrder.Airdate, p.Order);
    }

    [Fact]
    public void Store_persists_and_notifies()
    {
        var path = TempFile();
        var store = new MatchPreferencesStore(path);
        MatchPreferences? seen = null;
        store.Changed += v => seen = v;
        var value = new MatchPreferences("it", MatchMode.Strict, EpisodeOrder.Dvd);

        store.Save(value);

        Assert.Equal(value, seen);
        Assert.Equal(value, new MatchPreferencesStore(path).Current);
    }

    [Fact]
    public void Corrupt_file_falls_back_to_defaults()
    {
        var path = TempFile();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "{ nope");
        Assert.Equal(new MatchPreferences(), new MatchPreferencesStore(path).Current);
    }

    [Fact]
    public void Dialog_view_model_reflects_and_returns_the_choices()
    {
        var vm = new MatchPreferencesViewModel(new MatchPreferences("it", MatchMode.Strict, EpisodeOrder.Absolute));
        Assert.Equal("Italian", vm.SelectedLanguage.Name);
        Assert.True(vm.IsStrict);
        Assert.False(vm.IsOpportunistic);
        Assert.True(vm.Orders.Single(o => o.Value == EpisodeOrder.Absolute).IsChecked);

        vm.SelectedLanguage = MatchPreferencesViewModel.Languages.Single(l => l.Code == "de");
        vm.IsOpportunistic = true;
        vm.Orders.Single(o => o.Value == EpisodeOrder.Absolute).IsChecked = false;
        vm.Orders.Single(o => o.Value == EpisodeOrder.Digital).IsChecked = true;

        Assert.Equal(new MatchPreferences("de", MatchMode.Opportunistic, EpisodeOrder.Digital), vm.ToPreferences());
    }

    [Fact]
    public void Unknown_stored_language_falls_back_to_its_primary_subtag_or_the_first_entry()
    {
        Assert.Equal("French", new MatchPreferencesViewModel(new MatchPreferences("fr-CA")).SelectedLanguage.Name);
        Assert.Equal("English", new MatchPreferencesViewModel(new MatchPreferences("xx")).SelectedLanguage.Name);
    }

    [Fact]
    public void Romanized_Japanese_is_distinct_from_Japanese_and_all_names_are_full_english()
    {
        var vm = new MatchPreferencesViewModel(new MatchPreferences("ja-Latn"));
        Assert.Equal("Romanized Japanese", vm.SelectedLanguage.Name);
        Assert.Equal("Japanese", new MatchPreferencesViewModel(new MatchPreferences("ja")).SelectedLanguage.Name);
        Assert.All(MatchPreferencesViewModel.Languages, l => Assert.True(l.Name.Length > 2));
    }
}
