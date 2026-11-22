using System.Globalization;

namespace Quill.Editor.Text;

/// <summary>
/// Grapheme cluster and word boundaries in UTF-16 text. Positions must lie on grapheme cluster
/// boundaries (02-document-model.md).
/// </summary>
public static class TextBoundaries
{
    /// <summary>The boundary after the grapheme cluster that starts at <paramref name="offset"/>.</summary>
    public static int NextGrapheme(string text, int offset)
    {
        if (offset >= text.Length)
        {
            return text.Length;
        }

        return offset + StringInfo.GetNextTextElementLength(text, offset);
    }

    /// <summary>The boundary before the grapheme cluster that ends at <paramref name="offset"/>.</summary>
    public static int PreviousGrapheme(string text, int offset)
    {
        if (offset <= 0)
        {
            return 0;
        }

        // A line break is a cluster of its own; the line search below would start after it.
        if (text[offset - 1] == '\n')
        {
            return offset - 1;
        }

        int start = LineStart(text, offset - 1);
        int previous = start;
        int current = start;
        while (current < offset)
        {
            previous = current;
            current += StringInfo.GetNextTextElementLength(text, current);
        }

        return previous;
    }

    /// <summary>The nearest grapheme cluster boundary at or before <paramref name="offset"/>.</summary>
    public static int SnapToGrapheme(string text, int offset)
    {
        offset = Math.Clamp(offset, 0, text.Length);
        if (offset == 0 || offset == text.Length)
        {
            return offset;
        }

        int current = LineStart(text, offset - 1);
        while (current < offset)
        {
            int next = current + StringInfo.GetNextTextElementLength(text, current);
            if (next > offset)
            {
                return current;
            }

            current = next;
        }

        return offset;
    }

    /// <summary>The end of the word or run of punctuation at or after <paramref name="offset"/>, skipping whitespace first.</summary>
    public static int NextWord(string text, int offset)
    {
        int i = offset;
        while (i < text.Length && char.IsWhiteSpace(text[i]))
        {
            i++;
        }

        if (i < text.Length)
        {
            var kind = Classify(text[i]);
            i++;
            while (i < text.Length && kind != CharKind.Ideograph && Classify(text[i]) == kind)
            {
                i++;
            }
        }

        return SnapForward(text, i);
    }

    /// <summary>The start of the word or run of punctuation before <paramref name="offset"/>, skipping whitespace first.</summary>
    public static int PreviousWord(string text, int offset)
    {
        int i = offset;
        while (i > 0 && char.IsWhiteSpace(text[i - 1]))
        {
            i--;
        }

        if (i > 0)
        {
            var kind = Classify(text[i - 1]);
            i--;
            while (i > 0 && kind != CharKind.Ideograph && Classify(text[i - 1]) == kind)
            {
                i--;
            }
        }

        return SnapToGrapheme(text, i);
    }

    /// <summary>
    /// The word at an offset for a double-click: the run of word characters, punctuation or spaces
    /// holding the character at the offset (or the one before it, at the end of a run). A run of CJK
    /// ideographs counts as one word〔默认〕.
    /// </summary>
    public static (int Start, int End) WordAt(string text, int offset)
    {
        if (text.Length == 0)
        {
            return (0, 0);
        }

        offset = Math.Clamp(offset, 0, text.Length);
        int probe = offset < text.Length && !(char.IsWhiteSpace(text[offset]) && offset > 0 && !char.IsWhiteSpace(text[offset - 1])) ? offset : offset - 1;
        if (probe < 0 || text[probe] == '\n')
        {
            return (offset, offset);
        }

        var kind = Classify(text[probe]);
        int start = probe;
        while (start > 0 && Classify(text[start - 1]) == kind && text[start - 1] != '\n')
        {
            start--;
        }

        int end = probe + 1;
        while (end < text.Length && Classify(text[end]) == kind && text[end] != '\n')
        {
            end++;
        }

        return (SnapToGrapheme(text, start), SnapForward(text, end));
    }

    private enum CharKind
    {
        Word,
        Ideograph,
        Punctuation,
        Space,
    }

    // TODO(spec): word boundaries are a simple character classification (each CJK ideograph is a
    // word); a dictionary-based segmentation for Chinese may be wanted later.
    private static CharKind Classify(char c)
    {
        if (char.IsWhiteSpace(c))
        {
            return CharKind.Space;
        }

        if (c is >= '⺀' and <= '鿿' or >= '豈' and <= '﫿' or >= '가' and <= '힯')
        {
            return CharKind.Ideograph;
        }

        return char.IsLetterOrDigit(c) || c == '_' || char.IsSurrogate(c) ? CharKind.Word : CharKind.Punctuation;
    }

    private static int SnapForward(string text, int offset)
    {
        int snapped = SnapToGrapheme(text, offset);
        return snapped == offset ? offset : NextGrapheme(text, snapped);
    }

    private static int LineStart(string text, int offset)
    {
        int newline = offset >= 0 ? text.LastIndexOf('\n', offset) : -1;
        return newline + 1;
    }
}
