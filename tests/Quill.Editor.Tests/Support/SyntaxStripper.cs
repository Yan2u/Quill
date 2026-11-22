using Quill.Editor.Documents;

namespace Quill.Editor.Tests.Support;

/// <summary>
/// Removes all syntax metadata from a tree, so that the serializer must generate every piece of
/// syntax. Used to test the generation path.
/// </summary>
public static class SyntaxStripper
{
    public static T Strip<T>(T block)
        where T : Block
    {
        Block stripped = block switch
        {
            Document document => document with { Children = StripChildren(document) },
            BlockQuote quote => quote with { Children = StripChildren(quote) },
            ListBlock list => list with { Syntax = null, Children = StripChildren(list) },
            ListItem item => item with { Syntax = null, Children = StripChildren(item) },
            Table table => table with { Syntax = null, Children = StripChildren(table) },
            TableRow row => row with { Syntax = null, Children = StripChildren(row) },
            FootnoteDefinition footnote => footnote with { Syntax = null, Children = StripChildren(footnote) },
            LinkReferenceDefinition definition => definition with { Syntax = null, Children = StripChildren(definition) },
            Heading heading => heading with { Syntax = null, LinePrefixes = [] },
            CodeFence code => code with { Syntax = null, Children = StripChildren(code) },
            MathBlock math => math with { Syntax = null, LinePrefixes = [] },
            FrontMatter frontMatter => frontMatter with { Syntax = null, LinePrefixes = [] },
            LeafBlock leaf => leaf with { LinePrefixes = [] },
            AtomicBlock atomic => atomic with { Syntax = null },
            _ => throw new NotSupportedException(block.Kind.ToString()),
        };
        return (T)(stripped with { SeparatorBefore = null });
    }

    private static System.Collections.Immutable.ImmutableList<Block> StripChildren(ContainerBlock container) =>
        [.. container.Children.Select(Strip)];
}
