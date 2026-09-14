using System.ComponentModel;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.Win32;
using WinRT.Interop;

namespace ScreenEnglish;

public static class Native
{
    [StructLayout(LayoutKind.Sequential)] public struct POINT { public int X, Y; public POINT(int x, int y) { X = x; Y = y; } }
    [StructLayout(LayoutKind.Sequential)] public struct RECT {
        public int Left, Top, Right, Bottom;
        public RECT(int left, int top, int right, int bottom) { Left = left; Top = top; Right = right; Bottom = bottom; }
        public int Width => Right - Left; public int Height => Bottom - Top;
        public bool Contains(POINT p) => p.X >= Left && p.Y >= Top && p.X < Right && p.Y < Bottom;
    }
    [StructLayout(LayoutKind.Sequential)] private struct MONITORINFO { public int Size; public RECT Monitor, Work; public uint Flags; }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] public struct NOTIFYICONDATA {
        public uint Size; public nint Window; public uint Id, Flags, Callback; public nint Icon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string Tip;
        public uint State, StateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string Info;
        public uint Timeout;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string Title;
        public uint InfoFlags; public Guid Guid; public nint BalloonIcon;
    }
    public delegate nint SubclassProc(nint hwnd, uint msg, nuint wParam, nint lParam, nuint id, nuint data);
    [DllImport("comctl32.dll")] public static extern bool SetWindowSubclass(nint hwnd, SubclassProc proc, nuint id, nuint data);
    [DllImport("comctl32.dll")] public static extern bool RemoveWindowSubclass(nint hwnd, SubclassProc proc, nuint id);
    [DllImport("comctl32.dll")] public static extern nint DefSubclassProc(nint hwnd, uint msg, nuint wParam, nint lParam);
    [DllImport("user32.dll", SetLastError = true)] public static extern bool RegisterHotKey(nint hwnd, int id, uint modifiers, uint key);
    [DllImport("user32.dll")] public static extern bool UnregisterHotKey(nint hwnd, int id);
    [DllImport("user32.dll")] public static extern short GetAsyncKeyState(int key);
    [DllImport("user32.dll")] public static extern bool GetCursorPos(out POINT point);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(nint hwnd, out RECT rect);
    [DllImport("user32.dll")] public static extern bool IsIconic(nint hwnd);
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(nint hwnd);
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(nint hwnd);
    [DllImport("user32.dll")] public static extern bool ShowWindow(nint hwnd, int cmd);
    [DllImport("user32.dll")] public static extern bool SetWindowPos(nint hwnd, nint after, int x, int y, int w, int h, uint flags);
    [DllImport("user32.dll")] private static extern nint MonitorFromPoint(POINT point, uint flags);
    [DllImport("shcore.dll")] private static extern int GetDpiForMonitor(nint monitor, int type, out uint x, out uint y);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern bool GetMonitorInfo(nint monitor, ref MONITORINFO info);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] private static extern nint GetWindowLongPtr(nint hwnd, int index);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")] private static extern nint SetWindowLongPtr(nint hwnd, int index, nint value);
    [DllImport("user32.dll")] private static extern bool SetLayeredWindowAttributes(nint hwnd, uint key, byte alpha, uint flags);
    [DllImport("user32.dll")] public static extern uint GetDpiForWindow(nint hwnd);
    [DllImport("user32.dll")] public static extern nint WindowFromPoint(POINT point);
    [DllImport("user32.dll")] public static extern nint GetAncestor(nint hwnd, uint flags);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern uint RegisterWindowMessage(string message);
    [DllImport("user32.dll")] public static extern nint LoadIcon(nint instance, nint name);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern nint LoadImage(nint instance, string name, uint type, int width, int height, uint flags);
    [DllImport("user32.dll")] public static extern bool DestroyIcon(nint icon);
    [DllImport("user32.dll")] public static extern nint CreatePopupMenu();
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern bool AppendMenu(nint menu, uint flags, nuint id, string text);
    [DllImport("user32.dll")] public static extern uint TrackPopupMenu(nint menu, uint flags, int x, int y, int reserved, nint hwnd, nint rect);
    [DllImport("user32.dll")] public static extern bool DestroyMenu(nint menu);
    [DllImport("user32.dll")] public static extern bool PostMessage(nint hwnd, uint message, nuint wParam, nint lParam);
    [DllImport("user32.dll")] public static extern bool ReleaseCapture();
    [DllImport("user32.dll")] public static extern nint SendMessage(nint hwnd, uint message, nuint wParam, nint lParam);
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] public static extern bool Shell_NotifyIcon(uint message, ref NOTIFYICONDATA data);
    [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(nint hwnd, int attribute, ref int value, int size);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int MessageBox(nint hwnd, string text, string caption, uint flags);

    public static POINT Cursor { get { GetCursorPos(out var p); return p; } }
    public static bool Held(string modifier) => (GetAsyncKeyState(modifier switch { "Alt" => 0x12, "Shift" => 0x10, _ => 0x11 }) & 0x8000) != 0;
    public static RECT Monitor(POINT point, bool work = false) {
        var info = new MONITORINFO { Size = Marshal.SizeOf<MONITORINFO>() };
        if (!GetMonitorInfo(MonitorFromPoint(point, 2), ref info)) throw new Win32Exception();
        return work ? info.Work : info.Monitor;
    }
    public static nint Hwnd(Window window) => WindowNative.GetWindowHandle(window);
    public static double Scale(POINT point) => GetDpiForMonitor(MonitorFromPoint(point, 2), 0, out uint dpi, out _) == 0 ? dpi / 96.0 : 1;
    public static void ToolWindow(Window window, bool noActivate = false) {
        window.AppWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, "Assets", "ScreenEnglish.ico"));
        // WS_EX_TOOLWINDOW also works on systems where IsShownInSwitchers is unavailable.
        nint hwnd = Hwnd(window);
        long style = (long)GetWindowLongPtr(hwnd, -20);
        SetWindowLongPtr(hwnd, -20, (nint)((style | 0x80L | (noActivate ? 0x08000000L : 0L)) & ~0x40000L));
        int corner = 3; DwmSetWindowAttribute(hwnd, 33, ref corner, sizeof(int));
    }
    public static void Opacity(Window window, int percent) {
        nint hwnd = Hwnd(window);
        long style = (long)GetWindowLongPtr(hwnd, -20);
        if (percent >= 100) SetWindowLongPtr(hwnd, -20, (nint)(style & ~0x80000L));
        else {
            SetWindowLongPtr(hwnd, -20, (nint)(style | 0x80000));
            SetLayeredWindowAttributes(hwnd, 0, (byte)(Math.Clamp(percent, 50, 100) * 255 / 100), 2);
        }
    }
    public static void Borderless(Window window, bool topmost = true) {
        if (window.AppWindow.Presenter is OverlappedPresenter presenter) {
            presenter.SetBorderAndTitleBar(false, false); presenter.IsResizable = false;
            presenter.IsMaximizable = false; presenter.IsAlwaysOnTop = topmost;
        }
        ToolWindow(window);
    }
    public static void Place(Window window, RECT rect, bool activate = true) {
        SetWindowPos(Hwnd(window), 0, rect.Left, rect.Top, rect.Width, rect.Height, 0x40 | (activate ? 0u : 0x10u));
        if (activate) { window.Activate(); SetForegroundWindow(Hwnd(window)); }
    }
    public static void Owner(Window window, Window? owner) => SetWindowLongPtr(Hwnd(window), -8, owner is null ? 0 : Hwnd(owner));
    public static RECT Near(RECT anchor, int width, int height) {
        var work = Monitor(new POINT(anchor.Left, anchor.Top), true);
        width = Math.Min(width, work.Width); height = Math.Min(height, work.Height);
        int x = anchor.Right + 12;
        if (x + width > work.Right) x = anchor.Left - width - 12;
        x = Math.Clamp(x, work.Left, work.Right - width);
        int y = Math.Clamp(anchor.Top, work.Top, work.Bottom - height);
        return new RECT(x, y, x + width, y + height);
    }
    public static byte[] Screenshot(RECT rect) {
        using var bitmap = new Bitmap(rect.Width, rect.Height, PixelFormat.Format32bppArgb);
        using (var graphics = Graphics.FromImage(bitmap)) graphics.CopyFromScreen(rect.Left, rect.Top, 0, 0, bitmap.Size, CopyPixelOperation.SourceCopy);
        using var stream = new MemoryStream(); bitmap.Save(stream, ImageFormat.Png); return stream.ToArray();
    }
    public static nint TrayIcon() {
        nint icon = LoadImage(0, Path.Combine(AppContext.BaseDirectory, "Assets", "ScreenEnglish.ico"), 1, 32, 32, 0x10);
        if (icon == 0) throw new Win32Exception();
        return icon;
    }
    public static byte[] Crop(byte[] image, RECT rect) {
        using var input = new MemoryStream(image); using var bitmap = new Bitmap(input);
        using var cropped = bitmap.Clone(new Rectangle(rect.Left, rect.Top, rect.Width, rect.Height), PixelFormat.Format32bppArgb);
        using var output = new MemoryStream(); cropped.Save(output, ImageFormat.Png); return output.ToArray();
    }
    public static (uint Modifiers, uint Key) ParseHotkey(string text) {
        var parts = text.Split('+', StringSplitOptions.TrimEntries);
        if (parts.Length < 2 || parts.Distinct(StringComparer.OrdinalIgnoreCase).Count() != parts.Length) throw new Exception("Use a shortcut such as Ctrl+Shift+T.");
        uint modifiers = 0;
        foreach (string part in parts[..^1]) modifiers |= part.ToLowerInvariant() switch {
            "ctrl" => 2u, "shift" => 4u, "alt" => 1u, "win" => 8u, _ => throw new Exception("Modifiers: Ctrl, Shift, Alt, Win.")
        };
        string key = parts[^1].ToUpperInvariant();
        if (key.Length == 1 && char.IsAsciiLetterOrDigit(key[0])) return (modifiers, key[0]);
        if (key.StartsWith('F') && int.TryParse(key[1..], out int f) && f is >= 1 and <= 24) return (modifiers, (uint)(0x70 + f - 1));
        throw new Exception("Shortcut keys must be A–Z, 0–9 or F1–F24.");
    }
    private const string RunPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    public static bool StartupEnabled { get { using var key = Registry.CurrentUser.OpenSubKey(RunPath); return key?.GetValue("ScreenEnglish") is string; } }
    public static void SetStartup(bool enabled) {
        using var key = Registry.CurrentUser.CreateSubKey(RunPath);
        if (enabled) key.SetValue("ScreenEnglish", "\"" + Environment.ProcessPath + "\"");
        else key.DeleteValue("ScreenEnglish", false);
    }
}
