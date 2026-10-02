using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using LenovoLegionToolkit.Lib.Controllers;
using LenovoLegionToolkit.Lib.Controllers.GodMode;
using LenovoLegionToolkit.Lib.Extensions;
using LenovoLegionToolkit.Lib.Messaging;
using LenovoLegionToolkit.Lib.Messaging.Messages;
using LenovoLegionToolkit.Lib.Overclocking.Amd;
using LenovoLegionToolkit.Lib.System.Management;
using LenovoLegionToolkit.Lib.Utils;

namespace LenovoLegionToolkit.Lib.Listeners;

public class PowerModeListener(
    GodModeController godModeController,
    WindowsPowerModeController windowsPowerModeController,
    WindowsPowerPlanController windowsPowerPlanController)
    : AbstractWMIListener<PowerModeListener.ChangedEventArgs, PowerModeState, int>(WMI.LenovoGameZoneSmartFanModeEvent.Listen), INotifyingListener<PowerModeListener.ChangedEventArgs, PowerModeState>
{
    public class ChangedEventArgs(PowerModeState state) : EventArgs
    {
        public PowerModeState State { get; } = state;
    }

    private readonly SemaphoreSlim _changeLock = new(1, 1);
    private PowerModeState? _lastState;

    protected override PowerModeState GetValue(int value)
    {
        var result = (PowerModeState)(value - 1);
        return result;
    }

    protected override ChangedEventArgs GetEventArgs(PowerModeState value) => new(value);

    protected override bool RaiseChangedAutomatically => false;

    protected override Task OnChangedAsync(PowerModeState value) =>
        ProcessChangeAsync(value);

    public Task NotifyAsync(PowerModeState value)
    {
        return ProcessChangeAsync(value, force: true);
    }

    public Task ChangeAsync(PowerModeState value, Func<Task> change, bool applyGodModePreset = true) =>
        ProcessChangeAsync(value, change, applyGodModePreset, force: true);

    private async Task ProcessChangeAsync(PowerModeState value, Func<Task>? change = null,
        bool applyGodModePreset = true, bool force = false)
    {
        await _changeLock.WaitAsync().ConfigureAwait(false);
        try
        {
            if (!force && _lastState == value)
            {
                return;
            }

            _lastState = null;
            if (change is not null)
            {
                await change().ConfigureAwait(false);
            }

            PublishNotification(value);
            await ChangeDependenciesAsync(value, applyGodModePreset).ConfigureAwait(false);
            RaiseChanged(value);
            _lastState = value;
        }
        finally
        {
            _changeLock.Release();
        }
    }

    protected override async Task<bool> CanStartAsync()
    {
        var mi = await Compatibility.GetMachineInformationAsync().ConfigureAwait(false);
        return Compatibility.IsLegion(mi.LegionSeries);
    }

    private async Task ChangeDependenciesAsync(PowerModeState value, bool applyGodModePreset = true)
    {
        var sw = Stopwatch.StartNew();

        if (value is PowerModeState.GodMode)
        {
            Log.Instance.Trace($"Delaying GodMode apply... [applyGodModePreset={applyGodModePreset}]");
            await Task.Delay(TimeSpan.FromMilliseconds(100)).ConfigureAwait(false);

            if (applyGodModePreset)
            {
                Log.Instance.Trace($"Calling GodModeController.ApplyStateAsync...");
                var godSw = Stopwatch.StartNew();
                await godModeController.ApplyStateAsync().ConfigureAwait(false);
                Log.Instance.Trace($"ApplyStateAsync completed [elapsed={godSw.ElapsedMilliseconds}ms]");
            }
            else
            {
                Log.Instance.Trace($"Suppressed God Mode preset apply, caller takes over.");
            }
        }
        else
        {
            Log.Instance.Trace($"Delaying restore defaults in other power mode...");
            await Task.Delay(TimeSpan.FromMilliseconds(100)).ConfigureAwait(false);

            Log.Instance.Trace($"Calling GodModeController.RestoreDefaultsInOtherPowerModeAsync({value})...");
            var restSw = Stopwatch.StartNew();
            await godModeController.RestoreDefaultsInOtherPowerModeAsync(value).ConfigureAwait(false);
            Log.Instance.Trace($"RestoreDefaultsInOtherPowerModeAsync completed [elapsed={restSw.ElapsedMilliseconds}ms]");
        }

        await windowsPowerModeController.SetPowerModeAsync(value).ConfigureAwait(false);
        await windowsPowerPlanController.SetPowerPlanAsync(value).ConfigureAwait(false);

        var gpuOverclockController = IoCContainer.Resolve<GPUOverclockController>();
        Log.Instance.Trace($"Checking GPUOverclock IsSupportedAsync...");
        if (await gpuOverclockController.IsSupportedAsync().ConfigureAwait(false))
        {
            Log.Instance.Trace($"GPU overclock supported, scheduling re-apply after 1s");
            _ = Task.Run(async () =>
            {
                await Task.Delay(TimeSpan.FromSeconds(1)).ConfigureAwait(false);
                await gpuOverclockController.EnsureOverclockIsAppliedAsync().ConfigureAwait(false);
            });
        }

        var amdOverclockingController = IoCContainer.Resolve<AmdOverclockingController>();
        if (amdOverclockingController.IsActive() && !amdOverclockingController.AllowInAllPowerModes)
        {
            Log.Instance.Trace($"Applying AMD OC default profile...");
            await amdOverclockingController.ApplyDefaultProfileAsync().ConfigureAwait(false);
        }

        Log.Instance.Trace($"ChangeDependenciesAsync total [elapsed={sw.ElapsedMilliseconds}ms]");
    }

    private static void PublishNotification(PowerModeState value)
    {
        switch (value)
        {
            case PowerModeState.Quiet:
                MessagingCenter.Publish(new NotificationMessage(NotificationType.PowerModeQuiet, value.GetDisplayName()));
                break;
            case PowerModeState.Balance:
                MessagingCenter.Publish(new NotificationMessage(NotificationType.PowerModeBalance, value.GetDisplayName()));
                break;
            case PowerModeState.Performance:
                MessagingCenter.Publish(new NotificationMessage(NotificationType.PowerModePerformance, value.GetDisplayName()));
                break;
            case PowerModeState.Extreme:
                MessagingCenter.Publish(new NotificationMessage(NotificationType.PowerModeExtreme, value.GetDisplayName()));
                break;
            case PowerModeState.GodMode:
                MessagingCenter.Publish(new NotificationMessage(NotificationType.PowerModeGodMode, value.GetDisplayName()));
                break;
        }
    }
}
