using Quill.Playground.Samples;
using Quill.VariableXaml.Markup;
using Quill.VariableXaml.Metadata;
using Quill.VariableXaml.Model;

namespace Quill.Playground.SelfTest;

/// <summary>
/// Tests for the markup parser and the incremental parser. The incremental parser is checked
/// differentially: after every edit its tree must hash-equal a full parse of the same text.
/// </summary>
internal static class MarkupParserTests
{
    private static readonly XamlMarkupParser Parser = new(XamlSchema.Default.IsWhitespaceSignificant);

    public static Task RunAsync(SelfTestContext context)
    {
        TestStructure(context);
        TestWhitespace(context);
        TestPropertyElementsAndDirectives(context);
        TestErrors(context);
        TestIncrementalEdits(context);
        TestIncrementalFuzz(context);
        TestParseSpeed(context);
        return Task.CompletedTask;
    }

    private static void TestStructure(SelfTestContext context)
    {
        VNode root = Parser.Parse("""
            <StackPanel Spacing="8" xmlns:tk="using:CommunityToolkit.WinUI.Controls">
                <!-- comment -->
                <TextBlock Text="a &lt; b &amp; &#x4E2D;" Grid.Row="1"/>
                <tk:SettingsCard Header="h"/>
            </StackPanel>
            """).Root;

        context.Check(root.Type == XamlName.Presentation("StackPanel") && root.Children.Count == 2, "parse: root and element children (comment skipped)");
        context.Check(root.GetAttribute(XamlMemberName.Plain("Spacing")) as string == "8", "parse: plain attribute");
        VNode text = root.Children[0];
        context.Check(text.GetAttribute(XamlMemberName.Plain("Text")) as string == "a < b & 中", "parse: entity and character references decoded");
        context.Check(text.GetAttribute(XamlMemberName.Attached(XamlName.Presentation("Grid"), "Row")) as string == "1", "parse: attached attribute");
        context.Check(root.Children[1].Type == new XamlName("using:CommunityToolkit.WinUI.Controls", "SettingsCard"), "parse: prefixed element namespace");
    }

    private static void TestWhitespace(SelfTestContext context)
    {
        VNode block = Parser.Parse("<TextBlock>\n    Hello <Bold>World</Bold>\n</TextBlock>").Root;
        context.Check(
            block.Children.Count == 2 && block.Children[0].Text == "Hello " && block.Children[1].Children[0].Text == "World",
            "whitespace: inline content trims edges but keeps the space before an element",
            Describe(block));

        VNode runs = Parser.Parse("<TextBlock>\n  <Run>A</Run>\n  <Run>B</Run>\n</TextBlock>").Root;
        context.Check(runs.Children.Count == 3 && runs.Children[1].Text == " ", "whitespace: line break between inline elements becomes one space", Describe(runs));

        VNode panel = Parser.Parse("<StackPanel>\n  <TextBlock/>\n  <TextBlock/>\n</StackPanel>").Root;
        context.Check(panel.Children.Count == 2, "whitespace: indentation inside panels is dropped", Describe(panel));

        VNode cjk = Parser.Parse("<TextBlock>中文\n换行  and   spaces</TextBlock>").Root;
        context.Check(cjk.Children[0].Text == "中文换行 and spaces", "whitespace: line break between CJK characters removed, runs collapsed", cjk.Children[0].Text);

        VNode preserved = Parser.Parse("<TextBlock xml:space=\"preserve\">  a  b  </TextBlock>").Root;
        context.Check(preserved.Children[0].Text == "  a  b  ", "whitespace: xml:space=preserve keeps text verbatim");
    }

    private static void TestPropertyElementsAndDirectives(SelfTestContext context)
    {
        VNode grid = Parser.Parse("""
            <Grid x:Name="Root" vx:Key="k1" mc:Ignorable="d" d:DesignWidth="100">
                <Grid.RowDefinitions><RowDefinition Height="Auto"/><RowDefinition/></Grid.RowDefinitions>
                <Border><Border.Background><SolidColorBrush Color="Red"/></Border.Background></Border>
            </Grid>
            """).Root;

        context.Check(grid.Key == "k1", "directives: vx:Key becomes the reconciliation key");
        context.Check(grid.Attributes.All(attribute => attribute.Name.Member != "DesignWidth"), "directives: ignorable d: attributes dropped");
        context.Check(
            grid.PropertyElements.Count == 1 && grid.PropertyElements[0].Member == XamlMemberName.Plain("RowDefinitions") && grid.PropertyElements[0].Values.Count == 2,
            "property elements: Grid.RowDefinitions on Grid is a plain member with two values");
        context.Check(grid.Children.Count == 1 && grid.Children[0].PropertyElements[0].Values[0].Type.LocalName == "SolidColorBrush", "property elements: nested Border.Background");
    }

    private static void TestErrors(SelfTestContext context)
    {
        ExpectError(context, "<StackPanel>\n  <TextBlock>\n</StackPanel>", 3, "mismatched end tag reports line 3");
        ExpectError(context, "<StackPanel><TextBlock Text=\"x/></StackPanel>", 1, "unterminated attribute value");
        ExpectError(context, "<p:TextBlock/>", 1, "undeclared prefix");
        ExpectError(context, "<A/><B/>", 1, "content after the root element");
    }

    private static void TestIncrementalEdits(SelfTestContext context)
    {
        var incremental = new IncrementalMarkupParser(Parser);
        string text = """
            <StackPanel>
                <TextBlock Text="first"/>
                <Border Padding="4"><TextBlock Text="nested"/></Border>
                <TextBlock Text="last"/>
            </StackPanel>
            """;
        MarkupDocument document = Parser.Parse(text);

        string edited = text.Replace("nested", "nested and edited");
        ParseOutcome outcome = incremental.Parse(edited, document);
        context.Check(outcome.Kind == ParseKind.Incremental && outcome.ReparsedElement == "TextBlock", "incremental: attribute edit re-parses only the enclosing TextBlock", $"{outcome.Kind} {outcome.ReparsedElement} {outcome.ParsedLength}/{edited.Length} chars");
        context.Check(outcome.Document.Root.Hash == Parser.Parse(edited).Root.Hash, "incremental: result equals a full parse");
        context.Check(
            ReferenceEquals(outcome.Document.Root.Children[0], document.Root.Children[0]) && ReferenceEquals(outcome.Document.Root.Children[2], document.Root.Children[2]),
            "incremental: untouched siblings keep their identity");

        string inserted = edited.Replace("<TextBlock Text=\"last\"/>", "<TextBlock Text=\"inserted\"/>\n    <TextBlock Text=\"last\"/>");
        ParseOutcome second = incremental.Parse(inserted, outcome.Document);
        context.Check(second.Document.Root.Hash == Parser.Parse(inserted).Root.Hash, "incremental: sibling insertion equals a full parse", $"{second.Kind} {second.ReparsedElement}");

        ParseOutcome unchanged = incremental.Parse(inserted, second.Document);
        context.Check(unchanged.Kind == ParseKind.Unchanged && ReferenceEquals(unchanged.Document, second.Document), "incremental: identical text is a no-op");
    }

    /// <summary>Random edits on a realistic document; incremental and full parses must always agree.</summary>
    private static void TestIncrementalFuzz(SelfTestContext context)
    {
        var random = new Random(12345);
        var incremental = new IncrementalMarkupParser(Parser);
        string text = SampleDocuments.Article(paragraphs: 30);
        MarkupDocument document = Parser.Parse(text);
        string[] snippets = ["x", " ", "中", "<Bold>b</Bold>", "\"", "<", "&amp;", "</TextBlock>", "<Run Text=\"r\"/>", "\n"];
        int incrementalCount = 0;
        int mismatches = 0;
        int failuresAgreed = 0;
        for (int i = 0; i < 400; i++)
        {
            int position = random.Next(text.Length);
            int deleteLength = random.Next(4) == 0 ? random.Next(1, 6) : 0;
            deleteLength = Math.Min(deleteLength, text.Length - position);
            string insert = random.Next(3) == 0 ? string.Empty : snippets[random.Next(snippets.Length)];
            string candidate = text.Remove(position, deleteLength).Insert(position, insert);

            ParseOutcome? viaIncremental = TryParse(() => incremental.Parse(candidate, document));
            MarkupDocument? viaFull = TryParse(() => Parser.Parse(candidate));
            if (viaIncremental is null || viaFull is null)
            {
                if ((viaIncremental is null) != (viaFull is null))
                {
                    mismatches++;
                }
                else
                {
                    failuresAgreed++;
                }

                continue;
            }

            if (viaIncremental.Document.Root.Hash != viaFull.Root.Hash)
            {
                mismatches++;
                continue;
            }

            incrementalCount += viaIncremental.Kind == ParseKind.Incremental ? 1 : 0;
            text = candidate;
            document = viaIncremental.Document;
        }

        context.Check(mismatches == 0, "incremental fuzz: 400 random edits agree with full parses",
            $"mismatches={mismatches}, incremental={incrementalCount}, both-failed={failuresAgreed}");
    }

    private static void TestParseSpeed(SelfTestContext context)
    {
        string text = SampleDocuments.Article(paragraphs: 2000);
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        MarkupDocument document = Parser.Parse(text);
        double fullMs = stopwatch.Elapsed.TotalMilliseconds;

        var incremental = new IncrementalMarkupParser(Parser);
        int middle = text.IndexOf("Paragraph 1001 ", StringComparison.Ordinal);
        string edited = text.Insert(middle + "Paragraph 1001 ".Length, "typed ");
        stopwatch.Restart();
        ParseOutcome outcome = incremental.Parse(edited, document);
        double incrementalMs = stopwatch.Elapsed.TotalMilliseconds;
        context.Info($"parse {text.Length / 1024} KB ({document.ElementCount} elements): full {fullMs:F2} ms; one-word edit: {outcome.Kind} {incrementalMs:F3} ms, re-parsed {outcome.ParsedLength} chars");
    }

    private static void ExpectError(SelfTestContext context, string text, int line, string name)
    {
        try
        {
            Parser.Parse(text);
            context.Check(false, $"error: {name}", "no exception");
        }
        catch (XamlMarkupException exception)
        {
            context.Check(exception.Line == line, $"error: {name}", exception.Message);
        }
    }

    private static T? TryParse<T>(Func<T> parse)
        where T : class
    {
        try
        {
            return parse();
        }
        catch (XamlMarkupException)
        {
            return null;
        }
    }

    private static string Describe(VNode node) =>
        node.IsText ? $"\"{node.Text}\"" : $"{node.Type.LocalName}[{string.Join(", ", node.Children.Select(Describe))}]";
}
