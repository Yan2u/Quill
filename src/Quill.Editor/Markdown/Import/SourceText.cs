namespace Quill.Editor.Markdown.Import;

/// <summary>
/// Normalized source text ("\n" line breaks only) split into physical lines. Line numbers match
/// Markdig's 0-based line indices.
/// </summary>
internal sealed class SourceText
{
    private readonly int[] _lineStarts;

    public SourceText(string text)
    {
        Text = text;
        EndsWithLineBreak = text.Length > 0 && text[^1] == '\n';
        var starts = new List<int> { 0 };
        int contentEnd = EndsWithLineBreak ? text.Length - 1 : text.Length;
        for (int i = 0; i < contentEnd; i++)
        {
            if (text[i] == '\n')
            {
                starts.Add(i + 1);
            }
        }

        _lineStarts = [.. starts];
    }

    public string Text { get; }

    /// <summary>Whether the text ends with a line break; that break does not start another line.</summary>
    public bool EndsWithLineBreak { get; }

    public int LineCount => _lineStarts.Length;

    public int LineStart(int line) => _lineStarts[line];

    /// <summary>Exclusive end of the line's content (the position of its "\n", or the text end).</summary>
    public int LineEnd(int line)
    {
        if (line + 1 < _lineStarts.Length)
        {
            return _lineStarts[line + 1] - 1;
        }

        return EndsWithLineBreak ? Text.Length - 1 : Text.Length;
    }

    /// <summary>The line containing an offset; a "\n" belongs to the line it ends.</summary>
    public int LineOf(int offset)
    {
        if (offset <= 0)
        {
            return 0;
        }

        int index = Array.BinarySearch(_lineStarts, offset);
        int line = index >= 0 ? index : ~index - 1;
        return Math.Min(line, _lineStarts.Length - 1);
    }

    public string Slice(int start, int end) => Text.Substring(start, end - start);
}
