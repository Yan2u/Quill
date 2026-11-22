using Quill.Editor.Commands;
using Quill.Editor.Input;
using Quill.Editor.Tests.Support;

namespace Quill.Editor.Tests.Editing;

/// <summary>
/// Clipboard slices (04, Q9, Q20, Q102): copy writes the selection's Markdown (private format) and its plain
/// text, the Markdown again by default, the text as shown with "copy Markdown source" off or with Copy as
/// plain text; paste parses Markdown and inserts blocks; raw text leaves take text as it is.
/// </summary>
public sealed class ClipboardTests
{
    private static readonly EditorOptions ShownText = EditorOptions.Default with { CopyMarkdownSource = false };

    [Theory]
    [InlineData("a<^>bc<$>d", "bc", "bc")]
    [InlineData("a<$>bc<^>d", "bc", "bc")]
    [InlineData("# a<^>b\n\nc<$>d", "# b\n\nc", "b\nc")]
    [InlineData("- a<^>b\n- c<$>d", "- b\n- c", "b\nc")]
    [InlineData("> a<^>b\n>\n> c<$>d", "b\n\nc", "b\nc")]
    [InlineData("<^>​\ta<$>", "​\ta", "\ta")]
    [InlineData("a<^>b\n\n```\ncode\n```\n\nc<$>d", "b\n\n```\ncode\n```\n\nc", "b\ncode\nc")]
    [InlineData("<^>a **b** [l](u) `c` x\\*<$>", "a **b** [l](u) `c` x\\*", "a b l c x*")]
    [InlineData("<^>![alt](i.png) $x^2$ :smile: &copy; a<br>b<$>", "![alt](i.png) $x^2$ :smile: &copy; a<br>b", "alt x^2 😄 © a\nb")]
    [InlineData("<^>see [r]<$>\n\n[r]: /u", "see [r]", "see r")]
    [InlineData("<^>see [r]<$>", "see [r]", "see [r]")]
    [InlineData("<^>**half<$>**", "**half", "**half")]
    public void CopyWritesMarkdownAndPlainText(string state, string markdown, string plain)
    {
        var host = EditorTestHost.Load(state, ShownText);
        host.Press("Ctrl+C");
        Assert.Equal(new ClipboardContent(markdown, plain), host.Clipboard);
    }

    [Theory]
    [InlineData("# a<^>b\n\nc<$>d", "# b\n\nc")]
    [InlineData("<^>a **b** [l](u)<$>", "a **b** [l](u)")]
    public void ByDefaultThePlainTextIsTheMarkdownSource(string state, string markdown)
    {
        // Q102: "copy Markdown source as plain text" is on by default.
        var host = EditorTestHost.Load(state);
        host.Press("Ctrl+C");
        Assert.Equal(new ClipboardContent(markdown, markdown), host.Clipboard);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void CopyAsPlainTextGivesTheTextAsShown(bool markdownSource)
    {
        var host = EditorTestHost.Load("# a<^>b\n\n**c**<$>d", EditorOptions.Default with { CopyMarkdownSource = markdownSource });
        host.Run(ClipboardAction.CopyAsPlainText);
        Assert.Equal(new ClipboardContent(null, "b\nc"), host.Clipboard);
    }

    [Fact]
    public void CopyAsMarkdownPutsSourceInPlainText()
    {
        var host = EditorTestHost.Load("# a<^>b\n\nc<$>d");
        host.Press("Ctrl+Shift+C");
        Assert.Equal("# b\n\nc", host.Clipboard!.PlainText);
    }

    [Fact]
    public void CutThenPasteRestoresContent()
    {
        const string text = "x\n\n- a<^>b\n- c\n\n> q<$>r";
        var host = EditorTestHost.Load(text);
        host.Press("Ctrl+X");
        Assert.Equal("x\n\n- a<|>r", host.Dump());
        host.Press("Ctrl+V");
        Assert.Equal(TestMarkdown.Serialize(EditorTestHost.Load(text.Replace("<^>", string.Empty).Replace("<$>", string.Empty)).State.Document),
            TestMarkdown.Serialize(host.State.Document));
    }

    [Theory]
    [InlineData("a<|>b", "x", "ax<|>b")]
    [InlineData("a<|>b", "x\ny", "ax\ny<|>b")]
    [InlineData("a<|>b", "x\n\ny", "ax\n\ny<|>b")]
    [InlineData("a<|>b", "# h\n\npara", "a\n\n# h\n\npara<|>b")]
    [InlineData("a<|>b", "x\n\n```\nc\n```", "ax\n\n```\nc<|>\n```\n\nb")]
    [InlineData("<|>", "```\nc\n```", "```\nc<|>\n```")]
    [InlineData("```\na<|>\n```", "# x", "```\na# x<|>\n```")]
    [InlineData("| a<|> |\n| - |", "x|y\nz", "| ax\\|y<br>z<|> |\n| - |")]
    [InlineData("# a<|>", "x\ny", "# ax y<|>")]
    [InlineData("```\na\n```\n\n<gap>", "x", "```\na\n```\n\nx<|>")]
    [InlineData("a<^>bc<$>d", "x", "ax<|>d")]
    public void PasteParsesMarkdown(string before, string pasted, string after)
    {
        var host = EditorTestHost.Load(before);
        host.Execute(new Paste(new ClipboardContent(null, pasted)));
        Assert.Equal(after, host.Dump());
    }

    // Ctrl+Shift+V reads the plain text and parses it as Markdown too, without escaping (Q20).
    [Fact]
    public void PasteAsPlainTextParsesMarkdown()
    {
        var host = EditorTestHost.Load("<|>");
        host.Clipboard = new ClipboardContent("ignored", "# h");
        host.Press("Ctrl+Shift+V");
        Assert.IsType<Documents.Heading>(Assert.Single(host.State.Document.Children));
    }

    [Fact]
    public void PastePrefersPrivateFormat()
    {
        var host = EditorTestHost.Load("<|>");
        host.Clipboard = new ClipboardContent("**b**", "b");
        host.Press("Ctrl+V");
        Assert.Equal("**b**<|>", host.Dump());
    }
}
