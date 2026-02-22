using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Threading;
using Humanizer;
using LenovoLegionToolkit.Lib;
using LenovoLegionToolkit.Lib.Controllers.Sensors;
using LenovoLegionToolkit.Lib.Messaging;
using LenovoLegionToolkit.Lib.Messaging.Messages;
using LenovoLegionToolkit.Lib.Settings;
using LenovoLegionToolkit.Lib.System;
using LenovoLegionToolkit.Lib.Utils;
using LenovoLegionToolkit.WPF.Resources;
using LenovoLegionToolkit.WPF.Settings;
using Wpf.Ui.Common;
using MenuItem = Wpf.Ui.Controls.MenuItem;

namespace LenovoLegionToolkit.WPF.Controls.Dashboard;

public partial class SensorsControlV2
{
    private readonly SensorsController _controller = IoCContainer.Resolve<SensorsController>();
    private readonly SensorsGroupController _sensorsGroupControllers = IoCContainer.Resolve<SensorsGroupController>();
    private readonly SensorsControlSettings _sensorsControlSettings = IoCContainer.Resolve<SensorsControlSettings>();
    private readonly ApplicationSettings _applicationSettings = IoCContainer.Resolve<ApplicationSettings>();
    private readonly DashboardSettings _dashboardSettings = IoCContainer.Resolve<DashboardSettings>();
    private readonly Lock _updateLock = new();
    private readonly Task<string> _cpuNameTask;
    private Task<string>? _gpuNameTask;
    private readonly HashSet<SensorItem> _activeSensorItems = [];
    private readonly Dictionary<SensorItem, FrameworkElement> _sensorItemToControlMap;

    public SensorsControlV2()
    {
        InitializeComponent();
        InitializeContextMenu();
        IsVisibleChanged += SensorsControl_IsVisibleChanged;

        _sensorsGroupControllers.SelectedGpuIsIgpu = _sensorsControlSettings.Store.SelectedGpuIsIgpu;

        _cpuNameTask = GetProcessedCpuName();
        _sensorItemToControlMap = new Dictionary<SensorItem, FrameworkElement>
        {
            { SensorItem.CpuUtilization, _cpuUtilizationGrid! },
            { SensorItem.CpuFrequency, _cpuCoreClockGrid! },
            { SensorItem.CpuFanSpeed, _cpuFanSpeedGrid! },
            { SensorItem.CpuTemperature, _cpuTemperatureGrid! },
            { SensorItem.CpuPower, _cpuPowerGrid! },

            { SensorItem.GpuUtilization, _gpuUtilizationGrid! },
            { SensorItem.GpuFrequency, _gpuCoreClockGrid! },
            { SensorItem.GpuFanSpeed, _gpuFanSpeedGrid! },
            { SensorItem.GpuTemperatures, _gpuTemperaturesGrid! },
            { SensorItem.GpuPower, _gpuPowerGrid! },

            { SensorItem.PchFanSpeed, _pchFanSpeedGrid! },
            { SensorItem.PchTemperature, _pchTemperatureGrid! },
            { SensorItem.BatteryState, _batteryStateGrid! },
            { SensorItem.BatteryLevel, _batteryLevelGrid! },
            { SensorItem.MemoryUtilization, _memoryUtilizationGrid! },
            { SensorItem.MemoryTemperature, _memoryTemperatureGrid! },
            { SensorItem.Disk1Temperature, _disk1TemperatureGrid! },
            { SensorItem.Disk2Temperature, _disk2TemperatureGrid! }
        };

        var mi = Compatibility.GetMachineInformationAsync().Result;
        if (mi.Properties.IsAmdDevice)
        {
            _pchGridName.Text = Resource.SensorsControl_Motherboard_Temperature;
        }

        MessagingCenter.Subscribe<DashboardElementChangedMessage>(this, message =>
        {
            Dispatcher.Invoke(() =>
            {
                lock (_updateLock)
                {
                    _activeSensorItems.Clear();

                    foreach (var item in message.Items)
                    {
                        _activeSensorItems.Add((SensorItem)(int)item);
                    }

                    UpdateControlsVisibility();
                }
            });
        });
    }

    private void UpdateControlsVisibility()
    {
        foreach (var kv in _sensorItemToControlMap)
        {
            kv.Value.Visibility = _activeSensorItems.Contains(kv.Key) ? Visibility.Visible : Visibility.Collapsed;
        }

        bool hasAnyGpuTemp = _activeSensorItems.Contains(SensorItem.GpuCoreTemperature) ||
                             _activeSensorItems.Contains(SensorItem.GpuVramTemperature);

        if (hasAnyGpuTemp)
        {
            _gpuTemperaturesGrid.Visibility = Visibility.Visible;
            _gpuCoreTempPanel.Visibility = _activeSensorItems.Contains(SensorItem.GpuCoreTemperature) ? Visibility.Visible : Visibility.Collapsed;
            _gpuVramTempPanel.Visibility = _activeSensorItems.Contains(SensorItem.GpuVramTemperature) ? Visibility.Visible : Visibility.Collapsed;
        }

        UpdateCardVisibility(_cpuCard, [SensorItem.CpuUtilization, SensorItem.CpuFrequency, SensorItem.CpuFanSpeed, SensorItem.CpuTemperature, SensorItem.CpuPower]);
        UpdateCardVisibility(_gpuCard, [SensorItem.GpuUtilization, SensorItem.GpuFrequency, SensorItem.GpuFanSpeed, SensorItem.GpuCoreTemperature, SensorItem.GpuVramTemperature, SensorItem.GpuPower]);
        UpdateMotherboardCardVisibility();
        UpdateMemoryDiskCardVisibility();
    }

    private void InitializeContextMenu()
    {
        ContextMenu = new ContextMenu();
        ContextMenu.Items.Add(new MenuItem { Header = Resource.SensorsControl_RefreshInterval, IsEnabled = false });
        foreach (var interval in new[] { 1, 2, 3, 5 })
        {
            var item = new MenuItem
            {
                SymbolIcon = _dashboardSettings.Store.SensorsRefreshIntervalSeconds == interval
                    ? SymbolRegular.Checkmark24
                    : SymbolRegular.Empty,
                Header = TimeSpan.FromSeconds(interval).Humanize(culture: Resource.Culture)
            };
            item.Click += (_, _) =>
            {
                _dashboardSettings.Store.SensorsRefreshIntervalSeconds = interval;
                _dashboardSettings.SynchronizeStore();
                InitializeContextMenu();
                if (IsVisible)
                {
                    _sensorsGroupControllers.Start(this, TimeSpan.FromSeconds(interval));
                }
            };
            ContextMenu.Items.Add(item);
        }
    }

    private void SensorsControl_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (IsVisible)
        {
            _sensorsGroupControllers.SelectedGpuIsIgpu = _sensorsControlSettings.Store.SelectedGpuIsIgpu;
            _sensorsGroupControllers.ShowAverageCpuFrequency = _sensorsControlSettings.Store.ShowCpuAverageFrequency;

            _activeSensorItems.Clear();
            if (_sensorsControlSettings.Store.VisibleItems != null)
            {
                foreach (SensorItem item in _sensorsControlSettings.Store.VisibleItems)
                {
                    _activeSensorItems.Add(item);
                }
            }

            UpdateControlsVisibility();
            _sensorsGroupControllers.SensorsUpdated += OnSensorsUpdated;
            _sensorsGroupControllers.Start(this, TimeSpan.FromSeconds(_dashboardSettings.Store.SensorsRefreshIntervalSeconds));
        }
        else
        {
            _sensorsGroupControllers.Stop(this);
            _sensorsGroupControllers.SensorsUpdated -= OnSensorsUpdated;
        }
    }

    private async void OnSensorsUpdated(object? sender, EventArgs e)
    {
        try
        {
            var dataTask = Task.Run(async () =>
            {
                try { return await _controller.GetDataAsync().ConfigureAwait(false); }
                catch { return default(SensorsData); }
            });

            var gpuNameTask = GetProcessedGpuName();
            var cpuUsageTask = _sensorsGroupControllers.GetCpuUsageAsync();
            var cpuTempTask = _sensorsGroupControllers.GetCpuTemperatureAsync();
            var cpuClockTask = _sensorsGroupControllers.IsHybrid
                ? _sensorsGroupControllers.GetCpuPCoreClockAsync()
                : _sensorsGroupControllers.GetCpuCoreClockAsync();
            var cpuPowerTask = _sensorsGroupControllers.GetCpuPowerAsync();

            var gpuUsageTask = _sensorsGroupControllers.GetGpuUsageAsync();
            var gpuTempTask = _sensorsGroupControllers.GetGpuTemperatureAsync();
            var gpuClockTask = _sensorsGroupControllers.GetGpuCoreClockAsync();
            var gpuPowerTask = _sensorsGroupControllers.GetGpuPowerAsync();
            var gpuVramTask = _sensorsGroupControllers.GetGpuVramTemperatureAsync();

            var diskTemperaturesTask = _sensorsGroupControllers.GetSsdTemperaturesAsync();
            var memoryUsageTask = _sensorsGroupControllers.GetMemoryUsageAsync();
            var memoryTemperaturesTask = _sensorsGroupControllers.GetHighestMemoryTemperatureAsync();

            var batteryInfoTask = Task.Run(Battery.GetBatteryInformation);

            await Task.WhenAll(
                dataTask,
                cpuUsageTask, gpuNameTask, cpuTempTask, cpuClockTask, cpuPowerTask,
                gpuUsageTask, gpuTempTask, gpuClockTask, gpuPowerTask, gpuVramTask,
                diskTemperaturesTask, memoryUsageTask, memoryTemperaturesTask,
                batteryInfoTask
            ).ConfigureAwait(false);

            _gpuNameTask = gpuNameTask;

            await Dispatcher.BeginInvoke(() => UpdateAllSensorValuesV2(
                dataTask.Result,
                cpuUsageTask.Result, cpuTempTask.Result, cpuClockTask.Result, cpuPowerTask.Result,
                gpuUsageTask.Result, gpuTempTask.Result, gpuClockTask.Result, gpuPowerTask.Result, gpuVramTask.Result,
                diskTemperaturesTask.Result, memoryUsageTask.Result, memoryTemperaturesTask.Result,
                batteryInfoTask.Result
            ), DispatcherPriority.Background);
        }
        catch (Exception ex)
        {
            Log.Instance.Trace($"Sensor refresh failed: {ex}");
            await Dispatcher.BeginInvoke(ClearAllSensorValues);
        }
    }

    private void ClearAllSensorValues()
    {
        lock (_updateLock)
        {
            UpdateValue(_cpuUtilizationBar, _cpuUtilizationLabel, -1, -1, "-");
            UpdateValue(_cpuCoreClockBar, _cpuCoreClockLabel, -1, -1, "-");
            UpdateValue(_cpuTemperatureBar, _cpuTemperatureLabel, -1, -1, "-");
            UpdateValue(_cpuFanSpeedBar, _cpuFanSpeedLabel, -1, -1, "-");
            UpdateValue(_cpuPowerLabel, "-");
            UpdateValue(_gpuUtilizationBar, _gpuUtilizationLabel, -1, -1, "-");
            UpdateValue(_gpuCoreClockBar, _gpuCoreClockLabel, -1, -1, "-");
            UpdateValue(_gpuFanSpeedBar, _gpuFanSpeedLabel, -1, -1, "-");

            _gpuCoreTemperatureLabel.Text = "-";
            _gpuMemoryTemperatureLabel.Text = "-";
            Grid.SetColumn(_gpuCoreTempPanel, 2);
            Grid.SetColumn(_gpuVramTempPanel, 3);
            _gpuVramTempPanel.Margin = new Thickness(12, 0, 0, 0);

            UpdateValue(_gpuPowerLabel, "-");
            UpdateValue(_pchTemperatureBar, _pchTemperatureLabel, -1, -1, "-");
            UpdateValue(_pchFanSpeedBar, _pchFanSpeedLabel, -1, -1, "-");
            UpdateValue(_disk1TemperatureBar, _disk1TemperatureLabel, -1, -1, "-");
            UpdateValue(_disk2TemperatureBar, _disk2TemperatureLabel, -1, -1, "-");
            UpdateValue(_memoryUtilizationBar, _memoryUtilizationLabel, -1, -1, "-");
            UpdateValue(_memoryTemperatureBar, _memoryTemperatureLabel, -1, -1, "-");
            UpdateBatteryStatus(_batteryStateLabel, null);
            UpdateValue(_batteryLevelBar, _batteryLevelLabel, -1, -1, "-");
        }
    }

    private void UpdateAllSensorValuesV2(
        SensorsData data,
        float cpuUsage, float cpuTemp, float cpuClock, float cpuPower,
        float gpuUsage, float gpuTemp, float gpuClock, float gpuPower, float gpuVramTemp,
        (float, float) diskTemps, float memoryUsage, double memoryTemp,
        BatteryInformation? batteryInfo)
    {
        lock (_updateLock)
        {
            foreach (var kv in _sensorItemToControlMap)
            {
                var control = kv.Value;
                control.Visibility = _activeSensorItems.Contains(kv.Key) ? Visibility.Visible : Visibility.Collapsed;
            }

            _cpuCardName.Text = _cpuNameTask.Result;
            _gpuCardName.Text = _gpuNameTask?.Result ?? "UNKNOWN";

            // --- CPU ---
            if (_activeSensorItems.Contains(SensorItem.CpuUtilization)) UpdateValue(_cpuUtilizationBar, _cpuUtilizationLabel, 100, cpuUsage, $"{cpuUsage:0}%");
            if (_activeSensorItems.Contains(SensorItem.CpuFrequency)) UpdateValue(_cpuCoreClockBar, _cpuCoreClockLabel, 6000, cpuClock, $"{cpuClock / 1000.0:0.0} {Resource.GHz}");
            if (_activeSensorItems.Contains(SensorItem.CpuTemperature)) UpdateValue(_cpuTemperatureBar, _cpuTemperatureLabel, 100, cpuTemp, GetTemperatureText(cpuTemp));
            if (_activeSensorItems.Contains(SensorItem.CpuPower)) UpdateValue(_cpuPowerLabel, $"{cpuPower:0}W");
            if (_activeSensorItems.Contains(SensorItem.CpuFanSpeed)) UpdateValue(_cpuFanSpeedBar, _cpuFanSpeedLabel, data.CPU.MaxFanSpeed, data.CPU.FanSpeed, $"{data.CPU.FanSpeed} {Resource.RPM}", $"{data.CPU.MaxFanSpeed} {Resource.RPM}");

            // --- GPU ---
            if (_activeSensorItems.Contains(SensorItem.GpuUtilization)) UpdateValue(_gpuUtilizationBar, _gpuUtilizationLabel, 100, gpuUsage, $"{gpuUsage:0}%");
            if (_activeSensorItems.Contains(SensorItem.GpuFrequency)) UpdateValue(_gpuCoreClockBar, _gpuCoreClockLabel, 3000, gpuClock, $"{gpuClock:0} {Resource.MHz}");
            if (_activeSensorItems.Contains(SensorItem.GpuPower)) UpdateValue(_gpuPowerLabel, $"{gpuPower:0}W");

            if (_activeSensorItems.Contains(SensorItem.GpuTemperatures))
            {
                bool showCoreTemp = _activeSensorItems.Contains(SensorItem.GpuCoreTemperature);
                bool showVramTemp = _activeSensorItems.Contains(SensorItem.GpuVramTemperature);
                _gpuCoreTempPanel.Visibility = showCoreTemp ? Visibility.Visible : Visibility.Collapsed;
                _gpuVramTempPanel.Visibility = showVramTemp ? Visibility.Visible : Visibility.Collapsed;

                if (showCoreTemp) UpdateTemperatureValue(_gpuCoreTemperatureLabel, gpuTemp);
                if (showVramTemp) UpdateTemperatureValue(_gpuMemoryTemperatureLabel, gpuVramTemp);

                switch (showCoreTemp)
                {
                    case true when showVramTemp:
                        Grid.SetColumn(_gpuCoreTempPanel, 2);
                        Grid.SetColumn(_gpuVramTempPanel, 3);
                        _gpuVramTempPanel.Margin = new Thickness(12, 0, 0, 0);
                        break;
                    case true:
                        Grid.SetColumn(_gpuCoreTempPanel, 3);
                        break;
                    default:
                        if (showVramTemp)
                        {
                            Grid.SetColumn(_gpuVramTempPanel, 3);
                            _gpuVramTempPanel.Margin = new Thickness(0);
                        }
                        break;
                }
            }

            if (_activeSensorItems.Contains(SensorItem.GpuFanSpeed)) UpdateValue(_gpuFanSpeedBar, _gpuFanSpeedLabel, data.GPU.MaxFanSpeed, data.GPU.FanSpeed, $"{data.GPU.FanSpeed} {Resource.RPM}", $"{data.GPU.MaxFanSpeed} {Resource.RPM}");

            // --- PCH / Motherboard ---
            if (_activeSensorItems.Contains(SensorItem.PchTemperature)) UpdateValue(_pchTemperatureBar, _pchTemperatureLabel, data.PCH.MaxTemperature, data.PCH.Temperature, GetTemperatureText(data.PCH.Temperature), GetTemperatureText(data.PCH.MaxTemperature));
            if (_activeSensorItems.Contains(SensorItem.PchFanSpeed)) UpdateValue(_pchFanSpeedBar, _pchFanSpeedLabel, data.PCH.MaxFanSpeed, data.PCH.FanSpeed, $"{data.PCH.FanSpeed} {Resource.RPM}", $"{data.PCH.MaxFanSpeed} {Resource.RPM}");

            // --- Disk & Memory ---
            if (_activeSensorItems.Contains(SensorItem.Disk1Temperature)) UpdateValue(_disk1TemperatureBar, _disk1TemperatureLabel, 100, diskTemps.Item1, GetTemperatureText(diskTemps.Item1));
            if (_activeSensorItems.Contains(SensorItem.Disk2Temperature)) UpdateValue(_disk2TemperatureBar, _disk2TemperatureLabel, 100, diskTemps.Item2, GetTemperatureText(diskTemps.Item2));
            if (_activeSensorItems.Contains(SensorItem.MemoryUtilization)) UpdateValue(_memoryUtilizationBar, _memoryUtilizationLabel, 100, memoryUsage, $"{memoryUsage:0}%");
            if (_activeSensorItems.Contains(SensorItem.MemoryTemperature)) UpdateValue(_memoryTemperatureBar, _memoryTemperatureLabel, 100, memoryTemp, GetTemperatureText(memoryTemp));

            // --- Battery ---
            if (_activeSensorItems.Contains(SensorItem.BatteryState)) UpdateBatteryStatus(_batteryStateLabel, batteryInfo);
            if (_activeSensorItems.Contains(SensorItem.BatteryLevel)) UpdateValue(_batteryLevelBar, _batteryLevelLabel, 100, batteryInfo?.BatteryPercentage ?? 0, batteryInfo != null ? $"{batteryInfo.Value.BatteryPercentage}%" : "-");

            UpdateCardVisibility(_cpuCard, [SensorItem.CpuUtilization, SensorItem.CpuFrequency, SensorItem.CpuFanSpeed, SensorItem.CpuTemperature, SensorItem.CpuPower]);
            UpdateCardVisibility(_gpuCard, [SensorItem.GpuUtilization, SensorItem.GpuFrequency, SensorItem.GpuFanSpeed, SensorItem.GpuCoreTemperature, SensorItem.GpuVramTemperature, SensorItem.GpuPower]);
            UpdateMotherboardCardVisibility();
            UpdateMemoryDiskCardVisibility();
        }
    }

    private void UpdateCardVisibility(FrameworkElement card, IEnumerable<SensorItem> sensorItems)
    {
        bool hasVisibleItems = sensorItems.Any(item => _activeSensorItems.Contains(item));
        card.Visibility = hasVisibleItems ? Visibility.Visible : Visibility.Collapsed;
    }

    private void UpdateMotherboardCardVisibility()
    {
        bool pchVisible = _activeSensorItems.Contains(SensorItem.PchFanSpeed) || _activeSensorItems.Contains(SensorItem.PchTemperature);
        bool batteryVisible = _activeSensorItems.Contains(SensorItem.BatteryState) || _activeSensorItems.Contains(SensorItem.BatteryLevel);

        _pchStackPanel.Visibility = pchVisible ? Visibility.Visible : Visibility.Collapsed;
        _batteryGrid.Visibility = batteryVisible ? Visibility.Visible : Visibility.Collapsed;

        _seperator1.Visibility = (pchVisible && batteryVisible) ? Visibility.Visible : Visibility.Collapsed;

        _motherboardCard.Visibility = (pchVisible || batteryVisible) ? Visibility.Visible : Visibility.Collapsed;
    }

    private void UpdateMemoryDiskCardVisibility()
    {
        var memoryItems = new[] { SensorItem.MemoryUtilization, SensorItem.MemoryTemperature };
        var diskItems = new[] { SensorItem.Disk1Temperature, SensorItem.Disk2Temperature };

        bool memoryVisible = memoryItems.Any(item => _activeSensorItems.Contains(item));
        bool diskVisible = diskItems.Any(item => _activeSensorItems.Contains(item));

        _memoryGrid.Visibility = memoryVisible ? Visibility.Visible : Visibility.Collapsed;
        _diskGrid.Visibility = diskVisible ? Visibility.Visible : Visibility.Collapsed;

        _seperator2.Visibility = (memoryVisible && diskVisible) ? Visibility.Visible : Visibility.Collapsed;

        _memoryDiskCard.Visibility = (memoryVisible || diskVisible) ? Visibility.Visible : Visibility.Collapsed;
    }

    private Task<string> GetProcessedCpuName() => _sensorsGroupControllers.GetCpuNameAsync();

    private Task<string> GetProcessedGpuName() => _sensorsGroupControllers.GetGpuNameAsync();

    private string GetTemperatureText(double temperature)
    {
        if (temperature <= 0) return "-";
        if (_applicationSettings.Store.TemperatureUnit == TemperatureUnit.F)
        {
            temperature = (temperature * 9 / 5) + 32;
            return $"{temperature:0}{Resource.Fahrenheit}";
        }
        return $"{temperature:0}{Resource.Celsius}";
    }

    private static void UpdateValue(RangeBase bar, TextBlock label, double max, double value, string text, string? toolTipText = null)
    {
        bool isMaxInvalid = double.IsNaN(max) || double.IsInfinity(max) || max <= 0;

        bool isValueInvalid = double.IsNaN(value) || double.IsInfinity(value) || value < 0;

        if (isMaxInvalid || isValueInvalid)
        {
            bar.Minimum = 0;
            bar.Maximum = 1;
            bar.Value = 0;
            label.Text = "-";
            label.ToolTip = null;
            label.Tag = 0;
        }
        else
        {
            bar.Minimum = 0;
            bar.Maximum = max;
            bar.Value = value;
            label.Text = text;
            label.ToolTip = toolTipText is null ? null : string.Format(Resource.SensorsControl_Maximum, toolTipText);
            label.Tag = value;
        }
    }

    private static void UpdateValue(TextBlock label, double max, double value, string text, string? toolTipText = null)
    {
        if (max <= 0 || value < 0)
        {
            label.Text = "-";
            label.ToolTip = null;
            label.Tag = 0;
        }
        else
        {
            label.Text = text;
            label.ToolTip = toolTipText is null ? null : string.Format(Resource.SensorsControl_Maximum, toolTipText);
            label.Tag = value;
        }
    }

    private static void UpdateBatteryStatus(TextBlock label, BatteryInformation? batteryInfo)
    {
        if (batteryInfo is null)
        {
            label.Text = "-";
            label.ToolTip = null;
            label.Tag = null;
        }
        else
        {
            label.Text = batteryInfo.Value.IsCharging
                ? Resource.DashboardBattery_AcConnected
                : Resource.DashboardBattery_AcDisconnected;
        }
    }

    private static void UpdateValue(TextBlock label, string str)
    {
        var processedStr = str.Replace("W", "");
        if (int.TryParse(processedStr, out var result))
        {
            label.Text = result <= 0 ? "-" : str;
        }
        else
        {
            label.Text = str;
        }
    }

    private void UpdateTemperatureValue(TextBlock label, double temperature)
    {
        if (temperature <= 0)
        {
            label.Text = "-";
            label.ToolTip = null;
        }
        else
        {
            label.Text = GetTemperatureText(temperature);
            label.ToolTip = null;
        }
    }
}