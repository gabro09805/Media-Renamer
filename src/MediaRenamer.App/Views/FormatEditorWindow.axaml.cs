using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using MediaRenamer.App.ViewModels;
using MediaRenamer.Bindings.Formats;

namespace MediaRenamer.App.Views;

public partial class FormatEditorWindow : Window
{
    private FormatEditorViewModel? _vm;

    public FormatEditorWindow()
    {
        InitializeComponent();
        Services.WindowPlacement.Attach(this);
        DataContextChanged += (_, _) => Attach();
        ExpressionBox.TemplateApplied += (_, e) =>
        {
            if (e.NameScope.Find<ScrollViewer>("PART_ScrollViewer") is { } sv) sv.ScrollChanged += SyncScroll;
        };
        Closed += (_, _) => _vm?.Dispose();
    }

    public FormatEditorWindow(FormatEditorViewModel vm) : this() => DataContext = vm;

    private void Attach()
    {
        if (_vm is not null) _vm.SpansChanged -= Redraw;
        _vm = DataContext as FormatEditorViewModel;
        if (_vm is null) return;
        _vm.SpansChanged += Redraw;
        Redraw();
    }

    // Keeps the colored layer aligned with the TextBox when long text scrolls horizontally.
    private void SyncScroll(object? sender, ScrollChangedEventArgs e)
    {
        if (sender is ScrollViewer sv) Highlighted.RenderTransform = new Avalonia.Media.TranslateTransform(-sv.Offset.X, 0);
    }

    // Blue: bindings between braces. Orange: characters that end up unchanged in the final name.
    private static readonly IBrush BindingBrush = new SolidColorBrush(Color.Parse("#2F8FE0"));
    private static readonly IBrush LiteralBrush = new SolidColorBrush(Color.Parse("#E08A1E"));

    private static IBrush Brush(SyntaxKind kind) => kind switch
    {
        SyntaxKind.Text => LiteralBrush,
        SyntaxKind.Separator => LiteralBrush,
        SyntaxKind.Brace => BindingBrush,
        SyntaxKind.Binding => BindingBrush,
        SyntaxKind.Function => new SolidColorBrush(Color.Parse("#C78BE8")),
        SyntaxKind.Variable => new SolidColorBrush(Color.Parse("#7FD1C8")),
        SyntaxKind.Keyword => new SolidColorBrush(Color.Parse("#E07F7F")),
        SyntaxKind.String => new SolidColorBrush(Color.Parse("#7FC87F")),
        SyntaxKind.Number => new SolidColorBrush(Color.Parse("#D9B45B")),
        SyntaxKind.Regex => new SolidColorBrush(Color.Parse("#E0A15B")),
        SyntaxKind.Operator => Brushes.Gray,
        SyntaxKind.Error => Brushes.Red,
        _ => Brushes.Transparent,
    };

    private void Redraw()
    {
        if (_vm is null) return;
        var text = _vm.Expression;
        Highlighted.Inlines ??= new InlineCollection();
        Highlighted.Inlines.Clear();
        foreach (var span in _vm.Spans)
        {
            if (span.Start < 0 || span.Length <= 0 || span.Start + span.Length > text.Length) continue;
            var run = new Run(text.Substring(span.Start, span.Length));
            run.Foreground = Brush(span.Kind);
            if (span.Kind == SyntaxKind.Error) run.TextDecorations = TextDecorations.Underline;
            Highlighted.Inlines.Add(run);
        }
    }

    private void OnInsertBinding(object? sender, RoutedEventArgs e)
    {
        if (_vm is null || _vm.IsBuiltIn || sender is not Button { Tag: string name }) return;
        var caret = ExpressionBox.CaretIndex;
        _vm.Insert("{" + name + "}", caret, out var next);
        ExpressionBox.Focus();
        ExpressionBox.CaretIndex = next;
    }

    private async void OnImport(object? sender, RoutedEventArgs e)
    {
        if (_vm is null) return;
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Import formats",
            AllowMultiple = false,
            FileTypeFilter = new[] { new FilePickerFileType("MediaRenamer formats") { Patterns = new[] { "*.json" } } },
        });
        if (files.Count > 0 && files[0].TryGetLocalPath() is { } path) _vm.Import(path);
    }

    private async void OnExport(object? sender, RoutedEventArgs e)
    {
        if (_vm?.SelectedFormat is null) return;
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Export format",
            SuggestedFileName = _vm.SelectedFormat.Name + ".json",
            DefaultExtension = "json",
            FileTypeChoices = new[] { new FilePickerFileType("MediaRenamer formats") { Patterns = new[] { "*.json" } } },
        });
        if (file?.TryGetLocalPath() is { } path) _vm.Export(path);
    }

    private async void OnShowValues(object? sender, RoutedEventArgs e)
    {
        if (_vm is null) return;
        await new BindingInspectorWindow(_vm.CreateInspector()).ShowDialog(this);
    }

    private void OnClose(object? sender, RoutedEventArgs e) => Close();
}
