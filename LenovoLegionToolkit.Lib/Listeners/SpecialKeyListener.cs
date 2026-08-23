using System;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using LenovoLegionToolkit.Lib.Features;
using LenovoLegionToolkit.Lib.Messaging;
using LenovoLegionToolkit.Lib.Messaging.Messages;
using LenovoLegionToolkit.Lib.Settings;
using LenovoLegionToolkit.Lib.SoftwareDisabler;
using LenovoLegionToolkit.Lib.System;
using LenovoLegionToolkit.Lib.System.Management;
using LenovoLegionToolkit.Lib.Utils;

namespace LenovoLegionToolkit.Lib.Listeners;

public class SpecialKeyListener(
    ApplicationSettings settings,
    FnKeysDisabler fnKeysDisabler,
    RefreshRateFeature feature,
    MicrophoneFeature microphoneFeature,
    PrecisionTouchpadLockFeature precisionTouchpadLockFeature)
    : AbstractWMIListener<SpecialKeyListener.ChangedEventArgs, SpecialKey, int>(WMI.LenovoUtilityEvent.Listen)
{
    public class ChangedEventArgs(SpecialKey specialKey, int rawValue) : EventArgs
    {
        public SpecialKey SpecialKey { get; } = specialKey;
        public int RawValue { get; } = rawValue;
    }

    public Func<SpecialKey, Task<bool>>? CustomKeyHandler { get; set; }
    public bool DiscoveryMode { get; set; }

    private readonly ThrottleFirstDispatcher _refreshRateDispatcher = new(TimeSpan.FromSeconds(2), nameof(SpecialKeyListener));
    private int _currentRawValue;

    protected override SpecialKey GetValue(int value)
    {
        Log.Instance.Trace($"Event received. [value={value}]");
        _currentRawValue = value;
        return (SpecialKey)value;
    }

    protected override ChangedEventArgs GetEventArgs(SpecialKey value) => new(value, _currentRawValue);

    protected override async Task OnChangedAsync(SpecialKey value)
    {
        if (DiscoveryMode)
            return;

        try
        {
            if (await fnKeysDisabler.GetStatusAsync().ConfigureAwait(false) == SoftwareStatus.Enabled)
            {
                Log.Instance.Trace($"Ignoring, FnKeys are enabled.");
                return;
            }

            if (HandleNotification(value))
                return;

            if (CustomKeyHandler is not null && await CustomKeyHandler(value).ConfigureAwait(false))
                return;

            await RunBuiltInAsync(value).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Log.Instance.Trace($"Failed to handle key. [key={value}, value={(int)value}]", ex);
        }
    }

    private static bool HandleNotification(SpecialKey value)
    {
        if (value is SpecialKey.FnLockOn or SpecialKey.FnLockOff)
        {
            NotifyFnLockState(value);
            return true;
        }

        if (value is SpecialKey.CameraOn or SpecialKey.CameraOff)
        {
            NotifyCameraState(value);
            return true;
        }

        if (value is SpecialKey.SpectrumBacklightOff or SpecialKey.SpectrumBacklight1
            or SpecialKey.SpectrumBacklight2 or SpecialKey.SpectrumBacklight3)
        {
            var brightness = value switch
            {
                SpecialKey.SpectrumBacklightOff => SpectrumKeyboardBacklightBrightness.Off,
                SpecialKey.SpectrumBacklight1 => SpectrumKeyboardBacklightBrightness.Low,
                SpecialKey.SpectrumBacklight2 => SpectrumKeyboardBacklightBrightness.Medium,
                SpecialKey.SpectrumBacklight3 => SpectrumKeyboardBacklightBrightness.High,
                _ => SpectrumKeyboardBacklightBrightness.Off
            };
            NotifySpectrumBacklight(brightness);
            return true;
        }

        if (value is >= SpecialKey.SpectrumPreset1 and <= SpecialKey.SpectrumPreset6)
        {
            NotifySpectrumPreset(value - SpecialKey.SpectrumPreset1 + 1);
            return true;
        }

        if (value is SpecialKey.WhiteBacklightOff or SpecialKey.WhiteBacklight1
            or SpecialKey.WhiteBacklight2)
        {
            var state = value switch
            {
                SpecialKey.WhiteBacklightOff => WhiteKeyboardBacklightState.Off,
                SpecialKey.WhiteBacklight1 => WhiteKeyboardBacklightState.Low,
                SpecialKey.WhiteBacklight2 => WhiteKeyboardBacklightState.High,
                _ => WhiteKeyboardBacklightState.Off
            };
            NotifyWhiteBacklight(state);
            return true;
        }

        return false;
    }

    private async Task RunBuiltInAsync(SpecialKey value)
    {
        switch (value)
        {
            case SpecialKey.CameraOn or SpecialKey.CameraOff:
                NotifyCameraState(value);
                break;
            case SpecialKey.FnLockOn or SpecialKey.FnLockOff:
                NotifyFnLockState(value);
                break;
            case SpecialKey.FnR or SpecialKey.FnR2:
                await ToggleRefreshRateAsync().ConfigureAwait(false);
                break;
            case SpecialKey.FnPrtSc or SpecialKey.FnPrtSc2:
                OpenSnippingTool();
                break;
            case SpecialKey.SpectrumBacklightOff:
                NotifySpectrumBacklight(SpectrumKeyboardBacklightBrightness.Off);
                break;
            case SpecialKey.SpectrumBacklight1:
                NotifySpectrumBacklight(SpectrumKeyboardBacklightBrightness.Low);
                break;
            case SpecialKey.SpectrumBacklight2:
                NotifySpectrumBacklight(SpectrumKeyboardBacklightBrightness.Medium);
                break;
            case SpecialKey.SpectrumBacklight3:
                NotifySpectrumBacklight(SpectrumKeyboardBacklightBrightness.High);
                break;
            case SpecialKey.SpectrumPreset1:
                NotifySpectrumPreset(1);
                break;
            case SpecialKey.SpectrumPreset2:
                NotifySpectrumPreset(2);
                break;
            case SpecialKey.SpectrumPreset3:
                NotifySpectrumPreset(3);
                break;
            case SpecialKey.SpectrumPreset4:
                NotifySpectrumPreset(4);
                break;
            case SpecialKey.SpectrumPreset5:
                NotifySpectrumPreset(5);
                break;
            case SpecialKey.SpectrumPreset6:
                NotifySpectrumPreset(6);
                break;
            case SpecialKey.FnF4:
                await ToggleMicrophoneAsync().ConfigureAwait(false);
                break;
            case SpecialKey.FnF8:
                ToggleAirplaneMode();
                break;
            case SpecialKey.FnF8_ThinkBook:
                await TogglePrecisionTouchPad().ConfigureAwait(false);
                break;
            case SpecialKey.WhiteBacklightOff:
                NotifyWhiteBacklight(WhiteKeyboardBacklightState.Off);
                break;
            case SpecialKey.WhiteBacklight1:
                NotifyWhiteBacklight(WhiteKeyboardBacklightState.Low);
                break;
            case SpecialKey.WhiteBacklight2:
                NotifyWhiteBacklight(WhiteKeyboardBacklightState.High);
                break;
        }
    }

    private static void NotifyCameraState(SpecialKey value)
    {
        switch (value)
        {
            case SpecialKey.CameraOn:
                MessagingCenter.Publish(new NotificationMessage(NotificationType.CameraOn));
                break;
            case SpecialKey.CameraOff:
                MessagingCenter.Publish(new NotificationMessage(NotificationType.CameraOff));
                break;
        }
    }

    private static void NotifyFnLockState(SpecialKey value)
    {
        switch (value)
        {
            case SpecialKey.FnLockOn:
                MessagingCenter.Publish(new NotificationMessage(NotificationType.FnLockOn));
                break;
            case SpecialKey.FnLockOff:
                MessagingCenter.Publish(new NotificationMessage(NotificationType.FnLockOff));
                break;
        }
    }

    private Task ToggleRefreshRateAsync() => _refreshRateDispatcher.DispatchAsync(async () =>
    {
        try
        {
            if (!await feature.IsSupportedAsync().ConfigureAwait(false))
                return;

            Log.Instance.Trace($"Switch refresh rate after Fn+R...");

            var all = await feature.GetAllStatesAsync().ConfigureAwait(false);
            var current = await feature.GetStateAsync().ConfigureAwait(false);
            var excluded = settings.Store.ExcludedRefreshRates;

            var filtered = all.Except(excluded).ToArray();

            Log.Instance.Trace($"Refresh rates: [all={string.Join(", ", all.Select(r => r.Frequency))}]");
            Log.Instance.Trace($" - All: {string.Join(", ", all.Select(r => r.Frequency))}");
            Log.Instance.Trace($" - Excluded: {string.Join(", ", excluded.Select(r => r.Frequency))}");
            Log.Instance.Trace($" - Filtered: {string.Join(", ", filtered.Select(r => r.Frequency))}");

            if (filtered.Length < 2)
            {
                Log.Instance.Trace($"Can't switch refresh rate after Fn+R when there is less than 2 available.");
                return;
            }

            var currentIndex = Array.IndexOf(filtered, current);
            var newIndex = currentIndex + 1;
            if (newIndex >= filtered.Length)
                newIndex = 0;

            var next = filtered[newIndex];

            Log.Instance.Trace($"Switching refresh rate after Fn+R to {next}...");

            await feature.SetStateAsync(next).ConfigureAwait(false);

            _ = Task.Delay(TimeSpan.FromSeconds(1)).ContinueWith(_ =>
            {
                MessagingCenter.Publish(new NotificationMessage(NotificationType.RefreshRate, next.DisplayName));
            });

            Log.Instance.Trace($"Switched refresh rate after Fn+R to {next}.");
        }
        catch (Exception ex)
        {
            Log.Instance.Trace($"Failed to switch refresh rate after Fn+R.", ex);
        }
    });

    private static void ToggleAirplaneMode()
    {
        var isAirplaneModeOn = AirplaneMode.Toggle();
        MessagingCenter.Publish(new NotificationMessage(isAirplaneModeOn ? NotificationType.AirplaneModeOn : NotificationType.AirplaneModeOff));
    }

    public async Task TogglePrecisionTouchPad()
    {
        if (!await precisionTouchpadLockFeature.IsSupportedAsync().ConfigureAwait(false))
            return;

        var currentState = await precisionTouchpadLockFeature.GetStateAsync().ConfigureAwait(false);
        var newState = currentState == TouchpadLockState.On ? TouchpadLockState.Off : TouchpadLockState.On;
        await precisionTouchpadLockFeature.SetStateAsync(newState).ConfigureAwait(false);
        await NotifyTouchpadLockAsync().ConfigureAwait(false);
    }

    private static void OpenSnippingTool()
    {
        Log.Instance.Trace($"Starting snipping tool..");
        Process.Start("explorer", "ms-screenclip:");
    }

    private async Task NotifyTouchpadLockAsync()
    {
        if (!await precisionTouchpadLockFeature.IsSupportedAsync().ConfigureAwait(false))
            return;
        var status = await precisionTouchpadLockFeature.GetStateAsync().ConfigureAwait(false);
        MessagingCenter.Publish(status == TouchpadLockState.Off
            ? new NotificationMessage(NotificationType.TouchpadOn)
            : new NotificationMessage(NotificationType.TouchpadOff));
    }

    private static void NotifySpectrumBacklight(SpectrumKeyboardBacklightBrightness value)
    {
        var type = value is SpectrumKeyboardBacklightBrightness.Off
            ? NotificationType.SpectrumBacklightOff
            : NotificationType.SpectrumBacklightChanged;
        MessagingCenter.Publish(new NotificationMessage(type, value));
    }

    private static void NotifySpectrumPreset(int value) => MessagingCenter.Publish(new NotificationMessage(NotificationType.SpectrumBacklightPresetChanged, value));

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

    private static void NotifyWhiteBacklight(WhiteKeyboardBacklightState value)
    {
        var type = value is WhiteKeyboardBacklightState.Off
            ? NotificationType.WhiteKeyboardBacklightOff
            : NotificationType.WhiteKeyboardBacklightChanged;
        MessagingCenter.Publish(new NotificationMessage(type, value));
    }
}