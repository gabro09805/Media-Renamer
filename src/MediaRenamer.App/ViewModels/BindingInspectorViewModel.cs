using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MediaRenamer.App.Services;
using MediaRenamer.Bindings.Catalog;
using MediaRenamer.Model;
using MediaRenamer.Plugin.Abstractions;
using MediaRenamer.PluginHost;

namespace MediaRenamer.App.ViewModels;

/// <summary>A search hit. <see cref="Thumbnail"/> is filled asynchronously when the provider supplies a poster.</summary>
public sealed partial class ResultItem : ObservableObject
{
    public ResultItem(SearchResult result, string label, bool loadThumbnail = false)
    {
        Result = result;
        Label = label;
        if (loadThumbnail && result.PosterUri is { } uri) _ = LoadThumbnailAsync(uri);
    }

    public SearchResult Result { get; }
    public string Label { get; }

    [ObservableProperty] private Avalonia.Media.Imaging.Bitmap? _thumbnail;

    private async Task LoadThumbnailAsync(Uri uri) => Thumbnail = await CoverCache.GetAsync(uri);

    public Uri? PosterUri => Result.PosterUri;
    public string Subtitle => string.Join(" · ", new[] { Result.Kind, Result.Year?.ToString() }.Where(s => !string.IsNullOrEmpty(s)));
}

public sealed record EpisodeItem(Episode Episode, string Label);

/// <summary>
/// Shows the value of every binding for a real match: the user picks a provider, searches by name, then (for series) picks an episode.
/// Nothing is fetched until the user asks for it.
/// </summary>
public sealed partial class BindingInspectorViewModel : ViewModelBase, IDisposable
{
    private readonly AppServices _services;
    private IReadOnlyList<BindingRow> _allRows = Array.Empty<BindingRow>();
    private Series? _series;
    private Movie? _movie;
    private int _version;
    private Task _loading = Task.CompletedTask;

    public BindingInspectorViewModel(AppServices services, FormatKind kind = FormatKind.Series)
    {
        _services = services;
        Kind = kind;
        Title = kind switch { FormatKind.Movie => "Binding values - Movies", _ => "Binding values - TV Series" };
        var mode = kind == FormatKind.Movie ? ProviderMode.Movie : ProviderMode.Episode;
        foreach (var p in services.Registry.Enabled(mode)) Providers.Add(p);
        SelectedProvider = Providers.FirstOrDefault();
        services.MatchPreferences.Changed += OnPreferencesChanged;
    }

    public FormatKind Kind { get; }
    public string Title { get; }

    public ObservableCollection<ProviderEntry> Providers { get; } = new();
    public ObservableCollection<ResultItem> Results { get; } = new();
    public ObservableCollection<EpisodeItem> Episodes { get; } = new();

    [ObservableProperty] private ProviderEntry? _selectedProvider;
    [ObservableProperty] private string _query = "";
    [ObservableProperty] private ResultItem? _selectedResult;
    [ObservableProperty] private EpisodeItem? _selectedEpisode;
    [ObservableProperty] private IReadOnlyList<BindingRow> _rows = Array.Empty<BindingRow>();
    [ObservableProperty] private string _filter = "";
    [ObservableProperty] private string _status = "Choose a provider, type a name and press Search.";
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private bool _isSeries;

    /// <summary>Completes when the last result selection has finished loading (used by tests).</summary>
    public Task Loading => _loading;

    private string Language => _services.MatchPreferences.Current.OutputLanguage;

    /// <summary>The series or movie confirmed in the picker window; only then are episodes loaded.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ChosenName))]
    private ResultItem? _chosenResult;

    public string ChosenName => ChosenResult?.Label ?? (Kind == FormatKind.Movie ? "No movie selected" : "No series selected");

    public string PickButtonText => Kind == FormatKind.Movie ? "Select movie..." : "Select series...";

    partial void OnSelectedProviderChanged(ProviderEntry? value)
    {
        Results.Clear();
        SelectedResult = null;
    }

    /// <summary>Called when the picker is confirmed.</summary>
    public void ConfirmSelection()
    {
        if (SelectedResult is null) return;
        ChosenResult = SelectedResult;
        _loading = LoadResultAsync(SelectedResult);
    }

    /// <summary>Called when the picker is dismissed: the picker goes back to what is currently chosen.</summary>
    public void CancelSelection() => SelectedResult = ChosenResult is not null && Results.Contains(ChosenResult) ? ChosenResult : null;

    partial void OnSelectedEpisodeChanged(EpisodeItem? value) => Refresh();

    partial void OnFilterChanged(string value) => ApplyFilter();

    /// <summary>Where the filter text is searched.</summary>
    public IReadOnlyList<string> FilterScopes { get; } = new[] { "Bindings and values", "Bindings only", "Values only" };

    [ObservableProperty]
    private string _filterScope = "Bindings and values";

    partial void OnFilterScopeChanged(string value) => ApplyFilter();

    private void ClearSelection()
    {
        _version++;
        _series = null;
        _movie = null;
        Episodes.Clear();
        IsSeries = false;
        _allRows = Array.Empty<BindingRow>();
        ApplyFilter();
    }

    [RelayCommand]
    public async Task SearchAsync()
    {
        if (SelectedProvider is null || string.IsNullOrWhiteSpace(Query)) return;
        var entry = SelectedProvider;
        var version = ++_version;
        Results.Clear();
        SelectedResult = null;
        IsBusy = true;
        Status = "Searching...";
        try
        {
            var config = _services.Settings.GetConfig(entry.Provider);
            var query = new SearchQuery(Query.Trim(), null, Language);
            var hits = entry.Provider switch
            {
                IEpisodeProvider p => await p.SearchSeriesAsync(query, config, CancellationToken.None),
                IMovieProvider p => await p.SearchMovieAsync(query, config, CancellationToken.None),
                _ => Array.Empty<SearchResult>(),
            };
            if (version != _version) return;
            foreach (var h in hits) Results.Add(new ResultItem(h, Label(h)));
            Status = hits.Count == 0 ? "No results." : $"{hits.Count} result(s). Select one.";
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

    /// <summary>Season and episode ascending; specials (season 0) go last, also by episode.</summary>
    public static IEnumerable<Episode> Sorted(IEnumerable<Episode> episodes) =>
        episodes.OrderBy(e => (e.Number.Season ?? 0) == 0 ? 1 : 0)
            .ThenBy(e => e.Number.Season ?? int.MaxValue)
            .ThenBy(e => e.Number.Episode ?? e.Number.Special ?? int.MaxValue);

    private string Label(SearchResult r)
    {
        var title = r.Titles.Resolve(Language, MissingTranslationBehavior.UseEnglish) ?? r.Ref.Id;
        return r.Year is { } y ? $"{title} ({y})" : title;
    }

    /// <summary>Search hits may lack a cover (AniDB's title dump has none); the detail call (cached, rate-limited) does.</summary>
    public async Task<Uri?> ResolvePosterAsync(ResultItem item)
    {
        if (item.PosterUri is not null) return item.PosterUri;
        if (SelectedProvider is not { } entry || entry.Provider is not IEpisodeProvider ep) return null;
        try
        {
            var series = await ep.GetSeriesAsync(item.Result.Ref, Language, _services.Settings.GetConfig(entry.Provider), CancellationToken.None);
            return series.PosterUri;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private async Task LoadResultAsync(ResultItem? item)
    {
        ClearSelection();
        if (item is null || SelectedProvider is null) return;
        var entry = SelectedProvider;
        var version = _version;
        IsBusy = true;
        Status = "Loading details...";
        try
        {
            var config = _services.Settings.GetConfig(entry.Provider);
            if (entry.Provider is IEpisodeProvider ep)
            {
                var series = await ep.GetSeriesAsync(item.Result.Ref, Language, config, CancellationToken.None);
                var episodes = await ep.GetEpisodesAsync(item.Result.Ref, LanguageTag.WithEnglish(Language), config, CancellationToken.None);
                if (version != _version) return;
                _series = series;
                IsSeries = true;
                foreach (var e in Sorted(episodes)) Episodes.Add(new EpisodeItem(e, EpisodeLabel(e)));
                Status = episodes.Count == 0 ? "This series has no episodes." : $"{episodes.Count} episode(s). Select one.";
            }
            else if (entry.Provider is IMovieProvider mp)
            {
                var movie = await mp.GetMovieAsync(item.Result.Ref, Language, config, CancellationToken.None);
                if (version != _version) return;
                _movie = movie;
                Status = "Movie loaded.";
                Refresh();
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

    private string EpisodeLabel(Episode e) => FormatEpisode(e, Language);

    public static string FormatEpisode(Episode e, string language)
    {
        var n = e.Number;
        var number = n.Season is { } s && n.Episode is { } ep ? $"{s}x{ep:00}" : n.Episode is { } only ? $"E{only:00}" : "?";
        var title = e.Titles.Resolve(language, MissingTranslationBehavior.UseEnglish);
        return string.IsNullOrEmpty(title) ? number : $"{number} - {title}";
    }

    private void Refresh()
    {
        var prefs = _services.MatchPreferences.Current;
        MediaBindingSource? source = null;
        if (_movie is not null) source = new MediaBindingSource(null, _movie, null, null, prefs);
        else if (_series is not null && SelectedEpisode is { } e) source = new MediaBindingSource(null, null, _series, e.Episode, prefs);

        _allRows = source is null ? Array.Empty<BindingRow>() : BindingExplorer.Rows(source);
        ApplyFilter();
    }

    private void ApplyFilter()
    {
        var f = Filter.Trim();
        Rows = f.Length == 0
            ? _allRows
            : _allRows.Where(r => FilterScope switch
            {
                "Bindings only" => r.Name.Contains(f, StringComparison.OrdinalIgnoreCase),
                "Values only" => r.Value.Contains(f, StringComparison.OrdinalIgnoreCase),
                _ => r.Name.Contains(f, StringComparison.OrdinalIgnoreCase)
                     || r.Value.Contains(f, StringComparison.OrdinalIgnoreCase)
                     || r.Category.Contains(f, StringComparison.OrdinalIgnoreCase),
            }).ToList();
    }

    private void OnPreferencesChanged(MatchPreferences _) => Refresh();

    public void Dispose() => _services.MatchPreferences.Changed -= OnPreferencesChanged;
}
