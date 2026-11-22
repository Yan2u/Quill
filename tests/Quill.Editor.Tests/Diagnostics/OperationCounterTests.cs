using Quill.Editor.Diagnostics;
using Quill.Editor.Documents;
using Quill.Editor.Markdown;

namespace Quill.Editor.Tests.Diagnostics;

/// <summary>
/// The counters behind 09-performance.md rule 8: edits and undo must not parse or serialize the whole
/// document. Later milestones assert zero inside edit scopes; here the counting itself is tested.
/// </summary>
public sealed class OperationCounterTests
{
    [Fact]
    public void ImportAndSerializeAreCounted()
    {
        using var scope = OperationCounters.BeginScope();
        var document = MarkdownImporter.Import("# a\n\nb\n", new SequentialBlockIdSource()).Document;
        Assert.Equal(1, scope.FullDocumentParses);
        Assert.Equal(0, scope.FullDocumentSerializations);

        MarkdownSerializer.Serialize(document);
        MarkdownSerializer.SerializeFile(document);
        Assert.Equal(1, scope.FullDocumentParses);
        Assert.Equal(2, scope.FullDocumentSerializations);
        Assert.Equal(0, scope.FullProjections);
    }

    [Fact]
    public void NestedScopesCountIntoTheirParents()
    {
        using var outer = OperationCounters.BeginScope();
        using (var inner = OperationCounters.BeginScope())
        {
            MarkdownImporter.Import("a", new SequentialBlockIdSource());
            Assert.Equal(1, inner.FullDocumentParses);
        }

        MarkdownImporter.Import("b", new SequentialBlockIdSource());
        Assert.Equal(2, outer.FullDocumentParses);
    }

    [Fact]
    public async Task ScopesOnOtherCallContextsDoNotInterfere()
    {
        using var scope = OperationCounters.BeginScope();
        await Task.Run(() =>
        {
            using var other = OperationCounters.BeginScope();
            MarkdownImporter.Import("a", new SequentialBlockIdSource());
            Assert.Equal(1, other.FullDocumentParses);
        }, TestContext.Current.CancellationToken);

        // The task inherited this scope as a parent, so its import counts here too; work started
        // outside this call context does not.
        Assert.Equal(1, scope.FullDocumentParses);
        long before = OperationCounters.Total(CountedOperation.FullDocumentParse);
        MarkdownImporter.Import("b", new SequentialBlockIdSource());
        Assert.True(OperationCounters.Total(CountedOperation.FullDocumentParse) > before);
        Assert.Equal(2, scope.FullDocumentParses);
    }
}
