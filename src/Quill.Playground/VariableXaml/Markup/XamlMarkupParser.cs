using System.Text;
using Quill.Playground.VariableXaml.Model;

namespace Quill.Playground.VariableXaml.Markup;

/// <summary>
/// A small position-tracking XML parser for the XAML subset VariableXamlRoot understands:
/// elements, attributes (plain, attached, directives), property elements, text with XAML
/// whitespace normalization, comments, CDATA and namespace declarations (incl. mc:Ignorable).
/// </summary>
/// <remarks>
/// Unlike System.Xml it records the exact source span of every element, which is what lets
/// <see cref="IncrementalMarkupParser"/> re-parse only the element that encloses an edit.
/// </remarks>
public sealed class XamlMarkupParser
{
    private readonly Func<XamlName, bool> _isWhitespaceSignificant;

    /// <param name="isWhitespaceSignificant">
    /// Returns true for element types whose content is a whitespace-significant collection
    /// (inline text such as TextBlock, Paragraph, Span), where spaces between elements are kept.
    /// </param>
    public XamlMarkupParser(Func<XamlName, bool>? isWhitespaceSignificant = null)
    {
        _isWhitespaceSignificant = isWhitespaceSignificant ?? (_ => false);
    }

    public MarkupDocument Parse(string text)
    {
        var spans = new List<ElementSpan>(Math.Max(16, text.Length / 48));
        var cursor = new Cursor(this, text, 0, spans);
        cursor.SkipMisc();
        if (cursor.AtEnd)
        {
            throw cursor.Error("The document has no root element.");
        }

        VNode root = cursor.ParseElement(NamespaceScope.Default, -1, SlotKind.Root, 0, 0);
        cursor.SkipMisc();
        if (!cursor.AtEnd)
        {
            throw cursor.Error("Unexpected content after the root element.");
        }

        return new MarkupDocument(text, root, spans.ToArray());
    }

    /// <summary>
    /// Parses exactly one element starting at <paramref name="start"/>. Spans are appended to
    /// <paramref name="spans"/> with parent indices relative to that list.
    /// </summary>
    internal VNode ParseElementAt(string text, int start, NamespaceScope scope, List<ElementSpan> spans, out int end)
    {
        var cursor = new Cursor(this, text, start, spans);
        VNode node = cursor.ParseElement(scope, -1, SlotKind.Root, 0, 0);
        end = cursor.Position;
        return node;
    }

    private readonly record struct RawAttribute(string Name, string Value, int Offset);

    private readonly record struct ContentItem(VNode? Element, int SpanIndex, string? Text);

    private sealed class Cursor
    {
        private readonly XamlMarkupParser _owner;
        private readonly string _text;
        private readonly List<ElementSpan> _spans;
        private int _pos;

        public Cursor(XamlMarkupParser owner, string text, int start, List<ElementSpan> spans)
        {
            _owner = owner;
            _text = text;
            _pos = start;
            _spans = spans;
        }

        public bool AtEnd => _pos >= _text.Length;

        public int Position => _pos;

        public void SkipMisc()
        {
            while (true)
            {
                SkipWhitespace();
                if (StartsWith("<!--"))
                {
                    SkipComment();
                }
                else if (StartsWith("<?"))
                {
                    SkipProcessingInstruction();
                }
                else
                {
                    return;
                }
            }
        }

        public VNode ParseElement(NamespaceScope scope, int parentIndex, SlotKind slot, int propertyIndex, int slotIndex)
        {
            int start = _pos;
            Expect('<');
            string qualifiedName = ReadName();
            List<RawAttribute> rawAttributes = ReadAttributes(out bool selfClosing);

            NamespaceScope elementScope = PushScope(scope, rawAttributes, start);
            (string? prefix, string localName) = SplitPrefix(qualifiedName);
            if (localName.Contains('.'))
            {
                throw Error($"Property element '{qualifiedName}' must be a direct child of its owner element.", start);
            }

            var type = new XamlName(ResolvePrefix(elementScope, prefix, start), localName);
            VAttribute[] attributes = ResolveAttributes(elementScope, rawAttributes);

            int spanIndex = _spans.Count;
            _spans.Add(new ElementSpan
            {
                Start = start,
                ParentIndex = parentIndex,
                Slot = slot,
                PropertyIndex = propertyIndex,
                SlotIndex = slotIndex,
                Scope = scope,
            });

            VNode[]? children = null;
            VPropertyElement[]? propertyElements = null;
            if (!selfClosing)
            {
                ParseContent(elementScope, qualifiedName, type, spanIndex, start, out children, out propertyElements);
            }

            VNode node = VNode.CreateElementOwned(type, attributes, children, propertyElements, key: null);
            ElementSpan span = _spans[spanIndex];
            span.Node = node;
            span.End = _pos;
            span.SubtreeLength = _spans.Count - spanIndex;
            _spans[spanIndex] = span;
            return node;
        }

        public XamlMarkupException Error(string message) => Error(message, _pos);

        private XamlMarkupException Error(string message, int offset)
        {
            offset = Math.Clamp(offset, 0, _text.Length);
            int line = 1;
            int lineStart = 0;
            for (int i = 0; i < offset; i++)
            {
                if (_text[i] == '\n')
                {
                    line++;
                    lineStart = i + 1;
                }
            }

            return new XamlMarkupException(message, offset, line, offset - lineStart + 1);
        }

        private void ParseContent(
            NamespaceScope scope,
            string qualifiedName,
            XamlName type,
            int spanIndex,
            int elementStart,
            out VNode[] children,
            out VPropertyElement[]? propertyElements)
        {
            var items = new List<ContentItem>();
            List<VPropertyElement>? properties = null;
            var text = new StringBuilder();

            while (true)
            {
                if (AtEnd)
                {
                    throw Error($"Element '{qualifiedName}' is not closed.", elementStart);
                }

                if (_text[_pos] != '<')
                {
                    ReadText(text);
                    continue;
                }

                if (StartsWith("</"))
                {
                    FlushText(items, text);
                    int endTagStart = _pos;
                    _pos += 2;
                    string endName = ReadName();
                    SkipWhitespace();
                    Expect('>');
                    if (endName != qualifiedName)
                    {
                        throw Error($"End tag '{endName}' does not match start tag '{qualifiedName}'.", endTagStart);
                    }

                    break;
                }

                if (StartsWith("<!--"))
                {
                    SkipComment();
                    continue;
                }

                if (StartsWith("<![CDATA["))
                {
                    ReadCData(text);
                    continue;
                }

                if (StartsWith("<?"))
                {
                    SkipProcessingInstruction();
                    continue;
                }

                FlushText(items, text);
                if (IsPropertyElementAhead())
                {
                    properties ??= [];
                    properties.Add(ParsePropertyElement(scope, type, spanIndex, properties.Count));
                    continue;
                }

                int childSpanIndex = _spans.Count;
                VNode child = ParseElement(scope, spanIndex, SlotKind.Child, 0, -1);
                if (IsIgnorableNamespace(scope, child.Type.Namespace))
                {
                    _spans.RemoveRange(childSpanIndex, _spans.Count - childSpanIndex);
                }
                else
                {
                    items.Add(new ContentItem(child, childSpanIndex, null));
                }
            }

            children = NormalizeContent(items, type, scope.PreserveSpace);
            propertyElements = properties?.ToArray();
        }

        private VPropertyElement ParsePropertyElement(NamespaceScope scope, XamlName ownerType, int ownerSpanIndex, int propertyIndex)
        {
            int start = _pos;
            Expect('<');
            string qualifiedName = ReadName();
            List<RawAttribute> rawAttributes = ReadAttributes(out bool selfClosing);
            NamespaceScope propertyScope = PushScope(scope, rawAttributes, start);
            foreach (RawAttribute attribute in rawAttributes)
            {
                if (!IsNamespaceDeclaration(attribute.Name))
                {
                    throw Error($"Property element '{qualifiedName}' cannot have attribute '{attribute.Name}'.", attribute.Offset);
                }
            }

            (string? prefix, string localName) = SplitPrefix(qualifiedName);
            int dot = localName.IndexOf('.');
            var owner = new XamlName(ResolvePrefix(propertyScope, prefix, start), localName[..dot]);
            string member = localName[(dot + 1)..];
            XamlMemberName name = owner == ownerType ? XamlMemberName.Plain(member) : XamlMemberName.Attached(owner, member);

            var values = new List<VNode>();
            var text = new StringBuilder();
            while (!selfClosing)
            {
                if (AtEnd)
                {
                    throw Error($"Property element '{qualifiedName}' is not closed.", start);
                }

                if (_text[_pos] != '<')
                {
                    ReadText(text);
                }
                else if (StartsWith("</"))
                {
                    int endTagStart = _pos;
                    _pos += 2;
                    string endName = ReadName();
                    SkipWhitespace();
                    Expect('>');
                    if (endName != qualifiedName)
                    {
                        throw Error($"End tag '{endName}' does not match start tag '{qualifiedName}'.", endTagStart);
                    }

                    break;
                }
                else if (StartsWith("<!--"))
                {
                    SkipComment();
                }
                else if (StartsWith("<![CDATA["))
                {
                    ReadCData(text);
                }
                else if (StartsWith("<?"))
                {
                    SkipProcessingInstruction();
                }
                else if (IsPropertyElementAhead())
                {
                    throw Error("A property element cannot contain another property element.");
                }
                else
                {
                    values.Add(ParseElement(propertyScope, ownerSpanIndex, SlotKind.PropertyValue, propertyIndex, values.Count));
                }
            }

            string textValue = propertyScope.PreserveSpace ? text.ToString() : CollapseWhitespace(text.ToString()).Trim(' ');
            if (textValue.Length > 0)
            {
                values.Add(VNode.CreateText(textValue));
            }

            return new VPropertyElement(name, values.ToArray());
        }

        private VNode[] NormalizeContent(List<ContentItem> items, XamlName type, bool preserveSpace)
        {
            if (items.Count == 0)
            {
                return [];
            }

            bool significant = _owner._isWhitespaceSignificant(type);
            var result = new List<VNode>(items.Count);
            for (int i = 0; i < items.Count; i++)
            {
                ContentItem item = items[i];
                if (item.Element is not null)
                {
                    ElementSpan span = _spans[item.SpanIndex];
                    span.SlotIndex = result.Count;
                    _spans[item.SpanIndex] = span;
                    result.Add(item.Element);
                    continue;
                }

                string text = item.Text!;
                if (!preserveSpace)
                {
                    text = CollapseWhitespace(text);
                    if (significant)
                    {
                        if (i == 0)
                        {
                            text = text.TrimStart(' ');
                        }

                        if (i == items.Count - 1)
                        {
                            text = text.TrimEnd(' ');
                        }
                    }
                    else
                    {
                        text = text.Trim(' ');
                    }
                }

                if (text.Length > 0)
                {
                    result.Add(VNode.CreateText(text));
                }
            }

            return result.ToArray();
        }

        private List<RawAttribute> ReadAttributes(out bool selfClosing)
        {
            var attributes = new List<RawAttribute>();
            while (true)
            {
                SkipWhitespace();
                if (AtEnd)
                {
                    throw Error("Unexpected end of markup inside a start tag.");
                }

                char c = _text[_pos];
                if (c == '/')
                {
                    _pos++;
                    Expect('>');
                    selfClosing = true;
                    return attributes;
                }

                if (c == '>')
                {
                    _pos++;
                    selfClosing = false;
                    return attributes;
                }

                int nameStart = _pos;
                string name = ReadName();
                SkipWhitespace();
                Expect('=');
                SkipWhitespace();
                if (AtEnd || (_text[_pos] != '"' && _text[_pos] != '\''))
                {
                    throw Error($"Attribute '{name}' value must be quoted.");
                }

                char quote = _text[_pos++];
                string value = ReadAttributeValue(quote);
                foreach (RawAttribute existing in attributes)
                {
                    if (existing.Name == name)
                    {
                        throw Error($"Duplicate attribute '{name}'.", nameStart);
                    }
                }

                attributes.Add(new RawAttribute(name, value, nameStart));
            }
        }

        private NamespaceScope PushScope(NamespaceScope scope, List<RawAttribute> attributes, int offset)
        {
            Dictionary<string, string>? prefixes = null;
            bool? preserveSpace = null;
            foreach (RawAttribute attribute in attributes)
            {
                if (attribute.Name == "xmlns")
                {
                    (prefixes ??= [])[string.Empty] = attribute.Value;
                }
                else if (attribute.Name.StartsWith("xmlns:", StringComparison.Ordinal))
                {
                    (prefixes ??= [])[attribute.Name[6..]] = attribute.Value;
                }
                else if (attribute.Name == "xml:space")
                {
                    preserveSpace = attribute.Value == "preserve";
                }
            }

            NamespaceScope pushed = scope.Push(prefixes, null, preserveSpace);
            HashSet<string>? ignorable = null;
            foreach (RawAttribute attribute in attributes)
            {
                (string? prefix, string localName) = SplitPrefix(attribute.Name);
                if (prefix is null || localName != "Ignorable" || pushed.LookupNamespace(prefix) != XamlNamespaces.MarkupCompatibility)
                {
                    continue;
                }

                foreach (string ignorablePrefix in attribute.Value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
                {
                    (ignorable ??= []).Add(ResolvePrefix(pushed, ignorablePrefix, offset));
                }
            }

            return ignorable is null ? pushed : scope.Push(prefixes, ignorable, preserveSpace);
        }

        private VAttribute[] ResolveAttributes(NamespaceScope scope, List<RawAttribute> rawAttributes)
        {
            var attributes = new List<VAttribute>(rawAttributes.Count);
            foreach (RawAttribute raw in rawAttributes)
            {
                if (IsNamespaceDeclaration(raw.Name))
                {
                    continue;
                }

                (string? prefix, string localName) = SplitPrefix(raw.Name);
                XamlMemberName name;
                if (prefix is not null)
                {
                    string ns = ResolvePrefix(scope, prefix, raw.Offset);
                    if (ns == XamlNamespaces.Xml || IsIgnorableNamespace(scope, ns))
                    {
                        continue;
                    }

                    if (ns is XamlNamespaces.Xaml or XamlNamespaces.VariableXaml)
                    {
                        name = XamlMemberName.Directive(ns, localName);
                    }
                    else
                    {
                        int dot = localName.IndexOf('.');
                        if (dot <= 0)
                        {
                            throw Error($"Attribute '{raw.Name}' must be an attached member (Owner.Member).", raw.Offset);
                        }

                        name = XamlMemberName.Attached(new XamlName(ns, localName[..dot]), localName[(dot + 1)..]);
                    }
                }
                else
                {
                    int dot = localName.IndexOf('.');
                    name = dot > 0
                        ? XamlMemberName.Attached(new XamlName(scope.LookupNamespace(string.Empty) ?? XamlNamespaces.Presentation, localName[..dot]), localName[(dot + 1)..])
                        : XamlMemberName.Plain(localName);
                }

                attributes.Add(new VAttribute(name, raw.Value, IsMarkup: true));
            }

            return attributes.ToArray();
        }

        private static bool IsIgnorableNamespace(NamespaceScope scope, string ns) =>
            ns != XamlNamespaces.VariableXaml && scope.IsIgnorable(ns);

        private static bool IsNamespaceDeclaration(string name) =>
            name == "xmlns" || name == "xml:space" || name.StartsWith("xmlns:", StringComparison.Ordinal);

        private string ResolvePrefix(NamespaceScope scope, string? prefix, int offset) =>
            scope.LookupNamespace(prefix ?? string.Empty)
            ?? throw Error($"Namespace prefix '{prefix}' is not declared.", offset);

        private static (string? Prefix, string LocalName) SplitPrefix(string name)
        {
            int colon = name.IndexOf(':');
            return colon < 0 ? (null, name) : (name[..colon], name[(colon + 1)..]);
        }

        private bool IsPropertyElementAhead()
        {
            int i = _pos + 1;
            while (i < _text.Length && IsNameChar(_text[i]))
            {
                if (_text[i] == '.')
                {
                    return true;
                }

                i++;
            }

            return false;
        }

        private void ReadText(StringBuilder builder)
        {
            while (!AtEnd && _text[_pos] != '<')
            {
                char c = _text[_pos];
                if (c == '&')
                {
                    ReadEntity(builder);
                }
                else if (c == '\r')
                {
                    builder.Append('\n');
                    _pos += _pos + 1 < _text.Length && _text[_pos + 1] == '\n' ? 2 : 1;
                }
                else
                {
                    builder.Append(c);
                    _pos++;
                }
            }
        }

        private void ReadCData(StringBuilder builder)
        {
            int start = _pos;
            _pos += "<![CDATA[".Length;
            int end = _text.IndexOf("]]>", _pos, StringComparison.Ordinal);
            if (end < 0)
            {
                throw Error("CDATA section is not closed.", start);
            }

            builder.Append(_text, _pos, end - _pos);
            _pos = end + 3;
        }

        private string ReadAttributeValue(char quote)
        {
            var builder = new StringBuilder();
            while (true)
            {
                if (AtEnd)
                {
                    throw Error("Attribute value is not closed.");
                }

                char c = _text[_pos];
                if (c == quote)
                {
                    _pos++;
                    return builder.ToString();
                }

                if (c == '<')
                {
                    throw Error("'<' is not allowed in attribute values; use &lt;.");
                }

                if (c == '&')
                {
                    ReadEntity(builder);
                    continue;
                }

                // XML attribute-value normalization: literal whitespace characters become spaces.
                builder.Append(c is '\t' or '\n' or '\r' ? ' ' : c);
                if (c == '\r' && _pos + 1 < _text.Length && _text[_pos + 1] == '\n')
                {
                    _pos++;
                }

                _pos++;
            }
        }

        private void ReadEntity(StringBuilder builder)
        {
            int start = _pos;
            int end = _text.IndexOf(';', _pos);
            if (end < 0 || end - start > 12)
            {
                throw Error("Malformed entity reference; use &amp; for a literal '&'.", start);
            }

            ReadOnlySpan<char> entity = _text.AsSpan(start + 1, end - start - 1);
            _pos = end + 1;
            switch (entity)
            {
                case "lt": builder.Append('<'); return;
                case "gt": builder.Append('>'); return;
                case "amp": builder.Append('&'); return;
                case "quot": builder.Append('"'); return;
                case "apos": builder.Append('\''); return;
            }

            if (entity.Length > 1 && entity[0] == '#')
            {
                bool hex = entity[1] is 'x' or 'X';
                ReadOnlySpan<char> digits = hex ? entity[2..] : entity[1..];
                if (int.TryParse(digits, hex ? System.Globalization.NumberStyles.HexNumber : System.Globalization.NumberStyles.None, null, out int codePoint)
                    && codePoint is > 0 and <= 0x10FFFF)
                {
                    builder.Append(char.ConvertFromUtf32(codePoint));
                    return;
                }
            }

            throw Error($"Unknown entity '&{entity};'.", start);
        }

        private string ReadName()
        {
            int start = _pos;
            if (AtEnd || !IsNameStartChar(_text[_pos]))
            {
                throw Error("Expected a name.");
            }

            while (!AtEnd && IsNameChar(_text[_pos]))
            {
                _pos++;
            }

            return _text[start.._pos];
        }

        private void SkipComment()
        {
            int start = _pos;
            int end = _text.IndexOf("-->", _pos + 4, StringComparison.Ordinal);
            if (end < 0)
            {
                throw Error("Comment is not closed.", start);
            }

            _pos = end + 3;
        }

        private void SkipProcessingInstruction()
        {
            int start = _pos;
            int end = _text.IndexOf("?>", _pos + 2, StringComparison.Ordinal);
            if (end < 0)
            {
                throw Error("Processing instruction is not closed.", start);
            }

            _pos = end + 2;
        }

        private void SkipWhitespace()
        {
            while (!AtEnd && IsXmlWhitespace(_text[_pos]))
            {
                _pos++;
            }
        }

        private void Expect(char c)
        {
            if (AtEnd || _text[_pos] != c)
            {
                throw Error(AtEnd ? $"Unexpected end of markup; expected '{c}'." : $"Expected '{c}' but found '{_text[_pos]}'.");
            }

            _pos++;
        }

        private bool StartsWith(string value) => string.CompareOrdinal(_text, _pos, value, 0, value.Length) == 0;

        private static void FlushText(List<ContentItem> items, StringBuilder text)
        {
            if (text.Length > 0)
            {
                items.Add(new ContentItem(null, -1, text.ToString()));
                text.Clear();
            }
        }
    }

    /// <summary>
    /// XAML whitespace rules: whitespace runs collapse to one space, and line breaks between two
    /// East Asian characters are removed entirely (so wrapped CJK markup does not gain spaces).
    /// </summary>
    internal static string CollapseWhitespace(string text)
    {
        StringBuilder? builder = null;
        int i = 0;
        while (i < text.Length)
        {
            char c = text[i];
            if (!IsXmlWhitespace(c))
            {
                builder?.Append(c);
                i++;
                continue;
            }

            int runStart = i;
            bool hasLineBreak = false;
            while (i < text.Length && IsXmlWhitespace(text[i]))
            {
                hasLineBreak |= text[i] == '\n';
                i++;
            }

            bool isSingleSpace = i - runStart == 1 && c == ' ';
            bool dropRun = hasLineBreak && runStart > 0 && i < text.Length && IsEastAsian(text[runStart - 1]) && IsEastAsian(text[i]);
            if (isSingleSpace && builder is null)
            {
                continue;
            }

            builder ??= new StringBuilder(text, 0, runStart, text.Length);
            if (!dropRun)
            {
                builder.Append(' ');
            }
        }

        return builder?.ToString() ?? text;
    }

    private static bool IsXmlWhitespace(char c) => c is ' ' or '\t' or '\n' or '\r';

    private static bool IsNameStartChar(char c) => char.IsLetter(c) || c is '_' or ':';

    private static bool IsNameChar(char c) => char.IsLetterOrDigit(c) || c is '_' or ':' or '.' or '-';

    private static bool IsEastAsian(char c) =>
        c is (>= '　' and <= '〿') or (>= '぀' and <= 'ヿ') or (>= '㐀' and <= '䶿')
            or (>= '一' and <= '鿿') or (>= '가' and <= '힯') or (>= '豈' and <= '﫿')
            or (>= '＀' and <= '￯');
}
