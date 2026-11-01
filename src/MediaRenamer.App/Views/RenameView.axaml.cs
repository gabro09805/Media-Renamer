using Avalonia.Controls;
using MediaRenamer.App.ViewModels;
using MediaRenamer.Model;
using MediaRenamer.PluginHost;

namespace MediaRenamer.App.Views;

public partial class RenameView : UserControl
{
    private RenameViewModel? _vm;

    public RenameView()
    {
        InitializeComponent();
        DropZone.AddHandler(Avalonia.Input.DragDrop.DropEvent, OnDrop);
        DropZone.AddHandler(Avalonia.Input.DragDrop.DragOverEvent, OnDragOver);
        Avalonia.Input.DragDrop.SetAllowDrop(DropZone, true);
        DataContextChanged += (_, _) => Attach();
        EnableDragReorder(OriginalList, (rows, delta) => _vm!.MoveOriginals(rows, delta));
        EnableDragReorder(NewList, (rows, delta) => _vm!.MoveNewNames(rows, delta));

        var linkMenu = new ContextMenu();
        linkMenu.Opening += (_, e) => { if (!FillLinkMenu(linkMenu)) e.Cancel = true; };
        OriginalList.ContextMenu = linkMenu;
    }

    /// <summary>Right-click on a subtitle: choose the video it follows, or match it on its own.</summary>
    private bool FillLinkMenu(ContextMenu menu)
    {
        menu.Items.Clear();
        if (_vm is null || OriginalList.SelectedItem is not FileRowViewModel { IsSubtitle: true } sub) return false;
        var current = _vm.LinkedVideoOf(sub.Path);

        menu.Items.Add(new MenuItem { Header = "Link this subtitle to video:", IsEnabled = false, FontWeight = Avalonia.Media.FontWeight.SemiBold });
        foreach (var video in _vm.VideosInOrder())
        {
            var path = video.Path;
            var item = new MenuItem { Header = System.IO.Path.GetFileName(path), IsChecked = string.Equals(path, current, StringComparison.OrdinalIgnoreCase) };
            item.Click += (_, _) => _vm.LinkSubtitle(sub.Path, path);
            menu.Items.Add(item);
        }
        menu.Items.Add(new Separator());
        var alone = new MenuItem { Header = "Match on its own name", IsChecked = current is null };
        alone.Click += (_, _) => _vm.LinkSubtitle(sub.Path, null);
        menu.Items.Add(alone);
        return true;
    }

    private void Attach()
    {
        if (_vm is not null) _vm.ProvidersChanged -= RebuildMenu;
        _vm = DataContext as RenameViewModel;
        if (_vm is null) return;
        _vm.ProvidersChanged += RebuildMenu;
        _vm.CandidateChooser = model => Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(async () =>
            TopLevel.GetTopLevel(this) is Window owner ? await new ChooseMatchWindow(model).ShowDialog<Plugin.Abstractions.SearchResult?>(owner) : null);
        RebuildMenu();
    }

    // Built here instead of in XAML because the entries depend on the providers currently enabled.
    private void RebuildMenu()
    {
        if (_vm is null) return;
        foreach (var button in new[] { FetchButton, MatchButton })
            if (button.Flyout is MenuFlyout flyout) FillMenu(flyout.Items, _vm);
    }

    private void FillMenu(ItemCollection items, RenameViewModel vm)
    {
        items.Clear();
        items.Add(new MenuItem { Header = "Fetch & Match Data", IsEnabled = false, FontWeight = Avalonia.Media.FontWeight.SemiBold });
        items.Add(new Separator());
        AddMode(items, "Episode Mode", vm.EpisodeProviders);
        items.Add(new Separator());
        AddMode(items, "Movie Mode", vm.MovieProviders);
        items.Add(new Separator());
        var editFormat = new MenuItem { Header = "Edit Format" };
        editFormat.Click += async (_, _) => await ShowFormatEditorAsync();
        items.Add(editFormat);
        var prefs = new MenuItem { Header = "Match Preferences" };
        prefs.Click += async (_, _) => await ShowMatchPreferencesAsync();
        items.Add(prefs);
    }

    private async void OnEditMatch(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => await EditMatchAsync();

    private async void OnNewNameDoubleTapped(object? sender, Avalonia.Input.TappedEventArgs e) => await EditMatchAsync();

    private async Task EditMatchAsync()
    {
        if (_vm is null || TopLevel.GetTopLevel(this) is not Window owner) return;
        var row = NewList.SelectedItem as FileRowViewModel ?? OriginalList.SelectedItem as FileRowViewModel;
        // With "Apply only to selected item" the dialog is offered again for the next unidentified file, until cancelled.
        while (row is not null && _vm.CreateEditMatch(row) is { } model)
        {
            if (await new EditMatchWindow(model).ShowDialog<MatchResult?>(owner) is not { } match) break;
            _vm.ApplyEdit(model, match);
            if (model.ApplyToAll) break;
            row = _vm.NextUnidentified(row.Path);
        }
    }

    private async void OnEditFormat(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => await ShowFormatEditorAsync();

    private void OnMoveUp(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => MoveSelected(-1);

    private void OnMoveDown(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => MoveSelected(1);

    private string? _dragPath;

    /// <summary>
    /// Drag a row with the mouse pressed: each time the pointer enters another row the selection moves one step toward it,
    /// the same operation as the arrow buttons (so original files keep their match, new names shift the others).
    /// </summary>
    private void EnableDragReorder(ListBox list, Func<IReadOnlyList<FileRowViewModel>, int, IReadOnlyList<string>> move)
    {
        list.AddHandler(Avalonia.Input.InputElement.PointerPressedEvent, (_, e) =>
        {
            _dragPath = e.GetCurrentPoint(list).Properties.IsLeftButtonPressed && e.KeyModifiers == Avalonia.Input.KeyModifiers.None
                        && RowAt(list, e.GetPosition(list)) is { } row ? row.Path : null;
        }, Avalonia.Interactivity.RoutingStrategies.Tunnel, handledEventsToo: true);

        list.AddHandler(Avalonia.Input.InputElement.PointerReleasedEvent, (_, _) => EndDrag(list),
            Avalonia.Interactivity.RoutingStrategies.Tunnel | Avalonia.Interactivity.RoutingStrategies.Bubble, handledEventsToo: true);
        list.AddHandler(Avalonia.Input.InputElement.PointerCaptureLostEvent, (_, _) => EndDrag(list));

        list.AddHandler(Avalonia.Input.InputElement.PointerMovedEvent, (_, e) =>
        {
            if (_vm is null || _dragPath is null || !e.GetCurrentPoint(list).Properties.IsLeftButtonPressed) { EndDrag(list); return; }
            ShowGhost(list, e);
            if (RowAt(list, e.GetPosition(list)) is not { } target || target.Path == _dragPath) return;

            var rows = _vm.Rows;
            var from = rows.ToList().FindIndex(r => r.Path == _dragPath);
            var to = rows.IndexOf(target);
            if (from < 0 || to < 0) return;

            var selected = list.SelectedItems?.Cast<FileRowViewModel>().ToList() ?? new List<FileRowViewModel>();
            if (!selected.Any(r => r.Path == _dragPath)) selected = new List<FileRowViewModel> { rows[from] };
            if (selected.Contains(target)) return;

            // Hovering the subtitles attached to the dragged video is not a move.
            if (to > from && !rows[from].IsSubtitle && Enumerable.Range(from + 1, to - from).All(i => rows[i].IsSubtitle)) return;

            Reselect(move(selected, to > from ? 1 : -1), list);
        });
    }

    private Avalonia.Point? _dragStart;

    /// <summary>Floating label that follows the pointer while rows are dragged (shown after a small movement, so plain clicks do not flash it).</summary>
    private void ShowGhost(ListBox list, Avalonia.Input.PointerEventArgs e)
    {
        var p = e.GetPosition(DragHost);
        _dragStart ??= p;
        if (Math.Abs(p.X - _dragStart.Value.X) + Math.Abs(p.Y - _dragStart.Value.Y) < 6 && !DragGhost.IsVisible) return;

        var selected = list.SelectedItems?.Cast<FileRowViewModel>().ToList() ?? new List<FileRowViewModel>();
        var dragged = selected.Any(r => r.Path == _dragPath) ? selected : _vm!.Rows.Where(r => r.Path == _dragPath).ToList();
        if (dragged.Count == 0) return;
        var name = dragged[0].OriginalName;
        DragGhostText.Text = dragged.Count > 1 ? $"{name}  (+{dragged.Count - 1})" : name;
        DragGhost.IsVisible = true;
        Avalonia.Controls.Canvas.SetLeft(DragGhost, p.X + 14);
        Avalonia.Controls.Canvas.SetTop(DragGhost, p.Y + 10);
        list.Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.SizeAll);
    }

    private void EndDrag(ListBox list)
    {
        _dragPath = null;
        _dragStart = null;
        DragGhost.IsVisible = false;
        list.Cursor = Avalonia.Input.Cursor.Default;
    }

    private static FileRowViewModel? RowAt(ListBox list, Avalonia.Point position) =>
        Avalonia.VisualTree.VisualExtensions.FindAncestorOfType<ListBoxItem>(Avalonia.VisualTree.VisualExtensions.GetVisualAt(list, position), true)?.DataContext as FileRowViewModel;
    private void OnMoveNewUp(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => MoveNames(-1);

    private void OnSelectAllOriginal(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => OriginalList.SelectAll();

    private void OnSelectAllNew(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => NewList.SelectAll();

    private void OnMoveNewDown(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => MoveNames(1);

    private void MoveSelected(int delta)
    {
        if (_vm is null || OriginalList.SelectedItems is null) return;
        Reselect(_vm.MoveOriginals(OriginalList.SelectedItems.Cast<FileRowViewModel>().ToList(), delta), OriginalList);
    }

    private void MoveNames(int delta)
    {
        if (_vm is null || NewList.SelectedItems is null) return;
        Reselect(_vm.MoveNewNames(NewList.SelectedItems.Cast<FileRowViewModel>().ToList(), delta), NewList);
    }

    // Rows are rebuilt after a move, so the selection is restored by path.
    private void Reselect(IReadOnlyList<string> paths, ListBox list)
    {
        if (_vm is null || list.SelectedItems is null) return;
        list.SelectedItems.Clear();
        foreach (var row in _vm.Rows.Where(r => paths.Contains(r.Path, StringComparer.OrdinalIgnoreCase)))
            list.SelectedItems.Add(row);
    }
    private void OnOriginalKeyDown(object? sender, Avalonia.Input.KeyEventArgs e)
    {
        if (e.Key != Avalonia.Input.Key.Delete || _vm is null || OriginalList.SelectedItems is null) return;
        _vm.RemoveRows(OriginalList.SelectedItems.Cast<FileRowViewModel>());
        e.Handled = true;
    }

    private void OnRemove(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (_vm is null) return;
        _vm.RemoveRows(OriginalList.SelectedItems!.Cast<FileRowViewModel>());
    }

    private async void OnLoadFiles(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (_vm is null || TopLevel.GetTopLevel(this) is not { } top) return;
        var files = await top.StorageProvider.OpenFilePickerAsync(new Avalonia.Platform.Storage.FilePickerOpenOptions
        {
            Title = "Select video and subtitle files",
            AllowMultiple = true,
        });
        _vm.AddPaths(files.Select(f => f.Path.LocalPath));
    }

    private async void OnLoadFolder(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (_vm is null || TopLevel.GetTopLevel(this) is not { } top) return;
        var folders = await top.StorageProvider.OpenFolderPickerAsync(new Avalonia.Platform.Storage.FolderPickerOpenOptions
        {
            Title = "Select a folder (subfolders included)",
            AllowMultiple = true,
        });
        _vm.AddPaths(folders.Select(f => f.Path.LocalPath));
    }

    private async void OnRename(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (_vm is null || TopLevel.GetTopLevel(this) is not Window owner) return;
        var plan = _vm.PendingRenames();
        if (plan.Count == 0) { _vm.Status = "Nothing to rename: run Match first, or fix the highlighted items."; return; }
        if (await new ConfirmRenameWindow(plan).ShowDialog<bool>(owner)) _vm.ApplyRename(plan);
    }

    private void OnDrop(object? sender, Avalonia.Input.DragEventArgs e)
    {
        if (_vm is null) return;
        var files = Avalonia.Input.DataTransferExtensions.TryGetFiles(e.DataTransfer);
        if (files is not null) _vm.AddPaths(files.Select(f => f.Path.LocalPath));
    }

    private void OnDragOver(object? sender, Avalonia.Input.DragEventArgs e) =>
        e.DragEffects = Avalonia.Input.DataTransferExtensions.Contains(e.DataTransfer, Avalonia.Input.DataFormat.File) ? Avalonia.Input.DragDropEffects.Copy : Avalonia.Input.DragDropEffects.None;

    private async Task ShowFormatEditorAsync()
    {
        if (_vm is null || TopLevel.GetTopLevel(this) is not Window owner) return;
        var editor = _vm.CreateFormatEditor();
        await new FormatEditorWindow(editor).ShowDialog(owner);
    }

    private async Task ShowMatchPreferencesAsync()
    {
        if (_vm is null || TopLevel.GetTopLevel(this) is not Window owner) return;
        var result = await new MatchPreferencesWindow(_vm.CurrentPreferences).ShowDialog<MatchPreferences?>(owner);
        if (result is not null) await _vm.SavePreferencesAsync(result);
    }

    private void AddMode(ItemCollection items, string title, IEnumerable<ProviderEntry> providers)
    {
        items.Add(new MenuItem { Header = title, IsEnabled = false });
        var any = false;
        foreach (var entry in providers)
        {
            any = true;
            var item = new MenuItem { Header = entry.Provider.DisplayName };
            // Choosing the provider in this category starts the match straight away.
            item.Click += async (_, _) =>
            {
                _vm!.SelectProviderCommand.Execute(entry);
                await _vm.MatchCommand.ExecuteAsync(null);
            };
            items.Add(item);
        }
        if (!any) items.Add(new MenuItem { Header = "(no providers enabled)", IsEnabled = false });
    }
}
