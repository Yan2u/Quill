using System.Collections.Immutable;
using System.Text;

namespace Quill.Editor.WinUI.Text;

/// <summary>The kind of an atomic inline object (one character of the displayed text).</summary>
internal enum TextObjectKind
{
    Math,
    Image,
    Emoji,
    Entity,
}

/// <summary>A styled span of a block's text.</summary>
internal readonly record struct TextSpan(int Start, int Length, SpanStyle Style);

/// <summary>An atomic object at one character of the text: its kind, what it shows, and its rendered resource if any (a decoded image).</summary>
internal readonly record struct TextObject(int Index, TextObjectKind Kind, string Text, SpanStyle Style, object? Resource = null);

/// <summary>
/// What a block shows, independent of width and of the drawing device: the displayed text
/// (objects as U+FFFC), styled spans and objects. Offsets are display offsets, exactly the ones
/// the projection uses for selections, compositions and carets (05-projection-and-view.md).
/// </summary>
internal sealed class TextSource
{
    public const char ObjectCharacter = '￼';

    public static TextSource Empty { get; } = new(string.Empty, [], []);

    public TextSource(string text, ImmutableArray<TextSpan> spans, ImmutableArray<TextObject> objects)
    {
        Text = text;
        Spans = spans;
        Objects = objects;
    }

    /// <summary>The same content shown as a paragraph of one image alone.</summary>
    public TextSource(TextSource source)
        : this(source.Text, source.Spans, source.Objects)
    {
        IsImageOnly = true;
    }

    public string Text { get; }

    public ImmutableArray<TextSpan> Spans { get; }

    public ImmutableArray<TextObject> Objects { get; }

    /// <summary>A paragraph of one image alone: the image shows as a block, centered in the column (10).</summary>
    public bool IsImageOnly { get; init; }

    /// <summary>Plain text in one style, such as the source of a code block.</summary>
    public static TextSource Plain(string text, SpanStyle style = default) =>
        new(text, text.Length == 0 || style == default ? [] : [new TextSpan(0, text.Length, style)], []);

    /// <summary>Builds a text source span by span.</summary>
    public sealed class Builder
    {
        private readonly StringBuilder _text = new();
        private readonly ImmutableArray<TextSpan>.Builder _spans = ImmutableArray.CreateBuilder<TextSpan>();
        private readonly ImmutableArray<TextObject>.Builder _objects = ImmutableArray.CreateBuilder<TextObject>();

        public int Length => _text.Length;

        public void Append(string text, SpanStyle style)
        {
            if (text.Length == 0)
            {
                return;
            }

            if (style != default)
            {
                _spans.Add(new TextSpan(_text.Length, text.Length, style));
            }

            _text.Append(text);
        }

        public void AppendObject(TextObjectKind kind, string text, SpanStyle style, object? resource = null)
        {
            _objects.Add(new TextObject(_text.Length, kind, text, style, resource));
            _text.Append(ObjectCharacter);
        }

        public TextSource Build() => new(_text.ToString(), _spans.ToImmutable(), _objects.ToImmutable());
    }
}
