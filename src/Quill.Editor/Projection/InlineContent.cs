using System.Collections.Immutable;
using Quill.VariableXaml.Model;

namespace Quill.Editor.Projection;

/// <summary>Inline formatting applied to a run; nested elements combine their flags.</summary>
[Flags]
public enum InlineStyle
{
    None = 0,
    Strong = 1 << 0,
    Emphasis = 1 << 1,
    Strikethrough = 1 << 2,
    Highlight = 1 << 3,
    Subscript = 1 << 4,
    Superscript = 1 << 5,
    Code = 1 << 6,
    Math = 1 << 7,
    Link = 1 << 8,
    Image = 1 << 9,
    FootnoteReference = 1 << 10,
    Underline = 1 << 11,
    Keyboard = 1 << 12,
    Html = 1 << 13,
    Escape = 1 << 14,
    Entity = 1 << 15,
    Emoji = 1 << 16,
}

/// <summary>What a run shows.</summary>
public enum RunRole
{
    /// <summary>Text: <see cref="InlineRun.Text"/>.</summary>
    Text,

    /// <summary>Syntax markers of an active element, shown as text (10-elements.md).</summary>
    Marker,

    /// <summary>A line break.</summary>
    LineBreak,

    /// <summary>Rendered inline math; <see cref="InlineRun.Value"/> is the TeX source.</summary>
    Math,

    /// <summary>An image; <see cref="InlineRun.Value"/> is the source URL, <see cref="InlineRun.Text"/> the alt text.</summary>
    Image,

    /// <summary>An emoji; <see cref="InlineRun.Text"/> is the emoji.</summary>
    Emoji,

    /// <summary>A rendered entity; <see cref="InlineRun.Text"/> is the character.</summary>
    Entity,
}

/// <summary>
/// One run of a text block's displayed content (Q3): the block control turns runs into text elements
/// and gets every style from its style slots, so a run carries semantics only.
/// </summary>
/// <param name="Display">Where the run lies in the displayed text; atomic runs have length 1.</param>
/// <param name="Text">The displayed text (for objects, the fallback text).</param>
/// <param name="IsActive">Whether the run belongs to an active element (markers shown, 05-projection-and-view.md).</param>
/// <param name="Value">The link URL, TeX source or image source, by role and style.</param>
public sealed record InlineRun(Inlines.TextRange Display, string Text, RunRole Role, InlineStyle Style, bool IsActive = false, string? Value = null);

/// <summary>
/// A preview shown outside the text flow for an active element, such as the rendered formula below
/// active inline math (10-elements.md).
/// </summary>
public sealed record InlinePreview(Inlines.TextRange Display, RunRole Role, string Value);

/// <summary>
/// The displayed content of a text block: the value of its <c>Content</c> attribute. Equality and the
/// 64-bit hash are structural, as the reconciler requires (05-projection-and-view.md).
/// </summary>
public sealed class InlineContent : IStructuralHash, IEquatable<InlineContent>
{
    public static InlineContent Empty { get; } = new([], []);

    public InlineContent(ImmutableArray<InlineRun> runs, ImmutableArray<InlinePreview> previews, bool isImageOnly = false)
    {
        Runs = runs;
        Previews = previews;
        IsImageOnly = isImageOnly;
        ulong hash = NodeHash.Combine(NodeHash.Seed, isImageOnly ? 1UL : 0UL);
        foreach (var run in runs)
        {
            hash = NodeHash.Combine(hash, (ulong)(uint)run.Display.Start);
            hash = NodeHash.Combine(hash, (ulong)(uint)run.Display.End);
            hash = NodeHash.Combine(hash, run.Text);
            hash = NodeHash.Combine(hash, ((ulong)run.Role << 40) | ((ulong)(uint)run.Style << 1) | (run.IsActive ? 1UL : 0UL));
            hash = NodeHash.Combine(hash, run.Value);
        }

        foreach (var preview in previews)
        {
            hash = NodeHash.Combine(hash, ((ulong)(uint)preview.Display.Start << 32) | (uint)preview.Display.End);
            hash = NodeHash.Combine(hash, (ulong)preview.Role);
            hash = NodeHash.Combine(hash, preview.Value);
        }

        StructuralHash = hash;
    }

    public ImmutableArray<InlineRun> Runs { get; }

    public ImmutableArray<InlinePreview> Previews { get; }

    /// <summary>
    /// A paragraph that holds one image and nothing else but spaces (10: shown as a block, centered when
    /// narrower than the column, scaled to it when wider); an image among text flows with the text.
    /// </summary>
    public bool IsImageOnly { get; }

    public ulong StructuralHash { get; }

    /// <summary>The displayed text, objects as U+FFFC.</summary>
    public string DisplayText => string.Concat(Runs.Select(static run => run.Role is RunRole.Text or RunRole.Marker or RunRole.LineBreak ? run.Text : "￼"));

    public bool Equals(InlineContent? other) =>
        other is not null && (ReferenceEquals(this, other)
            || (other.StructuralHash == StructuralHash && other.IsImageOnly == IsImageOnly && other.Runs.SequenceEqual(Runs) && other.Previews.SequenceEqual(Previews)));

    public override bool Equals(object? obj) => Equals(obj as InlineContent);

    public override int GetHashCode() => (int)StructuralHash;

    public override string ToString() => DisplayText;
}
