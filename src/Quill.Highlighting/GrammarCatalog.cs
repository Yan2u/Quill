using System.Globalization;
using TextMateSharp.Grammars;
using TextMateSharp.Registry;
using TextMateSharp.Themes;

namespace Quill.Highlighting;

/// <summary>
/// The grammars and theme colors of one theme (Light+ or Dark+), shared by every highlighter of that
/// theme: grammars load on first use, the color of a scope list is computed once. A catalog can be
/// created on a background thread (<see cref="WarmUpAsync"/>, about 100 ms for the registry and theme);
/// afterwards it is used on one thread.
/// </summary>
public sealed class GrammarCatalog
{
    private static readonly Lock CatalogGate = new();
    private static readonly Dictionary<HighlightTheme, Lazy<GrammarCatalog>> Catalogs = [];
    private static int _activeLeases;
    private static long _lastUsedTick = Environment.TickCount64;

    private readonly RegistryOptions _options;
    private readonly Registry _registry;
    private readonly Theme _theme;
    private readonly Lock _sync = new();
    private readonly Dictionary<string, IGrammar?> _grammars = new(StringComparer.Ordinal);
    private readonly Dictionary<string, (uint Foreground, HighlightFontStyle Style)> _styles = new(StringComparer.Ordinal);

    private GrammarCatalog(HighlightTheme theme)
    {
        Theme = theme;
        _options = new RegistryOptions(theme == HighlightTheme.Dark ? ThemeName.DarkPlus : ThemeName.LightPlus);
        _registry = new Registry(_options);
        _theme = _registry.GetTheme();
        DefaultForeground = Parse(_theme.GetGuiColorDictionary().TryGetValue("editor.foreground", out var color) ? color : null)
            ?? (theme == HighlightTheme.Dark ? 0xFFD4D4D4 : 0xFF000000);
    }

    public HighlightTheme Theme { get; }

    /// <summary>The color of text no rule colors.</summary>
    public uint DefaultForeground { get; }

    /// <summary>Whether any theme catalog is currently initialized in memory.</summary>
    public static bool HasLoadedCatalogs
    {
        get
        {
            lock (CatalogGate)
            {
                foreach (var entry in Catalogs.Values)
                {
                    if (entry.IsValueCreated)
                    {
                        return true;
                    }
                }

                return false;
            }
        }
    }

    /// <summary>The catalog of a theme, created now (on this thread) if it is not yet.</summary>
    public static GrammarCatalog For(HighlightTheme theme)
    {
        Touch();
        return Entry(theme).Value;
    }

    /// <summary>Whether the catalog of a theme exists, so <see cref="For"/> does not wait for it.</summary>
    public static bool IsLoaded(HighlightTheme theme)
    {
        lock (CatalogGate)
        {
            return Catalogs.TryGetValue(theme, out var entry) && entry.IsValueCreated;
        }
    }

    /// <summary>Creates the catalog of a theme on a background thread.</summary>
    public static Task WarmUpAsync(HighlightTheme theme)
    {
        Touch();
        return Task.Run(() =>
        {
            var catalog = For(theme);
            Touch();
            return catalog;
        });
    }

    /// <summary>
    /// Pins loaded grammar catalogs while an active highlighting session (focused code block or source mode)
    /// relies on incremental line tokenization state stacks.
    /// </summary>
    public static IDisposable AcquireLease()
    {
        Interlocked.Increment(ref _activeLeases);
        Touch();
        return new Lease();
    }

    /// <summary>
    /// Drops cached grammar registries when no active lease exists and catalogs have been idle for at
    /// least <paramref name="minIdle"/>, allowing managed and Oniguruma native heaps to be reclaimed.
    /// </summary>
    public static bool ReleaseIfUnused(TimeSpan minIdle)
    {
        lock (CatalogGate)
        {
            if (Volatile.Read(ref _activeLeases) > 0 || Catalogs.Count == 0)
            {
                return false;
            }

            if (minIdle > TimeSpan.Zero)
            {
                long elapsedMs = Environment.TickCount64 - Volatile.Read(ref _lastUsedTick);
                if (elapsedMs < (long)minIdle.TotalMilliseconds)
                {
                    return false;
                }
            }

            Catalogs.Clear();
            return true;
        }
    }

    /// <summary>Immediately drops all unleased grammar catalogs.</summary>
    public static bool ReleaseAll() => ReleaseIfUnused(TimeSpan.Zero);

    private static void Touch() => Volatile.Write(ref _lastUsedTick, Environment.TickCount64);

    private static Lazy<GrammarCatalog> Entry(HighlightTheme theme)
    {
        Touch();
        lock (CatalogGate)
        {
            if (!Catalogs.TryGetValue(theme, out var entry))
            {
                entry = new Lazy<GrammarCatalog>(() => new GrammarCatalog(theme), LazyThreadSafetyMode.ExecutionAndPublication);
                Catalogs[theme] = entry;
            }

            return entry;
        }
    }

    /// <summary>The Markdown grammar.</summary>
    public IGrammar Markdown => Grammar("markdown") ?? throw new InvalidOperationException("The Markdown grammar is missing.");

    /// <summary>The grammar of a language ID (after <see cref="LanguageMap"/>), or null when none ships for it.</summary>
    public IGrammar? Grammar(string language)
    {
        Touch();
        lock (_sync)
        {
            if (_grammars.TryGetValue(language, out var known))
            {
                return known;
            }

            string? scope = _options.GetScopeByLanguageId(language) ?? _options.GetScopeByExtension("." + language);
            IGrammar? grammar = null;
            if (scope is not null)
            {
                try
                {
                    grammar = _registry.LoadGrammar(scope);
                }
                catch (Exception)
                {
                    // A broken or missing grammar file: the code shows without colors.
                    grammar = null;
                }
            }

            _grammars[language] = grammar;
            return grammar;
        }
    }

    /// <summary>Tokenizes one line under this catalog's synchronization lock for thread-safe background highlighting.</summary>
    internal ITokenizeLineResult TokenizeLine(IGrammar grammar, string line, IStateStack? ruleStack, TimeSpan timeLimit)
    {
        Touch();
        lock (_sync)
        {
            return grammar.TokenizeLine(line, ruleStack, timeLimit);
        }
    }

    /// <summary>The color and style of a token from its scopes, innermost last.</summary>
    public (uint Foreground, HighlightFontStyle Style) StyleOf(IList<string> scopes)
    {
        Touch();
        lock (_sync)
        {
            string key = string.Join(' ', scopes);
            if (_styles.TryGetValue(key, out var style))
            {
                return style;
            }

            uint foreground = DefaultForeground;
            var fontStyle = HighlightFontStyle.None;
            bool colored = false, styled = false;
            foreach (var rule in _theme.Match(scopes))
            {
                if (!colored && rule.foreground > 0 && Parse(_theme.GetColor(rule.foreground)) is { } color)
                {
                    foreground = color;
                    colored = true;
                }

                if (!styled && rule.fontStyle != FontStyle.NotSet)
                {
                    fontStyle = (HighlightFontStyle)(int)rule.fontStyle;
                    styled = true;
                }
            }

            style = (foreground, fontStyle);
            _styles[key] = style;
            return style;
        }
    }

    /// <summary>
    /// The colored runs of a tokenized line: covering it without gaps, neighbors of one style merged. A
    /// line the grammar stopped on early (its time limit) ends in one plain run.
    /// </summary>
    internal HighlightSpan[] Spans(string text, IToken[] tokens)
    {
        var spans = new List<HighlightSpan>(tokens.Length);
        foreach (var token in tokens)
        {
            int start = Math.Min(token.StartIndex, text.Length);
            int end = Math.Min(token.EndIndex, text.Length);
            if (end <= start)
            {
                continue;
            }

            var (foreground, style) = StyleOf(token.Scopes);
            if (spans.Count > 0 && spans[^1] is var last && last.Foreground == foreground && last.Style == style && last.Start + last.Length == start)
            {
                spans[^1] = last with { Length = last.Length + end - start };
            }
            else
            {
                spans.Add(new HighlightSpan(start, end - start, foreground, style));
            }
        }

        int covered = spans.Count > 0 ? spans[^1].Start + spans[^1].Length : 0;
        if (covered < text.Length)
        {
            spans.Add(new HighlightSpan(covered, text.Length - covered, DefaultForeground, HighlightFontStyle.None));
        }

        return [.. spans];
    }

    /// <summary>"#RRGGBB" or "#RRGGBBAA" as 0xAARRGGBB.</summary>
    private static uint? Parse(string? color)
    {
        if (color is null || color.Length is not (7 or 9) || color[0] != '#'
            || !uint.TryParse(color.AsSpan(1, 6), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out uint rgb))
        {
            return null;
        }

        uint alpha = color.Length == 9 && uint.TryParse(color.AsSpan(7, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out uint a) ? a : 0xFF;
        return (alpha << 24) | rgb;
    }

    private sealed class Lease : IDisposable
    {
        private int _disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
            {
                Interlocked.Decrement(ref _activeLeases);
                Touch();
            }
        }
    }
}
