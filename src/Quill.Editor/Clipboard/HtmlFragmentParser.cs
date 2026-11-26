using System.Net;

namespace Quill.Editor.Editing;

internal abstract class HtmlNode;

internal sealed class HtmlText(string value) : HtmlNode
{
    public string Value { get; } = value;
}

internal sealed class HtmlElement(string tag, Dictionary<string, string> attributes, List<HtmlNode> children) : HtmlNode
{
    public string Tag { get; } = tag;

    public Dictionary<string, string> Attributes { get; } = attributes;

    public List<HtmlNode> Children { get; } = children;

    public string? GetAttribute(string name) => Attributes.TryGetValue(name, out string? value) ? value : null;

    public bool HasAttribute(string name) => Attributes.ContainsKey(name);
}

/// <summary>
/// Forgiving, zero-dependency HTML5 fragment parser tailored for clipboard payloads from browsers,
/// Excel, Word, WPS, and Google Docs. Handles implicit closing of table/paragraph/list tags and
/// strips Office conditional comments and non-content metadata blocks.
/// </summary>
internal static class HtmlFragmentParser
{
    private const int MaxDepth = 128;

    private static readonly HashSet<string> VoidTags = new(StringComparer.Ordinal)
    {
        "area", "base", "br", "col", "embed", "hr", "img", "input",
        "link", "meta", "param", "source", "track", "wbr",
    };

    private static readonly HashSet<string> RawTextTags = new(StringComparer.Ordinal)
    {
        "script", "style", "title", "textarea", "noscript",
    };

    private static readonly HashSet<string> IgnoredTags = new(StringComparer.Ordinal)
    {
        "script", "style", "noscript", "title", "head", "meta", "link", "xml", "colgroup", "col",
    };

    private static readonly Dictionary<string, string> EmptyAttributes = new(StringComparer.OrdinalIgnoreCase);

    public static HtmlElement Parse(string html)
    {
        var root = new HtmlElement("#root", EmptyAttributes, []);
        if (string.IsNullOrEmpty(html))
        {
            return root;
        }

        var stack = new List<HtmlElement>(16) { root };
        int pos = 0;
        while (pos < html.Length)
        {
            int lt = html.IndexOf('<', pos);
            if (lt < 0)
            {
                AppendText(stack[^1], html[pos..]);
                break;
            }

            if (lt > pos)
            {
                AppendText(stack[^1], html[pos..lt]);
            }

            if (lt + 1 >= html.Length)
            {
                AppendText(stack[^1], "<");
                break;
            }

            // Comments and Office conditional comments: <!-- ... -->
            if (html.AsSpan(lt).StartsWith("<!--", StringComparison.Ordinal))
            {
                int commentEnd = html.IndexOf("-->", lt + 4, StringComparison.Ordinal);
                pos = commentEnd >= 0 ? commentEnd + 3 : html.Length;
                continue;
            }

            // Declarations, Office downlevel-revealed <![if ...]>, <!DOCTYPE ...>, <?xml ...?>
            if (html[lt + 1] is '!' or '?')
            {
                int declEnd = html.IndexOf('>', lt + 2);
                pos = declEnd >= 0 ? declEnd + 1 : html.Length;
                continue;
            }

            // Closing tag
            if (html[lt + 1] == '/')
            {
                int nameStart = lt + 2;
                int nameEnd = ScanTagNameEnd(html, nameStart);
                int tagEnd = FindTagClose(html, nameEnd);
                if (nameEnd > nameStart)
                {
                    string closingTag = html[nameStart..nameEnd].ToLowerInvariant();
                    CloseMatchingTag(stack, closingTag);
                }

                pos = tagEnd >= 0 ? tagEnd + 1 : html.Length;
                continue;
            }

            // Opening tag
            int openNameStart = lt + 1;
            int openNameEnd = ScanTagNameEnd(html, openNameStart);
            if (openNameEnd == openNameStart)
            {
                AppendText(stack[^1], "<");
                pos = lt + 1;
                continue;
            }

            string tag = html[openNameStart..openNameEnd].ToLowerInvariant();
            int openTagEnd = FindTagClose(html, openNameEnd);
            if (openTagEnd < 0)
            {
                AppendText(stack[^1], html[lt..]);
                break;
            }

            bool selfClosing = openTagEnd > openNameEnd && html[openTagEnd - 1] == '/';
            int attrEnd = selfClosing ? openTagEnd - 1 : openTagEnd;
            var attributes = ParseAttributes(html, openNameEnd, attrEnd);
            pos = openTagEnd + 1;

            if (RawTextTags.Contains(tag) && !selfClosing)
            {
                int rawEnd = FindRawTextEnd(html, pos, tag);
                pos = rawEnd >= 0 ? rawEnd : html.Length;
                if (IgnoredTags.Contains(tag))
                {
                    continue;
                }

                var rawElement = new HtmlElement(tag, attributes, []);
                stack[^1].Children.Add(rawElement);
                continue;
            }

            if (IgnoredTags.Contains(tag))
            {
                continue;
            }

            ApplyImplicitCloseRules(stack, tag);

            var element = new HtmlElement(tag, attributes, []);
            stack[^1].Children.Add(element);
            if (!selfClosing && !VoidTags.Contains(tag) && stack.Count < MaxDepth)
            {
                stack.Add(element);
            }
        }

        return root;
    }

    private static void AppendText(HtmlElement parent, string rawText)
    {
        if (string.IsNullOrEmpty(rawText))
        {
            return;
        }

        string decoded = rawText.Contains('&') ? WebUtility.HtmlDecode(rawText) : rawText;
        if (decoded.Length > 0)
        {
            parent.Children.Add(new HtmlText(decoded));
        }
    }

    private static void ApplyImplicitCloseRules(List<HtmlElement> stack, string nextTag)
    {
        switch (nextTag)
        {
            case "td" or "th":
                CloseUntilBoundary(stack, static t => t is "td" or "th", static t => t is "tr" or "table" or "thead" or "tbody" or "tfoot");
                break;
            case "tr":
                CloseUntilBoundary(stack, static t => t is "td" or "th" or "tr", static t => t is "table" or "thead" or "tbody" or "tfoot");
                break;
            case "thead" or "tbody" or "tfoot":
                CloseUntilBoundary(stack, static t => t is "td" or "th" or "tr" or "thead" or "tbody" or "tfoot", static t => t is "table");
                break;
            case "p":
                CloseUntilBoundary(stack, static t => t is "p", static t => t is "td" or "th" or "li" or "blockquote" or "div" or "table");
                break;
            case "li":
                CloseUntilBoundary(stack, static t => t is "li", static t => t is "ul" or "ol" or "td" or "th" or "table");
                break;
        }
    }

    private static void CloseUntilBoundary(List<HtmlElement> stack, Func<string, bool> shouldClose, Func<string, bool> isBoundary)
    {
        for (int i = stack.Count - 1; i >= 1; i--)
        {
            string tag = stack[i].Tag;
            if (isBoundary(tag))
            {
                return;
            }

            if (shouldClose(tag))
            {
                stack.RemoveRange(i, stack.Count - i);
                return;
            }
        }
    }

    private static void CloseMatchingTag(List<HtmlElement> stack, string closingTag)
    {
        for (int i = stack.Count - 1; i >= 1; i--)
        {
            string current = stack[i].Tag;
            if (current == closingTag)
            {
                stack.RemoveRange(i, stack.Count - i);
                return;
            }

            // Do not reach outside an enclosing <table> unless closing the <table> itself.
            if (current == "table" && closingTag != "table")
            {
                return;
            }
        }
    }

    private static Dictionary<string, string> ParseAttributes(string html, int start, int end)
    {
        Dictionary<string, string>? attrs = null;
        int pos = start;
        while (pos < end)
        {
            while (pos < end && char.IsWhiteSpace(html[pos]))
            {
                pos++;
            }

            if (pos >= end)
            {
                break;
            }

            int nameStart = pos;
            while (pos < end && !char.IsWhiteSpace(html[pos]) && html[pos] != '=')
            {
                pos++;
            }

            string name = html[nameStart..pos].Trim();
            if (name.Length == 0)
            {
                pos++;
                continue;
            }

            while (pos < end && char.IsWhiteSpace(html[pos]))
            {
                pos++;
            }

            string value = string.Empty;
            if (pos < end && html[pos] == '=')
            {
                pos++;
                while (pos < end && char.IsWhiteSpace(html[pos]))
                {
                    pos++;
                }

                if (pos < end && html[pos] is '"' or '\'')
                {
                    char quote = html[pos++];
                    int valueStart = pos;
                    int closeQuote = html.IndexOf(quote, pos, end - pos);
                    if (closeQuote >= 0)
                    {
                        value = html[valueStart..closeQuote];
                        pos = closeQuote + 1;
                    }
                    else
                    {
                        value = html[valueStart..end];
                        pos = end;
                    }
                }
                else
                {
                    int valueStart = pos;
                    while (pos < end && !char.IsWhiteSpace(html[pos]))
                    {
                        pos++;
                    }

                    value = html[valueStart..pos];
                }

                if (value.Contains('&'))
                {
                    value = WebUtility.HtmlDecode(value);
                }
            }

            attrs ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            attrs[name] = value;
        }

        return attrs ?? EmptyAttributes;
    }

    private static int ScanTagNameEnd(string html, int start)
    {
        int i = start;
        while (i < html.Length && (char.IsAsciiLetterOrDigit(html[i]) || html[i] is ':' or '-' or '_'))
        {
            i++;
        }

        return i;
    }

    private static int FindTagClose(string html, int start)
    {
        char quote = '\0';
        for (int i = start; i < html.Length; i++)
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

    private static int FindRawTextEnd(string html, int start, string tag)
    {
        int idx = start;
        while (idx < html.Length)
        {
            int close = html.IndexOf("</" + tag, idx, StringComparison.OrdinalIgnoreCase);
            if (close < 0)
            {
                return -1;
            }

            int afterName = close + 2 + tag.Length;
            if (afterName < html.Length && !char.IsAsciiLetterOrDigit(html[afterName]))
            {
                int gt = html.IndexOf('>', afterName);
                return gt >= 0 ? gt + 1 : html.Length;
            }

            idx = afterName;
        }

        return -1;
    }
}
