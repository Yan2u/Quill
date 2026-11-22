using Quill.Editor.WinUI.Theming;

namespace Quill.Shell.Themes;

/// <summary>A theme as the app shows it: its metadata and its style sheet (15-themes.md).</summary>
public sealed record ThemeInfo(string Id, string Name, string Description, DateTimeOffset Created, DateTimeOffset Modified, bool IsBuiltIn, string BasedOn, MdStyleSheet Sheet);
