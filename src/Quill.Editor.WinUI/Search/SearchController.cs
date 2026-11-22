using Microsoft.UI.Dispatching;
using Quill.Editor.Commands;
using Quill.Editor.Documents;
using Quill.Editor.Search;
using Quill.Editor.State;
using Quill.Editor.WinUI.Elements;
using Quill.Editor.WinUI.Layout;

namespace Quill.Editor.WinUI.Search;

/// <summary>
/// Find and replace in a <see cref="MarkdownBox"/> (12-find-replace.md): the query, jumping between
/// matches, replacing, and the matches each realized block draws (in display offsets, through the
/// block's PositionMap; the current match is the one the selection is). The number of matches is
/// counted when the query changes, after a jump or a replacement, and 200 ms after other changes.
/// </summary>
public sealed class SearchController : IMatchSource
{
    private static readonly TimeSpan RecountDelay = TimeSpan.FromMilliseconds(200);

    private readonly MarkdownBox _box;
    private DispatcherQueueTimer? _recount;
    private DocumentSearch? _search;
    private Position? _origin;
    private int _countVersion;

    internal SearchController(MarkdownBox box)
    {
        _box = box;
    }

    /// <summary>The count, the current number or the error changed.</summary>
    public event EventHandler? Changed;

    /// <summary>The query in use; null while nothing is searched (an empty text, an invalid expression, the panel closed).</summary>
    public SearchQuery? Query => _search?.Query;

    /// <summary>Why the expression is invalid, or null.</summary>
    public string? Error { get; private set; }

    public int Count { get; private set; }

    /// <summary>The 1-based number of the match the selection is; 0 when it is none.</summary>
    public int Current { get; private set; }

    /// <summary>Incremental search starts from here: the start of the selection when the panel opens.</summary>
    public void Begin()
    {
        _origin = _box.HasDocument ? PositionOrder.Ordered(_box.State.Document, _box.State.Selection).Start : null;
    }

    /// <summary>A new query: the matches are drawn; with <paramref name="selectFirst"/> the first one from where the search began is selected.</summary>
    public void SetQuery(SearchQuery? query, bool selectFirst = true)
    {
        string? error = null;
        _search = query is null ? null : DocumentSearch.TryCreate(query, out error);
        Error = error;
        if (_search is not null && selectFirst && _box.HasDocument)
        {
            var from = _origin is { } origin && Holds(_box.State.Document, origin) ? origin : PositionOrder.Ordered(_box.State.Document, _box.State.Selection).Start;
            if (_search.Next(_box.State.Document, TextSelection.Caret(from), forward: true) is { } match)
            {
                Select(match);
            }
        }

        Recount();
        Redraw();
    }

    /// <summary>F3, Shift+F3, Enter in the panel: the next (previous) match; false when there is none.</summary>
    public bool FindNext(bool forward)
    {
        if (_search is null || !_box.HasDocument || _search.Next(_box.State.Document, _box.State.Selection, forward) is not { } match)
        {
            Recount();
            return false;
        }

        Select(match);
        Recount();
        return true;
    }

    /// <summary>Replaces the selected match (when the selection is one), then goes to the next match.</summary>
    public void ReplaceCurrent(string replacement)
    {
        if (_search is null || !_box.HasDocument)
        {
            return;
        }

        if (_search.At(_box.State.Document, _box.State.Selection) is not null)
        {
            _box.Execute(new ReplaceMatch(_search.Query, replacement));
        }

        FindNext(forward: true);
    }

    /// <summary>Replaces every match, one undo item; returns how many were replaced.</summary>
    public int ReplaceAll(string replacement)
    {
        if (_search is null || !_box.HasDocument)
        {
            return 0;
        }

        int count = _search.All(_box.State.Document).Count();
        if (count > 0)
        {
            _box.Execute(new ReplaceAllMatches(_search.Query, replacement));
        }

        Recount();
        return count;
    }

    /// <summary>The panel closed: nothing is searched or drawn.</summary>
    public void Clear()
    {
        _recount?.Stop();
        _countVersion++;
        _search = null;
        _origin = null;
        Error = null;
        Count = 0;
        Current = 0;
        Redraw();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public IReadOnlyList<(int Start, int End, bool Current)>? MarksFor(string blockKey)
    {
        if (_search is null || !_box.HasDocument || BlockGeometry.ParseKey(blockKey) is not { } id
            || DocumentLookup.Find(_box.State.Document, id) is not LeafBlock leaf || _box.CurrentProjection?.Map[id] is not { } map)
        {
            return null;
        }

        var matches = _search.MatchesIn(leaf);
        if (matches.Count == 0)
        {
            return null;
        }

        var current = _search.At(_box.State.Document, _box.State.Selection);
        var marks = new List<(int, int, bool)>(matches.Count);
        foreach (var range in matches)
        {
            int start = map.ToDisplay(range.Start, Affinity.Forward);
            int end = map.ToDisplay(range.End, Affinity.Backward);

            // A match in hidden syntax (a collapsed link's URL, an inactive formula) shows once the element is active.
            if (end > start)
            {
                marks.Add((start, end, current is { } selected && selected.Block == id && selected.Start == range.Start));
            }
        }

        return marks;
    }

    /// <summary>After a command: the count follows a moment later (not on every key, 09).</summary>
    internal void OnStateChanged()
    {
        if (_search is not null)
        {
            if (_recount is null)
            {
                _recount = DispatcherQueue.GetForCurrentThread().CreateTimer();
                _recount.Interval = RecountDelay;
                _recount.IsRepeating = false;
                _recount.Tick += OnRecountTick;
            }

            _recount.Stop();
            _recount.Start();
        }
    }

    internal void DetachTimer()
    {
        if (_recount is { } timer)
        {
            timer.Stop();
            timer.Tick -= OnRecountTick;
            _recount = null;
        }
    }

    private void OnRecountTick(DispatcherQueueTimer sender, object args) => Recount();

    /// <summary>Another document is shown (a new file, the source mode): count and draw for it now.</summary>
    internal void OnDocumentReplaced()
    {
        // The start of an incremental search belongs to the document that was shown.
        _origin = null;
        if (_search is not null)
        {
            Recount();
            Redraw();
        }
    }

    private static bool Holds(Document document, Position position) => position switch
    {
        TextPosition text => DocumentLookup.Find(document, text.Block) is not null,
        BoundaryPosition boundary => DocumentLookup.Find(document, boundary.Parent) is not null,
        _ => false,
    };

    private void Select(SearchMatch match) =>
        _box.Execute(new SetSelection(new TextSelection(new TextPosition(match.Block, match.Start), new TextPosition(match.Block, match.End, Affinity.Backward)), Exact: true));

    /// <summary>
    /// Counts on a background thread (the document is immutable, the leaf cache thread-safe): a new query
    /// on a long document searches every leaf once. The result is shown only if nothing changed meanwhile.
    /// </summary>
    private void Recount()
    {
        _recount?.Stop();
        int version = ++_countVersion;
        if (_search is not { } search || !_box.HasDocument)
        {
            (Count, Current) = (0, 0);
            Changed?.Invoke(this, EventArgs.Empty);
            return;
        }

        var document = _box.State.Document;
        var selection = _box.State.Selection;
        var dispatcher = DispatcherQueue.GetForCurrentThread();
        _ = Task.Run(() => search.Count(document, selection)).ContinueWith(task => dispatcher.TryEnqueue(() =>
        {
            if (version == _countVersion && task.IsCompletedSuccessfully)
            {
                (Count, Current) = task.Result;
                Changed?.Invoke(this, EventArgs.Empty);
            }
        }), TaskScheduler.Default);
    }

    private void Redraw() => _box.Document?.RedrawMatches();
}
