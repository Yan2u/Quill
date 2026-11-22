using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using Quill.Editor.Documents;
using Quill.Editor.Inlines;
using Quill.Editor.Transactions;

namespace Quill.Editor.Projection;

/// <summary>When a piece of the render model is present (05-projection-and-view.md, two-phase projection).</summary>
internal enum PieceShow
{
    Always,

    /// <summary>Only while the owning node is active (its source shown).</summary>
    WhenActive,

    /// <summary>Only while the owning node is inactive (its rendering shown).</summary>
    WhenInactive,
}

internal enum PieceKind
{
    /// <summary>Displayed source text.</summary>
    Text,

    /// <summary>Syntax shown while the owner is active, hidden otherwise.</summary>
    Marker,

    /// <summary>A line break inside syntax: a line break while the owner is active, hidden otherwise.</summary>
    MarkerBreak,

    /// <summary>Never shown, such as the leading zero-width space.</summary>
    Hidden,

    LineBreak,

    /// <summary>An object of display length 1; an empty source range makes it display-only.</summary>
    Atomic,
}

/// <summary>
/// A piece of a leaf: a source range, its display policy and its semantics. <see cref="Show"/> refers to
/// <see cref="Owner"/>; a piece with an <see cref="OuterGate"/> is present only while that node is active
/// too (the alt text of an image).
/// </summary>
internal readonly record struct Piece(
    TextRange Source,
    PieceKind Kind,
    PieceShow Show,
    int Owner,
    InlineStyle Style,
    InlineNode? Node,
    RunRole Role = RunRole.Text,
    string? Text = null,
    string? Value = null,
    int OuterGate = -1);

/// <summary>
/// Phase one of the projection of a text leaf: everything that does not depend on the caret. Each
/// syntax marker carries its display policy; phase two (<see cref="LeafRenderer"/>) applies the
/// active set. Cached per inline tree.
/// </summary>
internal sealed class LeafRenderModel
{
    private static readonly ConditionalWeakTable<InlineTree, LeafRenderModel> Cache = new();

    private LeafRenderModel(string source, ImmutableArray<Piece> pieces, ImmutableArray<InlineNode> owners)
    {
        Source = source;
        Pieces = pieces;
        Owners = owners;
    }

    public string Source { get; }

    public ImmutableArray<Piece> Pieces { get; }

    /// <summary>The nodes that can be active, indexed by <see cref="Piece.Owner"/>.</summary>
    public ImmutableArray<InlineNode> Owners { get; }

    /// <summary>The render model of a text leaf; the leading zero-width space of a paragraph is hidden.</summary>
    public static LeafRenderModel For(InlineTree tree, bool hideLeadingZeroWidthSpace)
    {
        if (!hideLeadingZeroWidthSpace)
        {
            return Cache.GetValue(tree, static tree => new Builder(tree.Source, false).Build(tree.Nodes));
        }

        return new Builder(tree.Source, true).Build(tree.Nodes);
    }

    public static bool HidesLeadingZeroWidthSpace(TextLeafBlock leaf) =>
        leaf is Paragraph && leaf.Text.Length > 0 && leaf.Text[0] == Normalizer.ZeroWidthSpace;

    private sealed class Builder
    {
        private readonly string _source;
        private readonly bool _hideLeadingZeroWidthSpace;
        private readonly ImmutableArray<Piece>.Builder _pieces = ImmutableArray.CreateBuilder<Piece>();
        private readonly ImmutableArray<InlineNode>.Builder _owners = ImmutableArray.CreateBuilder<InlineNode>();

        public Builder(string source, bool hideLeadingZeroWidthSpace)
        {
            _source = source;
            _hideLeadingZeroWidthSpace = hideLeadingZeroWidthSpace;
        }

        public LeafRenderModel Build(ImmutableArray<InlineNode> nodes)
        {
            foreach (var node in nodes)
            {
                Visit(node, -1, InlineStyle.None, null);
            }

            return Finish();
        }

        public LeafRenderModel Finish() => new(_source, _pieces.ToImmutable(), _owners.ToImmutable());

        public void AddText(TextRange range, int owner, InlineStyle style, InlineNode? node, string? value = null, PieceShow show = PieceShow.Always)
        {
            int start = range.Start;
            for (int i = range.Start; i < range.End; i++)
            {
                if (_source[i] == '\n')
                {
                    Add(new TextRange(start, i), PieceKind.Text, show, owner, style, node, value: value);
                    Add(new TextRange(i, i + 1), PieceKind.LineBreak, show, owner, style, node);
                    start = i + 1;
                }
                else if (i == 0 && _hideLeadingZeroWidthSpace)
                {
                    Add(new TextRange(0, 1), PieceKind.Hidden, PieceShow.Always, owner, style, node);
                    start = 1;
                }
            }

            Add(new TextRange(start, range.End), PieceKind.Text, show, owner, style, node, value: value);
        }

        private void Marker(TextRange range, int owner, InlineStyle style, InlineNode node, PieceShow show = PieceShow.Always)
        {
            int start = range.Start;
            for (int i = range.Start; i < range.End; i++)
            {
                if (_source[i] == '\n')
                {
                    Add(new TextRange(start, i), PieceKind.Marker, show, owner, style, node);
                    Add(new TextRange(i, i + 1), PieceKind.MarkerBreak, show, owner, style, node);
                    start = i + 1;
                }
            }

            Add(new TextRange(start, range.End), PieceKind.Marker, show, owner, style, node);
        }

        private void Add(TextRange range, PieceKind kind, PieceShow show, int owner, InlineStyle style, InlineNode? node, RunRole role = RunRole.Text, string? text = null, string? value = null)
        {
            if (range.Length > 0 || kind == PieceKind.Atomic)
            {
                _pieces.Add(new Piece(range, kind, show, owner, style, node, role, text, value));
            }
        }

        private int Own(InlineNode node)
        {
            _owners.Add(node);
            return _owners.Count - 1;
        }

        private void Children(InlineNode node, int owner, InlineStyle style, string? value)
        {
            if (node.Children.Length == 0)
            {
                AddText(node.Content, owner, style, node, value);
                return;
            }

            foreach (var child in node.Children)
            {
                Visit(child, owner, style, value);
            }
        }

        /// <summary>Opening marker, content, closing marker: the shape of most elements.</summary>
        private void Wrapped(InlineNode node, InlineStyle style, string? value = null)
        {
            int owner = Own(node);
            Marker(node.Opening, owner, style, node);
            Children(node, owner, style, value);
            Marker(node.Closing, owner, style, node);
        }

        private void Visit(InlineNode node, int parent, InlineStyle style, string? value)
        {
            switch (node.Kind)
            {
                case InlineKind.Text:
                    AddText(node.Range, parent, style, null, value);
                    break;
                case InlineKind.Escape:
                {
                    int owner = Own(node);
                    Marker(node.Opening, owner, style | InlineStyle.Escape, node);
                    AddText(node.Content, owner, style, node, value);
                    break;
                }

                case InlineKind.Strong:
                    Wrapped(node, style | InlineStyle.Strong, value);
                    break;
                case InlineKind.Emphasis:
                    Wrapped(node, style | InlineStyle.Emphasis, value);
                    break;
                case InlineKind.Strikethrough:
                    Wrapped(node, style | InlineStyle.Strikethrough, value);
                    break;
                case InlineKind.Highlight:
                    Wrapped(node, style | InlineStyle.Highlight, value);
                    break;
                case InlineKind.Subscript:
                    Wrapped(node, style | InlineStyle.Subscript, value);
                    break;
                case InlineKind.Superscript:
                    Wrapped(node, style | InlineStyle.Superscript, value);
                    break;
                case InlineKind.Code:
                    Wrapped(node, style | InlineStyle.Code, value);
                    break;
                case InlineKind.FootnoteReference:
                    Wrapped(node, style | InlineStyle.FootnoteReference, node.Label);
                    break;
                case InlineKind.Link:
                case InlineKind.Autolink:
                    Wrapped(node, style | InlineStyle.Link, node.Value);
                    break;
                case InlineKind.HtmlSpan:
                    Wrapped(node, style | node.Value switch
                    {
                        "u" => InlineStyle.Underline,
                        "kbd" => InlineStyle.Keyboard,
                        "sub" => InlineStyle.Subscript,
                        "sup" => InlineStyle.Superscript,
                        _ => InlineStyle.None,
                    }, value);
                    break;
                case InlineKind.Url:
                    AddText(node.Range, parent, style | InlineStyle.Link, node, node.Value);
                    break;
                case InlineKind.Html:
                    AddText(node.Range, parent, style | InlineStyle.Html, node, value);
                    break;
                case InlineKind.SoftBreak:
                    Add(node.Range, PieceKind.LineBreak, PieceShow.Always, parent, style, node);
                    break;
                case InlineKind.HardBreak:
                {
                    int owner = Own(node);
                    if (_source.AsSpan(node.Opening.Start, node.Opening.Length).Trim(' ').IsEmpty)
                    {
                        AddText(node.Opening, owner, style, node);
                    }
                    else
                    {
                        Marker(node.Opening, owner, style, node);
                    }

                    Add(node.Content, PieceKind.LineBreak, PieceShow.Always, owner, style, node);
                    break;
                }

                case InlineKind.HtmlBreak:
                {
                    int owner = Own(node);
                    Add(node.Range, PieceKind.LineBreak, PieceShow.WhenInactive, owner, style, node);
                    Marker(node.Range, owner, style, node, PieceShow.WhenActive);
                    break;
                }

                case InlineKind.Math:
                {
                    int owner = Own(node);
                    string tex = Slice(node.Content);
                    Add(node.Range, PieceKind.Atomic, PieceShow.WhenInactive, owner, style | InlineStyle.Math, node, RunRole.Math, tex, tex);
                    Marker(node.Opening, owner, style | InlineStyle.Math, node, PieceShow.WhenActive);
                    AddText(node.Content, owner, style | InlineStyle.Math, node, tex, PieceShow.WhenActive);
                    Marker(node.Closing, owner, style | InlineStyle.Math, node, PieceShow.WhenActive);
                    break;
                }

                case InlineKind.Image:
                {
                    int owner = Own(node);
                    string alt = Slice(node.Content);
                    Add(node.Range, PieceKind.Atomic, PieceShow.WhenInactive, owner, style | InlineStyle.Image, node, RunRole.Image, alt, node.Value);
                    Marker(node.Opening, owner, style | InlineStyle.Image, node, PieceShow.WhenActive);
                    foreach (var child in node.Children)
                    {
                        VisitActiveOnly(child, owner, style);
                    }

                    Marker(node.Closing, owner, style | InlineStyle.Image, node, PieceShow.WhenActive);

                    // The image stays in the document while its source shows (10-elements.md).
                    Add(new TextRange(node.Range.End, node.Range.End), PieceKind.Atomic, PieceShow.WhenActive, owner, style | InlineStyle.Image, node, RunRole.Image, alt, node.Value);
                    break;
                }

                case InlineKind.Emoji:
                {
                    int owner = Own(node);
                    Add(node.Range, PieceKind.Atomic, PieceShow.WhenInactive, owner, style | InlineStyle.Emoji, node, RunRole.Emoji, node.Value);

                    // Active: the emoji, then its shortcode on the right (10-elements.md).
                    Add(new TextRange(node.Range.Start, node.Range.Start), PieceKind.Atomic, PieceShow.WhenActive, owner, style | InlineStyle.Emoji, node, RunRole.Emoji, node.Value);
                    Marker(node.Range, owner, style | InlineStyle.Emoji, node, PieceShow.WhenActive);
                    break;
                }

                case InlineKind.Entity:
                {
                    int owner = Own(node);
                    Add(node.Range, PieceKind.Atomic, PieceShow.WhenInactive, owner, style | InlineStyle.Entity, node, RunRole.Entity, node.Value);
                    Marker(node.Range, owner, style | InlineStyle.Entity, node, PieceShow.WhenActive);
                    break;
                }

                default:
                    AddText(node.Range, parent, style, node, value);
                    break;
            }
        }

        /// <summary>The alt text of an image: shown only while the image is active, then like other text.</summary>
        private void VisitActiveOnly(InlineNode node, int imageOwner, InlineStyle style)
        {
            int before = _pieces.Count;
            Visit(node, imageOwner, style, null);
            for (int i = before; i < _pieces.Count; i++)
            {
                _pieces[i] = _pieces[i] with { OuterGate = imageOwner };
            }
        }

        private string Slice(TextRange range) => _source[range.Start..range.End];
    }
}
