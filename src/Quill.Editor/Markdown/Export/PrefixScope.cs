using Quill.Editor.Documents;
using Quill.Editor.Documents.Syntax;

namespace Quill.Editor.Markdown.Export;

/// <summary>
/// A container (or a block's own indentation) that contributes a prefix to every line written inside
/// it. A recorded segment is reused while the line still has it; otherwise the scope generates one.
/// Generated prefixes take their short form only when nothing follows them on the line, so that
/// whitespace-only bodies survive a round trip.
/// </summary>
internal abstract class PrefixScope
{
    protected PrefixScope(BlockId owner)
    {
        Owner = owner;
    }

    public BlockId Owner { get; }

    /// <summary>Whether the first line written inside the scope gets an opening prefix.</summary>
    public virtual bool HasOpening => false;

    /// <param name="stored">The prefixes recorded for the line.</param>
    /// <param name="opening">The line is the first one inside the scope.</param>
    /// <param name="rest">Everything after this scope's prefix: inner prefixes and the body.</param>
    /// <param name="keepIndentation">The line is written with full indentation even when nothing follows it (an empty paragraph in a list item).</param>
    /// <param name="afterItemMarker">An enclosing list item's marker opens the line, so whitespace before this prefix would join the marker's spacing.</param>
    public abstract string Prefix(in LinePrefix stored, bool opening, string rest, bool keepIndentation, bool afterItemMarker);

}

/// <summary>"> " on every line; ">" on empty lines.</summary>
/// <param name="absorbedFrom">Right after a list: the indentation from which the quote's first line would continue the list's last item.</param>
internal sealed class QuoteScope(BlockId owner, int absorbedFrom = int.MaxValue) : PrefixScope(owner)
{
    private bool _firstLine = true;

    public override string Prefix(in LinePrefix stored, bool opening, string rest, bool keepIndentation, bool afterItemMarker)
    {
        bool firstLine = _firstLine;
        _firstLine = false;
        if (stored.TryGetSegment(Owner, out var text))
        {
            // Indentation recorded before the '>' would move a list item's content column, or put the
            // quote into the list before it (Q50).
            string marker = text.TrimStart(' ', '\t');
            bool drop = afterItemMarker || (firstLine && BlockWriter.Columns(text[..^marker.Length]) >= absorbedFrom);
            return drop ? marker : text;
        }

        return rest.Length == 0 ? ">" : "> ";
    }
}

/// <summary>
/// The marker on the item's first line; on the others, indentation to the item's content column.
/// Indenting exactly to the content column keeps the content unchanged when reopened: extra columns
/// would become part of a paragraph's text or a code block's code.
/// </summary>
internal sealed class ListItemScope : PrefixScope
{
    private readonly string? _storedOpening;
    private readonly string _generatedOpening;
    private readonly bool _reuseContinuations;
    private readonly int _contentColumns;

    /// <param name="storedOpening">Indentation, marker and spacing as written (possibly with a rewritten marker); null for generated items.</param>
    /// <param name="generatedOpening">Indentation and marker of a generated item; one space follows.</param>
    /// <param name="bullet">The bullet or delimiter character of the item's marker.</param>
    /// <param name="reuseContinuations">Recorded continuation indentation still matches the content column.</param>
    /// <param name="contentColumns">Columns from the item's start to its content.</param>
    public ListItemScope(BlockId owner, string? storedOpening, string generatedOpening, char bullet, bool reuseContinuations, int contentColumns)
        : base(owner)
    {
        _storedOpening = storedOpening;
        _generatedOpening = generatedOpening;
        Bullet = bullet;
        _reuseContinuations = reuseContinuations;
        _contentColumns = contentColumns;
    }

    public override bool HasOpening => true;

    public char Bullet { get; }

    /// <summary>Columns from the item's start to its content.</summary>
    public int ContentColumns => _contentColumns;

    public override string Prefix(in LinePrefix stored, bool opening, string rest, bool keepIndentation, bool afterItemMarker)
    {
        if (opening)
        {
            // An empty item is still written with the space after its marker, as Typora writes it.
            return _storedOpening ?? _generatedOpening + " ";
        }

        if (_reuseContinuations && stored.TryGetSegment(Owner, out var text))
        {
            return text;
        }

        return rest.Length == 0 && !keepIndentation ? string.Empty : new string(' ', _contentColumns);
    }
}

/// <summary>Nothing on the definition line; four spaces on continuation lines.</summary>
internal sealed class FootnoteScope(BlockId owner) : PrefixScope(owner)
{
    public override bool HasOpening => true;

    public override string Prefix(in LinePrefix stored, bool opening, string rest, bool keepIndentation, bool afterItemMarker)
    {
        if (opening)
        {
            return string.Empty;
        }

        if (stored.TryGetSegment(Owner, out var text))
        {
            return text;
        }

        return rest.Length == 0 && !keepIndentation ? string.Empty : "    ";
    }
}

/// <summary>
/// The indentation a block removes from its own content lines: four columns for indented code, the
/// opening fence's indentation for fenced blocks.
/// </summary>
/// <param name="exact">Recorded indentation of another width than <paramref name="generated"/> is not reused on lines with content (indented code takes exactly four columns).</param>
internal sealed class OwnIndentScope(BlockId owner, string generated, bool reuseStored, bool exact = false) : PrefixScope(owner)
{
    public override string Prefix(in LinePrefix stored, bool opening, string rest, bool keepIndentation, bool afterItemMarker)
    {
        if (reuseStored && stored.TryGetSegment(Owner, out var text)
            && !(exact && rest.Length > 0 && BlockWriter.Columns(text) != BlockWriter.Columns(generated)))
        {
            return text;
        }

        return rest.Length == 0 ? string.Empty : generated;
    }
}
