using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;

namespace Quill.Settings.Taskbar;

/// <summary>
/// Windows Shell implementation of <see cref="IJumpListBackend"/> backed by <c>ICustomDestinationList</c>,
/// <c>IShellLinkW</c>, and <c>SHGetStockIconInfo</c>. Leaves the process AppUserModelID system-assigned so
/// unpackaged windows, taskbar pins, and Jump List entries share the same identity.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsJumpListBackend : IJumpListBackend
{
    private const int PathBufferCapacity = 4096;

    public JumpListIcon ResolveFolderIcon()
    {
        var info = new ShStockIconInfo
        {
            cbSize = (uint)Marshal.SizeOf<ShStockIconInfo>(),
        };

        try
        {
            int hr = NativeShellMethods.SHGetStockIconInfo(
                NativeShellMethods.SiidFolder,
                NativeShellMethods.ShgsiIconLocation,
                ref info);

            if (info.hIcon != 0)
            {
                _ = NativeShellMethods.DestroyIcon(info.hIcon);
            }

            if (hr >= 0 && !string.IsNullOrWhiteSpace(info.szPath))
            {
                return new JumpListIcon(info.szPath, info.iIcon);
            }
        }
        catch (Exception error) when (error is DllNotFoundException or EntryPointNotFoundException)
        {
        }

        return new JumpListIcon(Path.Combine(Environment.SystemDirectory, "shell32.dll"), 3);
    }

    public IJumpListSession? TryBeginSession(out int hresult)
    {
        var clsid = ShellClsid.DestinationList;
        var iid = ShellClsid.ICustomDestinationList;
        hresult = NativeShellMethods.CoCreateInstance(ref clsid, 0, NativeShellMethods.ClsCtxInprocServer, ref iid, out object? obj);
        if (hresult < 0 || obj is not ICustomDestinationList list)
        {
            ReleaseComObject(obj);
            return null;
        }

        var arrayIid = ShellClsid.IObjectArray;
        hresult = list.BeginList(out uint minSlots, ref arrayIid, out object? removedObj);
        if (hresult < 0)
        {
            ReleaseComObject(removedObj);
            ReleaseComObject(list);
            return null;
        }

        IReadOnlyList<string> removedArgs = [];
        if (removedObj is IObjectArray removedArray)
        {
            try
            {
                removedArgs = ReadRemovedArguments(removedArray);
            }
            finally
            {
                ReleaseComObject(removedArray);
            }
        }
        else
        {
            ReleaseComObject(removedObj);
        }

        return new WindowsJumpListSession(list, minSlots, removedArgs);
    }

    /// <summary>
    /// Exercises real COM vtables for <c>IShellLinkW</c>, <c>IPropertyStore</c>, <c>IObjectCollection</c>,
    /// <c>IObjectArray</c>, and <c>PropVariant</c> purely in memory without modifying the Windows taskbar.
    /// </summary>
    internal static JumpListItemSpec RoundTripShellLinkInMemory(JumpListItemSpec spec)
    {
        ArgumentNullException.ThrowIfNull(spec);

        var collection = CreateCollection([spec], out int hr);
        if (collection is null)
        {
            Marshal.ThrowExceptionForHR(hr);
        }

        try
        {
            var array = (IObjectArray)collection!;
            hr = array.GetCount(out uint count);
            if (hr < 0)
            {
                Marshal.ThrowExceptionForHR(hr);
            }

            if (count != 1)
            {
                throw new InvalidOperationException($"Expected 1 item in object collection, got {count}.");
            }

            var shellLinkIid = ShellClsid.IShellLinkW;
            hr = array.GetAt(0, ref shellLinkIid, out object? itemObj);
            if (hr < 0 || itemObj is not IShellLinkW link)
            {
                ReleaseComObject(itemObj);
                Marshal.ThrowExceptionForHR(hr < 0 ? hr : unchecked((int)0x80004002));
                throw new InvalidOperationException();
            }

            try
            {
                var buffer = new StringBuilder(PathBufferCapacity);

                CheckHr(link.GetPath(buffer, buffer.Capacity, 0, 4)); // SLGP_RAWPATH
                string executablePath = buffer.ToString();

                buffer.Clear();
                CheckHr(link.GetArguments(buffer, buffer.Capacity));
                string arguments = buffer.ToString();

                buffer.Clear();
                CheckHr(link.GetDescription(buffer, buffer.Capacity));
                string description = buffer.ToString();

                buffer.Clear();
                CheckHr(link.GetIconLocation(buffer, buffer.Capacity, out int iconIndex));
                string iconPath = buffer.ToString();

                string title = string.Empty;
                if (link is IPropertyStore store)
                {
                    var key = ShellClsid.TitleKey;
                    var pv = default(PropVariant);
                    try
                    {
                        CheckHr(store.GetValue(ref key, out pv));
                        title = pv.AsString() ?? string.Empty;
                    }
                    finally
                    {
                        pv.Dispose();
                    }
                }

                return new JumpListItemSpec(title, description, executablePath, arguments, new JumpListIcon(iconPath, iconIndex));
            }
            finally
            {
                ReleaseComObject(link);
            }
        }
        finally
        {
            ReleaseComObject(collection);
        }
    }

    private static IObjectCollection? CreateCollection(IReadOnlyList<JumpListItemSpec> items, out int hresult)
    {
        var clsid = ShellClsid.EnumerableObjectCollection;
        var iid = ShellClsid.IObjectCollection;
        hresult = NativeShellMethods.CoCreateInstance(ref clsid, 0, NativeShellMethods.ClsCtxInprocServer, ref iid, out object? obj);
        if (hresult < 0 || obj is not IObjectCollection collection)
        {
            ReleaseComObject(obj);
            return null;
        }

        try
        {
            foreach (var item in items)
            {
                var link = CreateShellLink(item, out hresult);
                if (link is null)
                {
                    ReleaseComObject(collection);
                    return null;
                }

                try
                {
                    hresult = collection.AddObject(link);
                    if (hresult < 0)
                    {
                        ReleaseComObject(collection);
                        return null;
                    }
                }
                finally
                {
                    ReleaseComObject(link);
                }
            }

            hresult = 0;
            return collection;
        }
        catch
        {
            ReleaseComObject(collection);
            throw;
        }
    }

    private static IShellLinkW? CreateShellLink(JumpListItemSpec spec, out int hresult)
    {
        var clsid = ShellClsid.ShellLink;
        var iid = ShellClsid.IShellLinkW;
        hresult = NativeShellMethods.CoCreateInstance(ref clsid, 0, NativeShellMethods.ClsCtxInprocServer, ref iid, out object? obj);
        if (hresult < 0 || obj is not IShellLinkW link)
        {
            ReleaseComObject(obj);
            return null;
        }

        try
        {
            hresult = link.SetPath(spec.ExecutablePath);
            if (hresult < 0)
            {
                ReleaseComObject(link);
                return null;
            }

            string workingDirectory = Path.GetDirectoryName(spec.ExecutablePath) ?? string.Empty;
            hresult = link.SetWorkingDirectory(workingDirectory);
            if (hresult < 0)
            {
                ReleaseComObject(link);
                return null;
            }

            hresult = link.SetArguments(spec.Arguments);
            if (hresult < 0)
            {
                ReleaseComObject(link);
                return null;
            }

            hresult = link.SetDescription(spec.Description);
            if (hresult < 0)
            {
                ReleaseComObject(link);
                return null;
            }

            hresult = link.SetIconLocation(spec.Icon.Path, spec.Icon.Index);
            if (hresult < 0)
            {
                ReleaseComObject(link);
                return null;
            }

            if (link is IPropertyStore store)
            {
                var key = ShellClsid.TitleKey;
                var pv = PropVariant.FromString(spec.Title);
                try
                {
                    hresult = store.SetValue(ref key, ref pv);
                    if (hresult < 0)
                    {
                        ReleaseComObject(link);
                        return null;
                    }

                    hresult = store.Commit();
                    if (hresult < 0)
                    {
                        ReleaseComObject(link);
                        return null;
                    }
                }
                finally
                {
                    pv.Dispose();
                }
            }

            hresult = 0;
            return link;
        }
        catch
        {
            ReleaseComObject(link);
            throw;
        }
    }

    private static List<string> ReadRemovedArguments(IObjectArray removedArray)
    {
        var result = new List<string>();
        if (removedArray.GetCount(out uint count) < 0 || count == 0)
        {
            return result;
        }

        var shellLinkIid = ShellClsid.IShellLinkW;
        var buffer = new StringBuilder(PathBufferCapacity);

        for (uint i = 0; i < count; i++)
        {
            if (removedArray.GetAt(i, ref shellLinkIid, out object? itemObj) >= 0 && itemObj is not null)
            {
                try
                {
                    if (itemObj is IShellLinkW link)
                    {
                        buffer.Clear();
                        if (link.GetArguments(buffer, buffer.Capacity) >= 0 && buffer.Length > 0)
                        {
                            result.Add(buffer.ToString());
                        }
                    }
                }
                finally
                {
                    ReleaseComObject(itemObj);
                }
            }
        }

        return result;
    }

    private static void CheckHr(int hresult)
    {
        if (hresult < 0)
        {
            Marshal.ThrowExceptionForHR(hresult);
        }
    }

    private static void ReleaseComObject(object? comObject)
    {
        if (comObject is not null && Marshal.IsComObject(comObject))
        {
            _ = Marshal.FinalReleaseComObject(comObject);
        }
    }

    private sealed class WindowsJumpListSession(
        ICustomDestinationList list,
        uint minSlots,
        IReadOnlyList<string> removedArguments) : IJumpListSession
    {
        private bool _completed;
        private bool _disposed;

        public uint MinSlots { get; } = minSlots;

        public IReadOnlyList<string> RemovedArguments { get; } = removedArguments;

        public bool TryAppendCategory(JumpListCategorySpec category, out int hresult)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (category.Items.Count == 0)
            {
                hresult = 0;
                return true;
            }

            var collection = CreateCollection(category.Items, out hresult);
            if (collection is null)
            {
                return false;
            }

            try
            {
                hresult = list.AppendCategory(category.Title, collection);
                return hresult >= 0;
            }
            finally
            {
                ReleaseComObject(collection);
            }
        }

        public bool TryAddUserTasks(IReadOnlyList<JumpListItemSpec> tasks, out int hresult)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (tasks.Count == 0)
            {
                hresult = 0;
                return true;
            }

            var collection = CreateCollection(tasks, out hresult);
            if (collection is null)
            {
                return false;
            }

            try
            {
                hresult = list.AddUserTasks(collection);
                return hresult >= 0;
            }
            finally
            {
                ReleaseComObject(collection);
            }
        }

        public bool TryCommit(out int hresult)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            hresult = list.CommitList();
            if (hresult >= 0)
            {
                _completed = true;
                return true;
            }

            return false;
        }

        public void Abort()
        {
            if (!_disposed && !_completed)
            {
                _completed = true;
                _ = list.AbortList();
            }
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            if (!_completed)
            {
                _completed = true;
                _ = list.AbortList();
            }

            _disposed = true;
            ReleaseComObject(list);
        }
    }
}
