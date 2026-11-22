namespace Quill.Editor.WinUI.Theming;

/// <summary>The kind of a style slot's value, which decides its control on the configuration page (15-themes.md).</summary>
public enum StyleSlotKind
{
    /// <summary>A color: a swatch with its hex text, a color picker.</summary>
    Color,

    /// <summary>A color that may be none (empty text).</summary>
    OptionalColor,

    /// <summary>A length in px: a number box.</summary>
    Length,

    /// <summary>A multiple (of the font size or the line): a number box.</summary>
    Scale,

    /// <summary>A whole number: a number box.</summary>
    Integer,

    /// <summary>A font family: an editable list of the system's fonts.</summary>
    Font,

    /// <summary>On or off: a toggle.</summary>
    Toggle,

    /// <summary>One of a few values: a list.</summary>
    Choice,

    /// <summary>Four lengths: left, top, right, bottom.</summary>
    Padding,
}
