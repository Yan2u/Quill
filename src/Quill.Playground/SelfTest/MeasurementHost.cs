using System.Runtime.InteropServices;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Windows.Graphics;

namespace Quill.Playground.SelfTest;

/// <summary>Where a measurement window is placed.</summary>
internal enum HostMode
{
    /// <summary>Left of the virtual screen.</summary>
    OffScreen,

    /// <summary>On the primary screen, fully transparent, click-through and at the bottom of the z-order.</summary>
    Transparent,
}

/// <summary>
/// Shows a window for in-process tests without taking the user's foreground, mouse or keyboard
/// (08-testing-and-debugging.md, Q7): never activated (and not activatable by a click), hidden from
/// the taskbar and Alt+Tab, and either off-screen or invisible.
/// </summary>
internal static partial class MeasurementHost
{
    private const int GwlExStyle = -20;
    private const long WsExNoActivate = 0x08000000;
    private const long WsExToolWindow = 0x00000080;
    private const long WsExLayered = 0x00080000;
    private const long WsExTransparent = 0x00000020;
    private const uint LwaAlpha = 0x2;
    private const uint SwpNoSize = 0x1;
    private const uint SwpNoMove = 0x2;
    private const uint SwpNoActivate = 0x10;
    private const nint HwndBottom = 1;
    private const int SmXVirtualScreen = 76;
    private const int SmYVirtualScreen = 77;

    public static HostMode Mode { get; private set; }

    public static bool SwitchersAvailable { get; private set; } = true;

    public static nint Show(Window window, int width, int height, HostMode mode)
    {
        Mode = mode;
        nint hwnd = WinRT.Interop.WindowNative.GetWindowHandle(window);
        long style = WsExNoActivate | WsExToolWindow | (mode == HostMode.Transparent ? WsExLayered | WsExTransparent : 0);
        SetWindowLongPtr(hwnd, GwlExStyle, GetWindowLongPtr(hwnd, GwlExStyle) | (nint)style);
        if (mode == HostMode.Transparent)
        {
            SetLayeredWindowAttributes(hwnd, 0, 0, LwaAlpha);
        }

        AppWindow appWindow = window.AppWindow;
        try
        {
            appWindow.IsShownInSwitchers = false;
            SwitchersAvailable = true;
        }
        catch (NotImplementedException)
        {
            // Private or headless desktops have no taskbar switcher service; WS_EX_TOOLWINDOW is already set.
            SwitchersAvailable = false;
        }
        int left = mode == HostMode.OffScreen ? GetSystemMetrics(SmXVirtualScreen) - width - 64 : 0;
        int top = mode == HostMode.OffScreen ? GetSystemMetrics(SmYVirtualScreen) : 0;
        appWindow.MoveAndResize(new RectInt32(left, top, width, height));
        appWindow.Show(activateWindow: false);
        if (mode == HostMode.Transparent)
        {
            SetWindowPos(hwnd, HwndBottom, 0, 0, 0, 0, SwpNoMove | SwpNoSize | SwpNoActivate);
        }

        return hwnd;
    }

    /// <summary>Whether the window lies wholly outside the virtual screen.</summary>
    public static bool IsOffScreen(Window window)
    {
        PointInt32 position = window.AppWindow.Position;
        SizeInt32 size = window.AppWindow.Size;
        return position.X + size.Width <= GetSystemMetrics(SmXVirtualScreen);
    }

    public static bool IsHiddenFromSwitchers(Window window) =>
        SwitchersAvailable
            ? !window.AppWindow.IsShownInSwitchers
            : (GetWindowLongPtr(WinRT.Interop.WindowNative.GetWindowHandle(window), GwlExStyle) & (nint)WsExToolWindow) != 0;

    public static nint ForegroundWindow() => GetForegroundWindow();

    public static (int X, int Y) CursorPosition() => GetCursorPos(out NativePoint point) ? (point.X, point.Y) : (int.MinValue, int.MinValue);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern nint GetWindowLongPtr(nint hwnd, int index);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    private static extern nint SetWindowLongPtr(nint hwnd, int index, nint value);

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int index);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetLayeredWindowAttributes(nint hwnd, uint colorKey, byte alpha, uint flags);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(nint hwnd, nint insertAfter, int x, int y, int width, int height, uint flags);

    [DllImport("user32.dll")]
    private static extern nint GetForegroundWindow();

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetCursorPos(out NativePoint point);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint
    {
        public int X;
        public int Y;
    }
}
