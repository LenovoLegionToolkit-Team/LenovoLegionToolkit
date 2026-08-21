using System;
using System.Threading;
using System.Threading.Tasks;
using Windows.Win32;
using Windows.Win32.UI.Input.KeyboardAndMouse;
using Windows.Win32.UI.WindowsAndMessaging;
using LenovoLegionToolkit.Lib.Features;
using LenovoLegionToolkit.Lib.Settings;
using LenovoLegionToolkit.Lib.Utils;

namespace LenovoLegionToolkit.Lib.Controllers;

public class SmartFnLockController(FnLockFeature feature, ApplicationSettings settings)
{
    private readonly object _stateLock = new();
    private CancellationTokenSource? _debounceCts;

    private bool _ctrlDepressed;
    private bool _shiftDepressed;
    private bool _altDepressed;
    private bool _restoreFnLock;
    private bool _wasModifierActive;

    public void OnKeyboardEvent(nuint wParam, KBDLLHOOKSTRUCT kbStruct)
    {
        if (settings.Store.SmartFnLockFlags == 0)
            return;

        bool isModifierActive = IsModifierKeyPressed(wParam, kbStruct);

        lock (_stateLock)
        {
            if (isModifierActive == _wasModifierActive)
                return;

            _wasModifierActive = isModifierActive;

            _debounceCts?.Cancel();
            _debounceCts?.Dispose();
            _debounceCts = new CancellationTokenSource();
            var ct = _debounceCts.Token;

            if (isModifierActive)
            {
                if (_restoreFnLock)
                    return;

                Task.Run(async () =>
                {
                    try
                    {
                        await Task.Delay(40, ct).ConfigureAwait(false);
                        if (ct.IsCancellationRequested)
                            return;

                        var state = await feature.GetStateAsync().ConfigureAwait(false);
                        if (state == FnLockState.Off || ct.IsCancellationRequested)
                            return;

                        lock (_stateLock)
                        {
                            if (ct.IsCancellationRequested)
                                return;

                            _restoreFnLock = true;
                        }

                        if (Log.Instance.IsTraceEnabled)
                            Log.Instance.Trace($"Disabling Fn Lock temporarily...");

                        await feature.SetStateAsync(FnLockState.Off, verify: false).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException) { }
                    catch (Exception ex)
                    {
                        if (Log.Instance.IsTraceEnabled)
                            Log.Instance.Trace($"Failed to handle keyboard event: {ex.Message}");
                    }
                }, ct);
            }
            else if (_restoreFnLock)
            {
                _restoreFnLock = false;

                Task.Run(async () =>
                {
                    try
                    {
                        if (Log.Instance.IsTraceEnabled)
                            Log.Instance.Trace($"Re-enabling Fn Lock...");

                        await feature.SetStateAsync(FnLockState.On, verify: false).ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        if (Log.Instance.IsTraceEnabled)
                            Log.Instance.Trace($"Failed to handle keyboard event: {ex.Message}");
                    }
                });
            }
        }
    }

    private bool IsModifierKeyPressed(nuint wParam, KBDLLHOOKSTRUCT kbStruct)
    {
        var isKeyDown = wParam is PInvoke.WM_KEYDOWN or PInvoke.WM_SYSKEYDOWN;
        var vkKeyCode = (VIRTUAL_KEY)kbStruct.vkCode;

        if (vkKeyCode is VIRTUAL_KEY.VK_LCONTROL or VIRTUAL_KEY.VK_RCONTROL or VIRTUAL_KEY.VK_CONTROL)
            _ctrlDepressed = isKeyDown;

        if (vkKeyCode is VIRTUAL_KEY.VK_LSHIFT or VIRTUAL_KEY.VK_RSHIFT or VIRTUAL_KEY.VK_SHIFT)
            _shiftDepressed = isKeyDown;

        if (vkKeyCode is VIRTUAL_KEY.VK_LMENU or VIRTUAL_KEY.VK_RMENU or VIRTUAL_KEY.VK_MENU)
            _altDepressed = isKeyDown;

        if (!_ctrlDepressed && !_shiftDepressed && !_altDepressed)
            return false;

        var result = false;
        var flags = settings.Store.SmartFnLockFlags;

        if (flags.HasFlag(ModifierKey.Ctrl))
            result |= _ctrlDepressed;

        if (flags.HasFlag(ModifierKey.Shift))
            result |= _shiftDepressed;

        if (flags.HasFlag(ModifierKey.Alt))
            result |= _altDepressed;

        return result;
    }
}