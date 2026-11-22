namespace Quill.Editor.Input;

/// <summary>
/// Translates Windows virtual-key codes to the platform-independent key names of <see cref="KeyChord"/>
/// (07). The view passes the code as a number, so the table is tested without WinUI.
/// </summary>
public static class VirtualKeys
{
    private static readonly Dictionary<int, string> Names = new()
    {
        [0x08] = "Backspace",
        [0x09] = "Tab",
        [0x0D] = "Enter",
        [0x1B] = "Escape",
        [0x20] = "Space",
        [0x21] = "PageUp",
        [0x22] = "PageDown",
        [0x23] = "End",
        [0x24] = "Home",
        [0x25] = "Left",
        [0x26] = "Up",
        [0x27] = "Right",
        [0x28] = "Down",
        [0x2D] = "Insert",
        [0x2E] = "Delete",
        [0xBA] = ";",
        [0xBB] = "=",
        [0xBC] = ",",
        [0xBD] = "-",
        [0xBE] = ".",
        [0xBF] = "/",
        [0xC0] = "`",
        [0xDB] = "[",
        [0xDC] = "\\",
        [0xDD] = "]",
        [0xDE] = "'",
    };

    /// <summary>
    /// The chord for a key press, or null for keys that are not chords on their own (Shift, Ctrl,
    /// Alt, Windows keys, the IME's process key).
    /// </summary>
    public static KeyChord? ToChord(int virtualKey, KeyModifiers modifiers) =>
        NameOf(virtualKey) is { } name ? new KeyChord(modifiers, name) : null;

    /// <summary>The key name: letters, digits (the number pad's too), F1–F24 and the named keys.</summary>
    public static string? NameOf(int virtualKey) => virtualKey switch
    {
        >= 0x41 and <= 0x5A => ((char)virtualKey).ToString(),
        >= 0x30 and <= 0x39 => ((char)virtualKey).ToString(),
        >= 0x60 and <= 0x69 => ((char)('0' + virtualKey - 0x60)).ToString(),
        >= 0x70 and <= 0x87 => $"F{virtualKey - 0x6F}",
        _ => Names.GetValueOrDefault(virtualKey),
    };
}
