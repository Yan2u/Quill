using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Quill.Editor.Editing;

/// <summary>
/// Decoded Windows CF_HTML clipboard payload preserving both the fragment and its surrounding context
/// (styles in &lt;head&gt; and whether the fragment sits inside an outer &lt;table&gt; or &lt;tr&gt;).
/// </summary>
internal sealed record CfHtmlPayload(
    string Fragment,
    string FullHtml,
    string? StylesCss,
    bool FragmentInsideTable,
    bool FragmentInsideRow);

/// <summary>
/// Encodes and decodes Windows CF_HTML ("HTML Format") strings using UTF-8 byte offsets as required
/// by the Win32 CF_HTML specification, avoiding UTF-16 char-index misalignment on non-ASCII text.
/// </summary>
internal static partial class CfHtmlCodec
{
    private const string StartFragmentMarker = "<!--StartFragment-->";
    private const string EndFragmentMarker = "<!--EndFragment-->";

    public static CfHtmlPayload Decode(string raw)
    {
        if (string.IsNullOrEmpty(raw))
        {
            return new CfHtmlPayload(string.Empty, string.Empty, null, false, false);
        }

        ParseHeader(raw, out bool hasHeader, out int startHtml, out int endHtml, out int startFragment, out int endFragment);

        byte[]? utf8 = null;
        string fullHtml;
        if (hasHeader && startHtml >= 0 && endHtml >= startHtml)
        {
            utf8 = Encoding.UTF8.GetBytes(raw);
            fullHtml = SliceUtf8(utf8, startHtml, endHtml);
        }
        else if (hasHeader)
        {
            int firstTag = raw.IndexOf('<');
            fullHtml = firstTag >= 0 ? raw[firstTag..] : string.Empty;
        }
        else
        {
            fullHtml = raw;
        }

        string fragment;
        string precedingHtml;
        if (TrySliceByMarkers(fullHtml, out string markedFragment, out string beforeMarkers))
        {
            fragment = markedFragment;
            precedingHtml = beforeMarkers;
        }
        else if (hasHeader && startFragment >= 0 && endFragment >= startFragment)
        {
            utf8 ??= Encoding.UTF8.GetBytes(raw);
            fragment = SliceUtf8(utf8, startFragment, endFragment);
            if (startHtml >= 0 && startFragment >= startHtml)
            {
                precedingHtml = SliceUtf8(utf8, startHtml, startFragment);
            }
            else
            {
                string before = SliceUtf8(utf8, 0, startFragment);
                int firstTag = before.IndexOf('<');
                precedingHtml = firstTag >= 0 ? before[firstTag..] : string.Empty;
            }
        }
        else
        {
            fragment = ExtractBodyOrSelf(fullHtml, out precedingHtml);
        }

        string? stylesCss = ExtractStyles(fullHtml);
        ComputeTableNesting(precedingHtml, out bool insideTable, out bool insideRow);
        return new CfHtmlPayload(fragment, fullHtml, stylesCss, insideTable, insideRow);
    }

    public static string Encode(string htmlFragment, bool includeExcelBrStyle = true)
    {
        string styleBlock = includeExcelBrStyle ? "<style>br{mso-data-placement:same-cell;}</style>" : string.Empty;
        string prefix = $"<html><head><meta charset=\"utf-8\">{styleBlock}</head><body>{StartFragmentMarker}";
        string suffix = $"{EndFragmentMarker}</body></html>";

        // Compute header size from a 10-digit template (all header fields are pure ASCII).
        string template = FormatHeader(0, 0, 0, 0);
        int headerBytes = Encoding.ASCII.GetByteCount(template);
        int startHtml = headerBytes;
        int startFragment = startHtml + Encoding.UTF8.GetByteCount(prefix);
        int endFragment = startFragment + Encoding.UTF8.GetByteCount(htmlFragment);
        int endHtml = endFragment + Encoding.UTF8.GetByteCount(suffix);

        return FormatHeader(startHtml, endHtml, startFragment, endFragment) + prefix + htmlFragment + suffix;
    }

    private static string FormatHeader(int startHtml, int endHtml, int startFragment, int endFragment) =>
        string.Create(
            CultureInfo.InvariantCulture,
            $"Version:0.9\r\nStartHTML:{startHtml:D10}\r\nEndHTML:{endHtml:D10}\r\nStartFragment:{startFragment:D10}\r\nEndFragment:{endFragment:D10}\r\n");

    private static void ParseHeader(
        string raw,
        out bool hasHeader,
        out int startHtml,
        out int endHtml,
        out int startFragment,
        out int endFragment)
    {
        hasHeader = false;
        startHtml = -1;
        endHtml = -1;
        startFragment = -1;
        endFragment = -1;

        int pos = 0;
        int lines = 0;
        while (pos < raw.Length && lines < 32)
        {
            int lineEnd = raw.IndexOf('\n', pos);
            string line = (lineEnd >= 0 ? raw[pos..lineEnd] : raw[pos..]).TrimEnd('\r');
            if (line.Length == 0 || line[0] == '<')
            {
                break;
            }

            int colon = line.IndexOf(':');
            if (colon <= 0)
            {
                break;
            }

            string key = line[..colon].Trim();
            string value = line[(colon + 1)..].Trim();
            if (key.Equals("Version", StringComparison.OrdinalIgnoreCase))
            {
                hasHeader = true;
            }
            else if (key.Equals("StartHTML", StringComparison.OrdinalIgnoreCase) && int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int sh))
            {
                hasHeader = true;
                startHtml = sh;
            }
            else if (key.Equals("EndHTML", StringComparison.OrdinalIgnoreCase) && int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int eh))
            {
                hasHeader = true;
                endHtml = eh;
            }
            else if (key.Equals("StartFragment", StringComparison.OrdinalIgnoreCase) && int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int sf))
            {
                hasHeader = true;
                startFragment = sf;
            }
            else if (key.Equals("EndFragment", StringComparison.OrdinalIgnoreCase) && int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int ef))
            {
                hasHeader = true;
                endFragment = ef;
            }

            if (lineEnd < 0)
            {
                break;
            }

            pos = lineEnd + 1;
            lines++;
        }
    }

    private static string SliceUtf8(byte[] utf8, int start, int end)
    {
        int clampedStart = Math.Clamp(start, 0, utf8.Length);
        int clampedEnd = Math.Clamp(end, clampedStart, utf8.Length);
        return Encoding.UTF8.GetString(utf8, clampedStart, clampedEnd - clampedStart);
    }

    private static bool TrySliceByMarkers(string html, out string fragment, out string preceding)
    {
        var startMatch = StartMarkerRegex().Match(html);
        if (!startMatch.Success)
        {
            fragment = string.Empty;
            preceding = string.Empty;
            return false;
        }

        int afterStart = startMatch.Index + startMatch.Length;
        var endMatch = EndMarkerRegex().Match(html, afterStart);
        if (!endMatch.Success)
        {
            fragment = string.Empty;
            preceding = string.Empty;
            return false;
        }

        fragment = html[afterStart..endMatch.Index];
        preceding = html[..startMatch.Index];
        return true;
    }

    private static string ExtractBodyOrSelf(string fullHtml, out string preceding)
    {
        var bodyOpen = BodyOpenRegex().Match(fullHtml);
        if (bodyOpen.Success)
        {
            int contentStart = bodyOpen.Index + bodyOpen.Length;
            var bodyClose = BodyCloseRegex().Match(fullHtml, contentStart);
            int contentEnd = bodyClose.Success ? bodyClose.Index : fullHtml.Length;
            preceding = fullHtml[..contentStart];
            return fullHtml[contentStart..contentEnd];
        }

        preceding = string.Empty;
        return fullHtml;
    }

    private static string? ExtractStyles(string fullHtml)
    {
        var matches = StyleBlockRegex().Matches(fullHtml);
        if (matches.Count == 0)
        {
            return null;
        }

        var builder = new StringBuilder();
        foreach (Match match in matches)
        {
            string css = match.Groups[1].Value.Trim();
            if (css.StartsWith("<!--", StringComparison.Ordinal))
            {
                css = css[4..];
            }

            if (css.EndsWith("-->", StringComparison.Ordinal))
            {
                css = css[..^3];
            }

            css = css.Trim();
            if (css.Length > 0)
            {
                if (builder.Length > 0)
                {
                    builder.Append('\n');
                }

                builder.Append(css);
            }
        }

        return builder.Length > 0 ? builder.ToString() : null;
    }

    private static void ComputeTableNesting(string precedingHtml, out bool insideTable, out bool insideRow)
    {
        int tableDepth = 0;
        int rowDepth = 0;
        int i = 0;
        while (i < precedingHtml.Length)
        {
            int lt = precedingHtml.IndexOf('<', i);
            if (lt < 0 || lt + 1 >= precedingHtml.Length)
            {
                break;
            }

            if (precedingHtml.AsSpan(lt).StartsWith("<!--", StringComparison.Ordinal))
            {
                int commentEnd = precedingHtml.IndexOf("-->", lt + 4, StringComparison.Ordinal);
                i = commentEnd >= 0 ? commentEnd + 3 : precedingHtml.Length;
                continue;
            }

            if (precedingHtml[lt + 1] is '!' or '?')
            {
                int declEnd = precedingHtml.IndexOf('>', lt + 2);
                i = declEnd >= 0 ? declEnd + 1 : precedingHtml.Length;
                continue;
            }

            bool closing = precedingHtml[lt + 1] == '/';
            int nameStart = closing ? lt + 2 : lt + 1;
            int nameEnd = nameStart;
            while (nameEnd < precedingHtml.Length && IsTagNameChar(precedingHtml[nameEnd]))
            {
                nameEnd++;
            }

            string tagName = precedingHtml[nameStart..nameEnd].ToLowerInvariant();
            int tagEnd = FindTagClose(precedingHtml, nameEnd);
            bool selfClosing = !closing && tagEnd > lt && precedingHtml[tagEnd - 1] == '/';

            if (tagName is "style" or "script" && !closing && !selfClosing && tagEnd >= 0)
            {
                int rawEnd = precedingHtml.IndexOf("</" + tagName, tagEnd + 1, StringComparison.OrdinalIgnoreCase);
                if (rawEnd >= 0)
                {
                    int rawClose = precedingHtml.IndexOf('>', rawEnd);
                    i = rawClose >= 0 ? rawClose + 1 : precedingHtml.Length;
                    continue;
                }
            }

            if (!selfClosing)
            {
                switch (tagName)
                {
                    case "table" when !closing:
                        tableDepth++;
                        break;
                    case "table" when closing:
                        if (tableDepth > 0)
                        {
                            tableDepth--;
                        }

                        rowDepth = 0;
                        break;
                    case "tr" when !closing && tableDepth > 0:
                        rowDepth++;
                        break;
                    case "tr" when closing && rowDepth > 0:
                        rowDepth--;
                        break;
                }
            }

            i = tagEnd >= 0 ? tagEnd + 1 : precedingHtml.Length;
        }

        insideTable = tableDepth > 0;
        insideRow = tableDepth > 0 && rowDepth > 0;
    }

    private static int FindTagClose(string html, int from)
    {
        char quote = '\0';
        for (int i = from; i < html.Length; i++)
        {
            char ch = html[i];
            if (quote != '\0')
            {
                if (ch == quote)
                {
                    quote = '\0';
                }
            }
            else if (ch is '"' or '\'')
            {
                quote = ch;
            }
            else if (ch == '>')
            {
                return i;
            }
        }

        return -1;
    }

    private static bool IsTagNameChar(char ch) => char.IsAsciiLetterOrDigit(ch) || ch is ':' or '-' or '_';

    [GeneratedRegex(@"<!--\s*StartFragment\s*-->", RegexOptions.IgnoreCase)]
    private static partial Regex StartMarkerRegex();

    [GeneratedRegex(@"<!--\s*EndFragment\s*-->", RegexOptions.IgnoreCase)]
    private static partial Regex EndMarkerRegex();

    [GeneratedRegex(@"<body\b[^>]*>", RegexOptions.IgnoreCase)]
    private static partial Regex BodyOpenRegex();

    [GeneratedRegex(@"</body\s*>", RegexOptions.IgnoreCase)]
    private static partial Regex BodyCloseRegex();

    [GeneratedRegex(@"<style\b[^>]*>([\s\S]*?)</style\s*>", RegexOptions.IgnoreCase)]
    private static partial Regex StyleBlockRegex();
}
