namespace Quill.Editor.Markdown.Import;

/// <summary>
/// A position inside one physical line that tracks columns the way CommonMark does (tab stops of 4).
/// <see cref="Column"/> is the precise column, which may lie inside a tab whose character was already
/// assigned to an earlier prefix segment: a partially consumed tab goes, as a whole character, to the
/// segment that started consuming it, and its remaining columns are still available as indentation to
/// the next consumer.
/// </summary>
internal struct LineCursor
{
    private readonly string _text;

    public LineCursor(string text, int start, int end)
    {
        _text = text;
        Position = start;
        End = end;
        Column = 0;
        PositionColumn = 0;
    }

    /// <summary>Index of the next unassigned character.</summary>
    public int Position { get; private set; }

    /// <summary>Exclusive end of the line.</summary>
    public int End { get; }

    /// <summary>The precise column.</summary>
    public int Column { get; private set; }

    /// <summary>The column where the character at <see cref="Position"/> starts (at least <see cref="Column"/>).</summary>
    public int PositionColumn { get; private set; }

    public readonly bool AtEnd => Position >= End;

    public readonly char Current => Position < End ? _text[Position] : '\0';

    /// <summary>Whether everything from here to the end of the line is spaces or tabs.</summary>
    public readonly bool RestIsBlank()
    {
        for (int i = Position; i < End; i++)
        {
            if (_text[i] is not (' ' or '\t'))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Columns of indentation available from here (pending tab columns, spaces and tabs).</summary>
    public readonly int MeasureIndentation()
    {
        var probe = this;
        return probe.ConsumeWhitespace(int.MaxValue);
    }

    /// <summary>Consumes up to <paramref name="maxColumns"/> columns of spaces and tabs; returns the columns consumed.</summary>
    public int ConsumeWhitespace(int maxColumns)
    {
        int start = Column;
        int pending = Math.Min(PositionColumn - Column, maxColumns);
        Column += pending;
        while (Column - start < maxColumns && Position < End)
        {
            char c = _text[Position];
            if (c == ' ')
            {
                Position++;
                PositionColumn++;
                Column = PositionColumn;
            }
            else if (c == '\t')
            {
                int width = 4 - (PositionColumn % 4);
                int need = maxColumns - (Column - start);
                Position++;
                PositionColumn += width;
                Column = width <= need ? PositionColumn : Column + need;
            }
            else
            {
                break;
            }
        }

        return Column - start;
    }

    /// <summary>Consumes any characters until the precise column reaches <paramref name="target"/> or the line ends.</summary>
    public void ConsumeThroughColumn(int target)
    {
        while (Column < target)
        {
            if (PositionColumn > Column)
            {
                Column = Math.Min(PositionColumn, target);
                continue;
            }

            if (Position >= End)
            {
                return;
            }

            char c = _text[Position];
            Position++;
            PositionColumn += c == '\t' ? 4 - (PositionColumn % 4) : 1;
            Column = Math.Min(PositionColumn, target);
        }
    }

    /// <summary>Consumes one character that is not a tab.</summary>
    public void ConsumeChar()
    {
        Position++;
        PositionColumn++;
        Column = PositionColumn;
    }

    /// <summary>Consumes exactly one column, splitting a tab if needed.</summary>
    public void ConsumeOneColumn()
    {
        if (PositionColumn > Column)
        {
            Column++;
            return;
        }

        char c = _text[Position];
        Position++;
        PositionColumn += c == '\t' ? 4 - (PositionColumn % 4) : 1;
        Column++;
    }

    public void ConsumeRest()
    {
        ConsumeThroughColumn(int.MaxValue);
    }
}
