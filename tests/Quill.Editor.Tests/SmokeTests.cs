using Markdig.Syntax;
using Quill.Editor.Markdown.Parsing;

namespace Quill.Editor.Tests;

public sealed class SmokeTests
{
    [Fact]
    public void MarkdigPipelineParsesWithPreciseSourceLocations()
    {
        const string source = "# Hello\n\nworld\n";

        var document = Markdig.Markdown.Parse(source, MarkdigPipelines.Import);

        var heading = Assert.IsType<HeadingBlock>(document[0]);
        Assert.Equal(1, heading.Level);
        Assert.Equal("# Hello", source[heading.Span.Start..(heading.Span.End + 1)]);
        var paragraph = Assert.IsType<ParagraphBlock>(document[1]);
        Assert.Equal("world", source[paragraph.Span.Start..(paragraph.Span.End + 1)]);
    }
}
