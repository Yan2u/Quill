using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;

namespace Quill.Settings.Taskbar;

[SupportedOSPlatform("windows")]
internal static class ShellClsid
{
    public static readonly Guid DestinationList = new("77f10cf0-3db5-4966-b520-b7c54fd35ed6");
    public static readonly Guid EnumerableObjectCollection = new("2d3468c1-36a7-43b6-ac24-d3f02fd9607a");
    public static readonly Guid ShellLink = new("00021401-0000-0000-C000-000000000046");

    public static readonly Guid ICustomDestinationList = new("6332debf-87b5-4670-90c0-5e57b408a49e");
    public static readonly Guid IObjectArray = new("92ca9dcd-5622-4bba-a805-5e9f541bd8c9");
    public static readonly Guid IObjectCollection = new("5632b1a4-e38a-400a-928a-d4cd63230295");
    public static readonly Guid IShellLinkW = new("000214F9-0000-0000-C000-000000000046");

    public static readonly PropertyKey TitleKey = new()
    {
        FormatId = new Guid("F29F85E0-4FF9-1068-AB91-08002B27B3D9"),
        PropertyId = 2,
    };
}

[SupportedOSPlatform("windows")]
[ComImport]
[Guid("6332debf-87b5-4670-90c0-5e57b408a49e")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface ICustomDestinationList
{
    [PreserveSig]
    int SetAppID([MarshalAs(UnmanagedType.LPWStr)] string pszAppID);

    [PreserveSig]
    int BeginList(
        out uint pcMinSlots,
        [In] ref Guid riid,
        [MarshalAs(UnmanagedType.IUnknown)] out object? ppv);

    [PreserveSig]
    int AppendCategory(
        [MarshalAs(UnmanagedType.LPWStr)] string pszCategory,
        [MarshalAs(UnmanagedType.Interface)] IObjectArray poa);

    [PreserveSig]
    int AppendKnownCategory(int category);

    [PreserveSig]
    int AddUserTasks([MarshalAs(UnmanagedType.Interface)] IObjectArray poa);

    [PreserveSig]
    int CommitList();

    [PreserveSig]
    int GetRemovedDestinations(
        [In] ref Guid riid,
        [MarshalAs(UnmanagedType.IUnknown)] out object? ppv);

    [PreserveSig]
    int DeleteList([MarshalAs(UnmanagedType.LPWStr)] string? pszAppID);

    [PreserveSig]
    int AbortList();
}

[SupportedOSPlatform("windows")]
[ComImport]
[Guid("92ca9dcd-5622-4bba-a805-5e9f541bd8c9")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IObjectArray
{
    [PreserveSig]
    int GetCount(out uint pcObjects);

    [PreserveSig]
    int GetAt(
        uint uiIndex,
        [In] ref Guid riid,
        [MarshalAs(UnmanagedType.IUnknown)] out object? ppv);
}

[SupportedOSPlatform("windows")]
[ComImport]
[Guid("5632b1a4-e38a-400a-928a-d4cd63230295")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IObjectCollection : IObjectArray
{
    [PreserveSig]
    new int GetCount(out uint pcObjects);

    [PreserveSig]
    new int GetAt(
        uint uiIndex,
        [In] ref Guid riid,
        [MarshalAs(UnmanagedType.IUnknown)] out object? ppv);

    [PreserveSig]
    int AddObject([MarshalAs(UnmanagedType.IUnknown)] object punk);

    [PreserveSig]
    int AddFromArray([MarshalAs(UnmanagedType.Interface)] IObjectArray poaSource);

    [PreserveSig]
    int RemoveObjectAt(uint uiIndex);

    [PreserveSig]
    int Clear();
}

[SupportedOSPlatform("windows")]
[ComImport]
[Guid("000214F9-0000-0000-C000-000000000046")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IShellLinkW
{
    [PreserveSig]
    int GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszFile, int cch, nint pfd, uint fFlags);

    [PreserveSig]
    int GetIDList(out nint ppidl);

    [PreserveSig]
    int SetIDList(nint pidl);

    [PreserveSig]
    int GetDescription([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszName, int cch);

    [PreserveSig]
    int SetDescription([MarshalAs(UnmanagedType.LPWStr)] string pszName);

    [PreserveSig]
    int GetWorkingDirectory([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszDir, int cch);

    [PreserveSig]
    int SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string pszDir);

    [PreserveSig]
    int GetArguments([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszArgs, int cch);

    [PreserveSig]
    int SetArguments([MarshalAs(UnmanagedType.LPWStr)] string pszArgs);

    [PreserveSig]
    int GetHotkey(out ushort pwHotkey);

    [PreserveSig]
    int SetHotkey(ushort wHotkey);

    [PreserveSig]
    int GetShowCmd(out int piShowCmd);

    [PreserveSig]
    int SetShowCmd(int iShowCmd);

    [PreserveSig]
    int GetIconLocation([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszIconPath, int cch, out int piIcon);

    [PreserveSig]
    int SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string pszIconPath, int iIcon);

    [PreserveSig]
    int SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string pszPathRel, uint dwReserved);

    [PreserveSig]
    int Resolve(nint hwnd, uint fFlags);

    [PreserveSig]
    int SetPath([MarshalAs(UnmanagedType.LPWStr)] string pszFile);
}

[SupportedOSPlatform("windows")]
[ComImport]
[Guid("886d8eeb-8cf2-4446-8d02-cdba1dbdcf99")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IPropertyStore
{
    [PreserveSig]
    int GetCount(out uint cProps);

    [PreserveSig]
    int GetAt(uint iProp, out PropertyKey pkey);

    [PreserveSig]
    int GetValue([In] ref PropertyKey key, out PropVariant pv);

    [PreserveSig]
    int SetValue([In] ref PropertyKey key, [In] ref PropVariant propvar);

    [PreserveSig]
    int Commit();
}

[StructLayout(LayoutKind.Sequential, Pack = 4)]
internal struct PropertyKey
{
    public Guid FormatId;
    public uint PropertyId;
}

[SupportedOSPlatform("windows")]
[StructLayout(LayoutKind.Sequential)]
internal struct PropVariant : IDisposable
{
    private const ushort VtLpWstr = 31;

    public ushort vt;
    public ushort wReserved1;
    public ushort wReserved2;
    public ushort wReserved3;
    public nint pwszVal;
    public nint pad;

    public static PropVariant FromString(string value) => new()
    {
        vt = VtLpWstr,
        pwszVal = Marshal.StringToCoTaskMemUni(value),
    };

    public readonly string? AsString() =>
        vt == VtLpWstr && pwszVal != 0 ? Marshal.PtrToStringUni(pwszVal) : null;

    public void Dispose() => _ = NativeShellMethods.PropVariantClear(ref this);
}

[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
internal struct ShStockIconInfo
{
    public uint cbSize;
    public nint hIcon;
    public int iSysImageIndex;
    public int iIcon;

    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
    public string szPath;
}

[SupportedOSPlatform("windows")]
internal static class NativeShellMethods
{
    public const uint ClsCtxInprocServer = 1;
    public const uint SiidFolder = 3;
    public const uint ShgsiIconLocation = 0;

    [DllImport("ole32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    public static extern int CoCreateInstance(
        [In] ref Guid rclsid,
        nint pUnkOuter,
        uint dwClsContext,
        [In] ref Guid riid,
        [MarshalAs(UnmanagedType.IUnknown)] out object? ppv);

    [DllImport("ole32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    public static extern int PropVariantClear(ref PropVariant pvar);

    [DllImport("shell32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    public static extern int SHGetStockIconInfo(uint siid, uint uFlags, ref ShStockIconInfo psii);

    [DllImport("user32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool DestroyIcon(nint hIcon);
}
