using System.Text;

namespace Quill.Playground.Samples;

/// <summary>Generated XAML documents used by the demos and the self-tests.</summary>
internal static class SampleDocuments
{
    private static readonly string[] Words =
    [
        "markdown", "editor", "native", "layout", "render", "update", "patch", "element", "virtual", "tree",
        "incremental", "measure", "arrange", "inline", "paragraph", "block", "cursor", "selection", "编辑器", "渲染",
    ];

    /// <summary>
    /// A long article: headings, rich paragraphs, code blocks and quotes, all inside a StackPanel.
    /// Rich paragraphs start with "Paragraph {i} " so tests can locate them.
    /// </summary>
    public static string Article(int paragraphs, bool virtualize = false)
    {
        var builder = new StringBuilder(paragraphs * 220);
        builder.AppendLine(virtualize
            ? "<ScrollViewer>\n<vx:VirtualStack Spacing=\"12\" Margin=\"24\">"
            : "<ScrollViewer>\n<StackPanel Spacing=\"12\" Padding=\"24\">");
        builder.AppendLine("  <TextBlock Text=\"VariableXamlRoot sample article\" FontSize=\"28\" FontWeight=\"SemiBold\"/>");
        for (int i = 0; i < paragraphs; i++)
        {
            builder.Append("  ").AppendLine(Block(i));
        }

        builder.AppendLine(virtualize ? "</vx:VirtualStack>\n</ScrollViewer>" : "</StackPanel>\n</ScrollViewer>");
        return builder.ToString();
    }

    public static string Block(int index) => (index % 10) switch
    {
        0 => $"<TextBlock Text=\"Section {index / 10 + 1}\" FontSize=\"20\" FontWeight=\"SemiBold\" Margin=\"0,12,0,0\"/>",
        3 => $"<Border Background=\"#14808080\" CornerRadius=\"4\" Padding=\"12\"><TextBlock FontFamily=\"Consolas\" Text=\"var block{index} = Render(node{index}); // code block\"/></Border>",
        7 => $"<Border BorderBrush=\"#FF8A8A8A\" BorderThickness=\"3,0,0,0\" Padding=\"12,4\"><TextBlock TextWrapping=\"Wrap\" FontStyle=\"Italic\">Quote {index}: {Sentence(index, 14)}</TextBlock></Border>",
        _ => $"<TextBlock TextWrapping=\"Wrap\">Paragraph {index} {Sentence(index, 18)} <Bold>bold {index}</Bold> and <Italic>{Sentence(index + 1, 4)}</Italic>.</TextBlock>",
    };

    public static string Sentence(int seed, int wordCount)
    {
        var builder = new StringBuilder();
        for (int i = 0; i < wordCount; i++)
        {
            if (i > 0)
            {
                builder.Append(' ');
            }

            builder.Append(Words[(seed * 7 + i * 3) % Words.Length]);
        }

        return builder.ToString();
    }

    /// <summary>The starter document of the live editor demo.</summary>
    public const string LiveEditorStarter = """
        <ScrollViewer>
        <StackPanel Spacing="12" Padding="24">
            <TextBlock Text="VariableXamlRoot" FontSize="28" FontWeight="SemiBold"/>
            <TextBlock TextWrapping="Wrap" Foreground="{ThemeResource TextFillColorSecondaryBrush}">
                Edit this markup on the left. Only the elements you change are patched:
                type into the text box below, then edit any other line and watch it keep its text and caret.
            </TextBlock>

            <TextBox vx:Key="note" Header="State survives edits elsewhere" PlaceholderText="Type something here..."/>

            <Border Background="{ThemeResource CardBackgroundFillColorDefaultBrush}" CornerRadius="8" Padding="16"
                    BorderBrush="{ThemeResource CardStrokeColorDefaultBrush}" BorderThickness="1">
                <StackPanel Spacing="8">
                    <TextBlock FontSize="16" FontWeight="SemiBold">Inline formatting</TextBlock>
                    <TextBlock TextWrapping="Wrap">
                        Typora-style rendering needs <Bold>bold</Bold>, <Italic>italic</Italic>,
                        <Run Foreground="Crimson">colored runs</Run> and <Underline>underline</Underline>.
                    </TextBlock>
                    <Button Content="Click me" Click="OnDemoClick"/>
                </StackPanel>
            </Border>

            <Grid ColumnSpacing="12">
                <Grid.ColumnDefinitions>
                    <ColumnDefinition Width="*"/>
                    <ColumnDefinition Width="2*"/>
                </Grid.ColumnDefinitions>
                <Border Background="#33E91E63" Height="48" CornerRadius="4"/>
                <Border Grid.Column="1" Background="#332196F3" Height="48" CornerRadius="4"/>
            </Grid>
        </StackPanel>
        </ScrollViewer>
        """;

    /// <summary>RichTextBlock content: Blocks and Inlines are patched like any other child list.</summary>
    public const string RichText = """
        <ScrollViewer>
        <RichTextBlock Margin="24" TextWrapping="Wrap" FontSize="15" IsTextSelectionEnabled="True">
            <Paragraph FontSize="26" FontWeight="SemiBold">RichTextBlock document</Paragraph>
            <Paragraph Margin="0,12,0,0">
                A Typora-like editor keeps a document of blocks. Here every <Bold>Paragraph</Bold> and every
                <Italic>inline</Italic> is a node: editing one word patches a single <Run Foreground="Crimson">Run</Run>.
            </Paragraph>
            <Paragraph Margin="0,12,0,0">
                Inline UI can sit in the text flow:
                <InlineUIContainer><Button Content="Inline button" Margin="4,0" Click="OnDemoClick"/></InlineUIContainer>
                and keeps its instance while the surrounding text changes.
            </Paragraph>
            <Paragraph Margin="0,12,0,0" FontFamily="Consolas" Foreground="{ThemeResource TextFillColorSecondaryBrush}">
                code-like paragraph: var node = Render(block);
            </Paragraph>
        </RichTextBlock>
        </ScrollViewer>
        """;
}
