using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Quill.Playground.VariableXaml;

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
    }

    private static string Item(string key, string text) => $"<TextBlock vx:Key=\"{key}\" Text=\"{text}\" Height=\"{ItemHeight}\"/>";
}
