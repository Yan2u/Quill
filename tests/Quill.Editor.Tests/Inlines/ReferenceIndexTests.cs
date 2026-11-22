using Quill.Editor.Documents;
using Quill.Editor.Inlines;
using Quill.Editor.Markdown;
using Quill.Editor.Tests.Support;

namespace Quill.Editor.Tests.Inlines;

/// <summary>
/// The document's definition and reference index (06-markdown-io.md): resolution in both directions,
/// first definition wins, versions that change only when the parse or the back links can change, and
/// incremental upkeep through edits.
/// </summary>
public sealed class ReferenceIndexTests
{
    [Fact]
    public void ResolvesLabelsCaseAndSpaceInsensitively()
    {
        var document = Import("[Foo  Bar]: /one\n\n> [foo bar]: /two\n\n[^Note]: n");
        var index = ReferenceIndex.Of(document);
        Assert.True(index.IsLinkDefined("foo bar"));
        Assert.False(index.IsLinkDefined("foo"));
        var first = index.ResolveLink(document, "foo bar");
        Assert.Equal(document.Children[0].Id, first);
        Assert.NotNull(index.ResolveFootnote(document, LinkLabel.Normalize("NOTE")));
    }

    [Fact]
    public void CountsFootnoteReferencesOutsideCode()
    {
        var document = Import("a[^1] `[^1]` \\[^1] [^1](x) b[^ 1 ]\n\n| [^1] |\n| - |\n| c |\n\n[^1]: n");
        Assert.Equal(3, ReferenceIndex.Of(document).FootnoteReferenceCount("1"));
    }

    [Fact]
    public void VersionsChangeOnlyWithTheLabelSet()
    {
        var host = EditorTestHost.Load("text<|>\n\n[a]: /u\n\n[^f]: n");
        var before = ReferenceIndex.Of(host.State.Document);
        host.Type(" more");
        Assert.Same(before, ReferenceIndex.Of(host.State.Document));

        host.Type(" [^f]");
        var counted = ReferenceIndex.Of(host.State.Document);
        Assert.Equal(before.LinkLabelsVersion, counted.LinkLabelsVersion);
        Assert.NotEqual(before.FootnotesVersion, counted.FootnotesVersion);
        Assert.Equal(1, counted.FootnoteReferenceCount("f"));

        host.Press("Ctrl+Z");
        Assert.Equal(0, ReferenceIndex.Of(host.State.Document).FootnoteReferenceCount("f"));
        Assert.True(ReferenceIndex.Of(host.State.Document).HasSameEntries(ReferenceIndex.BuildFresh(host.State.Document)));
    }

    [Fact]
    public void EditingALabelSlotRedefines()
    {
        var host = EditorTestHost.Load("[ab<|>]: /u");
        var before = ReferenceIndex.Of(host.State.Document);
        Assert.True(before.IsLinkDefined("ab"));
        host.Type("c");
        var after = ReferenceIndex.Of(host.State.Document);
        Assert.False(after.IsLinkDefined("ab"));
        Assert.True(after.IsLinkDefined("abc"));
        Assert.NotEqual(before.LinkLabelsVersion, after.LinkLabelsVersion);
    }

    private static Document Import(string markdown) => MarkdownImporter.Import(markdown, new SequentialBlockIdSource()).Document;
}
