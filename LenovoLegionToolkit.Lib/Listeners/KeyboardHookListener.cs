using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows.Forms;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.Input.KeyboardAndMouse;
using Windows.Win32.UI.WindowsAndMessaging;
using LenovoLegionToolkit.Lib.Utils;

namespace LenovoLegionToolkit.Lib.Listeners;

public class KeyboardHookListener : NativeWindow, IListener<KeyboardHookListener.ChangedEventArgs>
{
    public class ChangedEventArgs(KeyboardKey key) : EventArgs
    {
        public KeyboardKey Key { get; } = key;
    }

    private readonly IMainThreadDispatcher _mainThreadDispatcher;
    private readonly HOOKPROC _hookProc;

    private HHOOK _hook;
    private uint _downKeyboardKeyVk;

    public Func<KeyboardKey, Task<bool>>? CustomKeyHandler { get; set; }

    public Func<ConsumerControlKey, bool>? SuppressConsumerControlKey { get; set; }

    public Func<KeyboardKey, bool>? SuppressKeyboardKey { get; set; }

    public event EventHandler<ChangedEventArgs>? Changed;

    public KeyboardHookListener(IMainThreadDispatcher mainThreadDispatcher)
    {
        _mainThreadDispatcher = mainThreadDispatcher;
        _hookProc = HookProc;
    }

    public Task StartAsync() => _mainThreadDispatcher.DispatchAsync(() =>
    {
        CreateHandle(new CreateParams
        {
            Caption = "LenovoLegionToolkit_KeyboardHookListenerWindow",
            Parent = new IntPtr(-3)
        });

        _hook = PInvoke.SetWindowsHookEx(WINDOWS_HOOK_ID.WH_KEYBOARD_LL, _hookProc, HINSTANCE.Null, 0);

        if (_hook.IsNull)
            Log.Instance.Trace($"Failed to install keyboard hook. [error={Marshal.GetLastWin32Error()}]");

        return Task.CompletedTask;
    });

    public Task StopAsync() => _mainThreadDispatcher.DispatchAsync(() =>
    {
        PInvoke.UnhookWindowsHookEx(_hook);

        _hook = HHOOK.Null;

        ReleaseHandle();

        return Task.CompletedTask;
    });

    private unsafe LRESULT HookProc(int nCode, WPARAM wParam, LPARAM lParam)
    {
        if (nCode != PInvoke.HC_ACTION)
            return PInvoke.CallNextHookEx(HHOOK.Null, nCode, wParam, lParam);

        ref var kbStruct = ref Unsafe.AsRef<KBDLLHOOKSTRUCT>((void*)lParam.Value);

        var consumerControlKey = GetConsumerControlKey(kbStruct.vkCode);

        if (consumerControlKey is { } cck && SuppressConsumerControlKey is not null && SuppressConsumerControlKey(cck))
            return new LRESULT(1);

        var keyboardKey = GetKeyboardKey(kbStruct.vkCode);

        if (keyboardKey is { } kk)
        {
            var suppress = SuppressKeyboardKey is not null && SuppressKeyboardKey(kk);

            if (wParam.Value == PInvoke.WM_KEYUP)
            {
                if (_downKeyboardKeyVk == kbStruct.vkCode)
                    _downKeyboardKeyVk = 0;
            }
            else if (wParam.Value == PInvoke.WM_KEYDOWN && _downKeyboardKeyVk != kbStruct.vkCode)
            {
                _downKeyboardKeyVk = kbStruct.vkCode;

                Log.Instance.Trace($"Keyboard key detected. [key={kk}, suppress={suppress}]");

                Changed?.Invoke(this, new(kk));

                _ = HandleAsync(kk);
            }

            if (suppress)
                return new LRESULT(1);
        }

        return PInvoke.CallNextHookEx(HHOOK.Null, nCode, wParam, lParam);
    }

    private static ConsumerControlKey? GetConsumerControlKey(uint vkCode) => vkCode switch
    {
        (uint)VIRTUAL_KEY.VK_VOLUME_MUTE => ConsumerControlKey.FnF1,
        (uint)VIRTUAL_KEY.VK_VOLUME_DOWN => ConsumerControlKey.FnF2,
        (uint)VIRTUAL_KEY.VK_VOLUME_UP => ConsumerControlKey.FnF3,
        (uint)VIRTUAL_KEY.VK_LAUNCH_APP2 => ConsumerControlKey.FnF12,
        _ => null,
    };

    private static KeyboardKey? GetKeyboardKey(uint vkCode) => vkCode switch
    {
        (uint)VIRTUAL_KEY.VK_INSERT => KeyboardKey.FnI,
        (uint)VIRTUAL_KEY.VK_SCROLL => KeyboardKey.FnK,
        _ => null,
    };

    private async Task HandleAsync(KeyboardKey key)
    {
        try
        {
            if (CustomKeyHandler is not null && await CustomKeyHandler(key).ConfigureAwait(false))
                return;
        }
        catch (Exception ex)
        {
            Log.Instance.Trace($"Couldn't handle keyboard key. [key={key}]", ex);
        }
    }
}
