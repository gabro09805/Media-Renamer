using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using MediaRenamer.Core.Matching;
using MediaRenamer.Model;

namespace MediaRenamer.App.ViewModels;

public sealed record PairingRow(string File, string Episode, string Note);

/// <summary>"Send to Rename" dialog (spec §8.3): choose the association mode and review the pairing before confirming.</summary>
public sealed partial class SendToRenameViewModel : ObservableObject
{
    private readonly IReadOnlyList<MediaFile> _files;
    private readonly Series _series;
    private readonly IReadOnlyList<Episode> _selected;
    private readonly MatchPreferences _prefs;

    public SendToRenameViewModel(IReadOnlyList<MediaFile> files, Series series, IReadOnlyList<Episode> selected, MatchPreferences prefs)
    {
        _files = files;
        _series = series;
        _selected = selected;
        _prefs = prefs;
        _plan = BySearchOrder(true);
        Rebuild();
    }

    public ObservableCollection<PairingRow> Preview { get; } = new();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEpisodeNumber))]
    private bool _isSearchOrder = true;

    public bool IsEpisodeNumber
    {
        get => !IsSearchOrder;
        set => IsSearchOrder = !value;
    }

    [ObservableProperty] private AssociationPlan _plan;
    [ObservableProperty] private string _summary = "";

    public bool CanSend => Plan.Problem is null;
    public bool HasProblem => Plan.Problem is not null;

    partial void OnIsSearchOrderChanged(bool value)
    {
        Plan = BySearchOrder(value);
        Rebuild();
    }

    private AssociationPlan BySearchOrder(bool searchOrder) => searchOrder
        ? EpisodeAssociator.BySearchOrder(_files, _series, _selected)
        : EpisodeAssociator.ByEpisodeNumber(_files, _series, _selected, _prefs);

    private void Rebuild()
    {
        Preview.Clear();
        foreach (var m in Plan.Matches)
            Preview.Add(new PairingRow(System.IO.Path.GetFileName(m.File.Path),
                m.Episode is null ? "(no episode)" : BindingInspectorViewModel.FormatEpisode(m.Episode, _prefs.OutputLanguage),
                m.Episode is null ? string.Join(" ", m.Reasons) : ""));
        Summary = $"{_files.Count} file(s), {_selected.Count} episode(s) selected.";
        OnPropertyChanged(nameof(CanSend));
        OnPropertyChanged(nameof(HasProblem));
    }
}
