using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using Quill.Editor.Documents;
using Quill.Editor.Inlines;
using Quill.VariableXaml.Model;

namespace Quill.Editor.Projection;

/// <summary>The projectors of the block kinds (01-architecture.md, element registration).</summary>
public sealed partial class DocumentProjector
{

    private VNode Build(Block block, BlockContext context, BlockViewState state, ImmutableArray<BlockId> previousChildren, Entry entry, ImmutableArray<string> previousCandidates)
    {
        switch (block)
        {
            case TextLeafBlock leaf:
            {
                var builder = Start(TextElement(leaf), block, state);
                var map = RenderText(leaf, state, builder, previousCandidates, entry);
                SetLeafState(builder, state, map);
                switch (leaf)
                {
                    case Heading heading:
                        builder.Set(ProjectionNames.Level, heading.Level);
                        break;
                    case TableCell:
                        SetIf(builder, ProjectionNames.Alignment, context.Alignment, context.Alignment != TableAlignment.None);
                        SetIf(builder, ProjectionNames.IsHeader, true, context.IsHeader);
                        break;
                    case DefinitionSlot slot:
                        builder.Set(ProjectionNames.SlotKind, slot.SlotKind);
                        break;
                }

                return builder.Build();
            }

            case RawTextBlock raw:
            {
                // Display math shows its source only while focused (10-elements.md); otherwise it is one object.
                bool atomic = raw is MathBlock && !state.IsFocused;
                var builder = Start(RawElement(raw), block, state).Set(ProjectionNames.Text, raw.Text);
                var map = RenderRaw(raw, atomic);
                SetLeafState(builder, state, map);
                if (raw is CodeBlock && context.CodeInfo is { Length: > 0 } info)
                {
                    // The language comes from the code block's language slot (Q91).
                    builder.Set(ProjectionNames.Info, info);
                }

                return builder.Build();
            }

            case ThematicBreak:
            {
                // A block caret selects the rule whole; it never shows its source (Q60).
                var rule = Start(ProjectionNames.ThematicBreak, block, state);
                SetIf(rule, ProjectionNames.HasBlockCaret, true, state.HasBlockCaret);
                return rule.Build();
            }

            case TocBlock toc:
            {
                // Read-only (Q59): the source shows while the caret stands on it, and cannot be edited.
                var builder = Start(ProjectionNames.Toc, block, state).Set(ProjectionNames.Source, toc.Syntax?.Text.Trim() ?? "[toc]")
                    .Set(ProjectionNames.Entries, TocEntries());
                _tocs.Add(toc.Id);
                SetIf(builder, ProjectionNames.IsActive, true, state.IsFocused);
                SetIf(builder, ProjectionNames.HasBlockCaret, true, state.HasBlockCaret);
                return builder.Build();
            }
            case ContainerBlock container:
                return BuildContainer(container, context, state, previousChildren, entry);
            default:
                throw new NotSupportedException($"No projector for {block.Kind}.");
        }
    }

    private VNode BuildContainer(ContainerBlock container, BlockContext context, BlockViewState state, ImmutableArray<BlockId> previousChildren, Entry entry)
    {
        VElementBuilder builder;
        Func<ContainerBlock, int, BlockContext> childContext;
        switch (container)
        {
            case ListBlock list:
            {
                int level = context.ListLevel + 1;
                int start = list.Children.Count > 0 && list.Children[0] is ListItem { Number: { } first } ? first : 1;
                builder = Start(ProjectionNames.List, container, state).Set(ProjectionNames.Level, level);
                SetIf(builder, ProjectionNames.IsOrdered, true, list.IsOrdered);
                childContext = list.IsOrdered ? (_, i) => new BlockContext(level, start + i) : (_, _) => new BlockContext(level);
                break;
            }

            case ListItem item:
                builder = Start(ProjectionNames.ListItem, container, state).Set(ProjectionNames.Level, context.ListLevel);
                SetIf(builder, ProjectionNames.Number, context.Number, context.Number != 0);
                SetIf(builder, ProjectionNames.Task, item.Task, item.Task != TaskState.None);
                childContext = (_, _) => new BlockContext(context.ListLevel);
                break;
            case Table table:
                builder = Start(ProjectionNames.Table, container, state).Set(ProjectionNames.Alignments, string.Join(",", table.Alignments));
                childContext = (_, i) => new BlockContext(context.ListLevel, IsHeader: i == 0, Alignments: table.Alignments);
                break;
            case TableRow:
            {
                builder = Start(ProjectionNames.TableRow, container, state);
                SetIf(builder, ProjectionNames.IsHeader, true, context.IsHeader);
                var alignments = context.Alignments.IsDefault ? [] : context.Alignments;
                childContext = (_, i) => new BlockContext(context.ListLevel, Alignment: i < alignments.Length ? alignments[i] : TableAlignment.None, IsHeader: context.IsHeader);
                break;
            }

            case FootnoteDefinition footnote:
            {
                builder = Start(ProjectionNames.FootnoteDefinition, container, state);
                string label = LinkLabel.Normalize(footnote.Name.Text);
                int backlinks = _currentReferences.ResolveFootnote(_document, label) == footnote.Id ? _currentReferences.FootnoteReferenceCount(label) : 0;
                SetIf(builder, ProjectionNames.BacklinkCount, backlinks, backlinks > 0);
                childContext = (_, _) => new BlockContext(context.ListLevel);
                break;
            }

            case LinkReferenceDefinition:
                builder = Start(ProjectionNames.LinkReferenceDefinition, container, state);
                childContext = (_, _) => new BlockContext(context.ListLevel);
                break;
            case CodeFence fence:
                builder = Start(ProjectionNames.CodeFence, container, state);
                childContext = (_, _) => new BlockContext(context.ListLevel, CodeInfo: fence.Info);
                break;
            default:
                builder = Start(ProjectionNames.BlockQuote, container, state);
                childContext = (_, _) => new BlockContext(context.ListLevel);
                break;
        }

        SetIf(builder, ProjectionNames.IsActive, true, state.IsActive);
        builder.AddRange(ProjectChildren(container, childContext, previousChildren, out var childIds));
        entry.ChildIds = childIds;
        return builder.Build();
    }

    private BlockPositionMap RenderText(TextLeafBlock leaf, BlockViewState state, VElementBuilder builder, ImmutableArray<string> previousCandidates, Entry entry)
    {
        var tree = InlineTrees.Get(_currentReferences, leaf);
        var model = LeafRenderModel.For(tree, LeafRenderModel.HidesLeadingZeroWidthSpace(leaf));
        var rendered = LeafRenderer.Render(leaf.Id, model, Activation.OwnersOf(model, state.ActiveKey), leaf is Paragraph && InlineTrees.IsImageOnly(tree));
        _leavesRendered++;
        _maps = _maps.SetItem(leaf.Id, rendered.Map);
        entry.Candidates = tree.ReferenceCandidates;
        SetCandidates(leaf.Id, previousCandidates, tree.ReferenceCandidates);
        builder.Set(ProjectionNames.Content, rendered.Content);
        return rendered.Map;
    }

    private BlockPositionMap RenderRaw(RawTextBlock raw, bool atomic)
    {
        BlockPositionMap map;
        if (atomic)
        {
            map = new BlockPositionMap(raw.Id, raw.Text.Length == 0 ? [] : [new Segment(new TextRange(0, raw.Text.Length), new TextRange(0, 1), SegmentKind.Atomic)]);
        }
        else
        {
            // Raw text shows as it is, line breaks included: one segment maps it, however long the block (09).
            var whole = new TextRange(0, raw.Text.Length);
            map = new BlockPositionMap(raw.Id, raw.Text.Length == 0 ? [] : [new Segment(whole, whole, SegmentKind.Content)]);
        }

        _leavesRendered++;
        _maps = _maps.SetItem(raw.Id, map);
        return map;
    }

    /// <summary>The headings for a TOC: their shown text (no hidden syntax), level and key.</summary>
    private TocContent TocEntries()
    {
        var headings = HeadingIndex.Of(_document);
        _headingsVersion = headings.Version;
        var entries = headings.InOrder(_document)
            .Select(heading => new TocEntry(KeyOf(heading.Id), heading.Level, PlainText.Of(heading, _currentReferences).Replace('\n', ' ').Trim()));
        return new TocContent([.. entries]);
    }

    private static void SetLeafState(VElementBuilder builder, BlockViewState state, BlockPositionMap map)
    {
        SetIf(builder, ProjectionNames.IsFocused, true, state.IsFocused);
        if (state.SelectionStart >= 0)
        {
            builder.Set(ProjectionNames.SelectionStart, map.ToDisplay(state.SelectionStart, State.Affinity.Forward));
            builder.Set(ProjectionNames.SelectionEnd, map.ToDisplay(state.SelectionEnd, State.Affinity.Backward));
        }

        if (state.CompositionStart >= 0)
        {
            builder.Set(ProjectionNames.CompositionStart, map.ToDisplay(state.CompositionStart, State.Affinity.Forward));
            builder.Set(ProjectionNames.CompositionEnd, map.ToDisplay(state.CompositionEnd, State.Affinity.Backward));
        }
    }

    private static VElementBuilder Start(XamlName type, Block block, BlockViewState state)
    {
        var builder = V.Element(type).Key(KeyOf(block.Id)).Set(ProjectionNames.BlockId, KeyOf(block.Id));
        SetIf(builder, ProjectionNames.IsSelected, true, state.IsSelected);
        return builder;
    }

    private VNode VirtualParagraph(ContainerBlock container, int index)
    {
        // The key comes from the boundary: the block after it, or the end of the container.
        string key = index < container.Children.Count ? $"gap-{KeyOf(container.Children[index].Id)}" : $"gap-end-{KeyOf(container.Id)}";
        if (_gap is { } gap && gap.Key == key && Equals(gap.GetAttribute(XamlMemberName.Plain(ProjectionNames.Number)), index)
            && Equals(gap.GetAttribute(XamlMemberName.Plain(ProjectionNames.BlockId)), KeyOf(container.Id)))
        {
            return gap;
        }

        _gap = V.Element(ProjectionNames.VirtualParagraph).Key(key).Set(ProjectionNames.BlockId, KeyOf(container.Id)).Set(ProjectionNames.Number, index).Build();
        return _gap;
    }

    private static void SetIf<T>(VElementBuilder builder, string member, T value, bool condition)
    {
        if (condition)
        {
            builder.Set(member, value);
        }
    }

    /// <summary>The key and BlockId attribute of a block's node.</summary>
    public static string KeyOf(BlockId id) => $"b{id.Value}";

    private static XamlName TextElement(TextLeafBlock leaf) => leaf switch
    {
        Heading => ProjectionNames.Heading,
        TableCell => ProjectionNames.TableCell,
        DefinitionSlot => ProjectionNames.DefinitionSlot,
        CodeLanguage => ProjectionNames.CodeLanguage,
        _ => ProjectionNames.Paragraph,
    };

    private static XamlName RawElement(RawTextBlock raw) => raw switch
    {
        CodeBlock => ProjectionNames.CodeBlock,
        MathBlock => ProjectionNames.MathBlock,
        FrontMatter => ProjectionNames.FrontMatter,
        SourceLine => ProjectionNames.SourceLine,
        _ => ProjectionNames.HtmlBlock,
    };
}
