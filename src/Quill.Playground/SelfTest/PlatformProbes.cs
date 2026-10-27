using System.Diagnostics;
using System.Reflection;
using System.Text;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Markup;
using Microsoft.UI.Xaml.Media;

namespace Quill.Playground.SelfTest;

/// <summary>
/// Probes WinUI runtime behaviors that the VariableXamlRoot design relies on.
/// Most results are observations (logged as info); hard requirements are checks.
/// </summary>
internal static class PlatformProbes
{
    private const string Xmlns = "xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\"";
    private const string XmlnsX = "xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\"";

    public static async Task RunAsync(SelfTestContext context)
    {
        ProbeContentProperties(context);
        ProbeDependencyPropertyDiscovery(context);
        ProbeSetPropertyFromString(context);
        ProbeConvertValue(context);
        ProbeResourceLookup(context);
        ProbeXamlReader(context);
        await ProbeMoveAndFocusAsync(context);
        await ProbeLayoutCachingAsync(context);
        ProbeCreationCost(context);
    }

    private static void ProbeContentProperties(SelfTestContext context)
    {
        Type[] types =
        [
            typeof(StackPanel), typeof(Grid), typeof(Border), typeof(TextBlock), typeof(RichTextBlock),
            typeof(Paragraph), typeof(Span), typeof(Bold), typeof(Run), typeof(Hyperlink), typeof(InlineUIContainer),
            typeof(ContentControl), typeof(Button), typeof(ItemsControl), typeof(Viewbox), typeof(ScrollViewer),
            typeof(UserControl), typeof(LinearGradientBrush), typeof(Expander), typeof(InfoBar), typeof(TextBox),
        ];

        var line = new StringBuilder();
        foreach (Type type in types)
        {
            string? name = null;
            for (Type? current = type; current is not null && name is null; current = current.BaseType)
            {
                name = current.GetCustomAttribute<ContentPropertyAttribute>(inherit: false)?.Name;
            }

            line.Append($"{type.Name}={name ?? "<none>"}; ");
        }

        context.Info($"ContentPropertyAttribute: {line}");
        context.Check(
            typeof(Panel).GetCustomAttribute<ContentPropertyAttribute>(inherit: false)?.Name == "Children",
            "ContentPropertyAttribute is discoverable via reflection on projected types");
    }

    private static void ProbeDependencyPropertyDiscovery(SelfTestContext context)
    {
        context.Check(FindDependencyProperty(typeof(TextBlock), "Text") == TextBlock.TextProperty, "DP lookup TextBlock.TextProperty via reflection");
        context.Check(FindDependencyProperty(typeof(TextBlock), "Margin") == FrameworkElement.MarginProperty, "DP lookup inherited FrameworkElement.MarginProperty (FlattenHierarchy)");
        context.Check(FindDependencyProperty(typeof(Grid), "Row") == Grid.RowProperty, "DP lookup attached Grid.RowProperty");
        context.Check(FindDependencyProperty(typeof(Run), "Text") is null, "Run.Text has no DependencyProperty (CLR property only)");
    }

    private static void ProbeSetPropertyFromString(SelfTestContext context)
    {
        var block = new TextBlock();
        var border = new Border();
        var row = new RowDefinition();

        SetFromString(context, block, TextBlock.FontWeightProperty, "Bold", () => block.FontWeight.Weight == 700);
        SetFromString(context, block, TextBlock.ForegroundProperty, "Red", () => block.Foreground is SolidColorBrush { Color: var c } && c == Colors.Red);
        SetFromString(context, block, TextBlock.ForegroundProperty, "#FF336699", () => block.Foreground is SolidColorBrush { Color: var c } && c.B == 0x99);
        SetFromString(context, block, FrameworkElement.MarginProperty, "1,2,3,4", () => block.Margin == new Thickness(1, 2, 3, 4));
        SetFromString(context, block, Grid.RowProperty, "2", () => Grid.GetRow(block) == 2);
        SetFromString(context, block, FrameworkElement.HorizontalAlignmentProperty, "Center", () => block.HorizontalAlignment == HorizontalAlignment.Center);
        SetFromString(context, block, TextBlock.FontFamilyProperty, "Consolas", () => block.FontFamily.Source == "Consolas");
        SetFromString(context, block, TextBlock.TextWrappingProperty, "Wrap", () => block.TextWrapping == TextWrapping.Wrap);
        SetFromString(context, block, TextBlock.FontSizeProperty, "20", () => block.FontSize == 20);
        SetFromString(context, block, TextBlock.TextProperty, "hello", () => block.Text == "hello");
        SetFromString(context, border, Border.CornerRadiusProperty, "4", () => border.CornerRadius == new CornerRadius(4));
        SetFromString(context, row, RowDefinition.HeightProperty, "Auto", () => row.Height.IsAuto);
        SetFromString(context, row, RowDefinition.HeightProperty, "2*", () => row.Height.IsStar && row.Height.Value == 2);

        ObserveFromString(context, block, TextBlock.FontSizeProperty, "not-a-number");
        ObserveFromString(context, block, TextBlock.ForegroundProperty, "{ThemeResource TextFillColorPrimaryBrush}");
        ObserveFromString(context, block, TextBlock.ForegroundProperty, "{x:Null}");
    }

    private static void ProbeConvertValue(SelfTestContext context)
    {
        (Type Type, string Text)[] cases =
        [
            (typeof(double), "12.5"), (typeof(int), "3"), (typeof(bool), "True"), (typeof(string), "abc"), (typeof(object), "abc"),
            (typeof(Visibility), "Collapsed"), (typeof(HorizontalAlignment), "Center"), (typeof(TextWrapping), "Wrap"),
            (typeof(Thickness), "4"), (typeof(Thickness), "1,2,3,4"), (typeof(CornerRadius), "4"),
            (typeof(GridLength), "Auto"), (typeof(GridLength), "2*"), (typeof(GridLength), "120"),
            (typeof(Brush), "Red"), (typeof(Brush), "#FF336699"), (typeof(Windows.UI.Color), "Red"),
            (typeof(Windows.UI.Text.FontWeight), "Bold"), (typeof(Windows.UI.Text.FontStyle), "Italic"),
            (typeof(FontFamily), "Consolas"), (typeof(Windows.UI.Text.TextDecorations), "Underline"),
            (typeof(Geometry), "M0,0 L10,10"), (typeof(ImageSource), "https://example.com/a.png"), (typeof(Uri), "https://example.com"),
            (typeof(Windows.Foundation.Point), "1,2"), (typeof(Duration), "0:0:1"), (typeof(FlowDirection), "RightToLeft"),
        ];

        foreach ((Type type, string text) in cases)
        {
            try
            {
                object value = XamlBindingHelper.ConvertValue(type, text);
                context.Info($"ConvertValue({type.Name}, \"{text}\") -> {value?.GetType().Name}: {Describe(value)}");
            }
            catch (Exception exception)
            {
                context.Info($"ConvertValue({type.Name}, \"{text}\") threw {exception.GetType().Name}: {FirstLine(exception.Message)}");
            }
        }

        const int iterations = 2000;
        var stopwatch = Stopwatch.StartNew();
        for (int i = 0; i < iterations; i++)
        {
            XamlBindingHelper.ConvertValue(typeof(Thickness), "0,0,0,8");
        }

        double convertUs = stopwatch.Elapsed.TotalMilliseconds * 1000 / iterations;
        stopwatch.Restart();
        for (int i = 0; i < iterations; i++)
        {
            var proxy = (TextBlock)XamlReader.Load($"<TextBlock {Xmlns} Margin=\"0,0,0,8\"/>");
            proxy.ReadLocalValue(FrameworkElement.MarginProperty);
        }

        double proxyUs = stopwatch.Elapsed.TotalMilliseconds * 1000 / iterations;
        context.Info($"string conversion cost: ConvertValue={convertUs:F1} us/call, XamlReader proxy element={proxyUs:F1} us/call");
    }

    private static string Describe(object? value) => value switch
    {
        null => "null",
        SolidColorBrush brush => $"SolidColorBrush {brush.Color}",
        GridLength length => $"{length.Value} {length.GridUnitType}",
        Windows.UI.Text.FontWeight weight => $"weight {weight.Weight}",
        FontFamily family => family.Source,
        _ => value.ToString() ?? string.Empty,
    };

    private static void ProbeResourceLookup(SelfTestContext context)
    {
        ResourceDictionary resources = Application.Current.Resources;
        foreach (string key in new[] { "TextFillColorPrimaryBrush", "BodyTextBlockStyle", "SystemAccentColor", "AccentButtonStyle", "CardBackgroundFillColorDefaultBrush" })
        {
            bool found = resources.TryGetValue(key, out object? value);
            context.Info($"Application.Resources.TryGetValue({key}) -> {(found ? value?.GetType().Name : "<missing>")}");
        }
    }

    private static void ProbeXamlReader(SelfTestContext context)
    {
        Load(context, "plain element", $"<TextBlock {Xmlns} Text=\"hi\"/>");
        Load(context, "StaticResource from app resources", $"<TextBlock {Xmlns} Style=\"{{StaticResource BodyTextBlockStyle}}\"/>");
        Load(context, "ThemeResource from app resources", $"<TextBlock {Xmlns} Foreground=\"{{ThemeResource TextFillColorPrimaryBrush}}\"/>");
        Load(context, "x:Name", $"<TextBlock {Xmlns} {XmlnsX} x:Name=\"a\"/>");
        Load(context, "mc:Ignorable custom attribute",
            $"<TextBlock {Xmlns} xmlns:mc=\"http://schemas.openxmlformats.org/markup-compatibility/2006\" xmlns:vx=\"urn:quill:variable-xaml\" mc:Ignorable=\"vx\" vx:Key=\"k1\" Text=\"x\"/>");
        Load(context, "toolkit type via using:", $"<tk:SettingsCard {Xmlns} xmlns:tk=\"using:CommunityToolkit.WinUI.Controls\" Header=\"h\"/>");
        Load(context, "DataTemplate root", $"<DataTemplate {Xmlns}><TextBlock Text=\"x\"/></DataTemplate>");
        Load(context, "Style root", $"<Style {Xmlns} TargetType=\"TextBlock\"><Setter Property=\"FontSize\" Value=\"20\"/></Style>");
        Load(context, "Brush root", $"<SolidColorBrush {Xmlns} Color=\"Red\"/>");
        Load(context, "missing default xmlns (expected to fail)", "<TextBlock Text=\"hi\"/>");
        Load(context, "event attribute (expected to fail)", $"<Button {Xmlns} Click=\"OnClick\"/>");
    }

    private static async Task ProbeMoveAndFocusAsync(SelfTestContext context)
    {
        var panel = new StackPanel();
        TextBox[] boxes = Enumerable.Range(0, 3).Select(i => new TextBox { Text = $"text box {i}" }).ToArray();
        int loaded = 0;
        int unloaded = 0;
        boxes[0].Loaded += (_, _) => loaded++;
        boxes[0].Unloaded += (_, _) => unloaded++;
        foreach (TextBox box in boxes)
        {
            panel.Children.Add(box);
        }

        context.Stage.Children.Add(panel);
        await Task.Delay(200);

        bool focused = boxes[0].Focus(FocusState.Programmatic);
        boxes[0].Select(2, 3);
        await Task.Delay(100);
        context.Info($"initial focus acquired={focused}, focused element is box0={IsFocused(boxes[0])}");

        loaded = unloaded = 0;
        panel.Children.Move(0, 2);
        await Task.Delay(200);
        context.Info($"UIElementCollection.Move(0,2): index={panel.Children.IndexOf(boxes[0])}, focus kept={IsFocused(boxes[0])}, " +
            $"selection=({boxes[0].SelectionStart},{boxes[0].SelectionLength}), Loaded={loaded}, Unloaded={unloaded}");

        boxes[0].Focus(FocusState.Programmatic);
        await Task.Delay(100);
        loaded = unloaded = 0;
        panel.Children.Remove(boxes[0]);
        panel.Children.Insert(0, boxes[0]);
        await Task.Delay(200);
        context.Info($"Remove+Insert: index={panel.Children.IndexOf(boxes[0])}, focus kept={IsFocused(boxes[0])}, " +
            $"selection=({boxes[0].SelectionStart},{boxes[0].SelectionLength}), Loaded={loaded}, Unloaded={unloaded}");

        try
        {
            var other = new StackPanel();
            other.Children.Add(boxes[1]);
            context.Info("adding an element that already has a parent did not throw");
        }
        catch (Exception exception)
        {
            context.Info($"adding an element that already has a parent threw {exception.GetType().Name}: {exception.Message}");
        }

        context.Stage.Children.Clear();
    }

    private static async Task ProbeLayoutCachingAsync(SelfTestContext context)
    {
        const int count = 2000;
        var panel = new StackPanel();
        var blocks = new TextBlock[count];
        for (int i = 0; i < count; i++)
        {
            blocks[i] = new TextBlock { Text = $"Paragraph {i}", TextWrapping = TextWrapping.Wrap };
            var host = new CountingPanel();
            host.Children.Add(blocks[i]);
            panel.Children.Add(host);
        }

        var scroller = new ScrollViewer { Content = panel, Height = 400, Width = 600, HorizontalAlignment = HorizontalAlignment.Left };
        context.Stage.Children.Add(scroller);

        var stopwatch = Stopwatch.StartNew();
        scroller.UpdateLayout();
        context.Info($"first layout of {count} blocks: {stopwatch.Elapsed.TotalMilliseconds:F1} ms, measure={CountingPanel.MeasureCount}, arrange={CountingPanel.ArrangeCount}");
        await Task.Delay(100);

        MeasureEdit(context, scroller, "same-height text edit (middle)", () => blocks[count / 2].Text = "Paragraph edited");
        MeasureEdit(context, scroller, "height-changing text edit (middle)", () =>
            blocks[count / 2].Text = string.Concat(Enumerable.Repeat("A much longer paragraph that wraps onto several lines. ", 12)));
        MeasureEdit(context, scroller, "height-changing text edit (first)", () =>
            blocks[0].Text = string.Concat(Enumerable.Repeat("The first paragraph grows as well. ", 12)));
        MeasureEdit(context, scroller, "insert one block (middle)", () =>
        {
            var host = new CountingPanel();
            host.Children.Add(new TextBlock { Text = "Inserted", TextWrapping = TextWrapping.Wrap });
            panel.Children.Insert(count / 2, host);
        });
        MeasureEdit(context, scroller, "viewport width change (all re-wrap)", () => scroller.Width = 500);

        context.Stage.Children.Clear();
    }

    private static void ProbeCreationCost(SelfTestContext context)
    {
        const int count = 2000;

        var stopwatch = Stopwatch.StartNew();
        var panel = new StackPanel();
        for (int i = 0; i < count; i++)
        {
            panel.Children.Add(new TextBlock { Text = $"Paragraph {i}", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8) });
        }

        double codeMs = stopwatch.Elapsed.TotalMilliseconds;

        var markup = new StringBuilder($"<StackPanel {Xmlns}>");
        for (int i = 0; i < count; i++)
        {
            markup.Append($"<TextBlock Text=\"Paragraph {i}\" TextWrapping=\"Wrap\" Margin=\"0,0,0,8\"/>");
        }

        markup.Append("</StackPanel>");
        stopwatch.Restart();
        XamlReader.Load(markup.ToString());
        double readerOneShotMs = stopwatch.Elapsed.TotalMilliseconds;

        stopwatch.Restart();
        for (int i = 0; i < count; i++)
        {
            XamlReader.Load($"<TextBlock {Xmlns} Text=\"Paragraph {i}\" TextWrapping=\"Wrap\" Margin=\"0,0,0,8\"/>");
        }

        double readerPerElementMs = stopwatch.Elapsed.TotalMilliseconds;

        stopwatch.Restart();
        for (int i = 0; i < count; i++)
        {
            var block = new TextBlock();
            block.SetValue(TextBlock.TextProperty, $"Paragraph {i}");
            block.SetValue(TextBlock.TextWrappingProperty, XamlBindingHelper.ConvertValue(typeof(TextWrapping), "Wrap"));
            block.SetValue(FrameworkElement.MarginProperty, XamlBindingHelper.ConvertValue(typeof(Thickness), "0,0,0,8"));
        }

        double convertSetterMs = stopwatch.Elapsed.TotalMilliseconds;
        context.Info($"create {count} TextBlocks: code={codeMs:F1} ms, XamlReader one document={readerOneShotMs:F1} ms, " +
            $"XamlReader per element={readerPerElementMs:F1} ms, new()+ConvertValue+SetValue={convertSetterMs:F1} ms");
    }

    private static void MeasureEdit(SelfTestContext context, ScrollViewer scroller, string name, Action edit)
    {
        CountingPanel.ResetCounters();
        var stopwatch = Stopwatch.StartNew();
        edit();
        scroller.UpdateLayout();
        context.Info($"{name}: layout {stopwatch.Elapsed.TotalMilliseconds:F2} ms, measure calls={CountingPanel.MeasureCount}, arrange calls={CountingPanel.ArrangeCount}");
    }

    private static bool IsFocused(UIElement element) =>
        ReferenceEquals(FocusManager.GetFocusedElement(element.XamlRoot), element);

    private static DependencyProperty? FindDependencyProperty(Type type, string name) =>
        type.GetProperty(name + "Property", BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy)?.GetValue(null) as DependencyProperty;

    private static void SetFromString(SelfTestContext context, DependencyObject target, DependencyProperty property, string value, Func<bool> verify)
    {
        // Observation only: SetPropertyFromString turned out to reject non-string properties.
        try
        {
            XamlBindingHelper.SetPropertyFromString(target, property, value);
            context.Info($"SetPropertyFromString {target.GetType().Name} \"{value}\": accepted, verified={verify()}");
        }
        catch (Exception exception)
        {
            context.Info($"SetPropertyFromString {target.GetType().Name} \"{value}\": threw {exception.GetType().Name}");
        }
    }

    private static void ObserveFromString(SelfTestContext context, DependencyObject target, DependencyProperty property, string value)
    {
        try
        {
            XamlBindingHelper.SetPropertyFromString(target, property, value);
            context.Info($"SetPropertyFromString \"{value}\" accepted; value now {target.GetValue(property)}");
        }
        catch (Exception exception)
        {
            context.Info($"SetPropertyFromString \"{value}\" threw {exception.GetType().Name}: {FirstLine(exception.Message)}");
        }
    }

    private static void Load(SelfTestContext context, string name, string xaml)
    {
        try
        {
            var stopwatch = Stopwatch.StartNew();
            object result = XamlReader.Load(xaml);
            string extra = result switch
            {
                TextBlock { Style: not null } => " (Style resolved)",
                TextBlock { Foreground: SolidColorBrush brush } => $" (Foreground {brush.Color})",
                _ => string.Empty,
            };
            context.Info($"XamlReader.Load {name}: ok -> {result.GetType().Name}{extra} in {stopwatch.Elapsed.TotalMilliseconds:F2} ms");
        }
        catch (Exception exception)
        {
            context.Info($"XamlReader.Load {name}: threw {exception.GetType().Name}: {FirstLine(exception.Message)}");
        }
    }

    private static string FirstLine(string text)
    {
        int index = text.IndexOfAny(['\r', '\n']);
        return index < 0 ? text : text[..index];
    }
}
