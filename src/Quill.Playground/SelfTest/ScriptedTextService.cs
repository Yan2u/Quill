using Quill.Editor.WinUI.Input;
using Windows.UI.Text.Core;

namespace Quill.Playground.SelfTest;

/// <summary>
/// Plays a text service for the layer-2 tests (08: input methods are simulated): keeps its own copy
/// of the text, applies the updates it sent once they succeed, and from every notification calls
/// back the way TextInputFramework may, recording any disagreement about the text's length (which
/// makes the real framework fail fast, 07 rule 3).
/// </summary>
internal sealed class ScriptedTextService : ITextServiceContext
{
    public TextInputController Controller { get; set; } = null!;

    public string Text { get; private set; } = string.Empty;

    public int Caret { get; private set; }

    public List<string> Violations { get; } = [];

    public int Notifications { get; private set; }

    public static CoreTextRange Range(int start, int end) => new() { StartCaretPosition = start, EndCaretPosition = end };

    public CoreTextTextUpdatingResult Update(int start, int end, string text, int caret)
    {
        var result = Controller.OnTextUpdating(Range(start, end), text, Range(caret, caret), canceled: false);
        if (result == CoreTextTextUpdatingResult.Succeeded)
        {
            Text = string.Concat(Text.AsSpan(0, Math.Min(start, Text.Length)), text, Text.AsSpan(Math.Min(end, Text.Length)));
            Caret = caret;
        }

        return result;
    }

    /// <summary>A composition as Microsoft Pinyin sends it: each step replaces the previous text; then the commit.</summary>
    public void Compose(params string[] steps)
    {
        Controller.OnCompositionStarted();
        int start = Caret;
        int length = 0;
        foreach (string step in steps)
        {
            Update(start, start + length, step, start + step.Length);
            length = step.Length;
        }
    }

    public void Commit() => Controller.OnCompositionCompleted();

    public void NotifyFocusEnter() => Text = Controller.OnTextRequested(Range(0, int.MaxValue));

    public void NotifyFocusLeave()
    {
    }

    public void NotifyTextChanged(CoreTextRange modifiedRange, int newLength, CoreTextRange newSelection)
    {
        Notifications++;
        if (modifiedRange.EndCaretPosition > Text.Length)
        {
            Violations.Add($"replaced range ends at {modifiedRange.EndCaretPosition}, beyond the service text of {Text.Length}");
        }

        int start = modifiedRange.StartCaretPosition;
        int end = Math.Min(modifiedRange.EndCaretPosition, Text.Length);
        string inserted = Controller.OnTextRequested(Range(start, start + newLength));
        if (inserted.Length != newLength)
        {
            Violations.Add($"announced {newLength} characters, TextRequested returned {inserted.Length}");
        }

        Text = string.Concat(Text.AsSpan(0, Math.Min(start, Text.Length)), inserted, Text.AsSpan(end));
        Caret = newSelection.EndCaretPosition;
    }

    public void NotifySelectionChanged(CoreTextRange selection) => Caret = selection.EndCaretPosition;

    public void NotifyLayoutChanged() => Controller.OnLayoutRequested();

    public void Detach()
    {
    }
}
