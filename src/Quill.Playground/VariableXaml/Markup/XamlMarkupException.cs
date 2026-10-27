namespace Quill.Playground.VariableXaml.Markup;

/// <summary>
/// A syntax error in VariableXaml markup, with the position where parsing stopped.
/// </summary>
public sealed class XamlMarkupException : Exception
{
    public XamlMarkupException(string message, int offset, int line, int column)
        : base($"{message} (line {line}, column {column})")
    {
        RawMessage = message;
        Offset = offset;
        Line = line;
        Column = column;
    }

    public string RawMessage { get; }

    public int Offset { get; }

    public int Line { get; }

    public int Column { get; }
}
