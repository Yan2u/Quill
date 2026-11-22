using Microsoft.UI.Xaml;
using Quill.Editor.Commands;
using Quill.Editor.Documents;
using Quill.Editor.Editing;
using Quill.Editor.Projection;
using Quill.Editor.State;
using Quill.Editor.WinUI.Elements;
using Quill.Editor.WinUI.Layout;
using Quill.VariableXaml;

namespace Quill.Playground.SelfTest;

/// <summary>
/// An editor session rendered into a <see cref="VariableXamlRoot"/> with the real layout service:
/// every query first projects the current state and completes layout (08). The layer-2 tests drive
/// it with commands, never with system input.
/// </summary>
internal sealed class RenderedSession
{
    private EditorState? _projected;

    public RenderedSession(VariableXamlRoot root, EditorState state)
    {
        Root = root;
        Layout = new XamlLayoutService(() => Root.Content as MdDocument, () => Projection, () => Session.State.Document, Sync);
        Session = new EditorSession(state, new EditorServices { Ids = new SequentialBlockIdSource(1_000_000), Layout = Layout });
        Sync();
    }

    public VariableXamlRoot Root { get; }

    public EditorSession Session { get; }

    public XamlLayoutService Layout { get; }

    public ProjectionResult? Projection { get; private set; }

    public MdDocument Document => (MdDocument)Root.Content;

    /// <summary>Runs a command, renders, and scrolls the caret into view (07 rule 9).</summary>
    public void Execute(Command command)
    {
        Session.Execute(command);
        Sync();
        Layout.ScrollPositionIntoView(Session.State.Selection.Head);
        Root.UpdateLayout();
    }

    public void Press(MoveDirection direction, TextUnit unit = TextUnit.VisualLine) => Execute(new MoveCaret(direction, unit));

    /// <summary>Projects the current state when it changed, renders it and completes layout.</summary>
    public void Sync()
    {
        if (!ReferenceEquals(_projected, Session?.State) && Session is not null)
        {
            Projection = Session.Project();
            Root.Render(Projection.Root);
            Root.Flush();
            _projected = Session.State;
        }

        Root.UpdateLayout();
    }
}
