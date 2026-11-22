using System.Text;
using Microsoft.UI.Xaml;
using Quill.Editor.Commands;
using Quill.Editor.Documents;
using Quill.Editor.Markdown;
using Quill.Editor.State;
using Quill.Editor.WinUI;
using Quill.Editor.WinUI.Input;
using Quill.Playground.Samples;

namespace Quill.Playground.SelfTest;

/// <summary>
/// M6.4, layer 2: a file changed on disk reloads into the editor as one undo item that counts as saved
/// (from the source mode too); a deleted file marks the document modified.
/// </summary>
internal static class FileProbes
{
    public static async Task RunAsync(SelfTestContext context)
    {
        context.Stage.Children.Clear();
        var box = new MarkdownBox
        {
            Width = 480,
            Height = 300,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            Clipboard = new MemoryClipboard(),
        };
        context.Stage.Children.Add(box);
        await Task.Delay(50);

        var ids = new SequentialBlockIdSource();
        var document = MarkdownImporter.Import("first\n\nsecond", ids).Document;
        box.Load(new EditorState { Document = document, Selection = TextSelection.Caret(EditorDocuments.FirstCaret(document)) }, ids);
        box.Execute(new InsertText("edited "));
        box.ToggleSourceMode();
        await Task.Delay(30);

        box.Reload(Encoding.UTF8.GetBytes("# changed on disk\n"));
        await Task.Delay(30);
        string Text() => MarkdownSerializer.Serialize(box.State.Document).TrimEnd('\n');
        context.Check(!box.IsSourceMode && Text() == "# changed on disk" && !box.IsModified, "files: a reload replaces the document and counts as saved (out of the source mode)", $"{Text()} modified {box.IsModified}");

        box.Execute(new Undo());
        await Task.Delay(30);
        context.Check(Text() == "edited first\n\nsecond" && box.IsModified, "files: Ctrl+Z goes back to the text before the reload (Typora)", Text().Replace("\n", "|"));

        box.Execute(new Redo());
        box.MarkUnsaved();
        context.Check(box.IsModified, "files: a deleted file marks the document modified");
        context.Stage.Children.Clear();
    }
}
