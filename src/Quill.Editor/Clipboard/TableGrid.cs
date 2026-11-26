using System.Collections.Immutable;
using System.Text;
using Quill.Editor.Documents;
using Quill.Editor.Transactions;

namespace Quill.Editor.Editing;

/// <summary>
/// Normalized rectangular grid of Markdown table cells, shared as the intermediate representation
/// between clipboard parsers (HTML table, TSV, GFM table slices) and editor paste operations.
/// </summary>
internal sealed record TableGrid
{
    public static TableGrid Empty { get; } = new()
    {
        Rows = ImmutableArray<ImmutableArray<string>>.Empty,
        Alignments = ImmutableArray<TableAlignment>.Empty,
    };

    public required ImmutableArray<ImmutableArray<string>> Rows { get; init; }

    public required ImmutableArray<TableAlignment> Alignments { get; init; }

    /// <summary>Whether the source explicitly marked a header row (e.g. HTML &lt;thead&gt; or &lt;th&gt;).</summary>
    public bool HasHeader { get; init; }

    /// <summary>
    /// Whether this grid came from a body-only slice of an existing Quill table (e.g. Ctrl+L on a body row),
    /// which should overwrite cells directly when pasted back into a table without prepending a synthetic header.
    /// </summary>
    public bool IsBodyOnly { get; init; }

    public int RowCount => Rows.Length;

    public int ColumnCount => Rows.IsEmpty ? Alignments.Length : Rows[0].Length;

    public bool IsEmpty => Rows.IsEmpty || ColumnCount == 0;

    public bool IsSingleCell => RowCount == 1 && ColumnCount == 1;

    /// <summary>
    /// Builds a rectangular <see cref="TableGrid"/> by padding short rows, normalizing cell text for
    /// GFM <see cref="TableCell"/> storage, and aligning the column alignment array.
    /// </summary>
    public static TableGrid Create(
        IReadOnlyList<IReadOnlyList<string>> rows,
        IReadOnlyList<TableAlignment>? alignments = null,
        bool hasHeader = false,
        bool isBodyOnly = false)
    {
        if (rows.Count == 0)
        {
            return Empty;
        }

        int maxCols = 0;
        for (int i = 0; i < rows.Count; i++)
        {
            if (rows[i].Count > maxCols)
            {
                maxCols = rows[i].Count;
            }
        }

        if (maxCols == 0)
        {
            return Empty;
        }

        var normalizedRows = ImmutableArray.CreateBuilder<ImmutableArray<string>>(rows.Count);
        for (int r = 0; r < rows.Count; r++)
        {
            var sourceRow = rows[r];
            var cells = ImmutableArray.CreateBuilder<string>(maxCols);
            for (int c = 0; c < maxCols; c++)
            {
                string raw = c < sourceRow.Count ? sourceRow[c] : string.Empty;
                cells.Add(NormalizeCellText(raw));
            }

            normalizedRows.Add(cells.MoveToImmutable());
        }

        var normalizedAlignments = ImmutableArray.CreateBuilder<TableAlignment>(maxCols);
        for (int c = 0; c < maxCols; c++)
        {
            normalizedAlignments.Add(alignments is not null && c < alignments.Count ? alignments[c] : TableAlignment.None);
        }

        return new TableGrid
        {
            Rows = normalizedRows.MoveToImmutable(),
            Alignments = normalizedAlignments.MoveToImmutable(),
            HasHeader = hasHeader,
            IsBodyOnly = isBodyOnly,
        };
    }

    /// <summary>Creates a grid snapshot from an existing <see cref="Table"/> document block.</summary>
    public static TableGrid FromTable(Table table, bool isBodyOnly = false)
    {
        var rows = ImmutableArray.CreateBuilder<ImmutableArray<string>>(table.Children.Count);
        int cols = table.ColumnCount;
        foreach (var child in table.Children)
        {
            var tableRow = (TableRow)child;
            var cells = ImmutableArray.CreateBuilder<string>(cols);
            for (int c = 0; c < cols; c++)
            {
                string text = c < tableRow.Children.Count ? ((TableCell)tableRow.Children[c]).Text : string.Empty;
                cells.Add(NormalizeCellText(text));
            }

            rows.Add(cells.MoveToImmutable());
        }

        return new TableGrid
        {
            Rows = rows.MoveToImmutable(),
            Alignments = table.Alignments,
            HasHeader = !isBodyOnly,
            IsBodyOnly = isBodyOnly,
        };
    }

    /// <summary>Materializes this grid as a new <see cref="Table"/> block with fresh IDs.</summary>
    public Table ToTable(IBlockIdSource ids)
    {
        if (IsEmpty)
        {
            throw new InvalidOperationException("Cannot create a Table block from an empty TableGrid.");
        }

        var tableRows = ImmutableList.CreateBuilder<Block>();
        for (int r = 0; r < Rows.Length; r++)
        {
            var rowCells = ImmutableList.CreateBuilder<Block>();
            var gridRow = Rows[r];
            for (int c = 0; c < gridRow.Length; c++)
            {
                rowCells.Add(new TableCell
                {
                    Id = ids.NextId(),
                    Text = gridRow[c],
                });
            }

            tableRows.Add(new TableRow
            {
                Id = ids.NextId(),
                Children = rowCells.ToImmutable(),
            });
        }

        return new Table
        {
            Id = ids.NextId(),
            Children = tableRows.ToImmutable(),
            Alignments = Alignments,
        };
    }

    /// <summary>
    /// Normalizes text for a Markdown <see cref="TableCell"/>: normalizes line endings to '\n',
    /// escapes unescaped '|' as '\|', and converts line breaks to '&lt;br&gt;'.
    /// </summary>
    public static string NormalizeCellText(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }

        string normalized = text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
        var builder = new StringBuilder(normalized.Length);
        for (int i = 0; i < normalized.Length; i++)
        {
            char ch = normalized[i];
            if (ch == '\n')
            {
                builder.Append("<br>");
            }
            else if (ch == '|' && !Normalizer.IsEscaped(normalized, i))
            {
                builder.Append("\\|");
            }
            else
            {
                builder.Append(ch);
            }
        }

        return builder.ToString();
    }
}
