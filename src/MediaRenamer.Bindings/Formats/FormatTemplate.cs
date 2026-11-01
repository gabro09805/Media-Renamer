using MediaRenamer.Bindings.Expressions;

namespace MediaRenamer.Bindings.Formats;

public abstract record TemplateSegment(int Start, int End);

public sealed record LiteralSegment(string Text, int Start, int End) : TemplateSegment(Start, End);

public sealed record ExpressionSegment(Program Program, string Source, int Start, int End) : TemplateSegment(Start, End);

/// <summary>A parsed format: literal text (where '/' separates folders) interleaved with <c>{expression}</c> blocks.</summary>
public sealed class FormatTemplate
{
    private FormatTemplate(string text, IReadOnlyList<TemplateSegment> segments)
    {
        Text = text;
        Segments = segments;
    }

    public string Text { get; }
    public IReadOnlyList<TemplateSegment> Segments { get; }

    /// <exception cref="ExpressionException">Syntax error; its position refers to <paramref name="text"/>.</exception>
    public static FormatTemplate Parse(string text)
    {
        var segments = new List<TemplateSegment>();
        var literal = new System.Text.StringBuilder();
        var literalStart = 0;

        void Flush(int end)
        {
            if (literal.Length > 0) segments.Add(new LiteralSegment(literal.ToString(), literalStart, end));
            literal.Clear();
        }

        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (c == '{')
            {
                Flush(i);
                var program = Parser.ParseBlock(text, i + 1, out var close);
                segments.Add(new ExpressionSegment(program, text[(i + 1)..close], i, close + 1));
                i = close;
                literalStart = i + 1;
            }
            else if (c == '}')
            {
                throw new ExpressionException("Unexpected '}' with no matching '{'.", i);
            }
            else
            {
                if (literal.Length == 0) literalStart = i;
                literal.Append(c);
            }
        }

        Flush(text.Length);
        return new FormatTemplate(text, segments);
    }
}
