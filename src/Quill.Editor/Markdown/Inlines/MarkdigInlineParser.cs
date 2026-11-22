using System.Collections.Immutable;
using Markdig;
using Markdig.Extensions.EmphasisExtras;
using Markdig.Extensions.Mathematics;
using Markdig.Extensions.TaskLists;
using Markdig.Helpers;
using Markdig.Parsers;
using Markdig.Parsers.Inlines;
using Markdig.Syntax;
using Quill.Editor.Documents;
using Quill.Editor.Inlines;
using Quill.Editor.Markdown.Parsing;

namespace Quill.Editor.Markdown.Inlines;

/// <summary>
/// <see cref="IInlineParser"/> on Markdig's public <see cref="InlineProcessor"/> (Q2): the leaf source
/// becomes the lines of a free-standing paragraph, so block syntax in it (a heading text "1. foo") is
/// never read as blocks. The parser lists follow the import pipeline; footnote content uses only the
/// subset Typora parses there (Q44).
/// </summary>
internal sealed class MarkdigInlineParser : IInlineParser
{
    // Reference definitions only need to exist for Markdig to make links; the projection resolves the
    // real destination from the document.
    private const string PlaceholderUrl = "#";

    private readonly InlineParserList _full;
    private readonly InlineParserList _footnoteContent;

    private MarkdigInlineParser()
    {
        var full = MarkdigPipelines.ConfigureSyntax(new MarkdownPipelineBuilder());
        full.Build();
        var fullParsers = full.InlineParsers.Where(static parser => parser is not TaskListInlineParser).ToList();
        int link = fullParsers.FindIndex(static parser => parser is LinkInlineParser);
        fullParsers.Insert(Math.Max(link, 0), new FootnoteReferenceParser());
        _full = new InlineParserList(fullParsers);

        var footnote = new MarkdownPipelineBuilder()
            .UsePreciseSourceLocation()
            .UseEmphasisExtras(EmphasisExtraOptions.Subscript | EmphasisExtraOptions.Superscript)
            .UseMathematics();
        footnote.Build();
        _footnoteContent = new InlineParserList(footnote.InlineParsers.Where(static parser =>
            parser is EscapeInlineParser or EmphasisInlineParser or CodeInlineParser or MathInlineParser or LineBreakInlineParser));
    }

    public static MarkdigInlineParser Instance { get; } = new();

    public InlineTree Parse(string source, InlineSyntaxSet syntax, Func<string, bool>? isLinkDefined = null)
    {
        if (source.Length == 0)
        {
            return new InlineTree(source, syntax, [], [], []);
        }

        if (syntax == InlineSyntaxSet.PlainText)
        {
            var whole = new TextRange(0, source.Length);
            return new InlineTree(source, syntax, [new InlineNode(InlineKind.Text, whole, whole)], [], []);
        }

        var document = new MarkdownDocument();
        var candidates = ImmutableArray<string>.Empty;
        var defined = ImmutableArray<string>.Empty;
        if (syntax == InlineSyntaxSet.Full)
        {
            var found = ReferenceCandidates.Find(source);
            candidates = [.. found.Select(static candidate => candidate.Normalized).Distinct()];
            var definedBuilder = ImmutableArray.CreateBuilder<string>();
            foreach (var candidate in found)
            {
                if (isLinkDefined?.Invoke(candidate.Normalized) != true)
                {
                    continue;
                }

                if (!definedBuilder.Contains(candidate.Normalized))
                {
                    definedBuilder.Add(candidate.Normalized);
                }

                // Register the label as Markdig will look it up: as written and with whitespace collapsed.
                Define(document, candidate.Written);
                Define(document, string.Join(' ', candidate.Written.Split([' ', '\t', '\n'], StringSplitOptions.RemoveEmptyEntries)));
            }

            defined = definedBuilder.ToImmutable();
        }

        var paragraph = new ParagraphBlock { Lines = new StringLineGroup(4) };
        int position = 0;
        int lineIndex = 0;
        while (position <= source.Length)
        {
            int newline = source.IndexOf('\n', position);
            int end = newline < 0 ? source.Length : newline;
            var slice = new StringSlice(source, position, end - 1, newline < 0 ? NewLine.None : NewLine.LineFeed);
            paragraph.Lines.Add(new StringLine(ref slice, lineIndex++, 0, position, 0));
            if (newline < 0)
            {
                break;
            }

            position = newline + 1;
        }

        var processor = new InlineProcessor(document, syntax == InlineSyntaxSet.Full ? _full : _footnoteContent, true, new MarkdownParserContext(), false);
        processor.ProcessInlineLeaf(paragraph);
        var nodes = new InlineTreeBuilder(source).Build(paragraph.Inline);
        return new InlineTree(source, syntax, nodes, candidates, defined);
    }

    private static void Define(MarkdownDocument document, string label)
    {
        if (label.Length > 0)
        {
            document.SetLinkReferenceDefinition(label, new Markdig.Syntax.LinkReferenceDefinition(label, PlaceholderUrl, null), true);
        }
    }
}
