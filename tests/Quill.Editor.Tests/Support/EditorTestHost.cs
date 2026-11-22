using System.Globalization;
using Quill.Editor.Behaviors;
using Quill.Editor.Commands;
using Quill.Editor.Documents;
using Quill.Editor.Editing;
using Quill.Editor.Input;
using Quill.Editor.Services;
using Quill.Editor.State;

namespace Quill.Editor.Tests.Support;

/// <summary>
/// The mock host of 08-testing-and-debugging.md: an editing session with sequential IDs, a manual
/// clock and the fake layout, driven by key chords and typed text, with the state read back as DSL.
/// No window, no system input.
/// </summary>
public sealed class EditorTestHost
{
    private readonly IReadOnlyDictionary<string, BlockId> _names;

    private EditorTestHost(string dsl, EditorOptions options, int columns)
    {
        Ids = new SequentialBlockIdSource();
        Clock = new ManualClock();
        var loaded = StateDsl.Load(dsl, Ids);
        _names = loaded.Names;
        EditorSession? session = null;
        Layout = new FakeLayoutService(() => session!.State.Document, columns);
        Session = session = new EditorSession(loaded.State, new EditorServices { Ids = Ids, Clock = Clock, Layout = Layout, Options = options });
    }

    public EditorSession Session { get; }

    public EditorState State => Session.State;

    public SequentialBlockIdSource Ids { get; }

    public ManualClock Clock { get; }

    public FakeLayoutService Layout { get; }

    public KeyMap KeyMap { get; set; } = KeyMap.Default;

    /// <summary>The content of the fake system clipboard.</summary>
    public ClipboardContent? Clipboard { get; set; }

    public static EditorTestHost Load(string dsl, EditorOptions? options = null, int columns = 80) =>
        new(dsl, options ?? EditorOptions.Default, columns);

    /// <summary>The ID of a block named with <c>&lt;#name&gt;</c> in the loaded DSL.</summary>
    public BlockId Id(string name) => _names[name];

    public string Dump() => StateDsl.Dump(Session.State, _names, Session.Services.Options);

    public Quill.Editor.Projection.ProjectionResult Project() => Session.Project();

    /// <summary>Presses key chords in turn, separated by spaces: <c>Press("Enter")</c>, <c>Press("Shift+Tab Enter")</c>.</summary>
    public EditorTestHost Press(string chords)
    {
        foreach (var text in chords.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            var chord = KeyChord.Parse(text);
            switch (KeyMap.Find(chord))
            {
                case CommandBinding command:
                    Session.Execute(command.Command);
                    break;
                case ClipboardBinding clipboard:
                    RunClipboard(clipboard.Action);
                    break;
                default:
                    throw new InvalidOperationException($"No binding for {chord}.");
            }
        }

        return this;
    }

    /// <summary>Types text one grapheme cluster at a time, as keystrokes would.</summary>
    public EditorTestHost Type(string text)
    {
        var graphemes = StringInfo.GetTextElementEnumerator(text);
        while (graphemes.MoveNext())
        {
            Session.Execute(new InsertText(graphemes.GetTextElement()));
        }

        return this;
    }

    public EditorTestHost Execute(Command command)
    {
        Session.Execute(command);
        return this;
    }

    public EditorTestHost Advance(TimeSpan time)
    {
        Clock.Advance(time);
        return this;
    }

    /// <summary>Runs a clipboard action that has no key, as its menu entry does (Copy as plain text).</summary>
    public EditorTestHost Run(ClipboardAction action)
    {
        RunClipboard(action);
        return this;
    }

    private void RunClipboard(ClipboardAction action)
    {
        switch (action)
        {
            case ClipboardAction.Copy:
                Clipboard = Session.Copy();
                break;
            case ClipboardAction.CopyAsMarkdown:
                Clipboard = Session.CopyAsMarkdown();
                break;
            case ClipboardAction.CopyAsPlainText:
                Clipboard = Session.CopyAsPlainText();
                break;
            case ClipboardAction.Cut:
                Clipboard = Session.Copy();
                Session.Execute(new Cut());
                break;
            case ClipboardAction.Paste when Clipboard is not null:
                Session.Execute(new Paste(Clipboard));
                break;
            case ClipboardAction.PasteAsPlainText when Clipboard is not null:
                Session.Execute(new PasteAsPlainText(Clipboard.PlainText));
                break;
        }
    }
}

/// <summary>A clock that only moves when told to.</summary>
public sealed class ManualClock : IClock
{
    public DateTimeOffset Now { get; private set; } = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    public void Advance(TimeSpan time) => Now += time;
}
