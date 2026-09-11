using System;
using System.Diagnostics;
using System.Windows.Forms;
using Windows.Win32;
using Windows.Win32.Foundation;
using LenovoLegionToolkit.Lib.Utils;

namespace LenovoLegionToolkit.WPF.Utils;

public static class FullscreenHelper
{
    public static bool IsAnyApplicationFullscreen() => GetForegroundFullscreenProcessName() is not null;

    public static unsafe string? GetForegroundFullscreenProcessName()
    {
        try
        {
            var desktopWindowHandle = PInvoke.GetDesktopWindow();
            var shellWindowHandle = PInvoke.GetShellWindow();

            var foregroundWindowHandle = PInvoke.GetForegroundWindow();
            if (foregroundWindowHandle == HWND.Null)
                return null;
            if (foregroundWindowHandle == desktopWindowHandle)
                return null;
            if (foregroundWindowHandle == shellWindowHandle)
                return null;

            if (!PInvoke.GetWindowRect(foregroundWindowHandle, out var appBounds))
                return null;

            var screenBounds = Screen.FromHandle(foregroundWindowHandle).Bounds;
            var coversFullScreen = appBounds.bottom - appBounds.top == screenBounds.Height && appBounds.right - appBounds.left == screenBounds.Width;
            if (!coversFullScreen)
                return null;

            var processId = 0u;
            _ = PInvoke.GetWindowThreadProcessId(foregroundWindowHandle, &processId);
            using var process = Process.GetProcessById((int)processId);
            return process.ProcessName.Equals("explorer", StringComparison.OrdinalIgnoreCase)
                ? null
                : process.ProcessName;
        }
        catch (Exception ex)
        {
            Log.Instance.Trace($"Couldn't check if application is full screen.", ex);

            return null;
        }
    }
}
