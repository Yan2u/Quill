namespace Quill.Editor.Inlines;

/// <summary>
/// Finds footnote references (<c>[^label]</c>) in a text leaf without a full inline parse, for the
/// document's reference counts. It skips backslash escapes and code spans, which covers the cases that
/// matter; it may differ from the inline parser in exotic sources (a reference inside an autolink),
/// which only affects the number of back links shown.
/// </summary>
internal static class FootnoteReferenceScanner
{
    public static List<string>? Labels(string text)
    {
        List<string>? labels = null;
        int i = text.IndexOfAny(['[', '`', '\\']);
        if (i < 0 || !text.Contains("[^", StringComparison.Ordinal))
        {
            return null;
        }

        for (; i < text.Length; i++)
        {
            char c = text[i];
            if (c == '\\')
            {
                i++;
            }
            else if (c == '`')
            {
                i = SkipCodeSpan(text, i);
            }
            else if (c == '[' && i + 1 < text.Length && text[i + 1] == '^')
            {
                int end = i + 2;
                while (end < text.Length && text[end] is not (']' or '['))
                {
                    end += text[end] == '\\' ? 2 : 1;
                }

                if (end < text.Length && text[end] == ']' && end > i + 2 && (end + 1 >= text.Length || text[end + 1] != '('))
                {
                    (labels ??= []).Add(LinkLabel.Normalize(text[(i + 2)..end]));
                    i = end;
                }
            }
        }

        return labels;
    }

    /// <summary>The last index of the code span opened by the backtick run at <paramref name="start"/>, or the end of the run.</summary>
    private static int SkipCodeSpan(string text, int start)
    {
        int run = 0;
        while (start + run < text.Length && text[start + run] == '`')
        {
            run++;
        }

        for (int i = start + run; i < text.Length;)
        {
            if (text[i] != '`')
            {
                i++;
                continue;
            }

            int closing = 0;
            while (i + closing < text.Length && text[i + closing] == '`')
            {
                closing++;
            }

            if (closing == run)
            {
                return i + closing - 1;
            }

            i += closing;
        }

        return start + run - 1;
    }
}
