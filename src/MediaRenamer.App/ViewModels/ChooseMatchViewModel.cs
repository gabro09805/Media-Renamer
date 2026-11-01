using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using MediaRenamer.Plugin.Abstractions;

namespace MediaRenamer.App.ViewModels;

/// <summary>Candidates offered when the automatic match cannot decide between several results.</summary>
public sealed partial class ChooseMatchViewModel : ViewModelBase
{
    public ChooseMatchViewModel(string title, IReadOnlyList<string> files, IEnumerable<ResultItem> candidates)
    {
        Title = title;
        Files = files.Take(10).Concat(files.Count > 10 ? new[] { $"... and {files.Count - 10} more" } : Array.Empty<string>()).ToList();
        foreach (var c in candidates) Candidates.Add(c);
        Selected = Candidates.FirstOrDefault();
    }

    public string Title { get; }
    public IReadOnlyList<string> Files { get; }
    public ObservableCollection<ResultItem> Candidates { get; } = new();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelection))]
    private ResultItem? _selected;

    public bool HasSelection => Selected is not null;
}
