using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using Quill.Editor.Commands;
using Quill.Editor.Documents;
using Quill.Editor.Input;
using Quill.Editor.Markdown;
using Quill.Editor.Projection;
using Quill.Editor.Services;
using Quill.Editor.State;
using Quill.Editor.WinUI;
using Quill.Editor.WinUI.Elements;
using Quill.Editor.WinUI.Input;
using Quill.Playground.Samples;
using Windows.Foundation;

namespace Quill.Playground.SelfTest;

/// <summary>
/// M5.3, layer 2: the <see cref="MarkdownBox"/> driven through its Handle* entry points, never by
/// simulating system input (08): keys and typing, clicks and drags with the threshold measured
/// from the press, the activation frozen while the pointer is down, double and triple click,
/// Ctrl+click navigation, the clipboard (in memory), focus, the caret scrolled into view and
/// scroll anchoring when a block above the viewport changes height.
/// </summary>
internal static class MarkdownBoxProbes
{
    public static async Task RunAsync(SelfTestContext context)
    {
        context.Stage.Children.Clear();
        var other = new Button { Content = "elsewhere", HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom };
        var box = new MarkdownBox
        {
            Width = 520,
            Height = Math.Min(480, context.Stage.XamlRoot.Size.Height - 40),
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            Clipboard = new MemoryClipboard(),
        };
        context.Stage.Children.Add(box);
        context.Stage.Children.Add(other);
        await Task.Delay(50);
        var ids = new SequentialBlockIdSource();
        var document = MarkdownImporter.Import(MarkdownShowcase.Text, ids).Document;
        box.Load(new EditorState { Document = document, Selection = TextSelection.Caret(EditorDocuments.FirstCaret(document)) }, ids);
        await Task.Delay(100);

        CheckKeys(context, box);
        CheckPointer(context, box);
        CheckNavigation(context, box);
        await CheckClipboardAsync(context, box);
        await CheckFocusAsync(context, box, other);
        CheckAnchoring(context, box);
        context.Stage.Children.Clear();
        await CheckCursorAsync(context);
        context.Stage.Children.Clear();
    }

    private static void CheckKeys(SelfTestContext context, MarkdownBox box)
    {
        box.HandleKey(0x23, KeyModifiers.Ctrl);
        var end = box.State.Selection.Head;
        var caret = box.Layout.GetCaretRect(end);
        var viewport = box.Layout.GetViewport();
        // The showcase ends with definitions (non-text blocks): the end is the virtual paragraph after them (02).
        context.Check(end is BoundaryPosition { Index: var index } && index == box.State.Document.Children.Count && caret.Y >= viewport.Y && caret.Bottom <= viewport.Bottom,
            "keys: Ctrl+End goes to the virtual paragraph at the end and the caret is scrolled into view (07 rule 9)", $"{end}, caret {caret.Y:F0}, viewport {viewport.Y:F0}..{viewport.Bottom:F0}");

        string before = Text(box);
        bool typed = box.HandleCharacter('x') & box.HandleCharacter('y');
        string after = Text(box);
        box.HandleKey(0x08, KeyModifiers.None);
        string erased = Text(box);
        box.HandleKey(0x5A, KeyModifiers.Ctrl);
        box.HandleKey(0x5A, KeyModifiers.Ctrl);
        context.Check(typed && after.TrimEnd().EndsWith("\n\nxy", StringComparison.Ordinal) && before.Length < after.Length,
            "keys: typed characters are inserted (CharacterReceived, Q66); in the virtual paragraph they start a paragraph", Tail(after));
        context.Check(erased.Length == after.Length - 1 && Text(box) == before, "keys: Backspace erases, Ctrl+Z undoes", Tail(erased));

        bool tab = box.HandleKey(0x09, KeyModifiers.None);
        box.HandleKey(0x5A, KeyModifiers.Ctrl);
        box.HandleKey(0x24, KeyModifiers.Ctrl);
        box.HandleKey(0x28, KeyModifiers.Shift);
        context.Check(tab && !box.State.Selection.IsCollapsed && box.Document!.CaretBounds is null,
            "keys: Tab is taken by the editor; Shift+Down selects and the caret overlay hides", box.State.Selection.ToString());
        context.Check(!box.HandleKey(0x10, KeyModifiers.Shift), "keys: a modifier alone does nothing");
    }

    private static void CheckPointer(SelfTestContext context, MarkdownBox box)
    {
        var paragraph = FirstParagraphWith(box, "**bold**");
        int boldContent = paragraph.Text.IndexOf("**bold**", StringComparison.Ordinal) + 4;
        box.Execute(new SetSelection(TextSelection.Caret(new TextPosition(paragraph.Id, paragraph.Text.Length))));
        bool markersBefore = HasMarkers(box, paragraph.Id);

        // Press inside the hidden bold text: the activation stays frozen until release (03).
        var press = ToBox(box, new TextPosition(paragraph.Id, boldContent));
        box.HandlePointerPressed(press, KeyModifiers.None);
        bool frozen = !HasMarkers(box, paragraph.Id) && box.State.Selection.Head is TextPosition { Offset: var pressed } && pressed == boldContent;
        box.HandlePointerMoved(new Point(press.X + 3, press.Y), KeyModifiers.None);
        bool belowThreshold = box.State.Selection.IsCollapsed;
        box.HandlePointerReleased(new Point(press.X + 3, press.Y), KeyModifiers.None);
        bool expanded = HasMarkers(box, paragraph.Id);
        context.Check(!markersBefore && frozen && expanded, "pointer: a click inside hidden bold text keeps the markers hidden while pressed and shows them after release (03)");
        context.Check(belowThreshold, "pointer: a move within the threshold from the press is not a drag (07)");

        var start = ToBox(box, new TextPosition(paragraph.Id, 2));
        box.HandlePointerPressed(start, KeyModifiers.None);
        box.HandlePointerMoved(new Point(start.X + 60, start.Y), KeyModifiers.None);
        box.HandlePointerReleased(new Point(start.X + 60, start.Y), KeyModifiers.None);
        context.Check(!box.State.Selection.IsCollapsed && box.State.Selection.Anchor is TextPosition { Offset: 2 }, "pointer: a drag past the threshold selects from the press", box.State.Selection.ToString());

        var word = ToBox(box, new TextPosition(paragraph.Id, 4));
        Click(box, word);
        Click(box, word);
        var selected = box.State.Selection;
        Click(box, word);
        var block = box.State.Selection;
        context.Check(selected is { Anchor: TextPosition { Offset: 2 }, Head: TextPosition { Offset: 11 } } && block is { Anchor: TextPosition { Offset: 0 }, Head: TextPosition blockEnd } && blockEnd.Offset == paragraph.Text.Length,
            "pointer: double-click selects the word, triple-click the block", $"{selected}; {block}");
    }

    private static void CheckNavigation(SelfTestContext context, MarkdownBox box)
    {
        var paragraph = FirstParagraphWith(box, "[^1]");
        int reference = paragraph.Text.IndexOf("[^1]", StringComparison.Ordinal) + 2;
        Click(box, ToBox(box, new TextPosition(paragraph.Id, reference)), KeyModifiers.Ctrl);
        var head = box.State.Selection.Head as TextPosition;
        var definition = box.State.Document.Children.OfType<FootnoteDefinition>().FirstOrDefault();
        context.Check(head is not null && definition is not null && head.Block == definition.Content.Id,
            "pointer: Ctrl+click on a footnote reference goes to its definition", head?.ToString());

        LinkTarget? activated = null;
        void OnLink(object? sender, LinkTarget link) => activated = link;
        box.LinkActivated += OnLink;
        paragraph = FirstParagraphWith(box, "[link]");
        int link = paragraph.Text.IndexOf("[link]", StringComparison.Ordinal) + 2;
        Click(box, ToBox(box, new TextPosition(paragraph.Id, link)), KeyModifiers.Ctrl);
        box.LinkActivated -= OnLink;
        context.Check(activated?.Destination == "https://example.com", "pointer: Ctrl+click on an external link is handed to the host", activated?.ToString());
    }

    private static async Task CheckClipboardAsync(SelfTestContext context, MarkdownBox box)
    {
        var clipboard = (MemoryClipboard)box.Clipboard;
        var paragraph = FirstParagraphWith(box, "**bold**");
        int start = paragraph.Text.IndexOf("**bold**", StringComparison.Ordinal);
        box.Execute(new SetSelection(new TextSelection(new TextPosition(paragraph.Id, start), new TextPosition(paragraph.Id, start + 8))));
        await box.RunClipboardAsync(ClipboardAction.Copy);
        var copied = clipboard.Content;
        context.Check(copied is { Markdown: "**bold**", PlainText: "**bold**" }, "clipboard: Ctrl+C puts Quill's Markdown, and by default the same as plain text (Q102)", copied?.ToString());

        // With "copy Markdown source" off the plain text is the text as shown.
        box.Options = Quill.Editor.EditorOptions.Default with { CopyMarkdownSource = false };
        await box.RunClipboardAsync(ClipboardAction.Copy);
        copied = clipboard.Content;
        context.Check(copied is { Markdown: "**bold**", PlainText: "bold" }, "clipboard: Ctrl+C puts plain text and Quill's Markdown (04)", copied?.ToString());

        box.HandleKey(0x23, KeyModifiers.Ctrl);
        string before = Text(box);
        box.HandleKey(0x56, KeyModifiers.Ctrl);
        await Task.Delay(20);
        string pasted = Text(box);
        box.HandleKey(0x5A, KeyModifiers.Ctrl);
        await box.RunClipboardAsync(ClipboardAction.PasteAsPlainText);
        string plain = Text(box);
        box.HandleKey(0x5A, KeyModifiers.Ctrl);
        context.Check(pasted.TrimEnd().EndsWith("\n\n**bold**", StringComparison.Ordinal) && plain.TrimEnd().EndsWith("\n\nbold", StringComparison.Ordinal) && Text(box) == before,
            "clipboard: Ctrl+V pastes the Markdown, Ctrl+Shift+V the plain text, each undone by Ctrl+Z", $"{Tail(pasted)} | {Tail(plain)}");
        box.Options = Quill.Editor.EditorOptions.Default;
    }

    private static async Task CheckFocusAsync(SelfTestContext context, MarkdownBox box, Control other)
    {
        box.Execute(new SetSelection(TextSelection.Caret(EditorDocuments.FirstCaret(box.State.Document))));
        box.Focus(FocusState.Programmatic);
        await Task.Delay(50);
        bool shown = box.State.Interaction.HasFocus && box.Document!.CaretBounds is not null;
        other.Focus(FocusState.Programmatic);
        await Task.Delay(50);
        bool hidden = !box.State.Interaction.HasFocus && box.Document!.CaretBounds is null;
        box.Focus(FocusState.Programmatic);
        await Task.Delay(50);
        context.Check(shown && hidden && box.Document!.CaretBounds is not null, "focus: the caret shows with focus and hides without it (SetInteraction)");
    }

    /// <summary>A block above the viewport grows: the first visible block stays where it is on screen (05).</summary>
    private static void CheckAnchoring(SelfTestContext context, MarkdownBox box)
    {
        var scroller = box.Document!.ScrollViewer;
        var target = box.State.Document.Children.OfType<Table>().First();
        box.Layout.ScrollIntoView(target.Id);
        box.Refresh();
        var viewport = box.Layout.GetViewport();
        var anchor = box.Layout.GetBlockBounds(target.Id);
        var first = box.State.Document.Children.OfType<Paragraph>().First();
        var before = (anchor?.Y ?? 0) - viewport.Y;

        // An edit far above, made without moving the view (as when asynchronous content arrives).
        box.Session.Execute(new SetSelection(TextSelection.Caret(new TextPosition(first.Id, 0))));
        for (int i = 0; i < 3; i++)
        {
            box.Session.Execute(new InsertSoftBreak());
        }

        box.Refresh();
        var after = (box.Layout.GetBlockBounds(target.Id)?.Y ?? 0) - box.Layout.GetViewport().Y;
        context.Check(anchor is not null && Math.Abs(after - before) < 1.5, "scroll: a block above the viewport grows and the visible content stays put (anchoring, 05)",
            $"table at {before:F1} -> {after:F1} in the viewport, offset {viewport.Y:F0} -> {scroller.VerticalOffset:F0}");
    }

    private static async Task CheckCursorAsync(SelfTestContext context)
    {
        var box = new MarkdownBox
        {
            Width = 1100,
            Height = 420,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            Clipboard = new MemoryClipboard(),
        };
        context.Stage.Children.Add(box);
        await Task.Delay(50);

        var ids = new SequentialBlockIdSource();
        string markdown = "# Heading\n\nParagraph text.\n\n- [ ] Task item\n";
        var doc = MarkdownImporter.Import(markdown, ids).Document;
        box.Load(new EditorState { Document = doc, Selection = TextSelection.Caret(EditorDocuments.FirstCaret(doc)) }, ids);
        box.UpdateLayout();
        await Task.Delay(80);
        box.UpdateLayout();

        var page = box.Document!.Page;
        Point pageOrigin = page.TransformToVisual(box).TransformPoint(default);
        double contentLeft = pageOrigin.X + page.Padding.Left;
        double contentRight = pageOrigin.X + page.ActualWidth - page.Padding.Right;

        // Wide viewport: default ContentMaxWidth=800, PagePaddingHorizontal=30 => page=860 centered at 120..980, content=150..950.
        bool wideBoundsOk = Math.Abs(page.ActualWidth - 860) < 2.0
            && Math.Abs(contentLeft - 150) < 2.0
            && Math.Abs(contentRight - 950) < 2.0;

        box.UpdateCursor(new Point(50, 100));
        bool leftOuter = !box.IsInContentArea(new Point(50, 100))
            && box.ActiveCursorShape == InputSystemCursorShape.Arrow
            && box.ProtectedCursorShape == InputSystemCursorShape.Arrow;

        box.UpdateCursor(new Point(135, 100));
        bool leftPadding = !box.IsInContentArea(new Point(135, 100))
            && box.ActiveCursorShape == InputSystemCursorShape.Arrow
            && box.ProtectedCursorShape == InputSystemCursorShape.Arrow;

        box.UpdateCursor(new Point(160, 100));
        bool contentStart = box.IsInContentArea(new Point(160, 100))
            && box.ActiveCursorShape == InputSystemCursorShape.IBeam
            && box.ProtectedCursorShape == InputSystemCursorShape.IBeam;

        box.UpdateCursor(new Point(550, 100));
        bool contentMid = box.IsInContentArea(new Point(550, 100))
            && box.ActiveCursorShape == InputSystemCursorShape.IBeam
            && box.ProtectedCursorShape == InputSystemCursorShape.IBeam;

        box.UpdateCursor(new Point(940, 100));
        bool contentEnd = box.IsInContentArea(new Point(940, 100))
            && box.ActiveCursorShape == InputSystemCursorShape.IBeam
            && box.ProtectedCursorShape == InputSystemCursorShape.IBeam;

        box.UpdateCursor(new Point(550, 360));
        bool contentBelowText = box.IsInContentArea(new Point(550, 360))
            && box.ActiveCursorShape == InputSystemCursorShape.IBeam
            && box.ProtectedCursorShape == InputSystemCursorShape.IBeam;

        box.UpdateCursor(new Point(965, 100));
        bool rightPadding = !box.IsInContentArea(new Point(965, 100))
            && box.ActiveCursorShape == InputSystemCursorShape.Arrow
            && box.ProtectedCursorShape == InputSystemCursorShape.Arrow;

        box.UpdateCursor(new Point(1050, 100));
        bool rightOuter = !box.IsInContentArea(new Point(1050, 100))
            && box.ActiveCursorShape == InputSystemCursorShape.Arrow
            && box.ProtectedCursorShape == InputSystemCursorShape.Arrow;

        context.Check(
            wideBoundsOk && leftOuter && leftPadding && contentStart && contentMid && contentEnd && contentBelowText && rightPadding && rightOuter,
            "cursor: wide editor shows IBeam inside content column and Arrow in outer margins",
            $"pageX={pageOrigin.X:F1}, pageW={page.ActualWidth:F1}, content=[{contentLeft:F1}..{contentRight:F1}]");

        // Interactive chrome controls inside the content column keep Arrow.
        var checkBox = Descendants(box).OfType<CheckBox>().FirstOrDefault();
        box.UpdateCursor(new Point(550, 100), checkBox);
        bool checkBoxArrow = box.ActiveCursorShape == InputSystemCursorShape.Arrow && box.ProtectedCursorShape == InputSystemCursorShape.Arrow;

        var scrollBar = new ScrollBar();
        box.UpdateCursor(new Point(550, 100), scrollBar);
        bool scrollBarArrow = box.ActiveCursorShape == InputSystemCursorShape.Arrow && box.ProtectedCursorShape == InputSystemCursorShape.Arrow;

        box.UpdateCursor(new Point(550, 100), box.Document.Adorners);
        bool adornerArrow = box.ActiveCursorShape == InputSystemCursorShape.Arrow && box.ProtectedCursorShape == InputSystemCursorShape.Arrow;

        context.Check(
            checkBox is not null && checkBoxArrow && scrollBarArrow && adornerArrow,
            "cursor: interactive controls inside content column keep Arrow",
            $"checkBox={(checkBox is not null)}, cb={checkBoxArrow}, sb={scrollBarArrow}, adorner={adornerArrow}");

        // Narrow viewport (Width = 520, content column ~30..490).
        box.Width = 520;
        box.UpdateLayout();
        await Task.Delay(60);
        box.UpdateLayout();
        box.UpdateCursor(new Point(10, 100));
        bool narrowLeft = box.ActiveCursorShape == InputSystemCursorShape.Arrow;
        box.UpdateCursor(new Point(260, 100));
        bool narrowCenter = box.ActiveCursorShape == InputSystemCursorShape.IBeam;
        box.UpdateCursor(new Point(505, 100));
        bool narrowRight = box.ActiveCursorShape == InputSystemCursorShape.Arrow;
        context.Check(
            narrowLeft && narrowCenter && narrowRight,
            "cursor: narrow viewport keeps Arrow in padding and IBeam inside content");

        // Source mode keeps the same column rule.
        box.ToggleSourceMode();
        await Task.Delay(60);
        box.UpdateLayout();
        box.UpdateCursor(new Point(10, 100));
        bool sourceLeft = box.ActiveCursorShape == InputSystemCursorShape.Arrow;
        box.UpdateCursor(new Point(260, 100));
        bool sourceCenter = box.ActiveCursorShape == InputSystemCursorShape.IBeam;
        box.UpdateCursor(new Point(505, 100));
        bool sourceRight = box.ActiveCursorShape == InputSystemCursorShape.Arrow;
        box.ToggleSourceMode();
        await Task.Delay(60);
        context.Check(
            sourceLeft && sourceCenter && sourceRight,
            "cursor: source mode shows IBeam inside content column and Arrow outside");

        // Read-only previews and ClearCursor reset ProtectedCursor cleanly.
        box.UpdateCursor(new Point(260, 100));
        bool beforeClear = box.ActiveCursorShape == InputSystemCursorShape.IBeam;
        box.ClearCursor();
        bool afterClear = box.ActiveCursorShape is null && box.ProtectedCursorShape is null;

        box.UpdateCursor(new Point(260, 100));
        box.IsReadOnly = true;
        bool clearedOnReadOnly = box.ActiveCursorShape is null && box.ProtectedCursorShape is null;
        box.UpdateCursor(new Point(260, 100));
        bool staysClearedInReadOnly = box.ActiveCursorShape is null && box.ProtectedCursorShape is null;

        context.Check(
            beforeClear && afterClear && clearedOnReadOnly && staysClearedInReadOnly,
            "cursor: ClearCursor and IsReadOnly clear editor cursor override cleanly");
    }

    private static IEnumerable<DependencyObject> Descendants(DependencyObject? root)
    {
        if (root is null)
        {
            yield break;
        }

        int count = VisualTreeHelper.GetChildrenCount(root);
        for (int i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            yield return child;
            foreach (var nested in Descendants(child))
            {
                yield return nested;
            }
        }
    }

    private static void Click(MarkdownBox box, Point point, KeyModifiers modifiers = KeyModifiers.None)
    {
        box.HandlePointerPressed(point, modifiers);
        box.HandlePointerReleased(point, modifiers);
    }

    /// <summary>The middle of the caret at a position, in the box's coordinates.</summary>
    private static Point ToBox(MarkdownBox box, TextPosition position)
    {
        box.Layout.ScrollIntoView(position.Block);
        var caret = box.Layout.GetCaretRect(position);
        var page = box.Document!.Page;
        return page.TransformToVisual(box).TransformPoint(new Point(caret.X + 0.5, caret.Y + caret.Height / 2));
    }

    private static Paragraph FirstParagraphWith(MarkdownBox box, string text) =>
        box.State.Document.Children.OfType<Paragraph>().First(paragraph => paragraph.Text.Contains(text, StringComparison.Ordinal));

    private static bool HasMarkers(MarkdownBox box, BlockId block) =>
        box.Document!.Geometry.Find(block) is MdParagraph view && view.Content.Runs.Any(static run => run.Role == RunRole.Marker && run.Style.HasFlag(InlineStyle.Strong));

    private static string Text(MarkdownBox box) => MarkdownSerializer.Serialize(box.State.Document);

    private static string Tail(string text) => text.Length > 40 ? text[^40..].Replace("\n", "|") : text.Replace("\n", "|");
}
