using System.Runtime.InteropServices;
using Microsoft.UI.Xaml;
using Windows.Graphics;
using WinRT.Interop;

namespace Vocabulary;

internal static class Native
{
    [DllImport("dwmapi")] internal static extern int DwmSetWindowAttribute(nint hwnd, int attribute, ref int value, int size);
    internal delegate bool EnumProc(nint hwnd, nint data);
    internal delegate nint SubclassProc(nint hwnd, uint msg, nint wp, nint lp, nuint id, nuint data);
    [DllImport("user32")] internal static extern bool EnumWindows(EnumProc callback, nint data);
    [DllImport("user32", CharSet = CharSet.Unicode)] internal static extern nint FindWindowEx(nint parent, nint after, string cls, string? title);
    [DllImport("user32")] internal static extern nint GetShellWindow();
    [DllImport("user32")] internal static extern uint GetWindowThreadProcessId(nint hwnd, out uint pid);
    [DllImport("user32", EntryPoint = "GetWindowLongPtrW")] internal static extern nint GetLong(nint hwnd, int index);
    [DllImport("user32", EntryPoint = "SetWindowLongPtrW")] internal static extern nint SetLong(nint hwnd, int index, nint value);
    [DllImport("user32")] internal static extern bool SetWindowPos(nint hwnd, nint after, int x, int y, int w, int h, uint flags);
    [DllImport("user32")] internal static extern bool IsWindow(nint hwnd);
    [DllImport("user32")] internal static extern bool SetForegroundWindow(nint hwnd);
    [DllImport("user32")] internal static extern bool GetCursorPos(out Point point);
    [DllImport("user32")] internal static extern uint GetDpiForWindow(nint hwnd);
    [DllImport("user32")] internal static extern nint MonitorFromRect(ref Rect rect, uint flags);
    [DllImport("user32", CharSet = CharSet.Unicode)] internal static extern bool GetMonitorInfo(nint monitor, ref MonitorInfo info);
    [DllImport("user32", CharSet = CharSet.Unicode)] internal static extern uint RegisterWindowMessage(string name);
    [DllImport("comctl32")] internal static extern bool SetWindowSubclass(nint hwnd, SubclassProc proc, nuint id, nuint data);
    [DllImport("comctl32")] internal static extern bool RemoveWindowSubclass(nint hwnd, SubclassProc proc, nuint id);
    [DllImport("comctl32")] internal static extern nint DefSubclassProc(nint hwnd, uint msg, nint wp, nint lp);
    [DllImport("user32")] internal static extern nint CreatePopupMenu();
    [DllImport("user32", CharSet = CharSet.Unicode)] internal static extern bool AppendMenu(nint menu, uint flags, nuint id, string? label);
    [DllImport("user32")] internal static extern int TrackPopupMenu(nint menu, uint flags, int x, int y, int reserved, nint hwnd, nint rect);
    [DllImport("user32")] internal static extern bool DestroyMenu(nint menu);
    [DllImport("user32", CharSet = CharSet.Unicode)] internal static extern nint LoadImage(nint instance, string path, uint type, int w, int h, uint flags);
    [DllImport("user32")] internal static extern bool DestroyIcon(nint icon);
    [DllImport("shell32", CharSet = CharSet.Unicode, EntryPoint = "Shell_NotifyIconW")] internal static extern bool Notify(uint action, ref NotifyData data);
    [DllImport("user32", CharSet = CharSet.Unicode)] internal static extern int MessageBox(nint hwnd, string text, string title, uint flags);
    [StructLayout(LayoutKind.Sequential)] internal struct Point { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)] internal struct Rect { public int L, T, R, B; }
    [StructLayout(LayoutKind.Sequential)] internal struct MonitorInfo { public int Size; public Rect Bounds, Work; public uint Flags; }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct NotifyData
    {
        public int Size; public nint Window; public uint Id, Flags, Callback; public nint Icon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string Tip;
        public uint State, StateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string Info;
        public uint Timeout;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string InfoTitle;
        public uint InfoFlags; public Guid Guid; public nint BalloonIcon;
    }
    internal static nint DesktopView()
    {
        var shell = GetShellWindow();
        if (shell == 0) return 0;
        GetWindowThreadProcessId(shell, out uint shellPid);
        nint view = 0;
        EnumWindows((hwnd, _) =>
        {
            GetWindowThreadProcessId(hwnd, out uint pid);
            if (pid == shellPid) view = FindWindowEx(hwnd, 0, "SHELLDLL_DefView", null);
            return view == 0;
        }, 0);
        return view;
    }
    internal static RectInt32 VisibleRect(int x, int y, int width, int height)
    {
        Rect rect = new() { L = x, T = y, R = x + width, B = y + height };
        MonitorInfo info = new() { Size = Marshal.SizeOf<MonitorInfo>() };
        if (!GetMonitorInfo(MonitorFromRect(ref rect, 2), ref info)) return new(x, y, width, height);
        var a = info.Work;
        width = Math.Min(width, a.R - a.L); height = Math.Min(height, a.B - a.T);
        return new(Math.Clamp(x, a.L, a.R - width), Math.Clamp(y, a.T, a.B - height), width, height);
    }
}
internal interface IDesktopWidgetHost { bool Attach(); void RestoreBounds(); }
internal sealed class DesktopHost(Window window) : IDesktopWidgetHost, IDisposable
{
    private readonly nint hwnd = WindowNative.GetWindowHandle(window);
    private readonly Native.SubclassProc filter = Filter;
    private bool attached;
    public bool Attach()
    {
        nint view = Native.DesktopView();
        if (attached && view != 0 && Native.GetLong(hwnd, -8) == view) return true;
        attached = false;
        long style = Native.GetLong(hwnd, -20);
        Native.SetLong(hwnd, -20, (nint)((style | 0x80) & ~0x40000L));
        if (view != 0)
        {
            Native.SetLong(hwnd, -8, view);
            attached = Native.GetLong(hwnd, -8) == view;
        }
        if (attached)
        {
            Native.SetLong(hwnd, -20, Native.GetLong(hwnd, -20) | 0x08000000);
            Native.SetWindowSubclass(hwnd, filter, 2, 0);
            Native.SetWindowPos(hwnd, 1, 0, 0, 0, 0, 0x13);
        }
        else
        {
            Native.SetLong(hwnd, -20, Native.GetLong(hwnd, -20) & ~0x08000000);
            Native.RemoveWindowSubclass(hwnd, filter, 2);
            Native.SetLong(hwnd, -8, 0);
        }
        return attached;
    }
    private static nint Filter(nint h, uint m, nint w, nint l, nuint i, nuint d)
    {
        if (m == 0x21) return 3; // MA_NOACTIVATE keeps clicks on the desktop.
        nint result = Native.DefSubclassProc(h, m, w, l);
        if (m == 0x24 && l != 0)
        {
            int dpi = (int)Native.GetDpiForWindow(h);
            Marshal.WriteInt32(l, 24, 300 * dpi / 96);
            Marshal.WriteInt32(l, 28, 200 * dpi / 96);
        }
        return result;
    }
    public void RestoreBounds()
    {
        var w = window.AppWindow;
        var p = w.Position; var s = w.Size;
        w.MoveAndResize(Native.VisibleRect(p.X, p.Y, s.Width, s.Height));
    }
    public void Dispose()
    {
        if (!Native.IsWindow(hwnd)) return;
        Native.RemoveWindowSubclass(hwnd, filter, 2);
        Native.SetLong(hwnd, -8, 0);
    }
}
internal sealed class Tray : IDisposable
{
    private const uint Callback = 0x8000 + 67;
    private readonly uint shellReady = Native.RegisterWindowMessage("TaskbarCreated");
    private readonly Native.SubclassProc handler;
    private readonly App app;
    private Native.NotifyData data;
    public Tray(Window window, App app)
    {
        this.app = app;
        nint hwnd = WindowNative.GetWindowHandle(window);
        handler = Handle;
        data = new()
        {
            Size = Marshal.SizeOf<Native.NotifyData>(),
            Window = hwnd,
            Id = 1,
            Flags = 7,
            Callback = Callback,
            Icon = Native.LoadImage(0, App.IconPath, 1, 32, 32, 0x10),
            Tip = "四六级单词",
            Info = "",
            InfoTitle = ""
        };
        if (!Native.SetWindowSubclass(hwnd, handler, 1, 0)) throw new InvalidOperationException("无法连接托盘菜单。");
        Native.Notify(0, ref data);
    }
    private nint Handle(nint hwnd, uint msg, nint wp, nint lp, nuint id, nuint ctx)
    {
        if (msg == shellReady) { Native.Notify(0, ref data); app.Main.DispatcherQueue.TryEnqueue(app.RecoverDesktop); }
        if (msg == 0x7e) app.Main.DispatcherQueue.TryEnqueue(app.DisplayChanged);
        if (msg is 0x1a or 0x31a) app.Main.DispatcherQueue.TryEnqueue(app.Main.RefreshAppearance);
        if (msg == Callback)
        {
            int action = (int)lp;
            if (action == 0x202) app.Main.DispatcherQueue.TryEnqueue(app.ShowMain);
            if (action == 0x205) Menu(hwnd);
            return 0;
        }
        return Native.DefSubclassProc(hwnd, msg, wp, lp);
    }
    private void Menu(nint hwnd)
    {
        var menu = Native.CreatePopupMenu();
        try
        {
            Native.AppendMenu(menu, 0, 1, "打开主窗口");
            Native.AppendMenu(menu, app.Ready ? 0u : 1u, 2, app.Store.Load().WidgetVisible ? "隐藏桌面卡片" : "显示桌面卡片");
            Native.AppendMenu(menu, 0x800, 0, null);
            Native.AppendMenu(menu, 0, 3, "退出");
            Native.GetCursorPos(out var point);
            Native.SetForegroundWindow(hwnd);
            int item = Native.TrackPopupMenu(menu, 0x100 | 2, point.X, point.Y, 0, hwnd, 0);
            app.Main.DispatcherQueue.TryEnqueue(() => { if (item == 1) app.ShowMain(); if (item == 2) app.ToggleWidget(); if (item == 3) app.Quit(); });
        }
        finally { Native.DestroyMenu(menu); }
    }
    public void Dispose()
    {
        Native.Notify(2, ref data);
        Native.RemoveWindowSubclass(data.Window, handler, 1);
        Native.DestroyIcon(data.Icon);
    }
}




