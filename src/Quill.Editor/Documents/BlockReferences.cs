using System.Collections.Immutable;
using Quill.Editor.Documents.Syntax;

namespace Quill.Editor.Documents;

/// <summary>
/// Rewrites a block ID throughout a subtree: the block that has it, and the syntax metadata that refers
/// to it (owners of recorded line prefix segments, the previous block of separators, the list a list
/// was written apart from, written table cells). Used when a block takes over the ID of the block it
/// replaces; references left to the old ID would make recorded prefixes look foreign, and the export
/// would generate others in their place.
/// </summary>
internal static class BlockReferences
{
    public static Block Rename(Block block, BlockId from, BlockId to) => new Renamer(from, to).Visit(block);

    private sealed class Renamer(BlockId from, BlockId to)
    {
        public Block Visit(Block block)
        {
            block = block with { Id = Id(block.Id), SeparatorBefore = Separator(block.SeparatorBefore) };
            if (block is LeafBlock leaf)
            {
                block = leaf with { LinePrefixes = Prefixes(leaf.LinePrefixes) };
            }

            block = block switch
            {
                Heading { Syntax: { } syntax } heading => heading with { Syntax = syntax with { Underline = OptionalLine(syntax.Underline) } },
                CodeFence { Syntax: { Fence: { } fence } syntax } code => code with { Syntax = syntax with { Fence = Fence(fence) } },
                MathBlock { Syntax: { } fence } math => math with { Syntax = Fence(fence) },
                FrontMatter { Syntax: { } fence } frontMatter => frontMatter with { Syntax = Fence(fence) },
                AtomicBlock { Syntax: { } line } atomic => atomic with { Syntax = Line(line) },
                ListBlock { Syntax: { SeparateFrom: { } apart } syntax } list => list with { Syntax = syntax with { SeparateFrom = Id(apart) } },
                ListItem { Syntax: { } syntax } item => item with { Syntax = syntax with { OpeningLinePrefix = Prefix(syntax.OpeningLinePrefix) } },
                Table { Syntax: { } syntax } table => table with { Syntax = syntax with { DelimiterRow = Line(syntax.DelimiterRow) } },
                TableRow { Syntax: { } syntax } row => row with { Syntax = syntax with { Prefix = Prefix(syntax.Prefix), Cells = [.. syntax.Cells.Select(Id)] } },
                FootnoteDefinition { Syntax: { } syntax } footnote => footnote with { Syntax = syntax with { Prefix = Prefix(syntax.Prefix) } },
                LinkReferenceDefinition { Syntax: { } syntax } definition => definition with
                {
                    Syntax = syntax with
                    {
                        Opening = Text(syntax.Opening),
                        AfterLabel = Text(syntax.AfterLabel),
                        AfterDestination = Text(syntax.AfterDestination),
                        AfterTitle = Text(syntax.AfterTitle),
                    },
                },
                _ => block,
            };

            return block is ContainerBlock container ? container with { Children = [.. container.Children.Select(Visit)] } : block;
        }

        private BlockId Id(BlockId id) => id == from ? to : id;

        private LinePrefix Prefix(LinePrefix prefix) =>
            prefix.IsNone || !prefix.Segments.Any(segment => segment.Owner == from)
                ? prefix
                : new LinePrefix([.. prefix.Segments.Select(segment => segment with { Owner = Id(segment.Owner) })]);

        private ImmutableArray<LinePrefix> Prefixes(ImmutableArray<LinePrefix> prefixes) =>
            prefixes.IsDefaultOrEmpty ? prefixes : [.. prefixes.Select(Prefix)];

        private SyntaxLine Line(SyntaxLine line) => line with { Prefix = Prefix(line.Prefix) };

        private SyntaxLine? OptionalLine(SyntaxLine? line) => line is { } value ? Line(value) : null;

        private SyntaxText Text(SyntaxText text) => text with { Prefixes = Prefixes(text.Prefixes) };

        private FenceSyntax Fence(FenceSyntax fence) => fence with { Opening = Line(fence.Opening), Closing = OptionalLine(fence.Closing) };

        private BlockSeparator? Separator(BlockSeparator? separator) =>
            separator is null ? null : separator with { Lines = [.. separator.Lines.Select(Line)], Previous = separator.Previous is { } previous ? Id(previous) : null };
    }
}
