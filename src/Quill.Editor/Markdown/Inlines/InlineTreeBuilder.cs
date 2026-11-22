using System.Collections.Immutable;
using System.Text.RegularExpressions;
using Markdig.Extensions.Emoji;
using Markdig.Extensions.Mathematics;
using Markdig.Syntax.Inlines;
using Quill.Editor.Inlines;

namespace Quill.Editor.Markdown.Inlines;

/// <summary>
/// Converts Markdig's inline tree into <see cref="InlineNode"/>s whose ranges tile the source. Markdig
/// only supplies the syntax nodes and their positions; all plain text, including the text Markdig
/// trims (such as the spaces before a hard break), is filled in from the gaps, and backslash escapes
/// are found in those gaps. Whitelisted HTML tags are paired here (Q37).
/// </summary>
internal sealed partial class InlineTreeBuilder
{
    private static readonly string[] SpanTags = ["u", "sub", "sup", "kbd"];

    private static long _droppedNodes;

    private readonly string _source;

    public InlineTreeBuilder(string source)
    {
        _source = source;
    }

    /// <summary>Nodes dropped because their positions broke the tiling, since the process started (tests assert none).</summary>
    public static long DroppedNodes => Interlocked.Read(ref _droppedNodes);

    public ImmutableArray<InlineNode> Build(ContainerInline? root) =>
        Level(root, new TextRange(0, _source.Length));

    private ImmutableArray<InlineNode> Level(ContainerInline? container, TextRange content)
    {
        var nodes = new List<InlineNode>();
        if (container is not null)
        {
            foreach (var child in container)
            {
                Convert(child, nodes);
            }
        }

        nodes.Sort(static (a, b) => a.Range.Start.CompareTo(b.Range.Start));
        PairHtml(nodes);
        return Fill(nodes, content);
    }

    private void Convert(Inline inline, List<InlineNode> into)
    {
        var span = inline.Span;
        var range = span.IsEmpty ? default : new TextRange(span.Start, span.End + 1);
        switch (inline)
        {
            case EmojiInline emoji:
                into.Add(new InlineNode(InlineKind.Emoji, range, new TextRange(range.Start, range.Start)) { Value = emoji.Content.ToString() });
                break;
            case LiteralInline:
                // Plain text comes from the gaps.
                break;
            case HtmlEntityInline entity:
                into.Add(new InlineNode(InlineKind.Entity, range, new TextRange(range.Start, range.Start)) { Value = entity.Transcoded.ToString() });
                break;
            case EmphasisInline emphasis:
            {
                int count = emphasis.DelimiterCount;
                var content = new TextRange(range.Start + count, range.End - count);
                into.Add(new InlineNode(EmphasisKind(emphasis.DelimiterChar, count), range, content, Level(emphasis, content)));
                break;
            }

            case CodeInline code:
                into.Add(Delimited(InlineKind.Code, range, code.DelimiterCount));
                break;
            case MathInline math:
                into.Add(Delimited(InlineKind.Math, range, math.DelimiterCount));
                break;
            case LinkInline link:
                into.Add(ConvertLink(link, range));
                break;
            case AutolinkInline autolink:
                into.Add(new InlineNode(InlineKind.Autolink, range, new TextRange(range.Start + 1, range.End - 1)) { Value = autolink.Url, IsEmail = autolink.IsEmail });
                break;
            case LineBreakInline lineBreak:
                ConvertLineBreak(lineBreak, range, into);
                break;
            case HtmlInline html:
                into.Add(new InlineNode(InlineKind.Html, range, range) { Value = html.Tag });
                break;
            case FootnoteReferenceInline footnote:
                into.Add(new InlineNode(InlineKind.FootnoteReference, range, new TextRange(range.Start + 2, range.End - 1)) { Label = footnote.Label });
                break;
            case ContainerInline other:
                // Leftover delimiters and the like: their children belong to this level.
                foreach (var child in other)
                {
                    Convert(child, into);
                }

                break;
        }
    }

    private static InlineKind EmphasisKind(char delimiter, int count) => delimiter switch
    {
        '~' => count >= 2 ? InlineKind.Strikethrough : InlineKind.Subscript,
        '^' => InlineKind.Superscript,
        '=' => InlineKind.Highlight,
        _ => count >= 2 ? InlineKind.Strong : InlineKind.Emphasis,
    };

    private static InlineNode Delimited(InlineKind kind, TextRange range, int count) =>
        new(kind, range, new TextRange(range.Start + count, range.End - count));

    private InlineNode ConvertLink(LinkInline link, TextRange range)
    {
        if (link.IsAutoLink)
        {
            return new InlineNode(InlineKind.Url, range, range) { Value = link.Url };
        }

        var kind = link.IsImage ? InlineKind.Image : InlineKind.Link;
        int textStart = range.Start + (link.IsImage ? 2 : 1);
        int textEnd = textStart;
        foreach (var child in link)
        {
            if (!child.Span.IsEmpty)
            {
                textEnd = Math.Max(textEnd, child.Span.End + 1);
            }
        }

        textEnd = ClosingBracket(textEnd, range.End);
        var content = new TextRange(textStart, textEnd);
        var children = Level(link, content);
        string tail = _source[Math.Min(textEnd + 1, range.End)..range.End];
        if (link.Reference is null && !link.IsShortcut && tail.StartsWith('('))
        {
            return new InlineNode(kind, range, content, children)
            {
                LinkForm = LinkForm.Inline,
                Value = link.Url,
                Title = string.IsNullOrEmpty(link.Title) ? null : link.Title,
                DestinationRange = SpanAfter(link.UrlSpan, textEnd, range.End),
                TitleRange = SpanAfter(link.TitleSpan, textEnd, range.End),
            };
        }

        var (form, label) = tail switch
        {
            "[]" => (LinkForm.Collapsed, _source[content.Start..content.End]),
            ['[', .., ']'] => (LinkForm.Full, tail[1..^1]),
            _ => (LinkForm.Shortcut, _source[content.Start..content.End]),
        };
        return new InlineNode(kind, range, content, children) { LinkForm = form, Label = label, Value = link.Url };
    }

    /// <summary>The ']' that closes a link text, at or after <paramref name="from"/>.</summary>
    private int ClosingBracket(int from, int end)
    {
        for (int i = from; i < end; i++)
        {
            if (_source[i] == '\\')
            {
                i++;
            }
            else if (_source[i] == ']')
            {
                return i;
            }
        }

        return Math.Min(from, end);
    }

    private static TextRange? SpanAfter(Markdig.Syntax.SourceSpan span, int after, int end) =>
        !span.IsEmpty && span.Start > after && span.End < end ? new TextRange(span.Start, span.End + 1) : null;

    private void ConvertLineBreak(LineBreakInline lineBreak, TextRange range, List<InlineNode> into)
    {
        int newline = _source.IndexOf('\n', Math.Max(range.Start, 0));
        if (newline < 0 || newline >= range.End)
        {
            return;
        }

        if (!lineBreak.IsHard)
        {
            into.Add(new InlineNode(InlineKind.SoftBreak, new TextRange(newline, newline + 1), new TextRange(newline, newline + 1)));
            return;
        }

        int start = newline;
        if (lineBreak.IsBackslash)
        {
            start = newline - 1;
        }
        else
        {
            // The trailing spaces, which Markdig trims from the text before.
            int floor = into.Count > 0 ? into[^1].Range.End : 0;
            while (start > floor && _source[start - 1] == ' ')
            {
                start--;
            }
        }

        into.Add(new InlineNode(InlineKind.HardBreak, new TextRange(start, newline + 1), new TextRange(newline, newline + 1)));
    }

    /// <summary>
    /// Pairs whitelisted tags on one level into <see cref="InlineKind.HtmlSpan"/> nodes and turns
    /// <c>&lt;br&gt;</c> into <see cref="InlineKind.HtmlBreak"/>. Unpaired tags stay plain HTML.
    /// </summary>
    private void PairHtml(List<InlineNode> nodes)
    {
        var open = new List<(int Index, string Name)>();
        for (int i = 0; i < nodes.Count; i++)
        {
            var node = nodes[i];
            if (node.Kind != InlineKind.Html || TagPattern().Match(node.Value ?? string.Empty) is not { Success: true } match)
            {
                continue;
            }

            string name = match.Groups["name"].Value.ToLowerInvariant();
            bool closing = match.Groups["close"].Success;
            if (name == "br" && !closing)
            {
                nodes[i] = new InlineNode(InlineKind.HtmlBreak, node.Range, new TextRange(node.Range.End, node.Range.End));
                continue;
            }

            if (Array.IndexOf(SpanTags, name) < 0 || match.Groups["self"].Success)
            {
                continue;
            }

            if (!closing)
            {
                open.Add((i, name));
                continue;
            }

            int opener = open.FindLastIndex(entry => entry.Name == name);
            if (opener < 0)
            {
                continue;
            }

            int start = open[opener].Index;
            open.RemoveRange(opener, open.Count - opener);
            var content = new TextRange(nodes[start].Range.End, node.Range.Start);
            var inner = nodes.GetRange(start + 1, i - start - 1);
            var span = new InlineNode(InlineKind.HtmlSpan, new TextRange(nodes[start].Range.Start, node.Range.End), content, Fill(inner, content)) { Value = name };
            nodes.RemoveRange(start, i - start + 1);
            nodes.Insert(start, span);
            i = start;
        }
    }

    /// <summary>Fills the gaps of <paramref name="content"/> between the nodes with text, escapes and line breaks.</summary>
    private ImmutableArray<InlineNode> Fill(List<InlineNode> nodes, TextRange content)
    {
        var result = ImmutableArray.CreateBuilder<InlineNode>(nodes.Count * 2 + 1);
        int position = content.Start;
        foreach (var node in nodes)
        {
            // A node outside the content or overlapping the previous one would break the tiling; such
            // nodes only come from position bugs, and dropping them keeps the text.
            if (node.Range.Start < position || node.Range.End > content.End || node.Range.End < node.Range.Start)
            {
                Interlocked.Increment(ref _droppedNodes);
                continue;
            }

            AddText(result, position, node.Range.Start);
            result.Add(node);
            position = node.Range.End;
        }

        AddText(result, position, content.End);
        return result.ToImmutable();
    }

    private void AddText(ImmutableArray<InlineNode>.Builder into, int start, int end)
    {
        int textStart = start;
        for (int i = start; i < end; i++)
        {
            char c = _source[i];
            if (c == '\\' && i + 1 < end && IsAsciiPunctuation(_source[i + 1]))
            {
                AddPlain(into, textStart, i);
                into.Add(new InlineNode(InlineKind.Escape, new TextRange(i, i + 2), new TextRange(i + 1, i + 2)));
                textStart = i + 2;
                i++;
            }
            else if (c == '\n')
            {
                AddPlain(into, textStart, i);
                into.Add(new InlineNode(InlineKind.SoftBreak, new TextRange(i, i + 1), new TextRange(i, i + 1)));
                textStart = i + 1;
            }
        }

        AddPlain(into, textStart, end);
    }

    private static void AddPlain(ImmutableArray<InlineNode>.Builder into, int start, int end)
    {
        if (end > start)
        {
            var range = new TextRange(start, end);
            into.Add(new InlineNode(InlineKind.Text, range, range));
        }
    }

    private static bool IsAsciiPunctuation(char c) => c < 128 && char.IsPunctuation(c) || c is '$' or '+' or '<' or '=' or '>' or '^' or '`' or '|' or '~';

    [GeneratedRegex(@"^<(?<close>/)?(?<name>[A-Za-z][A-Za-z0-9-]*)(?:\s[^>]*?)?\s*(?<self>/)?>$", RegexOptions.CultureInvariant)]
    private static partial Regex TagPattern();
}
