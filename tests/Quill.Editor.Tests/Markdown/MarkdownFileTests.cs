using Quill.Editor.Documents;
using Quill.Editor.Markdown;

namespace Quill.Editor.Tests.Markdown;

/// <summary>
/// M5.6: opening and saving files (06). An unedited file is written back byte for byte, with its
/// encoding, byte order mark and line breaks; a save replaces the file only once fully written.
/// </summary>
public sealed class MarkdownFileTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "quill-tests", Guid.NewGuid().ToString("N"));

    public MarkdownFileTests() => Directory.CreateDirectory(_folder);

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    [Theory]
    [InlineData(new byte[] { 0xEF, 0xBB, 0xBF, 0x23, 0x20, 0x61, 0x0D, 0x0A, 0x0D, 0x0A, 0x62, 0x0D, 0x0A })]
    [InlineData(new byte[] { 0xFF, 0xFE, 0x61, 0x00, 0x0A, 0x00 })]
    [InlineData(new byte[] { 0x2D, 0x20, 0x61, 0x0D, 0x2D, 0x20, 0x62 })]
    public async Task OpenThenSaveWritesTheSameBytes(byte[] content)
    {
        string path = Path.Combine(_folder, "a.md");
        await File.WriteAllBytesAsync(path, content, TestContext.Current.CancellationToken);
        var document = (await MarkdownFile.OpenAsync(path, new SequentialBlockIdSource(), TestContext.Current.CancellationToken)).Document;
        await MarkdownFile.SaveAsync(document, path, cancellation: TestContext.Current.CancellationToken);
        Assert.Equal(content, await File.ReadAllBytesAsync(path, TestContext.Current.CancellationToken));
        Assert.Equal([Path.Combine(_folder, "a.md")], Directory.GetFiles(_folder));
    }

    [Fact]
    public async Task SaveCreatesANewFile()
    {
        string path = Path.Combine(_folder, "new.md");
        var document = MarkdownImporter.Import("# 标题\n\n正文\n", new SequentialBlockIdSource()).Document;
        await MarkdownFile.SaveAsync(document, path, cancellation: TestContext.Current.CancellationToken);
        Assert.Equal("# 标题\n\n正文\n"u8.ToArray(), await File.ReadAllBytesAsync(path, TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData(LineEnding.CrLf, new byte[] { 0x61, 0x0D, 0x0A, 0x0D, 0x0A, 0x62, 0x0D, 0x0A })]
    [InlineData(LineEnding.Lf, new byte[] { 0x61, 0x0A, 0x0A, 0x62, 0x0A })]
    public async Task ANewDocumentIsSavedWithTheLineBreakOfTheSettings(LineEnding lineEnding, byte[] expected)
    {
        // Q103: new documents take the line break from the settings; UTF-8 without BOM, ending with a line break.
        string path = Path.Combine(_folder, "new.md");
        var document = MarkdownImporter.Import("a\n\nb", new SequentialBlockIdSource()).Document with { Format = DocumentFormat.ForNewDocument(lineEnding) };
        await MarkdownFile.SaveAsync(document, path, cancellation: TestContext.Current.CancellationToken);
        Assert.Equal(expected, await File.ReadAllBytesAsync(path, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task AFailedSaveLeavesTheFileAsItWas()
    {
        string path = Path.Combine(_folder, "locked.md");
        await File.WriteAllTextAsync(path, "old", TestContext.Current.CancellationToken);
        var document = MarkdownImporter.Import("new", new SequentialBlockIdSource()).Document;
        using (new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            await Assert.ThrowsAnyAsync<Exception>(() => MarkdownFile.SaveAsync(document, path, cancellation: TestContext.Current.CancellationToken));
        }

        Assert.Equal("old", await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken));
        Assert.Equal([path], Directory.GetFiles(_folder));
    }
}
