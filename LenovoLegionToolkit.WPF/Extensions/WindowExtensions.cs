using LenovoLegionToolkit.Lib;
using LenovoLegionToolkit.Lib.Settings;
using LenovoLegionToolkit.Lib.Utils;
using LenovoLegionToolkit.WPF.Windows.Osd;
using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.Shell;
using Windows.Win32.UI.WindowsAndMessaging;

namespace LenovoLegionToolkit.WPF.Extensions;

public static class WindowExtensions
{
    #region TaskbarList COM

    [ComImport]
    [Guid("56FDF344-FD6D-11d0-958A-006097C9A090")]
    private class TaskbarList { }

    private static readonly Lazy<ITaskbarList> _taskbarList = new(() =>
    {
        var list = (ITaskbarList)new TaskbarList();
        list.HrInit();
        return list;
    });

    private static void HideFromTaskbar(IntPtr hwnd)
    {
        try
        {
            _taskbarList.Value.DeleteTab(new HWND(hwnd));
        }
        catch { /* Ignore */ }
    }

    #endregion

    #region Constants

    private static readonly HWND HWND_TOPMOST = new HWND(-1);

    private const int WM_STYLECHANGING = 0x007D;
    private const int WM_WINDOWPOSCHANGING = 0x0046;
    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_TOOLWINDOW = 0x00000080;
    private const int WS_EX_NOACTIVATE = 0x08000000;
    private const int SWP_NOACTIVATE = 0x0010;

    #endregion

    #region Native Interop

    [DllImport("user32.dll", EntryPoint = "GetWindowBand")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowBand(IntPtr hWnd, out uint pdwBand);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
    public static extern uint RegisterWindowMessage(string lpString);

    [StructLayout(LayoutKind.Sequential)]
    internal struct STYLESTRUCT
    {
        public int styleOld;
        public int styleNew;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct WINDOWPOS
    {
        public IntPtr hwnd;
        public IntPtr hwndInsertAfter;
        public int x;
        public int y;
        public int cx;
        public int cy;
        public int flags;
    }

    #endregion

    #region Public Methods

    public static void EscalateZBand(this Window window)
    {
        if (PresentationSource.FromVisual(window) is not HwndSource source)
            return;

        var hwnd = (HWND)source.Handle;
        try
        {
            PInvoke.SetWindowPos(hwnd, HWND_TOPMOST, 0, 0, 0, 0,
                SET_WINDOW_POS_FLAGS.SWP_NOMOVE |
                SET_WINDOW_POS_FLAGS.SWP_NOSIZE |
                SET_WINDOW_POS_FLAGS.SWP_NOACTIVATE);

            if (GetWindowBand(source.Handle, out uint currentBand))
                Log.Instance.Trace($"EscalateZBand executed for {window.GetType().Name}. Current Band: {currentBand}");
        }
        catch (Exception ex)
        {
            Log.Instance.Trace($"Exception for HWND {hwnd}", ex);
        }
    }

    public static void SetClickThrough(this Window window, bool clickThrough)
    {
        if (PresentationSource.FromVisual(window) is not HwndSource source)
            return;

        var hwnd = (HWND)source.Handle;
        var extendedStyle = (WINDOW_EX_STYLE)PInvoke.GetWindowLong(hwnd, WINDOW_LONG_PTR_INDEX.GWL_EXSTYLE);

        extendedStyle |= WINDOW_EX_STYLE.WS_EX_TOOLWINDOW | WINDOW_EX_STYLE.WS_EX_NOACTIVATE;

        if (clickThrough)
            extendedStyle |= WINDOW_EX_STYLE.WS_EX_TRANSPARENT;
        else
            extendedStyle &= ~WINDOW_EX_STYLE.WS_EX_TRANSPARENT;

        PInvoke.SetWindowLong(hwnd, WINDOW_LONG_PTR_INDEX.GWL_EXSTYLE, (int)extendedStyle);
    }

    public static void BringToForeground(this Window window)
    {
        window.ShowInTaskbar = true;

        if (window.Visibility == Visibility.Hidden)
            window.Show();

        if (window.WindowState == WindowState.Minimized)
            SystemCommands.RestoreWindow(window);

        var wasTopmost = window.Topmost;
        window.Activate();
        window.Topmost = true;
        window.Topmost = wasTopmost;
        window.Focus();
    }

    public static IntPtr WndProcHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_STYLECHANGING)
        {
            if (wParam.ToInt32() == GWL_EXSTYLE)
            {
                var styleStruct = Marshal.PtrToStructure<STYLESTRUCT>(lParam);
                styleStruct.styleNew |= unchecked((int)(WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE));
                Marshal.StructureToPtr(styleStruct, lParam, false);
                HideFromTaskbar(hwnd);
                handled = true;
            }
        }
        else if (msg == WM_WINDOWPOSCHANGING)
        {
            var wp = Marshal.PtrToStructure<WINDOWPOS>(lParam);
            wp.flags |= SWP_NOACTIVATE;
            Marshal.StructureToPtr(wp, lParam, false);
            HideFromTaskbar(hwnd);
            handled = true;
        }
        else if (msg == OsdWindowBase.TaskbarCreatedMsg)
        {
            if (HwndSource.FromHwnd(hwnd)?.RootVisual is Window window)
            {
                var osdSettings = IoCContainer.Resolve<OsdSettings>();
                window.SetClickThrough(osdSettings.Store.IsLocked);
                HideFromTaskbar(hwnd);
                handled = true;
            }
        }

        return IntPtr.Zero;
    }

    #endregion
}