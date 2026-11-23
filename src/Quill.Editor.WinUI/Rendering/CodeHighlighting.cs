using Quill.Editor.WinUI.Text;
using Quill.Highlighting;

namespace Quill.Editor.WinUI.Rendering;

/// <summary>
/// The colors of one code block in the editing mode (M6.3, Q87): its <see cref="CodeHighlighter"/>,
/// which re-analyzes only the lines an edit changed, and the text source built from its runs. Blocks
/// realized again (scrolled back into view) take their colors from a small shared cache instead of
/// analyzing the whole block again. UI thread only.
/// </summary>
internal sealed class CodeHighlighting
{
    private const int CacheCapacity = 128;
    private const int MaxCacheChars = 256_000;

    private static readonly Dictionary<(HighlightTheme, string, string), LinkedListNode<CacheEntry>> Cache = [];
    private static readonly LinkedList<CacheEntry> Recent = new();
    private static readonly Lock WarmingGate = new();
    private static readonly HashSet<HighlightTheme> Warming = [];
    private static int _cachedChars;

    private readonly CodeHighlighter _highlighter;

    private CodeHighlighting(CodeHighlighter highlighter, string info)
    {
        _highlighter = highlighter;
        Info = info;
    }

    public string Info { get; }

    public HighlightTheme Theme => _highlighter.Theme;

    /// <summary>How many lines the highlighter analyzed in all, for tests (cache hits analyze none).</summary>
    public static long AnalyzedLines { get; private set; }

    /// <summary>Raised on the UI thread when a theme's grammars finished loading in the background: code shown plain meanwhile colors now.</summary>
    public static event Action? Loaded;

    /// <summary>
    /// Returns cached highlighting spans without touching or warming <see cref="GrammarCatalog"/>.
    /// Returns false only when the language has a grammar and the block text is not cached yet.
    /// </summary>
    public static bool TryBuildCached(HighlightTheme theme, string info, string text, out TextSource? source)
    {
        if (LanguageMap.LanguageOf(info) is not { } language)
        {
            source = null;
            return true;
        }

        var key = (theme, language, text);
        if (Cache.TryGetValue(key, out var node))
        {
            Recent.Remove(node);
            Recent.AddFirst(node);
            source = BuildSource(text, node.Value.Lines, node.Value.DefaultForeground);
            return true;
        }

        source = null;
        return false;
    }

    /// <summary>
    /// Highlights an unfocused code block without retaining a <see cref="CodeHighlighter"/> or state
    /// stacks on the element; checks the bounded span cache before checking or warming grammars.
    /// </summary>
    public static TextSource? BuildOnce(HighlightTheme theme, string info, string text)
    {
        if (TryBuildCached(theme, info, text, out TextSource? cached))
        {
            return cached;
        }

        if (LanguageMap.LanguageOf(info) is not { } language)
        {
            return null;
        }

        if (!GrammarCatalog.IsLoaded(theme))
        {
            WarmUp(theme);
            return null;
        }

        using var lease = GrammarCatalog.AcquireLease();
        EditorMemoryTrimmer.NotifyGrammarUsed();
        if (CodeHighlighter.Create(theme, info) is not { } highlighter)
        {
            return null;
        }

        IReadOnlyList<HighlightSpan[]> lines;
        try
        {
            lines = highlighter.Highlight(text);
        }
        catch (Exception)
        {
            // A grammar that fails on some input (TextMateSharp, Oniguruma): the code shows without colors.
            return TextSource.Plain(text);
        }

        AnalyzedLines += highlighter.AnalyzedLines;
        Remember((theme, language, text), lines, highlighter.DefaultForeground, text.Length);
        return BuildSource(text, lines, highlighter.DefaultForeground);
    }

    /// <summary>
    /// The highlighting of a block with an info string in a theme, or null when it names no language with
    /// a grammar, or while the theme's grammars still load in the background (the code shows plain until
    /// <see cref="Loaded"/>; the UI thread never waits for the registry).
    /// </summary>
    public static CodeHighlighting? Create(HighlightTheme theme, string info)
    {
        if (!GrammarCatalog.IsLoaded(theme))
        {
            WarmUp(theme);
            return null;
        }

        EditorMemoryTrimmer.NotifyGrammarUsed();
        return CodeHighlighter.Create(theme, info) is { } highlighter ? new CodeHighlighting(highlighter, info) : null;
    }

    /// <summary>Loads a theme's grammars off the UI thread, once; <see cref="Loaded"/> follows on the UI thread.</summary>
    public static void WarmUp(HighlightTheme theme)
    {
        if (GrammarCatalog.IsLoaded(theme))
        {
            return;
        }

        lock (WarmingGate)
        {
            if (GrammarCatalog.IsLoaded(theme) || !Warming.Add(theme))
            {
                return;
            }
        }

        EditorMemoryTrimmer.NotifyGrammarUsed();
        var queue = Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread();
        _ = GrammarCatalog.WarmUpAsync(theme).ContinueWith(_ =>
        {
            lock (WarmingGate)
            {
                Warming.Remove(theme);
            }

            EditorMemoryTrimmer.NotifyGrammarUsed();
            if (queue is null || !queue.TryEnqueue(() => Loaded?.Invoke()))
            {
                Loaded?.Invoke();
            }
        }, TaskScheduler.Default);
    }

    /// <summary>The text with its colors; plain text when highlighting fails.</summary>
    public TextSource Build(string text)
    {
        var key = (_highlighter.Theme, _highlighter.Language, text);
        IReadOnlyList<HighlightSpan[]> lines;
        uint plain;
        if (Cache.TryGetValue(key, out var node))
        {
            Recent.Remove(node);
            Recent.AddFirst(node);
            lines = node.Value.Lines;
            plain = node.Value.DefaultForeground;
        }
        else
        {
            EditorMemoryTrimmer.NotifyGrammarUsed();
            try
            {
                lines = _highlighter.Highlight(text);
            }
            catch (Exception)
            {
                // A grammar that fails on some input (TextMateSharp, Oniguruma): the code shows without colors.
                return TextSource.Plain(text);
            }

            plain = _highlighter.DefaultForeground;
            AnalyzedLines += _highlighter.AnalyzedLines;
            Remember(key, lines, plain, text.Length);
        }

        return BuildSource(text, lines, plain);
    }

    private static TextSource BuildSource(string text, IReadOnlyList<HighlightSpan[]> lines, uint plain)
    {
        var builder = new TextSource.Builder();
        int lineStart = 0;
        for (int index = 0; index < lines.Count; index++)
        {
            int lineEnd = text.IndexOf('\n', lineStart);
            if (lineEnd < 0)
            {
                lineEnd = text.Length;
            }

            int position = lineStart;
            foreach (var span in lines[index])
            {
                int start = lineStart + span.Start;
                int end = Math.Min(start + span.Length, lineEnd);
                if (start >= end)
                {
                    continue;
                }

                builder.Append(text[position..start], default);
                builder.Append(text[start..end], HighlightStyles.StyleOf(span, plain));
                position = end;
            }

            // The rest of the line ('\r') and its break.
            int next = Math.Min(lineEnd + 1, text.Length);
            builder.Append(text[position..next], default);
            lineStart = next;
        }

        builder.Append(text[lineStart..], default);
        return builder.Build();
    }

    private static void Remember((HighlightTheme, string, string) key, IReadOnlyList<HighlightSpan[]> lines, uint defaultForeground, int charCount)
    {
        if (Cache.TryGetValue(key, out var existing))
        {
            Recent.Remove(existing);
            _cachedChars -= existing.Value.CharCount;
        }

        var node = Recent.AddFirst(new CacheEntry(key, lines, defaultForeground, charCount));
        Cache[key] = node;
        _cachedChars += charCount;
        while ((Recent.Count > CacheCapacity || (_cachedChars > MaxCacheChars && Recent.Count > 1)) && Recent.Last is { } last)
        {
            Recent.RemoveLast();
            Cache.Remove(last.Value.Key);
            _cachedChars -= last.Value.CharCount;
        }
    }

    private sealed record CacheEntry((HighlightTheme, string, string) Key, IReadOnlyList<HighlightSpan[]> Lines, uint DefaultForeground, int CharCount);
}
