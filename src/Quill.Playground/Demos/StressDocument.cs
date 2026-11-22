using Quill.Playground.Samples;
using Quill.VariableXaml.Model;

namespace Quill.Playground.Demos;

internal enum BlockKind
{
    Heading,
    Paragraph,
    Code,
    Quote,
}

/// <summary>One block of the simulated document, with a stable id and a version bumped on edit.</summary>
internal sealed class DocumentBlock(string id, BlockKind kind, string text)
{
    public string Id { get; } = id;

    public BlockKind Kind { get; set; } = kind;

    public string Text { get; set; } = text;

    public int Version { get; set; }
}

/// <summary>
/// A stand-in for a Markdown document model: blocks with stable ids, rendered to virtual nodes
/// through the C# API. Each block's node is memoized by (id, version), so unchanged blocks reuse
/// the same VNode instance and the reconciler skips them by reference.
/// </summary>
internal sealed class StressDocument
{
    private readonly Dictionary<string, (int Version, VNode Node)> _cache = new(StringComparer.Ordinal);
    private int _nextId;

    public List<DocumentBlock> Blocks { get; } = [];

    public static StressDocument Generate(int count)
    {
        var document = new StressDocument();
        for (int i = 0; i < count; i++)
        {
            document.Blocks.Add(document.CreateBlock(i));
        }

        return document;
    }

    public DocumentBlock CreateBlock(int seed)
    {
        string id = $"b{_nextId++}";
        return (seed % 10) switch
        {
            0 => new DocumentBlock(id, BlockKind.Heading, $"Section {seed / 10 + 1}"),
            3 => new DocumentBlock(id, BlockKind.Code, $"var block{seed} = Render(node{seed}); // code block"),
            7 => new DocumentBlock(id, BlockKind.Quote, $"Quote {seed}: {SampleDocuments.Sentence(seed, 14)}"),
            _ => new DocumentBlock(id, BlockKind.Paragraph, $"Paragraph {seed} {SampleDocuments.Sentence(seed, 16)} **bold {seed}** {SampleDocuments.Sentence(seed + 1, 6)}."),
        };
    }

    public void Touch(DocumentBlock block) => block.Version++;

    /// <summary>Drops memoized nodes of blocks that no longer exist.</summary>
    public void PruneCache()
    {
        var alive = Blocks.Select(block => block.Id).ToHashSet(StringComparer.Ordinal);
        foreach (string id in _cache.Keys.Where(id => !alive.Contains(id)).ToList())
        {
            _cache.Remove(id);
        }
    }

    public VNode Render(bool virtualize)
    {
        var children = new VNode[Blocks.Count];
        for (int i = 0; i < children.Length; i++)
        {
            children[i] = RenderBlock(Blocks[i]);
        }

        VElementBuilder container = virtualize
            ? V.Element("vx:VirtualStack").Set("Margin", "32,24")
            : V.Element("StackPanel").Set("Padding", "32,24");
        return V.Element("ScrollViewer").Add(container.Set("Spacing", "12").AddRange(children));
    }

    private VNode RenderBlock(DocumentBlock block)
    {
        if (_cache.TryGetValue(block.Id, out var cached) && cached.Version == block.Version)
        {
            return cached.Node;
        }

        VNode node = block.Kind switch
        {
            BlockKind.Heading => V.Element("TextBlock").Key(block.Id).Set("Text", block.Text)
                .Set("FontSize", "22").Set("FontWeight", "SemiBold").Set("TextWrapping", "Wrap").Set("Margin", "0,12,0,0"),
            BlockKind.Code => V.Element("Border").Key(block.Id).Set("Background", "#14808080").Set("CornerRadius", "4").Set("Padding", "12")
                .Add(V.Element("TextBlock").Set("FontFamily", "Consolas").Set("Text", block.Text).Set("TextWrapping", "Wrap")),
            BlockKind.Quote => V.Element("Border").Key(block.Id).Set("BorderBrush", "#FF8A8A8A").Set("BorderThickness", "3,0,0,0").Set("Padding", "12,4")
                .Add(V.Element("TextBlock").Set("FontStyle", "Italic").Set("TextWrapping", "Wrap").Set("Text", block.Text)),
            _ => RenderParagraph(block),
        };

        _cache[block.Id] = (block.Version, node);
        return node;
    }

    /// <summary>Renders "**bold**" spans as Bold inlines, like a Markdown inline renderer would.</summary>
    private static VNode RenderParagraph(DocumentBlock block)
    {
        VElementBuilder paragraph = V.Element("TextBlock").Key(block.Id).Set("TextWrapping", "Wrap");
        string[] parts = block.Text.Split("**");
        for (int i = 0; i < parts.Length; i++)
        {
            if (parts[i].Length == 0)
            {
                continue;
            }

            paragraph.Add(i % 2 == 1 ? V.Element("Bold").Add(parts[i]) : V.Text(parts[i]));
        }

        return paragraph;
    }
}
