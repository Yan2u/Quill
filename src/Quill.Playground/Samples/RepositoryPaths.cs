namespace Quill.Playground.Samples;

/// <summary>Locates the repository from the app directory: test corpora and temporary output live there.</summary>
internal static class RepositoryPaths
{
    private static readonly Lazy<string?> RootPath = new(FindRoot);

    /// <summary>The repository root (the folder holding Quill.slnx), or null when the app runs elsewhere.</summary>
    public static string? Root => RootPath.Value;

    /// <summary>A folder under <c>.agents/temp</c>, created on demand (AGENTS.md: temporary output goes there).</summary>
    public static string Temp(string name)
    {
        string path = Path.Combine(Root ?? Path.GetTempPath(), ".agents", "temp", name);
        Directory.CreateDirectory(path);
        return path;
    }

    public static string? TestData(params string[] parts) => Root is { } root ? Path.Combine([root, "testdata", .. parts]) : null;

    private static string? FindRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Quill.slnx")))
            {
                return directory.FullName;
            }
        }

        return null;
    }
}
