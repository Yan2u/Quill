using Microsoft.UI.Xaml;
using Quill.Playground.ImePrototype;
using Windows.UI.Text;
using Windows.UI.Text.Core;

namespace Quill.Playground.SelfTest;

/// <summary>
/// Q6: the parts of the CoreTextEditContext integration that run without a real input method.
/// The platform probes create the context in this unpackaged C# app and focus the surface; the
/// scripted probes drive the controller with the event sequences a text service sends (typing,
/// composition, cancel, input rules, Enter, block switches, undo) through a fake service that calls
/// back re-entrantly from every notification and records any length disagreement, which would make
/// TextInputFramework fail fast (07, rule 3). Typing with Microsoft Pinyin is the manual part.
/// </summary>
internal static class ImeProbes
{
    public static async Task RunAsync(SelfTestContext context)
    {
        ProbeScripted(context);
        await ProbePlatformAsync(context);
    }

    private static void ProbeScripted(SelfTestContext context)
    {
        var log = new ImeEventLog();
        var store = new ImeTextStore("", "second");
        var queue = new Queue<Action>();
        var service = new FakeTextService();
        var controller = new ImeInputController(store, action => { queue.Enqueue(action); return true; }, () => null, log);
        store.Changed += controller.NotifyClientChanged;
        service.Controller = controller;
        controller.Attach(service);
        controller.FocusEnter();
        void Run()
        {
            while (queue.TryDequeue(out var action))
            {
                action();
            }
        }

        bool Synchronized() => service.Text == store.ActiveText;

        // Typing: plain text updates match the prediction and need no resynchronization.
        service.Type("a");
        service.Type("b");
        Run();
        context.Check(store.ActiveText == "ab" && store.Selection == (2, 2) && controller.FullSynchronizations == 0 && Synchronized(),
            "scripted: typed characters are applied without resynchronization", $"{ImeEventLog.Quote(store.ActiveText)}, syncs {controller.FullSynchronizations}");

        // A composition: three updates, a target clause, a commit: one undo item.
        int undo = store.UndoCount;
        controller.OnCompositionStarted();
        service.Update(2, 2, "n", 3);
        service.Update(2, 3, "ni", 4);
        controller.OnFormatUpdating(Range(2, 4), CoreTextFormatUpdatingReason.CompositionTargetUnconverted, UnderlineType.Thick, false);
        bool underlined = store.CompositionRange == (2, 4) && store.CompositionSegments is [{ ThickUnderline: true }];
        service.Update(2, 4, "你", 3);
        controller.OnCompositionCompleted(false);
        Run();
        context.Check(underlined, "scripted: the composition range and a thick target clause are recorded for the projection");
        context.Check(store.ActiveText == "ab你" && !store.IsComposing && store.CompositionRange is null && store.UndoCount == undo + 1 && store.CompositionSteps == 3,
            "scripted: a committed composition is one undo item", $"undo {undo} -> {store.UndoCount}, steps {store.CompositionSteps}");
        context.Check(controller.FullSynchronizations == 0 && Synchronized(), "scripted: no resynchronization during or after a composition");

        // Esc: the service removes the composition text and cancels.
        undo = store.UndoCount;
        controller.OnCompositionStarted();
        service.Update(3, 3, "x", 4);
        service.Update(3, 4, "", 3);
        controller.OnCompositionCompleted(true);
        Run();
        context.Check(store.ActiveText == "ab你" && store.UndoCount == undo && Synchronized(), "scripted: a canceled composition leaves no text and no undo item");

        // An input rule changes the text the service predicted: resynchronize after the callback.
        int syncs = controller.FullSynchronizations;
        service.Type("-");
        var result = service.Type(">");
        bool deferred = controller.FullSynchronizations == syncs;
        Run();
        context.Check(result == CoreTextTextUpdatingResult.Succeeded && deferred && store.ActiveText == "ab你→" && controller.FullSynchronizations == syncs + 1 && Synchronized(),
            "scripted: an input rule is resynchronized after the callback, not inside it (07 rule 4)", ImeEventLog.Quote(service.Text));

        // Enter runs through the key map; the line break the service reports afterwards is acknowledged.
        controller.RecordSemanticKey("\r");
        store.ExecuteKey(ImeKey.Enter, shift: false);
        Run();
        int blocks = store.BlockCount;
        context.Check(blocks == 3 && store.ActiveText == string.Empty && Synchronized(), "scripted: Enter splits the block and resynchronizes to the new one (07 rule 2)");
        service.Update(service.Text.Length, service.Text.Length, "\r", service.Text.Length + 1);
        Run();
        context.Check(store.BlockCount == blocks && controller.Acknowledgements == 1 && controller.PendingAcknowledgements == 0 && Synchronized(),
            "scripted: the service's line break after Enter is an acknowledgement, not a second split (07 rule 5)");
        service.Update(service.Text.Length, service.Text.Length, "\r", service.Text.Length + 1);
        Run();
        context.Check(store.BlockCount == blocks + 1 && Synchronized(), "scripted: a line break without a pending key splits the block");

        // Switching blocks resynchronizes the whole text; a composition in the new block works.
        store.ExecuteKey(ImeKey.Up, shift: false);
        Run();
        context.Check(store.BlockIndex == 1 && Synchronized(), "scripted: Up switches the block and the service sees its text", ImeEventLog.Quote(service.Text));
        controller.OnCompositionStarted();
        service.Update(0, 0, "zhong", 5);
        service.Update(0, 5, "中", 1);
        controller.OnCompositionCompleted(false);
        Run();
        context.Check(store.ActiveText == "中" && Synchronized(), "scripted: a composition in an empty block after switching");

        // Undo restores the block before the composition in one step.
        store.ExecuteKey(ImeKey.Undo, shift: false);
        Run();
        context.Check(store.ActiveText == string.Empty && Synchronized(), "scripted: Ctrl+Z undoes the committed phrase at once");

        // Keys during a composition belong to the text service.
        controller.OnCompositionStarted();
        bool handled = store.ExecuteKey(ImeKey.Backspace, shift: false);
        controller.OnCompositionCompleted(true);
        context.Check(!handled, "scripted: semantic keys are ignored during a composition");

        // Keys the input method passes through (Microsoft Pinyin in English mode) arrive only as characters (Q6).
        int acknowledgements = controller.Acknowledgements;
        foreach (char character in "a->")
        {
            controller.OnCharacterReceived(character);
        }

        Run();
        context.Check(store.ActiveText == "a→" && Synchronized(), "scripted: CharacterReceived inserts text, input rules included", ImeEventLog.Quote(store.ActiveText));
        context.Check(!controller.OnCharacterReceived('\r') && !controller.OnCharacterReceived('\b') && !controller.OnCharacterReceived('\u001A'),
            "scripted: control characters are left to the key map");
        controller.OnCharacterReceived('\uD83D');
        controller.OnCharacterReceived('\uDC4D');
        Run();
        context.Check(store.ActiveText == "a→👍" && Synchronized(), "scripted: a surrogate pair from two CharacterReceived events is one insertion");

        // One keystroke through both paths, in either order, is inserted once (no time window).
        controller.OnCharacterReceived('x');
        Run();
        service.Update(4, 4, "x", 5);
        Run();
        context.Check(store.ActiveText == "a→👍x" && controller.Acknowledgements == acknowledgements + 1 && Synchronized(),
            "scripted: a CoreText update repeating a received character is an acknowledgement", ImeEventLog.Quote(store.ActiveText));
        service.Type("y");
        bool consumed = controller.OnCharacterReceived('y');
        Run();
        context.Check(consumed && store.ActiveText == "a→👍xy" && controller.Acknowledgements == acknowledgements + 2 && Synchronized(),
            "scripted: a character repeating the CoreText update just applied is an acknowledgement", ImeEventLog.Quote(store.ActiveText));
        controller.OnCharacterReceived('z');
        controller.OnCharacterReceived('z');
        Run();
        context.Check(store.ActiveText == "a→👍xyzz" && Synchronized(), "scripted: the same character typed twice is inserted twice");
        bool ignoredWhileComposing = true;
        controller.OnCompositionStarted();
        ignoredWhileComposing &= !controller.OnCharacterReceived('q');
        controller.OnCompositionCompleted(true);
        Run();
        context.Check(ignoredWhileComposing && store.ActiveText == "a→👍xyzz", "scripted: characters are not taken during a composition");

        context.Check(service.Violations.Count == 0, "scripted: every re-entrant request agreed with the notified length (07 rule 3)",
            service.Violations.Count == 0 ? $"{service.ReentrantRequests} re-entrant requests" : string.Join("; ", service.Violations));
        controller.Detach();
    }

    private static async Task ProbePlatformAsync(SelfTestContext context)
    {
        try
        {
            var manager = CoreTextServicesManager.GetForCurrentView();
            context.Check(manager is not null, "platform: CoreTextServicesManager.GetForCurrentView() in an unpackaged C# WinUI 3 app");
            context.Info($"platform: input language {manager?.InputLanguage?.LanguageTag ?? "unknown"}");
        }
        catch (Exception exception)
        {
            context.Fail("platform: CoreTextServicesManager.GetForCurrentView()", exception);
            return;
        }

        context.Stage.Children.Clear();
        var log = new ImeEventLog();
        var surface = new ImeSurface(new ImeTextStore("Hello 中文", "second"), log) { Width = 500, Height = 200, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top };
        bool loaded = false;
        surface.Loaded += (_, _) => loaded = true;
        context.Stage.Children.Add(surface);
        for (int i = 0; i < 50 && !loaded; i++)
        {
            await Task.Delay(20);
        }

        surface.UpdateLayout();
        context.Check(surface.HasService, "platform: CoreTextEditContext is created on the UI thread", surface.ServiceError);
        bool focused = surface.Focus(FocusState.Programmatic);
        await Task.Delay(100);
        context.Check(focused && surface.Controller.IsFocused, "platform: focusing the surface enters the text service focus (NotifyFocusEnter does not throw)");

        surface.Store.PlaceCaret(0, 5);
        var bounds = surface.GetLayoutBounds();
        context.Check(bounds is { } b && b.Text.Height > 0 && b.Control.Width > 0
            && b.Text.X >= b.Control.X && b.Text.Y >= b.Control.Y && b.Text.Y + b.Text.Height <= b.Control.Y + b.Control.Height,
            "platform: layout bounds put the caret inside the surface, in screen pixels (07 rule 6)",
            bounds is { } r ? $"text {r.Text}, control {r.Control}" : "none");
        double scale = surface.XamlRoot.RasterizationScale;
        context.Check(bounds is { } h && Math.Abs(h.Control.Width - 500 * scale) < 1, "platform: control bounds are scaled to physical pixels", $"scale {scale:F2}");

        surface.Store.ExecuteKey(ImeKey.Down, shift: false);
        await Task.Delay(50);
        context.Check(surface.Controller.KnownText == "second", "platform: a block switch notifies the real context without an exception",
            string.Join(" | ", log.Lines.Where(static line => line.Contains("threw", StringComparison.Ordinal))));
        context.Info($"platform: callbacks from the real service while unfocused by the system: {log.Lines.Count(static line => line.Contains("Requested", StringComparison.Ordinal))} requests");

        surface.Controller.FocusLeave();
        context.Stage.Children.Clear();
    }

    private static CoreTextRange Range(int start, int end) => new() { StartCaretPosition = start, EndCaretPosition = end };

    /// <summary>
    /// Plays the text service: keeps its own copy of the text, applies updates it sent once they
    /// succeed, and from every notification calls back the way TextInputFramework may, checking
    /// that the text it gets has the length the notification announced.
    /// </summary>
    private sealed class FakeTextService : ITextServiceContext
    {
        public ImeInputController Controller { get; set; } = null!;

        public string Text { get; private set; } = string.Empty;

        public int Caret { get; private set; }

        public List<string> Violations { get; } = [];

        public int ReentrantRequests { get; private set; }

        public CoreTextTextUpdatingResult Type(string text) => Update(Caret, Caret, text, Caret + text.Length);

        public CoreTextTextUpdatingResult Update(int start, int end, string text, int caret)
        {
            var result = Controller.OnTextUpdating(Range(start, end), text, Range(caret, caret), canceled: false);
            if (result == CoreTextTextUpdatingResult.Succeeded)
            {
                Text = string.Concat(Text.AsSpan(0, start), text, Text.AsSpan(end));
                Caret = caret;
            }

            return result;
        }

        public void NotifyFocusEnter() => Text = Controller.OnTextRequested(Range(0, int.MaxValue));

        public void NotifyFocusLeave()
        {
        }

        public void NotifyTextChanged(CoreTextRange modifiedRange, int newLength, CoreTextRange newSelection)
        {
            if (modifiedRange.EndCaretPosition > Text.Length)
            {
                Violations.Add($"replaced range {modifiedRange.EndCaretPosition} beyond the service text {Text.Length}");
            }

            int start = modifiedRange.StartCaretPosition;
            int end = Math.Min(modifiedRange.EndCaretPosition, Text.Length);
            ReentrantRequests++;
            string inserted = Controller.OnTextRequested(Range(start, start + newLength));
            if (inserted.Length != newLength)
            {
                Violations.Add($"announced {newLength} characters, TextRequested returned {inserted.Length}");
            }

            Text = string.Concat(Text.AsSpan(0, start), inserted, Text.AsSpan(end));
            string all = Controller.OnTextRequested(Range(0, int.MaxValue));
            if (all != Text)
            {
                Violations.Add($"service text {ImeEventLog.Quote(Text)} but TextRequested gives {ImeEventLog.Quote(all)}");
            }

            Caret = newSelection.EndCaretPosition;
            var selection = Controller.OnSelectionRequested();
            if (selection.EndCaretPosition > Text.Length)
            {
                Violations.Add($"selection {selection.EndCaretPosition} beyond {Text.Length}");
            }
        }

        public void NotifySelectionChanged(CoreTextRange selection) => Caret = selection.EndCaretPosition;

        public void NotifyLayoutChanged() => Controller.OnLayoutRequested();

        public void Detach()
        {
        }
    }
}
