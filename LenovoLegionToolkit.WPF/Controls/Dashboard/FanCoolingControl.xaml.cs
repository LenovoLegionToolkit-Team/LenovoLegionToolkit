using System;
using System.Threading.Tasks;
using System.Windows;
using LenovoLegionToolkit.Lib;
using LenovoLegionToolkit.Lib.Controllers;
using LenovoLegionToolkit.Lib.Utils;

namespace LenovoLegionToolkit.WPF.Controls.Dashboard;

public partial class FanCoolingControl
{
    private static readonly TimeSpan Cooldown = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan RunDuration = TimeSpan.FromSeconds(10);

    private readonly FanCoolingController _fanCoolingController = IoCContainer.Resolve<FanCoolingController>();

    private DateTime _nextAvailableAtUtc = DateTime.MinValue;

    public FanCoolingControl() => InitializeComponent();

    private async void RunButton_Click(object sender, RoutedEventArgs e)
    {
        if (DateTime.UtcNow < _nextAvailableAtUtc)
        {
            var remaining = _nextAvailableAtUtc - DateTime.UtcNow;
            Log.Instance.Trace($"Fan cleaning button press ignored because cooldown is active. [remaining={remaining}]");
            return;
        }

        _nextAvailableAtUtc = DateTime.UtcNow.Add(Cooldown);
        _runButton.IsEnabled = false;
        Log.Instance.Trace($"Fan cleaning button clicked. [runDuration={RunDuration}, cooldown={Cooldown}, nextAvailableAtUtc={_nextAvailableAtUtc:O}]");

        try
        {
            await _fanCoolingController.RunAsync(RunDuration).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            Log.Instance.Trace($"Couldn't run fan cleaning mode.", ex);
        }

        var waitTime = _nextAvailableAtUtc - DateTime.UtcNow;
        if (waitTime > TimeSpan.Zero)
        {
            Log.Instance.Trace($"Fan cleaning cooldown active. Waiting before re-enabling button. [remaining={waitTime}]");
            await Task.Delay(waitTime).ConfigureAwait(true);
        }

        _runButton.IsEnabled = true;
        Log.Instance.Trace($"Fan cleaning cooldown finished. Button re-enabled.");
    }

    protected override Task OnRefreshAsync()
    {
        _runButton.IsEnabled = DateTime.UtcNow >= _nextAvailableAtUtc;
        return Task.CompletedTask;
    }

    protected override void OnFinishedLoading() { }
}
