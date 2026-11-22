namespace Quill.Editor.WinUI.Theming;

/// <summary>
/// The built-in editor themes: Github (light) and Github Dark Dimmed, based on MIT-licensed
/// <c>primer/primitives</c> and <c>sindresorhus/github-markdown-css</c>. Their ids are what the
/// settings store for the light and dark theme; user themes are managed in M7.4 (15-themes.md).
/// </summary>
public static class BuiltInThemes
{
    public const string GithubId = "github";
    public const string GithubDarkDimmedId = "github-dark-dimmed";

    public static BuiltInTheme Github { get; } = new(GithubId, "Github", "GitHub Light theme based on Primer primitives and github-markdown-css.", MdStyleSheet.Light);

    public static BuiltInTheme GithubDarkDimmed { get; } = new(GithubDarkDimmedId, "Github Dark Dimmed", "GitHub Dark Dimmed theme based on Primer primitives and github-markdown-css.", MdStyleSheet.Dark);

    public static IReadOnlyList<BuiltInTheme> All { get; } = [Github, GithubDarkDimmed];

    /// <summary>The theme with <paramref name="id"/>, or null when there is none.</summary>
    public static BuiltInTheme? Find(string? id) => All.FirstOrDefault(theme => theme.Id == id);
}
