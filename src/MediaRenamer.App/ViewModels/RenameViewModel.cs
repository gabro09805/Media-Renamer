using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MediaRenamer.App.Services;
using MediaRenamer.Core.Import;
using MediaRenamer.Core.Matching;
using MediaRenamer.Core.Renaming;
using MediaRenamer.Model;
using MediaRenamer.Plugin.Abstractions;
using MediaRenamer.PluginHost;

namespace MediaRenamer.App.ViewModels;

public sealed partial class RenameViewModel : ViewModelBase
{
    private readonly AppServices _services;
    private readonly List<string> _files = new();
    private readonly MatchService _matcher = new();
    private readonly RenamePlanner _planner;
    private readonly RenameExecutor _executor = new();
    private ImportResult _import = new(Array.Empty<MediaFile>(), Array.Empty<SubtitleFile>());
    private IReadOnlyList<MatchResult> _matches = Array.Empty<MatchResult>();
    private ProviderEntry? _movieProvider;
    private ProviderEntry? _episodeProvider;
    private string? _lastJournal;

    public RenameViewModel(AppServices services)
    {
        _services = services;
        _planner = new RenamePlanner(services.Renderer);
        services.Registry.Changed += Refresh;
        Refresh();
    }

    public string Header => "Rename";

    public ObservableCollection<FileRowViewModel> Rows { get; } = new();
    public ObservableCollection<ProviderEntry> EpisodeProviders { get; } = new();
    public ObservableCollection<ProviderEntry> MovieProviders { get; } = new();

    public ProviderEntry? MovieProvider => _movieProvider;
    public ProviderEntry? EpisodeProvider => _episodeProvider;

    /// <summary>The provider chosen last from Fetch Data. Never switched automatically.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ActiveProviderText))]
    private ProviderEntry? _activeProvider;

    [ObservableProperty]
    private string _status = "Drop files or folders here, or press Load.";

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private bool _canUndo;

    public string ActiveProviderText => ActiveProvider is null ? "No provider selected" : "Provider: " + ActiveProvider.Label;

    public int ReadyCount => Rows.Count(r => r.State == PlanState.Ready);

    /// <summary>Raised when the Fetch Data menu has to be rebuilt.</summary>
    public event Action? ProvidersChanged;

    [RelayCommand]
    public void SelectProvider(ProviderEntry entry)
    {
        if (entry.Provider.Mode == ProviderMode.Movie) _movieProvider = entry; else _episodeProvider = entry;
        ActiveProvider = entry;
        ProvidersChanged?.Invoke();
    }

    /// <summary>Set by the view: shows the candidate dialog on the UI thread. Without it ambiguous matches are decided automatically.</summary>
    public Func<ChooseMatchViewModel, Task<SearchResult?>>? CandidateChooser { get; set; }

    private async Task<SearchResult?> ChooseAsync(string title, IReadOnlyList<string> files, IReadOnlyList<SearchResult> candidates, CancellationToken ct)
    {
        var language = CurrentPreferences.OutputLanguage;
        var items = candidates.Select(c => new ResultItem(c,
            (c.Titles.Resolve(language, MissingTranslationBehavior.UseEnglish) ?? c.Ref.Id) + (c.Year is { } y ? $" ({y})" : ""), loadThumbnail: true));
        return await CandidateChooser!(new ChooseMatchViewModel(title, files, items));
    }

    public MatchPreferences CurrentPreferences => _services.MatchPreferences.Current;

    /// <summary>A new output language re-reads titles of the matched items (no new matching); other changes only replan.</summary>
    public async Task SavePreferencesAsync(MatchPreferences value)
    {
        var languageChanged = !string.Equals(CurrentPreferences.OutputLanguage, value.OutputLanguage, StringComparison.OrdinalIgnoreCase);
        _services.MatchPreferences.Save(value);
        if (_matches.Count == 0) return;
        if (languageChanged)
        {
            IsBusy = true;
            Status = "Updating titles...";
            try
            {
                _matches = await _matcher.RelocalizeAsync(_matches, _movieProvider?.Provider as IMovieProvider, _episodeProvider?.Provider as IEpisodeProvider,
                    _movieProvider is null ? new EmptyConfig() : _services.Settings.GetConfig(_movieProvider.Provider),
                    _episodeProvider is null ? new EmptyConfig() : _services.Settings.GetConfig(_episodeProvider.Provider), value);
            }
            finally
            {
                IsBusy = false;
            }
        }
        Replan();
    }

    /// <summary>Video files in the order shown in Original Files (the order used by Search Order).</summary>
    public IReadOnlyList<MediaFile> VideosInOrder()
    {
        var byPath = _import.Videos.ToDictionary(v => v.Path, StringComparer.OrdinalIgnoreCase);
        return Rows.Where(r => !r.IsSubtitle && byPath.ContainsKey(r.Path)).Select(r => byPath[r.Path]).ToList();
    }

    /// <summary>Title detected in the first video: the initial value of the Search Episodes box.</summary>
    public string? SuggestedTitle() => VideosInOrder().Select(v => v.Detected.Title).FirstOrDefault(t => !string.IsNullOrWhiteSpace(t));

    /// <summary>Receives the pairing confirmed in Search Episodes; the provider becomes the active Episode Mode provider.</summary>
    public void ApplyAssociation(ProviderEntry provider, IReadOnlyList<MatchResult> matches)
    {
        SelectProvider(provider);
        _matches = matches;
        Replan();
    }
    public FormatEditorViewModel CreateFormatEditor() => new(_services);

    public bool IsSelected(ProviderEntry entry) => entry == _movieProvider || entry == _episodeProvider;

    public void AddPaths(IEnumerable<string> paths)
    {
        var batch = new List<string>();
        foreach (var f in FileImporter.Expand(paths))
        {
            if (_files.Contains(f, StringComparer.OrdinalIgnoreCase)) continue;
            var ext = Path.GetExtension(f).TrimStart('.');
            if (!FileImporter.VideoExtensions.Contains(ext, StringComparer.OrdinalIgnoreCase) && !Core.Subtitles.SubtitleDetector.IsSubtitle(f)) continue;
            if (!batch.Contains(f, StringComparer.OrdinalIgnoreCase)) batch.Add(f);
        }
        batch.Sort(StringComparer.OrdinalIgnoreCase);
        _files.AddRange(batch);
        var added = batch.Count;
        Status = added == 0 ? "No new video or subtitle files found." : $"{added} file(s) added.";
        _matches = Array.Empty<MatchResult>();
        Rebuild();
    }

    public void RemoveRows(IEnumerable<FileRowViewModel> rows)
    {
        foreach (var r in rows.ToList())
            _files.RemoveAll(f => string.Equals(f, r.Path, StringComparison.OrdinalIgnoreCase));
        // Matches of the remaining files are kept, so the other rows stay aligned with their new names.
        _matches = _matches.Where(m => _files.Contains(m.File.Path, StringComparer.OrdinalIgnoreCase)).ToList();
        Rebuild();
        if (_matches.Count > 0) Replan();
    }

    /// <summary>Primary rows (videos, then standalone subtitles); linked subtitles follow their video and never move on their own.</summary>
    private List<FileRowViewModel> PrimaryRows(bool subtitles)
    {
        var linked = _import.Subtitles.Where(s => s.LinkedVideoPath is not null).Select(s => s.Path).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return Rows.Where(r => !linked.Contains(r.Path) && r.IsSubtitle == subtitles).ToList();
    }

    private static List<int> SelectedIndices(List<FileRowViewModel> primaries, IEnumerable<FileRowViewModel> rows)
    {
        var paths = rows.Select(r => r.Path).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return primaries.Select((r, i) => (r, i)).Where(x => paths.Contains(x.r.Path)).Select(x => x.i).ToList();
    }

    /// <summary>Moves original files (with the match found on their row). Returns the paths to reselect.</summary>
    public IReadOnlyList<string> MoveOriginals(IEnumerable<FileRowViewModel> rows, int delta)
    {
        var selected = rows.ToList();
        if (selected.Count == 0) return Array.Empty<string>();
        var subtitles = selected[0].IsSubtitle;
        var primaries = PrimaryRows(subtitles);
        var order = primaries.Select(r => r.Path).ToList();
        var moved = MatchReordering.MoveBlock(order, SelectedIndices(primaries, selected), delta);
        var kept = moved.Select(i => order[i]).ToList();
        var others = _files.Where(f => !order.Contains(f, StringComparer.OrdinalIgnoreCase)).ToList();
        var videosFirst = subtitles ? others.Concat(order) : order.Concat(others);
        _files.Clear();
        _files.AddRange(videosFirst);
        Rebuild();
        if (_matches.Count > 0) Replan();
        return kept;
    }

    /// <summary>
    /// Moves only the new names: the matches shift among the files, the original files stay where they are.
    /// Rows that no longer agree with their original are highlighted, but can still be renamed.
    /// </summary>
    public IReadOnlyList<string> MoveNewNames(IEnumerable<FileRowViewModel> rows, int delta)
    {
        var selected = rows.ToList();
        if (selected.Count == 0) return Array.Empty<string>();
        if (_matches.Count == 0) { Status = "Run Match first: there are no new names to move."; return Array.Empty<string>(); }
        var subtitles = selected[0].IsSubtitle;
        var primaries = PrimaryRows(subtitles);
        var byFile = _import.Videos.Concat<MediaFile>(_import.Subtitles).ToDictionary(f => f.Path, StringComparer.OrdinalIgnoreCase);
        var byMatch = _matches.ToDictionary(m => m.File.Path, StringComparer.OrdinalIgnoreCase);
        var files = primaries.Where(r => byFile.ContainsKey(r.Path) && byMatch.ContainsKey(r.Path)).ToList();
        var slots = files.Select(r => byMatch[r.Path]).ToList();
        var moved = MatchReordering.MoveBlock(slots, SelectedIndices(files, selected), delta);
        var reassigned = MatchReordering.Reassign(files.Select(r => byFile[r.Path]).ToList(), slots);
        var changed = files.Select(r => r.Path).ToHashSet(StringComparer.OrdinalIgnoreCase);
        _matches = _matches.Where(m => !changed.Contains(m.File.Path)).Concat(reassigned).ToList();
        Replan();
        return moved.Select(i => files[i].Path).ToList();
    }
    [RelayCommand]
    private void Reload()
    {
        _files.RemoveAll(f => !File.Exists(f));
        _matches = Array.Empty<MatchResult>();
        Rebuild();
    }

    [RelayCommand]
    private void Clear()
    {
        _files.Clear();
        _manualLinks.Clear();
        _matches = Array.Empty<MatchResult>();
        Rebuild();
        Status = "List cleared.";
    }

    [RelayCommand]
    private async Task MatchAsync()
    {
        // Subtitles with no video to follow are matched on their own name.
        var inputs = _import.Videos.Concat<MediaFile>(_import.Subtitles.Where(s => s.LinkedVideoPath is null)).ToList();
        if (inputs.Count == 0) { Status = "Nothing to match: load some files first."; return; }
        if (ActiveProvider is null) { Status = "Choose a provider from Fetch Data first (Movie or Episode mode)."; return; }
        IsBusy = true;
        Status = "Matching...";
        try
        {
            var movie = _movieProvider?.Provider as IMovieProvider;
            var episode = _episodeProvider?.Provider as IEpisodeProvider;
            _matches = await _matcher.MatchAsync(inputs, ActiveProvider.Provider.Mode, movie, episode,
                movie is null ? new EmptyConfig() : _services.Settings.GetConfig(_movieProvider!.Provider),
                episode is null ? new EmptyConfig() : _services.Settings.GetConfig(_episodeProvider!.Provider),
                CurrentPreferences, default, CandidateChooser is null ? null : ChooseAsync);
            Replan();
        }
        catch (Exception ex)
        {
            Status = "Match failed: " + ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>Dialog model for fixing the match of one row; null (with a status message) when it cannot be edited.</summary>
    public EditMatchViewModel? CreateEditMatch(FileRowViewModel? row)
    {
        if (row is null) { Status = "Select a file first."; return null; }
        if (_matches.Count == 0) { Status = "Run Match first, then correct the result."; return null; }
        if (ActiveProvider is null) { Status = "Choose a provider from Fetch Data first."; return null; }
        var match = _matches.FirstOrDefault(m => string.Equals(m.File.Path, row.Path, StringComparison.OrdinalIgnoreCase));
        if (match is null) { Status = "This file is not part of the current match: select a video or an unassociated subtitle."; return null; }
        return new EditMatchViewModel(_services, ActiveProvider, match);
    }

    /// <summary>
    /// Applies the result of the Edit dialog. "Apply to all" re-associates every file of the same series/movie
    /// (series: episode by episode, never the same episode for all); otherwise only the edited file changes.
    /// </summary>
    public void ApplyEdit(EditMatchViewModel scope, MatchResult manual)
    {
        if (scope.ApplyToAll && manual.Series is { } series && scope.AllEpisodes.Count > 0)
            _matches = MatchService.ReapplySeries(_matches, scope.Current, manual, series, scope.AllEpisodes, CurrentPreferences);
        else if (scope.ApplyToAll && manual.Movie is { } movie)
            _matches = MatchService.ReapplyMovie(_matches, scope.Current, manual, movie);
        else
        {
            ApplyManualMatch(manual);
            return;
        }
        Replan();
    }

    /// <summary>Next file (after <paramref name="afterPath"/>) still without a series/movie/episode, for the guided correction.</summary>
    public FileRowViewModel? NextUnidentified(string afterPath)
    {
        var start = Rows.ToList().FindIndex(r => string.Equals(r.Path, afterPath, StringComparison.OrdinalIgnoreCase));
        var isEpisode = ActiveProvider?.Provider.Mode == ProviderMode.Episode;
        bool Open(FileRowViewModel r) =>
            _matches.FirstOrDefault(m => string.Equals(m.File.Path, r.Path, StringComparison.OrdinalIgnoreCase)) is { } m
            && m.Movie is null && (m.Series is null || (isEpisode && m.Episode is null));
        var ordered = Rows.Skip(start + 1).Concat(Rows.Take(Math.Max(start, 0)));
        return ordered.FirstOrDefault(Open);
    }
    public void ApplyManualMatch(MatchResult manual)
    {
        _matches = _matches.Select(m => string.Equals(m.File.Path, manual.File.Path, StringComparison.OrdinalIgnoreCase) ? manual : m).ToList();
        Replan();
    }

    /// <summary>Recomputes new names from the current matches (after a format or preference change).</summary>
    public void Replan()
    {
        if (_matches.Count == 0) { foreach (var r in Rows) r.Clear(); OnPropertyChanged(nameof(ReadyCount)); return; }
        var plan = BuildPlan();
        var byPath = Rows.ToDictionary(r => r.Path, StringComparer.OrdinalIgnoreCase);
        foreach (var item in plan)
            if (byPath.TryGetValue(item.Source, out var row))
            {
                row.DestinationRoot = string.IsNullOrWhiteSpace(_services.AppSettings.LibraryRoot) ? null : _services.AppSettings.LibraryRoot;
                row.Apply(item);
                row.Warning = item.Match is null ? null : MatchConsistency.Check(item.Match);
            }
        OnPropertyChanged(nameof(ReadyCount));
        var issues = Rows.Count(r => r.HasIssue);
        Status = $"{ReadyCount} ready to rename" + (issues > 0 ? $", {issues} need attention (see tooltips)." : ".");
    }

    public IReadOnlyList<PlanItem> BuildPlan() =>
        _planner.Plan(_matches, _import.Subtitles, CurrentPreferences,
            new RenameOptions(
                _services.Formats.Selected(FormatKind.Movie).Expression,
                _services.Formats.Selected(FormatKind.Series).Expression,
                string.IsNullOrWhiteSpace(_services.AppSettings.LibraryRoot) ? null : _services.AppSettings.LibraryRoot));

    /// <summary>The lines shown in the confirmation dialog: only items that will really be renamed.</summary>
    public IReadOnlyList<PlanItem> PendingRenames() => BuildPlan().Where(p => p.CanRename).ToList();

    public RenameOutcome ApplyRename(IReadOnlyList<PlanItem> plan)
    {
        var journalDir = Path.Combine(_services.Paths.Data, "journals");
        Directory.CreateDirectory(journalDir);
        var journal = Path.Combine(journalDir, $"rename-{DateTime.Now:yyyyMMdd-HHmmss}.json");
        var outcome = _executor.Execute(plan, journal);

        var moved = plan.Where(p => p.CanRename && p.Target is not null && File.Exists(p.Target)).ToDictionary(p => Path.GetFullPath(p.Source), p => p.Target!, StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < _files.Count; i++)
            if (moved.TryGetValue(Path.GetFullPath(_files[i]), out var to)) _files[i] = to;

        _manualLinks.Clear();
        _lastJournal = outcome.JournalPath;
        CanUndo = _lastJournal is not null;
        _matches = Array.Empty<MatchResult>();
        Rebuild();
        Status = $"{outcome.Renamed} file(s) renamed" + (outcome.Failures.Count > 0 ? $", {outcome.Failures.Count} failed: {outcome.Failures[0].Error}" : ".");
        return outcome;
    }

    [RelayCommand]
    private void Undo()
    {
        if (_lastJournal is null || !File.Exists(_lastJournal)) { Status = "Nothing to undo."; return; }
        var entries = System.Text.Json.JsonSerializer.Deserialize<List<JournalEntry>>(File.ReadAllText(_lastJournal)) ?? new();
        var outcome = _executor.Undo(_lastJournal);
        var back = entries.ToDictionary(e => Path.GetFullPath(e.To), e => e.From, StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < _files.Count; i++)
            if (back.TryGetValue(Path.GetFullPath(_files[i]), out var from) && File.Exists(from)) _files[i] = from;
        _manualLinks.Clear();
        _lastJournal = null;
        CanUndo = false;
        _matches = Array.Empty<MatchResult>();
        Rebuild();
        Status = $"{outcome.Renamed} file(s) restored" + (outcome.Failures.Count > 0 ? $", {outcome.Failures.Count} could not be restored." : ".");
    }

    // Subtitle links chosen by hand (subtitle path -> video path, or null = match on its own); they survive re-imports.
    private readonly Dictionary<string, string?> _manualLinks = new(StringComparer.OrdinalIgnoreCase);

    public bool IsLinkedSubtitle(string path) =>
        _import.Subtitles.Any(s => s.LinkedVideoPath is not null && string.Equals(s.Path, path, StringComparison.OrdinalIgnoreCase));

    /// <summary>Video the subtitle currently follows, if any.</summary>
    public string? LinkedVideoOf(string subtitlePath) =>
        _import.Subtitles.FirstOrDefault(s => string.Equals(s.Path, subtitlePath, StringComparison.OrdinalIgnoreCase))?.LinkedVideoPath;

    /// <summary>Makes a subtitle follow the chosen video, or (null) be matched on its own name.</summary>
    public void LinkSubtitle(string subtitlePath, string? videoPath)
    {
        _manualLinks[subtitlePath] = videoPath;
        // A subtitle that now follows a video is no longer matched by itself.
        if (videoPath is not null)
            _matches = _matches.Where(m => !string.Equals(m.File.Path, subtitlePath, StringComparison.OrdinalIgnoreCase)).ToList();
        Rebuild();
        Replan();
        if (_matches.Count > 0 && videoPath is null) Status = "Subtitle unlinked: run Match to match it on its own name.";
        else if (videoPath is not null) Status = $"Subtitle linked to {Path.GetFileName(videoPath)}. " + Status;
    }

    private ImportResult ApplyManualLinks(ImportResult import)
    {
        if (_manualLinks.Count == 0) return import;
        var videos = import.Videos.Select(v => v.Path).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var subs = import.Subtitles.Select(s =>
            !_manualLinks.TryGetValue(s.Path, out var video) ? s
            : video is null ? s with { LinkedVideoPath = null, Link = LinkConfidence.None }
            : videos.Contains(video) ? s with { LinkedVideoPath = video, Link = LinkConfidence.High }
            : s).ToList();
        return import with { Subtitles = subs };
    }

    private void Rebuild()
    {
        var imported = ApplyManualLinks(FileImporter.Import(_files));
        var position = _files.Select((f, i) => (f, i)).ToDictionary(x => x.f, x => x.i, StringComparer.OrdinalIgnoreCase);
        _import = imported with
        {
            Videos = imported.Videos.OrderBy(v => position.GetValueOrDefault(v.Path, int.MaxValue)).ToList(),
            Subtitles = imported.Subtitles.OrderBy(s => position.GetValueOrDefault(s.Path, int.MaxValue)).ToList(),
        };
        var rows = new List<FileRowViewModel>();
        var placed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var v in _import.Videos)
        {
            rows.Add(new FileRowViewModel(v.Path, false));
            foreach (var s in _import.Subtitles.Where(s => string.Equals(s.LinkedVideoPath, v.Path, StringComparison.OrdinalIgnoreCase)))
            {
                rows.Add(new FileRowViewModel(s.Path, true));
                placed.Add(s.Path);
            }
        }
        foreach (var s in _import.Subtitles.Where(s => !placed.Contains(s.Path)))
            rows.Add(new FileRowViewModel(s.Path, true));

        Rows.Clear();
        foreach (var r in rows) Rows.Add(r);
        OnPropertyChanged(nameof(ReadyCount));
    }

    partial void OnActiveProviderChanged(ProviderEntry? value) => ProvidersChanged?.Invoke();

    private void Refresh()
    {
        Fill(EpisodeProviders, _services.Registry.Enabled(ProviderMode.Episode));
        Fill(MovieProviders, _services.Registry.Enabled(ProviderMode.Movie));
        if (_movieProvider is { IsEnabled: false }) _movieProvider = null;
        if (_episodeProvider is { IsEnabled: false }) _episodeProvider = null;
        if (ActiveProvider is { } a && !a.IsEnabled) ActiveProvider = null;
        ProvidersChanged?.Invoke();
    }

    private static void Fill(ObservableCollection<ProviderEntry> target, IEnumerable<ProviderEntry> source)
    {
        target.Clear();
        foreach (var e in source) target.Add(e);
    }

    private sealed class EmptyConfig : IProviderConfig
    {
        public string? Get(string key) => null;
    }
}
