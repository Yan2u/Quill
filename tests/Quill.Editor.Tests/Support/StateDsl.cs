using System.Text;
using Quill.Editor.Documents;
using Quill.Editor.Markdown;
using Quill.Editor.State;

namespace Quill.Editor.Tests.Support;

/// <summary>
/// The state DSL of 08-testing-and-debugging.md: Markdown with markers for the selection and block
/// IDs. <c>&lt;|&gt;</c> is the caret; <c>&lt;^&gt;</c> and <c>&lt;$&gt;</c> are the anchor and head
/// of a selection; <c>&lt;empty&gt;</c> on its own line is an empty paragraph (a line with only a
/// caret is one too); <c>&lt;gap&gt;</c> on its own line is a caret at a block boundary (a virtual
/// paragraph); <c>&lt;#name&gt;</c> at the start of a block names it. Extension: <c>&lt;gap:^&gt;</c>
/// and <c>&lt;gap:$&gt;</c> are selection ends at block boundaries (a block selected whole);
/// <c>&lt;block&gt;</c> on its own line is the block caret on the block after it (a TOC, Q59).
/// In DSL text an empty paragraph is written like any other paragraph, with blank lines around it.
/// </summary>
public static class StateDsl
{
    private const char Caret = '';
    private const char Anchor = '';
    private const char Head = '';
    private const char Empty = '';
    private const char Gap = '';
    private const char GapAnchor = '';
    private const char GapHead = '';
    private const char BlockCaret = '';

    private static readonly (string Marker, char Sentinel)[] Markers =
    [
        ("<|>", Caret), ("<^>", Anchor), ("<$>", Head), ("<empty>", Empty),
        ("<gap>", Gap), ("<gap:^>", GapAnchor), ("<gap:$>", GapHead), ("<block>", BlockCaret),
    ];

    /// <summary>A loaded state and the names given to blocks.</summary>
    public sealed record Loaded(EditorState State, IReadOnlyDictionary<string, BlockId> Names);

    public static Loaded Load(string dsl, IBlockIdSource ids)
    {
        var (markdown, nameOffsets) = Encode(dsl);
        var document = MarkdownImporter.Import(markdown, ids).Document;
        var names = new Dictionary<string, BlockId>();
        if (nameOffsets.Count > 0)
        {
            string serialized = MarkdownSerializer.SerializeNormalized(document, out var starts);
            Assert.Equal(markdown, serialized);
            foreach (var (name, offset) in nameOffsets)
            {
                var block = starts.Where(start => start.Value == offset)
                    .Select(start => start.Key)
                    .MaxBy(id => DocumentTree.Locate(document, id).Ancestors.Length);
                names[name] = block != default ? block : throw new InvalidOperationException($"No block starts at <#{name}>.");
            }
        }

        var found = new Dictionary<char, Position>();
        document = (Document)Clean(document, found);
        Position? anchor = found.GetValueOrDefault(Anchor) ?? found.GetValueOrDefault(GapAnchor);
        Position? head = found.GetValueOrDefault(Head) ?? found.GetValueOrDefault(GapHead);
        Position caret = found.GetValueOrDefault(Caret) ?? found.GetValueOrDefault(Gap) ?? head ?? FirstPosition(document);
        var selection = found.GetValueOrDefault(BlockCaret) is BoundaryPosition onBlock
            ? TextSelection.BlockCaret(onBlock.Parent, onBlock.Index)
            : anchor is not null && head is not null ? new TextSelection(anchor, head) : TextSelection.Caret(caret);
        return new Loaded(new EditorState { Document = document, Selection = selection }, names);
    }

    public static string Dump(EditorState state, IReadOnlyDictionary<string, BlockId>? names = null, EditorOptions? options = null)
    {
        var document = state.Document;
        var selection = state.Selection;
        var marks = new List<(Position Position, char Text, char Gap)>();
        if (selection.IsBlockCaret)
        {
            marks.Add((selection.Anchor, BlockCaret, BlockCaret));
        }
        else if (selection.IsCollapsed)
        {
            marks.Add((selection.Head, Caret, Gap));
        }
        else
        {
            marks.Add((selection.Anchor, Anchor, GapAnchor));
            marks.Add((selection.Head, Head, GapHead));
        }

        // Text marks from the highest offset down; boundary marks from the highest index down.
        foreach (var (position, text, _) in marks.Where(mark => mark.Position is TextPosition).OrderByDescending(mark => ((TextPosition)mark.Position).Offset))
        {
            var at = (TextPosition)position;
            var leaf = DocumentTree.Find<LeafBlock>(document, at.Block);
            document = DocumentTree.Replace(document, leaf with { Text = leaf.Text.Insert(at.Offset, text.ToString()) }, document.Revision);
        }

        foreach (var (position, _, gap) in marks.Where(mark => mark.Position is BoundaryPosition).OrderByDescending(mark => ((BoundaryPosition)mark.Position).Index))
        {
            var at = (BoundaryPosition)position;
            document = DocumentTree.Insert(document, at.Parent, at.Index, [new Paragraph { Id = new BlockId(-1 - at.Index), Text = gap.ToString() }], document.Revision);
        }

        document = (Document)MarkEmpty(document);
        string markdown = MarkdownSerializer.SerializeNormalized(document, out var starts, options);
        if (names is not null)
        {
            var inserts = names
                .Where(name => starts.ContainsKey(name.Value))
                .Select(name => (Offset: starts[name.Value], Depth: DocumentTree.Locate(document, name.Value).Ancestors.Length, Text: $"<#{name.Key}>"))
                .OrderByDescending(insert => insert.Offset)
                .ThenByDescending(insert => insert.Depth);
            foreach (var insert in inserts)
            {
                markdown = markdown.Insert(insert.Offset, insert.Text);
            }
        }

        var builder = new StringBuilder(markdown);
        foreach (var (marker, sentinel) in Markers)
        {
            builder.Replace(sentinel.ToString(), marker);
        }

        return builder.ToString();
    }

    /// <summary>Replaces markers with sentinels; names are removed and their offsets recorded.</summary>
    private static (string Markdown, List<(string Name, int Offset)> Names) Encode(string dsl)
    {
        var builder = new StringBuilder(dsl.Length);
        var names = new List<(string, int)>();
        int i = 0;
        while (i < dsl.Length)
        {
            if (dsl[i] == '<')
            {
                if (dsl.AsSpan(i).StartsWith("<#"))
                {
                    int close = dsl.IndexOf('>', i);
                    names.Add((dsl[(i + 2)..close], builder.Length));
                    i = close + 1;
                    continue;
                }

                var match = Markers.FirstOrDefault(marker => dsl.AsSpan(i).StartsWith(marker.Marker));
                if (match.Marker is not null)
                {
                    builder.Append(match.Sentinel);
                    i += match.Marker.Length;
                    continue;
                }
            }

            builder.Append(dsl[i]);
            i++;
        }

        return (builder.ToString(), names);
    }

    /// <summary>Removes sentinels from leaves (recording positions) and gap paragraphs from containers.</summary>
    private static Block Clean(Block block, Dictionary<char, Position> found)
    {
        if (block is LeafBlock leaf)
        {
            if (leaf is Paragraph && leaf.Text == Empty.ToString())
            {
                return leaf with { Text = string.Empty };
            }

            var text = new StringBuilder(leaf.Text.Length);
            foreach (char c in leaf.Text)
            {
                if (c is Caret or Anchor or Head)
                {
                    found[c] = new TextPosition(leaf.Id, text.Length);
                }
                else
                {
                    text.Append(c);
                }
            }

            return text.Length == leaf.Text.Length ? leaf : leaf with { Text = text.ToString() };
        }

        if (block is not ContainerBlock container)
        {
            return block;
        }

        var children = new List<Block>(container.Children.Count);
        foreach (var child in container.Children)
        {
            if (child is Paragraph { Text: [GapAnchor or GapHead or Gap or BlockCaret] } gap)
            {
                found[gap.Text[0]] = new BoundaryPosition(container.Id, children.Count);
                continue;
            }

            children.Add(Clean(child, found));
        }

        // A marker in a code block's language was read as part of its opening fence too (Q91).
        if (container is CodeFence { Syntax.Fence: { } fence } code)
        {
            string Strip(string text) => new([.. text.Where(static c => c is not (Caret or Anchor or Head))]);
            container = code with
            {
                Syntax = code.Syntax with { Fence = fence with { Info = Strip(fence.Info), Opening = fence.Opening with { Text = Strip(fence.Opening.Text) } } },
            };
        }

        return container with { Children = [.. children] };
    }

    private static Block MarkEmpty(Block block) => block switch
    {
        Paragraph { Text.Length: 0 } paragraph => paragraph with { Text = Empty.ToString() },
        ContainerBlock container => container with { Children = [.. container.Children.Select(MarkEmpty)] },
        _ => block,
    };

    private static Position FirstPosition(Document document)
    {
        var leaf = document.Children.Count > 0 ? FirstLeaf(document) : null;
        return leaf is not null ? new TextPosition(leaf.Id, 0) : new BoundaryPosition(document.Id, 0);

        static LeafBlock? FirstLeaf(Block block) => block switch
        {
            LeafBlock leaf => leaf,
            ContainerBlock container => container.Children.Select(FirstLeaf).FirstOrDefault(found => found is not null),
            _ => null,
        };
    }
}
