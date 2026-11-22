using Markdig;
using Markdig.Extensions.EmphasisExtras;
using Markdig.Extensions.Tables;
using Markdig.Parsers;
using Markdig.Syntax;

namespace Quill.Editor.Markdown.Parsing;

/// <summary>
/// The Markdig pipelines used by Markdown I/O (06-markdown-io.md). Markdig types must stay internal to
/// Markdown I/O.
/// </summary>
internal static class MarkdigPipelines
{
    /// <summary>
    /// Block structure for import. Leaves are not parsed for inlines: the importer only needs structure
    /// and positions, and skipping inline parsing keeps each leaf's <see cref="LeafBlock.Lines"/>.
    /// </summary>
    public static MarkdownPipeline Import { get; } = CreateImportPipeline();

    /// <summary>
    /// Configures the syntax Quill supports (Q13): CommonMark, GFM tables / task lists / strikethrough /
    /// autolinks, math, highlight, subscript and superscript, footnotes, YAML front matter, [toc] and
    /// emoji shortcodes.
    /// </summary>
    public static MarkdownPipelineBuilder ConfigureSyntax(MarkdownPipelineBuilder builder)
    {
        return builder
            .UsePreciseSourceLocation()
            .UsePipeTables(new PipeTableOptions { UseGfmRules = true })
            .UseTaskLists()
            .UseEmphasisExtras(EmphasisExtraOptions.Strikethrough | EmphasisExtraOptions.Subscript
                | EmphasisExtraOptions.Superscript | EmphasisExtraOptions.Marked)
            .UseAutoLinks()
            .UseMathematics()
            .UseYamlFrontMatter()
            .UseEmojiAndSmiley(enableSmileys: false)
            .Use<QuillBlockExtension>();
    }

    private static MarkdownPipeline CreateImportPipeline()
    {
        var builder = ConfigureSyntax(new MarkdownPipelineBuilder());
        var pipeline = builder.Build();
        foreach (var parser in builder.BlockParsers)
        {
            parser.Closed += static (_, block) =>
            {
                if (block is LeafBlock leaf)
                {
                    leaf.ProcessInlines = false;
                }
            };
        }

        return pipeline;
    }
}

/// <summary>
/// Adds Quill's own block parsers: in-place footnote definitions and "[toc]"; replaces the list parser
/// so that a rule indented into a list item stays in it (<see cref="QuillListBlockParser"/>).
/// </summary>
internal sealed class QuillBlockExtension : IMarkdownExtension
{
    public void Setup(MarkdownPipelineBuilder pipeline)
    {
        if (!pipeline.BlockParsers.Contains<QuillListBlockParser>())
        {
            pipeline.BlockParsers.Replace<ListBlockParser>(new QuillListBlockParser());
        }

        if (!pipeline.BlockParsers.Contains<TocBlockParser>())
        {
            pipeline.BlockParsers.Insert(0, new TocBlockParser());
        }

        if (!pipeline.BlockParsers.Contains<FootnoteDefinitionParser>())
        {
            pipeline.BlockParsers.Insert(0, new FootnoteDefinitionParser());
        }
    }

    public void Setup(MarkdownPipeline pipeline, Markdig.Renderers.IMarkdownRenderer renderer)
    {
    }
}
