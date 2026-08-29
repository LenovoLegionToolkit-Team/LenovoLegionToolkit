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

public class SmartFnLockController(FnLockFeature feature, ApplicationSettings settings) : IDisposable
{
    private const int HOLD_THRESHOLD_MS = 180;
    private const int HARDWARE_SETTLE_MS = 150;

    private readonly SemaphoreSlim _hardwareGate = new(1, 1);
    private readonly object _stateLock = new();
    private CancellationTokenSource? _holdCts;

    private bool _ctrlDepressed;
    private bool _shiftDepressed;
    private bool _altDepressed;
    private bool _wasModifierActive;
    private bool _hardwareToggledOff;
    private bool _isDisposed;

    public void OnKeyboardEvent(nuint wParam, KBDLLHOOKSTRUCT kbStruct)
    {
        if (settings.Store.SmartFnLockFlags == 0)
            return;

        bool isModifierActive = IsModifierKeyPressed(wParam, kbStruct);

        lock (_stateLock)
        {
            if (_isDisposed || isModifierActive == _wasModifierActive)
                return;

            _wasModifierActive = isModifierActive;

            _holdCts?.Cancel();
            _holdCts?.Dispose();
            _holdCts = new CancellationTokenSource();
            var ct = _holdCts.Token;

            if (isModifierActive)
            {
                Task.Run(async () =>
                {
                    try
                    {
                        await Task.Delay(HOLD_THRESHOLD_MS, ct).ConfigureAwait(false);
                        if (ct.IsCancellationRequested)
                            return;

                        await _hardwareGate.WaitAsync(ct).ConfigureAwait(false);
                        var didWrite = false;
                        try
                        {
                            if (ct.IsCancellationRequested)
                                return;

                            var state = await feature.GetStateAsync().ConfigureAwait(false);
                            if (state == FnLockState.Off || ct.IsCancellationRequested)
                                return;

                            lock (_stateLock)
                            {
                                if (_isDisposed || ct.IsCancellationRequested)
                                    return;

                                _hardwareToggledOff = true;
                            }

                            if (Log.Instance.IsTraceEnabled)
                                Log.Instance.Trace($"Modifier held past threshold, disabling Fn Lock temporarily...");

                            await feature.SetStateAsync(FnLockState.Off, verify: false).ConfigureAwait(false);
                            didWrite = true;
                        }
                        finally
                        {
                            if (didWrite)
                                await Task.Delay(HARDWARE_SETTLE_MS).ConfigureAwait(false);
                            _hardwareGate.Release();
                        }
                    }
                    catch (OperationCanceledException) { }
                    catch (Exception ex)
                    {
                        if (Log.Instance.IsTraceEnabled)
                            Log.Instance.Trace($"Failed to handle keyboard event, {ex.Message}");
                    }
                }, ct);
            }
            else
            {
                var needRestore = _hardwareToggledOff;
                _hardwareToggledOff = false;

                if (needRestore)
                {
                    Task.Run(async () =>
                    {
                        try
                        {
                            await _hardwareGate.WaitAsync().ConfigureAwait(false);
                            try
                            {
                                if (Log.Instance.IsTraceEnabled)
                                    Log.Instance.Trace($"Modifier released, re-enabling Fn Lock...");

                                await feature.SetStateAsync(FnLockState.On, verify: false).ConfigureAwait(false);
                            }
                            finally
                            {
                                await Task.Delay(HARDWARE_SETTLE_MS).ConfigureAwait(false);
                                _hardwareGate.Release();
                            }
                        }
                        catch (Exception ex)
                        {
                            if (Log.Instance.IsTraceEnabled)
                                Log.Instance.Trace($"Failed to handle keyboard event, {ex.Message}");
                        }
                    });
                }
            }
        }
    }

    public void Dispose()
    {
        bool needRestore;
        lock (_stateLock)
        {
            if (_isDisposed)
                return;

            _isDisposed = true;
            needRestore = _hardwareToggledOff;
            _hardwareToggledOff = false;
            _holdCts?.Cancel();
            _holdCts?.Dispose();
        }

        if (needRestore)
        {
            try { feature.SetStateAsync(FnLockState.On, verify: false).GetAwaiter().GetResult(); }
            catch { }
        }

        _hardwareGate.Dispose();
        GC.SuppressFinalize(this);
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