namespace MediaRenamer.Bindings.Expressions;

/// <summary>Recursive-descent parser producing an AST from the tokens of one <c>{...}</c> block.</summary>
public sealed class Parser
{
    private readonly IReadOnlyList<Token> _t;
    private int _p;

    private Parser(IReadOnlyList<Token> tokens) => _t = tokens;

    /// <summary>Parses the block that starts at <paramref name="start"/>; <paramref name="end"/> is the index of its closing '}'.</summary>
    public static Program ParseBlock(string src, int start, out int end)
    {
        var tokens = Lexer.Tokenize(src, start, out end);
        if (end >= src.Length) throw new ExpressionException("Missing closing '}'.", Math.Max(0, start - 1), src.Length - Math.Max(0, start - 1));
        return ParseTokens(tokens, start, end);
    }

    internal static Program ParseTokens(IReadOnlyList<Token> tokens, int start, int end)
    {
        var parser = new Parser(tokens);
        var statements = new List<Expr>();
        while (true)
        {
            while (parser.Match(";")) { }
            if (parser.Peek.Kind == TokenKind.End) break;
            statements.Add(parser.Statement());
            if (parser.Peek.Kind != TokenKind.End && !parser.Check(";"))
                throw parser.Unexpected("Expected an operator, ';' or the end of the expression");
        }

        if (statements.Count == 0) throw new ExpressionException("The expression is empty.", Math.Max(0, start - 1), 2);
        return new Program(statements) { Start = start, End = end };
    }

    private Token Peek => _t[_p];
    private Token Next() => _t[_p++];

    private bool Check(string op) => Peek.Kind == TokenKind.Operator && Peek.Text == op;

    private bool Match(string op)
    {
        if (!Check(op)) return false;
        _p++;
        return true;
    }

    private Token Expect(string op)
    {
        if (!Check(op)) throw Unexpected($"Expected '{op}'");
        return Next();
    }

    private ExpressionException Unexpected(string expected)
    {
        var t = Peek;
        var found = t.Kind == TokenKind.End ? "the end of the expression" : $"'{t.Text}'";
        return new ExpressionException($"{expected} but found {found}.", t.Start, t.Length);
    }

    private static T At<T>(T node, int start, int end) where T : Expr => node with { Start = start, End = end };

    private Expr Statement()
    {
        var first = Peek;
        if (first.Kind == TokenKind.Identifier && first.Text == "def" && _t[_p + 1].Kind == TokenKind.Identifier)
        {
            _p++;
            var name = Next();
            Expect("=");
            var value = Ternary();
            return At(new AssignExpr(name.Text, value, true), first.Start, value.End);
        }

        if (first.Kind == TokenKind.Identifier && _t[_p + 1] is { Kind: TokenKind.Operator, Text: "=" })
        {
            _p += 2;
            var value = Ternary();
            return At(new AssignExpr(first.Text, value, false), first.Start, value.End);
        }

        return Ternary();
    }

    private Expr Ternary()
    {
        var cond = Or();
        if (Check("?:"))
        {
            _p++;
            var right = Ternary();
            return At(new ElvisExpr(cond, right), cond.Start, right.End);
        }

        if (Check("?"))
        {
            _p++;
            var a = Ternary();
            Expect(":");
            var b = Ternary();
            return At(new TernaryExpr(cond, a, b), cond.Start, b.End);
        }

        return cond;
    }

    private Expr Or()
    {
        var left = And();
        while (Check("||"))
        {
            _p++;
            var r = And();
            left = At(new BinaryExpr("||", left, r), left.Start, r.End);
        }
        return left;
    }

    private Expr And()
    {
        var left = Equality();
        while (Check("&&"))
        {
            _p++;
            var r = Equality();
            left = At(new BinaryExpr("&&", left, r), left.Start, r.End);
        }
        return left;
    }

    private Expr Equality()
    {
        var left = Relational();
        while (Peek.Kind == TokenKind.Operator && Peek.Text is "==" or "!=" or "=~" or "==~")
        {
            var op = Next().Text;
            var r = Relational();
            left = At(new BinaryExpr(op, left, r), left.Start, r.End);
        }
        return left;
    }

    private Expr Relational()
    {
        var left = Additive();
        while (Peek.Kind == TokenKind.Operator && Peek.Text is "<" or "<=" or ">" or ">=")
        {
            var op = Next().Text;
            var r = Additive();
            left = At(new BinaryExpr(op, left, r), left.Start, r.End);
        }
        return left;
    }

    private Expr Additive()
    {
        var left = Multiplicative();
        while (Peek.Kind == TokenKind.Operator && Peek.Text is "+" or "-")
        {
            var op = Next().Text;
            var r = Multiplicative();
            left = At(new BinaryExpr(op, left, r), left.Start, r.End);
        }
        return left;
    }

    private Expr Multiplicative()
    {
        var left = Unary();
        while (Peek.Kind == TokenKind.Operator && Peek.Text is "*" or "/" or "%")
        {
            var op = Next().Text;
            var r = Unary();
            left = At(new BinaryExpr(op, left, r), left.Start, r.End);
        }
        return left;
    }

    private Expr Unary()
    {
        if (Peek.Kind == TokenKind.Operator && Peek.Text is "!" or "-")
        {
            var op = Next();
            var operand = Unary();
            return At(new UnaryExpr(op.Text, operand), op.Start, operand.End);
        }
        return Postfix();
    }

    private Expr Postfix()
    {
        var e = Primary();
        while (true)
        {
            if (Check(".") || Check("?."))
            {
                var nullSafe = Next().Text == "?.";
                if (Peek.Kind != TokenKind.Identifier) throw Unexpected("Expected a property or function name after '.'");
                var name = Next();
                if (Check("("))
                {
                    var (args, close) = Arguments();
                    e = At(new CallExpr(e, name.Text, args, nullSafe) { NameStart = name.Start }, e.Start, close);
                }
                else e = At(new MemberExpr(e, name.Text, nullSafe), e.Start, name.End);
            }
            else if (Check("["))
            {
                _p++;
                var index = Ternary();
                var close = Expect("]");
                e = At(new IndexExpr(e, index), e.Start, close.End);
            }
            else return e;
        }
    }

    private (List<Expr> Args, int End) Arguments()
    {
        Expect("(");
        var args = new List<Expr>();
        if (!Check(")"))
        {
            do args.Add(Ternary());
            while (Match(","));
        }
        var close = Expect(")");
        return (args, close.End);
    }

    private Expr Primary()
    {
        var t = Peek;
        switch (t.Kind)
        {
            case TokenKind.Number:
                _p++;
                return At(new LiteralExpr(t.Value), t.Start, t.End);
            case TokenKind.String:
                _p++;
                return At(new LiteralExpr(t.Value), t.Start, t.End);
            case TokenKind.Regex:
                _p++;
                return At(new RegexExpr((string)t.Value!), t.Start, t.End);
            case TokenKind.Interpolated:
                _p++;
                return At(new InterpolatedExpr(InterpolationParts(t)), t.Start, t.End);
            case TokenKind.Identifier:
                _p++;
                switch (t.Text)
                {
                    case "true": return At(new LiteralExpr(true), t.Start, t.End);
                    case "false": return At(new LiteralExpr(false), t.Start, t.End);
                    case "null": return At(new LiteralExpr(null), t.Start, t.End);
                }
                if (Check("("))
                {
                    var (args, close) = Arguments();
                    return At(new CallExpr(null, t.Text, args, false) { NameStart = t.Start }, t.Start, close);
                }
                return At(new NameExpr(t.Text), t.Start, t.End);
            case TokenKind.Operator when t.Text == "(":
            {
                _p++;
                var inner = Ternary();
                var close = Expect(")");
                return At(inner, t.Start, close.End);
            }
            case TokenKind.Operator when t.Text == "[":
            {
                _p++;
                var items = new List<Expr>();
                if (!Check("]"))
                {
                    do items.Add(Ternary());
                    while (Match(","));
                }
                var close = Expect("]");
                return At(new ListExpr(items), t.Start, close.End);
            }
            default:
                throw Unexpected("Expected a value (binding, text, number or function)");
        }
    }

    private static List<object> InterpolationParts(Token t)
    {
        var result = new List<object>();
        foreach (var part in (List<InterpolationPart>)t.Value!)
        {
            if (part.Literal is not null) { result.Add(part.Literal); continue; }
            var tokens = part.Tokens!;
            var program = ParseTokens(tokens, tokens[0].Start, tokens[^1].Start);
            result.Add(program.Statements.Count == 1
                ? program.Statements[0]
                : throw new ExpressionException("Only one expression is allowed inside \"${...}\".", program.Start, program.End - program.Start));
        }
        return result;
    }
}
