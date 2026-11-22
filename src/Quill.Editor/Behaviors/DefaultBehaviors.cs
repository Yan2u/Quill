using Quill.Editor.Documents;

namespace Quill.Editor.Behaviors;

/// <summary>
/// The behaviors of the built-in elements, in the dispatch order of 04-commands-and-behaviors.md.
/// </summary>
public static class DefaultBehaviors
{
    public static BehaviorRegistry Create()
    {
        var raw = new RawTextBehavior();
        return new BehaviorRegistry()
            .Register(BlockKind.Paragraph, new ParagraphBehavior())
            .Register(BlockKind.Heading, new HeadingBehavior())
            .Register(BlockKind.CodeBlock, raw)
            .Register(BlockKind.MathBlock, raw)
            .Register(BlockKind.HtmlBlock, raw)
            .Register(BlockKind.FrontMatter, raw)
            .Register(BlockKind.SourceLine, new SourceLineBehavior())
            .Register(BlockKind.TableCell, new TableCellBehavior())
            .Register(BlockKind.DefinitionSlot, new DefinitionSlotBehavior())
            .Register(BlockKind.CodeLanguage, new CodeLanguageBehavior())
            .Register(BlockKind.ListItem, new ListItemBehavior())
            .Register(BlockKind.BlockQuote, new BlockQuoteBehavior())
            .RegisterBoundary(new BlockCaretBehavior())
            .RegisterBoundary(new VirtualParagraphBehavior())
            .RegisterFallback(new TextBehavior());
    }
}
