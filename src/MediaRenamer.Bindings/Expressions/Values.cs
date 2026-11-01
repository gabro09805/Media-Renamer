using System.Globalization;

namespace MediaRenamer.Bindings.Expressions;

/// <summary>Where identifiers such as <c>n</c> or <c>s00e00</c> come from. Implemented once, over the Canonical Media Model.</summary>
public interface IBindingSource
{
    IReadOnlyCollection<string> Names { get; }

    /// <summary>False when the name is not a binding. A known binding without a value for this media returns true with a null value.</summary>
    bool TryGet(string name, out object? value);
}

/// <summary>Object whose members are read with <c>obj.name</c> or <c>obj['name']</c> (metadata maps, <c>localize</c>).</summary>
public interface IPropertyBag
{
    IReadOnlyCollection<string> PropertyNames { get; }
    bool TryGetProperty(string name, out object? value);
}

public sealed class DictionaryBag : IPropertyBag
{
    private readonly IReadOnlyDictionary<string, object?> _values;

    public DictionaryBag(IReadOnlyDictionary<string, object?> values) =>
        _values = new Dictionary<string, object?>(values, StringComparer.OrdinalIgnoreCase);

    public IReadOnlyCollection<string> PropertyNames => _values.Keys.ToList();
    public bool TryGetProperty(string name, out object? value) => _values.TryGetValue(name, out value);
    public override string ToString() => string.Join(", ", _values.Select(kv => kv.Key + "=" + Values.Stringify(kv.Value)));
}

/// <summary>Marks a value that is a relative path: its separators stay folder separators when it is used in a format.</summary>
public interface IPathValue { }

/// <summary>A value that prints as text but also exposes members, like <c>lang</c> / <c>lang.ISO2</c> or <c>plex</c> / <c>plex.name</c>.</summary>
public sealed class ValueBag : IPropertyBag, IPathValue
{
    private readonly string _text;
    private readonly IReadOnlyDictionary<string, object?> _values;

    public ValueBag(string text, IReadOnlyDictionary<string, object?>? values = null, bool isPath = false)
    {
        _text = text;
        IsPath = isPath;
        _values = new Dictionary<string, object?>(values ?? new Dictionary<string, object?>(), StringComparer.OrdinalIgnoreCase);
    }

    public bool IsPath { get; }
    public IReadOnlyCollection<string> PropertyNames => _values.Keys.ToList();
    public bool TryGetProperty(string name, out object? value) => _values.TryGetValue(name, out value);
    public override string ToString() => _text;
}

/// <summary>Dynamic-value helpers shared by the evaluator and the built-in functions.</summary>
public static class Values
{
    public static bool IsNumber(object? v) => v is long or double;

    public static double ToDouble(object v) => v is long l ? l : (double)v;

    /// <summary>Groovy truth: null, false, 0, "" and empty lists are false.</summary>
    public static bool Truthy(object? v) => v switch
    {
        null => false,
        bool b => b,
        long l => l != 0,
        double d => d != 0,
        string s => s.Length > 0,
        System.Collections.ICollection c => c.Count > 0,
        RegexMatchValue m => m.Success,
        _ => true,
    };

    public static string Stringify(object? v) => v switch
    {
        null => "",
        string s => s,
        bool b => b ? "true" : "false",
        long l => l.ToString(CultureInfo.InvariantCulture),
        double d => d.ToString("0.##########", CultureInfo.InvariantCulture),
        DateOnly d => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        DateTime d => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        IEnumerable<object?> list => string.Join(", ", list.Select(Stringify)),
        RegexMatchValue m => m.Success ? m.Groups[0] ?? "" : "",
        _ => v.ToString() ?? "",
    };

    public static string TypeName(object? v) => v switch
    {
        null => "null",
        string => "text",
        bool => "boolean",
        long or double => "number",
        DateOnly or DateTime => "date",
        IEnumerable<object?> => "list",
        RegexMatchValue => "regex match",
        IPropertyBag => "object",
        _ => v.GetType().Name,
    };

    /// <summary>Normalizes CLR values coming from the model (int, int?, IReadOnlyList&lt;string&gt;, ...) to the language's value types.</summary>
    public static object? Normalize(object? v) => v switch
    {
        int i => (long)i,
        float f => (double)f,
        IEnumerable<string> strings when v is not string => strings.Cast<object?>().ToList(),
        _ => v,
    };

    public static bool AreEqual(object? a, object? b)
    {
        if (a is null || b is null) return a is null && b is null;
        if (a is ValueBag va) a = va.ToString();
        if (b is ValueBag vb) b = vb.ToString();
        if (IsNumber(a) && IsNumber(b)) return ToDouble(a) == ToDouble(b);
        if (a is string sa && b is string sb) return string.Equals(sa, sb, StringComparison.Ordinal);
        if (a is DateTime da) a = DateOnly.FromDateTime(da);
        if (b is DateTime db) b = DateOnly.FromDateTime(db);
        return a.Equals(b);
    }

    public static int? Compare(object? a, object? b)
    {
        if (a is null || b is null) return null;
        if (IsNumber(a) && IsNumber(b)) return ToDouble(a).CompareTo(ToDouble(b));
        if (a is string sa && b is string sb) return Math.Sign(string.CompareOrdinal(sa, sb));
        if (a is DateOnly da && b is DateOnly db) return da.CompareTo(db);
        return null;
    }
}
