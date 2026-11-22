using Microsoft.UI;
using Microsoft.UI.Xaml;
using Windows.UI;

namespace Quill;

/// <summary>
/// The caption buttons of the extended title bar: the system draws them, so their colors follow the
/// app's theme (RequestedTheme or the system's) only when set here, again on every theme change.
/// </summary>
public sealed partial class MainWindow
{
    private void InitializeTitleBarColors()
    {
        RootGrid.ActualThemeChanged += (_, _) => ApplyTitleBarColors();
        ApplyTitleBarColors();
    }

    private void ApplyTitleBarColors()
    {
        bool dark = RootGrid.ActualTheme == ElementTheme.Dark;
        var bar = AppWindow.TitleBar;
        Color foreground = dark ? Colors.White : Colors.Black;
        bar.ButtonBackgroundColor = Colors.Transparent;
        bar.ButtonInactiveBackgroundColor = Colors.Transparent;
        bar.ButtonForegroundColor = foreground;
        bar.ButtonHoverForegroundColor = foreground;
        bar.ButtonPressedForegroundColor = foreground;
        bar.ButtonInactiveForegroundColor = dark ? Color.FromArgb(0xFF, 0x78, 0x78, 0x78) : Color.FromArgb(0xFF, 0x99, 0x99, 0x99);

        // The Fluent subtle fills (SubtleFillColorSecondary / Tertiary).
        bar.ButtonHoverBackgroundColor = dark ? Color.FromArgb(0x0F, 0xFF, 0xFF, 0xFF) : Color.FromArgb(0x09, 0x00, 0x00, 0x00);
        bar.ButtonPressedBackgroundColor = dark ? Color.FromArgb(0x0A, 0xFF, 0xFF, 0xFF) : Color.FromArgb(0x06, 0x00, 0x00, 0x00);
    }
}
