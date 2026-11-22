using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Quill.VariableXaml;

namespace Quill.Playground.SelfTest;

/// <summary>Tests of <c>vx:VirtualStack</c>: realization window, in-place patches, scrolling, insertion.</summary>
internal static class VirtualStackTests
{
    private const double ItemHeight = 30;
    private const double Spacing = 2;

    public static async Task RunAsync(SelfTestContext context)
    {
        context.Stage.Children.Clear();
        var host = new VariableXamlRoot { DeferUpdates = false, Height = 400, VerticalAlignment = VerticalAlignment.Top };
        context.Stage.Children.Add(host);

        List<string> blocks = Enumerable.Range(0, 1000).Select(i => Item($"p{i}", $"Item {i}")).ToList();
        string Markup() => $"<ScrollViewer><vx:VirtualStack Spacing=\"{Spacing}\">{string.Concat(blocks)}</vx:VirtualStack></ScrollViewer>";

        host.Xaml = Markup();
        await Task.Delay(300);
        VirtualizationSummary summary = host.GetVirtualizationSummary();
        context.Check(summary.Items == 1000 && summary.Realized is > 0 and < 150, "virtual: only items near the viewport are realized", summary.ToString());

        var scroller = (ScrollViewer)host.Content;
        var repeater = (ItemsRepeater)scroller.Content;
        UIElement? second = repeater.TryGetElement(2);
        blocks[2] = Item("p2", "Item 2 edited");
        host.Xaml = Markup();
        context.Check(ReferenceEquals(repeater.TryGetElement(2), second) && second is TextBlock { Text: "Item 2 edited" }, "virtual: realized item patched in place");

        blocks[900] = Item("p900", "Item 900 edited");
        host.Xaml = Markup();
        context.Check(host.LastReport!.Statistics.NodesCreated == 0, "virtual: editing an unrealized item creates nothing", host.LastReport.Statistics.ToString());

        scroller.ChangeView(null, 899 * (ItemHeight + Spacing), null, disableAnimation: true);
        await Task.Delay(400);
        context.Check(repeater.TryGetElement(900) is TextBlock { Text: "Item 900 edited" }, "virtual: item scrolled into view shows its latest content");

        blocks.Insert(0, Item("top", "Inserted at top"));
        host.Xaml = Markup();
        await Task.Delay(300);
        summary = host.GetVirtualizationSummary();
        context.Check(summary.Items == 1001 && repeater.TryGetElement(901) is TextBlock { Text: "Item 900 edited" },
            "virtual: insertion shifts realized items", summary.ToString());
        context.Info($"virtual: {summary.Items} items, {summary.Realized} realized, {summary.CreatedTotal} elements created, {summary.ReusedTotal} reused from the pool");
        context.Stage.Children.Clear();

        await TestKeyedContainerRecyclingAsync(context);
    }

    private static async Task TestKeyedContainerRecyclingAsync(SelfTestContext context)
    {
        LifecycleProbePanel.EnsureRegistered();
        context.Stage.Children.Clear();
        var host = new VariableXamlRoot { DeferUpdates = false, Height = 240, VerticalAlignment = VerticalAlignment.Top };
        context.Stage.Children.Add(host);

        string rows = string.Concat(Enumerable.Range(0, 60).Select(i =>
            $"<probe:LifecycleProbePanel vx:Key=\"r{i}\" Height=\"40\"><probe:LifecycleProbePanel vx:Key=\"c{i}\" Height=\"20\"><TextBlock Text=\"Row {i}\"/></probe:LifecycleProbePanel></probe:LifecycleProbePanel>"));
        host.Xaml = $"<ScrollViewer xmlns:probe=\"using:Quill.Playground.SelfTest\"><vx:VirtualStack>{rows}</vx:VirtualStack></ScrollViewer>";
        await Task.Delay(300);

        var scroller = (ScrollViewer)host.Content;
        var repeater = (ItemsRepeater)scroller.Content;
        var initial = new Dictionary<LifecycleProbePanel, LifecycleProbePanel>();
        for (int i = 0; i < 60; i++)
        {
            if (repeater.TryGetElement(i) is LifecycleProbePanel outer && outer.Children.FirstOrDefault() is LifecycleProbePanel inner)
            {
                initial[outer] = inner;
            }
        }

        scroller.ChangeView(null, scroller.ScrollableHeight, null, disableAnimation: true);
        await Task.Delay(400);

        VirtualizationSummary summary = host.GetVirtualizationSummary();
        var reusedPairs = new List<(LifecycleProbePanel Outer, LifecycleProbePanel ExpectedInner)>();
        for (int i = 40; i < 60; i++)
        {
            if (repeater.TryGetElement(i) is LifecycleProbePanel outer && initial.TryGetValue(outer, out LifecycleProbePanel? expectedInner))
            {
                reusedPairs.Add((outer, expectedInner));
            }
        }

        bool reusedCleanly = reusedPairs.Count > 0 && reusedPairs.All(static pair =>
            pair.Outer.PooledCount >= 1
            && pair.ExpectedInner.PooledCount >= 1
            && pair.Outer.UnmountedCount == 0
            && pair.ExpectedInner.UnmountedCount == 0
            && pair.Outer.Children.Count == 1
            && ReferenceEquals(pair.Outer.Children[0], pair.ExpectedInner));
        context.Check(
            initial.Count > 0 && summary.ReusedTotal > 0 && reusedCleanly,
            "virtual: recycling a keyed container reuses nested keyed children in place and invokes OnPooled",
            $"initial={initial.Count}, reusedTotal={summary.ReusedTotal}, bottomMatched={reusedPairs.Count}, pooledOuter={reusedPairs.FirstOrDefault().Outer?.PooledCount}, pooledInner={reusedPairs.FirstOrDefault().ExpectedInner?.PooledCount}, unmountInner={reusedPairs.FirstOrDefault().ExpectedInner?.UnmountedCount}");

        host.Reset();
        bool allUnmounted = initial.All(static kv => kv.Key.UnmountedCount == 1 && kv.Value.UnmountedCount == 1);
        context.Check(allUnmounted, "virtual: Reset unmounts realized and pooled nested elements");
        context.Stage.Children.Clear();
    }

    private static string Item(string key, string text) => $"<TextBlock vx:Key=\"{key}\" Text=\"{text}\" Height=\"{ItemHeight}\"/>";
}
