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
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<HighlightTheme, Lazy<GrammarCatalog>> Catalogs = new();

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

    /// <summary>The catalog of a theme, created now (on this thread) if it is not yet.</summary>
    public static GrammarCatalog For(HighlightTheme theme) => Entry(theme).Value;

    /// <summary>Whether the catalog of a theme exists, so <see cref="For"/> does not wait for it.</summary>
    public static bool IsLoaded(HighlightTheme theme) => Entry(theme).IsValueCreated;

    /// <summary>Creates the catalog of a theme on a background thread.</summary>
    public static Task WarmUpAsync(HighlightTheme theme) => Task.Run(() => For(theme));

    private static Lazy<GrammarCatalog> Entry(HighlightTheme theme) =>
        Catalogs.GetOrAdd(theme, static key => new Lazy<GrammarCatalog>(() => new GrammarCatalog(key), LazyThreadSafetyMode.ExecutionAndPublication));

    /// <summary>The Markdown grammar.</summary>
    public IGrammar Markdown => Grammar("markdown") ?? throw new InvalidOperationException("The Markdown grammar is missing.");

    /// <summary>The grammar of a language ID (after <see cref="LanguageMap"/>), or null when none ships for it.</summary>
    public IGrammar? Grammar(string language)
    {
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

    /// <summary>The color and style of a token from its scopes, innermost last.</summary>
    public (uint Foreground, HighlightFontStyle Style) StyleOf(IList<string> scopes)
    {
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
}
