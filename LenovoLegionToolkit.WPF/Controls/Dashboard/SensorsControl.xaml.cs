using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Threading;
using Humanizer;
using LenovoLegionToolkit.Lib;
using LenovoLegionToolkit.Lib.Controllers.Sensors;
using LenovoLegionToolkit.Lib.Settings;
using LenovoLegionToolkit.Lib.Utils;
using LenovoLegionToolkit.WPF.Resources;
using LenovoLegionToolkit.WPF.Settings;
using Wpf.Ui.Common;
using MenuItem = Wpf.Ui.Controls.MenuItem;

namespace LenovoLegionToolkit.WPF.Controls.Dashboard;

public partial class SensorsControl
{
    private readonly ISensorsController _controller = IoCContainer.Resolve<ISensorsController>();
    private readonly ApplicationSettings _applicationSettings = IoCContainer.Resolve<ApplicationSettings>();
    private readonly DashboardSettings _dashboardSettings = IoCContainer.Resolve<DashboardSettings>();

    private CancellationTokenSource? _cts;
    private Task? _refreshTask;

    public SensorsControl()
    {
        InitializeComponent();
        InitializeContextMenu();

        IsVisibleChanged += SensorsControl_IsVisibleChanged;
        Loaded += (_, _) => SyncRefreshing();
        Unloaded += (_, _) => StopRefreshing();
    }

    private void InitializeContextMenu()
    {
        ContextMenu = new ContextMenu();
        ContextMenu.Items.Add(new MenuItem { Header = Resource.SensorsControl_RefreshInterval, IsEnabled = false });

        foreach (var interval in new double[] { 0.5, 1, 2, 3, 4, 5 })
        {
            var item = new MenuItem
            {
                SymbolIcon = _dashboardSettings.Store.SensorsRefreshIntervalSeconds == interval ? SymbolRegular.Checkmark24 : SymbolRegular.Empty,
                Header = TimeSpan.FromSeconds(interval).Humanize(culture: Resource.Culture)
            };
            item.Click += (_, _) =>
            {
                _dashboardSettings.Store.SensorsRefreshIntervalSeconds = interval;
                _dashboardSettings.SynchronizeStore();
                InitializeContextMenu();
            };
            ContextMenu.Items.Add(item);
        }
    }

    private void SensorsControl_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e) => SyncRefreshing();

    private void SyncRefreshing()
    {
        if (Dispatcher.HasShutdownStarted)
            return;

        if (!IsLoaded || !IsVisible)
        {
            StopRefreshing();
            return;
        }

        if (_cts is not null)
            return;

        var previousTask = _refreshTask;
        var cts = new CancellationTokenSource();
        _cts = cts;
        _refreshTask = Task.Run(() => RefreshAsync(previousTask, cts));
    }

    private void StopRefreshing()
    {
        var cts = _cts;
        _cts = null;
        if (!Dispatcher.HasShutdownStarted)
            cts?.Cancel();
        UpdateValues(SensorsData.Empty);
    }

    private async Task RefreshAsync(Task? previousTask, CancellationTokenSource cts)
    {
        var token = cts.Token;
        try
        {
            if (previousTask is not null)
                await previousTask.ConfigureAwait(false);

            token.ThrowIfCancellationRequested();
            Log.Instance.Trace($"Sensors refresh started...");
            var prepared = false;

            while (!token.IsCancellationRequested)
            {
                try
                {
                    if (!prepared)
                    {
                        if (!await _controller.IsSupportedAsync().ConfigureAwait(false))
                            throw new InvalidOperationException("Sensors are unavailable.");

                        token.ThrowIfCancellationRequested();
                        await _controller.PrepareAsync().ConfigureAwait(false);
                        token.ThrowIfCancellationRequested();
                        prepared = true;
                    }

                    var data = await _controller.GetDataAsync().ConfigureAwait(false);
                    await UpdateValuesAsync(data, token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    prepared = false;
                    Log.Instance.Trace($"Sensors refresh failed.", ex);
                    await UpdateValuesAsync(SensorsData.Empty, token).ConfigureAwait(false);
                }

                var interval = _dashboardSettings.Store.SensorsRefreshIntervalSeconds;
                if (!double.IsFinite(interval) || interval < 0.5 || interval > 5)
                    interval = 1;

                await Task.Delay(TimeSpan.FromSeconds(interval), token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception ex)
        {
            Log.Instance.Trace($"Sensors refresh stopped unexpectedly.", ex);
        }
        finally
        {
            try
            {
                if (!Dispatcher.HasShutdownStarted)
                {
                    await Dispatcher.InvokeAsync(() =>
                    {
                        if (ReferenceEquals(_cts, cts))
                            _cts = null;
                    }, DispatcherPriority.Background);
                }
            }
            catch (OperationCanceledException) { }
            catch (InvalidOperationException) when (Dispatcher.HasShutdownStarted) { }
            finally
            {
                cts.Dispose();
                Log.Instance.Trace($"Sensors refresh stopped.");
            }
        }
    }

    private async Task UpdateValuesAsync(SensorsData data, CancellationToken token)
    {
        await Dispatcher.InvokeAsync(() =>
        {
            if (!token.IsCancellationRequested && IsLoaded && IsVisible)
                UpdateValues(data);
        }, DispatcherPriority.Background, token);
    }

    private void UpdateValues(SensorsData data)
    {
        UpdateValue(_cpuUtilizationBar, _cpuUtilizationLabel, data.CPU.MaxUtilization, data.CPU.Utilization,
            $"{data.CPU.Utilization}{Resource.Percent}");
        UpdateValue(_cpuCoreClockBar, _cpuCoreClockLabel, data.CPU.MaxCoreClock, data.CPU.CoreClock,
            $"{data.CPU.CoreClock / 1000.0:0.0} {Resource.GHz}", $"{data.CPU.MaxCoreClock / 1000.0:0.0} {Resource.GHz}");
        UpdateValue(_cpuTemperatureBar, _cpuTemperatureLabel, data.CPU.MaxTemperature, data.CPU.Temperature,
            GetTemperatureText(data.CPU.Temperature), GetTemperatureText(data.CPU.MaxTemperature));
        UpdateValue(_cpuFanSpeedBar, _cpuFanSpeedLabel, data.CPU.MaxFanSpeed, data.CPU.FanSpeed,
            $"{data.CPU.FanSpeed} {Resource.RPM}", $"{data.CPU.MaxFanSpeed} {Resource.RPM}");

        UpdateValue(_gpuUtilizationBar, _gpuUtilizationLabel, data.GPU.MaxUtilization, data.GPU.Utilization,
            $"{data.GPU.Utilization} {Resource.Percent}");
        UpdateValue(_gpuCoreClockBar, _gpuCoreClockLabel, data.GPU.MaxCoreClock, data.GPU.CoreClock,
            $"{data.GPU.CoreClock} {Resource.MHz}", $"{data.GPU.MaxCoreClock} {Resource.MHz}");
        UpdateValue(_gpuMemoryClockBar, _gpuMemoryClockLabel, data.GPU.MaxMemoryClock, data.GPU.MemoryClock,
            $"{data.GPU.MemoryClock} {Resource.MHz}", $"{data.GPU.MaxMemoryClock} {Resource.MHz}");
        UpdateValue(_gpuTemperatureBar, _gpuTemperatureLabel, data.GPU.MaxTemperature, data.GPU.Temperature,
            GetTemperatureText(data.GPU.Temperature), GetTemperatureText(data.GPU.MaxTemperature));
        UpdateValue(_gpuFanSpeedBar, _gpuFanSpeedLabel, data.GPU.MaxFanSpeed, data.GPU.FanSpeed,
            $"{data.GPU.FanSpeed} {Resource.RPM}", $"{data.GPU.MaxFanSpeed} {Resource.RPM}");
    }

    private string GetTemperatureText(double temperature)
    {
        if (double.IsNaN(temperature) || temperature < 0) return "-";

        if (_applicationSettings.Store.TemperatureUnit == TemperatureUnit.F)
        {
            var fahrenheit = temperature * 9.0 / 5.0 + 32.0;
            return $"{fahrenheit:0} {Resource.Fahrenheit}";
        }

        return $"{temperature:0} {Resource.Celsius}";
    }

    private static void UpdateValue(RangeBase bar, ContentControl label, double max, double value, string text, string? toolTipText = null)
    {
        if (double.IsNaN(max) || double.IsNaN(value) || max < 0 || value < 0)
        {
            bar.Minimum = 0;
            bar.Maximum = 1;
            bar.Value = 0;
            label.Content = "-";
            label.ToolTip = null;
            label.Tag = 0;
        }
        else
        {
            bar.Minimum = 0;
            bar.Maximum = max;
            bar.Value = value;
            label.Content = text;
            label.ToolTip = toolTipText is null ? null : string.Format(Resource.SensorsControl_Maximum, toolTipText);
            label.Tag = value;
        }
    }
}
