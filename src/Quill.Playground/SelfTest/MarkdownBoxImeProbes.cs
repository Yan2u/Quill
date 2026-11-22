using Microsoft.UI.Xaml;
using Quill.Editor.Documents;
using Quill.Editor.Input;
using Quill.Editor.Markdown;
using Quill.Editor.State;
using Quill.Editor.WinUI;
using Quill.Editor.WinUI.Elements;
using Quill.Editor.WinUI.Input;
using Quill.Playground.Samples;

namespace Quill.Playground.SelfTest;

/// <summary>
/// M5.4, layer 2: the input method connection of <see cref="MarkdownBox"/> with a scripted text
/// service (Microsoft Pinyin's sequences, Q6): compositions shown by the block, keys left to the
/// input method meanwhile, one undo item, Esc, a composition in a virtual paragraph that never
/// resynchronizes mid-way, Enter and typed characters acknowledged across the two paths (Q66), the
/// layout bounds for the candidate window, and the real CoreTextEditContext attached on load.
/// </summary>
internal static class MarkdownBoxImeProbes
{
    public static async Task RunAsync(SelfTestContext context)
    {
        context.Stage.Children.Clear();
        var service = new ScriptedTextService();
        var box = new MarkdownBox
        {
            Width = 520,
            Height = 360,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            Clipboard = new MemoryClipboard(),
            TextServiceFactory = controller =>
            {
                service.Controller = controller;
                return service;
            },
        };
        context.Stage.Children.Add(box);
        await Task.Delay(50);
        Load(box, "Hello\n\nworld");
        box.Focus(FocusState.Programmatic);
        await Task.Delay(50);
        context.Check(box.TextInput is { IsAttached: true, IsFocused: true } && service.Text == "Hello",
            "ime: the controller is attached and focused; the service sees the active paragraph", service.Text);

        // A composition: shown by the paragraph, keys go to the input method, one undo item.
        box.HandleKey(0x23, KeyModifiers.None);
        int synchronizations = box.TextInput!.FullSynchronizations;
        service.Compose("n", "ni");
        await Task.Delay(30);
        var paragraph = Paragraph(box, 0);
        bool shown = paragraph is { CompositionStart: 5, CompositionEnd: 7 };
        bool keysIgnored = !box.HandleKey(0x08, KeyModifiers.None);
        service.Update(5, 7, "你", 6);
        service.Commit();
        await Task.Delay(30);
        context.Check(shown && keysIgnored, "ime: the composition is shown by the block and keys belong to the input method meanwhile",
            $"composition {paragraph?.CompositionStart}..{paragraph?.CompositionEnd}");
        context.Check(Text(box) == "Hello你\n\nworld" && box.TextInput.FullSynchronizations == synchronizations && Paragraph(box, 0) is { CompositionStart: -1 },
            "ime: the committed text stays, without a full resynchronization during the composition", Text(box).Replace("\n", "|"));
        box.HandleKey(0x5A, KeyModifiers.Ctrl);
        await Task.Delay(30);
        context.Check(Text(box) == "Hello\n\nworld" && service.Text == "Hello", "ime: Ctrl+Z undoes the composition at once, and the service follows", service.Text);

        // Esc: Microsoft Pinyin empties the composition and ends it without a cancel flag.
        service.Compose("t", "tu");
        service.Update(5, 7, string.Empty, 5);
        service.Commit();
        await Task.Delay(30);
        box.HandleKey(0x5A, KeyModifiers.Ctrl);
        await Task.Delay(30);
        context.Check(Text(box) == "Hello\n\nworld", "ime: Esc leaves nothing behind, not even an undo item", Text(box).Replace("\n", "|"));

        // Enter through the key map; the line break the service may report afterwards is acknowledged.
        int acknowledged = box.TextInput.Acknowledgements;
        box.HandleKey(0x0D, KeyModifiers.None);
        await Task.Delay(30);
        int blocks = box.State.Document.Children.Count;
        service.Update(service.Text.Length, service.Text.Length, "\r", service.Text.Length + 1);
        await Task.Delay(30);
        context.Check(box.State.Document.Children.Count == blocks && box.TextInput.Acknowledgements == acknowledged + 1 && service.Text == string.Empty,
            "ime: Enter splits once; the service's line break afterwards is an acknowledgement (07 rule 5)", $"{blocks} blocks");

        // The character path, and CoreText repeating it (Q66).
        box.HandleCharacter('a');
        await Task.Delay(30);
        service.Update(0, 0, "a", 1);
        await Task.Delay(30);
        context.Check(Text(box).Contains("Hello\n\na\n", StringComparison.Ordinal) && !Text(box).Contains("aa", StringComparison.Ordinal),
            "ime: a typed character that CoreText repeats is inserted once (Q66)", Text(box).Replace("\n", "|"));

        // Candidate window: the caret rectangle from the composition start, inside the control, in pixels.
        service.Compose("zh", "zhong");
        var bounds = box.TextInput.OnLayoutRequested();
        double scale = box.XamlRoot.RasterizationScale;
        bool inside = bounds is { } b && b.Text.X >= b.Control.X && b.Text.Y >= b.Control.Y && b.Text.Bottom <= b.Control.Bottom
            && Math.Abs(b.Control.Width - box.ActualWidth * scale) < 1 && b.Text.Width > 10;
        service.Update(1, 6, string.Empty, 1);
        service.Commit();
        context.Check(inside, "ime: layout bounds cover the composition from its start, inside the control, in physical pixels (07 rule 6)", bounds?.ToString());

        // A composition in a virtual paragraph makes a paragraph without resynchronizing mid-way.
        Load(box, "[toc]\n\n[toc]");
        box.Execute(new Quill.Editor.Commands.SetSelection(TextSelection.Caret(new BoundaryPosition(box.State.Document.Id, 1))));
        await Task.Delay(30);
        synchronizations = box.TextInput.FullSynchronizations;
        service.Compose("zi", "字");
        int during = box.TextInput.FullSynchronizations;
        service.Commit();
        await Task.Delay(30);
        context.Check(Text(box) == "[toc]\n\n字\n\n[toc]" && during == synchronizations,
            "ime: a composition in a virtual paragraph makes a paragraph, with no full resynchronization during it (07 rule 10)", Text(box).Replace("\n", "|"));

        context.Check(service.Violations.Count == 0, "ime: every re-entrant request agreed with the notified length (07 rule 3)",
            service.Violations.Count == 0 ? $"{service.Notifications} notifications" : string.Join("; ", service.Violations.Take(3)));

        // View layout and Changed are deferred out of CoreText callbacks and flush on the dispatcher.
        Load(box, "abc");
        await Task.Delay(50);
        box.Focus(FocusState.Programmatic);
        await Task.Delay(30);
        int changedCount = 0;
        void OnChanged(object? sender, EventArgs e) => changedCount++;
        box.Changed += OnChanged;
        service.Compose("c");
        int beforeUpdate = changedCount;
        service.Update(0, 1, "从", 1);
        int changedDuringUpdate = changedCount - beforeUpdate;
        await Task.Delay(50);
        int changedAfterFlush = changedCount - beforeUpdate;
        service.Commit();
        await Task.Delay(50);
        box.Changed -= OnChanged;
        context.Check(changedDuringUpdate == 0 && changedAfterFlush > 0,
            "ime: Changed is deferred during CoreText callback and flushes on dispatcher",
            $"during={changedDuringUpdate}, after={changedAfterFlush}");

        // Read-only MarkdownBox never creates or attaches a CoreTextEditContext.
        context.Stage.Children.Clear();
        var readOnly = new MarkdownBox { Width = 300, Height = 200, IsReadOnly = true, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top };
        Load(readOnly, "Preview only");
        context.Stage.Children.Add(readOnly);
        await Task.Delay(100);
        context.Check(readOnly.TextInput is { IsAttached: false } && readOnly.ActiveTextService is null,
            "ime: read-only MarkdownBox never attaches CoreTextEditContext");

        // The real text service: attached once on load, reused across Load and ToggleSourceMode, detached on IsReadOnly/unload.
        context.Stage.Children.Clear();
        var real = new MarkdownBox { Width = 300, Height = 200, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top };
        Load(real, "Hello");
        context.Stage.Children.Add(real);
        await Task.Delay(100);
        real.Focus(FocusState.Programmatic);
        await Task.Delay(50);
        var firstService = real.ActiveTextService;
        context.Check(real.TextServiceError is null && real.TextInput is { IsAttached: true, IsFocused: true } && firstService is not null,
            "ime: MarkdownBox attaches a CoreTextEditContext when loaded", real.TextServiceError);

        Load(real, "Second document");
        await Task.Delay(60);
        var afterLoad = real.ActiveTextService;
        real.ToggleSourceMode();
        await Task.Delay(60);
        var afterSource = real.ActiveTextService;
        real.ToggleSourceMode();
        await Task.Delay(60);
        var afterBack = real.ActiveTextService;
        context.Check(ReferenceEquals(firstService, afterLoad) && ReferenceEquals(firstService, afterSource) && ReferenceEquals(firstService, afterBack)
            && real.TextInput is { IsAttached: true },
            "ime: CoreTextEditContext instance is reused across Load and ToggleSourceMode");

        real.IsReadOnly = true;
        await Task.Delay(30);
        context.Check(real.TextInput is { IsAttached: false } && real.ActiveTextService is null,
            "ime: setting IsReadOnly detaches CoreTextEditContext");

        real.IsReadOnly = false;
        await Task.Delay(50);
        context.Check(real.TextInput is { IsAttached: true } && real.ActiveTextService is not null,
            "ime: clearing IsReadOnly reattaches CoreTextEditContext");

        context.Stage.Children.Clear();
        await Task.Delay(50);
        context.Check(real.TextInput is { IsAttached: false } && real.ActiveTextService is null,
            "ime: unloading MarkdownBox detaches CoreTextEditContext");
    }

    private static void Load(MarkdownBox box, string markdown)
    {
        var ids = new SequentialBlockIdSource();
        var document = MarkdownImporter.Import(markdown, ids).Document;
        box.Load(new EditorState { Document = document, Selection = TextSelection.Caret(EditorDocuments.FirstCaret(document)) }, ids);
    }

    private static MdTextBlockView? Paragraph(MarkdownBox box, int index) =>
        box.State.Document.Children[index] is { } block ? box.Document?.Geometry.Find(block.Id) as MdTextBlockView : null;

    private static string Text(MarkdownBox box) => MarkdownSerializer.Serialize(box.State.Document).TrimEnd('\n');
}
