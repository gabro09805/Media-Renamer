namespace MediaRenamer.Model;

/// <summary>Anime is a TV series: it has no formats of its own. The value is kept only so older saved files still load (they are read as Series).</summary>
public enum FormatKind { Movie, Series, Anime }

/// <summary>A rename format such as <c>{n}/Season {s00}/{n} - {s00e00} - {t}</c>. Folders are separated by '/'; the extension is added by the renamer.</summary>
public sealed record RenameFormat(string Id, string Name, FormatKind Kind, string Expression, bool IsBuiltIn = false);

public static class BuiltInFormats
{
    public static readonly IReadOnlyList<RenameFormat> All = new[]
    {
        new RenameFormat("builtin.movie.folder", "Movie – folder per movie", FormatKind.Movie, "{n} ({y})/{n} ({y})", true),
        new RenameFormat("builtin.movie.flat", "Movie – single folder", FormatKind.Movie, "{n} ({y})", true),
        new RenameFormat("builtin.series.seasons", "Series – folders per season", FormatKind.Series, "{n}/Season {s00}/{n} - {s00e00} - {t}", true),
        new RenameFormat("builtin.series.flat", "Series – single folder", FormatKind.Series, "{n} - {s00e00} - {t}", true),
        new RenameFormat("builtin.anime.absolute", "Series – absolute numbering (anime)", FormatKind.Series, "{n}/Season {s00}/{n} - {absolute} - {t}", true),
        new RenameFormat("builtin.anime.seasons", "Series – season numbering (anime)", FormatKind.Series, "{n}/Season {s00}/{n} - {s00e00} - {t}", true),
    };

    public static RenameFormat Default(FormatKind kind)
    {
        if (kind == FormatKind.Anime) kind = FormatKind.Series;
        return All.First(f => f.Kind == kind);
    }
}
