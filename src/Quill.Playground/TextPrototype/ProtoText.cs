using Windows.UI;

namespace Quill.Playground.TextPrototype;

/// <summary>Inline formatting of a prototype run (a subset of the editor's <c>InlineStyle</c>).</summary>
[Flags]
internal enum ProtoStyle
{
    None = 0,
    Strong = 1 << 0,
    Emphasis = 1 << 1,
    Strikethrough = 1 << 2,
    Highlight = 1 << 3,
    Code = 1 << 4,
    Superscript = 1 << 5,
    Subscript = 1 << 6,
    Marker = 1 << 7,
    Link = 1 << 8,
}

/// <summary>How an inline object sits on its line (10-elements.md).</summary>
internal enum ProtoObjectAlign
{
    /// <summary>On the text baseline at <see cref="ProtoObject.Baseline"/>: inline math.</summary>
    Baseline,

    /// <summary>Vertically centered on the text: images next to text (Typora).</summary>
    Center,
}

/// <summary>An inline object of fixed size, standing in for a rendered formula or an image.</summary>
internal sealed record ProtoObject(float Width, float Height, ProtoObjectAlign Align, Color Fill, float Baseline = 0);

/// <summary>A run of the prototype's input; an object run's text is one U+FFFC.</summary>
internal sealed record ProtoRun(string Text, ProtoStyle Style = ProtoStyle.None, ProtoObject? Object = null)
{
    public static ProtoRun Inline(ProtoObject value) => new("￼", ProtoStyle.None, value);
}

/// <summary>The style slots the prototype draws with (05-projection-and-view.md).</summary>
internal sealed record TextTheme
{
    public static TextTheme Light { get; } = new();

    public string FontFamily { get; init; } = "Segoe UI";

    public string MonospaceFamily { get; init; } = "Consolas";

    public float FontSize { get; init; } = 16;

    public float IncrementalTabStop { get; init; } = 32;

    public Color Foreground { get; init; } = Color.FromArgb(0xE4, 0, 0, 0);

    public Color MarkerForeground { get; init; } = Color.FromArgb(0xFF, 0x8A, 0x8A, 0x8A);

    public Color LinkForeground { get; init; } = Color.FromArgb(0xFF, 0x00, 0x5F, 0xB8);

    public Color HighlightBackground { get; init; } = Color.FromArgb(0xFF, 0xFF, 0xF1, 0x76);

    public Color CodeBackground { get; init; } = Color.FromArgb(0xFF, 0xF0, 0xF0, 0xF0);

    public Color SelectionBackground { get; init; } = Color.FromArgb(0xFF, 0xB4, 0xD7, 0xFF);

    public Color CompositionUnderline { get; init; } = Color.FromArgb(0xFF, 0x40, 0x40, 0x40);

    /// <summary>Superscript and subscript size relative to the text.</summary>
    public float ScriptScale { get; init; } = 0.7f;
}
