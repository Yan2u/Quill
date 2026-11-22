namespace Quill.Editor.Markdown.Import;

/// <summary>
/// Splits the body of an ATX heading line into its opening markers, content and closing sequence
/// (CommonMark 0.31.2, section 4.2).
/// </summary>
internal static class AtxHeading
{
    public static (string Opening, string Content, string Closing) Split(string body)
    {
        int i = 0;
        while (i < body.Length && i < 3 && body[i] == ' ')
        {
            i++;
        }

        while (i < body.Length && body[i] == '#')
        {
            i++;
        }

        int contentStart = i;
        while (contentStart < body.Length && body[contentStart] is ' ' or '\t')
        {
            contentStart++;
        }

        int end = body.Length;
        while (end > contentStart && body[end - 1] is ' ' or '\t')
        {
            end--;
        }

        int contentEnd = end;
        int hashes = end;
        while (hashes > contentStart && body[hashes - 1] == '#')
        {
            hashes--;
        }

        if (hashes < end)
        {
            if (hashes == contentStart)
            {
                // The content is only a closing sequence: an empty heading.
                contentEnd = contentStart;
            }
            else if (body[hashes - 1] is ' ' or '\t')
            {
                contentEnd = hashes;
                while (contentEnd > contentStart && body[contentEnd - 1] is ' ' or '\t')
                {
                    contentEnd--;
                }
            }
        }

        return (body[..contentStart], body[contentStart..contentEnd], body[contentEnd..]);
    }
}
