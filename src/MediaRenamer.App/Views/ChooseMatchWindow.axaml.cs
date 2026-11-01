using Avalonia.Controls;
using Avalonia.Input;
using MediaRenamer.App.ViewModels;
using MediaRenamer.Plugin.Abstractions;

namespace MediaRenamer.App.Views;

/// <summary>Closes with the chosen <see cref="SearchResult"/>, or null when skipped.</summary>
public partial class ChooseMatchWindow : Window
{
    public ChooseMatchWindow()
    {
        InitializeComponent();
        Services.WindowPlacement.Attach(this);
    }

    public ChooseMatchWindow(ChooseMatchViewModel vm) : this() => DataContext = vm;

    private ChooseMatchViewModel? Vm => DataContext as ChooseMatchViewModel;

    private void OnDoubleTapped(object? sender, TappedEventArgs e) => Confirm();

    private void OnSelect(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => Confirm();

    private void Confirm()
    {
        if (Vm?.Selected is { } item) Close(item.Result);
    }

    private void OnSkip(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => Close((SearchResult?)null);
}
