using System;
using System.Diagnostics;
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

    protected override PowerModeState GetValue(int value)
    {
        var result = (PowerModeState)(value - 1);
        return result;
    }

    protected override ChangedEventArgs GetEventArgs(PowerModeState value) => new(value);

    protected override async Task OnChangedAsync(PowerModeState value)
    {
        Log.Instance.Trace($"PowerModeListener.OnChangedAsync (WMI event path): value={value}");
        var sw = Stopwatch.StartNew();
        await ChangeDependenciesAsync(value).ConfigureAwait(false);
        Log.Instance.Trace($"ChangeDependenciesAsync completed [elapsed={sw.ElapsedMilliseconds}ms]");
        PublishNotification(value);
    }

    public async Task NotifyAsync(PowerModeState value)
    {
        Log.Instance.Trace($"PowerModeListener.NotifyAsync (explicit path): value={value}");
        var sw = Stopwatch.StartNew();
        await ChangeDependenciesAsync(value).ConfigureAwait(false);
        Log.Instance.Trace($"ChangeDependenciesAsync completed [elapsed={sw.ElapsedMilliseconds}ms]");
        RaiseChanged(value);
    }

    protected override async Task<bool> CanStartAsync()
    {
        var mi = await Compatibility.GetMachineInformationAsync().ConfigureAwait(false);
        return Compatibility.IsLegion(mi.LegionSeries);
    }

    private async Task ChangeDependenciesAsync(PowerModeState value)
    {
        var sw = Stopwatch.StartNew();

        if (value is PowerModeState.GodMode)
        {
            Log.Instance.Trace($"Calling GodModeController.ApplyStateAsync...");
            var godSw = Stopwatch.StartNew();
            await godModeController.ApplyStateAsync().ConfigureAwait(false);
            Log.Instance.Trace($"ApplyStateAsync completed [elapsed={godSw.ElapsedMilliseconds}ms]");
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
