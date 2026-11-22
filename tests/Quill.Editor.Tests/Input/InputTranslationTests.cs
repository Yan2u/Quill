using Quill.Editor.Commands;
using Quill.Editor.Input;
using Quill.Editor.State;
using Quill.Editor.Tests.Support;

namespace Quill.Editor.Tests.Input;

/// <summary>
/// M5.3, layer 1: the view's key codes become chords and then commands through the key map (07);
/// pointer gestures become commands with the fake layout's hit testing; double and triple click
/// select a word and a block; Ctrl+click and backlinks navigate.
/// </summary>
public sealed class InputTranslationTests
{
    [Theory]
    [InlineData(0x0D, KeyModifiers.None, "Enter")]
    [InlineData(0x0D, KeyModifiers.Shift, "Shift+Enter")]
    [InlineData(0x08, KeyModifiers.Ctrl, "Ctrl+Backspace")]
    [InlineData(0x09, KeyModifiers.Shift, "Shift+Tab")]
    [InlineData(0x2E, KeyModifiers.None, "Delete")]
    [InlineData(0x25, KeyModifiers.Ctrl | KeyModifiers.Shift, "Ctrl+Shift+Left")]
    [InlineData(0x22, KeyModifiers.None, "PageDown")]
    [InlineData(0x24, KeyModifiers.Ctrl, "Ctrl+Home")]
    [InlineData(0x42, KeyModifiers.Ctrl, "Ctrl+B")]
    [InlineData(0x31, KeyModifiers.Ctrl, "Ctrl+1")]
    [InlineData(0x61, KeyModifiers.Ctrl, "Ctrl+1")]
    [InlineData(0x43, KeyModifiers.Ctrl | KeyModifiers.Shift, "Ctrl+Shift+C")]
    [InlineData(0xDB, KeyModifiers.Ctrl | KeyModifiers.Shift, "Ctrl+Shift+[")]
    [InlineData(0x71, KeyModifiers.None, "F2")]
    public void VirtualKeysBecomeChords(int key, KeyModifiers modifiers, string chord)
    {
        Assert.Equal(KeyChord.Parse(chord), VirtualKeys.ToChord(key, modifiers));
    }

    [Theory]
    [InlineData(0x10)]
    [InlineData(0x11)]
    [InlineData(0x12)]
    [InlineData(0x5B)]
    [InlineData(0xE5)]
    public void ModifierAndProcessKeysAreNoChords(int key)
    {
        Assert.Null(VirtualKeys.ToChord(key, KeyModifiers.None));
    }

    [Theory]
    [InlineData(0x0D, KeyModifiers.None, typeof(SplitBlock))]
    [InlineData(0x26, KeyModifiers.Shift, typeof(MoveCaret))]
    [InlineData(0x5A, KeyModifiers.Ctrl, typeof(Undo))]
    [InlineData(0x32, KeyModifiers.Ctrl, typeof(SetHeadingLevel))]
    public void KeyCodesReachTheirCommands(int key, KeyModifiers modifiers, Type command)
    {
        var binding = Assert.IsType<CommandBinding>(KeyMap.Default.Find(VirtualKeys.ToChord(key, modifiers)!.Value));
        Assert.IsType(command, binding.Command);
    }

    [Fact]
    public void AClickPlacesTheCaretAndUnfreezesOnRelease()
    {
        var (host, gestures) = Load("hello world<|>");
        Run(host, gestures.Pressed(At(3), host.Layout.HitTest(new(3, 0.5)), host.State));
        Assert.True(host.State.Interaction.IsPointerDown);
        Run(host, gestures.Released(At(3), host.State, out var target));
        Assert.Null(target);
        Assert.False(host.State.Interaction.IsPointerDown);
        Assert.Equal("hel<|>lo world", host.Dump());
    }

    [Fact]
    public void TheActivationStaysFrozenWhileThePointerIsDown()
    {
        // The caret ends outside the bold text; a press inside it must not expand the markers yet (03).
        var (host, gestures) = Load("**bold** text<|>");
        var before = host.State.ActivationHead;
        Run(host, gestures.Pressed(At(4), host.Layout.HitTest(new(4, 0.5)), host.State));
        Assert.Equal(before, host.State.ActivationHead);
        Assert.Equal(new TextPosition(host.State.Selection.Head is TextPosition head ? head.Block : default, 4), host.State.Selection.Head);
        Run(host, gestures.Released(At(4), host.State, out _));
        Assert.Equal(host.State.Selection.Head, host.State.ActivationHead);
    }

    [Fact]
    public void ADragStartsOnlyPastTheThresholdFromThePress()
    {
        var (host, gestures) = Load("hello world<|>");
        Run(host, gestures.Pressed(At(2), host.Layout.HitTest(new(2, 0.5)), host.State));
        Assert.Empty(gestures.Moved(At(4), () => host.Layout.HitTest(new(4, 0.5))));
        Assert.False(gestures.IsDragging);
        Run(host, gestures.Moved(At(8), () => host.Layout.HitTest(new(8, 0.5))));
        Assert.True(gestures.IsDragging);
        Run(host, gestures.Released(At(8), host.State, out _));
        Assert.Equal("he<^>llo wo<$>rld", host.Dump());
    }

    [Fact]
    public void DoubleAndTripleClickSelectAWordAndTheBlock()
    {
        var (host, gestures) = Load("first\n\nhello world<|>");
        var hit = host.Layout.HitTest(new(8, 1.5));
        Click(host, gestures, At(8, 1.5), hit);
        host.Advance(TimeSpan.FromMilliseconds(150));
        Click(host, gestures, At(8, 1.5), hit);
        Assert.Equal(2, gestures.ClickCount);
        Assert.Equal("first\n\nhello <^>world<$>", host.Dump());
        host.Advance(TimeSpan.FromMilliseconds(150));
        Click(host, gestures, At(8, 1.5), hit);
        Assert.Equal(3, gestures.ClickCount);
        Assert.Equal("first\n\n<^>hello world<$>", host.Dump());
    }

    [Fact]
    public void ASlowSecondClickIsASingleClick()
    {
        var (host, gestures) = Load("hello world<|>");
        var hit = host.Layout.HitTest(new(8, 0.5));
        Click(host, gestures, At(8), hit);
        host.Advance(TimeSpan.FromMilliseconds(600));
        Click(host, gestures, At(8), hit);
        Assert.Equal(1, gestures.ClickCount);
        Assert.Equal("hello wo<|>rld", host.Dump());
    }

    [Fact]
    public void ShiftClickExtendsTheSelection()
    {
        var (host, gestures) = Load("hel<|>lo world");
        Click(host, gestures, At(9, 0.5, KeyModifiers.Shift), host.Layout.HitTest(new(9, 0.5)));
        Assert.Equal("hel<^>lo wor<$>ld", host.Dump());
    }

    [Fact]
    public void CtrlClickOnAFootnoteReferenceGoesToItsDefinition()
    {
        var (host, gestures) = Load("a[^1] b<|>\n\n[^1]: note");
        var target = Click(host, gestures, At(2, 0.5, KeyModifiers.Ctrl), host.Layout.HitTest(new(2, 0.5)));
        Assert.Equal(new FootnoteTarget("1"), target);
        Assert.Equal("a[^1] b\n\n[^1]: <|>note", host.Dump());
    }

    [Fact]
    public void CtrlClickOnAnAnchorLinkGoesToTheHeading()
    {
        var (host, gestures) = Load("# Intro Part\n\nsee [x](#intro-part)<|>");
        Click(host, gestures, At(6, 1.5, KeyModifiers.Ctrl), host.Layout.HitTest(new(6, 1.5)));
        Assert.Equal("# <|>Intro Part\n\nsee [x](#intro-part)", host.Dump());
    }

    [Fact]
    public void CtrlClickOnAnExternalLinkIsLeftToTheHost()
    {
        var (host, gestures) = Load("see [x](https://example.com)<|>");
        var target = Click(host, gestures, At(5, 0.5, KeyModifiers.Ctrl), host.Layout.HitTest(new(5, 0.5)));
        Assert.Equal(new LinkTarget("https://example.com"), target);
        Assert.Equal("see [<|>x](https://example.com)", host.Dump());
    }

    [Fact]
    public void ABacklinkGoesToTheReferenceByIndex()
    {
        var host = EditorTestHost.Load("x[^a] y[^a]\n\n[^a]: n<|>");
        host.Execute(new GoToFootnoteReference("a", 1));
        Assert.Equal("x[^a] y<|>[^a]\n\n[^a]: n", host.Dump());
    }

    [Theory]
    [InlineData("hello world<|>", 7, "hello <^>world<$>")]
    [InlineData("hello world<|>", 5, "<^>hello<$> world")]
    [InlineData("a, b<|>", 1, "a<^>,<$> b")]
    [InlineData("中文测试，好<|>", 1, "<^>中文测试<$>，好")]
    public void SelectWordSelectsTheRunAtThePosition(string before, int offset, string after)
    {
        var host = EditorTestHost.Load(before);
        var block = ((TextPosition)host.State.Selection.Head).Block;
        host.Execute(new SelectWord(new TextPosition(block, offset)));
        Assert.Equal(after, host.Dump());
    }

    [Fact]
    public void SelectingAWholeHiddenBoldWordTakesItsMarkers()
    {
        // The bold element is not active (the caret is elsewhere): its markers follow the selection (Q18).
        var host = EditorTestHost.Load("**bold** text<|>");
        var block = ((TextPosition)host.State.Selection.Head).Block;
        host.Execute(new SelectWord(new TextPosition(block, 4)));
        Assert.Equal("<^>**bold**<$> text", host.Dump());
    }

    private static (EditorTestHost Host, PointerGestures Gestures) Load(string dsl)
    {
        var host = EditorTestHost.Load(dsl);
        return (host, new PointerGestures(host.Clock));
    }

    private static PointerInput At(double x, double y = 0.5, KeyModifiers modifiers = KeyModifiers.None) => new(x, y, modifiers);

    private static NavigationTarget? Click(EditorTestHost host, PointerGestures gestures, PointerInput input, Position hit)
    {
        Run(host, gestures.Pressed(input, hit, host.State));
        Run(host, gestures.Released(input, host.State, out var target));
        return target;
    }

    private static void Run(EditorTestHost host, IEnumerable<Command> commands)
    {
        foreach (var command in commands)
        {
            host.Execute(command);
        }
    }
}
