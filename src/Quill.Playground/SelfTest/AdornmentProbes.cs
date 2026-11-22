using Microsoft.Graphics.Canvas;
using Microsoft.UI;
using Microsoft.UI.Xaml;
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
/// M5.5, layer 2: attached nodes and asynchronous content in the <see cref="MarkdownBox"/>, driven
/// through its Handle* entry points: TOC entries and clicks on them (Q68), the heading badge in the
/// margin without moving the heading (10), task checkboxes, footnote backlinks (Q72), the hover
/// flyout (Q29), the code language box, and a local image decoded in the background (05).
/// </summary>
internal static class AdornmentProbes
{
    public static async Task RunAsync(SelfTestContext context)
    {
        context.Stage.Children.Clear();
        var box = new MarkdownBox
        {
            Width = 560,
            Height = 420,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            Clipboard = new MemoryClipboard(),
        };
        context.Stage.Children.Add(box);
        await Task.Delay(50);

        await CheckTocAsync(context, box);
        await CheckBadgeAsync(context, box);
        await CheckClicksAsync(context, box);
        await CheckHoverAsync(context, box);
        await CheckLanguageAsync(context, box);
        await CheckTableToolbarAsync(context, box);
        await CheckImageAsync(context, box);
        context.Stage.Children.Clear();
    }

    private static async Task CheckTocAsync(SelfTestContext context, MarkdownBox box)
    {
        Load(box, "[toc]\n\n# One\n\ntext\n\n## Two\n\n### Three");
        await Task.Delay(50);
        var toc = Find<MdToc>(box);
        context.Check(toc is { Entries.Entries.Length: 3 }, "toc: the TOC shows every heading (Q68)", toc?.Entries.ToString());

        // Click the second entry: the caret goes to that heading.
        var bounds = box.Document!.Geometry.Bounds(toc!);
        double line = (bounds.Height - 16) / 3;
        var point = ToBox(box, new Point(bounds.X + 40, bounds.Y + 8 + line * 1.5));
        box.HandlePointerPressed(point, KeyModifiers.None);
        box.HandlePointerReleased(point, KeyModifiers.None);
        var heading = box.State.Document.Children[3];
        context.Check(box.State.Selection.Head is TextPosition { Offset: 0 } head && head.Block == heading.Id, "toc: a click on an entry goes to its heading", box.State.Selection.ToString());

        // Typing in a heading updates the entry.
        box.HandleKey(0x23, KeyModifiers.None);
        box.HandleCharacter('!');
        await Task.Delay(30);
        context.Check(Find<MdToc>(box)?.Entries.Entries[1].Text == "Two!", "toc: the entry follows the heading's text", Find<MdToc>(box)?.Entries.ToString());
    }

    private static async Task CheckBadgeAsync(SelfTestContext context, MarkdownBox box)
    {
        Load(box, "para\n\n## Heading two");
        await Task.Delay(50);
        var heading = Find<MdHeading>(box)!;
        var before = box.Document!.Geometry.Bounds(heading);
        bool hiddenUnfocused = box.Document.Adorners.Badge is null;
        box.Execute(new SetSelection(TextSelection.Caret(new TextPosition(box.State.Document.Children[1].Id, 2))));
        await Task.Delay(30);
        var badge = box.Document.Adorners.Badge;
        var after = box.Document.Geometry.Bounds(Find<MdHeading>(box)!);
        context.Check(hiddenUnfocused && badge is { Text: "H2" } shown && shown.Bounds.Right <= after.X && Math.Abs(after.X - before.X) < 0.5 && Math.Abs(after.Height - before.Height) < 0.5,
            "badge: the focused heading shows H2 in the margin and its text does not move (10)", $"badge {badge?.Bounds}, heading {before} -> {after}");
    }

    private static async Task CheckClicksAsync(SelfTestContext context, MarkdownBox box)
    {
        Load(box, "- [ ] task\n- [x] done");
        await Task.Delay(50);
        var item = Find<MdListItem>(box)!;
        var check = item.CheckboxBounds!.Value;
        var origin = box.Document!.Geometry.Bounds(item);
        var point = ToBox(box, new Point(origin.X + check.X + check.Width / 2, origin.Y + check.Y + check.Height / 2));
        box.HandlePointerPressed(point, KeyModifiers.None);
        box.HandlePointerReleased(point, KeyModifiers.None);
        await Task.Delay(30);
        context.Check(Text(box) == "- [x] task\n- [x] done", "click: the task checkbox toggles the item (ToggleTaskItem)", Text(box).Replace("\n", "|"));

        Load(box, "a[^1] and b[^1].\n\n[^1]: The note.");
        await Task.Delay(50);
        var slot = FindAll<MdDefinitionSlot>(box).Last();
        var slotBounds = box.Document!.Geometry.Bounds(slot);
        Point? backlink = null;
        for (double x = 0; x < slotBounds.Width && backlink is null; x += 1)
        {
            for (double y = 0; y < slotBounds.Height && backlink is null; y += 2)
            {
                if (slot.BacklinkAt(new Point(x, y)) == 1)
                {
                    backlink = new Point(slotBounds.X + x + 1, slotBounds.Y + y);
                }
            }
        }

        if (backlink is { } link)
        {
            var at = ToBox(box, link);
            box.HandlePointerPressed(at, KeyModifiers.None);
            box.HandlePointerReleased(at, KeyModifiers.None);
        }

        var paragraph = box.State.Document.Children[0];
        int second = ((Paragraph)paragraph).Text.LastIndexOf("[^1]", StringComparison.Ordinal);
        context.Check(backlink is not null && box.State.Selection.Head is TextPosition { Offset: var offset } head && head.Block == paragraph.Id && offset == second,
            "click: the second backlink goes to the second reference (Q72)", box.State.Selection.ToString());
    }

    private static async Task CheckHoverAsync(SelfTestContext context, MarkdownBox box)
    {
        Load(box, "end\n\na[^1] b\n\n[^1]: The note text.");
        await Task.Delay(50);
        var paragraph = box.State.Document.Children[1];
        var head = box.State.Selection.Head;
        var caret = box.Layout.GetCaretRect(new TextPosition(paragraph.Id, 2));
        var over = ToBox(box, new Point(caret.X + 2, caret.Y + caret.Height / 2));
        box.HandlePointerMoved(over, KeyModifiers.None);
        await Task.Delay(30);
        var flyout = box.Document!.Adorners.Flyout;
        var shown = flyout is { Text: "The note text." } && box.State.Selection.Head == head;
        var away = ToBox(box, new Point(caret.X + 200, caret.Y - 60));
        box.HandlePointerMoved(away, KeyModifiers.None);
        await Task.Delay(30);
        context.Check(shown && box.Document.Adorners.Flyout is null, "hover: a footnote reference shows its note in a flyout, the caret stays, and it hides when the pointer leaves (Q29)",
            $"{flyout?.Text} at {flyout?.Bounds}; after leaving {box.Document.Adorners.Flyout?.Text ?? "none"}; hover target {box.State.Interaction.HoverTarget ?? "none"}");
    }

    /// <summary>
    /// Q91: the language of a code block is document text in a box outside the code, at its bottom right,
    /// shown while the block is active; Down from the last line of code moves the one caret into it, and
    /// typing there changes the info string.
    /// </summary>
    private static async Task CheckLanguageAsync(SelfTestContext context, MarkdownBox box)
    {
        Load(box, "text\n\n```js\nlet a = 1;\n```\n\nafter");
        await Task.Delay(50);
        var fence = (CodeFence)box.State.Document.Children[1];
        var language = Blocks(box).OfType<MdCodeLanguage>().FirstOrDefault();
        bool hidden = language is { IsShown: false };
        double afterTop = AfterTop(box);

        box.Execute(new SetSelection(TextSelection.Caret(new TextPosition(fence.Code.Id, 2))));
        box.Execute(new MoveCaret(MoveDirection.Down, TextUnit.VisualLine));
        await Task.Delay(50);
        var code = Blocks(box).OfType<MdCodeBlock>().FirstOrDefault();
        language = Blocks(box).OfType<MdCodeLanguage>().FirstOrDefault();
        bool inLanguage = box.State.Selection.Head is TextPosition { Offset: 2 } head && head.Block == fence.Language.Id;
        bool outside = false;
        if (code is not null && language is not null)
        {
            var codeBounds = box.Document!.Geometry.Bounds(code);
            var languageBounds = box.Document.Geometry.Bounds(language);
            outside = languageBounds.Top >= codeBounds.Bottom && Math.Abs(languageBounds.Right - codeBounds.Right) < 1;
        }

        context.Check(hidden && inLanguage && language is { IsShown: true } && outside,
            "language: Down from the last line of code moves the caret to the end of the language, shown below the code at its right (Q91)",
            $"hidden before {hidden}, caret {box.State.Selection.Head}, shown {language?.IsShown}, outside {outside}");

        // Only block controls come to the front: the body raised over the caret layer hid the caret.
        var raised = new List<string>();
        for (Microsoft.UI.Xaml.DependencyObject? element = language; element is not null and not MdDocument; element = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetParent(element))
        {
            if (element is UIElement ui and not MdBlockView && Microsoft.UI.Xaml.Controls.Canvas.GetZIndex(ui) != 0)
            {
                raised.Add(ui.GetType().Name);
            }
        }

        context.Check(raised.Count == 0, "language: an active code block raises only block controls, the caret layer stays on top", string.Join(", ", raised));
        context.Check(Math.Abs(AfterTop(box) - afterTop) < 0.5, "language: showing the language box moves nothing below it (an overlay, Q91)", $"{afterTop:F1} -> {AfterTop(box):F1}");
        box.Execute(new InsertText("on"));
        await Task.Delay(30);
        context.Check(Text(box).Contains("```json", StringComparison.Ordinal), "language: typing in the language changes the info string", Text(box).Replace("\n", "|"));
        await EditorRenderProbes.WaitForDrawsAsync(box);
        await EditorRenderProbes.SaveAsync(box, Path.Combine(RepositoryPaths.Temp("m6-3"), "code-language.png"));
    }

    /// <summary>Q76–Q78: the toolbar over the active table, its buttons (invoked through automation, no system input) and actions.</summary>
    private static async Task CheckTableToolbarAsync(SelfTestContext context, MarkdownBox box)
    {
        Load(box, "text\n\n| a | b |\n| --- | --- |\n| c | d |");
        await Task.Delay(50);
        bool hidden = box.Document!.Adorners.Toolbar is null;
        var cell = ((TableRow)((Table)box.State.Document.Children[1]).Children[1]).Children[0];
        box.Execute(new SetSelection(TextSelection.Caret(new TextPosition(cell.Id, 1))));
        await Task.Delay(50);
        var toolbar = box.Document.Adorners.Toolbar;
        var table = Find<MdTable>(box)!;
        var tableBounds = box.Document.Geometry.Bounds(table);
        context.Check(hidden && toolbar is { } bar && Math.Abs(bar.Bounds.Bottom - tableBounds.Y) < 0.5 && bar.Bounds.Width >= table.GridWidth,
            "toolbar: shown over the active table only, ending at its top edge, at least as wide as the grid (Q76)", $"bar {toolbar?.Bounds}, table {tableBounds}");

        // The align-center button, clicked through its automation peer.
        var center = FindButton(box.Document.Adorners.TableToolbar, "Align center");
        (Microsoft.UI.Xaml.Automation.Peers.FrameworkElementAutomationPeer.CreatePeerForElement(center!) as Microsoft.UI.Xaml.Automation.Provider.IInvokeProvider)?.Invoke();
        await Task.Delay(50);
        context.Check(Text(box).Contains("| :---: | --- |", StringComparison.Ordinal), "toolbar: align center sets the caret's column (Q76)", Text(box).Replace("\n", "|"));

        await box.RunTableActionAsync(new ResizeTable(3, 2));
        context.Check(Text(box) == "text\n\n| a | b |  |\n| :---: | --- | --- |\n| c | d |  |", "toolbar: resize to 3 × 2 (Q78)", Text(box).Replace("\n", "|"));

        // A gap between squares belongs to the square before it, so moving across the grid keeps the preview.
        var grid = box.Document.Adorners.TableToolbar.ResizeGrid;
        grid.Show(3, 2);
        grid.PreviewAt(new Point(4 * 17 + 4, 3 * 17 + 4));
        var square = grid.Shown;
        grid.PreviewAt(new Point(5 * 17 - 1.5, 4 * 17 - 1.5));
        context.Check(square == (5, 4) && grid.Shown == (5, 4), "toolbar: the resize preview stays the same over the gaps between squares (no flicker)", $"{square} -> {grid.Shown}");
        await box.RunTableActionAsync(null);
        context.Check(((MemoryClipboard)box.Clipboard).Content?.Markdown?.StartsWith("| a | b |", StringComparison.Ordinal) == true, "toolbar: copy table puts its Markdown on the clipboard (Q77)");
        var delete = FindButton(box.Document.Adorners.TableToolbar, "Delete table");
        (Microsoft.UI.Xaml.Automation.Peers.FrameworkElementAutomationPeer.CreatePeerForElement(delete!) as Microsoft.UI.Xaml.Automation.Provider.IInvokeProvider)?.Invoke();
        await Task.Delay(50);
        context.Check(Text(box) == "text" && box.Document.Adorners.Toolbar is null, "toolbar: delete table removes it without asking and the toolbar goes (Q76)", Text(box).Replace("\n", "|"));
    }

    private static Microsoft.UI.Xaml.Controls.Button? FindButton(DependencyObject root, string name)
    {
        int count = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChildrenCount(root);
        for (int i = 0; i < count; i++)
        {
            var child = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChild(root, i);
            if (child is Microsoft.UI.Xaml.Controls.Button button && Microsoft.UI.Xaml.Automation.AutomationProperties.GetName(button) == name)
            {
                return button;
            }

            if (FindButton(child, name) is { } nested)
            {
                return nested;
            }
        }

        return null;
    }

    private static async Task CheckImageAsync(SelfTestContext context, MarkdownBox box)
    {
        string folder = RepositoryPaths.Temp("m5-5");
        string file = Path.Combine(folder, "picture.png");
        using (var target = new CanvasRenderTarget(CanvasDevice.GetSharedDevice(), 240, 120, 96))
        {
            using (var session = target.CreateDrawingSession())
            {
                session.Clear(Colors.SteelBlue);
                session.FillEllipse(120, 60, 50, 30, Colors.Orange);
            }

            await target.SaveAsync(file);
        }

        box.DocumentDirectory = folder;
        Load(box, "before\n\n![a picture](picture.png)\n\n![missing](none.png)\n\nafter");
        await Task.Delay(50);
        var paragraph = FindAll<MdParagraph>(box)[1];
        double placeholder = paragraph.ActualHeight;
        for (int i = 0; i < 50 && paragraph.ActualHeight < 100; i++)
        {
            await Task.Delay(40);
            box.Root.UpdateLayout();
        }

        var missing = FindAll<MdParagraph>(box)[2];
        // A small local image may decode before the first check; the placeholder height is reported, not required.
        context.Check(paragraph.ActualHeight >= 120 && missing.ActualHeight < 60 && box.Renders?.Count == 2,
            "image: a local image decodes in the background and replaces its placeholder; a missing one keeps it (05)",
            $"placeholder {placeholder:F0} -> {paragraph.ActualHeight:F0} DIP, missing {missing.ActualHeight:F0}, cache {box.Renders?.Count}");
        box.DocumentDirectory = null;
    }

    private static void Load(MarkdownBox box, string markdown)
    {
        var ids = new SequentialBlockIdSource();
        var document = MarkdownImporter.Import(markdown, ids).Document;
        box.Load(new EditorState { Document = document, Selection = TextSelection.Caret(EditorDocuments.FirstCaret(document)) }, ids);
    }

    private static Point ToBox(MarkdownBox box, Point page) => box.Document!.Page.TransformToVisual(box).TransformPoint(page);

    private static T? Find<T>(MarkdownBox box)
        where T : MdBlockView => FindAll<T>(box).FirstOrDefault();

    private static List<T> FindAll<T>(MarkdownBox box)
        where T : MdBlockView =>
        box.Document!.Geometry.Realized.Where(view => !box.Document.Geometry.IsDetached(view)).OfType<T>().ToList();

    /// <summary>The top of the last top-level paragraph, in page coordinates.</summary>
    private static double AfterTop(MarkdownBox box) =>
        Blocks(box).OfType<MdParagraph>().LastOrDefault() is { } paragraph ? box.Document!.Geometry.Bounds(paragraph).Top : double.NaN;

    private static List<MdBlockView> Blocks(MarkdownBox box) =>
        box.Document!.Geometry.Realized.Where(view => !box.Document.Geometry.IsDetached(view)).ToList();

    private static string Text(MarkdownBox box) => MarkdownSerializer.Serialize(box.State.Document).TrimEnd('\n');
}
