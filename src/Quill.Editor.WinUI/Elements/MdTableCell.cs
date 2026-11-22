using Microsoft.Graphics.Canvas.Text;
using Microsoft.UI.Xaml;
using Quill.Editor.Documents;
using Quill.Editor.WinUI.Text;

namespace Quill.Editor.WinUI.Elements;

/// <summary>A table cell: aligned by its column, bold in the header row; the row paints the header background.</summary>
public sealed partial class MdTableCell : MdInlineTextView
{
    private TableAlignment _alignment;
    private bool _isHeader;

    public MdTableCell()
        : base(draws: false)
    {
    }

    public TableAlignment Alignment
    {
        get => _alignment;
        set
        {
            if (_alignment != value)
            {
                _alignment = value;
                InvalidateText();
            }
        }
    }

    public bool IsHeader
    {
        get => _isHeader;
        set
        {
            if (_isHeader != value)
            {
                _isHeader = value;
                InvalidateText();
            }
        }
    }

    private protected override BlockTextFormat Format() => base.Format() with
    {
        Bold = _isHeader,
        Alignment = _alignment switch
        {
            TableAlignment.Center => CanvasHorizontalAlignment.Center,
            TableAlignment.Right => CanvasHorizontalAlignment.Right,
            _ => CanvasHorizontalAlignment.Left,
        },
    };

    protected override Thickness Padding() => new(Sheet.CellPaddingHorizontal, Sheet.CellPaddingVertical, Sheet.CellPaddingHorizontal, Sheet.CellPaddingVertical);
}
