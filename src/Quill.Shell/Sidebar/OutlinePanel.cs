using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Quill.Editor.Documents;
using Quill.Editor.Statistics;
using Quill.Editor.WinUI.Localization;

namespace Quill.Shell.Sidebar;

/// <summary>
/// The outline sidebar (M6.5): the document's headings as a native WinUI <see cref="TreeView"/>, nested
/// by level, all expanded at first; what the user collapsed stays collapsed while the outline is rebuilt.
/// The current heading (the section in view) is selected; invoking a heading raises <see cref="HeadingInvoked"/>.
/// </summary>
public sealed partial class OutlinePanel : UserControl
{
    private readonly TreeView _tree = new() { SelectionMode = TreeViewSelectionMode.Single, CanDragItems = false, CanReorderItems = false };
    private readonly TextBlock _empty = new() { Margin = new Thickness(16, 8, 16, 8), Opacity = 0.6 };
    private readonly Dictionary<TreeViewNode, BlockId> _ids = [];
    private readonly Dictionary<BlockId, TreeViewNode> _nodes = [];
    private readonly HashSet<BlockId> _collapsed = [];
    private IReadOnlyList<OutlineEntry> _entries = [];

    public OutlinePanel()
    {
        // Long headings wrap instead of scrolling sideways (13-app-shell.md).
        _tree.ItemTemplate = (DataTemplate)Microsoft.UI.Xaml.Markup.XamlReader.Load(
            "<DataTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'>"
            + "<TextBlock Text='{Binding Content}' TextWrapping='Wrap' Padding='0,5,4,5' /></DataTemplate>");

        // The sidebar's tabs name the panel (Sidebar/SidebarPanel.cs).
        var layout = new Grid();
        layout.Children.Add(_tree);
        layout.Children.Add(_empty);
        Content = layout;

        _tree.ItemInvoked += (_, args) =>
        {
            if (args.InvokedItem is TreeViewNode node && _ids.TryGetValue(node, out var id))
            {
                HeadingInvoked?.Invoke(this, id);
            }
        };
        _tree.Collapsed += (_, args) => Remember(args.Node, collapsed: true);
        _tree.Expanding += (_, args) => Remember(args.Node, collapsed: false);

        ApplyLanguage();
        Loaded += (_, _) =>
        {
            UiStrings.LanguageChanged -= OnLanguageChanged;
            UiStrings.LanguageChanged += OnLanguageChanged;
            ApplyLanguage();
        };
        Unloaded += (_, _) =>
        {
            if (!IsLoaded)
            {
                UiStrings.LanguageChanged -= OnLanguageChanged;
            }
        };
    }

    private void OnLanguageChanged(object? sender, EventArgs e) => ApplyLanguage();

    internal void RefreshLanguage() => ApplyLanguage();

    private void ApplyLanguage()
    {
        _empty.Text = UiStrings.Get("sidebar.outline.empty");
        string emptyHeading = UiStrings.Get("sidebar.outline.emptyHeading");
        foreach (var entry in _entries)
        {
            if (entry.Text.Length == 0 && _nodes.TryGetValue(entry.Id, out var node))
            {
                node.Content = emptyHeading;
            }
        }
    }

    public event EventHandler<BlockId>? HeadingInvoked;

    /// <summary>The headings shown, for tests.</summary>
    public IReadOnlyList<OutlineEntry> Entries => _entries;

    /// <summary>The heading selected as current, or null.</summary>
    public BlockId? Current => _tree.SelectedNode is { } node && _ids.TryGetValue(node, out var id) ? id : null;

    /// <summary>Shows these headings; nothing happens when they are the ones shown.</summary>
    public void Update(IReadOnlyList<OutlineEntry> entries)
    {
        if (entries.SequenceEqual(_entries))
        {
            return;
        }

        var current = Current;
        _entries = entries;
        _ids.Clear();
        _nodes.Clear();
        _tree.RootNodes.Clear();
        string emptyHeading = UiStrings.Get("sidebar.outline.emptyHeading");
        var stack = new List<(int Level, TreeViewNode Node)>();
        foreach (var entry in entries)
        {
            var node = new TreeViewNode { Content = entry.Text.Length > 0 ? entry.Text : emptyHeading };
            _ids[node] = entry.Id;
            _nodes[entry.Id] = node;
            while (stack.Count > 0 && stack[^1].Level >= entry.Level)
            {
                stack.RemoveAt(stack.Count - 1);
            }

            if (stack.Count == 0)
            {
                _tree.RootNodes.Add(node);
            }
            else
            {
                stack[^1].Node.Children.Add(node);
            }

            stack.Add((entry.Level, node));
        }

        foreach (var (id, node) in _nodes)
        {
            node.IsExpanded = !_collapsed.Contains(id);
        }

        _collapsed.IntersectWith(_nodes.Keys);
        _empty.Visibility = entries.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        SetCurrent(current);
    }

    /// <summary>Selects the current heading, or the collapsed heading that hides it, and scrolls it into view.</summary>
    public void SetCurrent(BlockId? heading)
    {
        if (heading is not { } id || !_nodes.TryGetValue(id, out var node))
        {
            _tree.SelectedNode = null;
            return;
        }

        for (var parent = node.Parent; parent is not null && _ids.ContainsKey(parent); parent = parent.Parent)
        {
            if (!parent.IsExpanded)
            {
                node = parent;
            }
        }

        if (!ReferenceEquals(_tree.SelectedNode, node))
        {
            _tree.SelectedNode = node;
            (_tree.ContainerFromNode(node) as UIElement)?.StartBringIntoView(new BringIntoViewOptions { AnimationDesired = false });
        }
    }

    private void Remember(TreeViewNode node, bool collapsed)
    {
        if (!_ids.TryGetValue(node, out var id))
        {
            return;
        }

        if (collapsed)
        {
            _collapsed.Add(id);
        }
        else
        {
            _collapsed.Remove(id);
        }
    }
}
