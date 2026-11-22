using System.Collections.Immutable;
using Quill.Editor.Commands;

namespace Quill.Editor.Input;

[Flags]
public enum KeyModifiers
{
    None = 0,
    Ctrl = 1,
    Shift = 2,
    Alt = 4,
}

/// <summary>
/// A platform-independent key combination such as <c>Ctrl+Shift+K</c> (07-input-and-ime.md). Key names
/// are the view's virtual key names without platform types: letters, digits, "Enter", "Tab", "Left"…
/// </summary>
public readonly record struct KeyChord(KeyModifiers Modifiers, string Key)
{
    /// <summary>Parses "Ctrl+Shift+K", "Shift+Tab", "Enter". Modifier names are case-insensitive.</summary>
    public static KeyChord Parse(string text)
    {
        var parts = text.Split('+', StringSplitOptions.TrimEntries);
        var modifiers = KeyModifiers.None;
        for (int i = 0; i < parts.Length - 1; i++)
        {
            modifiers |= parts[i].ToUpperInvariant() switch
            {
                "CTRL" or "CONTROL" => KeyModifiers.Ctrl,
                "SHIFT" => KeyModifiers.Shift,
                "ALT" => KeyModifiers.Alt,
                _ => throw new FormatException($"Unknown modifier '{parts[i]}' in '{text}'."),
            };
        }

        string key = parts[^1];
        if (key.Length == 0)
        {
            throw new FormatException($"No key in '{text}'.");
        }

        return new KeyChord(modifiers, key.Length == 1 ? key.ToUpperInvariant() : key);
    }

    public override string ToString()
    {
        // The Windows order: Ctrl, Alt, Shift (Alt+Shift+5, as in Typora's menus).
        string prefix = (Modifiers.HasFlag(KeyModifiers.Ctrl) ? "Ctrl+" : string.Empty)
            + (Modifiers.HasFlag(KeyModifiers.Alt) ? "Alt+" : string.Empty)
            + (Modifiers.HasFlag(KeyModifiers.Shift) ? "Shift+" : string.Empty);
        return prefix + Key;
    }
}

/// <summary>Clipboard actions are completed by the view, which reads or writes the system clipboard.</summary>
public enum ClipboardAction
{
    Copy,
    CopyAsMarkdown,

    /// <summary>The text as shown, whatever the settings say (Edit menu, Q102).</summary>
    CopyAsPlainText,
    Cut,
    Paste,
    PasteAsPlainText,
}

/// <summary>What a key chord does.</summary>
public abstract record KeyBinding;

public sealed record CommandBinding(Command Command) : KeyBinding;

public sealed record ClipboardBinding(ClipboardAction Action) : KeyBinding;

/// <summary>Dialogs the view shows before it runs a command (Ctrl+T asks for the table's size).</summary>
public enum EditorDialog
{
    InsertTable,

    /// <summary>Ctrl+K: the view reads the clipboard for a URL, then runs <see cref="InsertLink"/>.</summary>
    InsertLink,

    /// <summary>Ctrl+Shift+I: as Ctrl+K, for <see cref="InsertImage"/>.</summary>
    InsertImage,

    /// <summary>Format > Image > Insert local images...: the host asks for image files and inserts them by the image rule (14-settings.md).</summary>
    InsertLocalImage,
}

/// <summary>Actions of the view alone, which change no state.</summary>
public enum ViewAction
{
    /// <summary>Ctrl+J: scrolls the caret into view.</summary>
    RevealSelection,

    /// <summary>Ctrl+/: switches between the editing and the source mode (11-source-mode.md).</summary>
    ToggleSourceMode,

    /// <summary>Ctrl+F: opens the find panel (12-find-replace.md).</summary>
    Find,

    /// <summary>Ctrl+H: opens the find panel with the replace row.</summary>
    Replace,

    /// <summary>F3: the next match (the find panel opens when nothing was searched yet).</summary>
    FindNext,

    /// <summary>Shift+F3: the previous match.</summary>
    FindPrevious,
}

/// <summary>A key bound to a view action.</summary>
public sealed record ViewBinding(ViewAction Action) : KeyBinding;

/// <summary>A key that opens a dialog of the view; the dialog's result becomes a command.</summary>
public sealed record DialogBinding(EditorDialog Dialog) : KeyBinding;

/// <summary>
/// The key map: data from key chords to commands (07-input-and-ime.md). The effect of a command in a
/// given block is up to the behaviors, so the map does not depend on block types.
/// </summary>
public sealed class KeyMap
{
    private readonly ImmutableDictionary<KeyChord, KeyBinding> _bindings;

    private KeyMap(ImmutableDictionary<KeyChord, KeyBinding> bindings) => _bindings = bindings;

    /// <summary>The default map: 07's editing keys, caret movement and Typora's heading shortcuts.</summary>
    public static KeyMap Default { get; } = CreateDefault();

    public IReadOnlyDictionary<KeyChord, KeyBinding> Bindings => _bindings;

    public KeyBinding? Find(KeyChord chord) => _bindings.GetValueOrDefault(chord);

    /// <summary>A map with one binding added or replaced.</summary>
    public KeyMap With(string chord, KeyBinding binding) => new(_bindings.SetItem(KeyChord.Parse(chord), binding));

    private static KeyMap CreateDefault()
    {
        var map = ImmutableDictionary.CreateBuilder<KeyChord, KeyBinding>();
        void Bind(string chord, Command command) => map[KeyChord.Parse(chord)] = new CommandBinding(command);
        void Clip(string chord, ClipboardAction action) => map[KeyChord.Parse(chord)] = new ClipboardBinding(action);

        Bind("Enter", new SplitBlock());
        Bind("Shift+Enter", new InsertSoftBreak());
        Bind("Ctrl+Enter", new SplitBlockAlternate());
        Bind("Tab", new Indent());
        Bind("Shift+Tab", new Outdent());
        Bind("Backspace", new DeleteBackward());
        Bind("Ctrl+Backspace", new DeleteBackward(TextUnit.Word));
        Bind("Delete", new DeleteForward());
        Bind("Ctrl+Delete", new DeleteForward(TextUnit.Word));
        Bind("Ctrl+A", new SelectAll());
        Bind("Ctrl+E", new SelectScope());
        Bind("Ctrl+L", new SelectLine());
        map[KeyChord.Parse("Ctrl+T")] = new DialogBinding(EditorDialog.InsertTable);

        // Typora's Format, Paragraph and Edit menus (M6.0).
        Bind("Ctrl+B", new ToggleInline(InlineFormat.Strong));
        Bind("Ctrl+I", new ToggleInline(InlineFormat.Emphasis));
        Bind("Ctrl+U", new ToggleInline(InlineFormat.Underline));
        Bind("Ctrl+Shift+`", new ToggleInline(InlineFormat.Code));
        Bind("Alt+Shift+5", new ToggleInline(InlineFormat.Strikethrough));
        Bind("Ctrl+\\", new ClearFormat());
        map[KeyChord.Parse("Ctrl+K")] = new DialogBinding(EditorDialog.InsertLink);
        map[KeyChord.Parse("Ctrl+Shift+I")] = new DialogBinding(EditorDialog.InsertImage);
        Bind("Ctrl+=", new ChangeHeadingLevel(1));
        Bind("Ctrl+-", new ChangeHeadingLevel(-1));
        Bind("Ctrl+Shift+K", new ToggleBlock(BlockFormat.CodeBlock));
        Bind("Ctrl+Shift+M", new ToggleBlock(BlockFormat.MathBlock));
        Bind("Ctrl+Shift+Q", new ToggleBlock(BlockFormat.Quote));
        Bind("Ctrl+Shift+[", new ToggleList(ListKind.Ordered));
        Bind("Ctrl+Shift+]", new ToggleList(ListKind.Bullet));
        Bind("Ctrl+Shift+X", new ToggleList(ListKind.Task));
        Bind("Ctrl+[", new Indent(InsertsTab: false));
        Bind("Ctrl+]", new Outdent());
        Bind("Ctrl+D", new SelectWordAtCaret());
        Bind("Ctrl+Shift+D", new DeleteWord());
        map[KeyChord.Parse("Ctrl+J")] = new ViewBinding(ViewAction.RevealSelection);
        map[KeyChord.Parse("Ctrl+/")] = new ViewBinding(ViewAction.ToggleSourceMode);
        map[KeyChord.Parse("Ctrl+F")] = new ViewBinding(ViewAction.Find);
        map[KeyChord.Parse("Ctrl+H")] = new ViewBinding(ViewAction.Replace);
        map[KeyChord.Parse("F3")] = new ViewBinding(ViewAction.FindNext);
        map[KeyChord.Parse("Shift+F3")] = new ViewBinding(ViewAction.FindPrevious);

        // Tables (Q77): Ctrl+Enter inserts a row below (the table's SplitBlockAlternate); outside a table these do nothing.
        Bind("Alt+Up", new EditTable(TableEdit.MoveRowUp));
        Bind("Alt+Down", new EditTable(TableEdit.MoveRowDown));
        Bind("Alt+Left", new EditTable(TableEdit.MoveColumnLeft));
        Bind("Alt+Right", new EditTable(TableEdit.MoveColumnRight));
        Bind("Ctrl+Shift+Backspace", new EditTable(TableEdit.DeleteRow));
        Bind("Ctrl+Z", new Undo());
        Bind("Ctrl+Y", new Redo());
        for (int level = 0; level <= 6; level++)
        {
            Bind($"Ctrl+{level}", new SetHeadingLevel(level));
        }

        foreach (var (shift, extend) in new[] { (string.Empty, false), ("Shift+", true) })
        {
            Bind(shift + "Left", new MoveCaret(MoveDirection.Backward, TextUnit.Grapheme, extend));
            Bind(shift + "Right", new MoveCaret(MoveDirection.Forward, TextUnit.Grapheme, extend));
            Bind("Ctrl+" + shift + "Left", new MoveCaret(MoveDirection.Backward, TextUnit.Word, extend));
            Bind("Ctrl+" + shift + "Right", new MoveCaret(MoveDirection.Forward, TextUnit.Word, extend));
            Bind(shift + "Up", new MoveCaret(MoveDirection.Up, TextUnit.VisualLine, extend));
            Bind(shift + "Down", new MoveCaret(MoveDirection.Down, TextUnit.VisualLine, extend));
            Bind("Ctrl+" + shift + "Up", new MoveCaret(MoveDirection.Backward, TextUnit.Paragraph, extend));
            Bind("Ctrl+" + shift + "Down", new MoveCaret(MoveDirection.Forward, TextUnit.Paragraph, extend));
            Bind(shift + "Home", new MoveCaret(MoveDirection.Backward, TextUnit.LineBoundary, extend));
            Bind(shift + "End", new MoveCaret(MoveDirection.Forward, TextUnit.LineBoundary, extend));
            Bind("Ctrl+" + shift + "Home", new MoveCaret(MoveDirection.Backward, TextUnit.Document, extend));
            Bind("Ctrl+" + shift + "End", new MoveCaret(MoveDirection.Forward, TextUnit.Document, extend));
            Bind(shift + "PageUp", new MoveCaret(MoveDirection.Up, TextUnit.Page, extend));
            Bind(shift + "PageDown", new MoveCaret(MoveDirection.Down, TextUnit.Page, extend));
        }

        Clip("Ctrl+C", ClipboardAction.Copy);
        Clip("Ctrl+Shift+C", ClipboardAction.CopyAsMarkdown);
        Clip("Ctrl+X", ClipboardAction.Cut);
        Clip("Ctrl+V", ClipboardAction.Paste);
        Clip("Ctrl+Shift+V", ClipboardAction.PasteAsPlainText);
        return new KeyMap(map.ToImmutable());
    }
}
