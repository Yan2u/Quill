using Quill.Editor.Commands;
using Quill.Editor.Documents;
using Quill.Editor.Tests.Support;

namespace Quill.Editor.Tests.Editing;

/// <summary>
/// Q91: the language of a code block is document text, a one-line slot after the code. Down from the
/// last line of code reaches it, Up from below reaches it before the code; Enter, Shift+Enter and
/// Ctrl+Enter go back to the end of the code; Ctrl+A selects it first; line breaks pasted into it are
/// dropped; the code block's other rules stay as they were. Find does not search it (FindReplaceTests).
/// </summary>
public sealed class CodeLanguageTests
{
    [Theory]
    [InlineData("```js\na\nb<|>\n```\n\nafter", "Down", "```js<|>\na\nb\n```\n\nafter")]
    [InlineData("```js\na\nb\n```\n\nafter<|>", "Up", "```js<|>\na\nb\n```\n\nafter")]
    [InlineData("```js\na\nb\n```\n\nafter<|>", "Up Up", "```js\na\nb<|>\n```\n\nafter")]
    [InlineData("```<|>\na\n```\n\nafter", "Up", "```\n<|>a\n```\n\nafter")]
    public void UpAndDownPassThroughTheLanguage(string before, string keys, string after)
    {
        var host = EditorTestHost.Load(before);
        host.Press(keys);
        Assert.Equal(after, host.Dump());
    }

    [Theory]
    [InlineData("Enter")]
    [InlineData("Shift+Enter")]
    [InlineData("Ctrl+Enter")]
    public void EnterInTheLanguageGoesBackToTheEndOfTheCode(string key)
    {
        var host = EditorTestHost.Load("```j<|>s\na\nb\n```\n\nafter");
        host.Press(key);
        Assert.Equal("```js\na\nb<|>\n```\n\nafter", host.Dump());
    }

    [Theory]
    [InlineData("```js\na\n```", "Ctrl+A", "```<^>js<$>\na\n```")]
    [InlineData("x\n\n```js\na\n```\n\ny", "Ctrl+A Ctrl+A", "<^>x\n\n```js\na\n```\n\ny<$>")]
    public void SelectAllInTheLanguageSelectsItFirst(string document, string keys, string after)
    {
        // The caret goes into the language: the end of its fence line.
        var host = EditorTestHost.Load(document.Replace("```js\n", "```j<|>s\n"));
        host.Press(keys);
        Assert.Equal(after, host.Dump());
    }

    [Fact]
    public void TypingInTheLanguageChangesTheInfoString()
    {
        var host = EditorTestHost.Load("~~~ js<|>\ncode\n~~~");
        host.Type("on");
        Assert.Equal("~~~json\ncode\n~~~", TestMarkdown.Serialize(host.State.Document));
        host.Press("Ctrl+Z");
        Assert.Equal("~~~ js\ncode\n~~~", TestMarkdown.Serialize(host.State.Document));
    }

    [Fact]
    public void LineBreaksPastedIntoTheLanguageAreDropped()
    {
        var host = EditorTestHost.Load("```<|>\ncode\n```");
        host.Execute(new InsertText("py\nthon\r\n"));
        Assert.Equal("python", ((CodeFence)host.State.Document.Children[0]).Info);
    }

    [Theory]
    [InlineData("```js<|>\na\n```", "Backspace", "```j<|>\na\n```")]
    [InlineData("```<|>js\na\n```", "Backspace", "```<|>js\na\n```")]
    [InlineData("```js<|>\na\n```", "Delete", "```js<|>\na\n```")]
    [InlineData("```js\na<|>\n```", "Delete", "```js\na<|>\n```")]
    public void BackspaceAndDeleteNeverMergeTheLanguageWithTheCode(string before, string key, string after)
    {
        var host = EditorTestHost.Load(before);
        host.Press(key);
        Assert.Equal(after, host.Dump());
    }

    [Fact]
    public void AnEmptyCodeBlockIsDeletedWhateverItsLanguage()
    {
        var host = EditorTestHost.Load("x\n\n```js\n<|>\n```");
        host.Press("Backspace");
        Assert.DoesNotContain(host.State.Document.Children, static block => block is CodeFence);
    }

    [Fact]
    public void ANewCodeBlockPutsTheCaretInTheCode()
    {
        var host = EditorTestHost.Load("<|>");
        host.Type("```js").Press("Enter").Type("a");
        Assert.Equal("```js\na<|>\n```", host.Dump());
    }

    [Fact]
    public void IndentedCodeThatGetsALanguageIsFenced()
    {
        var host = EditorTestHost.Load("    code\n");
        var fence = (CodeFence)host.State.Document.Children[0];
        host.Execute(new SetCodeLanguage(fence.Id, "py"));
        Assert.Equal("```py\ncode\n```\n", TestMarkdown.Serialize(host.State.Document));
    }
}
