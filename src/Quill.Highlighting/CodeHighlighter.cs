using TextMateSharp.Grammars;

namespace Quill.Highlighting;

/// <summary>
/// The highlighting of one code block in the editing mode (M6.3): its lines in the grammar of its
/// language, kept from one call to the next. <see cref="Highlight"/> takes the block's new text and
/// analyzes from the first changed line until a line ends in the state it had before (usually the
/// edited line alone); the other lines keep their runs. Used on one thread.
/// </summary>
public sealed class CodeHighlighter
{
    private static readonly TimeSpan LineLimit = TimeSpan.FromMilliseconds(50);

    private readonly GrammarCatalog _catalog;
    private readonly IGrammar _grammar;
    private Line[] _lines = [];

    private CodeHighlighter(GrammarCatalog catalog, IGrammar grammar, string language)
    {
        _catalog = catalog;
        _grammar = grammar;
        Language = language;
    }

    public HighlightTheme Theme => _catalog.Theme;

    /// <summary>The language ID (after <see cref="LanguageMap"/>).</summary>
    public string Language { get; }

    /// <summary>The color of text no rule colors.</summary>
    public uint DefaultForeground => _catalog.DefaultForeground;

    /// <summary>How many lines the last <see cref="Highlight"/> analyzed (the others were kept), for tests.</summary>
    public int AnalyzedLines { get; private set; }

    /// <summary>A highlighter for the language an info string names, or null when it names none or no grammar ships for it.</summary>
    public static CodeHighlighter? Create(HighlightTheme theme, string info)
    {
        if (LanguageMap.LanguageOf(info) is not { } language)
        {
            return null;
        }

        var catalog = GrammarCatalog.For(theme);
        return catalog.Grammar(language) is { } grammar ? new CodeHighlighter(catalog, grammar, language) : null;
    }

    /// <summary>
    /// The runs of each line of <paramref name="text"/> (lines split at '\n'; a '\r' before it belongs to
    /// no run). Runs cover their lines without gaps.
    /// </summary>
    public IReadOnlyList<HighlightSpan[]> Highlight(string text)
    {
        string[] texts = text.Split('\n');
        var old = _lines;
        int limit = Math.Min(old.Length, texts.Length);
        int prefix = 0;
        while (prefix < limit && old[prefix].Text == TrimReturn(texts[prefix]))
        {
            prefix++;
        }

        int suffix = 0;
        while (suffix < limit - prefix && old[^(suffix + 1)].Text == TrimReturn(texts[^(suffix + 1)]))
        {
            suffix++;
        }

        var lines = new Line[texts.Length];
        Array.Copy(old, lines, prefix);
        int delta = texts.Length - old.Length;
        int analyzed = 0;
        for (int i = prefix; i < texts.Length; i++)
        {
            IStateStack? before = i > 0 ? lines[i - 1].End : null;

            // An unchanged line at the end that starts in the state it started in before: it and the
            // lines after it keep their runs.
            int was = i - delta;
            if (i >= texts.Length - suffix && Equals(before, was > 0 ? old[was - 1].End : null))
            {
                Array.Copy(old, was, lines, i, texts.Length - i);
                break;
            }

            string line = TrimReturn(texts[i]);
            var result = _grammar.TokenizeLine(line, before, LineLimit);
            lines[i] = new Line(line, result.RuleStack, _catalog.Spans(line, result.Tokens));
            analyzed++;
        }

        _lines = lines;
        AnalyzedLines = analyzed;
        return Array.ConvertAll(lines, static line => line.Spans);
    }

    private static string TrimReturn(string line) => line.EndsWith('\r') ? line[..^1] : line;

    private sealed record Line(string Text, IStateStack End, HighlightSpan[] Spans);
}
