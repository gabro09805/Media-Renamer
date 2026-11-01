using CommunityToolkit.Mvvm.ComponentModel;
using MediaRenamer.Core.Renaming;
using MediaRenamer.Model;

namespace MediaRenamer.App.ViewModels;

/// <summary>One line of the Rename lists: the original file and what it would become.</summary>
public sealed partial class FileRowViewModel : ObservableObject
{
    public FileRowViewModel(string path, bool isSubtitle)
    {
        Path = path;
        IsSubtitle = isSubtitle;
    }

    public string Path { get; }
    public bool IsSubtitle { get; }
    public string OriginalName => System.IO.Path.GetFileName(Path);

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DisplayNewName))]
    [NotifyPropertyChangedFor(nameof(NewNameParts))]
    private string? _newPath;

    /// <summary>Destination root (library folder); folders below it get their own colour.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NewNameParts))]
    private string? _destinationRoot;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DisplayNewName))]
    [NotifyPropertyChangedFor(nameof(HasIssue))]
    [NotifyPropertyChangedFor(nameof(NewNameParts))]
    private PlanState? _state;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasWarning))]
    [NotifyPropertyChangedFor(nameof(RowBackground))]
    [NotifyPropertyChangedFor(nameof(ToolTipText))]
    private string? _warning;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ToolTipText))]
    private string? _message;

    /// <summary>The match disagrees with the original file (name or numbering): shown in red, renaming stays possible.</summary>
    public bool HasWarning => Warning is not null;

    private static readonly Avalonia.Media.IBrush WarningBrush = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.FromArgb(0x55, 0xFF, 0x40, 0x40));

    /// <summary>Tint of the whole row when the match disagrees with the file.</summary>
    public Avalonia.Media.IBrush RowBackground => HasWarning ? WarningBrush : Avalonia.Media.Brushes.Transparent;

    public string? ToolTipText => string.Join(Environment.NewLine, new[] { Message, Warning }.Where(s => !string.IsNullOrEmpty(s)));

    [ObservableProperty]
    private MatchResult? _match;

    public string Prefix => IsSubtitle ? "    ↳ " : "";

    public string DisplayOriginal => Prefix + OriginalName;

    public string DisplayNewName => State switch
    {
        null => "",
        PlanState.Ready or PlanState.Unchanged when NewPath is not null => Prefix + System.IO.Path.GetFileName(NewPath),
        PlanState.NoMatch => Prefix + "(no match)",
        PlanState.NeedsReview => Prefix + "(needs review)",
        PlanState.Collision => Prefix + "(name collision)",
        PlanState.TargetExists => Prefix + "(target exists)",
        PlanState.InvalidPath => Prefix + "(invalid path)",
        PlanState.FormatError => Prefix + "(format error)",
        _ => "",
    };

    private static readonly Avalonia.Media.IBrush[] FolderBrushes =
    {
        new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#D98E1F")),
        new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#2E9E4F")),
        new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#3B82D9")),
        new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#A855D9")),
        new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#14A3A3")),
    };

    /// <summary>The colour of the folder at this depth, shared with the format preview so both show the same levels in the same colours.</summary>
    public static Avalonia.Media.IBrush FolderBrush(int level) => FolderBrushes[level % FolderBrushes.Length];

    /// <summary>
    /// The new name split for display: the base folder (library root, or the file's own folder) in the first colour,
    /// every further folder in the next colour, then the file name. A file renamed in place shows just its name.
    /// </summary>
    public IReadOnlyList<PathPart> NewNameParts
    {
        get
        {
            if (State is not (PlanState.Ready or PlanState.Unchanged) || NewPath is null)
                return new[] { new PathPart(DisplayNewName, null) };
            return BuildParts(Path, NewPath, DestinationRoot, Prefix);
        }
    }

    public static IReadOnlyList<PathPart> BuildParts(string source, string target, string? root, string prefix)
    {
        var parts = new List<PathPart>();
        var targetDir = System.IO.Path.GetDirectoryName(target) ?? "";
        var sourceDir = System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(source)) ?? "";
        var name = prefix + System.IO.Path.GetFileName(target);
        if (string.Equals(targetDir, sourceDir, StringComparison.OrdinalIgnoreCase)) return new[] { new PathPart(name, null) };

        var baseDir = !string.IsNullOrEmpty(root) && IsUnder(targetDir, root!) ? System.IO.Path.TrimEndingDirectorySeparator(root!) : sourceDir;
        if (!IsUnder(targetDir, baseDir)) baseDir = targetDir;

        parts.Add(new PathPart(prefix + baseDir.Replace('\\', '/') + "/", FolderBrushes[0]));
        var rest = System.IO.Path.GetRelativePath(baseDir, targetDir);
        if (rest != ".")
        {
            var level = 1;
            foreach (var folder in rest.Split(new[] { '\\', '/' }, StringSplitOptions.RemoveEmptyEntries))
                parts.Add(new PathPart(folder + "/", FolderBrushes[level++ % FolderBrushes.Length]));
        }
        parts.Add(new PathPart(System.IO.Path.GetFileName(target), null));
        return parts;
    }

    private static bool IsUnder(string dir, string root)
    {
        var r = System.IO.Path.TrimEndingDirectorySeparator(root);
        return string.Equals(dir, r, StringComparison.OrdinalIgnoreCase)
            || dir.StartsWith(r + System.IO.Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    public bool HasIssue => State is not null and not PlanState.Ready and not PlanState.Unchanged;

    public void Apply(PlanItem item)
    {
        NewPath = item.Target;
        State = item.Status;
        Message = item.Message;
        Match = item.Match;
    }

    public void Clear()
    {
        NewPath = null;
        State = null;
        Message = null;
        Match = null;
        Warning = null;
    }
}

/// <summary>A piece of the new name; <see cref="Brush"/> is null for the file name (default text colour).</summary>
public sealed record PathPart(string Text, Avalonia.Media.IBrush? Brush)
{
    public bool IsColored => Brush is not null;
    public bool IsPlain => Brush is null;
}
