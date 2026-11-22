using System.Runtime.InteropServices;

namespace Quill.Editor.WinUI.Theming;

/// <summary>Whether a high-contrast theme is on: AccessibilitySettings is not available to desktop apps.</summary>
internal static partial class SystemColors
{
    private const uint GetHighContrast = 0x0042;
    private const uint HighContrastOn = 0x00000001;

    public static bool IsHighContrast()
    {
        var info = new HighContrastInfo { Size = (uint)Marshal.SizeOf<HighContrastInfo>() };
        return SystemParametersInfo(GetHighContrast, info.Size, ref info, 0) && (info.Flags & HighContrastOn) != 0;
    }

    [LibraryImport("user32.dll", EntryPoint = "SystemParametersInfoW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SystemParametersInfo(uint action, uint parameter, ref HighContrastInfo info, uint winIni);

    [StructLayout(LayoutKind.Sequential)]
    private struct HighContrastInfo
    {
        public uint Size;
        public uint Flags;
        public nint DefaultScheme;
    }
}
