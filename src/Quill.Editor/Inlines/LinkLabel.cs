using System.Text;

namespace Quill.Editor.Inlines;

/// <summary>
/// Matching of link and footnote labels (CommonMark 4.7): whitespace runs collapse to one space, the
/// ends are trimmed and case is folded.
/// </summary>
public static class LinkLabel
{
    public static string Normalize(string label)
    {
        var builder = new StringBuilder(label.Length);
        bool pendingSpace = false;
        foreach (char c in label)
        {
            if (c is ' ' or '\t' or '\n' or '\r')
            {
                pendingSpace = builder.Length > 0;
                continue;
            }

            if (pendingSpace)
            {
                builder.Append(' ');
                pendingSpace = false;
            }

            builder.Append(c);
        }

        // Upper then lower case approximates Unicode case folding.
        return builder.ToString().ToUpperInvariant().ToLowerInvariant();
    }
}
