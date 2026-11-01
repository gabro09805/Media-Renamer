using System.Globalization;
using System.Text;

namespace MediaRenamer.Bindings.Expressions;

/// <summary>Functions available on values (<c>n.upper()</c>, <c>s.pad(2)</c>, <c>airdate.format('yyyy')</c>) and global ones (<c>max(a, b)</c>).</summary>
public static class BuiltinFunctions
{
    public static readonly IReadOnlySet<string> GlobalNames = new HashSet<string> { "min", "max", "abs", "round" };

    /// <summary>Every method name, regardless of the type it applies to. Used for static validation.</summary>
    public static readonly IReadOnlySet<string> MethodNames = new HashSet<string>
    {
        "upper", "toUpperCase", "lower", "toLowerCase", "upperInitial", "lowerTrail", "capitalize", "space", "replace", "replaceAll",
        "replaceFirst", "removeAll", "match", "matches", "contains", "startsWith", "endsWith", "trim", "substring", "take", "drop",
        "before", "after", "length", "size", "sortName", "ascii", "initials", "split", "pad", "toInteger", "toNumber", "toString",
        "format", "join", "first", "last", "reverse", "unique", "sort", "isEmpty", "abs", "round",
    };

    public static IReadOnlyCollection<string> AllNames => GlobalNames.Concat(MethodNames).Distinct().ToList();

    public static object? CallGlobal(string name, object?[] args, Evaluator ev)
    {
        switch (name)
        {
            case "min" or "max":
            {
                var values = args.Length == 1 && args[0] is IReadOnlyList<object?> l ? l.ToArray() : args;
                var numbers = values.Where(v => v is not null).ToList();
                if (numbers.Count == 0) return null;
                if (numbers.Any(v => !Values.IsNumber(v))) throw new ExpressionException($"{name}() needs numbers.", -1);
                var picked = name == "min" ? numbers.MinBy(v => Values.ToDouble(v!)) : numbers.MaxBy(v => Values.ToDouble(v!));
                return picked;
            }
            case "abs" or "round":
                RequireArgs(name, args, 1, 1);
                return Number(args[0] ?? 0L, name, args);
            default:
                throw new ExpressionException($"Unknown function '{name}'." + Suggestions.For(name, GlobalNames), -1);
        }
    }

    public static object? CallMethod(object target, string name, object?[] args, Evaluator ev)
    {
        switch (target)
        {
            case string s: return StringMethod(s, name, args, ev);
            case long or double: return NumberMethod(target, name, args);
            case DateOnly d: return DateMethod(d, name, args);
            case DateTime dt: return DateMethod(DateOnly.FromDateTime(dt), name, args);
            case IReadOnlyList<object?> list: return ListMethod(list, name, args);
            case bool b when name == "toString": return Values.Stringify(b);
            case RegexMatchValue m when name == "toString": return Values.Stringify(m);
        }

        throw Unsupported(target, name);
    }

    private static ExpressionException Unsupported(object target, string name) =>
        MethodNames.Contains(name)
            ? new ExpressionException($"'{name}()' cannot be used on a {Values.TypeName(target)}.", -1)
            : new ExpressionException($"Unknown function '{name}'." + Suggestions.For(name, MethodNames), -1);

    private static void RequireArgs(string name, object?[] args, int min, int max)
    {
        if (args.Length >= min && args.Length <= max) return;
        var expected = min == max ? min.ToString(CultureInfo.InvariantCulture) : $"{min} to {max}";
        throw new ExpressionException($"{name}() takes {expected} argument(s) but got {args.Length}.", -1);
    }

    private static string Str(object?[] args, int i, string name)
    {
        var v = args[i];
        return v is RegexPattern p ? p.Pattern : v is string s ? s : v is null ? "" : Values.Stringify(v);
    }

    private static int Int(object?[] args, int i, string name) =>
        args[i] is long l ? (int)Math.Clamp(l, int.MinValue, int.MaxValue) : throw new ExpressionException($"{name}() needs a whole number as argument {i + 1}.", -1);

    private static object? Number(object v, string name, object?[] args) => name switch
    {
        "abs" => v is long l ? (object)Math.Abs(l) : Math.Abs((double)v),
        "round" => v is long ? v : (object)(long)Math.Round((double)v, MidpointRounding.AwayFromZero),
        _ => v,
    };

    private static object? NumberMethod(object n, string name, object?[] args)
    {
        switch (name)
        {
            case "pad":
                RequireArgs(name, args, 1, 2);
                return Pad(Values.Stringify(n), Int(args, 0, name), args.Length > 1 ? Str(args, 1, name) : "0");
            case "abs" or "round":
                RequireArgs(name, args, 0, 0);
                return Number(n, name, args);
            case "toString":
                return Values.Stringify(n);
            case "toInteger":
                return n is long ? n : (long)Math.Truncate((double)n);
            default:
                throw Unsupported(n, name);
        }
    }

    private static string Pad(string s, int width, string pad) =>
        pad.Length == 0 ? s : s.PadLeft(width, pad[0]);

    private static object? StringMethod(string s, string name, object?[] args, Evaluator ev)
    {
        switch (name)
        {
            case "upper" or "toUpperCase": return s.ToUpperInvariant();
            case "lower" or "toLowerCase": return s.ToLowerInvariant();
            case "capitalize": return s.Length == 0 ? s : char.ToUpperInvariant(s[0]) + s[1..];
            case "upperInitial": return MapWords(s, w => char.ToUpperInvariant(w[0]) + w[1..]);
            case "lowerTrail": return MapWords(s, w => w[0] + w[1..].ToLowerInvariant());
            case "trim": return s.Trim();
            case "length" or "size": return (long)s.Length;
            case "isEmpty": return s.Length == 0;
            case "toString": return s;
            case "space":
                RequireArgs(name, args, 1, 1);
                return ev.Regex.ReplaceAll(s, @"\s+", Str(args, 0, name).Replace("$", "\\$"));
            case "replace":
                RequireArgs(name, args, 2, 2);
                return Str(args, 0, name).Length == 0 ? s : s.Replace(Str(args, 0, name), Str(args, 1, name), StringComparison.Ordinal);
            case "replaceAll":
                RequireArgs(name, args, 2, 2);
                return ev.Regex.ReplaceAll(s, Str(args, 0, name), Str(args, 1, name));
            case "replaceFirst":
                RequireArgs(name, args, 2, 2);
                return ev.Regex.ReplaceFirst(s, Str(args, 0, name), Str(args, 1, name));
            case "removeAll":
                RequireArgs(name, args, 1, 1);
                return ev.Regex.ReplaceAll(s, Str(args, 0, name), "");
            case "match":
            {
                RequireArgs(name, args, 1, 1);
                var m = ev.Regex.Find(s, Str(args, 0, name));
                if (!m.Success) return null;
                return m.Groups.Count > 1 ? m.Groups[1] : m.Groups[0];
            }
            case "matches":
                RequireArgs(name, args, 1, 1);
                return ev.Regex.IsFullMatch(s, Str(args, 0, name));
            case "split":
                RequireArgs(name, args, 1, 1);
                return ev.Regex.Split(s, Str(args, 0, name)).Cast<object?>().ToList();
            case "contains":
                RequireArgs(name, args, 1, 1);
                return s.Contains(Str(args, 0, name), StringComparison.Ordinal);
            case "startsWith":
                RequireArgs(name, args, 1, 1);
                return s.StartsWith(Str(args, 0, name), StringComparison.Ordinal);
            case "endsWith":
                RequireArgs(name, args, 1, 1);
                return s.EndsWith(Str(args, 0, name), StringComparison.Ordinal);
            case "substring":
            {
                RequireArgs(name, args, 1, 2);
                var from = Math.Clamp(Int(args, 0, name), 0, s.Length);
                var to = args.Length > 1 ? Math.Clamp(Int(args, 1, name), from, s.Length) : s.Length;
                return s[from..to];
            }
            case "take":
                RequireArgs(name, args, 1, 1);
                return s[..Math.Clamp(Int(args, 0, name), 0, s.Length)];
            case "drop":
                RequireArgs(name, args, 1, 1);
                return s[Math.Clamp(Int(args, 0, name), 0, s.Length)..];
            case "before":
            {
                RequireArgs(name, args, 1, 1);
                var i = s.IndexOf(Str(args, 0, name), StringComparison.Ordinal);
                return i < 0 ? s : s[..i];
            }
            case "after":
            {
                RequireArgs(name, args, 1, 1);
                var key = Str(args, 0, name);
                var i = s.IndexOf(key, StringComparison.Ordinal);
                return i < 0 ? s : s[(i + key.Length)..];
            }
            case "sortName": return SortName(s);
            case "ascii": return Ascii(s);
            case "initials": return string.Concat(s.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Select(w => w[0]));
            case "pad":
                RequireArgs(name, args, 1, 2);
                return Pad(s, Int(args, 0, name), args.Length > 1 ? Str(args, 1, name) : "0");
            case "toInteger" or "toNumber":
                if (long.TryParse(s.Trim(), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var l)) return l;
                if (name == "toNumber" && double.TryParse(s.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var d)) return d;
                return null;
            default:
                throw Unsupported(s, name);
        }
    }

    private static string MapWords(string s, Func<string, string> map)
    {
        var sb = new StringBuilder();
        var word = new StringBuilder();
        void Flush()
        {
            if (word.Length > 0) sb.Append(map(word.ToString()));
            word.Clear();
        }

        foreach (var c in s)
        {
            if (char.IsWhiteSpace(c)) { Flush(); sb.Append(c); }
            else word.Append(c);
        }
        Flush();
        return sb.ToString();
    }

    private static readonly string[] Articles = { "The", "A", "An" };

    /// <summary>"The Simpsons" → "Simpsons, The".</summary>
    public static string SortName(string s)
    {
        foreach (var article in Articles)
        {
            if (s.Length > article.Length + 1 && s.StartsWith(article + " ", StringComparison.OrdinalIgnoreCase))
                return s[(article.Length + 1)..].TrimStart() + ", " + s[..article.Length];
        }
        return s;
    }

    public static string Ascii(string s)
    {
        var sb = new StringBuilder();
        foreach (var c in s.Normalize(NormalizationForm.FormD))
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark) continue;
            sb.Append(c switch
            {
                'ß' => "ss", 'æ' => "ae", 'Æ' => "AE", 'ø' => "o", 'Ø' => "O", 'đ' => "d", 'Đ' => "D", 'ł' => "l", 'Ł' => "L", 'œ' => "oe", 'Œ' => "OE",
                _ => c.ToString(),
            });
        }
        return sb.ToString().Normalize(NormalizationForm.FormC);
    }

    private static object? DateMethod(DateOnly d, string name, object?[] args)
    {
        switch (name)
        {
            case "format":
                RequireArgs(name, args, 1, 1);
                try
                {
                    return d.ToString(JavaDatePattern(Str(args, 0, name)), CultureInfo.InvariantCulture);
                }
                catch (FormatException)
                {
                    throw new ExpressionException($"Invalid date format '{Str(args, 0, name)}'. Use patterns such as yyyy-MM-dd.", -1);
                }
            case "toString":
                return Values.Stringify(d);
            default:
                throw Unsupported(d, name);
        }
    }

    /// <summary>Java <c>SimpleDateFormat</c> letters that differ from .NET.</summary>
    private static string JavaDatePattern(string p)
    {
        var sb = new StringBuilder();
        for (var i = 0; i < p.Length; i++)
        {
            var c = p[i];
            if (c == '\'')
            {
                var close = p.IndexOf('\'', i + 1);
                if (close < 0) throw new FormatException();
                sb.Append(p, i, close - i + 1);
                i = close;
            }
            else if (c == 'E')
            {
                var n = 1;
                while (i + 1 < p.Length && p[i + 1] == 'E') { n++; i++; }
                sb.Append(n >= 4 ? "dddd" : "ddd");
            }
            else if (c == 'u') sb.Append('d');
            else sb.Append(c);
        }
        return sb.ToString();
    }

    private static object? ListMethod(IReadOnlyList<object?> list, string name, object?[] args)
    {
        switch (name)
        {
            case "join":
                RequireArgs(name, args, 0, 1);
                return string.Join(args.Length > 0 ? Str(args, 0, name) : ", ", list.Select(Values.Stringify));
            case "size" or "length": return (long)list.Count;
            case "isEmpty": return list.Count == 0;
            case "first": return list.Count > 0 ? list[0] : null;
            case "last": return list.Count > 0 ? list[^1] : null;
            case "reverse": return list.Reverse().ToList();
            case "unique": return list.DistinctBy(Values.Stringify).ToList();
            case "sort": return list.OrderBy(Values.Stringify, StringComparer.OrdinalIgnoreCase).ToList();
            case "take":
                RequireArgs(name, args, 1, 1);
                return list.Take(Math.Max(0, Int(args, 0, name))).ToList();
            case "contains":
                RequireArgs(name, args, 1, 1);
                return list.Any(x => Values.AreEqual(x, args[0]));
            case "toString": return Values.Stringify(list);
            default:
                throw Unsupported(list, name);
        }
    }
}
