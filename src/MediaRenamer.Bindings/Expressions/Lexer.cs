using System.Globalization;

namespace MediaRenamer.Bindings.Expressions;

public enum TokenKind { Number, String, Interpolated, Regex, Identifier, Operator, End }

/// <summary>Part of a double-quoted string: either literal text or an embedded expression (<c>$name</c> / <c>${...}</c>).</summary>
public sealed record InterpolationPart(string? Literal, IReadOnlyList<Token>? Tokens);

public sealed record Token(TokenKind Kind, string Text, int Start, int Length)
{
    public object? Value { get; init; }
    public int End => Start + Length;
}

/// <summary>
/// Tokenizer of the expression language (a Groovy-like subset, as used by FileBot formats).
/// It stops at the first <c>}</c>, which closes the <c>{...}</c> block of a format.
/// </summary>
public static class Lexer
{
    private static readonly string[] Operators =
    {
        "==~", "?.", "?:", "==", "!=", "<=", ">=", "&&", "||", "=~",
        "+", "-", "*", "/", "%", "<", ">", "!", "?", ":", "(", ")", "[", "]", ",", ".", ";", "=",
    };

    /// <summary>Tokenizes from <paramref name="start"/>; <paramref name="end"/> is the index of the closing '}' or the text length.</summary>
    public static List<Token> Tokenize(string src, int start, out int end)
    {
        var tokens = new List<Token>();
        var i = start;
        while (true)
        {
            while (i < src.Length && char.IsWhiteSpace(src[i])) i++;
            if (i >= src.Length || src[i] == '}') break;

            var c = src[i];
            if (char.IsDigit(c)) tokens.Add(ReadNumber(src, ref i));
            else if (char.IsLetter(c) || c == '_') tokens.Add(ReadIdentifier(src, ref i));
            else if (c == '\'') tokens.Add(ReadSingleQuoted(src, ref i));
            else if (c == '"') tokens.Add(ReadInterpolated(src, ref i));
            else if (c == '{') throw new ExpressionException("Unexpected '{': braces cannot be nested here. Use \"${...}\" inside a quoted string to embed an expression.", i);
            else if (c == '/' && !EndsOperand(tokens)) tokens.Add(ReadRegex(src, ref i));
            else tokens.Add(ReadOperator(src, ref i));
        }

        end = i;
        tokens.Add(new Token(TokenKind.End, "", i, 0));
        return tokens;
    }

    private static bool EndsOperand(List<Token> tokens)
    {
        if (tokens.Count == 0) return false;
        var t = tokens[^1];
        return t.Kind is TokenKind.Number or TokenKind.String or TokenKind.Interpolated or TokenKind.Regex or TokenKind.Identifier
               || (t.Kind == TokenKind.Operator && t.Text is ")" or "]");
    }

    private static Token ReadNumber(string src, ref int i)
    {
        var s = i;
        while (i < src.Length && char.IsDigit(src[i])) i++;
        var isDouble = false;
        if (i + 1 < src.Length && src[i] == '.' && char.IsDigit(src[i + 1]))
        {
            isDouble = true;
            i++;
            while (i < src.Length && char.IsDigit(src[i])) i++;
        }

        var text = src[s..i];
        object value;
        if (!isDouble && long.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var l)) value = l;
        else value = double.Parse(text, CultureInfo.InvariantCulture);
        return new Token(TokenKind.Number, text, s, i - s) { Value = value };
    }

    private static Token ReadIdentifier(string src, ref int i)
    {
        var s = i;
        while (i < src.Length && (char.IsLetterOrDigit(src[i]) || src[i] == '_')) i++;
        return new Token(TokenKind.Identifier, src[s..i], s, i - s);
    }

    private static char Unescape(char c) => c switch { 'n' => '\n', 't' => '\t', 'r' => '\r', _ => c };

    private static Token ReadSingleQuoted(string src, ref int i)
    {
        var s = i++;
        var sb = new System.Text.StringBuilder();
        while (i < src.Length)
        {
            var c = src[i];
            if (c == '\\' && i + 1 < src.Length) { sb.Append(Unescape(src[i + 1])); i += 2; continue; }
            if (c == '\'')
            {
                i++;
                return new Token(TokenKind.String, src[s..i], s, i - s) { Value = sb.ToString() };
            }
            sb.Append(c);
            i++;
        }
        throw new ExpressionException("Unterminated string: missing closing '.", s, src.Length - s);
    }

    private static Token ReadInterpolated(string src, ref int i)
    {
        var s = i++;
        var parts = new List<InterpolationPart>();
        var sb = new System.Text.StringBuilder();

        void FlushLiteral()
        {
            if (sb.Length == 0) return;
            parts.Add(new InterpolationPart(sb.ToString(), null));
            sb.Clear();
        }

        while (i < src.Length)
        {
            var c = src[i];
            if (c == '\\' && i + 1 < src.Length) { sb.Append(Unescape(src[i + 1])); i += 2; continue; }
            if (c == '"')
            {
                i++;
                FlushLiteral();
                return new Token(TokenKind.Interpolated, src[s..i], s, i - s) { Value = parts };
            }
            if (c == '$' && i + 1 < src.Length && src[i + 1] == '{')
            {
                FlushLiteral();
                var inner = Tokenize(src, i + 2, out var close);
                if (close >= src.Length) throw new ExpressionException("Missing closing '}' in \"${...}\".", i, src.Length - i);
                parts.Add(new InterpolationPart(null, inner));
                i = close + 1;
                continue;
            }
            if (c == '$' && i + 1 < src.Length && (char.IsLetter(src[i + 1]) || src[i + 1] == '_'))
            {
                FlushLiteral();
                var p = i + 1;
                var id = ReadIdentifier(src, ref p);
                parts.Add(new InterpolationPart(null, new List<Token> { id, new Token(TokenKind.End, "", p, 0) }));
                i = p;
                continue;
            }
            sb.Append(c);
            i++;
        }
        throw new ExpressionException("Unterminated string: missing closing \".", s, src.Length - s);
    }

    private static Token ReadRegex(string src, ref int i)
    {
        var s = i++;
        var sb = new System.Text.StringBuilder();
        while (i < src.Length)
        {
            var c = src[i];
            if (c == '\\' && i + 1 < src.Length)
            {
                if (src[i + 1] == '/') sb.Append('/');
                else sb.Append(c).Append(src[i + 1]);
                i += 2;
                continue;
            }
            if (c == '/')
            {
                i++;
                return new Token(TokenKind.Regex, src[s..i], s, i - s) { Value = sb.ToString() };
            }
            sb.Append(c);
            i++;
        }
        throw new ExpressionException("Unterminated regular expression: missing closing /.", s, src.Length - s);
    }

    private static Token ReadOperator(string src, ref int i)
    {
        foreach (var op in Operators)
        {
            if (string.CompareOrdinal(src, i, op, 0, op.Length) != 0) continue;
            var t = new Token(TokenKind.Operator, op, i, op.Length);
            i += op.Length;
            return t;
        }
        throw new ExpressionException($"Unexpected character '{src[i]}'.", i);
    }
}
