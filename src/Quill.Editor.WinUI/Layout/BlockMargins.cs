using Quill.Editor.WinUI.Theming;

namespace Quill.Editor.WinUI.Layout;

/// <summary>
/// The space between two adjacent blocks: each kind has a margin (style sheet), and adjacent margins
/// collapse to the larger, following standard CSS margin collapsing (e.g. a heading after a paragraph
/// collapses to the larger margin rather than summing both).
/// Kinds are the element names of the projection (<c>MdHeading</c>, …), which are also the control types.
/// </summary>
public static class BlockMargins
{
    public static double Between(MdStyleSheet sheet, string upper, string lower) => Math.Max(Of(sheet, upper), Of(sheet, lower));

    public static double Of(MdStyleSheet sheet, string kind) => kind switch
    {
        "MdHeading" => sheet.HeadingSpacing,
        "MdCodeFence" or "MdCodeBlock" => sheet.CodeBlockSpacing,
        "MdMathBlock" => sheet.MathBlockSpacing,
        "MdThematicBreak" => sheet.RuleSpacing,
        "MdToc" => sheet.TocSpacing,
        _ => sheet.BlockSpacing,
    };
}
