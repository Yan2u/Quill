using System.Collections.Immutable;
using System.Text;
using Quill.Editor.Documents;
using Quill.Editor.Documents.Syntax;

namespace Quill.Editor.Markdown.Export;

/// <summary>
/// Writes physical lines. Blocks write line bodies; each line's prefix is composed from the active
/// <see cref="PrefixScope"/>s, inside out, once the line is complete. Lines are joined with "\n".
/// Optionally records where each block starts in the output.
/// </summary>
internal sealed class MarkdownWriter
{
    private readonly StringBuilder _output = new();
    private readonly StringBuilder _body = new();
    private readonly List<PrefixScope> _scopes = [];
    private readonly HashSet<PrefixScope> _pendingOpenings = new(ReferenceEqualityComparer.Instance);
    private readonly List<(BlockId Block, int Depth)> _pendingStarts = [];
    private readonly List<(BlockId Block, int Depth)> _lineStarts = [];
    private readonly Dictionary<BlockId, int>? _blockStarts;
    private PrefixScope[] _lineScopes = [];
    private bool[] _lineOpenings = [];
    private LinePrefix _lineStored;
    private bool _lineKeepsIndentation;
    private bool _lineOpen;
    private bool _wroteLine;
    private (string Text, PrefixScope[] Scopes)? _openFence;

    public MarkdownWriter(Dictionary<BlockId, int>? blockStarts = null)
    {
        _blockStarts = blockStarts;
    }

    public void PushScope(PrefixScope scope)
    {
        _scopes.Add(scope);
        if (scope.HasOpening)
        {
            _pendingOpenings.Add(scope);
        }
    }

    public void PopScope()
    {
        _pendingOpenings.Remove(_scopes[^1]);
        _scopes.RemoveAt(_scopes.Count - 1);
    }

    /// <summary>Whether the next line is the first line of the document, outside any container.</summary>
    public bool IsAtDocumentStart => !_wroteLine && !_lineOpen && _scopes.Count == 0;

    /// <summary>The column where content starts inside the open containers, with generated prefixes.</summary>
    public int ContentColumn() => _scopes.Sum(static scope => scope switch
    {
        ListItemScope item => item.ContentColumns,
        QuoteScope => 2,
        FootnoteScope => 4,
        _ => 0,
    });

    /// <summary>The bullet of the innermost list item whose marker the next line will carry, if any.</summary>
    public char? PendingItemBullet()
    {
        for (int i = _scopes.Count - 1; i >= 0; i--)
        {
            if (_scopes[i] is ListItemScope item && _pendingOpenings.Contains(item))
            {
                return item.Bullet;
            }
        }

        return null;
    }

    /// <summary>
    /// Records that a block starts on the next line begun, after the prefixes of the scopes active
    /// now (a block's own prefix, such as a list item's marker, belongs to the block).
    /// </summary>
    public void MarkBlockStart(BlockId block)
    {
        if (_blockStarts is not null)
        {
            _pendingStarts.Add((block, _scopes.Count));
        }
    }

    /// <summary>Starts a physical line whose recorded prefixes are <paramref name="stored"/>.</summary>
    /// <param name="keepIndentation">Write the containers' full indentation even if the line stays empty.</param>
    public void BeginLine(LinePrefix stored, bool keepIndentation = false)
    {
        FlushLine();
        _lineScopes = [.. _scopes];
        _lineOpenings = new bool[_lineScopes.Length];
        for (int i = 0; i < _lineScopes.Length; i++)
        {
            _lineOpenings[i] = _pendingOpenings.Remove(_lineScopes[i]);
        }

        _lineStarts.AddRange(_pendingStarts);
        _pendingStarts.Clear();
        _lineStored = stored;
        _lineKeepsIndentation = keepIndentation;
        _lineOpen = true;
    }

    /// <summary>
    /// Leaves a fence open, as the source did at the end of its container (06: unedited content is
    /// reproduced exactly). If the next line written is blank and still inside every quote around the
    /// fence, it would become part of the code (blank lines do not end a list item), so the closing
    /// line <paramref name="text"/> is written before it.
    /// </summary>
    public void DeferClosingFence(string text)
    {
        FlushLine();
        _openFence = (text, [.. _scopes]);
    }

    /// <summary>Appends text without line breaks to the current line.</summary>
    public void Write(ReadOnlySpan<char> text) => _body.Append(text);

    /// <summary>
    /// Appends text that may contain "\n"; each break starts a new line whose recorded prefix is
    /// <paramref name="prefixes"/>[k] for the k-th break.
    /// </summary>
    public void WriteLines(string text, ImmutableArray<LinePrefix> prefixes)
    {
        int start = 0;
        int lineIndex = 0;
        while (true)
        {
            int newline = text.IndexOf('\n', start);
            if (newline < 0)
            {
                _body.Append(text, start, text.Length - start);
                return;
            }

            _body.Append(text, start, newline - start);
            lineIndex++;
            BeginLine(lineIndex < prefixes.Length ? prefixes[lineIndex] : LinePrefix.None);
            start = newline + 1;
        }
    }

    public string Finish(bool endsWithLineBreak)
    {
        FlushLine();
        _openFence = null;
        if (endsWithLineBreak)
        {
            _output.Append('\n');
        }

        return _output.ToString();
    }

    private void FlushLine()
    {
        if (!_lineOpen)
        {
            return;
        }

        string body = _body.ToString();
        if (_openFence is { } fence)
        {
            // Blank for the fence's container: whitespace after the prefixes of the same quotes that
            // hold the fence, with no marker opening a container (a new quote's ">" is content).
            _openFence = null;
            var fenceQuotes = fence.Scopes.OfType<QuoteScope>().ToHashSet();
            var lineQuotes = _lineScopes.OfType<QuoteScope>().ToHashSet();
            bool blank = string.IsNullOrWhiteSpace(body) && !_lineOpenings.Contains(true) && fenceQuotes.SetEquals(lineQuotes);
            if (blank)
            {
                AppendLine(Compose(fence.Text, fence.Scopes, new bool[fence.Scopes.Length], LinePrefix.None, keepIndentation: false, out _));
            }
        }

        string line = Compose(body, _lineScopes, _lineOpenings, _lineStored, _lineKeepsIndentation, out var prefixLengths);
        if (_blockStarts is not null)
        {
            foreach (var (block, depth) in _lineStarts)
            {
                int offset = _output.Length + (_wroteLine ? 1 : 0);
                for (int i = 0; i < depth && i < prefixLengths.Length; i++)
                {
                    offset += prefixLengths[i];
                }

                _blockStarts.TryAdd(block, offset);
            }
        }

        _lineStarts.Clear();
        AppendLine(line);
        _body.Clear();
        _lineOpen = false;
    }

    private void AppendLine(string line)
    {
        if (_wroteLine)
        {
            _output.Append('\n');
        }

        _output.Append(line);
        _wroteLine = true;
    }

    /// <summary>Puts the prefixes of the scopes in front of a line body, inside out.</summary>
    private static string Compose(string body, PrefixScope[] scopes, bool[] openings, LinePrefix stored, bool keepIndentation, out int[] prefixLengths)
    {
        prefixLengths = new int[scopes.Length];
        int markerScope = -1;
        for (int i = 0; i < scopes.Length && markerScope < 0; i++)
        {
            if (scopes[i] is ListItemScope && openings[i])
            {
                markerScope = i;
            }
        }

        string rest = body;
        for (int i = scopes.Length - 1; i >= 0; i--)
        {
            bool afterItemMarker = markerScope >= 0 && markerScope < i;
            string prefix = scopes[i].Prefix(stored, openings[i], rest, keepIndentation, afterItemMarker);
            prefixLengths[i] = prefix.Length;
            rest = prefix + rest;
        }

        return rest;
    }
}
