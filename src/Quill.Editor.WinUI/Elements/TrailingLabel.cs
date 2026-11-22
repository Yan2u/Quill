using Windows.UI;

namespace Quill.Editor.WinUI.Elements;

/// <summary>
/// A label drawn right after the text of a definition slot: the closing quote of a title, the
/// backlinks of a footnote. <paramref name="Scale"/> is relative to the body font size;
/// <paramref name="Shift"/> moves it down from the baseline, in multiples of the font size;
/// <paramref name="Backlinks"/> is the number of backlink marks it holds, each clickable (Q72).
/// </summary>
internal readonly record struct TrailingLabel(string Text, Color Color, float Scale, float Shift, int Backlinks = 0);
