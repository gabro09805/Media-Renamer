using System.Globalization;
using MediaRenamer.Bindings.Expressions;

namespace MediaRenamer.Bindings.Catalog;

/// <summary>One line of the binding explorer: a binding (or a member of it, like <c>lang.ISO2</c>) and what it gives for a media.</summary>
public sealed record BindingRow(string Name, string Category, string Description, string Value, bool IsNull, bool IsError)
{
    public bool IsMember => Name.Contains('.');
}

/// <summary>Evaluates every binding, and the members of the object ones, against a media. Used by the format editor.</summary>
public static class BindingExplorer
{
    private const int MaxDepth = 3;

    public static IReadOnlyList<BindingRow> Rows(MediaBindingSource source)
    {
        var rows = new List<BindingRow>();
        foreach (var definition in BindingCatalog.All)
        {
            object? value;
            try
            {
                value = Values.Normalize(definition.Resolve(source));
            }
            catch (Exception ex)
            {
                rows.Add(new BindingRow(definition.Name, definition.Category, definition.Description, "error: " + ex.Message, false, true));
                continue;
            }

            rows.Add(Row(definition.Name, definition.Category, definition.Description, value));
            if (value is IPropertyBag bag) AddMembers(rows, definition, definition.Name, bag, 1);
        }
        return rows;
    }

    private static void AddMembers(List<BindingRow> rows, BindingDefinition definition, string prefix, IPropertyBag bag, int depth)
    {
        if (depth > MaxDepth) return;
        foreach (var name in bag.PropertyNames.OrderBy(n => n, StringComparer.OrdinalIgnoreCase))
        {
            if (!bag.TryGetProperty(name, out var raw)) continue;
            var value = Values.Normalize(raw);
            var path = prefix + "." + name;
            rows.Add(Row(path, definition.Category, definition.Description, value));
            if (value is IPropertyBag child) AddMembers(rows, definition, path, child, depth + 1);
        }
    }

    private static BindingRow Row(string name, string category, string description, object? value) =>
        new(name, category, description, Display(value), value is null, false);

    public static string Display(object? value) => value switch
    {
        null => "null",
        string s => s,
        IEnumerable<object?> list => "[" + string.Join(", ", list.Select(Display)) + "]",
        IPropertyBag and not ValueBag => "{" + string.Join(", ", ((IPropertyBag)value).PropertyNames) + "}",
        double d => d.ToString("0.##########", CultureInfo.InvariantCulture),
        _ => Values.Stringify(value),
    };
}
