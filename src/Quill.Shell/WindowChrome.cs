using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Quill.Shell.Pages;
using Windows.Graphics;

namespace Quill.Shell;

/// <summary>
/// Safe window chrome calculations for extended title bars and responsive navigation (13-app-shell.md):
/// guards against minimized windows reporting negative title bar insets or zero layout widths.
/// </summary>
public static class WindowChrome
{
    /// <summary>Below this width the navigation switches from the side rail to a drawer.</summary>
    public const double NarrowNavigationWidth = 641;

    /// <summary>The top-level editor menus (File, Edit, Paragraph, Format, View) show only on the editor page.</summary>
    public static Visibility EditorMenuBarVisibility(ShellPage page) =>
        page == ShellPage.Editor ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>Whether the window is currently minimized.</summary>
    public static bool IsMinimized(AppWindow appWindow) =>
        appWindow.Presenter is OverlappedPresenter { State: OverlappedPresenterState.Minimized };

    /// <summary>Whether the window is currently maximized.</summary>
    public static bool IsMaximized(AppWindow appWindow) =>
        appWindow.Presenter is OverlappedPresenter { State: OverlappedPresenterState.Maximized };

    /// <summary>Whether the window is in its normal restored state.</summary>
    public static bool IsRestored(AppWindow appWindow) =>
        appWindow.Presenter is OverlappedPresenter { State: OverlappedPresenterState.Restored };

    /// <summary>
    /// Clamps a saved window size to the current display work area, returning null when the saved size
    /// or work area is missing or non-positive.
    /// </summary>
    public static SizeInt32? TryGetRestoredWindowSize(
        int? savedWidth,
        int? savedHeight,
        RectInt32 workArea,
        int minWidth = 480,
        int minHeight = 360)
    {
        if (savedWidth is not > 0 || savedHeight is not > 0 || workArea.Width <= 0 || workArea.Height <= 0)
        {
            return null;
        }

        int effectiveMinWidth = Math.Min(minWidth, workArea.Width);
        int effectiveMinHeight = Math.Min(minHeight, workArea.Height);
        return new SizeInt32(
            Math.Clamp(savedWidth.Value, effectiveMinWidth, workArea.Width),
            Math.Clamp(savedHeight.Value, effectiveMinHeight, workArea.Height));
    }

    /// <summary>
    /// Converts physical title bar insets to DIP <see cref="GridLength"/> values, returning null when
    /// minimized or when any inset or scale is invalid (preventing negative GridLength exceptions inside AppWindow.Changed).
    /// </summary>
    public static (GridLength Left, GridLength Right)? TryGetTitleBarInsets(
        int leftInsetPixels,
        int rightInsetPixels,
        double rasterizationScale,
        bool isMinimized = false)
    {
        if (isMinimized
            || leftInsetPixels < 0
            || rightInsetPixels < 0
            || rasterizationScale <= 0
            || !double.IsFinite(rasterizationScale))
        {
            return null;
        }

        return (
            new GridLength(leftInsetPixels / rasterizationScale),
            new GridLength(rightInsetPixels / rasterizationScale));
    }

    /// <summary>
    /// Determines whether the navigation bar should use the narrow drawer mode for <paramref name="width"/>,
    /// returning null while minimized or when width is not positive and finite.
    /// </summary>
    public static bool? TryGetNarrowNavigation(double width, bool isMinimized = false)
    {
        if (isMinimized || width <= 0 || !double.IsFinite(width))
        {
            return null;
        }

        return width < NarrowNavigationWidth;
    }
}
