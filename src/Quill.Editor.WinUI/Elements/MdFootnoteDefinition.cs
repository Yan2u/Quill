namespace Quill.Editor.WinUI.Elements;

/// <summary>
/// A footnote definition: <c>[^name]: description</c> with the brackets and colon drawn (10), one
/// backlink ↩ per reference after the description (06: from the reference index), and the further
/// blocks of a multi-paragraph footnote below.
/// </summary>
public sealed partial class MdFootnoteDefinition : MdDefinitionView
{
    private const string Backlink = "↩";

    private int _backlinkCount;

    /// <summary>The number of references to this footnote: one backlink each.</summary>
    public int BacklinkCount
    {
        get => _backlinkCount;
        set => SetAndRemeasure(ref _backlinkCount, value);
    }

    protected override int SlotCount => 2;

    protected override IEnumerable<object> RowParts() => ["[^", 0, "]: ", 1];

    // Small and lowered: a subscript-like mark (10).
    private protected override IEnumerable<TrailingLabel> ExtraTrailing() =>
        _backlinkCount > 0 ? [new TrailingLabel(string.Concat(Enumerable.Repeat(Backlink, _backlinkCount)), Sheet.FootnoteBacklinkForeground, Sheet.ScriptScale, 0.15f, _backlinkCount)] : [];
}
