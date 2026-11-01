using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MediaRenamer.App.Services;
using MediaRenamer.Core.Matching;
using MediaRenamer.Model;
using MediaRenamer.Plugin.Abstractions;
using MediaRenamer.PluginHost;

namespace MediaRenamer.App.ViewModels;

/// <summary>
/// "Edit Match" dialog for one file. For series the user can pick another episode of the current series
/// (episode only) or search another series and pick its episode (series change). For movies, search and pick the movie.
/// Uses only the provider the match was made with: no automatic provider switching.
/// </summary>
public sealed partial class EditMatchViewModel : ViewModelBase
{
    private readonly AppServices _services;
    private readonly ProviderEntry _provider;
    private readonly MatchResult _current;
    private Series? _series;
    private Movie? _movie;
    private int _version;
    private Task _loading = Task.CompletedTask;

    public EditMatchViewModel(AppServices services, ProviderEntry provider, MatchResult current)
    {
        _services = services;
        _provider = provider;
        _current = current;
        IsSeries = provider.Provider.Mode == ProviderMode.Episode;
        Title = "Edit Match - " + System.IO.Path.GetFileName(current.File.Path);
        Query = current.Series?.Titles.Resolve(Language, MissingTranslationBehavior.UseEnglish)
                ?? current.Movie?.Titles.Resolve(Language, MissingTranslationBehavior.UseEnglish)
                ?? current.File.Detected.Title ?? "";
        _loading = IsSeries ? LoadCurrentSeriesAsync() : Task.CompletedTask;
    }

    public string Title { get; }
    public bool IsSeries { get; }
    public string ProviderText => "Provider: " + _provider.Label;
    public string SelectedName => IsSeries
        ? (_series is null ? "No series selected" : _series.Titles.Resolve(Language, MissingTranslationBehavior.UseEnglish) ?? "Series")
        : (_movie is null ? "No movie selected" : _movie.Titles.Resolve(Language, MissingTranslationBehavior.UseEnglish) ?? "Movie");

    public ObservableCollection<ResultItem> Results { get; } = new();
    public ObservableCollection<EpisodeItem> Episodes { get; } = new();

    [ObservableProperty] private string _query = "";
    [ObservableProperty] private ResultItem? _selectedResult;
    [ObservableProperty] private EpisodeItem? _selectedEpisode;
    [ObservableProperty] private string _status = "Pick an episode, or search for another series.";
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private bool _hasResults;

    /// <summary>"Apply to all" (every file of the same series/movie, episode by episode) vs "Apply only to selected item".</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ApplyToSelectedOnly))]
    private bool _applyToAll;

    public bool ApplyToSelectedOnly
    {
        get => !ApplyToAll;
        set => ApplyToAll = !value;
    }

    public MatchResult Current => _current;
    public Series? ChosenSeries => _series;
    public Movie? ChosenMovie => _movie;
    public IReadOnlyList<Episode> AllEpisodes { get; private set; } = Array.Empty<Episode>();

    /// <summary>Completes when the last load has finished (used by tests).</summary>
    public Task Loading => _loading;

    private string Language => _services.MatchPreferences.Current.OutputLanguage;

    public bool CanConfirm => IsSeries ? _series is not null && SelectedEpisode is not null : _movie is not null;

    partial void OnSelectedEpisodeChanged(EpisodeItem? value) => OnPropertyChanged(nameof(CanConfirm));

    partial void OnSelectedResultChanged(ResultItem? value)
    {
        if (value is not null) _loading = LoadResultAsync(value);
    }

    /// <summary>The match to apply, or null while the choice is incomplete.</summary>
    public MatchResult? Build()
    {
        if (!CanConfirm) return null;
        return IsSeries
            ? MatchService.Manual(_current.File, _series, SelectedEpisode!.Episode, null, _current.OriginalNumbering)
            : MatchService.Manual(_current.File, null, null, _movie, _current.OriginalNumbering);
    }

    [RelayCommand]
    public async Task SearchAsync()
    {
        if (string.IsNullOrWhiteSpace(Query)) return;
        var version = ++_version;
        IsBusy = true;
        Status = "Searching...";
        try
        {
            var config = _services.Settings.GetConfig(_provider.Provider);
            var query = new SearchQuery(Query.Trim(), null, Language);
            var hits = _provider.Provider switch
            {
                IEpisodeProvider p => await p.SearchSeriesAsync(query, config, CancellationToken.None),
                IMovieProvider p => await p.SearchMovieAsync(query, config, CancellationToken.None),
                _ => Array.Empty<SearchResult>(),
            };
            if (version != _version) return;
            SelectedResult = null;
            Results.Clear();
            foreach (var h in hits) Results.Add(new ResultItem(h, Label(h), loadThumbnail: true));
            HasResults = hits.Count > 0;
            Status = hits.Count == 0 ? "No results." : IsSeries ? $"{hits.Count} series found. Select one to see its episodes." : $"{hits.Count} movie(s) found. Select one.";
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            if (version == _version) Status = "Search failed: " + ex.Message;
        }
        finally
        {
            if (version == _version) IsBusy = false;
        }
    }

    private string Label(SearchResult r)
    {
        var title = r.Titles.Resolve(Language, MissingTranslationBehavior.UseEnglish) ?? r.Ref.Id;
        return r.Year is { } y ? $"{title} ({y})" : title;
    }

    private async Task LoadCurrentSeriesAsync()
    {
        if (_current.Series is not { } s || _provider.Provider is not IEpisodeProvider ep) return;
        if (!string.Equals(s.Source.ProviderId, _provider.Provider.ProviderId, StringComparison.OrdinalIgnoreCase)) return;
        await LoadSeriesAsync(ep, s.Source, _current.Series, _current.Episode);
    }

    private async Task LoadResultAsync(ResultItem item)
    {
        var version = ++_version;
        IsBusy = true;
        Status = "Loading...";
        try
        {
            if (_provider.Provider is IEpisodeProvider ep) await LoadSeriesAsync(ep, item.Result.Ref, null, null);
            else if (_provider.Provider is IMovieProvider mp)
            {
                var movie = await mp.GetMovieAsync(item.Result.Ref, Language, _services.Settings.GetConfig(_provider.Provider), CancellationToken.None);
                if (version != _version) return;
                _movie = movie;
                OnPropertyChanged(nameof(SelectedName));
                OnPropertyChanged(nameof(CanConfirm));
                Status = "Movie selected. Press OK.";
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            if (version == _version) Status = "Loading failed: " + ex.Message;
        }
        finally
        {
            if (version == _version) IsBusy = false;
        }
    }

    private async Task LoadSeriesAsync(IEpisodeProvider ep, ProviderRef id, Series? knownSeries, Episode? currentEpisode)
    {
        var version = ++_version;
        IsBusy = true;
        try
        {
            var config = _services.Settings.GetConfig(_provider.Provider);
            var series = knownSeries ?? await ep.GetSeriesAsync(id, Language, config, CancellationToken.None);
            var episodes = await ep.GetEpisodesAsync(id, LanguageTag.WithEnglish(Language), config, CancellationToken.None);
            if (version != _version) return;

            _series = series;
            AllEpisodes = episodes;
            Episodes.Clear();
            foreach (var e in BindingInspectorViewModel.Sorted(episodes))
                Episodes.Add(new EpisodeItem(e, BindingInspectorViewModel.FormatEpisode(e, Language)));

            // Same series: keep the current episode. New series: try the episode number found in the file name.
            SelectedEpisode = currentEpisode is not null
                ? Episodes.FirstOrDefault(i => SameEpisode(i.Episode, currentEpisode))
                : FromOriginalNumbering();
            OnPropertyChanged(nameof(SelectedName));
            OnPropertyChanged(nameof(CanConfirm));
            Status = episodes.Count == 0 ? "This series has no episodes." : $"{episodes.Count} episode(s). Select the right one.";
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            if (version == _version) Status = "Loading failed: " + ex.Message;
        }
        finally
        {
            if (version == _version) IsBusy = false;
        }
    }

    private static bool SameEpisode(Episode a, Episode b) =>
        a.Source.Id == b.Source.Id && a.Number == b.Number;

    private EpisodeItem? FromOriginalNumbering()
    {
        if (_current.OriginalNumbering is not { Episode: { } n } orig) return null;
        var season = orig.Season ?? 1;
        return Episodes.FirstOrDefault(i => i.Episode.Number.Season == season && i.Episode.Number.Episode == n);
    }
}
