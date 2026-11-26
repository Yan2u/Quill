using System.Text;
using Quill.Editor.Editing;

namespace Quill.Editor.Tests.Clipboard;

public sealed class CfHtmlCodecTests
{
    [Fact]
    public void EncodeAndDecodeRoundTripWithCjkAndEmojiUtf8Offsets()
    {
        const string fragment = "<table><tr><td>中文单元格 🚀</td><td>ASCII</td></tr></table>";
        string encoded = CfHtmlCodec.Encode(fragment);

        var decoded = CfHtmlCodec.Decode(encoded);
        Assert.Equal(fragment, decoded.Fragment);
        Assert.Contains("mso-data-placement:same-cell", decoded.StylesCss);
        Assert.False(decoded.FragmentInsideTable);
        Assert.False(decoded.FragmentInsideRow);

        // Verify numeric byte offsets are exact even when StartFragment/EndFragment comments are removed.
        string withoutMarkers = encoded
            .Replace("<!--StartFragment-->", "<!--xxxxxxxxxxxxx-->", StringComparison.Ordinal)
            .Replace("<!--EndFragment-->", "<!--xxxxxxxxxxx-->", StringComparison.Ordinal);
        var byOffsets = CfHtmlCodec.Decode(withoutMarkers);
        Assert.Equal(fragment, byOffsets.Fragment);
    }

    [Fact]
    public void DecodeHandlesNonAsciiSourceUrlAndUtf8ByteOffsetsWithoutMarkers()
    {
        const string html = "<html><head><style>.xl65{text-align:right;}</style></head><body><table><tr><td>测试</td><td>值</td></tr></table></body></html>";
        const string headerTemplate = "Version:1.0\r\nStartHTML:0000000000\r\nEndHTML:0000000000\r\nStartFragment:0000000000\r\nEndFragment:0000000000\r\nSourceURL:file:///C:/文档/表格.xlsx\r\n";
        int startHtml = Encoding.UTF8.GetByteCount(headerTemplate);
        const string frag = "<tr><td>测试</td><td>值</td></tr>";
        int fragIdx = html.IndexOf(frag, StringComparison.Ordinal);
        int startFrag = startHtml + Encoding.UTF8.GetByteCount(html[..fragIdx]);
        int endFrag = startFrag + Encoding.UTF8.GetByteCount(frag);
        int endHtml = startHtml + Encoding.UTF8.GetByteCount(html);

        string payload = $"Version:1.0\r\nStartHTML:{startHtml:D10}\r\nEndHTML:{endHtml:D10}\r\nStartFragment:{startFrag:D10}\r\nEndFragment:{endFrag:D10}\r\nSourceURL:file:///C:/文档/表格.xlsx\r\n{html}";

        var decoded = CfHtmlCodec.Decode(payload);
        Assert.Equal(frag, decoded.Fragment);
        Assert.Equal(".xl65{text-align:right;}", decoded.StylesCss);
        Assert.True(decoded.FragmentInsideTable);
        Assert.False(decoded.FragmentInsideRow);
    }

    [Fact]
    public void DecodeHandlesExcelOrphanRowsAndCommentedStyleBlock()
    {
        const string excelRaw = """
            Version:1.0
            StartHTML:0000000100
            EndHTML:0000000500
            StartFragment:0000000250
            EndFragment:0000000450
            <html xmlns:x="urn:schemas-microsoft-com:office:excel">
            <head><style><!--table {} .xl66 {text-align:center; font-weight:700;} br {mso-data-placement:same-cell;}--></style></head>
            <body>
            <table>
            <!--StartFragment-->
              <col width="64">
              <tr><td class="xl66">A</td><td>B</td></tr>
            <!--EndFragment-->
            </table>
            </body>
            </html>
            """;

        var decoded = CfHtmlCodec.Decode(excelRaw);
        Assert.Contains("<tr><td class=\"xl66\">A</td><td>B</td></tr>", decoded.Fragment);
        Assert.Contains(".xl66 {text-align:center; font-weight:700;}", decoded.StylesCss);
        Assert.True(decoded.FragmentInsideTable);
        Assert.False(decoded.FragmentInsideRow);
    }

    [Fact]
    public void DecodeDetectsTrackTagWithoutConfusingItWithTr()
    {
        const string raw = "<html><body><video><track><!--StartFragment--><td>A</td><!--EndFragment--></video></body></html>";
        var decoded = CfHtmlCodec.Decode(raw);
        Assert.False(decoded.FragmentInsideTable);
        Assert.False(decoded.FragmentInsideRow);
    }

    [Fact]
    public void DecodeHandlesRawHtmlWithoutCfHeader()
    {
        const string raw = "<table><tr><td>1</td><td>2</td></tr></table>";
        var decoded = CfHtmlCodec.Decode(raw);
        Assert.Equal(raw, decoded.Fragment);
        Assert.False(decoded.FragmentInsideTable);
    }
}
