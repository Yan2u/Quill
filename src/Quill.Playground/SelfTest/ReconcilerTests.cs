using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Quill.Playground.Samples;
using Quill.VariableXaml;
using Quill.VariableXaml.Metadata;
using Quill.VariableXaml.Model;

namespace Quill.Playground.SelfTest;

/// <summary>
/// End-to-end tests of VariableXamlRoot against live WinUI objects: instance preservation,
/// minimal mutations, focus across keyed moves, and differential equivalence with full reloads.
/// </summary>
internal static class ReconcilerTests
{
    public static async Task RunAsync(SelfTestContext context)
    {
        TestLineEndingNormalization(context);
        TestAttributePatch(context);
        TestInsertKeepsSiblings(context);
        TestInlineTextEdit(context);
        await TestKeyedMoveKeepsFocusAsync(context);
        TestTypeChangeReplaces(context);
        TestPropertyElements(context);
        TestResources(context);
        TestPlaceholderRecovery(context);
        await TestMarkupEventsAsync(context);
        TestCodeBuiltTrees(context);
        TestLifecycleCallbacks(context);
        TestDifferentialRandomEdits(context);
        TestDifferentialTyping(context);
        context.Stage.Children.Clear();
    }

    /// <summary>
    /// Simulates typing in the markup editor: random single-character insertions and deletions in
    /// the starter document (with CR line breaks, as WinUI's TextBox reports them). Every edit that
    /// still parses must leave the patched tree identical to a fresh mount.
    /// </summary>
    private static void TestDifferentialTyping(SelfTestContext context)
    {
        var random = new Random(99);
        context.Stage.Children.Clear();
        var incremental = new VariableXamlRoot { DeferUpdates = false };
        var full = new VariableXamlRoot { DeferUpdates = false, Strategy = UpdateStrategy.FullReload };
        context.Stage.Children.Add(incremental);
        context.Stage.Children.Add(full);

        string text = SampleDocuments.LiveEditorStarter.Replace("\n", "\r");
        incremental.Xaml = text;
        const string alphabet = "abcdef xyz 0123中<>/\"=\r{},.#*";
        int applied = 0;
        int rejected = 0;
        int mismatches = 0;
        int replacedWithoutCause = 0;
        string? firstMismatch = null;
        for (int step = 0; step < 3000; step++)
        {
            int position = random.Next(text.Length);
            string candidate = random.Next(3) == 0
                ? text.Remove(position, 1)
                : text.Insert(position, alphabet[random.Next(alphabet.Length)].ToString());
            context.Trace($"=== typing step {step}\n{candidate}");
            incremental.Xaml = candidate;
            if (!incremental.LastReport!.Succeeded)
            {
                // A mid-typing state that does not parse: the old content stays; then "undo" it.
                rejected++;
                incremental.Xaml = text;
                if (!incremental.LastReport.Succeeded)
                {
                    mismatches++;
                    firstMismatch ??= $"step {step}: reverting to the last good text failed";
                }

                continue;
            }

            applied++;
            text = candidate;
            full.Xaml = candidate;
            string left = LiveTreeSnapshot.Capture(incremental.Content);
            string right = LiveTreeSnapshot.Capture(full.Content);
            if (left != right)
            {
                mismatches++;
                firstMismatch ??= $"step {step}: {FirstDifference(left, right)}";
            }

            if (incremental.LastReport.Statistics.NodesReplaced > 0 && incremental.LastReport.Diagnostics.Count == 0
                && incremental.LastReport.Statistics.NodesCreated > 20)
            {
                replacedWithoutCause++;
            }
        }

        context.Check(mismatches == 0, $"differential typing: {applied} parseable keystrokes ({rejected} rejected and undone), patched tree == fresh mount", firstMismatch);
        context.Info($"differential typing: large replacements without diagnostics: {replacedWithoutCause}");
    }

    private static VariableXamlRoot NewHost(SelfTestContext context)
    {
        context.Stage.Children.Clear();
        var host = new VariableXamlRoot { DeferUpdates = false };
        context.Stage.Children.Add(host);
        return host;
    }

    /// <summary>WinUI's TextBox rewrites LF line breaks as CR; that must not change the tree.</summary>
    private static void TestLineEndingNormalization(SelfTestContext context)
    {
        VariableXamlRoot host = NewHost(context);
        host.Xaml = SampleDocuments.LiveEditorStarter;
        object content = host.Content;
        host.Xaml = SampleDocuments.LiveEditorStarter.Replace("\n", "\r");
        UpdateReport report = host.LastReport!;
        context.Check(ReferenceEquals(host.Content, content) && report.Statistics is { NodesCreated: 0, NodesRemoved: 0 },
            "line endings: LF -> CR re-parse changes nothing", $"{report.ParseKind} {report.ReparsedElement}; {report.Statistics}");
    }

    private static void TestAttributePatch(SelfTestContext context)
    {
        VariableXamlRoot host = NewHost(context);
        host.Xaml = "<StackPanel><TextBlock Text=\"one\" FontSize=\"20\"/><TextBlock Text=\"two\"/></StackPanel>";
        var panel = (StackPanel)host.Content;
        var first = (TextBlock)panel.Children[0];

        host.Xaml = "<StackPanel><TextBlock Text=\"one\" FontSize=\"30\"/><TextBlock Text=\"two\"/></StackPanel>";
        UpdateReport report = host.LastReport!;
        context.Check(ReferenceEquals(host.Content, panel) && ReferenceEquals(panel.Children[0], first) && first.FontSize == 30,
            "patch: changed attribute is set on the existing element");
        context.Check(report.Statistics is { PropertiesSet: 1, NodesCreated: 0, NodesRemoved: 0 }, "patch: exactly one property set, nothing created", report.Statistics.ToString());

        host.Xaml = "<StackPanel><TextBlock Text=\"one\"/><TextBlock Text=\"two\"/></StackPanel>";
        context.Check(ReferenceEquals(panel.Children[0], first) && first.ReadLocalValue(TextBlock.FontSizeProperty) == DependencyProperty.UnsetValue,
            "patch: removed attribute is cleared (ClearValue)");

        host.Xaml = "<StackPanel>\n    <TextBlock Text=\"one\"/>\n\n    <TextBlock Text=\"two\"/>\n</StackPanel>";
        context.Check(host.LastReport!.Statistics is { PropertiesSet: 0, NodesPatched: 0 }, "patch: whitespace-only markup change does no work", host.LastReport.Statistics.ToString());
    }

    private static void TestInsertKeepsSiblings(SelfTestContext context)
    {
        VariableXamlRoot host = NewHost(context);
        string[] items = Enumerable.Range(0, 6).Select(i => $"<TextBlock Text=\"item {i}\"/>").ToArray();
        host.Xaml = $"<StackPanel>{string.Concat(items)}</StackPanel>";
        var panel = (StackPanel)host.Content;
        UIElement[] before = panel.Children.ToArray();

        host.Xaml = $"<StackPanel>{string.Concat(items[..3])}<TextBlock Text=\"inserted\"/>{string.Concat(items[3..])}</StackPanel>";
        UIElement[] after = panel.Children.ToArray();
        bool preserved = after.Length == 7 && before.Take(3).SequenceEqual(after.Take(3)) && before.Skip(3).SequenceEqual(after.Skip(4));
        context.Check(preserved, "list: unkeyed insertion keeps every sibling instance");
        context.Check(host.LastReport!.Statistics is { NodesCreated: 1, NodesPatched: 1, PropertiesSet: 0, XamlReaderLoads: 1 } && host.LastReport.Diagnostics.Count == 0,
            "list: insertion creates one element (via XamlReader) and patches nothing else", host.LastReport.Statistics.ToString());

        host.Xaml = $"<StackPanel>{string.Concat(items[..3])}<TextBlock Text=\"inserted\"/>{string.Concat(items[4..])}</StackPanel>";
        context.Check(panel.Children.Count == 6 && ReferenceEquals(panel.Children[3], after[3]) && ReferenceEquals(panel.Children[4], after[5]),
            "list: deletion removes only the deleted element");
    }

    private static void TestInlineTextEdit(SelfTestContext context)
    {
        VariableXamlRoot host = NewHost(context);
        host.Xaml = "<TextBlock>Hello <Bold>World</Bold> and more</TextBlock>";
        var block = (TextBlock)host.Content;
        Inline[] inlines = block.Inlines.ToArray();
        var bold = (Bold)inlines[1];
        var inner = (Run)bold.Inlines[0];

        host.Xaml = "<TextBlock>Hello <Bold>Universe</Bold> and more</TextBlock>";
        context.Check(ReferenceEquals(block.Inlines[1], bold) && ReferenceEquals(bold.Inlines[0], inner) && inner.Text == "Universe",
            "inline: text edit inside Bold updates the existing Run");
        context.Check(host.LastReport!.Statistics is { TextUpdates: 1, NodesCreated: 0 }, "inline: one text update, no creation", host.LastReport.Statistics.ToString());

        host.Xaml = "<TextBlock Text=\"plain\"/>";
        context.Check(ReferenceEquals(host.Content, block) && block.Text == "plain" && block.Inlines.Count == 1, "inline: inline children -> Text attribute");
        host.Xaml = "<TextBlock>rich <Bold>text</Bold></TextBlock>";
        context.Check(ReferenceEquals(host.Content, block) && block.Inlines.Count == 2 && block.Inlines[1] is Bold, "inline: Text attribute -> inline children (stale Run dropped)");
    }

    private static async Task TestKeyedMoveKeepsFocusAsync(SelfTestContext context)
    {
        VariableXamlRoot host = NewHost(context);
        host.Xaml = "<StackPanel><TextBox vx:Key=\"a\"/><TextBox vx:Key=\"b\"/><TextBox vx:Key=\"c\"/><TextBox vx:Key=\"d\"/></StackPanel>";
        var panel = (StackPanel)host.Content;
        var b = (TextBox)panel.Children[1];
        await Task.Delay(150);
        b.Focus(FocusState.Programmatic);
        b.Text = "user typed";
        b.Select(2, 4);
        await Task.Delay(100);

        host.Xaml = "<StackPanel><TextBox vx:Key=\"d\"/><TextBox vx:Key=\"a\"/><TextBox vx:Key=\"c\"/><TextBox vx:Key=\"b\"/></StackPanel>";
        await Task.Delay(150);
        context.Check(ReferenceEquals(panel.Children[3], b) && b.Text == "user typed", "keyed move: element instance and user text preserved");
        context.Check(ReferenceEquals(FocusManager.GetFocusedElement(b.XamlRoot), b) && b.SelectionStart == 2 && b.SelectionLength == 4,
            "keyed move: focus and selection survive (UIElementCollection.Move)",
            $"focused {FocusManager.GetFocusedElement(b.XamlRoot)?.GetType().Name ?? "none"}, selection {b.SelectionStart}+{b.SelectionLength}");
        context.Check(host.LastReport!.Statistics is { NodesMoved: 2, NodesCreated: 0 }, "keyed move: minimal moves (two of four stay on the LIS)", host.LastReport.Statistics.ToString());
    }

    private static void TestTypeChangeReplaces(SelfTestContext context)
    {
        VariableXamlRoot host = NewHost(context);
        host.Xaml = "<StackPanel><TextBlock Text=\"a\"/><TextBlock Text=\"b\"/></StackPanel>";
        var panel = (StackPanel)host.Content;
        UIElement first = panel.Children[0];
        host.Xaml = "<StackPanel><TextBlock Text=\"a\"/><Button Content=\"b\"/></StackPanel>";
        context.Check(ReferenceEquals(panel.Children[0], first) && panel.Children[1] is Button { Content: "b" }, "replace: type change recreates only that element");
    }

    private static void TestPropertyElements(SelfTestContext context)
    {
        VariableXamlRoot host = NewHost(context);
        host.Xaml = """
            <Grid>
                <Grid.RowDefinitions><RowDefinition Height="Auto"/><RowDefinition/></Grid.RowDefinitions>
                <Border><Border.Background><SolidColorBrush Color="Red"/></Border.Background></Border>
            </Grid>
            """;
        var grid = (Grid)host.Content;
        RowDefinition firstRow = grid.RowDefinitions[0];
        var border = (Border)grid.Children[0];
        var brush = (SolidColorBrush)border.Background;

        host.Xaml = """
            <Grid>
                <Grid.RowDefinitions><RowDefinition Height="Auto"/><RowDefinition Height="2*"/><RowDefinition/></Grid.RowDefinitions>
                <Border><Border.Background><SolidColorBrush Color="Blue"/></Border.Background></Border>
            </Grid>
            """;
        context.Check(grid.RowDefinitions.Count == 3 && ReferenceEquals(grid.RowDefinitions[0], firstRow), "property element: RowDefinitions insertion keeps existing rows");
        context.Check(ReferenceEquals(border.Background, brush) && brush.Color == Microsoft.UI.Colors.Blue, "property element: brush patched in place");
    }

    private static void TestResources(SelfTestContext context)
    {
        VariableXamlRoot host = NewHost(context);
        host.Xaml = "<TextBlock Text=\"r\" Foreground=\"{ThemeResource TextFillColorSecondaryBrush}\"/>";
        var block = (TextBlock)host.Content;
        context.Check(ReferenceEquals(block.Foreground, Application.Current.Resources["TextFillColorSecondaryBrush"]) || block.Foreground is SolidColorBrush,
            "resources: ThemeResource resolved at mount (XamlReader)");

        host.Xaml = "<TextBlock Text=\"r\" Foreground=\"{StaticResource AccentTextFillColorPrimaryBrush}\"/>";
        context.Check(ReferenceEquals(host.Content, block) && ReferenceEquals(block.Foreground, Application.Current.Resources["AccentTextFillColorPrimaryBrush"]),
            "resources: StaticResource resolved by a patch (object model)");

        host.Xaml = """
            <StackPanel>
                <StackPanel.Resources><SolidColorBrush x:Key="LocalBrush" Color="Orange"/></StackPanel.Resources>
                <TextBlock Text="local"/>
            </StackPanel>
            """;
        host.Xaml = """
            <StackPanel>
                <StackPanel.Resources><SolidColorBrush x:Key="LocalBrush" Color="Orange"/></StackPanel.Resources>
                <TextBlock Text="local" Foreground="{StaticResource LocalBrush}"/>
            </StackPanel>
            """;
        var local = (TextBlock)((StackPanel)host.Content).Children[0];
        context.Check(local.Foreground is SolidColorBrush { Color: var color } && color == Microsoft.UI.Colors.Orange,
            "resources: StaticResource from a dynamic ancestor's Resources", string.Join(" | ", host.LastReport!.Diagnostics));
    }

    private static void TestPlaceholderRecovery(SelfTestContext context)
    {
        VariableXamlRoot host = NewHost(context);
        host.Xaml = "<StackPanel><TextBlock Text=\"ok\"/><TextBlok Text=\"typo\"/></StackPanel>";
        var panel = (StackPanel)host.Content;
        context.Check(panel.Children.Count == 2 && panel.Children[1] is Border && host.LastReport!.Diagnostics.Count > 0,
            "placeholder: unknown element shows a placeholder and a diagnostic", host.LastReport!.Diagnostics.FirstOrDefault());

        host.Xaml = "<StackPanel><TextBlock Text=\"ok\"/><TextBlock Text=\"typo fixed\"/></StackPanel>";
        context.Check(panel.Children[1] is TextBlock { Text: "typo fixed" }, "placeholder: fixing the markup replaces the placeholder");

        host.Xaml = "<StackPanel><TextBlock Text=\"ok\" FontSize=\"huge\"/></StackPanel>";
        context.Check(host.LastReport!.Succeeded && host.LastReport.Diagnostics.Any(message => message.Contains("FontSize")),
            "diagnostics: bad attribute value is reported, rest still applied", host.LastReport.Diagnostics.FirstOrDefault());
    }

    private static async Task TestMarkupEventsAsync(SelfTestContext context)
    {
        VariableXamlRoot host = NewHost(context);
        string? fired = null;
        host.MarkupEvent += (_, args) => fired = args.HandlerName;
        host.Xaml = "<StackPanel><Button Content=\"Go\" Click=\"OnGo\"/></StackPanel>";
        var button = (Button)((StackPanel)host.Content).Children[0];
        await Task.Delay(100);
        new ButtonAutomationPeer(button).Invoke();
        await Task.Delay(100);
        context.Check(fired == "OnGo", "events: markup event attribute dispatches to MarkupEvent", fired);

        host.Xaml = "<StackPanel><Button Content=\"Go\" Click=\"OnOther\"/></StackPanel>";
        new ButtonAutomationPeer(button).Invoke();
        await Task.Delay(100);
        context.Check(fired == "OnOther", "events: changing the handler name swaps the subscription", fired);
    }

    private static void TestCodeBuiltTrees(SelfTestContext context)
    {
        VariableXamlRoot host = NewHost(context);
        VNode[] blocks = Enumerable.Range(0, 50)
            .Select(i => (VNode)V.Element("TextBlock").Key($"b{i}").Set("Text", $"Block {i}").Set("TextWrapping", "Wrap"))
            .ToArray();
        host.Render(V.Element("StackPanel").Set("Spacing", 4d).AddRange(blocks));
        var panel = (StackPanel)host.Content;
        UIElement block10 = panel.Children[10];

        // Memoized rendering: unchanged blocks are the same VNode instances, the edited one is new.
        blocks[10] = V.Element("TextBlock").Key("b10").Set("Text", "Block 10 edited").Set("TextWrapping", "Wrap");
        host.Render(V.Element("StackPanel").Set("Spacing", 4d).AddRange(blocks));
        UpdateReport report = host.LastReport!;
        context.Check(ReferenceEquals(panel.Children[10], block10) && ((TextBlock)block10).Text == "Block 10 edited", "code API: keyed block patched in place");
        context.Check(report.Statistics is { NodesPatched: 2, SubtreesSkipped: 49, PropertiesSet: 1 }, "code API: 49 memoized blocks skipped by reference", report.Statistics.ToString());
    }

    private static void TestLifecycleCallbacks(SelfTestContext context)
    {
        LifecycleProbePanel.EnsureRegistered();
        VariableXamlRoot host = NewHost(context);
        const string ns = "xmlns:probe=\"using:Quill.Playground.SelfTest\"";

        host.Xaml = $"<StackPanel {ns}><probe:LifecycleProbePanel vx:Key=\"a\"><probe:LifecycleProbePanel vx:Key=\"b\"/></probe:LifecycleProbePanel></StackPanel>";
        var rootPanel = (StackPanel)host.Content;
        var outer = (LifecycleProbePanel)rootPanel.Children[0];
        var inner = (LifecycleProbePanel)outer.Children[0];
        context.Check(host.LastReport!.Diagnostics.Count == 0, "lifecycle: registered probe element mounts without diagnostics",
            host.LastReport.Diagnostics.FirstOrDefault());

        host.Xaml = $"<StackPanel {ns}/>";
        context.Check(outer.UnmountedCount == 1 && inner.UnmountedCount == 1,
            "lifecycle: removing a subtree invokes OnUnmounted on parent and child",
            $"outer={outer.UnmountedCount}, inner={inner.UnmountedCount}");

        host.Xaml = $"<StackPanel {ns}><probe:LifecycleProbePanel/></StackPanel>";
        var replaced = (LifecycleProbePanel)((StackPanel)host.Content).Children[0];
        host.Xaml = "<StackPanel><TextBlock Text=\"done\"/></StackPanel>";
        context.Check(replaced.UnmountedCount == 1,
            "lifecycle: type-change replacement invokes OnUnmounted",
            $"replaced={replaced.UnmountedCount}");

        host.Xaml = $"<StackPanel {ns}><probe:LifecycleProbePanel><probe:LifecycleProbePanel/></probe:LifecycleProbePanel></StackPanel>";
        var onResetOuter = (LifecycleProbePanel)((StackPanel)host.Content).Children[0];
        var onResetInner = (LifecycleProbePanel)onResetOuter.Children[0];
        host.Reset();
        context.Check(onResetOuter.UnmountedCount == 1 && onResetInner.UnmountedCount == 1,
            "lifecycle: VariableXamlRoot.Reset invokes OnUnmounted on mounted elements",
            $"outer={onResetOuter.UnmountedCount}, inner={onResetInner.UnmountedCount}");
    }

    /// <summary>
    /// Applies random block-level edits incrementally to one host and as full reloads to another;
    /// after every step both live trees must be identical.
    /// </summary>
    private static void TestDifferentialRandomEdits(SelfTestContext context)
    {
        var random = new Random(4242);
        var blocks = Enumerable.Range(0, 40).Select(SampleDocuments.Block).ToList();
        context.Stage.Children.Clear();
        var incremental = new VariableXamlRoot { DeferUpdates = false };
        var full = new VariableXamlRoot { DeferUpdates = false, Strategy = UpdateStrategy.FullReload };
        context.Stage.Children.Add(incremental);
        context.Stage.Children.Add(full);

        int mismatches = 0;
        int created = 0;
        int totalNodes = 0;
        string? firstMismatch = null;
        for (int step = 0; step < 150; step++)
        {
            int index = random.Next(blocks.Count);
            switch (random.Next(7))
            {
                case 0:
                    blocks.Insert(index, SampleDocuments.Block(random.Next(1000)));
                    break;
                case 1 when blocks.Count > 5:
                    blocks.RemoveAt(index);
                    break;
                case 2:
                    int other = random.Next(blocks.Count);
                    (blocks[index], blocks[other]) = (blocks[other], blocks[index]);
                    break;
                case 3:
                    blocks[index] = blocks[index].Replace("Wrap\"", "Wrap\" FontSize=\"18\"");
                    break;
                case 4:
                    blocks[index] = blocks[index].Replace("<Bold>", "<Italic>").Replace("</Bold>", "</Italic>");
                    break;
                case 5:
                    // Unkeyed replacement: pairs by type, so a Text-attribute TextBlock may become an inline one.
                    blocks[index] = SampleDocuments.Block(random.Next(1000));
                    break;
                default:
                    blocks[index] = blocks[index].Replace("editor", "editing").Replace("render", $"render{step}");
                    break;
            }

            string markup = $"<StackPanel Spacing=\"12\">{string.Join("\n", blocks)}</StackPanel>";
            incremental.Xaml = markup;
            full.Xaml = markup;
            created += incremental.LastReport!.Statistics.NodesCreated;
            totalNodes += incremental.LastReport.TreeNodeCount;
            string left = LiveTreeSnapshot.Capture(incremental.Content);
            string right = LiveTreeSnapshot.Capture(full.Content);
            if (left != right)
            {
                mismatches++;
                firstMismatch ??= $"step {step}: {FirstDifference(left, right)}";
            }
        }

        context.Check(mismatches == 0, "differential: 150 random edits, patched tree == freshly mounted tree", firstMismatch);
        context.Info($"differential: incremental updates created {created} nodes in total vs {totalNodes} for full reloads ({100.0 * created / Math.Max(1, totalNodes):F1}%)");
    }

    private static string FirstDifference(string left, string right)
    {
        string[] a = left.Split('\n');
        string[] b = right.Split('\n');
        for (int i = 0; i < Math.Min(a.Length, b.Length); i++)
        {
            if (a[i] != b[i])
            {
                return $"line {i}: '{a[i].Trim()}' vs '{b[i].Trim()}'";
            }
        }

        return $"length {a.Length} vs {b.Length}";
    }
}

internal sealed partial class LifecycleProbePanel : StackPanel, IVirtualLifecycle
{
    private static bool _registered;

    public int PooledCount { get; private set; }

    public int UnmountedCount { get; private set; }

    public static void EnsureRegistered()
    {
        if (_registered)
        {
            return;
        }

        _registered = true;
        var name = new XamlName("using:Quill.Playground.SelfTest", nameof(LifecycleProbePanel));
        XamlSchema.Default.Register(
            new ElementRegistration<LifecycleProbePanel>(name, static () => new())
                .Property<double>("Height", static (p, v) => p.Height = v, double.NaN)
                .Children("Children", static p => p.Children));
    }

    public void OnPooled() => PooledCount++;

    public void OnUnmounted() => UnmountedCount++;
}
