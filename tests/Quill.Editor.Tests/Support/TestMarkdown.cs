using Quill.Editor.Documents;
using Quill.Editor.Markdown;

namespace Quill.Editor.Tests.Support;

/// <summary>
/// Shorthands for importing and serializing in tests.
/// </summary>
public static class TestMarkdown
{
    public static Document Import(string markdown)
    {
        var result = MarkdownImporter.Import(markdown, new SequentialBlockIdSource());
        Assert.Empty(result.Diagnostics);
        Assert.Empty(DocumentValidator.Validate(result.Document));
        return result.Document;
    }

    public static string Serialize(Document document, EditorOptions? options = null) =>
        MarkdownSerializer.Serialize(document, options);

    /// <summary>Imports, checks that serializing reproduces the input exactly, and returns the document.</summary>
    public static Document RoundTrip(string markdown)
    {
        var document = Import(markdown);
        Assert.Equal(markdown, Serialize(document));
        return document;
    }

    /// <summary>Serializes a document, imports the result and checks that the structure survives.</summary>
    public static string SerializeStable(Document document, EditorOptions? options = null)
    {
        string markdown = Serialize(document, options);
        Assert.Equal(ModelDump.Structure(document), ModelDump.Structure(Import(markdown)));
        return markdown;
    }

    public static T Single<T>(Document document)
        where T : Block
    {
        return Assert.IsType<T>(Assert.Single(document.Children));
    }
}
