using Quill.Editor.Commands;
using Quill.Editor.Input;
using Quill.Editor.State;
using Quill.Editor.Tests.Support;

namespace Quill.Editor.Tests.Input;

/// <summary>
/// M6.0: the menus as data (Edit, Paragraph, Format and the context menu, after Typora's), with
/// shortcuts from the key map and states from the editor; the right click (Q73, Q80).
/// </summary>
public sealed class ContextMenuTests
{
    private static MenuContext Context(EditorTestHost host, bool canPaste = false) => new(host.State, host.Session.History, canPaste, KeyMap.Default);

    [Fact]
    public void TheContextMenuHasTheClipboardAndFormatIconsAndThreeSubmenus()
    {
        var host = EditorTestHost.Load("a<|>b");
        var (primary, secondary) = EditorMenus.Context(Context(host, canPaste: true));
        Assert.Equal("cut copy paste bold italic code link", string.Join(" ", primary.Select(entry => entry.Id)));
        Assert.Equal("delete copy-paste-as paragraph-menu insert undo redo select-all", string.Join(" ", secondary.Where(entry => !entry.IsSeparator).Select(entry => entry.Id)));
        Assert.Equal("Ctrl+B", primary.Single(entry => entry.Id == "bold").Shortcut);
        Assert.Equal("Ctrl+K", primary.Single(entry => entry.Id == "link").Shortcut);
    }

    [Fact]
    public void EntriesAreEnabledByTheStateTheyNeed()
    {
        var host = EditorTestHost.Load("a<|>b");
        string Enabled(bool canPaste) => string.Join(" ", EditorMenus.Context(Context(host, canPaste)).Primary.Where(entry => entry.IsEnabled).Select(entry => entry.Id));

        Assert.Equal("paste bold italic code link", Enabled(canPaste: true));
        host.Type("x").Press("Shift+Left");
        Assert.Equal("cut copy bold italic code link", Enabled(canPaste: false));
        var host2 = EditorTestHost.Load("```\nco<|>de\n```");
        Assert.DoesNotContain(EditorMenus.Context(Context(host2)).Primary, entry => entry.Id == "bold" && entry.IsEnabled);
    }

    [Fact]
    public void TheParagraphMenuChecksTheCurrentBlock()
    {
        string Checked(string state) => string.Join(" ", EditorMenus.Flatten(EditorMenus.Paragraph(Context(EditorTestHost.Load(state)))).Where(entry => entry.IsChecked).Select(entry => entry.Id));

        Assert.Equal("paragraph", Checked("a<|>"));
        Assert.Equal("heading-2", Checked("## a<|>"));
        Assert.Equal("paragraph quote ordered-list", Checked("> 1. a<|>"));
        Assert.Equal("paragraph task-list", Checked("- [ ] a<|>"));
        Assert.Equal("code-block", Checked("```\na<|>\n```"));
    }

    [Fact]
    public void EveryShortcutOfTheMenusIsBoundToItsEntry()
    {
        var host = EditorTestHost.Load("| a<|> |\n| - |");
        var entries = EditorMenus.Flatten([.. EditorMenus.Edit(Context(host)), .. EditorMenus.Paragraph(Context(host)), .. EditorMenus.Format(Context(host))]).ToList();
        foreach (var entry in entries.Where(entry => entry.Shortcut is not null))
        {
            Assert.Equal(entry.Binding, KeyMap.Default.Find(KeyChord.Parse(entry.Shortcut!)));
        }

        // Typora's shortcuts of these menus are all there.
        string shortcuts = string.Join(" ", entries.Select(entry => entry.Shortcut).Where(shortcut => shortcut is not null));
        foreach (string expected in new[] { "Ctrl+B", "Ctrl+I", "Ctrl+U", "Ctrl+Shift+`", "Alt+Shift+5", "Ctrl+K", "Ctrl+Shift+I", "Ctrl+\\", "Ctrl+=", "Ctrl+-", "Ctrl+T", "Ctrl+Shift+K", "Ctrl+Shift+M", "Ctrl+Shift+Q", "Ctrl+Shift+[", "Ctrl+Shift+]", "Ctrl+Shift+X", "Ctrl+[", "Ctrl+]", "Ctrl+D", "Ctrl+Shift+D", "Ctrl+J", "Ctrl+E", "Ctrl+L" })
        {
            Assert.Contains(expected, shortcuts.Split(' '));
        }
    }

    [Fact]
    public void EntriesRunTheirBindings()
    {
        var host = EditorTestHost.Load("ab<|>");
        host.Press("Shift+Left");
        var entry = EditorMenus.Flatten(EditorMenus.Format(Context(host))).Single(item => item.Id == "bold");
        host.Execute(((CommandBinding)entry.Binding!).Command);
        Assert.Equal("a**<^>b<$>**", host.Dump());
    }

    [Fact]
    public void RightClickInsideTheSelectionKeepsIt()
    {
        var host = EditorTestHost.Load("a<^>bcd<$>e");
        var block = host.State.Document.Children[0].Id;
        Assert.Null(ContextMenu.SelectionFor(new TextPosition(block, 2), host.State));
        Assert.Null(ContextMenu.SelectionFor(new TextPosition(block, 4), host.State));
    }

    [Fact]
    public void RightClickOutsideTheSelectionMovesTheCaret()
    {
        var host = EditorTestHost.Load("a<^>bc<$>de");
        var block = host.State.Document.Children[0].Id;
        var command = ContextMenu.SelectionFor(new TextPosition(block, 4), host.State);
        Assert.Equal(new SetSelection(TextSelection.Caret(new TextPosition(block, 4))), command);
        host.Execute(command!);
        Assert.Null(ContextMenu.SelectionFor(new TextPosition(block, 4), host.State));
    }
}
