using System.Text;

namespace Quill.Editor.Editing;

/// <summary>
/// RFC 4180 tab-separated value (TSV) parser for clipboard fallback when CF_HTML is unavailable
/// (e.g. spreadsheets, terminals, SQL clients). Outside tables, strict heuristics avoid false
/// positives on tab-indented code, prose, or Markdown blocks; CSV is intentionally not auto-parsed.
/// </summary>
internal static class TsvTableParser
{
    public static TableGrid? TryParse(string text, bool insideTable)
    {
        if (string.IsNullOrEmpty(text) || !text.Contains('\t'))
        {
            return null;
        }

        string normalized = text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
        var rows = ParseRfc4180Rows(normalized);
        if (rows.Count == 0)
        {
            return null;
        }

        int maxCols = 0;
        for (int i = 0; i < rows.Count; i++)
        {
            if (rows[i].Count > maxCols)
            {
                maxCols = rows[i].Count;
            }
        }

        if (maxCols < 2)
        {
            return null;
        }

        if (!insideTable)
        {
            if (rows.Count < 2)
            {
                return null;
            }

            // Require rectangular rows when creating a new table from plain text.
            for (int i = 0; i < rows.Count; i++)
            {
                if (rows[i].Count != maxCols)
                {
                    return null;
                }
            }

            // Reject tab-indented code/text where every line starts with a tab.
            if (rows.All(static row => row[0].Length == 0))
            {
                return null;
            }

            if (LooksLikeMarkdownBlock(normalized))
            {
                return null;
            }
        }

        return TableGrid.Create(rows);
    }

    private static List<List<string>> ParseRfc4180Rows(string text)
    {
        var rows = new List<List<string>>();
        var currentRow = new List<string>();
        var field = new StringBuilder();
        bool atFieldStart = true;
        bool inQuotes = false;

        for (int i = 0; i < text.Length; i++)
        {
            char ch = text[i];
            if (inQuotes)
            {
                if (ch == '"')
                {
                    if (i + 1 < text.Length && text[i + 1] == '"')
                    {
                        field.Append('"');
                        i++;
                    }
                    else
                    {
                        inQuotes = false;
                    }
                }
                else
                {
                    field.Append(ch);
                }

                continue;
            }

            if (atFieldStart && ch == '"')
            {
                inQuotes = true;
                atFieldStart = false;
                continue;
            }

            if (ch == '\t')
            {
                currentRow.Add(field.ToString());
                field.Clear();
                atFieldStart = true;
            }
            else if (ch == '\n')
            {
                currentRow.Add(field.ToString());
                field.Clear();
                rows.Add(currentRow);
                currentRow = [];
                atFieldStart = true;
            }
            else
            {
                field.Append(ch);
                atFieldStart = false;
            }
        }

        if (!atFieldStart || currentRow.Count > 0)
        {
            currentRow.Add(field.ToString());
            rows.Add(currentRow);
        }

        // Remove trailing empty single-cell lines produced by trailing newlines from spreadsheet copies.
        while (rows.Count > 0 && rows[^1].Count == 1 && rows[^1][0].Length == 0)
        {
            rows.RemoveAt(rows.Count - 1);
        }

        return rows;
    }

    private static bool LooksLikeMarkdownBlock(string text)
    {
        string trimmed = text.TrimStart();
        if (trimmed.StartsWith("```", StringComparison.Ordinal)
            || trimmed.StartsWith("~~~", StringComparison.Ordinal)
            || trimmed.StartsWith("> ", StringComparison.Ordinal)
            || trimmed.StartsWith("# ", StringComparison.Ordinal)
            || trimmed.StartsWith("## ", StringComparison.Ordinal))
        {
            return true;
        }

        // Let MarkdownImporter handle GFM pipe tables directly.
        string[] lines = text.Split('\n');
        if (lines.Length >= 2 && lines[0].Contains('|') && IsGfmDelimiterLine(lines[1]))
        {
            return true;
        }

        return false;
    }

    private static bool IsGfmDelimiterLine(string line)
    {
        string trimmed = line.Trim();
        if (trimmed.Length == 0 || !trimmed.Contains('-'))
        {
            return false;
        }

        bool hasDash = false;
        foreach (char ch in trimmed)
        {
            if (ch == '-')
            {
                hasDash = true;
            }
            else if (ch is not ('|' or ':' or ' ' or '\t'))
            {
                return false;
            }
        }

        return hasDash && trimmed.Contains('|');
    }
}
