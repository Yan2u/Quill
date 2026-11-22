namespace Quill.Settings;

/// <summary>The tab the sidebar shows.</summary>
public enum SidebarTabState
{
    Outline,
    Files,
}

/// <summary>How the sidebar's Files tab shows the folder.</summary>
public enum FilesViewState
{
    Tree,
    List,
}

/// <summary>
/// What the application remembers between sessions without it being a setting (14-settings.md): the
/// sidebar, the navigation and the last folder and file. Kept in <c>state.json</c>, not on the settings page.
/// </summary>
public sealed record AppState
{
    public const double DefaultSidebarWidth = 260;
    public const double MinSidebarWidth = 180;
    public const double MaxSidebarWidth = 480;
    public const int MinWindowWidth = 480;
    public const int MinWindowHeight = 360;
    public const int MaxWindowWidth = 16384;
    public const int MaxWindowHeight = 16384;

    public static AppState Default { get; } = new();

    public bool SidebarVisible { get; init; }

    public double SidebarWidth { get; init; } = DefaultSidebarWidth;

    public SidebarTabState SidebarTab { get; init; } = SidebarTabState.Outline;

    public FilesViewState FilesView { get; init; } = FilesViewState.Tree;

    /// <summary>Whether the navigation shows its labels.</summary>
    public bool NavigationExpanded { get; init; }

    /// <summary>The restored width of the window in physical pixels; null uses the system default.</summary>
    public int? WindowWidth { get; init; }

    /// <summary>The restored height of the window in physical pixels; null uses the system default.</summary>
    public int? WindowHeight { get; init; }

    /// <summary>Whether the window was maximized when last closed.</summary>
    public bool WindowMaximized { get; init; }

    /// <summary>The folder last opened in the sidebar's Files tab.</summary>
    public string? LastFolder { get; init; }

    /// <summary>The file last shown in a window.</summary>
    public string? LastFile { get; init; }

    /// <summary>The state with every value in its range.</summary>
    public AppState Normalize()
    {
        double width = double.IsFinite(SidebarWidth) ? Math.Clamp(SidebarWidth, MinSidebarWidth, MaxSidebarWidth) : DefaultSidebarWidth;
        int? winWidth = WindowWidth;
        int? winHeight = WindowHeight;
        if (winWidth is not > 0 || winHeight is not > 0)
        {
            winWidth = null;
            winHeight = null;
        }
        else
        {
            winWidth = Math.Clamp(winWidth.Value, MinWindowWidth, MaxWindowWidth);
            winHeight = Math.Clamp(winHeight.Value, MinWindowHeight, MaxWindowHeight);
        }

        return width == SidebarWidth && winWidth == WindowWidth && winHeight == WindowHeight
            ? this
            : this with { SidebarWidth = width, WindowWidth = winWidth, WindowHeight = winHeight };
    }
}
