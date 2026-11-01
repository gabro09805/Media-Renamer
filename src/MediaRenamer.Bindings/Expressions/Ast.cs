namespace MediaRenamer.Bindings.Expressions;

public abstract record Expr
{
    public int Start { get; init; }
    public int End { get; init; }
}

public sealed record LiteralExpr(object? Value) : Expr;

/// <summary>Double-quoted string; parts are <see cref="string"/> literals or embedded <see cref="Expr"/>.</summary>
public sealed record InterpolatedExpr(IReadOnlyList<object> Parts) : Expr;

public sealed record RegexExpr(string Pattern) : Expr;

public sealed record NameExpr(string Name) : Expr;

public sealed record ListExpr(IReadOnlyList<Expr> Items) : Expr;

public sealed record MemberExpr(Expr Target, string Name, bool NullSafe) : Expr;

/// <summary>Method call (<c>x.pad(2)</c>) when <see cref="Target"/> is set, otherwise a global function (<c>max(a, b)</c>).</summary>
public sealed record CallExpr(Expr? Target, string Name, IReadOnlyList<Expr> Args, bool NullSafe) : Expr
{
    public int NameStart { get; init; }
}

public sealed record IndexExpr(Expr Target, Expr Index) : Expr;

public sealed record UnaryExpr(string Op, Expr Operand) : Expr;

public sealed record BinaryExpr(string Op, Expr Left, Expr Right) : Expr;

public sealed record TernaryExpr(Expr Condition, Expr Then, Expr Else) : Expr;

public sealed record ElvisExpr(Expr Left, Expr Right) : Expr;

public sealed record AssignExpr(string Name, Expr Value, bool IsDefinition) : Expr;

/// <summary>A <c>{...}</c> block: statements separated by ';', the value is the last one.</summary>
public sealed record Program(IReadOnlyList<Expr> Statements)
{
    public int Start { get; init; }
    public int End { get; init; }
}
