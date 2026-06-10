using System.Runtime.InteropServices;

namespace LongBetterWindows.Host.Services
{
    [ComImport, Guid("9BA05972-F6A8-11CF-A442-00A0C90A8F39")]
    internal class ShellWindowsClass { }

    [ComImport, Guid("85CB6900-4D95-11CF-960C-0080C7F4EE85")]
    [InterfaceType(ComInterfaceType.InterfaceIsIDispatch)]
    internal interface IShellWindows
    {
        int Count { get; }
        [DispId(0)] object Item(object index);
        [DispId(-4)] System.Collections.IEnumerator GetEnumerator();
    }

    [ComImport, Guid("D8F015C0-C278-11CE-A49E-444553540000")]
    [InterfaceType(ComInterfaceType.InterfaceIsIDispatch)]
    internal interface IWebBrowserApp
    {
        [DispId(200)] object HWND { get; }
        [DispId(210)] string LocationURL { get; }
        [DispId(211)] string LocationName { get; }
        [DispId(300)] void Quit();
    }

    [ComImport, Guid("000214E2-0000-0000-C000-000000000046")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IShellBrowser
    {
        [PreserveSig] int GetWindow(out IntPtr phwnd);
        [PreserveSig] int ContextSensitiveHelp(int fEnterMode);
        [PreserveSig] int InsertMenusSB(IntPtr hmenuShared, out IntPtr lpMenuWidths);
        [PreserveSig] int SetMenuSB(IntPtr hmenuShared, IntPtr holemenuRes, IntPtr hwndActiveObject);
        [PreserveSig] int RemoveMenusSB(IntPtr hmenuShared);
        [PreserveSig] int SetStatusTextSB(IntPtr pszStatusText);
        [PreserveSig] int EnableModelessSB(int fEnable);
        [PreserveSig] int TranslateAcceleratorSB(IntPtr pmsg, ushort wID);
        [PreserveSig] int BrowseObject(IntPtr pidl, uint wFlags);
        [PreserveSig] int GetViewStateStream(int grfMode, out IntPtr ppStrm);
        [PreserveSig] int GetControlWindow(uint id, out IntPtr phwnd);
        [PreserveSig] int SendControlMsg(uint id, uint uMsg, ushort wParam, int lParam, out int pret);
        [PreserveSig] int QueryActiveShellView(out IntPtr ppshv);
        [PreserveSig] int OnViewWindowActive(IntPtr pshv);
        [PreserveSig] int SetToolbarItems(IntPtr lpButtons, uint nButtons, uint uFlags);
    }

    [ComImport, Guid("000214E1-0000-0000-C000-000000000046")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IShellView
    {
        [PreserveSig] int GetWindow(out IntPtr phwnd);
        [PreserveSig] int ContextSensitiveHelp(int fEnterMode);
        [PreserveSig] int TranslateAccelerator(IntPtr pmsg);
        [PreserveSig] int EnableModeless(int fEnable);
        [PreserveSig] int UIActivate(uint uState);
        [PreserveSig] int Refresh();
        [PreserveSig] int CreateViewWindow(IntPtr psvPrevious, IntPtr pfs, IntPtr psb,
            ref RECT prcView, out IntPtr phWnd);
        [PreserveSig] int DestroyViewWindow();
        [PreserveSig] int GetCurrentInfo(ref IntPtr pfs);
        [PreserveSig] int AddPropertySheetPages(int dwReserved, IntPtr pfn,
            int lparam);
        [PreserveSig] int SaveViewState();
        [PreserveSig] int SelectItem(IntPtr pidlItem, uint uFlags);
        [PreserveSig] int GetItemObject(uint uItem, ref Guid riid, out IntPtr ppv);
    }

    [ComImport, Guid("1DE3DF80-4FED-4A71-B8E0-8C36C0BDD16F")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IFolderView2
    {
        [PreserveSig] int GetCurrentFolder(out IntPtr ppidl);
        [PreserveSig] int SetCurrentFolder(IntPtr pidl);
        [PreserveSig] int GetFolder(ref Guid riid, out IntPtr ppv);
        [PreserveSig] int Item(int iItemIndex, out IntPtr ppidl);
        [PreserveSig] int ItemCount(uint uFlags, out int pcItems);
        [PreserveSig] int Items(uint uFlags, ref Guid riid, out IntPtr ppv);
        [PreserveSig] int GetSelectionMarkedItem(out int piItem);
        [PreserveSig] int GetFocusedItem(out int piItem);
        [PreserveSig] int GetItemPosition(IntPtr pidl, out POINT ppt);
        [PreserveSig] int GetSpacing(out POINT pSz);
        [PreserveSig] int GetDefaultSpacing(out POINT pSz);
        [PreserveSig] int GetAutoArrange();
        [PreserveSig] int SelectItem(int iItem, uint dwFlags);
        [PreserveSig] int SelectAndPositionItems(uint cidl, IntPtr apidl,
            IntPtr apt, uint dwFlags);
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct RECT
    {
        public int Left, Top, Right, Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct POINT
    {
        public int X, Y;
    }

    internal static class ShellIIDs
    {
        public static readonly Guid IFolderView2 = new("1DE3DF80-4FED-4A71-B8E0-8C36C0BDD16F");
        public static readonly Guid IShellBrowser = new("000214E2-0000-0000-C000-000000000046");
    }

    internal static class Shell32
    {
        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        public static extern IntPtr ILCreateFromPathW(string pszPath);

        [DllImport("shell32.dll")]
        public static extern void ILFree(IntPtr pidl);

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        public static extern int SHGetNameFromIDList(IntPtr pidl, uint sigdnName, out IntPtr ppszName);

        [DllImport("shell32.dll")]
        public static extern int SHGetDesktopFolder(out IntPtr ppshf);

        [DllImport("user32.dll")]
        public static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

        public const uint SIGDN_FILESYSPATH = 0x80058000;
    }
}
