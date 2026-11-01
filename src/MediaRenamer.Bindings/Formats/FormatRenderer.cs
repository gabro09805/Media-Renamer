using System.Collections.Concurrent;
using MediaRenamer.Bindings.Expressions;

namespace MediaRenamer.Bindings.Formats;

public sealed record RenderResult(
    IReadOnlyList<string> Segments,
    IReadOnlyList<string> Warnings,
    ExpressionException? Error)
{
    public bool Success => Error is null;

    /// <summary>Folders and file name (without extension) joined with '/', or null on error.</summary>
    public string? RelativePath => Success ? string.Join('/', Segments) : null;

    public string? FileName => Success ? Segments[^1] : null;

    public string? FolderPath => Success && Segments.Count > 1 ? string.Join('/', Segments.Take(Segments.Count - 1)) : null;
}

public sealed record ValidationResult(IReadOnlyList<ExpressionException> Errors)
{
    public bool IsValid => Errors.Count == 0;
}

/// <summary>Renders a format against a <see cref="IBindingSource"/>. Stateless apart from a small parse cache, so it can be shared.</summary>
public sealed class FormatRenderer
{
    private const int CacheLimit = 256;

    private readonly RegexEngine _regex;
    private readonly ConcurrentDictionary<string, FormatTemplate> _cache = new();

    public FormatRenderer(RegexEngine? regex = null) => _regex = regex ?? RegexEngine.Default;

    private FormatTemplate Template(string format)
    {
        if (_cache.TryGetValue(format, out var cached)) return cached;
        var parsed = FormatTemplate.Parse(format);
        if (_cache.Count >= CacheLimit) _cache.Clear();
        _cache[format] = parsed;
        return parsed;
    }

    /// <summary>Checks syntax and names without evaluating anything: no media is needed, so it works while the user types.</summary>
    public ValidationResult Validate(string format, IBindingSource names)
    {
        FormatTemplate template;
        try
        {
            template = Template(format);
        }
        catch (ExpressionException ex)
        {
            return new ValidationResult(new[] { ex });
        }

        var errors = new List<ExpressionException>();
        foreach (var segment in template.Segments.OfType<ExpressionSegment>())
        {
            ExpressionAnalyzer.Analyze(segment.Program, names.Names, errors);
            ValidateRegexLiterals(segment.Program, errors);
        }

        return new ValidationResult(errors);
    }

    private void ValidateRegexLiterals(Program program, List<ExpressionException> errors)
    {
        foreach (var expr in ExpressionAnalyzer.Walk(program))
        {
            if (expr is RegexExpr r && _regex.Validate(r.Pattern) is { } message)
                errors.Add(new ExpressionException(message, r.Start, r.End - r.Start));
        }
    }

    public RenderResult Render(string format, IBindingSource source)
    {
        var warnings = new List<string>();
        try
        {
            var template = Template(format);
            var segments = new List<string>();
            var current = new System.Text.StringBuilder();

            void AppendLiteral(string text)
            {
                foreach (var c in text)
                {
                    if (c is '/' or '\\')
                    {
                        segments.Add(current.ToString());
                        current.Clear();
                    }
                    else current.Append(c);
                }
            }

            foreach (var part in template.Segments)
            {
                switch (part)
                {
                    case LiteralSegment literal:
                        AppendLiteral(literal.Text);
                        break;
                    case ExpressionSegment expression:
                    {
                        var result = new Evaluator(source, _regex).Run(expression.Program);
                        var value = Values.Stringify(result);
                        if (string.IsNullOrWhiteSpace(value)) warnings.Add($"{{{expression.Source.Trim()}}} has no value for this file.");
                        if (result is ValueBag { IsPath: true }) AppendLiteral(value);
                        else current.Append(PathSanitizer.Value(value));
                        break;
                    }
                }
            }

            segments.Add(current.ToString());
            var cleaned = segments.Select((s, i) => i == 0 && PathSanitizer.IsDriveLetter(s) ? s.ToUpperInvariant() : PathSanitizer.Segment(s)).Where(s => s.Length > 0).ToList();
            if (cleaned.Count == 0)
                return new RenderResult(Array.Empty<string>(), warnings, new ExpressionException("The format produced an empty name.", 0, Math.Max(1, format.Length)));

            return new RenderResult(cleaned, warnings, null);
        }
        catch (ExpressionException ex)
        {
            return new RenderResult(Array.Empty<string>(), warnings, ex);
        }
    }
}
