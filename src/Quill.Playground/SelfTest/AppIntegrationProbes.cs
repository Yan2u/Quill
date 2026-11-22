using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Controls;
using Quill.Editor.Commands;
using Quill.Editor.Documents;
using Quill.Editor.Input;
using Quill.Editor.Markdown;
using Quill.Editor.State;
using Quill.Editor.WinUI;
using Quill.Editor.WinUI.Elements;
using Quill.Editor.WinUI.Input;
using Quill.Playground.Samples;
using Windows.Foundation;

namespace Quill.Playground.SelfTest;

/// <summary>
/// M5.6, layer 2: the context menu (Q73) opened through the box's entry point and its entries invoked
/// through automation (no system input), and the save path (06: the paragraph at the caret is committed
/// first and the view follows; the modified state for the title bar).
/// </summary>
internal static class AppIntegrationProbes
{
    public static async Task RunAsync(SelfTestContext context)
    {
        context.Stage.Children.Clear();
        var clipboard = new MemoryClipboard();
        var box = new MarkdownBox
        {
            Width = 560,
            Height = 420,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            Clipboard = clipboard,
        };
        context.Stage.Children.Add(box);
        await Task.Delay(50);

        await CheckContextMenuAsync(context, box, clipboard);
        await CheckSaveAsync(context, box);
        await CheckScopesAsync(context, box);
        await CheckInsertTableAsync(context, box);
        await CheckMenuBarAsync(context, box);
        await CheckSelectionMaskAsync(context, box);
        context.Stage.Children.Clear();
    }

    private static async Task CheckContextMenuAsync(SelfTestContext context, MarkdownBox box, MemoryClipboard clipboard)
    {
        Load(box, "alpha beta gamma");
        await Task.Delay(50);
        var block = box.State.Document.Children[0].Id;

        // A right click outside the (collapsed) selection moves the caret; nothing to cut or copy, nothing to paste.
        var menu = box.HandleContextRequested(PointAt(box, block, 8))!;
        await Task.Delay(50);
        context.Check(box.State.Selection.Head is TextPosition { Offset: 8 } && menu.IsOpen,
            "context menu: a right click moves the caret to the click and opens the menu (Q73)", $"{box.State.Selection}, open {menu.IsOpen}");
        var overflowing = menu.PrimaryCommands.OfType<AppBarButton>().Where(button => button.IsInOverflow).Select(button => button.Label).ToList();
        context.Check(Enabled(menu) == "Bold Italic Code Hyperlink Paragraph Insert Select all" && menu.PrimaryCommands.Count == 7,
            "context menu: the icon row (cut, copy, paste; bold, italic, code, link) and the submenus; only the entries the state allows are enabled (M6.0)", Enabled(menu));
        context.Check(overflowing.Count == 0, "context menu: every icon of the row fits in it, none overflows into the list (user report)", overflowing.Count == 0 ? null : string.Join(", ", overflowing));
        menu.Hide();

        // Inside a selection the selection stays, and copy acts on it.
        box.Execute(new SetSelection(new TextSelection(new TextPosition(block, 6), new TextPosition(block, 10))));
        menu = box.HandleContextRequested(PointAt(box, block, 8))!;
        await Task.Delay(50);
        bool kept = box.State.Selection.Anchor is TextPosition { Offset: 6 } && box.State.Selection.Head is TextPosition { Offset: 10 };
        Invoke(menu, "Copy");

        // The menu closes with an animation: wait for it rather than a fixed delay.
        for (int i = 0; i < 25 && menu.IsOpen; i++)
        {
            await Task.Delay(20);
        }

        context.Check(kept && clipboard.Content?.PlainText == "beta" && !menu.IsOpen,
            "context menu: a right click inside the selection keeps it; Copy copies it and closes the menu", $"{box.State.Selection}, clipboard {clipboard.Content?.PlainText}");

        // With text on the clipboard, paste is enabled; the menu key opens the menu at the caret.
        menu = box.HandleContextRequested(null)!;
        await Task.Delay(50);
        context.Check(Enabled(menu) == "Cut Copy Paste Bold Italic Code Hyperlink Delete Copy / paste as Paragraph Insert Select all",
            "context menu: the menu key opens it at the caret, the selection's entries and paste enabled", Enabled(menu));

        // Paste as plain text sits in the "Copy / paste as" submenu.
        var submenu = menu.SecondaryCommands.OfType<AppBarButton>().First(button => button.Label == "Copy / paste as").Flyout as MenuFlyout;
        var paste = submenu?.Items.OfType<MenuFlyoutItem>().FirstOrDefault(item => item.Text == "Paste as plain text");
        menu.Hide();
        await box.RunMenuItemAsync(Quill.Editor.Input.EditorMenus.Flatten(Quill.Editor.Input.EditorMenus.Context(box.MenuContext()).Secondary).First(entry => entry.Id == "paste-plain"));
        await Task.Delay(50);
        context.Check(paste is { IsEnabled: true, KeyboardAcceleratorTextOverride: "Ctrl+Shift+V" } && Text(box) == "alpha beta gamma" && box.State.Selection.IsCollapsed,
            "context menu: Paste as plain text (in its submenu, with its shortcut) replaces the selection with the clipboard text", Text(box));

        // Bold from the icon row.
        box.Execute(new SetSelection(new TextSelection(new TextPosition(block, 0), new TextPosition(block, 5))));
        menu = box.HandleContextRequested(null)!;
        await Task.Delay(50);
        Invoke(menu, "Bold");
        await Task.Delay(50);
        context.Check(Text(box) == "**alpha** beta gamma", "context menu: Bold in the icon row formats the selection (M6.0)", Text(box));

        menu = box.HandleContextRequested(null)!;
        await Task.Delay(50);
        Invoke(menu, "Select all");
        await Task.Delay(50);
        context.Check(box.State.Selection is { Anchor: TextPosition { Offset: 0 }, Head: TextPosition { Offset: 20 } },
            "context menu: Select all selects the document", box.State.Selection.ToString());
    }

    private static async Task CheckSaveAsync(SelfTestContext context, MarkdownBox box)
    {
        Load(box, "start");
        await Task.Delay(50);
        bool clean = !box.IsModified;
        box.Execute(new SetSelection(TextSelection.Caret(new TextPosition(box.State.Document.Children[0].Id, 5))));
        box.HandleKey(0x0D, KeyModifiers.None);
        foreach (char c in "Title")
        {
            box.HandleCharacter(c);
        }

        box.HandleKey(0x0D, KeyModifiers.Shift);
        foreach (char c in "===")
        {
            box.HandleCharacter(c);
        }

        bool modified = box.IsModified;
        bool paragraph = box.State.Document.Children[1] is Paragraph;
        var snapshot = box.PrepareSave();
        await Task.Delay(30);
        bool heading = snapshot.Document.Children[1] is Heading && Find<MdHeading>(box) is not null;
        box.MarkSaved(snapshot);
        context.Check(clean && modified && paragraph && heading && !box.IsModified,
            "save: the paragraph at the caret is committed before saving and the view shows the heading; the save clears the modified state (06)",
            $"clean {clean}, modified {modified}, paragraph before {paragraph}, heading after {heading}, modified after {box.IsModified}");

        string folder = RepositoryPaths.Temp("m5-6");
        string path = Path.Combine(folder, "save.md");
        await MarkdownFile.SaveAsync(snapshot.Document, path);
        context.Check(await File.ReadAllTextAsync(path) == "start\n\nTitle\n===", "save: the snapshot is written in the background as the committed Markdown",
            (await File.ReadAllTextAsync(path)).Replace("\n", "|"));

        box.HandleCharacter('!');
        bool again = box.IsModified;
        box.Execute(new Undo());
        context.Check(again && !box.IsModified, "save: typing after a save modifies the document, undoing it returns to the saved state", $"after typing {again}, after undo {box.IsModified}");
    }

    /// <summary>Q39: Ctrl+L selects the visual line of a wrapped paragraph; Ctrl+L and Ctrl+A in a table select the row and the table.</summary>
    private static async Task CheckScopesAsync(SelfTestContext context, MarkdownBox box)
    {
        string words = string.Join(" ", Enumerable.Repeat("wrapping words in a long paragraph", 12));
        Load(box, words + "\n\n| a | b |\n| - | - |\n| c | d |");
        await Task.Delay(50);
        var paragraph = box.State.Document.Children[0].Id;
        box.Execute(new SetSelection(TextSelection.Caret(new TextPosition(paragraph, 5))));
        box.HandleKey(0x4C, KeyModifiers.Ctrl);
        var line = box.Layout.GetVisualLine(new TextPosition(paragraph, 5));
        bool visual = box.State.Selection is { Anchor: TextPosition { Offset: 0 }, Head: TextPosition { Offset: var end } } && end > 0 && end < words.Length
            && line.End is TextPosition lineEnd && lineEnd.Offset == end;
        context.Check(visual, "scope: Ctrl+L outside a table selects the visual line of a wrapped paragraph (Q39)", box.State.Selection.ToString());

        var table = (Table)box.State.Document.Children[1];
        var cell = ((TableRow)table.Children[1]).Children[1];
        box.Execute(new SetSelection(TextSelection.Caret(new TextPosition(cell.Id, 1))));
        box.HandleKey(0x4C, KeyModifiers.Ctrl);
        bool row = box.State.Selection.Anchor is TextPosition { Offset: 0 } rowStart && rowStart.Block == ((TableRow)table.Children[1]).Children[0].Id;
        box.HandleKey(0x41, KeyModifiers.Ctrl);
        bool whole = box.State.Selection.Anchor is TextPosition tableStart && tableStart.Block == ((TableRow)table.Children[0]).Children[0].Id
            && box.State.Selection.Head is TextPosition tableEnd && tableEnd.Block == cell.Id;
        await Task.Delay(30);
        context.Check(row && whole, "scope: Ctrl+L selects the table row, Ctrl+A the whole table (Q39)", box.State.Selection.ToString());
    }

    /// <summary>Ctrl+T: the dialog's answer (supplied by a handler, no dialog opens) becomes a table at the caret.</summary>
    private static async Task CheckInsertTableAsync(SelfTestContext context, MarkdownBox box)
    {
        Load(box, "before after");
        await Task.Delay(50);
        box.Execute(new SetSelection(TextSelection.Caret(new TextPosition(box.State.Document.Children[0].Id, 6))));
        EditorDialog? asked = null;
        box.DialogHandler = dialog =>
        {
            asked = dialog;
            return Task.FromResult<Command?>(new InsertTable(3, 4));
        };
        box.HandleKey(0x54, KeyModifiers.Ctrl);
        await Task.Delay(50);
        bool inserted = box.State.Document.Children is [Paragraph, Table { ColumnCount: 3, Children.Count: 4 }, Paragraph] && Find<MdTable>(box) is not null;
        context.Check(asked == EditorDialog.InsertTable && inserted, "insert table: Ctrl+T asks for the size and inserts the table between the paragraph's halves", Text(box).Replace("\n", "|"));

        box.DialogHandler = _ => Task.FromResult<Command?>(null);
        var before = box.State.Document;
        box.HandleKey(0x54, KeyModifiers.Ctrl);
        await Task.Delay(30);
        context.Check(ReferenceEquals(before, box.State.Document), "insert table: cancelling the dialog changes nothing");
        box.DialogHandler = null;

        var dialog = new Quill.Editor.WinUI.Input.InsertTableDialog();
        bool defaults = dialog.Size == (3, 4);
        dialog.SetSize(0, 2000);
        context.Check(defaults && dialog.Size == (1, 999), "insert table: the dialog offers 3 × 4 and keeps the size within 1–64 columns and 1–999 rows", dialog.Size?.ToString());
    }

    /// <summary>The window's Edit, Paragraph and Format menus keep their items across refreshes (user report: items were repeated).</summary>
    private static async Task CheckMenuBarAsync(SelfTestContext context, MarkdownBox box)
    {
        Load(box, "para\n\n## heading");
        await Task.Delay(30);
        var bar = new MenuBar();
        bar.Items.Add(new MenuBarItem { Title = "File" });
        context.Stage.Children.Add(bar);
        var menus = new EditorMenuBar(bar, box);
        menus.Refresh();
        var first = menus.Items.Select(item => item.Items.Count).ToList();
        var instances = menus.Items.Select(item => item.Items[0]).ToList();
        var heading = box.State.Document.Children[1].Id;
        for (int i = 0; i < 5; i++)
        {
            box.Execute(new SetSelection(TextSelection.Caret(new TextPosition(i % 2 == 0 ? heading : box.State.Document.Children[0].Id, 1))));
            menus.Refresh();
        }

        box.Execute(new SetSelection(TextSelection.Caret(new TextPosition(heading, 1))));
        menus.Refresh();
        var paragraph = menus.Items[1];
        bool checkedHeading = paragraph.Items.OfType<ToggleMenuFlyoutItem>().Any(item => item.Text == "Heading 2" && item.IsChecked)
            && paragraph.Items.OfType<ToggleMenuFlyoutItem>().Any(item => item.Text == "Paragraph" && !item.IsChecked);
        bool same = menus.Items.Select(item => item.Items.Count).SequenceEqual(first) && menus.Items.Select(item => item.Items[0]).SequenceEqual(instances);
        context.Check(bar.Items.Select(item => item.Title).SequenceEqual(["File", "Edit", "Paragraph", "Format", "View"]) && same && checkedHeading,
            "menu bar: Edit, Paragraph, Format and View follow File; refreshing updates the same items in place, no item is repeated, the current block is checked (M6.0)",
            $"{string.Join("/", bar.Items.Select(item => item.Title))}; counts {string.Join(",", first)} -> {string.Join(",", menus.Items.Select(item => item.Items.Count))}; heading checked {checkedHeading}");
        context.Stage.Children.Remove(bar);
    }

    /// <summary>Q84: a selection shows on the text of text blocks (Typora); only blocks without text show "selected whole".</summary>
    private static async Task CheckSelectionMaskAsync(SelfTestContext context, MarkdownBox box)
    {
        Load(box, "first\n\n---\n\n> quoted\n\nlast");
        await Task.Delay(30);
        box.HandleKey(0x41, KeyModifiers.Ctrl);
        await Task.Delay(30);
        var views = box.Document!.Geometry.Realized.Where(view => !box.Document.Geometry.IsDetached(view)).ToList();
        var masked = views.Where(view => view.DrawsWholeSelection).Select(view => view.GetType().Name).ToList();
        context.Check(masked.SequenceEqual(["MdThematicBreak"]), "selection: covered text blocks and containers show only their text highlighted, the rule shows selected whole (Q84)", string.Join(", ", masked));
    }

    private static void Invoke(CommandBarFlyout menu, string name)
    {
        var button = menu.PrimaryCommands.Concat(menu.SecondaryCommands).OfType<AppBarButton>()
            .First(item => Microsoft.UI.Xaml.Automation.AutomationProperties.GetName(item) == name);
        if (FrameworkElementAutomationPeer.CreatePeerForElement(button) is IInvokeProvider invoke)
        {
            invoke.Invoke();
        }
    }

    private static string Enabled(CommandBarFlyout menu) =>
        string.Join(" ", menu.PrimaryCommands.Concat(menu.SecondaryCommands).OfType<AppBarButton>().Where(item => item.IsEnabled).Select(item => item.Label));

    private static Point PointAt(MarkdownBox box, BlockId block, int offset)
    {
        var caret = box.Layout.GetCaretRect(new TextPosition(block, offset));
        return box.Document!.Page.TransformToVisual(box).TransformPoint(new Point(caret.X + 0.5, caret.Y + caret.Height / 2));
    }

    private static void Load(MarkdownBox box, string markdown)
    {
        var ids = new SequentialBlockIdSource();
        var document = MarkdownImporter.Import(markdown, ids).Document;
        box.Load(new EditorState { Document = document, Selection = TextSelection.Caret(EditorDocuments.FirstCaret(document)) }, ids);
    }

    private static T? Find<T>(MarkdownBox box)
        where T : MdBlockView =>
        box.Document!.Geometry.Realized.Where(view => !box.Document.Geometry.IsDetached(view)).OfType<T>().FirstOrDefault();

    private static string Text(MarkdownBox box) => MarkdownSerializer.Serialize(box.State.Document).TrimEnd('\n');
}
