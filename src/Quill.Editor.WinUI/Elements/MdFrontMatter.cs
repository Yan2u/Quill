namespace Quill.Editor.WinUI.Elements;

/// <summary>YAML front matter: shown as a code block whose language cannot be set (10), colored as YAML.</summary>
public sealed partial class MdFrontMatter : MdCodeBlock
{
    private protected override string HighlightInfo => "yaml";

    private protected override Windows.UI.Color CodeBackground => Sheet.FrontMatterBackground;

    private protected override Microsoft.UI.Xaml.Thickness BlockPadding() => Sheet.FrontMatterPadding;

    /// <summary>Line numbers are for code blocks (10); front matter has none.</summary>
    private protected override bool NumbersLines => false;
}
