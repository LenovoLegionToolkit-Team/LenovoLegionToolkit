using System;
using System.Threading.Tasks;
using LenovoLegionToolkit.Lib.Controllers;
using LenovoLegionToolkit.Lib.Features;
using LenovoLegionToolkit.Lib.Messaging;
using LenovoLegionToolkit.Lib.Messaging.Messages;
using LenovoLegionToolkit.Lib.Utils;

namespace LenovoLegionToolkit.Lib.Listeners;

public class ITSModeListener(
    WindowsPowerModeController windowsPowerModeController,
    WindowsPowerPlanController windowsPowerPlanController) : IListener<ITSModeListener.ChangedEventArgs>, IDisposable
{
    public class ChangedEventArgs(ITSMode state) : EventArgs
    {
        public ITSMode State { get; } = state;
    }

    public event EventHandler<ChangedEventArgs>? Changed;

    public async Task StartAsync()
    {
        var mi = await Compatibility.GetMachineInformationAsync().ConfigureAwait(false);
        if (!mi.Properties.SupportsITSMode)
        {
            return;
        }

        MessagingCenter.Subscribe<DriverKeyPressedMessage>(this, OnFnQKeyPressedAsync);
        Log.Instance.Trace($"ITSModeListener started, listening for driver keys.");
    }

    public Task StopAsync()
    {
        MessagingCenter.Unsubscribe<DriverKeyPressedMessage>(this);
        return Task.CompletedTask;
    }

    private async void OnFnQKeyPressedAsync(DriverKeyPressedMessage message)
    {
        if (message.Key != DriverKey.FnQ)
        {
            return;
        }

        Log.Instance.Trace($"ITSModeListener detected Fn+Q, requesting toggle.");
        MessagingCenter.Publish(new ITSModeToggleRequestMessage());
    }

    public Task NotifyAsync(ITSMode value)
    {
        return OnChangedAsync(value, false);
    }

    internal virtual async Task OnChangedAsync(ITSMode value, bool showNotification)
    {
        await ChangeDependenciesAsync(value).ConfigureAwait(false);
        if (showNotification)
        {
            PublishNotification(value);
        }

        RaiseChanged(value);
    }

    private async Task ChangeDependenciesAsync(ITSMode value)
    {
        await windowsPowerModeController.SetPowerModeAsync(value).ConfigureAwait(false);
        await windowsPowerPlanController.SetPowerPlanAsync(value, true).ConfigureAwait(false);
    }

    internal static void PublishNotification(ITSMode value)
    {
        var displayName = IoCContainer.Resolve<ITSModeFeature>().GetITSModeDisplayName(value);

        switch (value)
        {
            case ITSMode.ItsAuto:
                MessagingCenter.Publish(new NotificationMessage(NotificationType.ITSModeAuto, displayName));
                break;
            case ITSMode.MmcCool:
                MessagingCenter.Publish(new NotificationMessage(NotificationType.ITSModeCool, displayName));
                break;
            case ITSMode.MmcPerformance:
                MessagingCenter.Publish(new NotificationMessage(NotificationType.ITSModePerformance, displayName));
                break;
            case ITSMode.MmcGeek:
                MessagingCenter.Publish(new NotificationMessage(NotificationType.ITSModeGeek, displayName));
                break;
        }
    }

    protected void RaiseChanged(ITSMode value)
    {
        Changed?.Invoke(this, new ChangedEventArgs(value));
    }

    public void Dispose()
    {
        MessagingCenter.Unsubscribe<DriverKeyPressedMessage>(this);
        GC.SuppressFinalize(this);
    }
}
