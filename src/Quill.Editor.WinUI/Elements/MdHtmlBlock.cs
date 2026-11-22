using Quill.Editor.WinUI.Text;

namespace Quill.Editor.WinUI.Elements;

/// <summary>An HTML block: not rendered, its source shows as monospace text (Q31).</summary>
public sealed partial class MdHtmlBlock : MdRawTextView
{
    private protected override BlockTextFormat Format() => base.Format() with { Foreground = Sheet.SecondaryForeground };
}
