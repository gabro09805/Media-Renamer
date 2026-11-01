using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using MediaRenamer.Bindings.Expressions;

namespace MediaRenamer.Bindings;

/// <summary>Result of a regex search: success flag plus numbered and named capture groups.</summary>
public sealed class RegexMatchValue
{
    public static readonly RegexMatchValue Failed = new(false, Array.Empty<string?>(), new Dictionary<string, string?>());

    public RegexMatchValue(bool success, IReadOnlyList<string?> groups, IReadOnlyDictionary<string, string?> named)
    {
        Success = success;
        Groups = groups;
        Named = named;
    }

    public bool Success { get; }

    /// <summary>Group 0 is the whole match, as in Groovy's <c>m[0]</c>.</summary>
    public IReadOnlyList<string?> Groups { get; }

    public IReadOnlyDictionary<string, string?> Named { get; }
}

/// <summary>
/// Single place where patterns are compiled. Accepts the Java/Groovy flavour used by FileBot formats
/// (possessive quantifiers, POSIX classes such as <c>\p{Alpha}</c>, <c>\Q..\E</c>) and protects against runaway patterns with a timeout.
/// </summary>
public sealed class RegexEngine
{
    public static readonly RegexEngine Default = new();

    private static readonly TimeSpan MatchTimeout = TimeSpan.FromSeconds(2);

    private static readonly (string From, string To)[] PosixClasses =
    {
        (@"\p{Alpha}", "a-zA-Z"), (@"\p{Digit}", "0-9"), (@"\p{Alnum}", "a-zA-Z0-9"),
        (@"\p{Upper}", "A-Z"), (@"\p{Lower}", "a-z"), (@"\p{Punct}", @"!-/:-@\[-`{-~"),
        (@"\p{Space}", @"\s"), (@"\p{Blank}", @" \t"), (@"\p{XDigit}", "0-9a-fA-F"),
    };

    private readonly ConcurrentDictionary<string, Regex> _cache = new();

    public Regex Get(string pattern, int errorPosition = -1)
    {
        try
        {
            return _cache.GetOrAdd(pattern, p => new Regex(Translate(p), RegexOptions.CultureInvariant, MatchTimeout));
        }
        catch (ArgumentException ex)
        {
            throw new ExpressionException($"Invalid regular expression /{pattern}/: {ex.Message}", errorPosition, pattern.Length + 2);
        }
    }

    /// <summary>Returns an error message when the pattern is not valid, otherwise null.</summary>
    public string? Validate(string pattern)
    {
        try
        {
            _ = Get(pattern);
            return null;
        }
        catch (ExpressionException ex)
        {
            return ex.Message;
        }
    }

    /// <summary>Converts Java-only syntax to its .NET equivalent.</summary>
    public static string Translate(string pattern)
    {
        var p = Regex.Replace(pattern, @"\\Q(.*?)(\\E|$)", m => Regex.Escape(m.Groups[1].Value), RegexOptions.Singleline);

        foreach (var (from, to) in PosixClasses)
        {
            // Inside a bracket expression the class body is spliced in; outside it becomes its own [..].
            p = Regex.Replace(p, Regex.Escape(from), m => IsInsideBrackets(p, m.Index) ? to : "[" + to + "]");
        }

        // Possessive quantifiers (*+, ++, ?+, {n,m}+) are not supported by .NET: use greedy ones.
        p = Regex.Replace(p, @"(?<!\\)([*+?]|\{\d+(?:,\d*)?\})\+", "$1");
        return p;
    }

    private static bool IsInsideBrackets(string p, int index)
    {
        var depth = 0;
        for (var i = 0; i < index; i++)
        {
            if (p[i] == '\\') { i++; continue; }
            if (p[i] == '[') depth++;
            else if (p[i] == ']' && depth > 0) depth--;
        }
        return depth > 0;
    }

    /// <summary>Converts a Java/Groovy replacement string ('\$' escapes, '$1', '${name}') to .NET syntax.</summary>
    public static string TranslateReplacement(string replacement)
    {
        var sb = new System.Text.StringBuilder();
        for (var i = 0; i < replacement.Length; i++)
        {
            var c = replacement[i];
            if (c == '\\' && i + 1 < replacement.Length)
            {
                var n = replacement[++i];
                sb.Append(n == '$' ? "$$" : n.ToString());
            }
            else sb.Append(c);
        }
        return sb.ToString();
    }

    public string ReplaceAll(string input, string pattern, string replacement) =>
        Guard(() => Get(pattern).Replace(input, TranslateReplacement(replacement)));

    public string ReplaceFirst(string input, string pattern, string replacement) =>
        Guard(() => Get(pattern).Replace(input, TranslateReplacement(replacement), 1));

    public bool IsFullMatch(string input, string pattern) =>
        Guard(() =>
        {
            var m = Get(pattern).Match(input);
            while (m.Success)
            {
                if (m.Index == 0 && m.Length == input.Length) return true;
                m = m.NextMatch();
            }
            return false;
        });

    public RegexMatchValue Find(string input, string pattern) => Guard(() =>
    {
        var regex = Get(pattern);
        var m = regex.Match(input);
        if (!m.Success) return RegexMatchValue.Failed;

        var groups = new List<string?>();
        var named = new Dictionary<string, string?>();
        foreach (Group g in m.Groups)
        {
            var value = g.Success ? g.Value : null;
            groups.Add(value);
            if (!int.TryParse(g.Name, out _)) named[g.Name] = value;
        }
        return new RegexMatchValue(true, groups, named);
    });

    public IReadOnlyList<string> Split(string input, string pattern) => Guard(() => (IReadOnlyList<string>)Get(pattern).Split(input));

    private static T Guard<T>(Func<T> action)
    {
        try
        {
            return action();
        }
        catch (RegexMatchTimeoutException)
        {
            throw new ExpressionException("The regular expression took too long to evaluate; simplify the pattern.", -1);
        }
    }
}
