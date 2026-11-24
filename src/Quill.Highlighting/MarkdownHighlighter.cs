using System.Diagnostics;
using TextMateSharp.Grammars;

namespace Quill.Highlighting;

/// <summary>
/// Incremental highlighting of Markdown source, one line at a time (11-source-mode.md). Each line keeps
/// the grammar state at its end and its colored runs. An edit marks lines from its first line; <see cref="Run"/>
/// analyzes them in order until a line ends in the state it had before (usually one or two lines), or the
/// time budget is spent, when the rest waits for the next call. Fenced code switches to its language: the
/// Markdown grammar embeds the common ones itself; for the others the <see cref="LanguageMap"/> finds a
/// grammar that analyzes the fence's lines on its own. Lines not analyzed yet have no runs.
/// </summary>
public sealed class MarkdownHighlighter
{
    private static readonly TimeSpan LineLimit = TimeSpan.FromMilliseconds(50);

    private readonly List<Line> _lines = [];
    private GrammarCatalog _catalog;
    private int _dirtyFrom = -1;
    private int _dirtyUntil = -1;

    public MarkdownHighlighter(HighlightTheme theme, IEnumerable<string>? lines = null)
    {
        _catalog = GrammarCatalog.For(theme);
        Reset(lines ?? []);
    }

    public HighlightTheme Theme => _catalog.Theme;

    public int LineCount => _lines.Count;

    /// <summary>Whether every line is analyzed.</summary>
    public bool IsComplete => _dirtyFrom < 0;

    /// <summary>The first line still to analyze, or -1.</summary>
    public int PendingFrom => _dirtyFrom;

    /// <summary>The color of text no rule colors.</summary>
    public uint DefaultForeground => _catalog.DefaultForeground;

    /// <summary>The runs of a line (provisional ones from <see cref="Preview"/> until analyzed), or null while it waits.</summary>
    public IReadOnlyList<HighlightSpan>? SpansOf(int line) => _lines[line].Spans ?? _lines[line].Provisional;

    /// <summary>Whether a line has runs from the analysis in order (not provisional).</summary>
    public bool IsAnalyzed(int line) => _lines[line].Spans is not null;

    /// <summary>
    /// Colors lines the analysis has not reached (the lines in view), as if a document started there; the
    /// analysis replaces them when it arrives. Returns the number of lines colored.
    /// </summary>
    public int Preview(int first, int count)
    {
        IStateStack? state = null;
        Fence? fence = null;
        IStateStack? embedded = null;
        int colored = 0;
        for (int index = first; index < Math.Min(_lines.Count, first + count); index++)
        {
            var line = _lines[index];
            var result = Tokenize(line.Text, state, fence, embedded);
            (state, fence, embedded) = (result.End, result.FenceAfter, result.EmbeddedEnd);
            if (line.Spans is null)
            {
                line.Provisional = result.Spans;
                colored++;
            }
        }

        return colored;
    }

    /// <summary>Starts over with new text: every line waits to be analyzed.</summary>
    public void Reset(IEnumerable<string> lines)
    {
        _lines.Clear();
        _lines.AddRange(lines.Select(static text => new Line(text)));
        MarkAll();
    }

    /// <summary>Another theme: the lines are analyzed and colored again.</summary>
    public void SetTheme(HighlightTheme theme)
    {
        if (theme == Theme)
        {
            return;
        }

        _catalog = GrammarCatalog.For(theme);
        foreach (var line in _lines)
        {
            line.Clear();
        }

        MarkAll();
    }

    /// <summary>Replaces <paramref name="removed"/> lines at <paramref name="start"/> with <paramref name="inserted"/>.</summary>
    public void Replace(int start, int removed, IReadOnlyList<string> inserted)
    {
        _lines.RemoveRange(start, removed);
        _lines.InsertRange(start, inserted.Select(static text => new Line(text)));
        int delta = inserted.Count - removed;
        int until = start + inserted.Count;
        if (_dirtyFrom >= 0)
        {
            _dirtyUntil = Math.Max(_dirtyUntil >= start + removed ? _dirtyUntil + delta : _dirtyUntil, until);
            _dirtyFrom = Math.Min(_dirtyFrom > start + removed ? _dirtyFrom + delta : _dirtyFrom, start);
        }
        else
        {
            _dirtyFrom = start;
            _dirtyUntil = until;
        }

        _dirtyFrom = Math.Min(_dirtyFrom, Math.Max(0, _lines.Count - 1));
        if (_lines.Count == 0)
        {
            _dirtyFrom = -1;
        }
    }

    /// <summary>
    /// Analyzes waiting lines for at most <paramref name="budget"/>; returns the range of lines whose runs
    /// were set (their views draw again), empty when nothing waited.
    /// </summary>
    public (int First, int Count) Run(TimeSpan budget)
    {
        if (_dirtyFrom < 0)
        {
            return (0, 0);
        }

        long start = Stopwatch.GetTimestamp();
        int first = _dirtyFrom;
        int index = first;
        while (index < _lines.Count)
        {
            bool settled = Analyze(index);
            index++;
            if (settled && index >= _dirtyUntil)
            {
                _dirtyFrom = -1;
                return (first, index - first);
            }

            if (Stopwatch.GetElapsedTime(start) >= budget)
            {
                break;
            }
        }

        _dirtyFrom = index < _lines.Count ? index : -1;
        return (first, index - first);
    }

    private void MarkAll()
    {
        _dirtyFrom = _lines.Count > 0 ? 0 : -1;
        _dirtyUntil = _lines.Count;
    }

    /// <summary>Analyzes one line from the state the line before ends in; true when its end state is the one it had.</summary>
    private bool Analyze(int index)
    {
        var line = _lines[index];
        var previous = index > 0 ? _lines[index - 1] : null;
        var result = Tokenize(line.Text, previous?.End, previous?.FenceAfter, previous?.EmbeddedEnd);
        bool settled = line.Spans is not null && Equals(line.End, result.End) && Equals(line.FenceAfter, result.FenceAfter) && Equals(line.EmbeddedEnd, result.EmbeddedEnd);
        line.End = result.End;
        line.FenceAfter = result.FenceAfter;
        line.EmbeddedEnd = result.EmbeddedEnd;
        line.Spans = result.Spans;
        line.Provisional = null;
        return settled;
    }

    /// <summary>One line from a start state: its runs and the states it leaves for the next line.</summary>
    private (HighlightSpan[] Spans, IStateStack End, Fence? FenceAfter, IStateStack? EmbeddedEnd) Tokenize(string text, IStateStack? start, Fence? fence, IStateStack? embeddedStart)
    {
        var markdown = _catalog.Markdown;
        var result = _catalog.TokenizeLine(markdown, text, start, LineLimit);

        bool inFence = false, embedded = false, delimiter = false;
        string? language = null;
        foreach (var token in result.Tokens)
        {
            foreach (string scope in token.Scopes)
            {
                if (scope.StartsWith("markup.fenced_code.block", StringComparison.Ordinal))
                {
                    inFence = true;
                }
                else if (scope.StartsWith("meta.embedded.block", StringComparison.Ordinal))
                {
                    embedded = true;
                }
                else if (scope.StartsWith("fenced_code.block.language", StringComparison.Ordinal))
                {
                    language = Slice(text, token.StartIndex, token.EndIndex);
                }
                else if (scope.StartsWith("punctuation.definition.markdown", StringComparison.Ordinal))
                {
                    delimiter = true;
                }
            }
        }

        // The fence this line leaves open for the next: opened here, kept, or closed.
        if (inFence && delimiter && fence is null)
        {
            var opened = new Fence(language is { } name && LanguageMap.LanguageOf(name) is { } id ? _catalog.Grammar(id) : null);
            return (_catalog.Spans(text, result.Tokens), result.RuleStack, opened, null);
        }

        if (inFence && delimiter && !embedded)
        {
            return (_catalog.Spans(text, result.Tokens), result.RuleStack, null, null);
        }

        if (inFence && !embedded && fence?.Grammar is { } grammar && grammar != markdown)
        {
            // Code the Markdown grammar does not embed: its own grammar, its state carried along the fence.
            var code = _catalog.TokenizeLine(grammar, text, embeddedStart, LineLimit);
            return (_catalog.Spans(text, code.Tokens), result.RuleStack, fence, code.RuleStack);
        }

        return (_catalog.Spans(text, result.Tokens), result.RuleStack, inFence ? fence : null, null);
    }

    private static string Slice(string text, int start, int end) =>
        text[Math.Min(start, text.Length)..Math.Min(end, text.Length)];

    /// <summary>An open fence: the grammar of its language when the Markdown grammar does not embed it.</summary>
    private sealed record Fence(IGrammar? Grammar);

    private sealed class Line(string text)
    {
        public string Text { get; } = text;

        public IStateStack? End { get; set; }

        public Fence? FenceAfter { get; set; }

        public IStateStack? EmbeddedEnd { get; set; }

        public HighlightSpan[]? Spans { get; set; }

        /// <summary>Runs from <see cref="Preview"/>, shown until the analysis reaches the line.</summary>
        public HighlightSpan[]? Provisional { get; set; }

        public void Clear()
        {
            End = null;
            FenceAfter = null;
            EmbeddedEnd = null;
            Spans = null;
            Provisional = null;
        }
    }
}
