using System;
using System.Threading.Tasks;
using System.Timers;
using LenovoLegionToolkit.Lib.System;
using LenovoLegionToolkit.Lib.Utils;

namespace LenovoLegionToolkit.Lib.AutoListeners;

public class BatteryAutoListener : AbstractAutoListener<BatteryAutoListener.ChangedEventArgs>
{
    public class ChangedEventArgs(int percentage) : EventArgs
    {
        public int Percentage { get; } = percentage;
    }

    private readonly Timer _timer;

    private int _lastPercentage = -1;

    public BatteryAutoListener()
    {
        _timer = new Timer(30_000);
        _timer.Elapsed += Timer_Elapsed;
        _timer.AutoReset = true;
    }

    protected override Task StartAsync()
    {
        _lastPercentage = Battery.GetBatteryInformation().BatteryPercentage;

        if (!_timer.Enabled)
            _timer.Enabled = true;

        return Task.CompletedTask;
    }

    protected override Task StopAsync()
    {
        _timer.Enabled = false;
        _lastPercentage = -1;
        return Task.CompletedTask;
    }

    private void Timer_Elapsed(object? sender, ElapsedEventArgs e)
    {
        try
        {
            var percentage = Battery.GetBatteryInformation().BatteryPercentage;
            if (percentage == _lastPercentage)
                return;

            _lastPercentage = percentage;
            RaiseChanged(new ChangedEventArgs(percentage));
        }
        catch (Exception ex)
        {
            Log.Instance.Trace($"Error in BatteryAutoListener.", ex);
        }
    }
}
