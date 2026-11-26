using Quill.Editor.Commands;
using Quill.Editor.Diagnostics;
using Quill.Editor.Documents;
using Quill.Editor.Editing;
using Quill.Editor.Input;
using Quill.Editor.State;
using Quill.Editor.Tests.Support;

namespace Quill.Editor.Tests.Clipboard;

public sealed class TableClipboardIntegrationTests
{
    private const string ExcelSampleHtml = """
        Version:1.0
        StartHTML:0000000100
        EndHTML:0000000900
        StartFragment:0000000200
        EndFragment:0000000800
        <html>
        <head><style>.xl65{font-weight:700;text-align:center;}.xl66{text-align:right;}</style></head>
        <body>
        <table>
        <!--StartFragment-->
          <tr><td class="xl65">Step</td><td class="xl65">R_mean</td></tr>
          <tr><td class="xl66">1</td><td class="xl66">0.6073</td></tr>
          <tr><td class="xl66">2</td><td class="xl66">0.6444</td></tr>
        <!--EndFragment-->
        </table>
        </body>
        </html>
        """;

    [Fact]
    public void PasteWordOnlineHtmlWithNewlineOnlyPlainTextCreatesTable()
    {
        // Mirrors the exact failure mode from the user report: PlainText has one cell per line without tabs.
        const string newlineOnlyPlain = "Step\nR_mean\n1\n0.6073\n2\n0.6444";
        var host = EditorTestHost.Load("<|>");
        host.Clipboard = new ClipboardContent(null, newlineOnlyPlain, ExcelSampleHtml);

        host.Press("Ctrl+V");

        var table = Assert.IsType<Table>(Assert.Single(host.State.Document.Children));
        Assert.Equal(3, table.Children.Count);
        Assert.Equal(2, table.ColumnCount);
        Assert.Equal([TableAlignment.Right, TableAlignment.Right], table.Alignments);
        Assert.Equal("**Step**", ((TableCell)((TableRow)table.Children[0]).Children[0]).Text);
        Assert.Equal("0.6444", ((TableCell)((TableRow)table.Children[2]).Children[1]).Text);
        Assert.Empty(DocumentValidator.Validate(host.State.Document));

        host.Press("Ctrl+Z");
        Assert.Equal("<|>", host.Dump());
    }

    [Fact]
    public void PasteHtmlTableMidParagraphAndInGapAndListItem()
    {
        var mid = EditorTestHost.Load("before<|>after");
        mid.Clipboard = new ClipboardContent(null, "a\tb\n1\t2", "<table><tr><th>a</th><th>b</th></tr><tr><td>1</td><td>2</td></tr></table>");
        mid.Press("Ctrl+V");
        Assert.Equal("before\n\n| a | b |\n| --- | --- |\n| 1 | 2<|> |\n\nafter", mid.Dump());

        var gap = EditorTestHost.Load("---\n\n<gap>");
        gap.Clipboard = new ClipboardContent(null, "a\tb\n1\t2", "<table><tr><th>a</th><th>b</th></tr><tr><td>1</td><td>2</td></tr></table>");
        gap.Press("Ctrl+V");
        Assert.Equal("---\n\n| a | b |\n| --- | --- |\n| 1 | 2<|> |", gap.Dump());

        var list = EditorTestHost.Load("- item<|>");
        list.Clipboard = new ClipboardContent(null, "a\tb\n1\t2", "<table><tr><th>a</th><th>b</th></tr><tr><td>1</td><td>2</td></tr></table>");
        list.Press("Ctrl+V");
        Assert.Equal("- item\n\n  | a | b |\n  | --- | --- |\n  | 1 | 2<|> |", list.Dump());
    }

    [Fact]
    public void PasteMixedTextAndTableHtmlPreservesAllSegments()
    {
        const string html = """
            <!--StartFragment-->
            <h2>Results</h2>
            <table><tr><th>K</th><th>V</th></tr><tr><td>a</td><td>1</td></tr></table>
            <p>Done.</p>
            <!--EndFragment-->
            """;

        var host = EditorTestHost.Load("<|>");
        host.Clipboard = new ClipboardContent(null, "Results\nK\nV\na\n1\nDone.", html);
        host.Press("Ctrl+V");

        Assert.Equal("## Results\n\n| K | V |\n| --- | --- |\n| a | 1 |\n\nDone.<|>", host.Dump());
        Assert.Empty(DocumentValidator.Validate(host.State.Document));
    }

    [Fact]
    public void PasteSingleCellExcelHtmlDoesNotCreateTableOrCsvQuotes()
    {
        var host = EditorTestHost.Load("val: <|>");
        host.Clipboard = new ClipboardContent(null, "\"line1\nline2\"\n", "<table><tr><td>line1<br>line2</td></tr></table>");
        host.Press("Ctrl+V");
        Assert.Equal("val: line1\nline2<|>", host.Dump());
    }

    [Fact]
    public void PasteSingleColumnMultiRowHtmlCreatesOneColumnTableOutsideTable()
    {
        var host = EditorTestHost.Load("<|>");
        host.Clipboard = new ClipboardContent(null, "r1\nr2\n", "<table><tr><td>r1</td></tr><tr><td>r2</td></tr></table>");
        host.Press("Ctrl+V");
        Assert.Equal("| r1 |\n| --- |\n| r2<|> |", host.Dump());
    }

    [Fact]
    public void PasteTsvOutsideTableCreatesTableAndRejectsCodeOrCsv()
    {
        var host = EditorTestHost.Load("<|>");
        host.Clipboard = new ClipboardContent(null, "H1\tH2\nA\tB\n");
        host.Press("Ctrl+V");
        Assert.Equal("| H1 | H2 |\n| --- | --- |\n| A | B<|> |", host.Dump());

        var csvHost = EditorTestHost.Load("<|>");
        csvHost.Clipboard = new ClipboardContent(null, "a,b\n1,2");
        csvHost.Press("Ctrl+V");
        Assert.Equal("a,b\n1,2<|>", csvHost.Dump());
    }

    [Fact]
    public void PasteAsPlainTextAndRawBlocksIgnoreHtmlAndTsvTableConversion()
    {
        var plainHost = EditorTestHost.Load("<|>");
        plainHost.Clipboard = new ClipboardContent("| a | b |\n| - | - |", "A\tB\n1\t2", ExcelSampleHtml);
        plainHost.Press("Ctrl+Shift+V");
        Assert.IsType<Paragraph>(Assert.Single(plainHost.State.Document.Children));
        Assert.Equal("A\tB\n1\t2<|>", plainHost.Dump());

        var codeHost = EditorTestHost.Load("```\n<|>\n```");
        codeHost.Clipboard = new ClipboardContent(null, "A\tB\n1\t2", ExcelSampleHtml);
        codeHost.Press("Ctrl+V");
        Assert.Equal("```\nA\tB\n1\t2<|>\n```", codeHost.Dump());

        var sourceDoc = SourceText.ToDocument(string.Empty, DocumentFormat.Default, plainHost.Ids);
        var sourceSession = new EditorSession(
            new EditorState { Document = sourceDoc, Selection = TextSelection.Caret(sourceDoc.Children[0].Id, 0) },
            plainHost.Session.Services);
        sourceSession.Execute(new Paste(new ClipboardContent(null, "A\tB\n1\t2", ExcelSampleHtml)));
        Assert.Equal("A\tB\n1\t2", SourceText.ToText(sourceSession.State.Document));
    }

    [Fact]
    public void PasteGridIntoTableCellOverwritesAndAutoGrowsWithSingleUndo()
    {
        var host = EditorTestHost.Load("| a | b |\n| - | - |\n| c | d<|> |");
        var tableBefore = (Table)host.State.Document.Children[0];
        var survivingId = ((TableRow)tableBefore.Children[0]).Children[0].Id;

        host.Clipboard = new ClipboardContent(
            null,
            "x\ty\np\tq",
            "<table><tr><td>x</td><td>y</td></tr><tr><td>p</td><td>q</td></tr></table>");
        host.Press("Ctrl+V");

        var tableAfter = (Table)host.State.Document.Children[0];
        Assert.Equal(3, tableAfter.Children.Count);
        Assert.Equal(3, tableAfter.ColumnCount);
        Assert.Equal(survivingId, ((TableRow)tableAfter.Children[0]).Children[0].Id);
        Assert.Equal("| a | b |  |\n| --- | --- | --- |\n| c | x | y |\n|  | p | q<|> |", host.Dump());
        Assert.Empty(DocumentValidator.Validate(host.State.Document));

        host.Press("Ctrl+Z");
        Assert.Equal("| a | b |\n| - | - |\n| c | d<|> |", host.Dump());
    }

    [Fact]
    public void PasteSingleRowTsvAndGfmPipeTableInsideTable()
    {
        var tsvHost = EditorTestHost.Load("| a<|> | b | c |\n| - | - | - |");
        tsvHost.Clipboard = new ClipboardContent(null, "1\t2");
        tsvHost.Press("Ctrl+V");
        Assert.Equal("| 1 | 2<|> | c |\n| - | - | - |", tsvHost.Dump());

        var pipeHost = EditorTestHost.Load("| a | b |\n| - | - |\n| c<|> | d |");
        pipeHost.Clipboard = new ClipboardContent(null, "| X | Y |\n| --- | --- |\n| Z | W |");
        pipeHost.Press("Ctrl+V");
        Assert.Equal("| a | b |\n| - | - |\n| X | Y |\n| Z | W<|> |", pipeHost.Dump());
    }

    [Fact]
    public void PasteOverWholeTableSelectionReplacesTableDimensions()
    {
        var host = EditorTestHost.Load("| a | b | c |\n| - | - | - |\n| d<|> | e | f |\n| g | h | i |");
        host.Press("Ctrl+A");
        host.Clipboard = new ClipboardContent(null, "x\ty\n1\t2", "<table><tr><th>x</th><th>y</th></tr><tr><td>1</td><td>2</td></tr></table>");
        host.Press("Ctrl+V");

        Assert.Equal("| x | y |\n| --- | --- |\n| 1 | 2<|> |", host.Dump());
        host.Press("Ctrl+Z");
        Assert.Equal("| <^>a | b | c |\n| - | - | - |\n| d | e | f |\n| g | h | i<$> |", host.Dump());
    }

    [Fact]
    public void CopyBodyRowAndPasteIntoAnotherRowDoesNotPrependSyntheticHeader()
    {
        var host = EditorTestHost.Load("| h1 | h2 |\n| - | - |\n| r1a<|> | r1b |\n| r2a | r2b |");
        host.Press("Ctrl+L Ctrl+C");

        var table = (Table)host.State.Document.Children[0];
        var row2First = ((TableRow)table.Children[2]).Children[0];
        host.Execute(new SetSelection(TextSelection.Caret(new TextPosition(row2First.Id, 0))));
        host.Press("Ctrl+V");

        Assert.Equal("| h1 | h2 |\n| - | - |\n| r1a | r1b |\n| r1a | r1b<|> |", host.Dump());
    }

    [Fact]
    public void TableClipboardOperationsPerformZeroWholeDocumentParsesOrSerializations()
    {
        var host = EditorTestHost.Load("| a | b |\n| - | - |\n| c<|> | d |");
        using var scope = OperationCounters.BeginScope();

        host.Press("Ctrl+A Ctrl+C");
        host.Clipboard = new ClipboardContent(null, "1\t2\n3\t4", ExcelSampleHtml);
        host.Press("Ctrl+V");
        host.Press("Ctrl+Z");
        host.Press("Ctrl+Y");
        var copiedTable = host.Session.CopyTable();
        Assert.NotNull(copiedTable?.Html);

        Assert.Equal(0, scope.FullDocumentParses);
        Assert.Equal(0, scope.FullDocumentSerializations);
        Assert.Empty(DocumentValidator.Validate(host.State.Document));
    }
}
