using System.Collections.Immutable;

namespace Quill.Editor.Documents.Syntax;

/// <summary>
/// The raw prefix text one container contributes to one physical line, such as "> " for a block quote
/// or the continuation indentation of a list item. <see cref="Owner"/> is the container's ID (or, for
/// the own indentation of a code block, the code block's ID).
/// </summary>
public readonly record struct PrefixSegment(BlockId Owner, string Text);

/// <summary>
/// The prefix segments recorded for one physical line at import, outermost owner first. The serializer
/// reuses a segment only while its owner still encloses the line; otherwise it generates a new one.
/// The default value records nothing.
/// </summary>
public readonly record struct LinePrefix(ImmutableArray<PrefixSegment> Segments)
{
    public static LinePrefix None => default;

    public bool IsNone => Segments.IsDefaultOrEmpty;

    public bool TryGetSegment(BlockId owner, out string text)
    {
        if (!Segments.IsDefault)
        {
            foreach (var segment in Segments)
            {
                if (segment.Owner == owner)
                {
                    text = segment.Text;
                    return true;
                }
            }
        }

        text = string.Empty;
        return false;
    }
}

/// <summary>
/// One physical line of block syntax (a code fence, a Setext underline, a table delimiter row, a blank
/// separator line...) as written, without container prefixes, together with the prefixes of its line.
/// </summary>
public readonly record struct SyntaxLine(string Text, LinePrefix Prefix);

/// <summary>
/// Block syntax that may span several physical lines, such as the parts of a link reference definition
/// between its slots. <see cref="Prefixes"/>[k] belongs to the line that starts after the k-th line break
/// in <see cref="Text"/>; <see cref="Prefixes"/>[0] belongs to the line the text starts on, when it
/// starts one.
/// </summary>
public sealed record SyntaxText(string Text, ImmutableArray<LinePrefix> Prefixes)
{
    public static SyntaxText Of(string text) => new(text, []);

    public LinePrefix PrefixAt(int line) => line < Prefixes.Length ? Prefixes[line] : LinePrefix.None;
}

/// <summary>
/// The blank lines between the previous sibling and this block, as written. An empty list means the
/// two blocks were adjacent (02-document-model.md: extra blank lines are empty paragraphs, so a
/// separator holds at most one blank line, except between list items). <see cref="Previous"/> is the
/// non-empty sibling the separator followed when recorded; once another block precedes this one, the
/// separator is generated instead (null: valid after any block).
/// </summary>
public sealed record BlockSeparator(ImmutableArray<SyntaxLine> Lines, BlockId? Previous = null)
{
    public static BlockSeparator Adjacent(BlockId? previous = null) => new(ImmutableArray<SyntaxLine>.Empty, previous);

    public bool IsAdjacent => Lines.IsEmpty;
}
