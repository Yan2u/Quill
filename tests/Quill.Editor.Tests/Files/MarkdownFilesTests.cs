using Quill.Editor.Files;

namespace Quill.Editor.Tests.Files;

public sealed class MarkdownFilesTests : IDisposable
{
    private readonly string _folder = Directory.CreateTempSubdirectory("quill-files-").FullName;

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    [Theory]
    [InlineData("a.md")]
    [InlineData("a.markdown")]
    [InlineData("a.mdown")]
    [InlineData("a.mkd")]
    [InlineData("a.txt")]
    [InlineData("A.MD")]
    [InlineData(@"C:\notes\a.b.md")]
    public void Counts_as_markdown(string path) => Assert.True(MarkdownFiles.IsMarkdown(path));

    [Theory]
    [InlineData("a.png")]
    [InlineData("a.md.bak")]
    [InlineData("md")]
    [InlineData("a")]
    public void Does_not_count_as_markdown(string path) => Assert.False(MarkdownFiles.IsMarkdown(path));

    [Fact]
    public void Reads_folders_and_markdown_files_in_natural_order()
    {
        File.WriteAllText(Path.Combine(_folder, "note10.md"), string.Empty);
        File.WriteAllText(Path.Combine(_folder, "note2.md"), string.Empty);
        File.WriteAllText(Path.Combine(_folder, "Apple.txt"), string.Empty);
        File.WriteAllText(Path.Combine(_folder, "picture.png"), string.Empty);
        Directory.CreateDirectory(Path.Combine(_folder, "b"));
        Directory.CreateDirectory(Path.Combine(_folder, "A"));

        var content = MarkdownFiles.Read(_folder);

        Assert.Equal(["A", "b"], content.Folders.Select(Path.GetFileName));
        Assert.Equal(["Apple.txt", "note2.md", "note10.md"], content.Files.Select(Path.GetFileName));
    }

    [Fact]
    public void Leaves_out_hidden_entries_and_dot_folders()
    {
        Directory.CreateDirectory(Path.Combine(_folder, ".git"));
        File.WriteAllText(Path.Combine(_folder, ".draft.md"), string.Empty);
        string hidden = Path.Combine(_folder, "hidden.md");
        File.WriteAllText(hidden, string.Empty);
        File.SetAttributes(hidden, FileAttributes.Hidden);
        File.WriteAllText(Path.Combine(_folder, "shown.md"), string.Empty);

        var content = MarkdownFiles.Read(_folder);

        Assert.Empty(content.Folders);
        Assert.Equal(["shown.md"], content.Files.Select(Path.GetFileName));
    }

    [Fact]
    public void A_folder_that_cannot_be_read_is_empty()
    {
        var content = MarkdownFiles.Read(Path.Combine(_folder, "missing"));

        Assert.Empty(content.Folders);
        Assert.Empty(content.Files);
    }
}
