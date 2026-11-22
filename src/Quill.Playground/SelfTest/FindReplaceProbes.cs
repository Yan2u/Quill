using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Quill.Editor.Documents;
using Quill.Editor.Input;
using Quill.Editor.Markdown;
using Quill.Editor.State;
using Quill.Editor.WinUI;
using Quill.Editor.WinUI.Elements;
using Quill.Editor.WinUI.Input;
using Quill.Editor.WinUI.Search;
using Quill.Playground.Samples;

namespace Quill.Playground.SelfTest;

/// <summary>
/// M6.2, layer 2: find and replace (12-find-replace.md) through the panel and the editor's keys: the
/// count, the matches drawn by the blocks, jumping into a link and into display math (both activate),
/// block syntax not found, replace all as one undo item, the source mode, and closing.
/// </summary>
internal static class FindReplaceProbes
{
    private const string Sample = "Intro with a [link text](https://example.com/target) and **bold**.\n\n$$\nx = target\n$$\n\n# Heading target\n\n- item target";

    public static async Task RunAsync(SelfTestContext context)
    {
        context.Stage.Children.Clear();
        var host = new Grid { Width = 720, Height = 520, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top };
        var box = new MarkdownBox { Clipboard = new MemoryClipboard() };
        var panel = new FindPanel(box);
        host.Children.Add(box);
        host.Children.Add(panel);
        context.Stage.Children.Add(host);
        await Task.Delay(50);

        var ids = new SequentialBlockIdSource();
        var document = MarkdownImporter.Import(Sample, ids).Document;
        box.Load(new EditorState { Document = document, Selection = TextSelection.Caret(document.Children[0].Id, 0) }, ids);
        await Task.Delay(50);

        // Ctrl+F opens the panel; typing the text counts, draws and selects the first match (in the URL).
        box.HandleKey(0x46, KeyModifiers.Ctrl);
        panel.FindBox.Text = "target";
        await Task.Delay(50);
        var paragraph = box.State.Document.Children[0];
        var map = box.CurrentProjection!.Map[paragraph.Id]!;
        var head = (TextPosition)box.State.Selection.Head;
        bool urlShown = map.ToDisplay(head.Offset, Affinity.Backward) - map.ToDisplay(head.Offset - 6, Affinity.Forward) == 6;
        context.Check(panel.IsOpen && box.Search.Count == 4 && panel.Status == "1 of 4" && head.Block == paragraph.Id && urlShown,
            "find: Ctrl+F opens the panel, the text counts 4 matches and selects the one in the link's URL, which shows (Q86)",
            $"open {panel.IsOpen}, status '{panel.Status}', head {head}, URL shown {urlShown}");

        var views = box.Document!.Geometry.Realized.Where(view => !box.Document.Geometry.IsDetached(view)).OfType<MdTextBlockView>().ToList();
        await Task.Delay(50);
        int drawn = views.Sum(view => view.DrawnMarks);
        context.Check(drawn >= 3, "find: the blocks draw the matches they hold", $"{drawn} drawn");

        // Enter in the panel goes to the next match: inside the display math, which now shows its source.
        box.Search.FindNext(forward: true);
        await Task.Delay(30);
        var math = box.State.Document.Children[1];
        var mathMap = box.CurrentProjection!.Map[math.Id]!;
        var mathHead = (TextPosition)box.State.Selection.Head;
        context.Check(mathHead.Block == math.Id && mathMap.ToDisplay(mathHead.Offset, Affinity.Backward) - mathMap.ToDisplay(mathHead.Offset - 6, Affinity.Forward) == 6 && panel.Status == "2 of 4",
            "find: the next match is in the display math, which shows its source (Q86)", $"head {mathHead}, status '{panel.Status}'");

        // Block syntax is not searchable: the heading's '#', the list marker.
        panel.FindBox.Text = "#";
        await Task.Delay(30);
        string noHeading = panel.Status;
        panel.FindBox.Text = "- ";
        await Task.Delay(30);
        context.Check(noHeading == "No results" && panel.Status == "No results", "find: the heading's '#' and the list marker are not found (Q86)", $"'#': {noHeading}, '- ': {panel.Status}");

        // Replace all: one undo item.
        box.HandleKey(0x48, KeyModifiers.Ctrl);
        panel.FindBox.Text = "target";
        panel.ReplaceBox.Text = "goal";
        await Task.Delay(30);
        box.Search.ReplaceAll(panel.ReplaceBox.Text);
        await Task.Delay(30);
        string replaced = MarkdownSerializer.Serialize(box.State.Document).TrimEnd('\n');
        bool countedNone = panel.Status == "No results";
        box.HandleKey(0x5A, KeyModifiers.Ctrl);
        await Task.Delay(30);
        context.Check(panel.IsReplaceShown && replaced == Sample.Replace("target", "goal", StringComparison.Ordinal) && countedNone
            && MarkdownSerializer.Serialize(box.State.Document).TrimEnd('\n') == Sample,
            "replace: Ctrl+H shows the replace row; replace all changes every match and undoes in one step", replaced.Replace("\n", "|"));

        // In the source mode the block syntax is searchable too.
        box.HandleKey(0xBF, KeyModifiers.Ctrl);
        await Task.Delay(50);
        panel.FindBox.Text = "#";
        await Task.Delay(30);
        context.Check(box.IsSourceMode && panel.Status is "1 of 1" or "1 result", "find: in the source mode the heading's '#' is found (Q86)", panel.Status);
        box.HandleKey(0xBF, KeyModifiers.Ctrl);
        await Task.Delay(50);

        // Esc closes the panel; nothing is drawn any more.
        panel.Close();
        await Task.Delay(50);
        int after = box.Document!.Geometry.Realized.Where(view => !box.Document.Geometry.IsDetached(view)).OfType<MdTextBlockView>().Sum(view => view.DrawnMarks);
        context.Check(!panel.IsOpen && box.Search.Query is null && after == 0, "find: closing the panel removes the matches", $"open {panel.IsOpen}, drawn {after}");
        context.Stage.Children.Clear();
    }
}
