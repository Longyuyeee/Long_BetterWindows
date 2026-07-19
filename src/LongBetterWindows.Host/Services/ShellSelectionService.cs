using System.IO;
using System.Runtime.InteropServices;
using LongBetterWindows.Host.Capabilities;
using LongBetterWindows.Host.Contracts;
using Serilog;

namespace LongBetterWindows.Host.Services
{
    public class ShellSelectionService : IShellSelectionService
    {
        public Task<HostApiResponse<string>> GetActiveExplorerFolderPathAsync()
            => GetExplorerFolderPathForWindowAsync(Shell32.GetForegroundWindow());

        public Task<HostApiResponse<string>> GetExplorerFolderPathForWindowAsync(
            IntPtr foregroundHwnd)
        {
            return Task.Run(() =>
            {
                try
                {
                    var shellWindows = GetShellWindows();

                    if (shellWindows == null)
                    {
                        return HostApiResponse<string>.Failure(
                            ApiErrorCode.ShellWindowNotFound, "无法获取 ShellWindows。");
                    }

                    foreach (object shellWindow in shellWindows)
                    {
                        try
                        {
                            if (shellWindow is not IWebBrowserApp browser)
                                continue;
                            var browserHwnd = (IntPtr)(int)browser.HWND;

                            if (browserHwnd == foregroundHwnd)
                            {
                                var path = GetFolderPathFromBrowser(browser);
                                if (path != null)
                                {
                                    return HostApiResponse<string>.Success(path);
                                }
                            }
                        }
                        catch
                        {
                            continue;
                        }
                    }

                    return HostApiResponse<string>.Failure(
                        ApiErrorCode.ShellWindowNotFound, "未找到活跃的资源管理器窗口。");
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "获取 Explorer 文件夹路径失败");
                    return HostApiResponse<string>.Failure(
                        ApiErrorCode.Unknown, ex.Message);
                }
            });
        }

        public Task<HostApiResponse<List<string>>> GetSelectedItemsAsync()
            => GetSelectedItemsForWindowAsync(Shell32.GetForegroundWindow());

        public Task<HostApiResponse<List<string>>> GetSelectedItemsForWindowAsync(
            IntPtr foregroundHwnd)
        {
            return Task.Run(() =>
            {
                try
                {
                    var shellWindows = GetShellWindows();

                    if (shellWindows == null)
                    {
                        return HostApiResponse<List<string>>.Failure(
                            ApiErrorCode.ShellWindowNotFound, "无法获取 ShellWindows。");
                    }

                    foreach (object shellWindow in shellWindows)
                    {
                        try
                        {
                            if (shellWindow is not IWebBrowserApp browser)
                                continue;
                            var browserHwnd = (IntPtr)(int)browser.HWND;

                            if (browserHwnd == foregroundHwnd)
                            {
                                var folderView = GetFolderView(browser);
                                if (folderView == null)
                                    continue;

                                var items = new List<string>();
                                var iidShellItemArray = ShellIIDs.IShellItemArray;

                                IntPtr shellItemArrayPtr;
                                int hr = folderView.Items(
                                    ShellConsts.SVGIO_SELECTION,
                                    ref iidShellItemArray,
                                    out shellItemArrayPtr);

                                if (hr != 0 || shellItemArrayPtr == IntPtr.Zero)
                                {
                                    return HostApiResponse<List<string>>.Failure(
                                        ApiErrorCode.ShellSelectionEmpty, "未选中任何项目。");
                                }

                                try
                                {
                                    var shellItemArray = (IShellItemArray)Marshal
                                        .GetTypedObjectForIUnknown(
                                            shellItemArrayPtr, typeof(IShellItemArray));

                                    if (shellItemArray.GetCount(out uint count) != 0 || count == 0)
                                    {
                                        return HostApiResponse<List<string>>.Failure(
                                            ApiErrorCode.ShellSelectionEmpty, "未选中任何项目。");
                                    }

                                    for (uint i = 0; i < count; i++)
                                    {
                                        try
                                        {
                                            if (shellItemArray.GetItemAt(i, out object itemObj) != 0)
                                                continue;

                                            if (itemObj is IShellItem shellItem)
                                            {
                                                if (shellItem.GetDisplayName(
                                                    Shell32.SIGDN_FILESYSPATH,
                                                    out IntPtr namePtr) == 0 && namePtr != IntPtr.Zero)
                                                {
                                                    var path = Marshal.PtrToStringUni(namePtr);
                                                    Marshal.FreeCoTaskMem(namePtr);

                                                    if (!string.IsNullOrEmpty(path))
                                                        items.Add(path);
                                                }
                                            }
                                        }
                                        catch
                                        {
                                            continue;
                                        }
                                    }
                                }
                                finally
                                {
                                    Marshal.Release(shellItemArrayPtr);
                                }

                                if (items.Count == 0)
                                {
                                    return HostApiResponse<List<string>>.Failure(
                                        ApiErrorCode.ShellSelectionEmpty, "未选中任何项目。");
                                }

                                return HostApiResponse<List<string>>.Success(items);
                            }
                        }
                        catch
                        {
                            continue;
                        }
                    }

                    return HostApiResponse<List<string>>.Failure(
                        ApiErrorCode.ShellWindowNotFound, "未找到活跃的资源管理器窗口。");
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "获取选中项目失败");
                    return HostApiResponse<List<string>>.Failure(
                        ApiErrorCode.Unknown, ex.Message);
                }
            });
        }

        public Task<HostApiResponse<ScreenRect>> GetSelectedItemScreenRectAsync()
        {
            return Task.Run(() =>
            {
                try
                {
                    var shellWindows = GetShellWindows();

                    if (shellWindows == null)
                    {
                        return HostApiResponse<ScreenRect>.Failure(
                            ApiErrorCode.ShellWindowNotFound, "无法获取 ShellWindows。");
                    }

                    var foregroundHwnd = Shell32.GetForegroundWindow();

                    foreach (object shellWindow in shellWindows)
                    {
                        try
                        {
                            if (shellWindow is not IWebBrowserApp browser)
                                continue;
                            var browserHwnd = (IntPtr)(int)browser.HWND;

                            if (browserHwnd == foregroundHwnd)
                            {
                                var folderView = GetFolderView(browser);
                                if (folderView == null)
                                    continue;

                                int selectedIndex;
                                if (folderView.GetSelectionMarkedItem(out selectedIndex) != 0)
                                {
                                    if (folderView.GetFocusedItem(out selectedIndex) != 0)
                                        continue;
                                }

                                if (folderView.Item(selectedIndex, out var pidl) != 0)
                                    continue;

                                if (pidl == IntPtr.Zero)
                                    continue;

                                POINT point;
                                int hr = folderView.GetItemPosition(pidl, out point);
                                Shell32.ILFree(pidl);

                                if (hr != 0)
                                    continue;

                                if (!Shell32.GetWindowRect(browserHwnd, out RECT windowRect))
                                {
                                    continue;
                                }

                                return HostApiResponse<ScreenRect>.Success(new ScreenRect
                                {
                                    X = windowRect.Left + point.X,
                                    Y = windowRect.Top + point.Y,
                                    Width = 0,
                                    Height = 0,
                                });
                            }
                        }
                        catch
                        {
                            continue;
                        }
                    }

                    return HostApiResponse<ScreenRect>.Failure(
                        ApiErrorCode.ShellWindowNotFound, "未找到活跃的资源管理器窗口。");
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "获取项目屏幕坐标失败");
                    return HostApiResponse<ScreenRect>.Failure(
                        ApiErrorCode.Unknown, ex.Message);
                }
            });
        }

        private static IShellWindows? GetShellWindows()
        {
            try
            {
                var shellWindowsType = Type.GetTypeFromCLSID(
                    typeof(ShellWindowsClass).GUID);

                if (shellWindowsType == null)
                    return null;

                return Activator.CreateInstance(shellWindowsType) as IShellWindows;
            }
            catch (Exception ex)
            {
                Log.Error(ex, "创建 ShellWindows COM 对象失败");
                return null;
            }
        }

        private static string? GetFolderPathFromBrowser(IWebBrowserApp browser)
        {
            try
            {
                var url = browser.LocationURL;

                if (string.IsNullOrEmpty(url))
                    return null;

                if (url.StartsWith("file://", StringComparison.OrdinalIgnoreCase))
                {
                    var path = Uri.UnescapeDataString(url.Substring("file://".Length));
                    path = path.Replace('/', Path.DirectorySeparatorChar);
                    return path;
                }

                return null;
            }
            catch
            {
                return null;
            }
        }

        private static IFolderView2? GetFolderView(IWebBrowserApp browser)
        {
            try
            {
                var doc = browser.GetType().InvokeMember("Document",
                    System.Reflection.BindingFlags.GetProperty,
                    null, browser, null);

                if (doc == null)
                    return null;

                if (doc is IShellBrowser shellBrowser)
                {
                    IntPtr shellViewPtr;
                    if (shellBrowser.QueryActiveShellView(out shellViewPtr) != 0)
                        return null;

                    if (shellViewPtr == IntPtr.Zero)
                        return null;

                    try
                    {
                        var shellView = (IShellView)Marshal.GetTypedObjectForIUnknown(
                            shellViewPtr, typeof(IShellView));

                        if (shellView == null)
                            return null;

                        IntPtr folderViewPtr;
                        var guid = ShellIIDs.IFolderView2;
                        if (shellView.GetItemObject(0, ref guid, out folderViewPtr) != 0)
                            return null;

                        if (folderViewPtr == IntPtr.Zero)
                            return null;

                        return (IFolderView2)Marshal.GetTypedObjectForIUnknown(
                            folderViewPtr, typeof(IFolderView2));
                    }
                    finally
                    {
                        Marshal.Release(shellViewPtr);
                    }
                }

                return null;
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "获取 IFolderView2 失败");
                return null;
            }
        }

        private static string? PidlToPath(IntPtr pidl)
        {
            if (Shell32.SHGetNameFromIDList(pidl, 0x80058000, out var namePtr) != 0)
                return null;

            try
            {
                var path = Marshal.PtrToStringUni(namePtr);
                return path;
            }
            finally
            {
                Marshal.FreeCoTaskMem(namePtr);
            }
        }
    }
}
