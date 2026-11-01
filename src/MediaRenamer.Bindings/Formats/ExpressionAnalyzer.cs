using MediaRenamer.Bindings.Expressions;

namespace MediaRenamer.Bindings.Formats;

/// <summary>Static checks on a parsed block: unknown bindings, unknown functions.</summary>
public static class ExpressionAnalyzer
{
    public static void Analyze(Program program, IEnumerable<string> bindingNames, List<ExpressionException> errors)
    {
        var known = new HashSet<string>(bindingNames, StringComparer.Ordinal);
        var defined = new HashSet<string>(StringComparer.Ordinal);
        foreach (var statement in program.Statements) Check(statement, known, defined, errors);
    }

    private static void Check(Expr e, HashSet<string> known, HashSet<string> defined, List<ExpressionException> errors)
    {
        switch (e)
        {
            case NameExpr n when !defined.Contains(n.Name) && !known.Contains(n.Name):
                errors.Add(new ExpressionException("Unknown binding '" + n.Name + "'." + Suggestions.For(n.Name, known.Concat(defined)), n.Start, n.End - n.Start));
                return;
            case AssignExpr a:
                Check(a.Value, known, defined, errors);
                defined.Add(a.Name);
                return;
            case CallExpr c:
            {
                if (c.Target is not null) Check(c.Target, known, defined, errors);
                var names = c.Target is null ? BuiltinFunctions.GlobalNames : BuiltinFunctions.MethodNames;
                if (!names.Contains(c.Name))
                    errors.Add(new ExpressionException("Unknown function '" + c.Name + "'." + Suggestions.For(c.Name, names), c.NameStart, c.Name.Length));
                foreach (var arg in c.Args) Check(arg, known, defined, errors);
                return;
            }
        }

        foreach (var child in Children(e)) Check(child, known, defined, errors);
    }

    public static IEnumerable<Expr> Walk(Program program)
    {
        var stack = new Stack<Expr>(program.Statements.Reverse());
        while (stack.Count > 0)
        {
            var e = stack.Pop();
            yield return e;
            foreach (var child in Children(e).Reverse()) stack.Push(child);
        }
    }

    private static IEnumerable<Expr> Children(Expr e) => e switch
    {
        InterpolatedExpr i => i.Parts.OfType<Expr>(),
        ListExpr l => l.Items,
        MemberExpr m => new[] { m.Target },
        CallExpr c => (c.Target is null ? Enumerable.Empty<Expr>() : new[] { c.Target }).Concat(c.Args),
        IndexExpr ix => new[] { ix.Target, ix.Index },
        UnaryExpr u => new[] { u.Operand },
        BinaryExpr b => new[] { b.Left, b.Right },
        TernaryExpr t => new[] { t.Condition, t.Then, t.Else },
        ElvisExpr el => new[] { el.Left, el.Right },
        AssignExpr a => new[] { a.Value },
        _ => Enumerable.Empty<Expr>(),
    };
}
