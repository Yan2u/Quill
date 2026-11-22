using System.Collections.Immutable;
using Quill.Editor.Documents;

namespace Quill.Editor.Tests.Support;

/// <summary>
/// Builds documents without syntax metadata, as editing creates them, with sequential IDs.
/// </summary>
public sealed class ModelBuilder
{
    private readonly SequentialBlockIdSource _ids = new();

    public Document Document(params Block[] blocks) => new() { Id = Id(), Children = [.. blocks] };

    public Paragraph Paragraph(string text) => new() { Id = Id(), Text = text };

    public Heading Heading(int level, string text) => new() { Id = Id(), Level = level, Text = text };

    public CodeFence Code(string text, string info = "") => CodeFence.Create(Id, text, info);

    public MathBlock Math(string text) => new() { Id = Id(), Text = text };

    public HtmlBlock Html(string text) => new() { Id = Id(), Text = text };

    public ThematicBreak Rule() => new() { Id = Id() };

    public TocBlock Toc() => new() { Id = Id() };

    public BlockQuote Quote(params Block[] children) => new() { Id = Id(), Children = [.. children] };

    public ListBlock Bullets(params ListItem[] items) => new() { Id = Id(), IsOrdered = false, Children = [.. items] };

    public ListBlock Numbered(params ListItem[] items) => new() { Id = Id(), IsOrdered = true, Children = [.. items] };

    public ListItem Item(params Block[] children) => new() { Id = Id(), Children = [.. children] };

    public ListItem Item(string text) => Item(Paragraph(text));

    public ListItem NumberedItem(int number, params Block[] children) => Item(children) with { Number = number };

    public ListItem Task(bool done, string text) => Item(text) with { Task = done ? TaskState.Checked : TaskState.Unchecked };

    public Table Table(ImmutableArray<TableAlignment> alignments, params string[][] rows) => new()
    {
        Id = Id(),
        Alignments = alignments,
        Children = [.. rows.Select(row => (Block)new TableRow
        {
            Id = Id(),
            Children = [.. row.Select(text => (Block)new TableCell { Id = Id(), Text = text })],
        })],
    };

    public FootnoteDefinition Footnote(string name, string content, params Block[] continuation) => new()
    {
        Id = Id(),
        Children = ImmutableList.Create<Block>(Slot(DefinitionSlotKind.FootnoteName, name), Slot(DefinitionSlotKind.FootnoteContent, content))
            .AddRange(continuation),
    };

    public LinkReferenceDefinition LinkDefinition(string label, string destination, string title = "") => new()
    {
        Id = Id(),
        Children =
        [
            Slot(DefinitionSlotKind.LinkLabel, label),
            Slot(DefinitionSlotKind.LinkDestination, destination),
            Slot(DefinitionSlotKind.LinkTitle, title),
        ],
    };

    private DefinitionSlot Slot(DefinitionSlotKind kind, string text) => new() { Id = Id(), SlotKind = kind, Text = text };

    private BlockId Id() => _ids.NextId();
}
