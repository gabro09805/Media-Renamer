using System.Globalization;

namespace MediaRenamer.Bindings.Expressions;

/// <summary>Evaluates a parsed block against a <see cref="IBindingSource"/>. One instance per rendering (it holds the local variables).</summary>
public sealed class Evaluator
{
    private readonly IBindingSource _source;
    private readonly Dictionary<string, object?> _variables = new();

    public Evaluator(IBindingSource source, RegexEngine? regex = null)
    {
        _source = source;
        Regex = regex ?? RegexEngine.Default;
    }

    public RegexEngine Regex { get; }

    public object? Run(Program program)
    {
        object? last = null;
        foreach (var statement in program.Statements) last = Eval(statement);
        return last;
    }

    private object? Eval(Expr e)
    {
        try
        {
            return EvalCore(e);
        }
        catch (ExpressionException ex) when (ex.Start < 0)
        {
            throw new ExpressionException(ex.Message, e.Start, e.End - e.Start);
        }
    }

    private object? EvalCore(Expr e)
    {
        switch (e)
        {
            case LiteralExpr l:
                return l.Value;
            case RegexExpr r:
                return new RegexPattern(r.Pattern);
            case InterpolatedExpr i:
            {
                var sb = new System.Text.StringBuilder();
                foreach (var part in i.Parts) sb.Append(part is string s ? s : Values.Stringify(Eval((Expr)part)));
                return sb.ToString();
            }
            case NameExpr n:
                return Lookup(n);
            case ListExpr l:
                return l.Items.Select(Eval).ToList();
            case MemberExpr m:
                return Member(m);
            case CallExpr c:
                return Call(c);
            case IndexExpr ix:
                return Index(ix);
            case UnaryExpr u:
                return Unary(u);
            case BinaryExpr b:
                return Binary(b);
            case TernaryExpr t:
                return Eval(Values.Truthy(Eval(t.Condition)) ? t.Then : t.Else);
            case ElvisExpr el:
            {
                var left = Eval(el.Left);
                return Values.Truthy(left) ? left : Eval(el.Right);
            }
            case AssignExpr a:
            {
                var value = Eval(a.Value);
                _variables[a.Name] = value;
                return value;
            }
            default:
                throw new ExpressionException("Unsupported expression.", e.Start, e.End - e.Start);
        }
    }

    private object? Lookup(NameExpr n)
    {
        if (_variables.TryGetValue(n.Name, out var local)) return local;
        if (_source.TryGet(n.Name, out var value)) return Values.Normalize(value);

        throw new ExpressionException(
            "Unknown binding '" + n.Name + "'." + Suggestions.For(n.Name, _source.Names.Concat(_variables.Keys)),
            n.Start, n.End - n.Start);
    }

    private object? Member(MemberExpr m)
    {
        var target = Eval(m.Target);
        if (target is null) return null;
        var value = Properties.Get(target, m.Name, out var found);
        if (found) return Values.Normalize(value);

        var hint = target is IPropertyBag bag ? Suggestions.For(m.Name, bag.PropertyNames) : "";
        throw new ExpressionException($"Cannot read '{m.Name}' of a {Values.TypeName(target)}." + hint, m.End - m.Name.Length, m.Name.Length);
    }

    private object? Index(IndexExpr ix)
    {
        var target = Eval(ix.Target);
        var index = Eval(ix.Index);
        if (target is null) return null;

        switch (target)
        {
            case string s when index is long si:
                return s.Length == 0 ? null : s[(int)Wrap(si, s.Length)].ToString();
            case IReadOnlyList<object?> list when index is long li:
                return list.Count == 0 ? null : list[(int)Wrap(li, list.Count)];
            case RegexMatchValue match when index is long gi:
                return gi >= 0 && gi < match.Groups.Count ? match.Groups[(int)gi] : null;
            case RegexMatchValue match:
                return match.Named.GetValueOrDefault(Values.Stringify(index));
            case IPropertyBag bag:
                return bag.TryGetProperty(Values.Stringify(index), out var v) ? Values.Normalize(v) : null;
            default:
                throw new ExpressionException($"Cannot index a {Values.TypeName(target)} with a {Values.TypeName(index)}.", ix.Start, ix.End - ix.Start);
        }
    }

    private static long Wrap(long i, int length)
    {
        var w = i < 0 ? length + i : i;
        return Math.Clamp(w, 0, length - 1);
    }

    private object? Call(CallExpr c)
    {
        object? target = null;
        if (c.Target is not null)
        {
            target = Eval(c.Target);
            if (target is null) return null;
            if (target is ValueBag text) target = text.ToString();
        }

        var args = c.Args.Select(Eval).ToArray();
        try
        {
            return c.Target is null
                ? BuiltinFunctions.CallGlobal(c.Name, args, this)
                : BuiltinFunctions.CallMethod(target!, c.Name, args, this);
        }
        catch (ExpressionException ex) when (ex.Start < 0)
        {
            throw new ExpressionException(ex.Message, c.NameStart, c.End - c.NameStart);
        }
    }

    private object? Unary(UnaryExpr u)
    {
        var v = Eval(u.Operand);
        if (u.Op == "!") return !Values.Truthy(v);
        if (v is null) return null;
        return v switch
        {
            long l => (object)(-l),
            double d => -d,
            _ => throw new ExpressionException($"Cannot negate a {Values.TypeName(v)}.", u.Start, u.End - u.Start),
        };
    }

    private object? Binary(BinaryExpr b)
    {
        if (b.Op == "&&") return Values.Truthy(Eval(b.Left)) && Values.Truthy(Eval(b.Right));
        if (b.Op == "||") return Values.Truthy(Eval(b.Left)) || Values.Truthy(Eval(b.Right));

        var l = Eval(b.Left);
        var r = Eval(b.Right);
        switch (b.Op)
        {
            case "==": return Values.AreEqual(l, r);
            case "!=": return !Values.AreEqual(l, r);
            case "<": return Values.Compare(l, r) is { } c1 && c1 < 0;
            case "<=": return Values.Compare(l, r) is { } c2 && c2 <= 0;
            case ">": return Values.Compare(l, r) is { } c3 && c3 > 0;
            case ">=": return Values.Compare(l, r) is { } c4 && c4 >= 0;
            case "=~": return RegexFind(l, r, b);
            case "==~": return Regex.IsFullMatch(Values.Stringify(l), PatternOf(r, b));
            case "+": return Add(l, r, b);
            default: return Arithmetic(b.Op, l, r, b);
        }
    }

    private object RegexFind(object? input, object? pattern, BinaryExpr b)
    {
        try
        {
            return Regex.Find(Values.Stringify(input), PatternOf(pattern, b));
        }
        catch (ExpressionException ex) when (ex.Start < 0)
        {
            throw new ExpressionException(ex.Message, b.Start, b.End - b.Start);
        }
    }

    private static string PatternOf(object? pattern, BinaryExpr b) => pattern switch
    {
        RegexPattern p => p.Pattern,
        string s => s,
        _ => throw new ExpressionException("The right side of a regex match must be a /pattern/ or a text.", b.Right.Start, b.Right.End - b.Right.Start),
    };

    private static object? Add(object? l, object? r, BinaryExpr b)
    {
        if (Values.IsNumber(l) && Values.IsNumber(r)) return Arithmetic("+", l, r, b);
        if (l is IReadOnlyList<object?> la && r is IReadOnlyList<object?> ra) return la.Concat(ra).ToList();
        return Values.Stringify(l) + Values.Stringify(r);
    }

    private static object? Arithmetic(string op, object? l, object? r, BinaryExpr b)
    {
        if (l is null || r is null) return null;
        if (!Values.IsNumber(l) || !Values.IsNumber(r))
            throw new ExpressionException($"Operator '{op}' needs two numbers but got a {Values.TypeName(l)} and a {Values.TypeName(r)}.", b.Start, b.End - b.Start);

        if (l is long a && r is long c)
        {
            switch (op)
            {
                case "+": return a + c;
                case "-": return a - c;
                case "*": return a * c;
                case "/":
                    if (c == 0) throw new ExpressionException("Division by zero.", b.Start, b.End - b.Start);
                    return a % c == 0 ? (object)(a / c) : (double)a / c;
                case "%":
                    if (c == 0) throw new ExpressionException("Division by zero.", b.Start, b.End - b.Start);
                    return a % c;
            }
        }

        var x = Values.ToDouble(l);
        var y = Values.ToDouble(r);
        return op switch
        {
            "+" => x + y,
            "-" => x - y,
            "*" => x * y,
            "/" when y == 0 => throw new ExpressionException("Division by zero.", b.Start, b.End - b.Start),
            "/" => x / y,
            "%" when y == 0 => throw new ExpressionException("Division by zero.", b.Start, b.End - b.Start),
            _ => x % y,
        };
    }
}

/// <summary>A <c>/pattern/</c> literal. Kept distinct from text so that <c>=~</c> and <c>replaceAll</c> can tell them apart.</summary>
public sealed record RegexPattern(string Pattern)
{
    public override string ToString() => Pattern;
}

/// <summary>Reads <c>obj.name</c> for the value types of the language.</summary>
internal static class Properties
{
    public static object? Get(object target, string name, out bool found)
    {
        found = true;
        switch (target)
        {
            case IPropertyBag bag:
                if (bag.TryGetProperty(name, out var v)) return v;
                break;
            case DateOnly d:
                switch (name)
                {
                    case "year": return (long)d.Year;
                    case "month": return (long)d.Month;
                    case "day": return (long)d.Day;
                }
                break;
            case string s when name is "length" or "size":
                return (long)s.Length;
            case IReadOnlyList<object?> list:
                switch (name)
                {
                    case "size" or "length": return (long)list.Count;
                    case "first": return list.Count > 0 ? list[0] : null;
                    case "last": return list.Count > 0 ? list[^1] : null;
                }
                break;
            case RegexMatchValue m when name == "success":
                return m.Success;
        }

        found = false;
        return null;
    }
}

/// <summary>"Did you mean ...?" hints for unknown names.</summary>
public static class Suggestions
{
    public static string For(string name, IEnumerable<string> candidates)
    {
        var best = candidates
            .Select(c => (Name: c, Distance: Levenshtein(name.ToLowerInvariant(), c.ToLowerInvariant())))
            .OrderBy(x => x.Distance)
            .FirstOrDefault();
        return best.Name is not null && best.Distance <= Math.Max(1, name.Length / 3) ? $" Did you mean '{best.Name}'?" : "";
    }

    private static int Levenshtein(string a, string b)
    {
        var d = new int[a.Length + 1, b.Length + 1];
        for (var i = 0; i <= a.Length; i++) d[i, 0] = i;
        for (var j = 0; j <= b.Length; j++) d[0, j] = j;
        for (var i = 1; i <= a.Length; i++)
            for (var j = 1; j <= b.Length; j++)
                d[i, j] = Math.Min(Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1), d[i - 1, j - 1] + (a[i - 1] == b[j - 1] ? 0 : 1));
        return d[a.Length, b.Length];
    }
}
