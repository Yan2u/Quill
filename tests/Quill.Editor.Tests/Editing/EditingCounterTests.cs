using Quill.Editor.Commands;
using Quill.Editor.Diagnostics;
using Quill.Editor.Tests.Support;

namespace Quill.Editor.Tests.Editing;

/// <summary>
/// Constraint 6 / 09-performance.md: no edit and no undo parses or serializes the whole document.
/// Loading and dumping the DSL do both, so they stay outside the counted scope.
/// </summary>
public sealed class EditingCounterTests
{
    public static TheoryData<string, string> Scenarios => new()
    {
        { "a<|>b", "Enter Shift+Enter Backspace Delete Ctrl+Enter" },
        { "# He<|>llo", "Enter Backspace Ctrl+2 Ctrl+0" },
        { "- one\n    - nested one<|>\n        - nested square one\n    - nested two\n- two", "Tab Shift+Tab Shift+Tab Enter Enter Backspace" },
        { "1. a<|>\n2. b", "Enter Enter Tab Shift+Tab Backspace" },
        { "> a\n>\n> <|>", "Enter Backspace" },
        { "| a | b |\n| - | - |\n| 1<|> | 2 |", "Tab Tab Tab Enter Shift+Tab Shift+Enter" },
        { "[^1]: no<|>te", "Enter Backspace Delete" },
        { "```\na<|>\n```\n\n---", "Enter Ctrl+Enter Right Backspace Delete" },
        { "a<^>b\n\nc<$>d", "Ctrl+C Ctrl+X Ctrl+V Ctrl+A Delete" },
    };

    [Theory]
    [MemberData(nameof(Scenarios))]
    public void EditsAndUndoDoNoWholeDocumentWork(string state, string keys)
    {
        var host = EditorTestHost.Load(state);
        using var scope = OperationCounters.BeginScope();
        host.Press(keys);
        host.Type("# x").Press("Shift+Enter").Type("---").Execute(new CommitParagraph());
        while (host.Session.History.CanUndo)
        {
            host.Press("Ctrl+Z");
        }

        while (host.Session.History.CanRedo)
        {
            host.Press("Ctrl+Y");
        }

        Assert.Equal(0, scope.FullDocumentParses);
        Assert.Equal(0, scope.FullDocumentSerializations);
    }
}
