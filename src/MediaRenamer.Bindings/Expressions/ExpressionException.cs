namespace MediaRenamer.Bindings.Expressions;

/// <summary>Error in a format expression. Positions are offsets in the full format text, so the editor can underline them.</summary>
public sealed class ExpressionException : Exception
{
    public ExpressionException(string message, int start, int length = 1) : base(message)
    {
        Start = start;
        Length = Math.Max(1, length);
    }

    public int Start { get; }
    public int Length { get; }
}
