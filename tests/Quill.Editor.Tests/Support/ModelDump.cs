using System.Text;
using Quill.Editor.Documents;

namespace Quill.Editor.Tests.Support;

/// <summary>
/// Text renderings of the document model for assertions. <see cref="Structure"/> covers what
/// structural equivalence compares (06-markdown-io.md): block kinds, texts and semantic properties,
/// ignoring IDs, revisions and syntax metadata.
/// </summary>
public static class ModelDump
{
    public static string Structure(Block block)
    {
        var builder = new StringBuilder();
        AppendStructure(builder, block, 0);
        return builder.ToString();
    }

    private static void AppendStructure(StringBuilder builder, Block block, int depth)
    {
        // A code block dumps as one line: its language and its code (Q91: they are its two slots).
        if (block is CodeFence fence)
        {
            builder.Append(' ', depth * 2).Append("CodeBlock info=").Append(Quote(fence.Info)).Append(' ').Append(Quote(fence.Code.Text)).Append('\n');
            return;
        }

        builder.Append(' ', depth * 2).Append(block.Kind);
        switch (block)
        {
            case Heading heading:
                builder.Append(" level=").Append(heading.Level);
                break;
            case ListBlock list:
                builder.Append(list.IsOrdered ? " ordered" : " bullet");
                break;
            case ListItem item:
                builder.Append(" task=").Append(item.Task);
                if (item.Number is { } number)
                {
                    builder.Append(" number=").Append(number);
                }

                break;
            case Table table:
                builder.Append(" align=").Append(string.Join(",", table.Alignments));
                break;
            case DefinitionSlot slot:
                builder.Append(' ').Append(slot.SlotKind);
                break;
        }

        if (block is LeafBlock leaf)
        {
            builder.Append(' ').Append(Quote(leaf.Text));
        }

        builder.Append('\n');
        if (block is ContainerBlock container)
        {
            foreach (var child in container.Children)
            {
                AppendStructure(builder, child, depth + 1);
            }
        }
    }

    public static string Quote(string text) =>
        "\"" + text.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\t", "\\t").Replace("\n", "\\n") + "\"";

    /// <summary>
    /// The structure with IDs and the syntax metadata that export depends on: separators (and the
    /// block they were recorded after), recorded line prefixes, list item openings. For debugging.
    /// </summary>
    public static string Detailed(Block block)
    {
        var builder = new StringBuilder();
        AppendDetailed(builder, block, 0);
        return builder.ToString();
    }

    private static void AppendDetailed(StringBuilder builder, Block block, int depth)
    {
        builder.Append(' ', depth * 2).Append(block.Kind).Append(' ').Append(block.Id);
        if (block.SeparatorBefore is { } separator)
        {
            builder.Append(" sep=[").Append(string.Join("|", separator.Lines.Select(static line => Prefix(line.Prefix) + Quote(line.Text))))
                .Append("] after ").Append(separator.Previous?.ToString() ?? "-");
        }

        if (block is ListItem { Syntax: { } syntax })
        {
            builder.Append(" opening=").Append(Quote(syntax.Indent + syntax.Marker + syntax.Spacing));
        }

        if (block is LeafBlock leaf)
        {
            builder.Append(' ').Append(Quote(leaf.Text));
            for (int line = 0; line < leaf.LinePrefixes.Length; line++)
            {
                if (!leaf.LinePrefixes[line].IsNone)
                {
                    builder.Append(" L").Append(line).Append('=').Append(Prefix(leaf.LinePrefixes[line]));
                }
            }
        }

        builder.Append('\n');
        if (block is ContainerBlock container)
        {
            foreach (var child in container.Children)
            {
                AppendDetailed(builder, child, depth + 1);
            }
        }
    }

    private static string Prefix(Documents.Syntax.LinePrefix prefix) =>
        prefix.IsNone ? string.Empty : "{" + string.Join(",", prefix.Segments.Select(static segment => $"{segment.Owner}:{Quote(segment.Text)}")) + "}";
}
