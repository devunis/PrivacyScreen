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

    public delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

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

    internal readonly record struct WindowInfo(IntPtr Handle, Rectangle Bounds, string ProcessName, bool IsTarget);

    internal static int AddClickThroughStyles(int existingStyles)
    {
        return existingStyles | WsExToolwindow | WsExNoactivate | WsExTransparent | WsExLayered;
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

            _ = GetWindowThreadProcessId(hWnd, out var processIdUInt);
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
