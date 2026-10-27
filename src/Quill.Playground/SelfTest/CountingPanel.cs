using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Foundation;

namespace Quill.Playground.SelfTest;

/// <summary>
/// A pass-through panel that counts how often the layout system calls into it.
/// Used to observe measure/arrange caching when a sibling changes.
/// </summary>
internal sealed partial class CountingPanel : Panel
{
    public static int MeasureCount { get; private set; }

    public static int ArrangeCount { get; private set; }

    public static void ResetCounters()
    {
        MeasureCount = 0;
        ArrangeCount = 0;
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        MeasureCount++;
        double width = 0;
        double height = 0;
        foreach (UIElement child in Children)
        {
            child.Measure(availableSize);
            width = Math.Max(width, child.DesiredSize.Width);
            height = Math.Max(height, child.DesiredSize.Height);
        }

        return new Size(width, height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        ArrangeCount++;
        foreach (UIElement child in Children)
        {
            child.Arrange(new Rect(0, 0, finalSize.Width, finalSize.Height));
        }

        return finalSize;
    }
}
