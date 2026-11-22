using System.Text;
using Quill.Editor.Documents;
using Quill.Editor.Inlines;
using Quill.Editor.Markdown;

namespace Quill.Editor.Projection;

/// <summary>
/// The text a leaf shows with no element active, for plain-text copies (04-commands-and-behaviors.md):
/// hidden syntax and the leading zero-width space are left out, images give their alt text, formulas
/// their TeX source, emoji and entities their characters.
/// </summary>
internal static class PlainText
{
    public static string Of(LeafBlock leaf, ReferenceIndex references)
    {
        if (leaf is not TextLeafBlock text)
        {
            return leaf.Text;
        }

        var tree = MarkdownInlines.Parser.Parse(text.Text, text.InlineSyntax, references.IsLinkDefined);
        var model = LeafRenderModel.For(tree, LeafRenderModel.HidesLeadingZeroWidthSpace(text));
        var content = LeafRenderer.Render(leaf.Id, model, new HashSet<int>()).Content;
        var builder = new StringBuilder();
        foreach (var run in content.Runs)
        {
            builder.Append(run.Role switch
            {
                RunRole.Math => run.Value,
                _ => run.Text,
            });
        }

        return builder.ToString();
    }
}
