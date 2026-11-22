using RaTeX;

namespace Quill.Editor.WinUI.Rendering.Formulas;

/// <summary>
/// Lays out TeX with the RaTeX engine (M6.3: KaTeX-compatible, native, no JavaScript) and keeps
/// recent layouts by source and mode. A layout takes well under a millisecond once the engine is
/// loaded, so views ask for it synchronously while they lay out; the first call loads the native
/// library, which <see cref="WarmUp"/> does off the UI thread ahead of time.
/// </summary>
internal static class MathTypesetter
{
    private const int Capacity = 512;

    private static readonly Lock Gate = new();
    private static readonly Dictionary<(string Tex, bool Display), LinkedListNode<Entry>> Entries = [];
    private static readonly LinkedList<Entry> Recency = new();
    private static readonly RatexOptions DisplayOptions = new() { Mode = RatexMode.Display };
    private static readonly RatexOptions InlineOptions = new() { Mode = RatexMode.Inline };

    /// <summary>The number of layouts computed (cache misses), for tests.</summary>
    public static int Layouts { get; private set; }

    /// <summary>Loads the native engine in the background so the first formula does not pay for it.</summary>
    public static void WarmUp() => _ = Task.Run(() => Typeset("x", display: false));

    /// <summary>The layout of a formula, or null when it does not parse (the view keeps its source).</summary>
    public static DisplayList? Typeset(string tex, bool display) => Typeset(tex, display, out _);

    /// <summary>The layout of a formula, or null with the reason when it does not parse (null for an empty formula).</summary>
    public static DisplayList? Typeset(string tex, bool display, out string? error)
    {
        tex = tex.Trim();
        error = null;
        if (tex.Length == 0)
        {
            return null;
        }

        var key = (tex, display);
        lock (Gate)
        {
            if (Entries.TryGetValue(key, out var node))
            {
                Recency.Remove(node);
                Recency.AddFirst(node);
                error = node.Value.Error;
                return node.Value.Layout;
            }
        }

        DisplayList? layout;
        try
        {
            layout = Ratex.TryParseAndLayout(tex, display ? DisplayOptions : InlineOptions, out var result, out var message) ? result : null;
            error = layout is null ? (message?.NativeMessage is { Length: > 0 } text ? text : Localization.UiStrings.Get("editor.formulaFailed")) : null;
        }
        catch (Exception exception) when (exception is RatexProtocolException or DllNotFoundException or EntryPointNotFoundException or BadImageFormatException)
        {
            layout = null;
            error = exception.Message;
        }

        lock (Gate)
        {
            Layouts++;
            if (!Entries.ContainsKey(key))
            {
                Entries[key] = Recency.AddFirst(new Entry(key, layout, error));
                if (Entries.Count > Capacity && Recency.Last is { } last)
                {
                    Recency.RemoveLast();
                    Entries.Remove(last.Value.Key);
                }
            }
        }

        return layout;
    }

    private sealed record Entry((string Tex, bool Display) Key, DisplayList? Layout, string? Error);
}
