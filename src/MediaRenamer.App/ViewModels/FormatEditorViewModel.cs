using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MediaRenamer.App.Services;
using MediaRenamer.Bindings.Catalog;
using MediaRenamer.Bindings.Formats;
using MediaRenamer.Model;

namespace MediaRenamer.App.ViewModels;

public sealed record KindOption(FormatKind Kind, string Label);

public sealed record BindingGroup(string Category, IReadOnlyList<BindingDefinition> Items);

public sealed partial class FormatEditorViewModel : ViewModelBase, IDisposable
{
    private readonly AppServices _services;
    private bool _loading;

    public FormatEditorViewModel(AppServices services, FormatKind kind = FormatKind.Series)
    {
        _services = services;
        Kinds = new[]
        {
            new KindOption(FormatKind.Movie, "Movies"),
            new KindOption(FormatKind.Series, "TV Series"),
        };
        BindingGroups = BindingCatalog.All
            .GroupBy(b => b.Category)
            .Select(g => new BindingGroup(g.Key, g.ToList()))
            .ToList();

        _selectedKind = Kinds.First(k => k.Kind == kind);
        LoadSamples();
        LoadFormats(services.Formats.Selected(kind).Id);

        services.Formats.Changed += OnStoreChanged;
        services.MatchPreferences.Changed += OnPreferencesChanged;
    }

    public event Action? SpansChanged;

    public IReadOnlyList<KindOption> Kinds { get; }
    public IReadOnlyList<BindingGroup> BindingGroups { get; }
    public ObservableCollection<RenameFormat> Formats { get; } = new();
    public ObservableCollection<SampleOption> Samples { get; } = new();

    [ObservableProperty] private KindOption _selectedKind;
    [ObservableProperty] private RenameFormat? _selectedFormat;
    [ObservableProperty] private SampleOption? _selectedSample;
    [ObservableProperty] private string _name = "";
    [ObservableProperty] private string _expression = "";
    [ObservableProperty] private bool _isBuiltIn;
    [ObservableProperty] private bool _isDirty;
    [ObservableProperty] private bool _isValid = true;
    [ObservableProperty] private string _preview = "";
    [ObservableProperty] private string _previewFolder = "";
    [ObservableProperty] private string _status = "";
    [ObservableProperty] private IReadOnlyList<string> _problems = Array.Empty<string>();
    [ObservableProperty] private IReadOnlyList<PathPart> _previewFolders = Array.Empty<PathPart>();
    public bool HasPreviewFolders => PreviewFolders.Count > 0;
    partial void OnPreviewFoldersChanged(IReadOnlyList<PathPart> value) => OnPropertyChanged(nameof(HasPreviewFolders));
    [ObservableProperty] private IReadOnlyList<SyntaxSpan> _spans = Array.Empty<SyntaxSpan>();

    public bool HasProblems => Problems.Count > 0;
    public bool CanEdit => !IsBuiltIn && SelectedFormat is not null;

    partial void OnSelectedKindChanged(KindOption value)
    {
        if (_loading) return;
        LoadSamples();
        LoadFormats(_services.Formats.Selected(value.Kind).Id);
    }

    partial void OnSelectedFormatChanged(RenameFormat? value)
    {
        if (_loading || value is null) return;
        Load(value);
    }

    partial void OnSelectedSampleChanged(SampleOption? value)
    {
        if (!_loading) Refresh();
    }

    partial void OnNameChanged(string value)
    {
        if (!_loading) IsDirty = true;
    }

    partial void OnExpressionChanged(string value)
    {
        if (_loading) return;
        IsDirty = true;
        Refresh();
    }

    partial void OnIsBuiltInChanged(bool value)
    {
        OnPropertyChanged(nameof(CanEdit));
        SaveCommand.NotifyCanExecuteChanged();
        DeleteCommand.NotifyCanExecuteChanged();
    }

    partial void OnProblemsChanged(IReadOnlyList<string> value) => OnPropertyChanged(nameof(HasProblems));

    private void LoadSamples()
    {
        var previous = _loading;
        _loading = true;
        Samples.Clear();
        foreach (var s in SampleMedia.All.Where(s => s.Kind == SelectedKind.Kind)) Samples.Add(s);
        SelectedSample = Samples.FirstOrDefault();
        _loading = previous;
    }

    private void LoadFormats(string selectId)
    {
        _loading = true;
        Formats.Clear();
        foreach (var f in _services.Formats.Of(SelectedKind.Kind)) Formats.Add(f);
        SelectedFormat = Formats.FirstOrDefault(f => f.Id == selectId) ?? Formats.FirstOrDefault();
        _loading = false;
        if (SelectedFormat is not null) Load(SelectedFormat);
    }

    private void Load(RenameFormat format)
    {
        _loading = true;
        Name = format.Name;
        Expression = format.Expression;
        IsBuiltIn = format.IsBuiltIn;
        IsDirty = false;
        _loading = false;
        Status = "";
        Refresh();
    }

    private MediaBindingSource? CreateSource() =>
        SelectedSample?.Create(_services.MatchPreferences.Current);

    private void Refresh()
    {
        var validation = _services.Renderer.Validate(Expression, new MediaBindingSource(null, null, null, null, _services.MatchPreferences.Current));
        IsValid = validation.IsValid;
        Problems = validation.Errors.Select(e => $"Position {e.Start + 1}: {e.Message}").ToList();
        Spans = SyntaxHighlighter.Highlight(Expression);
        SpansChanged?.Invoke();

        var source = CreateSource();
        if (source is null || !IsValid)
        {
            Preview = "";
            PreviewFolder = "";
            PreviewFolders = Array.Empty<PathPart>();
        }
        else
        {
            var result = _services.Renderer.Render(Expression, source);
            if (result.Success)
            {
                var ext = Path.GetExtension(source.File?.Path ?? "");
                Preview = result.FileName + ext;
                PreviewFolder = result.FolderPath ?? "";
                PreviewFolders = PreviewFolder.Split(new[] { '/', '\\' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select((f, i) => new PathPart(f + "/", FileRowViewModel.FolderBrush(i + 1))).ToList();
                Status = result.Warnings.Count > 0 ? string.Join(" ", result.Warnings) : "";
            }
            else
            {
                Preview = "";
                PreviewFolder = "";
                PreviewFolders = Array.Empty<PathPart>();
                Problems = new[] { result.Error!.Message };
            }
        }

    }

    private void OnStoreChanged()
    {
        if (_loading) return;
        var id = SelectedFormat?.Id ?? "";
        _loading = true;
        Formats.Clear();
        foreach (var f in _services.Formats.Of(SelectedKind.Kind)) Formats.Add(f);
        SelectedFormat = Formats.FirstOrDefault(f => f.Id == id) ?? Formats.FirstOrDefault();
        _loading = false;
    }

    private void OnPreferencesChanged(MatchPreferences _) => Refresh();

    [RelayCommand]
    private void New()
    {
        var saved = _services.Formats.Save(new RenameFormat("", _services.Formats.Of(SelectedKind.Kind).Count == 0 ? "New format" : "New format " + Guid.NewGuid().ToString("N")[..4], SelectedKind.Kind, BuiltInFormats.Default(SelectedKind.Kind).Expression));
        SelectFormat(saved.Id);
    }

    [RelayCommand]
    private void Duplicate()
    {
        if (SelectedFormat is null) return;
        var copy = _services.Formats.Duplicate(SelectedFormat.Id);
        SelectFormat(copy.Id);
    }

    [RelayCommand(CanExecute = nameof(CanEdit))]
    private void Delete()
    {
        if (SelectedFormat is null || SelectedFormat.IsBuiltIn) return;
        _services.Formats.Delete(SelectedFormat.Id);
        SelectFormat(_services.Formats.Selected(SelectedKind.Kind).Id);
    }

    [RelayCommand(CanExecute = nameof(CanEdit))]
    private void Save() => TrySave();

    [RelayCommand]
    private void Use()
    {
        if (SelectedFormat is null) return;
        var id = SelectedFormat.Id;
        if (!SelectedFormat.IsBuiltIn)
        {
            if (!TrySave()) return;
            id = SelectedFormat.Id;
        }
        _services.Formats.Select(SelectedKind.Kind, id);
        Status = "Format selected.";
    }

    private bool TrySave()
    {
        if (SelectedFormat is null || SelectedFormat.IsBuiltIn) return false;
        if (!IsValid || string.IsNullOrWhiteSpace(Name) || string.IsNullOrWhiteSpace(Expression))
        {
            Status = "Fix the problems before saving.";
            return false;
        }
        var saved = _services.Formats.Save(SelectedFormat with { Name = Name, Expression = Expression });
        SelectFormat(saved.Id);
        Status = "Saved.";
        return true;
    }

    private void SelectFormat(string id)
    {
        _loading = true;
        Formats.Clear();
        foreach (var f in _services.Formats.Of(SelectedKind.Kind)) Formats.Add(f);
        SelectedFormat = Formats.FirstOrDefault(f => f.Id == id) ?? Formats.FirstOrDefault();
        _loading = false;
        if (SelectedFormat is not null) Load(SelectedFormat);
    }

    public BindingInspectorViewModel CreateInspector() => new(_services, SelectedKind.Kind);

    public void Insert(string text, int caret, out int next)
    {
        caret = Math.Clamp(caret, 0, Expression.Length);
        Expression = Expression.Insert(caret, text);
        next = caret + text.Length;
    }

    public void Import(string path)
    {
        try
        {
            var added = _services.Formats.Import(path);
            Status = $"Imported {added} format(s).";
            SelectFormat(SelectedFormat?.Id ?? "");
        }
        catch (Exception ex) when (ex is FormatException or IOException)
        {
            Status = ex.Message;
        }
    }

    public void Export(string path)
    {
        if (SelectedFormat is null) return;
        try
        {
            _services.Formats.Export(new[] { SelectedFormat.Id }, path);
            Status = "Exported.";
        }
        catch (IOException ex)
        {
            Status = ex.Message;
        }
    }

    public void Dispose()
    {
        _services.Formats.Changed -= OnStoreChanged;
        _services.MatchPreferences.Changed -= OnPreferencesChanged;
    }
}
