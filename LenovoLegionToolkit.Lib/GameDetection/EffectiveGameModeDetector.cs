using System;
using System.Threading.Tasks;
using Windows.Win32;
using Windows.Win32.System.Power;
using LenovoLegionToolkit.Lib.Utils;

namespace LenovoLegionToolkit.Lib.GameDetection;

internal unsafe class EffectiveGameModeDetector
{
    private readonly EFFECTIVE_POWER_MODE_CALLBACK _callbackPointer;

    private IntPtr _handle;
    private bool? _lastState;

    public bool IsActive => _lastState == true;

    public event EventHandler<bool>? Changed;

    public EffectiveGameModeDetector()
    {
        _callbackPointer = Callback;
    }

    public Task StartAsync()
    {
        var result = PInvoke.PowerRegisterForEffectivePowerModeNotifications(PInvoke.EFFECTIVE_POWER_MODE_V2, _callbackPointer, null, out var handle);
        if (result == 0)
            _handle = new IntPtr(handle);
        return Task.CompletedTask;
    }

    public Task StopAsync()
    {
        PInvoke.PowerUnregisterFromEffectivePowerModeNotifications(_handle.ToPointer());
        _handle = IntPtr.Zero;
        return Task.CompletedTask;
    }

    private void Callback(EFFECTIVE_POWER_MODE mode, void* context)
    {
        Log.Instance.Trace($"Effective power mode is {mode}.");

        var state = mode == EFFECTIVE_POWER_MODE.EffectivePowerModeGameMode;

        _lastState ??= state;

        if (_lastState == state)
            return;

        _lastState = state;

        Changed?.Invoke(this, state);
    }
}
