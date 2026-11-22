using Quill.Editor.Commands;
using Quill.Editor.Documents;
using Quill.Editor.Editing;
using Quill.Editor.State;

namespace Quill.Editor.Input;

/// <summary>
/// <see cref="ITextInputClient"/> over an <see cref="EditorSession"/>. Every change goes through
/// <paramref name="execute"/> as a command, so the host renders after each (07, rule 9).
/// </summary>
public sealed class EditorTextInput : ITextInputClient
{
    private readonly EditorSession _session;
    private readonly Action<Command> _execute;
    private EditorState? _seen;
    private BlockId? _seenBlock;
    private string _seenText = string.Empty;
    private long _revision;

    public EditorTextInput(EditorSession session, Action<Command>? execute = null)
    {
        _session = session;
        _execute = execute ?? session.Execute;
    }

    public long Revision
    {
        get
        {
            if (!ReferenceEquals(_seen, _session.State))
            {
                _seen = _session.State;
                var block = ActiveBlock;
                string text = ActiveText;
                if (block != _seenBlock || !string.Equals(text, _seenText, StringComparison.Ordinal))
                {
                    _seenBlock = block;
                    _seenText = text;
                    _revision++;
                }
            }

            return _revision;
        }
    }

    public BlockId? ActiveBlock => _session.State.Selection.Head is TextPosition head ? head.Block : null;

    public string ActiveText => ActiveBlock is { } block && DocumentLookup.Find(_session.State.Document, block) is LeafBlock leaf ? leaf.Text : string.Empty;

    public (int Anchor, int Head) Selection
    {
        get
        {
            var selection = _session.State.Selection;
            if (selection.Head is not TextPosition head)
            {
                return (0, 0);
            }

            int anchor = selection.Anchor is TextPosition { } start && start.Block == head.Block ? start.Offset : head.Offset;
            return (anchor, head.Offset);
        }
    }

    public bool IsComposing => _session.State.Composition is not null;

    public bool ReplaceText(int start, int end, string text, int selectionStart, int selectionEnd)
    {
        var block = ActiveBlock;
        int length = ActiveText.Length;
        start = Math.Clamp(start, 0, length);
        end = Math.Clamp(end, start, length);
        if (IsComposing)
        {
            // A step of the composition; in a virtual paragraph the first one makes it a paragraph.
            _execute(new ReplaceRange(block ?? default, start, end, text, Math.Max(0, start + text.Length)));
            if (ActiveBlock is { } composed && (selectionStart != start + text.Length || selectionEnd != selectionStart))
            {
                Select(composed, selectionStart, selectionEnd);
            }

            return true;
        }

        if (block is { } leaf && (start != Selection.Anchor || end != Selection.Head))
        {
            Select(leaf, start, end);
        }

        if (text is "\r" or "\n" or "\r\n")
        {
            _execute(new SplitBlock());
        }
        else if (text.Length > 0)
        {
            _execute(new Commands.InsertText(text));
        }
        else if (end > start)
        {
            _execute(new DeleteBackward());
        }

        return true;
    }

    public void InsertText(string text) => _execute(new Commands.InsertText(text));

    public void SetSelection(int anchor, int head)
    {
        if (ActiveBlock is { } block)
        {
            Select(block, anchor, head);
        }
    }

    public void BeginComposition() => _execute(new CompositionStart());

    public void EndComposition() => _execute(new CompositionEnd(Cancel: _session.State.Composition is { Length: 0 }));

    private void Select(BlockId block, int anchor, int head)
    {
        int length = ActiveText.Length;
        _execute(new SetSelection(new TextSelection(new TextPosition(block, Math.Clamp(anchor, 0, length)), new TextPosition(block, Math.Clamp(head, 0, length)))));
    }
}
