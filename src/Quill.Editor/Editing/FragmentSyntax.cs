using System.Collections.Immutable;
using Quill.Editor.Documents;
using Quill.Editor.Documents.Syntax;

namespace Quill.Editor.Editing;

/// <summary>
/// Blocks parsed from a fragment (a committed paragraph, pasted text) were read at column 0. Where
/// they are placed inside a container, recorded syntax holding a tab would cover a different number of
/// columns, so it is dropped and generated on export instead: list item openings, line prefixes.
/// </summary>
internal static class FragmentSyntax
{
    public static Block WithoutTabs(Block block)
    {
        switch (block)
        {
            case LeafBlock leaf when leaf.LinePrefixes.Any(HasTab):
                return leaf with { LinePrefixes = [.. leaf.LinePrefixes.Select(static prefix => HasTab(prefix) ? LinePrefix.None : prefix)] };
            case ListItem item:
            {
                var children = item.Children.Select(WithoutTabs).ToImmutableList();
                bool tabbed = item.Syntax is { } syntax && (syntax.Indent.Contains('\t') || syntax.Spacing.Contains('\t'));
                return item with { Children = children, Syntax = tabbed ? null : item.Syntax };
            }

            case ContainerBlock container:
                return container with { Children = [.. container.Children.Select(WithoutTabs)] };
            default:
                return block;
        }
    }

    private static bool HasTab(LinePrefix prefix) =>
        !prefix.IsNone && prefix.Segments.Any(static segment => segment.Text.Contains('\t'));
}
