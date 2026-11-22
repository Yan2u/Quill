using System.Collections.Immutable;
using Quill.Editor.Commands;
using Quill.Editor.Documents;
using Quill.Editor.Editing;
using Quill.Editor.State;

namespace Quill.Editor.Input;

/// <summary>
/// One entry of a menu: what it runs (a key binding: command, clipboard action, dialog or view action),
/// its shortcut as found in the key map, and whether it is enabled or checked now. An entry with
/// children is a submenu; <see cref="IsSeparator"/> marks a separator line. <see cref="Id"/> is stable:
/// the view chooses icons by it.
/// </summary>
public sealed record MenuEntry
{
    public static MenuEntry Separator { get; } = new() { Id = "-", Label = string.Empty, IsSeparator = true };

    public required string Id { get; init; }

    public required string Label { get; init; }

    public KeyBinding? Binding { get; init; }

    public string? Shortcut { get; init; }

    public bool IsEnabled { get; init; } = true;

    public bool IsChecked { get; init; }

    /// <summary>Whether the entry can carry a check mark (shown as a toggle item even while unchecked).</summary>
    public bool IsCheckable { get; init; }

    public bool IsSeparator { get; init; }

    public ImmutableArray<MenuEntry> Children { get; init; } = [];
}

/// <summary>The state menus are built from.</summary>
/// <param name="IsSourceMode">In the source mode (11-source-mode.md) the Paragraph, Format and Insert entries are disabled.</param>
/// <param name="CopiesLine">Copy and cut work without a selection (the setting, Q110).</param>
public sealed record MenuContext(EditorState State, UndoHistory History, bool CanPaste, KeyMap KeyMap, bool IsSourceMode = false, bool CopiesLine = false);

/// <summary>
/// The editor's menus as data (M6.0, after Typora's menu bar and context menu, shown in the Windows 11
/// style by the view): Edit, Paragraph and Format, and the context menu. Only commands Quill has are
/// listed; the entries run the same bindings as their shortcuts.
/// </summary>
public static class EditorMenus
{
    public static ImmutableArray<MenuEntry> Edit(MenuContext context)
    {
        var builder = new Builder(context);
        bool selection = builder.HasSelection;
        bool copy = builder.CanCopy;
        return
        [
            builder.Command("undo", "Undo", new Undo(), context.History.CanUndo),
            builder.Command("redo", "Redo", new Redo(), context.History.CanRedo),
            MenuEntry.Separator,
            builder.Clipboard("cut", "Cut", ClipboardAction.Cut, copy),
            builder.Clipboard("copy", "Copy", ClipboardAction.Copy, copy),
            builder.Clipboard("paste", "Paste", ClipboardAction.Paste, context.CanPaste),
            MenuEntry.Separator,
            builder.Clipboard("copy-markdown", "Copy as Markdown", ClipboardAction.CopyAsMarkdown, copy),
            builder.Clipboard("copy-plain", "Copy as plain text", ClipboardAction.CopyAsPlainText, copy),
            builder.Clipboard("paste-plain", "Paste as plain text", ClipboardAction.PasteAsPlainText, context.CanPaste),
            MenuEntry.Separator,
            Submenu("select", "Select",
            [
                builder.Command("select-all", "Select all", new SelectAll()),
                builder.Command("select-scope", builder.InTable ? "Select cell" : "Select style scope", new SelectScope(), builder.InText),
                builder.Command("select-line", builder.InTable ? "Select row" : "Select line", new SelectLine(), builder.InText),
                builder.Command("select-word", "Select word", new SelectWordAtCaret(), builder.InText),
            ]),
            builder.Command("delete", "Delete", new DeleteBackward(), selection),
            builder.Command("delete-word", "Delete word", new DeleteWord(), builder.InText),
            MenuEntry.Separator,
            builder.Entry("reveal", "Jump to selection", new ViewBinding(ViewAction.RevealSelection)),
            MenuEntry.Separator,
            Submenu("find-replace", "Find and replace",
            [
                builder.Entry("find", "Find", new ViewBinding(ViewAction.Find)),
                builder.Entry("find-next", "Find next", new ViewBinding(ViewAction.FindNext)),
                builder.Entry("find-previous", "Find previous", new ViewBinding(ViewAction.FindPrevious)),
                builder.Entry("replace", "Replace", new ViewBinding(ViewAction.Replace)),
            ]),
        ];
    }

    public static ImmutableArray<MenuEntry> Paragraph(MenuContext context)
    {
        var builder = new Builder(context);
        var leaf = builder.Leaf;
        int level = leaf switch
        {
            Heading heading => heading.Level,
            Documents.Paragraph => 0,
            _ => -1,
        };
        bool text = leaf is Documents.Paragraph or Heading;
        var entries = ImmutableArray.CreateBuilder<MenuEntry>();
        for (int i = 1; i <= 6; i++)
        {
            entries.Add(builder.Command($"heading-{i}", $"Heading {i}", new SetHeadingLevel(i), text) with { IsCheckable = true, IsChecked = level == i });
        }

        entries.Add(MenuEntry.Separator);
        entries.Add(builder.Command("paragraph", "Paragraph", new SetHeadingLevel(0), text) with { IsCheckable = true, IsChecked = level == 0 });
        entries.Add(MenuEntry.Separator);
        entries.Add(builder.Command("heading-up", "Increase heading level", new ChangeHeadingLevel(1), text));
        entries.Add(builder.Command("heading-down", "Decrease heading level", new ChangeHeadingLevel(-1), level > 0));
        entries.Add(MenuEntry.Separator);
        entries.Add(Submenu("table", "Table", Table(builder)));
        entries.Add(builder.Command("math-block", "Math block", new ToggleBlock(BlockFormat.MathBlock), builder.InText || leaf is MathBlock) with { IsCheckable = true, IsChecked = leaf is MathBlock });
        entries.Add(builder.Command("code-block", "Code block", new ToggleBlock(BlockFormat.CodeBlock), builder.InText || leaf is CodeBlock) with { IsCheckable = true, IsChecked = leaf is CodeBlock });
        entries.Add(MenuEntry.Separator);
        entries.AddRange(Blocks(builder));
        entries.Add(Submenu("list-indent", "List indentation",
        [
            builder.Command("indent", "Indent", new Indent(InsertsTab: false), builder.InList),
            builder.Command("outdent", "Outdent", new Outdent(), builder.InList),
        ]));
        entries.Add(MenuEntry.Separator);
        entries.Add(builder.Command("paragraph-above", "Insert paragraph above", new InsertBlock(InsertedBlock.ParagraphAbove), builder.InAny));
        entries.Add(builder.Command("paragraph-below", "Insert paragraph below", new InsertBlock(InsertedBlock.ParagraphBelow), builder.InAny));
        entries.Add(MenuEntry.Separator);
        entries.Add(builder.Command("rule", "Horizontal rule", new InsertBlock(InsertedBlock.HorizontalRule), builder.InText));
        entries.Add(builder.Command("toc", "Table of contents", new InsertBlock(InsertedBlock.TableOfContents), builder.InText));
        entries.Add(builder.Command("front-matter", "YAML front matter", new InsertBlock(InsertedBlock.FrontMatter), builder.Editing));
        return entries.ToImmutable();
    }

    /// <summary>The View menu: the source mode (Ctrl+/, Typora's "Source Code Mode").</summary>
    public static ImmutableArray<MenuEntry> View(MenuContext context)
    {
        var builder = new Builder(context);
        return [builder.Entry("source-mode", "Source code mode", new ViewBinding(ViewAction.ToggleSourceMode)) with { IsCheckable = true, IsChecked = context.IsSourceMode }];
    }

    public static ImmutableArray<MenuEntry> Format(MenuContext context)
    {
        var builder = new Builder(context);
        bool text = builder.InText;
        MenuEntry Toggle(string id, string label, InlineFormat format) => builder.Command(id, label, new ToggleInline(format), text);
        return
        [
            Toggle("bold", "Bold", InlineFormat.Strong),
            Toggle("italic", "Italic", InlineFormat.Emphasis),
            Toggle("underline", "Underline", InlineFormat.Underline),
            Toggle("code", "Code", InlineFormat.Code),
            MenuEntry.Separator,
            Toggle("inline-math", "Inline math", InlineFormat.Math),
            Toggle("strikethrough", "Strikethrough", InlineFormat.Strikethrough),
            Toggle("superscript", "Superscript", InlineFormat.Superscript),
            Toggle("subscript", "Subscript", InlineFormat.Subscript),
            Toggle("comment", "Comment", InlineFormat.Comment),
            MenuEntry.Separator,
            builder.Entry("link", "Hyperlink", new DialogBinding(EditorDialog.InsertLink), text),
            Submenu("image", "Image",
            [
                builder.Entry("insert-image", "Insert image", new DialogBinding(EditorDialog.InsertImage), text),
                builder.Entry("insert-local-image", "Insert local images...", new DialogBinding(EditorDialog.InsertLocalImage), text),
            ]),
            MenuEntry.Separator,
            builder.Command("clear-format", "Clear format", new ClearFormat(), text),
        ];
    }

    /// <summary>
    /// The context menu (Q73, Q80; layout after Typora's): <c>Primary</c> is the icon row (cut, copy,
    /// paste; bold, italic, code, link: seven, what one row of the Windows 11 menu holds without
    /// overflowing), <c>Secondary</c> the list below it, starting with delete.
    /// </summary>
    public static (ImmutableArray<MenuEntry> Primary, ImmutableArray<MenuEntry> Secondary) Context(MenuContext context)
    {
        var builder = new Builder(context);
        bool selection = builder.HasSelection;
        bool text = builder.InText;
        ImmutableArray<MenuEntry> primary =
        [
            builder.Clipboard("cut", "Cut", ClipboardAction.Cut, selection),
            builder.Clipboard("copy", "Copy", ClipboardAction.Copy, selection),
            builder.Clipboard("paste", "Paste", ClipboardAction.Paste, context.CanPaste),
            builder.Command("bold", "Bold", new ToggleInline(InlineFormat.Strong), text),
            builder.Command("italic", "Italic", new ToggleInline(InlineFormat.Emphasis), text),
            builder.Command("code", "Code", new ToggleInline(InlineFormat.Code), text),
            builder.Entry("link", "Hyperlink", new DialogBinding(EditorDialog.InsertLink), text),
        ];
        var paragraph = Paragraph(context);
        ImmutableArray<MenuEntry> secondary =
        [
            builder.Command("delete", "Delete", new DeleteBackward(), selection),
            Submenu("copy-paste-as", "Copy / paste as",
            [
                builder.Clipboard("copy-markdown", "Copy as Markdown", ClipboardAction.CopyAsMarkdown, selection),
                MenuEntry.Separator,
                builder.Clipboard("paste-plain", "Paste as plain text", ClipboardAction.PasteAsPlainText, context.CanPaste),
            ]),
            MenuEntry.Separator,
            Submenu("paragraph-menu", "Paragraph", [.. paragraph.Take(8), .. Blocks(builder)]),
            Submenu("insert", "Insert",
            [
                builder.Entry("insert-image", "Image", new DialogBinding(EditorDialog.InsertImage), text),
                MenuEntry.Separator,
                builder.Command("rule", "Horizontal rule", new InsertBlock(InsertedBlock.HorizontalRule), text),
                builder.Entry("insert-table", "Table", new DialogBinding(EditorDialog.InsertTable), text),
                builder.Command("code-block", "Code block", new ToggleBlock(BlockFormat.CodeBlock), text),
                builder.Command("math-block", "Math block", new ToggleBlock(BlockFormat.MathBlock), text),
                builder.Command("toc", "Table of contents", new InsertBlock(InsertedBlock.TableOfContents), text),
                builder.Command("front-matter", "YAML front matter", new InsertBlock(InsertedBlock.FrontMatter), builder.Editing),
                MenuEntry.Separator,
                builder.Command("paragraph-above", "Paragraph above", new InsertBlock(InsertedBlock.ParagraphAbove), builder.InAny),
                builder.Command("paragraph-below", "Paragraph below", new InsertBlock(InsertedBlock.ParagraphBelow), builder.InAny),
            ]),
            MenuEntry.Separator,
            builder.Command("undo", "Undo", new Undo(), context.History.CanUndo),
            builder.Command("redo", "Redo", new Redo(), context.History.CanRedo),
            builder.Command("select-all", "Select all", new SelectAll()),
        ];
        return (primary, secondary);
    }

    /// <summary>The shortest chord bound to <paramref name="binding"/>, as shown next to a menu entry.</summary>
    public static string? ShortcutOf(KeyMap keyMap, KeyBinding binding) =>
        keyMap.Bindings.Where(pair => pair.Value == binding)
            .Select(pair => pair.Key.ToString())
            .OrderBy(text => text.Length)
            .ThenBy(text => text, StringComparer.Ordinal)
            .FirstOrDefault();

    /// <summary>All entries of a menu, submenus included, separators left out.</summary>
    public static IEnumerable<MenuEntry> Flatten(IEnumerable<MenuEntry> entries) =>
        entries.Where(static entry => !entry.IsSeparator).SelectMany(static entry => entry.Children.IsEmpty ? [entry] : Flatten(entry.Children));

    private static MenuEntry Submenu(string id, string label, ImmutableArray<MenuEntry> children) => new()
    {
        Id = id,
        Label = label,
        Children = children,
        IsEnabled = children.Any(static child => child.IsEnabled && !child.IsSeparator),
    };

    /// <summary>Quote and the three list kinds, checked when the caret is in one.</summary>
    private static ImmutableArray<MenuEntry> Blocks(Builder builder)
    {
        var (quote, list, item) = builder.Containers;
        bool ordered = list?.IsOrdered == true;
        bool task = list is { IsOrdered: false } && item?.Task is not (null or TaskState.None);
        bool bullet = list is { IsOrdered: false } && !task;
        return
        [
            builder.Command("quote", "Quote", new ToggleBlock(BlockFormat.Quote), builder.InAny) with { IsCheckable = true, IsChecked = quote },
            MenuEntry.Separator,
            builder.Command("ordered-list", "Ordered list", new ToggleList(ListKind.Ordered), builder.InAny) with { IsCheckable = true, IsChecked = ordered },
            builder.Command("bullet-list", "Unordered list", new ToggleList(ListKind.Bullet), builder.InAny) with { IsCheckable = true, IsChecked = bullet },
            builder.Command("task-list", "Task list", new ToggleList(ListKind.Task), builder.InAny) with { IsCheckable = true, IsChecked = task },
        ];
    }

    private static ImmutableArray<MenuEntry> Table(Builder builder)
    {
        bool cell = builder.InTable;
        MenuEntry Edit(string id, string label, TableEdit edit) => builder.Command(id, label, new EditTable(edit), cell);
        return
        [
            builder.Entry("insert-table", "Insert table", new DialogBinding(EditorDialog.InsertTable), builder.InText),
            MenuEntry.Separator,
            Edit("row-above", "Insert row above", TableEdit.InsertRowAbove),
            Edit("row-below", "Insert row below", TableEdit.InsertRowBelow),
            Edit("column-left", "Insert column left", TableEdit.InsertColumnLeft),
            Edit("column-right", "Insert column right", TableEdit.InsertColumnRight),
            MenuEntry.Separator,
            Edit("row-up", "Move row up", TableEdit.MoveRowUp),
            Edit("row-down", "Move row down", TableEdit.MoveRowDown),
            Edit("column-left-move", "Move column left", TableEdit.MoveColumnLeft),
            Edit("column-right-move", "Move column right", TableEdit.MoveColumnRight),
            MenuEntry.Separator,
            Edit("delete-row", "Delete row", TableEdit.DeleteRow),
            Edit("delete-column", "Delete column", TableEdit.DeleteColumn),
            MenuEntry.Separator,
            Edit("format-table", "Format table source", TableEdit.FormatSource),
            Edit("delete-table", "Delete table", TableEdit.DeleteTable),
        ];
    }

    /// <summary>Entries built against one state, with the shortcuts of one key map.</summary>
    private sealed class Builder(MenuContext context)
    {
        private readonly Document _document = context.State.Document;

        public LeafBlock? Leaf { get; } = !context.IsSourceMode && context.State.Selection.Head is TextPosition head && DocumentTree.TryFind(context.State.Document, head.Block) is LeafBlock leaf ? leaf : null;

        /// <summary>The editing mode: entries that work without a caret (front matter) are on.</summary>
        public bool Editing => !context.IsSourceMode;

        public bool HasSelection => !context.State.Selection.IsCollapsed || context.State.Selection.IsBlockCaret;

        /// <summary>Whether copy and cut have something to take: a selection, or the line at the caret (Q110).</summary>
        public bool CanCopy => HasSelection || (context.CopiesLine && context.State.Selection.Head is TextPosition);

        /// <summary>The caret is in a block that takes inline formats.</summary>
        public bool InText => Leaf is TextLeafBlock { InlineSyntax: not InlineSyntaxSet.PlainText };

        public bool InTable => Leaf is TableCell;

        public bool InAny => Leaf is not null;

        public bool InList => Containers.List is not null;

        public (bool Quote, ListBlock? List, ListItem? Item) Containers
        {
            get
            {
                if (Leaf is null)
                {
                    return (false, null, null);
                }

                var containers = DocumentTree.Locate(_document, Leaf.Id).ContainersInnermostFirst().ToList();
                var item = containers.OfType<ListItem>().FirstOrDefault();
                var list = item is null ? null : DocumentTree.Locate(_document, item.Id).Parent as ListBlock;
                return (containers.OfType<BlockQuote>().Any(), list, item);
            }
        }

        public MenuEntry Command(string id, string label, Command command, bool enabled = true) => Entry(id, label, new CommandBinding(command), enabled);

        public MenuEntry Clipboard(string id, string label, ClipboardAction action, bool enabled) => Entry(id, label, new ClipboardBinding(action), enabled);

        public MenuEntry Entry(string id, string label, KeyBinding binding, bool enabled = true) => new()
        {
            Id = id,
            Label = label,
            Binding = binding,
            Shortcut = ShortcutOf(context.KeyMap, binding),
            IsEnabled = enabled,
        };
    }
}
