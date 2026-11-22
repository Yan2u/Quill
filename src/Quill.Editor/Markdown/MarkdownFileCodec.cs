using System.Globalization;
using System.Text;
using Quill.Editor.Documents;

namespace Quill.Editor.Markdown;

/// <summary>An encoding chosen by the user to reopen a file with ("Reopen with Encoding", M6.4).</summary>
/// <param name="CodePage">The Windows code page of <see cref="TextEncodingKind.Legacy"/>; 0 for the system's.</param>
public sealed record TextEncodingChoice(TextEncodingKind Kind, int CodePage = 0)
{
    public static TextEncodingChoice Utf8 { get; } = new(TextEncodingKind.Utf8);

    public static TextEncodingChoice Utf16LittleEndian { get; } = new(TextEncodingKind.Utf16LittleEndian);

    public static TextEncodingChoice Utf16BigEndian { get; } = new(TextEncodingKind.Utf16BigEndian);

    public static TextEncodingChoice Gb18030 { get; } = new(TextEncodingKind.Legacy, MarkdownFileCodec.Gb18030CodePage);
}

/// <summary>A decoded file: its text and how it was encoded.</summary>
public readonly record struct DecodedText(string Text, TextEncodingKind Encoding, bool HasByteOrderMark, int CodePage = 0);

/// <summary>
/// Decodes and encodes Markdown files, recording the encoding and byte order mark (M6.4). Without a
/// choice, the encoding is detected: a byte order mark; UTF-16 without one (by its zero bytes); valid
/// UTF-8; otherwise the system's legacy code page (GB18030 for a Chinese system). Saving keeps the
/// encoding; text a legacy code page cannot hold is saved as UTF-8 instead, never with lost characters.
/// </summary>
public static class MarkdownFileCodec
{
    public const int Gb18030CodePage = 54936;

    private const int SampleLength = 4096;

    private static readonly UTF8Encoding Utf8 = new(encoderShouldEmitUTF8Identifier: false);
    private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    static MarkdownFileCodec()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    /// <summary>
    /// The legacy code page of the system: its ANSI code page, GB18030 in place of GBK (936), and
    /// Windows-1252 when the system code page is UTF-8 (every byte decodes).
    /// </summary>
    public static int SystemCodePage => CultureInfo.CurrentCulture.TextInfo.ANSICodePage switch
    {
        936 => Gb18030CodePage,
        65001 or 0 => 1252,
        var page => page,
    };

    /// <summary>The display name of an encoding, such as "UTF-8 with BOM" or "GB18030".</summary>
    public static string NameOf(DocumentFormat format) => format.Encoding switch
    {
        TextEncodingKind.Utf16LittleEndian => "UTF-16 LE",
        TextEncodingKind.Utf16BigEndian => "UTF-16 BE",
        TextEncodingKind.Legacy => Legacy(format.CodePage).WebName.ToUpperInvariant(),
        _ => format.HasByteOrderMark ? "UTF-8 with BOM" : "UTF-8",
    };

    /// <summary>Decodes file bytes, detecting the encoding or using <paramref name="choice"/>.</summary>
    public static DecodedText Decode(ReadOnlySpan<byte> bytes, TextEncodingChoice? choice = null)
    {
        if (choice is not null)
        {
            return Forced(bytes, choice);
        }

        if (bytes.StartsWith((ReadOnlySpan<byte>)[0xEF, 0xBB, 0xBF]))
        {
            return new(Utf8.GetString(bytes[3..]), TextEncodingKind.Utf8, true);
        }

        if (bytes.StartsWith((ReadOnlySpan<byte>)[0xFF, 0xFE]))
        {
            return new(Encoding.Unicode.GetString(bytes[2..]), TextEncodingKind.Utf16LittleEndian, true);
        }

        if (bytes.StartsWith((ReadOnlySpan<byte>)[0xFE, 0xFF]))
        {
            return new(Encoding.BigEndianUnicode.GetString(bytes[2..]), TextEncodingKind.Utf16BigEndian, true);
        }

        if (Utf16WithoutMark(bytes) is { } utf16)
        {
            var encoding = utf16 == TextEncodingKind.Utf16LittleEndian ? Encoding.Unicode : Encoding.BigEndianUnicode;
            return new(encoding.GetString(bytes), utf16, false);
        }

        try
        {
            return new(StrictUtf8.GetString(bytes), TextEncodingKind.Utf8, false);
        }
        catch (DecoderFallbackException)
        {
            int page = SystemCodePage;
            return new(Legacy(page).GetString(bytes), TextEncodingKind.Legacy, false, page);
        }
    }

    /// <summary>Encodes text in a document's format; the format actually used is UTF-8 when a legacy code page cannot hold the text.</summary>
    public static (byte[] Bytes, DocumentFormat Format) Encode(string text, DocumentFormat format)
    {
        if (format.Encoding == TextEncodingKind.Legacy)
        {
            var strict = Encoding.GetEncoding(format.CodePage, EncoderFallback.ExceptionFallback, DecoderFallback.ReplacementFallback);
            try
            {
                return (strict.GetBytes(text), format);
            }
            catch (EncoderFallbackException)
            {
                format = format with { Encoding = TextEncodingKind.Utf8, HasByteOrderMark = false, CodePage = 0 };
            }
        }

        Encoding target = format.Encoding switch
        {
            TextEncodingKind.Utf16LittleEndian => Encoding.Unicode,
            TextEncodingKind.Utf16BigEndian => Encoding.BigEndianUnicode,
            _ => Encoding.UTF8,
        };
        var preamble = format.HasByteOrderMark ? target.GetPreamble() : [];
        return ([.. preamble, .. target.GetBytes(text)], format);
    }

    private static DecodedText Forced(ReadOnlySpan<byte> bytes, TextEncodingChoice choice)
    {
        switch (choice.Kind)
        {
            case TextEncodingKind.Utf16LittleEndian:
            {
                bool mark = bytes.StartsWith((ReadOnlySpan<byte>)[0xFF, 0xFE]);
                return new(Encoding.Unicode.GetString(mark ? bytes[2..] : bytes), choice.Kind, mark);
            }

            case TextEncodingKind.Utf16BigEndian:
            {
                bool mark = bytes.StartsWith((ReadOnlySpan<byte>)[0xFE, 0xFF]);
                return new(Encoding.BigEndianUnicode.GetString(mark ? bytes[2..] : bytes), choice.Kind, mark);
            }

            case TextEncodingKind.Legacy:
            {
                int page = choice.CodePage == 0 ? SystemCodePage : choice.CodePage;
                return new(Legacy(page).GetString(bytes), choice.Kind, false, page);
            }

            default:
            {
                bool mark = bytes.StartsWith((ReadOnlySpan<byte>)[0xEF, 0xBB, 0xBF]);
                return new(Utf8.GetString(mark ? bytes[3..] : bytes), TextEncodingKind.Utf8, mark);
            }
        }
    }

    /// <summary>
    /// UTF-16 without a byte order mark: in a sample of even length, most bytes on one side of each pair
    /// are zero (ASCII-range text) and almost none on the other. Null when the sample does not look so.
    /// </summary>
    private static TextEncodingKind? Utf16WithoutMark(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length < 4 || bytes.Length % 2 != 0)
        {
            return null;
        }

        var sample = bytes[..Math.Min(bytes.Length, SampleLength)];
        int pairs = sample.Length / 2, evenZeros = 0, oddZeros = 0;
        for (int i = 0; i + 1 < sample.Length; i += 2)
        {
            evenZeros += sample[i] == 0 ? 1 : 0;
            oddZeros += sample[i + 1] == 0 ? 1 : 0;
        }

        if (oddZeros > pairs * 0.4 && evenZeros < pairs * 0.05)
        {
            return TextEncodingKind.Utf16LittleEndian;
        }

        return evenZeros > pairs * 0.4 && oddZeros < pairs * 0.05 ? TextEncodingKind.Utf16BigEndian : null;
    }

    private static Encoding Legacy(int codePage) => Encoding.GetEncoding(codePage);
}
