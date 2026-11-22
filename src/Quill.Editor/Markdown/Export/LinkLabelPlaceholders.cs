using Quill.Editor.Documents;

namespace Quill.Editor.Markdown.Export;

/// <summary>
/// Labels for link reference definitions whose label is empty. Markdown has no definition without a
/// label (Typora reads one; Markdig does not), so the writer gives such a definition a placeholder:
/// "ref1", "ref2", ..., one that no definition uses and no text mentions as "[ref1]", which the new
/// definition would turn into a link.
/// </summary>
internal sealed class LinkLabelPlaceholders(IReadOnlyList<Block> roots)
{
    private const string Stem = "ref";

    private HashSet<string>? _labels;
    private List<string>? _texts;
    private int _next = 1;

    public static bool IsBlank(string label) => label.AsSpan().Trim(" \t\n").IsEmpty;

    public string Next()
    {
        if (_labels is null || _texts is null)
        {
            _labels = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            _texts = [];
            foreach (var block in roots.SelectMany(Descendants))
            {
                if (block is LinkReferenceDefinition definition)
                {
                    _labels.Add(string.Join(' ', definition.Label.Text.Split((char[])[' ', '\t', '\n'], StringSplitOptions.RemoveEmptyEntries)));
                }
                else if (block is LeafBlock leaf && leaf.Text.Contains('['))
                {
                    _texts.Add(leaf.Text);
                }
            }
        }

        while (true)
        {
            string candidate = Stem + _next++;
            string mention = "[" + candidate + "]";
            if (!_labels.Contains(candidate) && !_texts.Any(text => text.Contains(mention, StringComparison.OrdinalIgnoreCase)))
            {
                _labels.Add(candidate);
                return candidate;
            }
        }
    }

    private static IEnumerable<Block> Descendants(Block block)
    {
        yield return block;
        if (block is ContainerBlock container)
        {
            foreach (var inner in container.Children.SelectMany(Descendants))
            {
                yield return inner;
            }
        }
    }
}
