using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using MediaRenamer.App.Services;
using MediaRenamer.App.ViewModels;
using MediaRenamer.App.Views;

namespace MediaRenamer.App;

public partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var services = AppServices.Create();
            WindowPlacement.Configure(Path.Combine(services.Paths.Config, "window-state.json"));
            desktop.MainWindow = new MainWindow
            {
                DataContext = new MainViewModel(services),
            };
        }

        base.OnFrameworkInitializationCompleted();
    }
}