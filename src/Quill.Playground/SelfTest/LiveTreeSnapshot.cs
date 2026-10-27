using System.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Media;

namespace Quill.Playground.SelfTest;

/// <summary>
/// Serializes a live WinUI object tree (types, key properties, children) into text, so two
/// trees built differently (patched vs freshly mounted) can be compared for equality.
/// </summary>
internal static class LiveTreeSnapshot
{
    public static string Capture(object? root)
    {
        var builder = new StringBuilder();
        Append(builder, root, 0);
        return builder.ToString();
    }

    private static void Append(StringBuilder builder, object? value, int depth)
    {
        builder.Append(' ', depth * 2);
        switch (value)
        {
            case null:
                builder.AppendLine("null");
                return;
            case string text:
                builder.Append('"').Append(text).AppendLine("\"");
                return;
        }

        builder.Append(value.GetType().Name);
        switch (value)
        {
            case TextBlock block:
                Property(builder, "Text", block.Text);
                Property(builder, "FontSize", block.FontSize);
                Property(builder, "FontWeight", block.FontWeight.Weight);
                Property(builder, "FontStyle", block.FontStyle);
                Property(builder, "Wrap", block.TextWrapping);
                Property(builder, "Foreground", Describe(block.Foreground));
                break;
            case Run run:
                Property(builder, "Text", run.Text);
                Property(builder, "FontWeight", run.FontWeight.Weight);
                Property(builder, "Foreground", Describe(run.Foreground));
                break;
            case TextElement element:
                Property(builder, "FontWeight", element.FontWeight.Weight);
                Property(builder, "FontStyle", element.FontStyle);
                break;
            case Border border:
                Property(builder, "Background", Describe(border.Background));
                Property(builder, "BorderThickness", border.BorderThickness);
                Property(builder, "Padding", border.Padding);
                Property(builder, "CornerRadius", border.CornerRadius);
                break;
            case StackPanel panel:
                Property(builder, "Spacing", panel.Spacing);
                Property(builder, "Orientation", panel.Orientation);
                Property(builder, "Padding", panel.Padding);
                break;
            case TextBox box:
                Property(builder, "Header", box.Header);
                Property(builder, "PlaceholderText", box.PlaceholderText);
                break;
            case ContentControl control when control.Content is string content:
                Property(builder, "Content", content);
                break;
        }

        if (value is FrameworkElement framework)
        {
            Property(builder, "Margin", framework.Margin);
            Property(builder, "Name", framework.Name);
            Property(builder, "Row", Grid.GetRow(framework));
            Property(builder, "Column", Grid.GetColumn(framework));
            if (!double.IsNaN(framework.Height))
            {
                Property(builder, "Height", framework.Height);
            }
        }

        builder.AppendLine();
        if (value is Grid grid)
        {
            foreach (RowDefinition row in grid.RowDefinitions)
            {
                builder.Append(' ', (depth + 1) * 2).AppendLine($"RowDefinition {row.Height}");
            }

            foreach (ColumnDefinition column in grid.ColumnDefinitions)
            {
                builder.Append(' ', (depth + 1) * 2).AppendLine($"ColumnDefinition {column.Width}");
            }
        }

        IEnumerable<object?> children = value switch
        {
            Panel panel => panel.Children,
            Border border => new object?[] { border.Child },
            ContentControl control when control.Content is DependencyObject => new object?[] { control.Content },
            TextBlock block => block.Inlines,
            Span span => span.Inlines,
            RichTextBlock rich => rich.Blocks,
            Paragraph paragraph => paragraph.Inlines,
            _ => Array.Empty<object?>(),
        };

        foreach (object? child in children)
        {
            Append(builder, child, depth + 1);
        }
    }

    private static void Property(StringBuilder builder, string name, object? value) => builder.Append(' ').Append(name).Append('=').Append(value);

    private static string Describe(Brush? brush) => brush switch
    {
        null => "null",
        SolidColorBrush solid => solid.Color.ToString(),
        _ => brush.GetType().Name,
    };
}
