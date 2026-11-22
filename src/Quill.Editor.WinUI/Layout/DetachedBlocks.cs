using Microsoft.UI.Xaml;
using Quill.Editor.WinUI.Elements;
using Quill.Editor.WinUI.Theming;
using Quill.VariableXaml.Metadata;
using Quill.VariableXaml.Model;
using Windows.Foundation;

namespace Quill.Editor.WinUI.Layout;

/// <summary>
/// Top-level blocks laid out outside the element tree (Q71): the block's VNode is mounted into a
/// detached tree through the registered element factories and typed setters (no reconciler, no
/// XamlReader), then measured and arranged at the column width. The layout service reads its
/// geometry like a realized block's; the virtual stack is never asked to realize a far block (Q64).
/// A few recent trees are kept, keyed by the node instance, so repeated queries are cheap.
/// </summary>
internal sealed class DetachedBlocks
{
    private const int Capacity = 8;

    private readonly XamlSchema _schema;
    private readonly LinkedList<(VNode Node, double Width, MdStyleSheet Sheet, MdViewOptions Options, MdBlockView Root)> _recent = new();

    public DetachedBlocks(XamlSchema? schema = null)
    {
        _schema = schema ?? XamlSchema.Default;
    }

    /// <summary>The detached, laid out tree of a top-level block, or null when it is not an element control.</summary>
    public MdBlockView? Get(VNode node, double width, MdStyleSheet sheet, MdViewOptions options)
    {
        for (var entry = _recent.First; entry is not null; entry = entry.Next)
        {
            if (ReferenceEquals(entry.Value.Node, node) && entry.Value.Width.Equals(width) && ReferenceEquals(entry.Value.Sheet, sheet) && ReferenceEquals(entry.Value.Options, options))
            {
                _recent.Remove(entry);
                _recent.AddFirst(entry);
                return entry.Value.Root;
            }
        }

        if (Mount(node) is not MdBlockView root)
        {
            return null;
        }

        root.DetachedContext = new BlockContext(sheet, sheet.PageBackground, sheet.Foreground, Options: options);
        root.Measure(new Size(width, double.PositiveInfinity));
        root.Arrange(new Rect(0, 0, width, root.DesiredSize.Height));
        _recent.AddFirst((node, width, sheet, options, root));
        if (_recent.Count > Capacity && _recent.Last is { } evicted)
        {
            _recent.RemoveLast();
            UnmountTree(evicted.Value.Root);
        }

        return root;
    }

    public void Clear()
    {
        foreach (var entry in _recent)
        {
            UnmountTree(entry.Root);
        }

        _recent.Clear();
    }

    private static void UnmountTree(MdBlockView root)
    {
        foreach (UIElement child in root.Children.ToList())
        {
            if (child is MdBlockView childBlock)
            {
                UnmountTree(childBlock);
            }
        }

        root.UnmountBlock();
    }

    private UIElement? Mount(VNode node)
    {
        if (node.IsText || _schema.GetType(node.Type) is not { IsRegistered: true, Factory: { } factory } type)
        {
            return null;
        }

        var element = (UIElement)factory();
        foreach (var attribute in node.Attributes)
        {
            if (attribute.Name.Kind == XamlMemberKind.Plain && type.ResolveMember(attribute.Name) is { Category: XamlMemberCategory.Property } member)
            {
                member.SetValue(element, attribute.Value);
            }
        }

        if (type.ContentMember is { Category: XamlMemberCategory.Collection } content && content.GetValue(element) is IList<UIElement> children)
        {
            foreach (var child in node.Children)
            {
                if (Mount(child) is { } mounted)
                {
                    children.Add(mounted);
                }
            }
        }

        return element;
    }
}
