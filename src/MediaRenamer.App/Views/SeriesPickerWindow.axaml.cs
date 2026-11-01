using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using MediaRenamer.App.ViewModels;

namespace MediaRenamer.App.Views;

/// <summary>Search window: pick the provider, search by name and choose one result while seeing its cover.</summary>
public partial class SeriesPickerWindow : Window
{
    private BindingInspectorViewModel? _vm;
    private int _posterVersion;

    public SeriesPickerWindow()
    {
        InitializeComponent();
        Services.WindowPlacement.Attach(this);
        DataContextChanged += (_, _) => Attach();
        Opened += (_, _) => QueryBox.Focus();
    }

    public SeriesPickerWindow(BindingInspectorViewModel vm) : this()
    {
        DataContext = vm;
        Title = vm.Kind == Model.FormatKind.Movie ? "Search movie" : "Search series";
    }

    private void Attach()
    {
        if (_vm is not null) _vm.PropertyChanged -= OnVmChanged;
        _vm = DataContext as BindingInspectorViewModel;
        if (_vm is null) return;
        _vm.PropertyChanged += OnVmChanged;
        _ = ShowPosterAsync();
    }

    private void OnVmChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(BindingInspectorViewModel.SelectedResult)) _ = ShowPosterAsync();
    }

    private async Task ShowPosterAsync()
    {
        var item = _vm?.SelectedResult;
        var version = ++_posterVersion;
        PosterTitle.Text = item?.Label ?? "";
        Poster.Source = null;
        NoPoster.IsVisible = false;
        if (item is null || _vm is null) return;
        if (await _vm.ResolvePosterAsync(item) is not { } uri)
        {
            if (version == _posterVersion) NoPoster.IsVisible = true;
            return;
        }

        var bitmap = await Services.CoverCache.GetAsync(uri);
        if (version != _posterVersion) return;
        Poster.Source = bitmap;
        NoPoster.IsVisible = bitmap is null;
    }

    private void OnQueryKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && _vm is not null) _vm.SearchCommand.Execute(null);
    }

    private void OnResultDoubleTapped(object? sender, TappedEventArgs e) => Confirm();

    private void OnSelect(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => Confirm();

    private void Confirm()
    {
        if (_vm?.SelectedResult is null) return;
        _vm.ConfirmSelection();
        Close();
    }

    private void OnCancel(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        _vm?.CancelSelection();
        Close();
    }

    protected override void OnClosed(EventArgs e)
    {
        if (_vm is not null) _vm.PropertyChanged -= OnVmChanged;
        base.OnClosed(e);
    }
}
