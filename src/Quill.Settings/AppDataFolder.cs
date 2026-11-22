namespace Quill.Settings;

/// <summary>Where the application keeps its files: <c>%LOCALAPPDATA%\Quill</c>.</summary>
public static class AppDataFolder
{
    public static string Path { get; } =
        System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Quill");
}
