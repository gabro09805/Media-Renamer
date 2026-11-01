using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MediaRenamer.App.Services;
using MediaRenamer.Model;
using MediaRenamer.Plugin.Abstractions;
using MediaRenamer.PluginHost;

namespace MediaRenamer.App.ViewModels;

public sealed partial class EpisodeNode : ObservableObject
{
    private readonly SeasonNode _season;

    public EpisodeNode(SeasonNode season, Episode episode, string language)
    {
        _season = season;
        Episode = episode;
        Label = BindingInspectorViewModel.FormatEpisode(episode, language);
        Aired = episode.AirDate?.ToString("yyyy-MM-dd") ?? "";
        Description = episode.Overview ?? "";
    }

    public Episode Episode { get; }
    public string Label { get; }
    public string Aired { get; }
    public string Description { get; }

    [ObservableProperty] private bool _isSelected;

    partial void OnIsSelectedChanged(bool value) => _season.EpisodeSelectionChanged();
}

public sealed partial class SeasonNode : ObservableObject
{
    private bool _updating;

    public SeasonNode(int? season, string header) { Season = season; Header = header; }

    public int? Season { get; }
    public string Header { get; }
    public ObservableCollection<EpisodeNode> Episodes { get; } = new();

    [ObservableProperty] private bool _isExpanded;

    /// <summary>Whole-season checkbox: checked when every episode is selected.</summary>
    [ObservableProperty] private bool _isSelected;

    partial void OnIsSelectedChanged(bool value)
    {
        if (_updating) return;
        _updating = true;
        foreach (var e in Episodes) e.IsSelected = value;
        _updating = false;
    }

    internal void EpisodeSelectionChanged()
    {
        if (_updating) return;
        _updating = true;
        IsSelected = Episodes.Count > 0 && Episodes.All(e => e.IsSelected);
        _updating = false;
    }
}

/// <summary>
/// Search Episodes (spec §8): search a series in a provider chosen by the user, browse seasons and episodes in the provider's order,
/// select episodes and send them to Rename.
/// </summary>
public sealed partial class SearchEpisodesViewModel : ViewModelBase
{
    private readonly AppServices _services;
    private readonly RenameViewModel _rename;
    private Series? _series;
    private int _version;

    public SearchEpisodesViewModel(AppServices services, RenameViewModel rename)
    {
        _services = services;
        _rename = rename;
        services.Registry.Changed += RefreshProviders;
        RefreshProviders();
    }

    public string Header => "Search Episodes";

    public ObservableCollection<ProviderEntry> Providers { get; } = new();
    public ObservableCollection<ResultItem> Results { get; } = new();
    public ObservableCollection<SeasonNode> Seasons { get; } = new();

    [ObservableProperty] private ProviderEntry? _selectedProvider;
    [ObservableProperty] private string _query = "";
    [ObservableProperty] private ResultItem? _selectedResult;
    [ObservableProperty] private string _status = "Choose a provider, type a series name and press Search.";
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private string _seriesName = "";
    [ObservableProperty] private int _selectedCount;

    /// <summary>Raised after a successful transfer so the shell can show Rename.</summary>
    public event Action? SentToRename;

    private string Language => _services.MatchPreferences.Current.OutputLanguage;

    public Series? CurrentSeries => _series;

    private void RefreshProviders()
    {
        var keep = SelectedProvider;
        Providers.Clear();
        foreach (var p in _services.Registry.Enabled(ProviderMode.Episode)) Providers.Add(p);
        SelectedProvider = Providers.FirstOrDefault(p => p == keep) ?? Providers.FirstOrDefault();
    }

    /// <summary>Initial value of the search box: the title detected in the first file loaded in Rename.</summary>
    public void SuggestQuery()
    {
        if (string.IsNullOrWhiteSpace(Query) && _rename.SuggestedTitle() is { } title) Query = title;
    }

    partial void OnSelectedProviderChanged(ProviderEntry? value)
    {
        _version++;
        Results.Clear();
        SelectedResult = null;
        ClearSeries();
    }

    partial void OnSelectedResultChanged(ResultItem? value)
    {
        if (value is not null) _ = LoadSeriesAsync(value);
    }

    private void ClearSeries()
    {
        _series = null;
        Seasons.Clear();
        SeriesName = "";
        SelectedCount = 0;
    }

    [RelayCommand]
    public async Task SearchAsync()
    {
        if (SelectedProvider?.Provider is not IEpisodeProvider provider || string.IsNullOrWhiteSpace(Query)) return;
        var version = ++_version;
        Results.Clear();
        SelectedResult = null;
        ClearSeries();
        IsBusy = true;
        Status = "Searching...";
        try
        {
            var hits = await provider.SearchSeriesAsync(new SearchQuery(Query.Trim(), null, Language),
                _services.Settings.GetConfig(provider), CancellationToken.None);
            if (version != _version) return;
            foreach (var h in hits) Results.Add(new ResultItem(h, Label(h), loadThumbnail: true));
            Status = hits.Count == 0 ? "No results." : $"{hits.Count} result(s). Select the correct series.";
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
        var title = r.Titles.Resolve(Language, _services.MatchPreferences.Current.MissingTranslation) ?? r.Ref.Id;
        var original = r.Titles.All.FirstOrDefault(t => t.Kind == TitleKind.Original)?.Value;
        if (!string.IsNullOrEmpty(original) && !string.Equals(original, title, StringComparison.OrdinalIgnoreCase)) title += $" [{original}]";
        return title;
    }

    private async Task LoadSeriesAsync(ResultItem item)
    {
        if (SelectedProvider?.Provider is not IEpisodeProvider provider) return;
        var version = ++_version;
        ClearSeries();
        IsBusy = true;
        Status = "Loading episodes...";
        try
        {
            var config = _services.Settings.GetConfig(provider);
            var series = await provider.GetSeriesAsync(item.Result.Ref, Language, config, CancellationToken.None);
            var episodes = await provider.GetEpisodesAsync(item.Result.Ref, LanguageTag.WithEnglish(Language), config, CancellationToken.None);
            if (version != _version) return;
            _series = series;
            SeriesName = series.Titles.Resolve(Language, _services.MatchPreferences.Current.MissingTranslation) ?? item.Label;
            BuildSeasons(episodes);
            Status = $"{episodes.Count} episode(s) in {Seasons.Count} season(s).";
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

    /// <summary>Seasons appear in the provider's order of first appearance, and so do their episodes: nothing is re-sorted.</summary>
    private void BuildSeasons(IReadOnlyList<Episode> episodes)
    {
        Seasons.Clear();
        foreach (var group in episodes.GroupBy(e => e.Number.Season))
        {
            var header = group.Key is null ? "Episodes" : group.Key == 0 ? "Specials" : $"Season {group.Key}";
            var season = new SeasonNode(group.Key, $"{header} ({group.Count()})");
            foreach (var e in group)
            {
                var node = new EpisodeNode(season, e, Language);
                node.PropertyChanged += (_, args) => { if (args.PropertyName == nameof(EpisodeNode.IsSelected)) UpdateCount(); };
                season.Episodes.Add(node);
            }
            season.PropertyChanged += (_, args) => { if (args.PropertyName == nameof(SeasonNode.IsSelected)) UpdateCount(); };
            Seasons.Add(season);
        }
        if (Seasons.Count == 1) Seasons[0].IsExpanded = true;
        UpdateCount();
    }

    private void UpdateCount() => SelectedCount = SelectedEpisodes().Count;

    /// <summary>Selected episodes in the order displayed.</summary>
    public IReadOnlyList<Episode> SelectedEpisodes() =>
        Seasons.SelectMany(s => s.Episodes).Where(e => e.IsSelected).Select(e => e.Episode).ToList();

    [RelayCommand]
    private void SelectAll() { foreach (var s in Seasons) s.IsSelected = true; }

    [RelayCommand]
    private void DeselectAll() { foreach (var s in Seasons) s.IsSelected = false; }

    [RelayCommand]
    private void ExpandAll() { foreach (var s in Seasons) s.IsExpanded = true; }

    [RelayCommand]
    private void CollapseAll() { foreach (var s in Seasons) s.IsExpanded = false; }

    /// <summary>Dialog model for choosing how files are paired; null (with a status message) when sending is not possible.</summary>
    public SendToRenameViewModel? CreateSendDialog()
    {
        if (_series is null || SelectedProvider is null) { Status = "Select a series first."; return null; }
        var selected = SelectedEpisodes();
        if (selected.Count == 0) { Status = "Select at least one episode."; return null; }
        var files = _rename.VideosInOrder();
        if (files.Count == 0) { Status = "Load some video files in Rename first."; return null; }
        return new SendToRenameViewModel(files, _series, selected, _services.MatchPreferences.Current);
    }

    public void Send(SendToRenameViewModel dialog)
    {
        if (SelectedProvider is null || !dialog.CanSend) return;
        _rename.ApplyAssociation(SelectedProvider, dialog.Plan.Matches);
        Status = "Episodes sent to Rename.";
        SentToRename?.Invoke();
    }
}
