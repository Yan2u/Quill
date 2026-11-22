using Quill.Editor.Inlines;

namespace Quill.Editor.Markdown.Inlines;

/// <summary>A bracketed text that may name a link reference definition.</summary>
/// <param name="Normalized">The label normalized with <see cref="LinkLabel.Normalize"/>.</param>
/// <param name="Written">The text between the brackets as written.</param>
internal readonly record struct ReferenceCandidate(string Normalized, string Written);

/// <summary>
/// Finds the texts in brackets that could be link labels (CommonMark 4.7: no unescaped brackets
/// inside, at most 999 characters, not blank). Whether a reference link is a link depends only on
/// whether these labels are defined, so they decide when a cached parse goes stale. The scan
/// over-approximates (it ignores code spans and the like), which only costs a needless reparse.
/// </summary>
internal static class ReferenceCandidates
{
    private const int MaxLabelLength = 999;

    public static List<ReferenceCandidate> Find(string source)
    {
        var result = new List<ReferenceCandidate>();
        int open = -1;
        for (int i = 0; i < source.Length; i++)
        {
            char c = source[i];
            if (c == '\\')
            {
                i++;
                continue;
            }

            if (c == '[')
            {
                open = i;
            }
            else if (c == ']' && open >= 0)
            {
                string written = source[(open + 1)..i];
                open = -1;
                if (written.Length <= MaxLabelLength && !string.IsNullOrWhiteSpace(written) && written[0] != '^')
                {
                    string normalized = LinkLabel.Normalize(written);
                    if (!result.Exists(candidate => candidate.Written == written))
                    {
                        result.Add(new ReferenceCandidate(normalized, written));
                    }
                }
            }
        }

        return result;
    }
}
