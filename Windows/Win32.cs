using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;

namespace PrivacyScreen.Windows;

internal static class Win32
{
    private const int WsExToolwindow = 0x00000080;
    private const int WsExNoactivate = 0x08000000;
    private const int WsExTransparent = 0x00000020;
    private const int WsExLayered = 0x00080000;
    private const int DwMWaCloaked = 14;

    public const int ScreenOverlayLevelThreshold = 40;

    private static readonly IntPtr HwndTopmost = new(-1);
    private const uint SwpNoactivate = 0x0010;
    private const uint SwpShowwindow = 0x0040;

    public delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);
    private delegate bool MonitorEnumProc(IntPtr hMonitor, IntPtr hdc, ref Rect rect, IntPtr data);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr lprcClip, MonitorEnumProc lpfnEnum, IntPtr dwData);

    [DllImport("user32.dll")]
    private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MonitorInfo lpmi);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint uFlags);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool GetWindowRect(IntPtr hWnd, out Rect lpRect);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [DllImport("dwmapi.dll")]
    private static extern int DwmGetWindowAttribute(
        IntPtr hwnd,
        int dwAttribute,
        out int pvAttribute,
        int cbAttribute);

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorInfo
    {
        public int Size;
        public Rect Monitor;
        public Rect Work;
        public uint Flags;
    }

    internal readonly record struct WindowInfo(IntPtr Handle, Rectangle Bounds, string ProcessName, bool IsTarget);

    internal static int AddClickThroughStyles(int existingStyles)
    {
        return existingStyles | WsExToolwindow | WsExNoactivate | WsExTransparent | WsExLayered;
    }

    /// 물리 픽셀 기준 모니터 영역 목록(Per-Monitor-V2 인식 프로세스에서 GetWindowRect 와 좌표계 일치).
    internal static IReadOnlyList<Rectangle> EnumerateMonitorBounds()
    {
        var monitors = new List<Rectangle>();
        bool Callback(IntPtr hMonitor, IntPtr hdc, ref Rect rect, IntPtr data)
        {
            var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
            if (GetMonitorInfo(hMonitor, ref info))
            {
                var r = info.Monitor;
                monitors.Add(Rectangle.FromLTRB(r.Left, r.Top, r.Right, r.Bottom));
            }

            return true;
        }

        EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, Callback, IntPtr.Zero);
        return monitors;
    }

    /// 창을 물리 픽셀 좌표로 최상위(topmost) 배치한다(WinForms DPI 스케일링 우회).
    internal static void PositionTopMost(IntPtr hWnd, Rectangle bounds)
    {
        SetWindowPos(hWnd, HwndTopmost, bounds.Left, bounds.Top, bounds.Width, bounds.Height, SwpNoactivate | SwpShowwindow);
    }

    internal static IReadOnlyList<WindowInfo> EnumerateTopLevelWindows(HashSet<string> targetProcessNames)
    {
        var currentPid = Process.GetCurrentProcess().Id;
        var windows = new List<WindowInfo>();

        EnumWindows((hWnd, _) =>
        {
            if (!IsWindowVisible(hWnd))
            {
                return true;
            }

            if (IsCloaked(hWnd))
            {
                return true;
            }

            if (!GetWindowRect(hWnd, out var rect))
            {
                return true;
            }

            var bounds = Rectangle.FromLTRB(rect.Left, rect.Top, rect.Right, rect.Bottom);
            if (bounds.Width < ScreenOverlayLevelThreshold || bounds.Height < ScreenOverlayLevelThreshold)
            {
                return true;
            }

            GetWindowThreadProcessId(hWnd, out var processIdUInt);
            var processId = unchecked((int)processIdUInt);
            if (processId == currentPid)
            {
                return true;
            }

            string processName;
            try
            {
                processName = Process.GetProcessById(processId).ProcessName;
            }
            catch (ArgumentException)
            {
                return true;
            }
            catch (InvalidOperationException)
            {
                return true;
            }

            if (string.IsNullOrWhiteSpace(processName))
            {
                return true;
            }

            windows.Add(new WindowInfo(
                hWnd,
                bounds,
                processName,
                targetProcessNames.Contains(processName)));
            return true;
        }, IntPtr.Zero);

        return windows;
    }

    internal static IReadOnlyList<string> EnumerateSelectableProcesses()
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var window in EnumerateTopLevelWindows(new HashSet<string>(StringComparer.OrdinalIgnoreCase)))
        {
            names.Add(window.ProcessName);
        }

        return names.OrderBy(name => name, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static bool IsCloaked(IntPtr hWnd)
    {
        var result = DwmGetWindowAttribute(hWnd, DwMWaCloaked, out var cloaked, sizeof(int));
        return result == 0 && cloaked != 0;
    }
}
