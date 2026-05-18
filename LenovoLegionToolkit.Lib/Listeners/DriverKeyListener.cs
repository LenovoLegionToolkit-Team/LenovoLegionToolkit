using LenovoLegionToolkit.Lib.Extensions;
using LenovoLegionToolkit.Lib.Features;
using LenovoLegionToolkit.Lib.Features.WhiteKeyboardBacklight;
using LenovoLegionToolkit.Lib.Messaging;
using LenovoLegionToolkit.Lib.Messaging.Messages;
using LenovoLegionToolkit.Lib.SoftwareDisabler;
using LenovoLegionToolkit.Lib.System;
using LenovoLegionToolkit.Lib.Utils;
using System;
using System.Threading;
using System.Threading.Tasks;
using Windows.Win32;
using Windows.Win32.Foundation;
using static System.Windows.Forms.AxHost;

namespace LenovoLegionToolkit.Lib.Listeners;

public class DriverKeyListener(
    FnKeysDisabler fnKeysDisabler,
    MicrophoneFeature microphoneFeature,
    TouchpadLockFeature touchpadLockFeature,
    WhiteKeyboardBacklightFeature whiteKeyboardBacklightFeature)
    : IListener<DriverKeyListener.ChangedEventArgs>
{
    public class ChangedEventArgs(DriverKey driverKey, uint rawValue) : EventArgs
    {
        public DriverKey DriverKey { get; } = driverKey;
        public uint RawValue { get; } = rawValue;
    }

    public bool DiscoveryMode { get; set; }

    public Func<DriverKey, Task<bool>>? CustomKeyHandler { get; set; }

    public event EventHandler<ChangedEventArgs>? Changed;

    private CancellationTokenSource? _cancellationTokenSource;
    private Task? _listenTask;

    public Task StartAsync()
    {
        if (_listenTask is not null)
            return Task.CompletedTask;

        _cancellationTokenSource = new();
        _listenTask = Task.Run(() => HandlerAsync(_cancellationTokenSource.Token));

        return Task.CompletedTask;
    }

    public async Task StopAsync()
    {
        if (_cancellationTokenSource is not null)
            await _cancellationTokenSource.CancelAsync().ConfigureAwait(false);

        _cancellationTokenSource = null;

        if (_listenTask is not null)
            await _listenTask;

        _listenTask = null;
    }

    private async Task HandlerAsync(CancellationToken token)
    {
        try
        {
            var resetEvent = new ManualResetEvent(false);
            var setHandleResult = BindListener(resetEvent);
            if (!setHandleResult)
                PInvokeExtensions.ThrowIfWin32Error("DeviceIoControl, setHandleResult");

            GetValue(out _); // Clear register

            while (true)
            {
                WaitHandle.WaitAny([resetEvent, token.WaitHandle]);

                token.ThrowIfCancellationRequested();

                if (await fnKeysDisabler.GetStatusAsync().ConfigureAwait(false) == SoftwareStatus.Enabled)
                {
                    Log.Instance.Trace($"Ignoring, FnKeys are enabled.");

                    resetEvent.Reset();
                    continue;
                }

                var getValueResult = GetValue(out var value);
                if (!getValueResult)
                    PInvokeExtensions.ThrowIfWin32Error("DeviceIoControl, getValueResult");

                var key = (DriverKey)value;
                Log.Instance.Trace($"Event received. [key={key}, value={value}]");

                if (!DiscoveryMode)
                    await OnChangedAsync(key).ConfigureAwait(false);
                Changed?.Invoke(this, new(key, value));

                resetEvent.Reset();
            }
        }
        catch (OperationCanceledException) { }
        catch (ThreadAbortException) { }
        catch (Exception ex)
        {
            Log.Instance.Trace($"Unknown error.", ex);
        }
    }

    private async Task OnChangedAsync(DriverKey value)
    {
        try
        {
            foreach (DriverKey flag in Enum.GetValues<DriverKey>())
            {
                if (!value.HasFlag(flag))
                    continue;
                if (CustomKeyHandler is not null && await CustomKeyHandler(flag).ConfigureAwait(false))
                    continue;
                await RunBuiltInAsync(flag).ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            Log.Instance.Trace($"Couldn't handle key press. [value={value}]", ex);
        }
    }

    private async Task RunBuiltInAsync(DriverKey key)
    {
        switch (key)
        {
            case DriverKey.FnQ:
                MessagingCenter.Publish(new DriverKeyPressedMessage(key));
                break;
            case DriverKey.FnF4:
                await ToggleMicrophoneAsync().ConfigureAwait(false);
                break;
            case DriverKey.FnF8:
                ToggleAirplaneMode();
                break;
            case DriverKey.FnF10:
                await NotifyTouchpadLockAsync().ConfigureAwait(false);
                break;
            case DriverKey.FnSpace:
                await NotifyWhiteBacklightAsync().ConfigureAwait(false);
                break;
        }
    }

    private static void ToggleAirplaneMode()
    {
        var isAirplaneModeOn = AirplaneMode.Toggle();
        MessagingCenter.Publish(new NotificationMessage(
            isAirplaneModeOn ? NotificationType.AirplaneModeOn : NotificationType.AirplaneModeOff));
    }

    private async Task ToggleMicrophoneAsync()
    {
        if (!await microphoneFeature.IsSupportedAsync().ConfigureAwait(false))
            return;

        var currentState = await microphoneFeature.GetStateAsync().ConfigureAwait(false);
        var isCurrentlyOn = currentState == MicrophoneState.On;

        var newState = isCurrentlyOn ? MicrophoneState.Off : MicrophoneState.On;
        var notification = isCurrentlyOn ? NotificationType.MicrophoneOff : NotificationType.MicrophoneOn;

        await microphoneFeature.SetStateAsync(newState).ConfigureAwait(false);
        MessagingCenter.Publish(new NotificationMessage(notification));

        await SpecialKeyLedHelper.SetLedAsync(isCurrentlyOn ? SpecialKeyLedState.MicrophoneOn : SpecialKeyLedState.MicrophoneOff).ConfigureAwait(false);
    }

    private async Task NotifyTouchpadLockAsync()
    {
        if (!await touchpadLockFeature.IsSupportedAsync().ConfigureAwait(false))
            return;
        var status = await touchpadLockFeature.GetStateAsync().ConfigureAwait(false);
        MessagingCenter.Publish(status == TouchpadLockState.Off
            ? new NotificationMessage(NotificationType.TouchpadOn)
            : new NotificationMessage(NotificationType.TouchpadOff));
    }

    private async Task NotifyWhiteBacklightAsync()
    {
        if (await whiteKeyboardBacklightFeature.IsSupportedAsync().ConfigureAwait(false))
        {
            var state = await whiteKeyboardBacklightFeature.GetStateAsync().ConfigureAwait(false);
            MessagingCenter.Publish(state == WhiteKeyboardBacklightState.Off
                ? new NotificationMessage(NotificationType.WhiteKeyboardBacklightOff, state.GetDisplayName())
                : new NotificationMessage(NotificationType.WhiteKeyboardBacklightChanged, state.GetDisplayName()));
            return;
        }

        try
        {
            var handle = Drivers.GetEnergy();
            if (PInvokeExtensions.DeviceIoControl(handle, Drivers.IOCTL_ENERGY_KEYBOARD, 0x1u, out uint buf))
            {
                if ((buf & 1) == 1)
                    buf >>= 1;
                var state = buf switch
                {
                    0 => WhiteKeyboardBacklightState.Off,
                    1 => WhiteKeyboardBacklightState.Low,
                    _ => WhiteKeyboardBacklightState.High,
                };
                MessagingCenter.Publish(state == WhiteKeyboardBacklightState.Off
                    ? new NotificationMessage(NotificationType.WhiteKeyboardBacklightOff, state.GetDisplayName())
                    : new NotificationMessage(NotificationType.WhiteKeyboardBacklightChanged, state.GetDisplayName()));

                Log.Instance.Trace($"DEBUG: {state}");
            }
        }
        catch (Exception ex)
        {
            Log.Instance.Trace($"Backlight read failed. [message={ex.Message}]");
        }
    }

    private static unsafe bool BindListener(WaitHandle waitHandle)
    {
        var handle = (uint)waitHandle.SafeWaitHandle.DangerousGetHandle();
        return PInvoke.DeviceIoControl(new HANDLE(Drivers.GetEnergy().DangerousGetHandle()),
            Drivers.IOCTL_KEY_WAIT_HANDLE,
            &handle,
            16,
            null,
            0,
            null,
            null);
    }

    private static unsafe bool GetValue(out uint value)
    {
        uint inBuff = 0;
        uint outBuff = 0;
        var result = PInvoke.DeviceIoControl(new HANDLE(Drivers.GetEnergy().DangerousGetHandle()),
            Drivers.IOCTL_KEY_VALUE,
            &inBuff,
            4,
            &outBuff,
            4,
            null,
            null);
        value = outBuff;
        return result;
    }
}