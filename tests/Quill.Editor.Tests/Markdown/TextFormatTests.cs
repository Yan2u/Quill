using System.Text;
using Quill.Editor.Documents;
using Quill.Editor.Markdown;

namespace Quill.Editor.Tests.Markdown;

/// <summary>
/// Line breaks, encoding and byte order mark are recorded on import and restored on save
/// (02-document-model.md); in memory every line break is "\n".
/// </summary>
public sealed class TextFormatTests
{
    [Theory]
    [InlineData("a\r\n\r\n- b\r\n  c\r\n", LineEnding.CrLf)]
    [InlineData("a\r\rb\r", LineEnding.Cr)]
    [InlineData("a\n\nb\n", LineEnding.Lf)]
    public void LineEndingsAreNormalizedAndRestored(string markdown, LineEnding expected)
    {
        var document = MarkdownImporter.Import(markdown, new SequentialBlockIdSource()).Document;
        Assert.Equal(expected, document.Format.LineEnding);
        Assert.False(document.Format.HasMixedLineEndings);
        Assert.DoesNotContain('\r', MarkdownSerializer.SerializeNormalized(document));
        Assert.Equal(markdown, MarkdownSerializer.Serialize(document));
    }

    [Fact]
    public void MixedLineEndingsAreReportedAndWrittenInTheFirstStyle()
    {
        var document = MarkdownImporter.Import("a\r\nb\nc\r\n", new SequentialBlockIdSource()).Document;
        Assert.True(document.Format.HasMixedLineEndings);
        Assert.Equal("a\r\nb\r\nc\r\n", MarkdownSerializer.Serialize(document));
    }

    [Theory]
    [InlineData("a")]
    [InlineData("a\n")]
    [InlineData("a\n\n")]
    public void TrailingLineBreakIsKept(string markdown)
    {
        var document = MarkdownImporter.Import(markdown, new SequentialBlockIdSource()).Document;
        Assert.Equal(markdown, MarkdownSerializer.Serialize(document));
    }

    public static TheoryData<string, bool> Encodings => new()
    {
        { "utf-8", false },
        { "utf-8", true },
        { "utf-16", true },
        { "utf-16BE", true },
    };

    [Theory]
    [MemberData(nameof(Encodings))]
    public void FileBytesRoundTrip(string encodingName, bool byteOrderMark)
    {
        var encoding = Encoding.GetEncoding(encodingName);
        byte[] bytes = [.. (byteOrderMark ? encoding.GetPreamble() : []), .. encoding.GetBytes("# 标题\r\n\r\n中文 *强调* 😀\r\n")];
        var document = MarkdownImporter.ImportFile(bytes, new SequentialBlockIdSource()).Document;
        Assert.Equal(byteOrderMark, document.Format.HasByteOrderMark);
        Assert.Equal(bytes, MarkdownSerializer.SerializeFile(document));
    }
}
