using Microsoft.UI.Xaml;
using Quill.Editor.Commands;
using Quill.Editor.Documents;
using Quill.Editor.Markdown;
using Quill.Editor.State;
using Quill.Editor.WinUI;
using Quill.Editor.WinUI.Input;
using Quill.Playground.Samples;
using Quill.Settings;
using Quill.Settings.Explorer;
using Quill.Settings.Insertion;
using Quill.Shell.Insertion;
using Quill.Shell.Settings;

namespace Quill.Playground.SelfTest;

/// <summary>
/// M7.3, layer 2: files and images in the editor with the app's handling (14-settings.md), over a temporary
/// folder and without a real drag or the real registry: a drop at a point inserts there by the image rule
/// (one undo item), dropped folders and Markdown files go to the window, pasted image files and image data
/// are inserted, Insert local images asks the host, and the settings page adds and removes File Explorer's
/// New menu entry in a registry in memory.
/// </summary>
internal static class InsertionProbes
{
    private static readonly byte[] Png = [0x89, 0x50, 0x4E, 0x47, 1, 2, 3];

    public static async Task RunAsync(SelfTestContext context)
    {
        context.Stage.Children.Clear();
        string root = Directory.CreateTempSubdirectory("quill-insert-").FullName;
        try
        {
            var box = new MarkdownBox
            {
                Width = 560,
                Height = 500,
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Top,
                Clipboard = new MemoryClipboard(),
            };
            context.Stage.Children.Add(box);
            await Task.Delay(50);

            string document = Path.Combine(root, "docs", "plan.md");
            Directory.CreateDirectory(Path.GetDirectoryName(document)!);
            string picture = Path.Combine(root, "pictures", "shot one.png");
            Directory.CreateDirectory(Path.GetDirectoryName(picture)!);
            File.WriteAllBytes(picture, Png);

            var settings = AppSettings.Default with { ImageRule = ImageInsertRule.CopyToAssets };
            string? openedFolder = null;
            var openedFiles = new List<string>();
            var insertions = new EditorInsertions(() => settings, () => document)
            {
                OpenFolder = folder => openedFolder = folder,
                OpenFiles = files =>
                {
                    openedFiles.AddRange(files);
                    return Task.CompletedTask;
                },
                PickImages = () => Task.FromResult<IReadOnlyList<string>>([picture]),
                ImagesFolder = Path.Combine(root, "app-images"),
                Download = null,
                Now = () => new DateTimeOffset(2026, 11, 3, 12, 0, 0, TimeSpan.Zero),
            };
            box.Insertions = insertions;

            await CheckDropAsync(context, box, picture, root);
            await CheckDropOpensAsync(context, box, root, () => openedFolder, openedFiles);
            await CheckPasteAsync(context, box, picture);
            await CheckPickAsync(context, box);
            await CheckNewMenuAsync(context);
        }
        finally
        {
            context.Stage.Children.Clear();
            Directory.Delete(root, recursive: true);
        }
    }

    private static async Task CheckDropAsync(SelfTestContext context, MarkdownBox box, string picture, string root)
    {
        Load(box, "first paragraph\n\nsecond paragraph");
        await Task.Delay(80);
        var second = box.State.Document.Children[1].Id;
        var target = box.Layout.GetCaretRect(new TextPosition(second, 6));
        var point = box.Document!.Page.TransformToVisual(box).TransformPoint(new Windows.Foundation.Point(target.X + 1, target.Y + target.Height / 2));
        await box.DropAsync(new DroppedItems([picture], []), point);
        string text = ((Paragraph)box.State.Document.Children[1]).Text;
        bool copied = File.Exists(Path.Combine(root, "docs", "assets", "shot one.png"));
        context.Check(text == "second![shot one](<./assets/shot one.png>) paragraph" && copied,
            "insert: a dropped image is copied by the rule and inserted at the drop point", text);
        box.Execute(new Undo());
        context.Check(((Paragraph)box.State.Document.Children[1]).Text == "second paragraph", "insert: one undo takes the dropped image out");
    }

    private static async Task CheckDropOpensAsync(SelfTestContext context, MarkdownBox box, string root, Func<string?> openedFolder, List<string> openedFiles)
    {
        string folder = Directory.CreateDirectory(Path.Combine(root, "notes")).FullName;
        string markdown = Path.Combine(root, "notes", "other.md");
        File.WriteAllText(markdown, "# other");
        string before = Text(box);
        await box.DropAsync(new DroppedItems([folder, markdown], []), null);
        context.Check(openedFolder() == folder && openedFiles.SequenceEqual([markdown]) && Text(box) == before,
            "insert: a dropped folder and Markdown file go to the window and insert nothing", $"{openedFolder()} | {string.Join(", ", openedFiles)}");
    }

    private static async Task CheckPasteAsync(SelfTestContext context, MarkdownBox box, string picture)
    {
        var clipboard = (MemoryClipboard)box.Clipboard;
        Load(box, "a ");
        box.Execute(new SetSelection(TextSelection.Caret(new TextPosition(box.State.Document.Children[0].Id, 2))));
        clipboard.Content = null;
        clipboard.Items = new DroppedItems([picture], []);
        await box.RunClipboardAsync(Quill.Editor.Input.ClipboardAction.Paste);
        string file = ((Paragraph)box.State.Document.Children[0]).Text;
        context.Check(file == "a ![shot one](<./assets/shot one.png>)", "insert: a pasted image file is inserted by the rule", file);

        clipboard.Items = new DroppedItems([], [], Png);
        await box.RunClipboardAsync(Quill.Editor.Input.ClipboardAction.Paste);
        string data = ((Paragraph)box.State.Document.Children[0]).Text;
        context.Check(data.EndsWith("![image-20261103120000000](./assets/image-20261103120000000.png)", StringComparison.Ordinal),
            "insert: pasted image data is saved under a time stamp and inserted", data);

        clipboard.Items = null;
        clipboard.Content = new ClipboardContent(null, "text");
        await box.RunClipboardAsync(Quill.Editor.Input.ClipboardAction.Paste);
        context.Check(((Paragraph)box.State.Document.Children[0]).Text.EndsWith("text", StringComparison.Ordinal), "insert: text on the clipboard pastes as before");
    }

    private static async Task CheckPickAsync(SelfTestContext context, MarkdownBox box)
    {
        Load(box, "x");
        box.Execute(new SetSelection(TextSelection.Caret(new TextPosition(box.State.Document.Children[0].Id, 1))));
        await box.RunDialogAsync(Quill.Editor.Input.EditorDialog.InsertLocalImage);
        string text = ((Paragraph)box.State.Document.Children[0]).Text;
        context.Check(text == "x![shot one](<./assets/shot one.png>)", "insert: Insert local images inserts the picked images by the rule", text);
    }

    private static async Task CheckNewMenuAsync(SelfTestContext context)
    {
        var registry = new MemoryRegistry();
        registry.SetValue(@"HKEY_CLASSES_ROOT\.md", null, "VSCode.md");
        var store = new SettingsStore(Path.Combine(Path.GetTempPath(), $"quill-settings-{Guid.NewGuid():N}.json"));
        var page = new SettingsPage(store, [new ThemeChoice("github", "Github")]) { Width = 900, Height = 600, NewMenu = new ExplorerNewMenu(registry) };
        context.Stage.Children.Clear();
        context.Stage.Children.Add(page);
        await Task.Delay(80);
        bool before = page is { NewMenuStatus.Text: "Not added", AddButton.IsEnabled: true, RemoveButton.IsEnabled: false };
        Invoke(page.AddButton!);
        bool added = page is { NewMenuStatus.Text: "Added", AddButton.IsEnabled: false, RemoveButton.IsEnabled: true }
            && registry.KeyExists(@"HKEY_CURRENT_USER\Software\Classes\.md\ShellNew");
        Invoke(page.RemoveButton!);
        bool removed = page is { NewMenuStatus.Text: "Not added" } && !registry.KeyExists(@"HKEY_CURRENT_USER\Software\Classes\.md");
        context.Check(before && added && removed, "insert: the settings page adds and removes the New menu entry", page.NewMenuStatus?.Text);
        context.Stage.Children.Clear();
    }

    /// <summary>Clicks a button as automation does (no system input, 08).</summary>
    private static void Invoke(Microsoft.UI.Xaml.Controls.Button button) =>
        (new Microsoft.UI.Xaml.Automation.Peers.ButtonAutomationPeer(button).GetPattern(Microsoft.UI.Xaml.Automation.Peers.PatternInterface.Invoke)
            as Microsoft.UI.Xaml.Automation.Provider.IInvokeProvider)?.Invoke();

    private static string Text(MarkdownBox box) => MarkdownSerializer.Serialize(box.State.Document);

    private static void Load(MarkdownBox box, string markdown)
    {
        var ids = new SequentialBlockIdSource();
        var document = MarkdownImporter.Import(markdown, ids).Document;
        box.Load(new EditorState { Document = document, Selection = TextSelection.Caret(EditorDocuments.FirstCaret(document)) }, ids);
    }
}
