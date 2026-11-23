using System.Collections.Immutable;
using Quill.Editor.Documents;
using Quill.Editor.Projection;
using Quill.Highlighting;

namespace Quill.Editor.WinUI.Rendering;

/// <summary>
/// The highlighting of a source document (11-source-mode.md): a <see cref="MarkdownHighlighter"/> kept in
/// step with the document's lines. <see cref="Sync"/> finds the lines an edit replaced (the unchanged
/// lines are the same objects at both ends); <see cref="Run"/> analyzes within a time budget and reports
/// the lines whose colors were set, by block key, for their views to draw again.
/// </summary>
public sealed class SourceHighlighting : IDisposable
{
    private readonly MarkdownHighlighter _highlighter;
    private IDisposable? _grammarLease;
    private List<string> _keys = [];
    private Dictionary<string, int> _index = new(StringComparer.Ordinal);
    private ImmutableList<Block> _lines = [];

    public SourceHighlighting(HighlightTheme theme, Document document)
    {
        _grammarLease = GrammarCatalog.AcquireLease();
        EditorMemoryTrimmer.NotifyGrammarUsed();
        _highlighter = new MarkdownHighlighter(theme);
        _lines = document.Children;
        _highlighter.Reset(_lines.Select(static block => ((LeafBlock)block).Text));
        Reindex();
    }

    public void Dispose()
    {
        _grammarLease?.Dispose();
        _grammarLease = null;
        EditorMemoryTrimmer.NotifyGrammarUsed();
    }

    public HighlightTheme Theme => _highlighter.Theme;

    public bool IsComplete => _highlighter.IsComplete;

    /// <summary>The color of text no rule colors (drawn in the style sheet's foreground instead).</summary>
    public uint DefaultForeground => _highlighter.DefaultForeground;

    /// <summary>The colored runs of the line with a block key, or null while it waits.</summary>
    public IReadOnlyList<HighlightSpan>? SpansOf(string key) =>
        _index.TryGetValue(key, out int line) && line < _highlighter.LineCount ? _highlighter.SpansOf(line) : null;

    public void SetTheme(HighlightTheme theme) => _highlighter.SetTheme(theme);

    /// <summary>Brings the lines in step with <paramref name="document"/>; cost proportional to the changed lines (and a pointer scan).</summary>
    public void Sync(Document document)
    {
        var lines = document.Children;
        if (ReferenceEquals(lines, _lines))
        {
            return;
        }

        int oldCount = _lines.Count, newCount = lines.Count;
        int limit = Math.Min(oldCount, newCount);
        int prefix = 0;
        foreach (var line in lines)
        {
            if (prefix == limit || !ReferenceEquals(line, _lines[prefix]))
            {
                break;
            }

            prefix++;
        }

        int suffix = 0;
        while (prefix + suffix < limit && ReferenceEquals(lines[newCount - 1 - suffix], _lines[oldCount - 1 - suffix]))
        {
            suffix++;
        }

        var inserted = lines.GetRange(prefix, newCount - prefix - suffix).Select(static block => ((LeafBlock)block).Text).ToList();
        _highlighter.Replace(prefix, oldCount - prefix - suffix, inserted);
        bool sameIds = oldCount == newCount && lines.GetRange(prefix, newCount - prefix - suffix).Select(static block => block.Id)
            .SequenceEqual(_lines.GetRange(prefix, oldCount - prefix - suffix).Select(static block => block.Id));
        _lines = lines;
        if (!sameIds)
        {
            Reindex();
        }
    }

    /// <summary>
    /// Colors the lines with these keys (the lines in view) ahead of the analysis when it has not reached
    /// them (11: the lines in view first); returns the keys colored.
    /// </summary>
    public IReadOnlyList<string> Preview(IEnumerable<string> keys)
    {
        var lines = keys.Select(key => _index.TryGetValue(key, out int line) ? line : -1).Where(line => line >= 0 && _highlighter.SpansOf(line) is null).ToList();
        if (lines.Count == 0)
        {
            return [];
        }

        int first = lines.Min(), last = lines.Max();
        _highlighter.Preview(first, last - first + 1);
        return _keys.GetRange(first, last - first + 1);
    }

    /// <summary>Analyzes for at most <paramref name="budget"/>; the keys of the lines whose colors were set.</summary>
    public IReadOnlyList<string> Run(TimeSpan budget)
    {
        var (first, count) = _highlighter.Run(budget);
        return count == 0 ? [] : _keys.GetRange(first, Math.Min(count, _keys.Count - first));
    }

    private void Reindex()
    {
        _keys = [.. _lines.Select(static block => DocumentProjector.KeyOf(block.Id))];
        _index = new Dictionary<string, int>(_keys.Count, StringComparer.Ordinal);
        for (int i = 0; i < _keys.Count; i++)
        {
            _index[_keys[i]] = i;
        }
    }
}
