using MediaRenamer.Bindings.Catalog;
using MediaRenamer.Bindings.Expressions;

namespace MediaRenamer.Bindings.Formats;

public enum SyntaxKind { Text, Separator, Brace, Binding, Function, Variable, Keyword, String, Number, Regex, Operator, Error }

public readonly record struct SyntaxSpan(int Start, int Length, SyntaxKind Kind);

/// <summary>Splits a format into colored spans for the editor. Tolerant: broken input never throws, it is marked as <see cref="SyntaxKind.Error"/>.</summary>
public static class SyntaxHighlighter
{
    private static readonly HashSet<string> Keywords = new() { "def", "true", "false", "null" };

    public static IReadOnlyList<SyntaxSpan> Highlight(string text)
    {
        var spans = new List<SyntaxSpan>();
        var i = 0;
        var runStart = 0;

        void FlushText(int end)
        {
            if (end > runStart) spans.Add(new SyntaxSpan(runStart, end - runStart, SyntaxKind.Text));
        }

        while (i < text.Length)
        {
            var c = text[i];
            if (c is '/' or '\\')
            {
                FlushText(i);
                spans.Add(new SyntaxSpan(i, 1, SyntaxKind.Separator));
                runStart = ++i;
            }
            else if (c == '{')
            {
                FlushText(i);
                spans.Add(new SyntaxSpan(i, 1, SyntaxKind.Brace));
                i = HighlightBlock(text, i + 1, spans);
                runStart = i;
            }
            else if (c == '}')
            {
                FlushText(i);
                spans.Add(new SyntaxSpan(i, 1, SyntaxKind.Error));
                runStart = ++i;
            }
            else i++;
        }

        FlushText(text.Length);
        return spans;
    }

    /// <summary>Adds the spans of one block and returns the index after its closing '}'.</summary>
    private static int HighlightBlock(string text, int start, List<SyntaxSpan> spans)
    {
        List<Token> tokens;
        int end;
        try
        {
            tokens = Lexer.Tokenize(text, start, out end);
        }
        catch (ExpressionException ex)
        {
            var close = text.IndexOf('}', Math.Max(start, ex.Start));
            var stop = close < 0 ? text.Length : close;
            var errorStart = Math.Clamp(ex.Start, start, stop);
            if (errorStart > start) spans.Add(new SyntaxSpan(start, errorStart - start, SyntaxKind.Text));
            if (stop > errorStart) spans.Add(new SyntaxSpan(errorStart, stop - errorStart, SyntaxKind.Error));
            if (close < 0) return text.Length;
            spans.Add(new SyntaxSpan(close, 1, SyntaxKind.Brace));
            return close + 1;
        }

        for (var k = 0; k < tokens.Count; k++)
        {
            var t = tokens[k];
            if (t.Kind == TokenKind.End) break;
            spans.Add(new SyntaxSpan(t.Start, t.Length, KindOf(tokens, k)));
        }

        if (end >= text.Length) return text.Length;
        spans.Add(new SyntaxSpan(end, 1, SyntaxKind.Brace));
        return end + 1;
    }

    private static SyntaxKind KindOf(List<Token> tokens, int k)
    {
        var t = tokens[k];
        switch (t.Kind)
        {
            case TokenKind.Number: return SyntaxKind.Number;
            case TokenKind.String or TokenKind.Interpolated: return SyntaxKind.String;
            case TokenKind.Regex: return SyntaxKind.Regex;
            case TokenKind.Operator: return SyntaxKind.Operator;
            case TokenKind.Identifier:
                if (Keywords.Contains(t.Text)) return SyntaxKind.Keyword;
                if (tokens[k + 1] is { Kind: TokenKind.Operator, Text: "(" }) return SyntaxKind.Function;
                if (k > 0 && tokens[k - 1] is { Kind: TokenKind.Operator, Text: "." or "?." }) return SyntaxKind.Variable;
                return BindingCatalog.TryFind(t.Text, out _) ? SyntaxKind.Binding : SyntaxKind.Variable;
            default: return SyntaxKind.Text;
        }
    }
}
