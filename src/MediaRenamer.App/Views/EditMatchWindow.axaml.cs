using Avalonia.Controls;
using Avalonia.Input;
using MediaRenamer.App.ViewModels;
using MediaRenamer.Model;

namespace MediaRenamer.App.Views;

/// <summary>Edit Match dialog: closes with the chosen <see cref="MatchResult"/>, or null when cancelled.</summary>
public partial class EditMatchWindow : Window
{
    public EditMatchWindow()
    {
        InitializeComponent();
        Services.WindowPlacement.Attach(this);
    }

    public EditMatchWindow(EditMatchViewModel vm) : this()
    {
        DataContext = vm;
        Title = vm.Title;
    }

    private EditMatchViewModel? Vm => DataContext as EditMatchViewModel;

    private void OnQueryKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) Vm?.SearchCommand.Execute(null);
    }

    private void OnEpisodeDoubleTapped(object? sender, TappedEventArgs e) => Confirm();

    private void OnOk(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => Confirm();

    private void Confirm()
    {
        if (Vm?.Build() is { } match) Close(match);
    }

    private void OnCancel(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => Close((MatchResult?)null);
}
