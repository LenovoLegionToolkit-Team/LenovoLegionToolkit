using LenovoLegionToolkit.Lib.System.Management;
using LenovoLegionToolkit.Lib.Utils;
using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Management;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using Windows.Win32;
using Windows.Win32.UI.Input.KeyboardAndMouse;

namespace LenovoLegionToolkit.Lib.Features;

public class PrecisionTouchpadLockFeature : IFeature<TouchpadLockState>
{
    private const short PrecisionTouchPad = 0x12;
    private const uint Supported = 0x18;

    public async Task<bool> IsSupportedAsync()
    {
        if (AppFlags.Instance.Debug)
        {
            return true;
        }

        return await WMI.LenovoUtilityData.GetFeatureSupportStateAsync(PrecisionTouchPad, Supported).ConfigureAwait(false);
    }

    public async Task<TouchpadLockState> GetStateAsync()
    {
        using var key = Registry.CurrentUser.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\PrecisionTouchPad\Status", false);
        
        object? value = key?.GetValue("Enabled");
        if (value is int enabled && enabled != 0)
        {
            return TouchpadLockState.Off;
        }

        return TouchpadLockState.On;
    }

    public async Task<TouchpadLockState[]> GetAllStatesAsync()
    {
        var states = Enum.GetValues<TouchpadLockState>().Cast<TouchpadLockState>().ToArray();
        return states;
    }

    public async Task SetStateAsync(TouchpadLockState state)
    {
        var currentState = await GetStateAsync();
        if (currentState == state)
        {
            return;
        }

        SendCtrlWinF24();
    }

    public void SendCtrlWinF24()
    {
        const ushort VK_CONTROL = 0x11;
        const ushort VK_LWIN = 0x5B;
        const ushort VK_F24 = 0x87;

        var inputs = new INPUT[]
        {
            KeyboardInput(VK_CONTROL, false),
            KeyboardInput(VK_LWIN,    false),
            KeyboardInput(VK_F24,     false),
            KeyboardInput(VK_F24,     true),
            KeyboardInput(VK_LWIN,    true),
            KeyboardInput(VK_CONTROL, true)
        };

        int size = Marshal.SizeOf<INPUT>(default);
        PInvoke.SendInput(new ReadOnlySpan<INPUT>(inputs), size);
    }

    private static INPUT KeyboardInput(ushort key, bool keyUp)
    {
        return new INPUT
        {
            type = INPUT_TYPE.INPUT_KEYBOARD,
            Anonymous = new INPUT._Anonymous_e__Union
            {
                ki = new KEYBDINPUT
                {
                    wVk = (VIRTUAL_KEY)key,
                    dwFlags = keyUp ? KEYBD_EVENT_FLAGS.KEYEVENTF_KEYUP : 0,
                    dwExtraInfo = 0
                }
            }
        };
    }
}