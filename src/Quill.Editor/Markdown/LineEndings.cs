using System.Text;
using Quill.Editor.Documents;

namespace Quill.Editor.Markdown;

/// <summary>
/// Converts between file line breaks and the "\n" used in memory (02-document-model.md).
/// </summary>
internal static class LineEndings
{
    /// <summary>Replaces every line break with "\n" and reports the first style found and whether styles were mixed.</summary>
    public static (string Text, LineEnding Style, bool Mixed) Normalize(string text)
    {
        if (!text.Contains('\r'))
        {
            return (text, LineEnding.Lf, false);
        }

        var builder = new StringBuilder(text.Length);
        LineEnding? first = null;
        bool mixed = false;
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            LineEnding? kind = null;
            if (c == '\r')
            {
                bool pair = i + 1 < text.Length && text[i + 1] == '\n';
                kind = pair ? LineEnding.CrLf : LineEnding.Cr;
                i += pair ? 1 : 0;
                builder.Append('\n');
            }
            else if (c == '\n')
            {
                kind = LineEnding.Lf;
                builder.Append('\n');
            }
            else
            {
                builder.Append(c);
            }

            if (kind is { } found)
            {
                first ??= found;
                mixed |= first != found;
            }
        }

        return (builder.ToString(), first ?? LineEnding.Lf, mixed);
    }

    public static string Apply(string normalized, LineEnding style)
    {
        return style switch
        {
            LineEnding.CrLf => normalized.Replace("\n", "\r\n"),
            LineEnding.Cr => normalized.Replace('\n', '\r'),
            _ => normalized,
        };
    }
}
