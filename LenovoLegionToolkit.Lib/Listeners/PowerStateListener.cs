using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.System.Power;
using Windows.Win32.UI.WindowsAndMessaging;
using LenovoLegionToolkit.Lib.Controllers;
using LenovoLegionToolkit.Lib.Features;
using LenovoLegionToolkit.Lib.Features.Hybrid.Notify;
using LenovoLegionToolkit.Lib.Messaging;
using LenovoLegionToolkit.Lib.Messaging.Messages;
using LenovoLegionToolkit.Lib.Overclocking.Amd;
using LenovoLegionToolkit.Lib.System;
using LenovoLegionToolkit.Lib.Utils;
using Microsoft.Win32;

namespace LenovoLegionToolkit.Lib.Listeners;

public sealed class PowerStateListener : IListener<PowerStateListener.ChangedEventArgs>, IDisposable
{
    public class ChangedEventArgs(PowerStateEvent powerStateEvent, bool powerAdapterStateChanged) : EventArgs
    {
        public PowerStateEvent PowerStateEvent { get; } = powerStateEvent;
        public bool PowerAdapterStateChanged { get; } = powerAdapterStateChanged;
    }

    private readonly SafeHandle _recipientHandle;
    private readonly PDEVICE_NOTIFY_CALLBACK_ROUTINE _callback;

    private readonly PowerModeFeature _powerModeFeature;
    private readonly BatteryFeature _batteryFeature;
    private readonly DGPUNotify _dgpuNotify;
    private readonly RGBKeyboardBacklightController _rgbController;

    private readonly SemaphoreSlim _processingLock = new(1, 1);

    private bool _started;
    private bool _disposed;
    private HPOWERNOTIFY _handle;
    private PowerAdapterStatus? _lastPowerAdapterState;

    public event EventHandler<ChangedEventArgs>? Changed;

    public unsafe PowerStateListener(
        PowerModeFeature powerModeFeature,
        BatteryFeature batteryFeature,
        DGPUNotify dgpuNotify,
        RGBKeyboardBacklightController rgbController)
    {
        _powerModeFeature = powerModeFeature;
        _batteryFeature = batteryFeature;
        _dgpuNotify = dgpuNotify;
        _rgbController = rgbController;

        _callback = Callback;

        _recipientHandle = new StructSafeHandle<DEVICE_NOTIFY_SUBSCRIBE_PARAMETERS>(new DEVICE_NOTIFY_SUBSCRIBE_PARAMETERS
        {
            Callback = _callback,
            Context = null,
        });
    }

    public async Task StartAsync()
    {
        if (_started || _disposed) return;

        _lastPowerAdapterState = await Power.IsPowerAdapterConnectedAsync().ConfigureAwait(false);

        SystemEvents.PowerModeChanged += SystemEvents_PowerModeChanged;
        RegisterSuspendResumeNotification();

        _started = true;
    }

    public Task StopAsync()
    {
        if (!_started) return Task.CompletedTask;

        SystemEvents.PowerModeChanged -= SystemEvents_PowerModeChanged;
        UnRegisterSuspendResumeNotification();

        _started = false;
        return Task.CompletedTask;
    }

    private async void SystemEvents_PowerModeChanged(object sender, PowerModeChangedEventArgs e)
    {
        try
        {
            Log.Instance.Trace($"Event received: {e.Mode}");

            var powerStateEvent = e.Mode switch
            {
                PowerModes.StatusChange => PowerStateEvent.StatusChange,
                PowerModes.Resume => PowerStateEvent.Resume,
                PowerModes.Suspend => PowerStateEvent.Suspend,
                _ => PowerStateEvent.Unknown
            };

            if (powerStateEvent is PowerStateEvent.Unknown) return;

            await ProcessPowerEventAsync(powerStateEvent).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Log.Instance.Trace($"Error in SystemEvents_PowerModeChanged: {ex}");
        }
    }

    private unsafe uint Callback(void* context, uint type, void* setting)
    {
        TriggerNativeCallback(type);
        return (uint)WIN32_ERROR.ERROR_SUCCESS;
    }

    private void TriggerNativeCallback(uint type)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                await CallbackAsync(type).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Log.Instance.Trace($"Error in Native Power Callback: {ex}");
            }
        });
    }

    private async Task CallbackAsync(uint type)
    {
        var powerStateEvent = type switch
        {
            PInvoke.PBT_APMSUSPEND => PowerStateEvent.Suspend,
            PInvoke.PBT_APMRESUMEAUTOMATIC => PowerStateEvent.Resume,
            PInvoke.PBT_APMPOWERSTATUSCHANGE => PowerStateEvent.StatusChange,
            _ => PowerStateEvent.Unknown
        };

        if (powerStateEvent == PowerStateEvent.Unknown) return;

        var mi = await Compatibility.GetMachineInformationAsync().ConfigureAwait(false);
        if (!mi.Properties.SupportsAlwaysOnAc.status)
        {
            return;
        }

        Log.Instance.Trace($"Event value: {type}");

        if (powerStateEvent is not PowerStateEvent.Resume)
            return;

        await ProcessPowerEventAsync(powerStateEvent).ConfigureAwait(false);
    }

    private async Task ProcessPowerEventAsync(PowerStateEvent powerStateEvent)
    {
        if (!await _processingLock.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false))
        {
            return;
        }

        try
        {
            var powerAdapterState = await Power.IsPowerAdapterConnectedAsync().ConfigureAwait(false);
            Log.Instance.Trace($"Handle {powerStateEvent}. [newState={powerAdapterState}]");

            switch (powerStateEvent)
            {
                case PowerStateEvent.Suspend:
                    await HandleSuspendAsync().ConfigureAwait(false);
                    break;

                case PowerStateEvent.Resume:
                    await HandleResumeInternalAsync(powerAdapterState).ConfigureAwait(false);
                    break;

                case PowerStateEvent.StatusChange when powerAdapterState == PowerAdapterStatus.Connected:
                    await HandleConnectedStatusChangeAsync().ConfigureAwait(false);
                    break;
            }

            HandlePowerStateChangeNotification(powerStateEvent, powerAdapterState);
        }
        finally
        {
            _processingLock.Release();
        }
    }

    private async Task HandleSuspendAsync()
    {
        if (await _powerModeFeature.IsSupportedAsync().ConfigureAwait(false) &&
            await _powerModeFeature.GetStateAsync().ConfigureAwait(false) == PowerModeState.GodMode)
        {
            Log.Instance.Trace($"Going to dark.");
            await _powerModeFeature.SuspendMode(PowerModeState.Balance).ConfigureAwait(false);
        }

        var feature = IoCContainer.Resolve<AmdOverclockingController>();
        if (feature.IsActive())
        {
            await feature.ResetAllActiveCoresCoAsync().ConfigureAwait(false);
        }

        MessagingCenter.Publish(new FanStateMessage(FanState.Auto));
    }

    private async Task HandleResumeInternalAsync(PowerAdapterStatus currentAdapterStatus)
    {
        if (await _batteryFeature.IsSupportedAsync().ConfigureAwait(false))
        {
            await _batteryFeature.EnsureCorrectBatteryModeIsSetAsync().ConfigureAwait(false);
        }

        if (await _rgbController.IsSupportedAsync().ConfigureAwait(false))
        {
            await _rgbController.SetLightControlOwnerAsync(true, true).ConfigureAwait(false);
        }

        if (currentAdapterStatus == PowerAdapterStatus.Connected)
        {
            var overclockingController = IoCContainer.Resolve<AmdOverclockingController>();
            if (overclockingController.IsActive())
            {
                Log.Instance.Trace($"Applying overclocking profile...");
                await overclockingController.ApplyInternalProfileAsync().ConfigureAwait(false);
            }
        }

        if (await _powerModeFeature.IsSupportedAsync().ConfigureAwait(false))
        {
            if (_powerModeFeature.LastPowerModeState == PowerModeState.GodMode)
            {
                Log.Instance.Trace($"Restore to {_powerModeFeature.LastPowerModeState}");
                await _powerModeFeature.SetStateAsync(_powerModeFeature.LastPowerModeState).ConfigureAwait(false);
            }

            await _powerModeFeature.EnsureCorrectWindowsPowerSettingsAreSetAsync().ConfigureAwait(false);
            await _powerModeFeature.EnsureGodModeStateIsAppliedAsync().ConfigureAwait(false);
        }

        MessagingCenter.Publish(new FanStateMessage(FanState.Manual));

        _ = NotifyDgpuAsync();
    }

    private async Task HandleConnectedStatusChangeAsync()
    {
        if (await _powerModeFeature.IsSupportedAsync().ConfigureAwait(false))
        {
            await _powerModeFeature.EnsureGodModeStateIsAppliedAsync().ConfigureAwait(false);
        }

        _ = NotifyDgpuAsync();
    }

    private async Task NotifyDgpuAsync()
    {
        try
        {
            if (await _dgpuNotify.IsSupportedAsync().ConfigureAwait(false))
            {
                await Task.Delay(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
                await _dgpuNotify.NotifyAsync().ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            Log.Instance.Trace($"Error in NotifyDgpuAsync: {ex}");
        }
    }

    private void HandlePowerStateChangeNotification(PowerStateEvent powerStateEvent, PowerAdapterStatus newAdapterState)
    {
        var powerAdapterStateChanged = newAdapterState != _lastPowerAdapterState;
        _lastPowerAdapterState = newAdapterState;

        if (powerAdapterStateChanged)
        {
            Notify(newAdapterState);
        }

        Changed?.Invoke(this, new(powerStateEvent, powerAdapterStateChanged));
    }

    private unsafe void RegisterSuspendResumeNotification()
    {
        var result = PInvoke.PowerRegisterSuspendResumeNotification(REGISTER_NOTIFICATION_FLAGS.DEVICE_NOTIFY_CALLBACK, _recipientHandle, out var handle);
        _handle = result == WIN32_ERROR.ERROR_SUCCESS ? new HPOWERNOTIFY(new IntPtr(handle)) : HPOWERNOTIFY.Null;
    }

    private void UnRegisterSuspendResumeNotification()
    {
        if (_handle != HPOWERNOTIFY.Null)
        {
            PInvoke.PowerUnregisterSuspendResumeNotification(_handle);
            _handle = HPOWERNOTIFY.Null;
        }
    }

    private static void Notify(PowerAdapterStatus newState)
    {
        var msgType = newState switch
        {
            PowerAdapterStatus.Connected => NotificationType.ACAdapterConnected,
            PowerAdapterStatus.ConnectedLowWattage => NotificationType.ACAdapterConnectedLowWattage,
            PowerAdapterStatus.Disconnected => NotificationType.ACAdapterDisconnected,
            _ => (NotificationType?)null
        };

        if (msgType.HasValue)
        {
            MessagingCenter.Publish(new NotificationMessage(msgType.Value));
        }
    }

    public void Dispose()
    {
        if (_disposed) return;

        StopAsync().GetAwaiter().GetResult();
        _processingLock.Dispose();
        _recipientHandle?.Dispose();

        _disposed = true;
    }
}