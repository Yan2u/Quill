using System.Text;
using Quill.Editor.Documents;
using Quill.Editor.Markdown;
using Quill.Editor.Tests.Support;

namespace Quill.Editor.Tests.Markdown;

/// <summary>
/// M6.4: the encoding of a file is detected (byte order mark, UTF-16 without one, UTF-8, then the
/// system's legacy code page), kept on save, can be chosen when reopening, and never loses characters:
/// text a legacy code page cannot hold is saved as UTF-8.
/// </summary>
public sealed class EncodingTests
{
    private const string Text = "# 标题\n\n中文 and English\n";

    static EncodingTests()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    private static Encoding Gb18030 => Encoding.GetEncoding(MarkdownFileCodec.Gb18030CodePage);

    public static TheoryData<string, byte[], TextEncodingKind, bool> Files => new()
    {
        { "UTF-8", Encoding.UTF8.GetBytes(Text), TextEncodingKind.Utf8, false },
        { "UTF-8 BOM", [0xEF, 0xBB, 0xBF, .. Encoding.UTF8.GetBytes(Text)], TextEncodingKind.Utf8, true },
        { "UTF-16 LE BOM", [0xFF, 0xFE, .. Encoding.Unicode.GetBytes(Text)], TextEncodingKind.Utf16LittleEndian, true },
        { "UTF-16 BE BOM", [0xFE, 0xFF, .. Encoding.BigEndianUnicode.GetBytes(Text)], TextEncodingKind.Utf16BigEndian, true },
        { "UTF-16 LE", Encoding.Unicode.GetBytes("# Title\n\nplain text\n"), TextEncodingKind.Utf16LittleEndian, false },
        { "UTF-16 BE", Encoding.BigEndianUnicode.GetBytes("# Title\n\nplain text\n"), TextEncodingKind.Utf16BigEndian, false },
    };

    [Theory]
    [MemberData(nameof(Files))]
    public void EncodingsAreDetectedAndKeptOnSave(string name, byte[] bytes, TextEncodingKind encoding, bool byteOrderMark)
    {
        var document = MarkdownImporter.ImportFile(bytes, new SequentialBlockIdSource()).Document;
        Assert.True(document.Format.Encoding == encoding && document.Format.HasByteOrderMark == byteOrderMark, name);
        Assert.Equal(bytes, MarkdownSerializer.SerializeFile(document));
    }

    [Fact]
    public void BytesThatAreNotUtf8AreReadInTheLegacyCodePage()
    {
        var decoded = MarkdownFileCodec.Decode(Encoding.GetEncoding(MarkdownFileCodec.SystemCodePage).GetBytes("café naïve"));
        Assert.Equal(TextEncodingKind.Legacy, decoded.Encoding);
        Assert.Equal(MarkdownFileCodec.SystemCodePage, decoded.CodePage);
    }

    [Fact]
    public void Gb18030FilesRoundTrip()
    {
        byte[] bytes = Gb18030.GetBytes(Text);
        var document = MarkdownImporter.ImportFile(bytes, new SequentialBlockIdSource(), TextEncodingChoice.Gb18030).Document;
        Assert.Equal("标题", ((Heading)document.Children[0]).Text);
        Assert.Equal(bytes, MarkdownSerializer.SerializeFile(document, null, out var format));
        Assert.Equal(TextEncodingKind.Legacy, format.Encoding);
        Assert.Equal("GB18030", MarkdownFileCodec.NameOf(format));
    }

    [Fact]
    public void ReopeningWithAnotherEncodingReadsTheBytesThatWay()
    {
        byte[] bytes = Gb18030.GetBytes("中文");
        var asUtf8 = MarkdownFileCodec.Decode(bytes, TextEncodingChoice.Utf8);
        var asGb = MarkdownFileCodec.Decode(bytes, TextEncodingChoice.Gb18030);
        Assert.NotEqual("中文", asUtf8.Text);
        Assert.Equal("中文", asGb.Text);
    }

    [Fact]
    public void TextALegacyCodePageCannotHoldIsSavedAsUtf8()
    {
        var format = DocumentFormat.Default with { Encoding = TextEncodingKind.Legacy, CodePage = 1252 };
        var (bytes, written) = MarkdownFileCodec.Encode("中文 \U0001F44D", format);
        Assert.Equal(TextEncodingKind.Utf8, written.Encoding);
        Assert.Equal("中文 \U0001F44D", Encoding.UTF8.GetString(bytes));
    }
}
