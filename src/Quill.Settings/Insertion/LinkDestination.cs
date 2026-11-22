using System.Text;

namespace Quill.Settings.Insertion;

/// <summary>
/// Writes the destination and text of an inserted link or image (06: "inserted images and links", Q109).
/// With escaping on, spaces and non-ASCII characters of the destination are percent-encoded (UTF-8); off,
/// a destination with characters that would end it (spaces, unbalanced parentheses) is written in angle
/// brackets, as CommonMark allows.
/// </summary>
public static class LinkDestination
{
    /// <summary>The file URL of a local path: <c>file:///C:/a/b.png</c>, <c>file://server/share/b.png</c> for a UNC path; not escaped.</summary>
    public static string FileUrl(string path)
    {
        string full = Path.GetFullPath(path).Replace('\\', '/');
        return full.StartsWith("//", StringComparison.Ordinal) ? "file:" + full : "file:///" + full;
    }

    /// <summary>A destination as written in Markdown.</summary>
    public static string Format(string destination, bool escape)
    {
        if (escape)
        {
            return Escape(destination);
        }

        if (!NeedsBrackets(destination))
        {
            return destination;
        }

        return "<" + destination.Replace("<", "\\<").Replace(">", "\\>") + ">";
    }

    /// <summary>Text between the brackets of a link or image: brackets and backslashes escaped.</summary>
    public static string Text(string text)
    {
        var builder = new StringBuilder(text.Length);
        foreach (char c in text)
        {
            if (c is '[' or ']' or '\\')
            {
                builder.Append('\\');
            }

            builder.Append(c is '\n' or '\r' ? ' ' : c);
        }

        return builder.ToString();
    }

    /// <summary><c>[name](destination)</c>, or <c>![name](destination)</c> for an image.</summary>
    public static string Markdown(string text, string destination, bool image, bool escape) =>
        $"{(image ? "!" : string.Empty)}[{Text(text)}]({Format(destination, escape)})";

    /// <summary>Percent-encodes spaces, controls, non-ASCII characters (as UTF-8) and the characters that would end the destination.</summary>
    private static string Escape(string destination)
    {
        var builder = new StringBuilder(destination.Length);
        Span<byte> bytes = stackalloc byte[4];
        for (int i = 0; i < destination.Length; i++)
        {
            char c = destination[i];
            if (c > ' ' && c < 0x7F && c is not ('<' or '>' or '(' or ')'))
            {
                builder.Append(c);
                continue;
            }

            int length = char.IsHighSurrogate(c) && i + 1 < destination.Length
                ? Encoding.UTF8.GetBytes(destination.AsSpan(i++, 2), bytes)
                : Encoding.UTF8.GetBytes(destination.AsSpan(i, 1), bytes);
            foreach (byte b in bytes[..length])
            {
                builder.Append('%').Append(b.ToString("X2", System.Globalization.CultureInfo.InvariantCulture));
            }
        }

        return builder.ToString();
    }

    private static bool NeedsBrackets(string destination)
    {
        if (destination.Length == 0)
        {
            return true;
        }

        int depth = 0;
        foreach (char c in destination)
        {
            if (char.IsWhiteSpace(c) || char.IsControl(c) || c is '<' or '>')
            {
                return true;
            }

            depth += c switch { '(' => 1, ')' => -1, _ => 0 };
            if (depth < 0)
            {
                return true;
            }
        }

        return depth != 0;
    }
}
