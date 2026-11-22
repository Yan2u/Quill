using Quill.Editor.Projection;
using Quill.Editor.WinUI.Theming;
using Quill.VariableXaml.Model;

namespace Quill.Editor.WinUI.Layout;

/// <summary>
/// Estimates the height of a block the virtual stack has never measured (Q69), from its VNode:
/// lines from the text length at an average character width, plus the block's own padding, and
/// containers as the sum of their children. Only the extent before a block is first seen depends on it.
/// </summary>
public static class BlockHeightEstimator
{
    public static double Estimate(VNode node, double width, MdStyleSheet sheet)
    {
        double line = sheet.FontSize * 1.6;
        double character = sheet.FontSize * 0.55;
        double Lines(string text, double scale = 1)
        {
            double perLine = Math.Max(1, Math.Floor(width / (character * scale)));
            int lines = 0;
            foreach (var part in text.Split('\n'))
            {
                lines += Math.Max(1, (int)Math.Ceiling(part.Length / perLine));
            }

            return lines;
        }

        string local = node.Type.LocalName;
        switch (local)
        {
            case "MdParagraph" or "MdTableCell" or "MdDefinitionSlot":
                return line * Lines(Attribute<InlineContent>(node, ProjectionNames.Content)?.DisplayText ?? string.Empty);
            case "MdHeading":
                double scale = sheet.HeadingSize(Attribute<int?>(node, ProjectionNames.Level) ?? 1) / sheet.FontSize;
                return line * scale * Lines(Attribute<InlineContent>(node, ProjectionNames.Content)?.DisplayText ?? string.Empty, scale);
            case "MdCodeBlock" or "MdFrontMatter" or "MdMathBlock" or "MdHtmlBlock":
                return line * Lines(Attribute<string>(node, ProjectionNames.Text) ?? string.Empty) + sheet.CodeBlockPadding.Top + sheet.CodeBlockPadding.Bottom;
            case "MdThematicBreak" or "MdVirtualParagraph":
                return line;
            case "MdToc":
                return 2 * line;
            case "MdTableRow":
                return node.Children.Where(static child => !child.IsText).Select(child => Estimate(child, width / Math.Max(1, node.Children.Count), sheet)).DefaultIfEmpty(line).Max();
            default:
                // Containers: their children, stacked.
                var children = node.Children.Where(static child => !child.IsText).ToList();
                return children.Count == 0 ? line : children.Sum(child => Estimate(child, width, sheet)) + (children.Count - 1) * sheet.ListItemSpacing;
        }
    }

    private static T? Attribute<T>(VNode node, string name) =>
        node.GetAttribute(XamlMemberName.Plain(name)) is T value ? value : default;
}
