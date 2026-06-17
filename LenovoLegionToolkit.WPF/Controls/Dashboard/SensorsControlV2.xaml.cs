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
using LenovoLegionToolkit.WPF.Windows.Dashboard;
using Wpf.Ui.Common;
using MenuItem = Wpf.Ui.Controls.MenuItem;

namespace LenovoLegionToolkit.WPF.Controls.Dashboard;

public partial class SensorsControlV2
{
    private readonly SensorsController _controller = IoCContainer.Resolve<SensorsController>();
    private readonly SensorsGroupController _sensorsGroupControllers = IoCContainer.Resolve<SensorsGroupController>();
    private readonly SensorsControlSettings _sensorsControlSettings = IoCContainer.Resolve<SensorsControlSettings>();
    private readonly HardwareSensorSettings _hardwareSensorSettings = IoCContainer.Resolve<HardwareSensorSettings>();
    private readonly ApplicationSettings _applicationSettings = IoCContainer.Resolve<ApplicationSettings>();
    private readonly Lock _updateLock = new();
    private readonly Task<string> _cpuNameTask;
    private Task<string>? _gpuNameTask;
    private readonly HashSet<SensorItem> _activeSensorItems = [];
    private static readonly double[] AvailableRefreshIntervals = [0.5, 1, 2, 3, 4, 5];
    private readonly Dictionary<SensorItem, FrameworkElement> _sensorItemToControlMap;
    private int _lastVisibleCardCount = -1;
    private double _lastAdjustedWidth = -1;

    public SensorsControlV2()
    {
        InitializeComponent();
        InitializeContextMenu();
        IsVisibleChanged += SensorsControl_IsVisibleChanged;
        SizeChanged += (_, e) => { if (e.WidthChanged) AdjustCardWidths(); };

        _sensorsGroupControllers.SelectedGpuIsIgpu = _hardwareSensorSettings.Store.SelectedGpuIsIgpu;

        _cpuNameTask = GetProcessedCpuName();
        _sensorItemToControlMap = new Dictionary<SensorItem, FrameworkElement>
        {
            { SensorItem.CpuUtilization, _cpuUtilizationGrid! },
            { SensorItem.CpuFrequency, _cpuCoreClockGrid! },
            { SensorItem.CpuFanSpeed, _cpuFanSpeedGrid! },
            { SensorItem.CpuTemperature, _cpuTemperatureGrid! },
            { SensorItem.CpuPower, _cpuPowerGrid! },

            { SensorItem.GpuUtilization, _gpuUtilizationGrid! },
            { SensorItem.GpuVramUtilization, _gpuVramUtilizationGrid! },
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
                    if (IsVisible && _applicationSettings.Store.EnableHardwareSensors)
                        StartSensorUpdates();
                }
            });
        });

        MessagingCenter.Subscribe<FeatureStateMessage<HardwareSensorsState>>(this, message =>
        {
            Dispatcher.Invoke(() =>
            {
                if (message.State == HardwareSensorsState.Off)
                {
                    _sensorsGroupControllers.Stop(this);
                    _sensorsGroupControllers.SensorsUpdated -= OnSensorsUpdated;
                    ClearAllSensorValues();
                }
                else if (IsVisible)
                {
                    _sensorsGroupControllers.SensorsUpdated += OnSensorsUpdated;
                    StartSensorUpdates();
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
        UpdateCardVisibility(_gpuCard, [SensorItem.GpuUtilization, SensorItem.GpuVramUtilization, SensorItem.GpuFrequency, SensorItem.GpuFanSpeed, SensorItem.GpuCoreTemperature, SensorItem.GpuVramTemperature, SensorItem.GpuPower]);
        UpdateMotherboardCardVisibility();
        UpdateMemoryDiskCardVisibility();
        AdjustCardWidths();
    }

    private void InitializeContextMenu()
    {
        ContextMenu = new ContextMenu();
        ContextMenu.Items.Add(new MenuItem { Header = Resource.SensorsControl_RefreshInterval, IsEnabled = false });
        foreach (var interval in AvailableRefreshIntervals)
        {
            var item = new MenuItem
            {
                SymbolIcon = _sensorsControlSettings.Store.SensorsRefreshIntervalSeconds == interval
                    ? SymbolRegular.Checkmark24
                    : SymbolRegular.Empty,
                Header = TimeSpan.FromSeconds(interval).Humanize(culture: Resource.Culture)
            };
            item.Click += (_, _) =>
            {
                _sensorsControlSettings.Store.SensorsRefreshIntervalSeconds = interval;
                _sensorsControlSettings.SynchronizeStore();
                InitializeContextMenu();
                if (IsVisible)
                {
                    StartSensorUpdates(interval);
                }
            };
            ContextMenu.Items.Add(item);
        }
        ContextMenu.Items.Add(new Separator());
        var customizeItem = new MenuItem
        {
            Header = Resource.DashboardPage_Customize,
            SymbolIcon = SymbolRegular.Settings24
        };
        customizeItem.Click += (_, _) => EditSensorGroupWindow.ShowInstance();
        ContextMenu.Items.Add(customizeItem);
    }

    private void SensorsControl_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (IsVisible)
        {
            _sensorsGroupControllers.SelectedGpuIsIgpu = _hardwareSensorSettings.Store.SelectedGpuIsIgpu;
            _sensorsGroupControllers.ShowAverageCpuFrequency = _hardwareSensorSettings.Store.ShowCpuAverageFrequency;

            _activeSensorItems.Clear();
            if (_sensorsControlSettings.Store.VisibleItems != null)
            {
                foreach (SensorItem item in _sensorsControlSettings.Store.VisibleItems)
                {
                    _activeSensorItems.Add(item);
                }
            }

            UpdateControlsVisibility();

            if (!_applicationSettings.Store.EnableHardwareSensors)
                return;

            _sensorsGroupControllers.SensorsUpdated += OnSensorsUpdated;
            StartSensorUpdates();
        }
        else
        {
            _sensorsGroupControllers.Stop(this);
            _sensorsGroupControllers.SensorsUpdated -= OnSensorsUpdated;
        }
    }

    private void StartSensorUpdates(double? intervalSeconds = null)
    {
        var interval = TimeSpan.FromSeconds(intervalSeconds ?? _sensorsControlSettings.Store.SensorsRefreshIntervalSeconds);
        _sensorsGroupControllers.Start(this, interval, GetHardwareUpdateScope());
    }

    private HardwareUpdateScope GetHardwareUpdateScope()
    {
        var scope = HardwareUpdateScope.None;

        if (_activeSensorItems.Overlaps([
                SensorItem.CpuUtilization,
                SensorItem.CpuFrequency,
                SensorItem.CpuTemperature,
                SensorItem.CpuPower
            ]))
            scope |= HardwareUpdateScope.Cpu;

        if (_activeSensorItems.Overlaps([
                SensorItem.GpuUtilization,
                SensorItem.GpuFrequency,
                SensorItem.GpuCoreTemperature,
                SensorItem.GpuVramTemperature,
                SensorItem.GpuTemperatures,
                SensorItem.GpuPower,
                SensorItem.GpuVramUtilization
            ]))
            scope |= HardwareUpdateScope.Gpu;

        if (_activeSensorItems.Overlaps([
                SensorItem.MemoryUtilization,
                SensorItem.MemoryTemperature
            ]))
            scope |= HardwareUpdateScope.Memory;

        if (_activeSensorItems.Overlaps([
                SensorItem.CpuFanSpeed,
                SensorItem.GpuFanSpeed,
                SensorItem.PchFanSpeed,
                SensorItem.PchTemperature
            ]))
            scope |= HardwareUpdateScope.Fans;

        if (_activeSensorItems.Overlaps([
                SensorItem.Disk1Temperature,
                SensorItem.Disk2Temperature
            ]))
            scope |= HardwareUpdateScope.Storage;

        return scope;
    }

    private async void OnSensorsUpdated(HardwareSensorSnapshot snapshot)
    {
        try
        {
            var dataTask = Task.Run(async () =>
            {
                try { return await _controller.GetDataAsync().ConfigureAwait(false); }
                catch { return default(SensorsData); }
            });

            var batteryInfoTask = Task.Run(Battery.GetBatteryInformation);
            var gpuNameTask = GetProcessedGpuName();

            await Task.WhenAll(dataTask, batteryInfoTask, gpuNameTask).ConfigureAwait(false);

            _gpuNameTask = gpuNameTask;

            await Dispatcher.BeginInvoke(() => UpdateAllSensorValuesV2(
                dataTask.Result,
                snapshot,
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
            UpdateValue(_gpuVramUtilizationBar, _gpuVramUtilizationLabel, -1, -1, "-");
            UpdateValue(_gpuCoreClockBar, _gpuCoreClockLabel, -1, -1, "-");
            UpdateValue(_gpuFanSpeedBar, _gpuFanSpeedLabel, -1, -1, "-");

            _gpuCoreTemperatureLabel.Text = "-";
            _gpuMemoryTemperatureLabel.Text = "-";
            Grid.SetColumn(_gpuCoreTempPanel, 0);
            Grid.SetColumn(_gpuVramTempPanel, 1);
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
        HardwareSensorSnapshot snapshot,
        BatteryInformation? batteryInfo)
    {
        var cpuUsage = snapshot.CpuUsage;
        var cpuTemp = snapshot.CpuTemp;
        var cpuClock = _sensorsGroupControllers.ShowAverageCpuFrequency ? snapshot.CpuAvgClock : snapshot.CpuMaxClock;
        if (_sensorsGroupControllers.IsHybrid)
        {
            cpuClock = _sensorsGroupControllers.ShowAverageCpuFrequency ? snapshot.CpuPAvgClock : snapshot.CpuPClock;
        }
        var cpuPower = snapshot.CpuPower;

        var gpuUsage = snapshot.GpuUsage;
        var gpuVramUsage = snapshot.GpuVramUtilization;
        var gpuVramUsed = snapshot.GpuVramUsed;
        var gpuVramTotal = snapshot.GpuVramTotal;
        var gpuTemp = snapshot.GpuTemp;
        var gpuClock = snapshot.GpuClock;
        var gpuPower = snapshot.GpuPower;
        var gpuVramTemp = snapshot.GpuVramTemp;

        var diskTemps = snapshot.SsdTemps;
        var memoryUsage = snapshot.MemUsage;
        var memoryUsed = snapshot.MemUsed;
        var memoryTotal = snapshot.MemTotal;
        var memoryTemp = snapshot.MemMaxTemp;

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
            if (_activeSensorItems.Contains(SensorItem.CpuUtilization)) UpdateValue(_cpuUtilizationBar, _cpuUtilizationLabel, 100, cpuUsage, $"{cpuUsage:0}{Resource.Percent}");
            if (_activeSensorItems.Contains(SensorItem.CpuFrequency)) UpdateValue(_cpuCoreClockBar, _cpuCoreClockLabel, 6000, cpuClock, $"{cpuClock / 1000.0:0.0} {Resource.GHz}");
            if (_activeSensorItems.Contains(SensorItem.CpuTemperature)) UpdateValue(_cpuTemperatureBar, _cpuTemperatureLabel, 100, cpuTemp, GetTemperatureText(cpuTemp));
            if (_activeSensorItems.Contains(SensorItem.CpuPower)) UpdateValue(_cpuPowerLabel, $"{cpuPower:0} {Resource.Watt}");
            if (_activeSensorItems.Contains(SensorItem.CpuFanSpeed)) UpdateValue(_cpuFanSpeedBar, _cpuFanSpeedLabel, data.CPU.MaxFanSpeed, data.CPU.FanSpeed, $"{data.CPU.FanSpeed} {Resource.RPM}", $"{data.CPU.MaxFanSpeed} {Resource.RPM}");

            // --- GPU ---
            if (_activeSensorItems.Contains(SensorItem.GpuUtilization)) UpdateValue(_gpuUtilizationBar, _gpuUtilizationLabel, 100, gpuUsage, $"{gpuUsage:0}{Resource.Percent}");
            if (_activeSensorItems.Contains(SensorItem.GpuVramUtilization)) UpdateValue(_gpuVramUtilizationBar, _gpuVramUtilizationLabel, 100, gpuVramUsage, GetMemoryUsageText(gpuVramUsage, gpuVramUsed, gpuVramTotal));
            if (_activeSensorItems.Contains(SensorItem.GpuFrequency)) UpdateValue(_gpuCoreClockBar, _gpuCoreClockLabel, 3000, gpuClock, $"{gpuClock:0} {Resource.MHz}");
            if (_activeSensorItems.Contains(SensorItem.GpuPower)) UpdateValue(_gpuPowerLabel, $"{gpuPower:0} {Resource.Watt}");

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
                        Grid.SetColumn(_gpuCoreTempPanel, 0);
                        Grid.SetColumn(_gpuVramTempPanel, 1);
                        _gpuVramTempPanel.Margin = new Thickness(12, 0, 0, 0);
                        break;
                    case true:
                        Grid.SetColumn(_gpuCoreTempPanel, 0);
                        break;
                    default:
                        if (showVramTemp)
                        {
                            Grid.SetColumn(_gpuVramTempPanel, 0);
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
            if (_activeSensorItems.Contains(SensorItem.MemoryUtilization)) UpdateValue(_memoryUtilizationBar, _memoryUtilizationLabel, 100, memoryUsage, GetMemoryUsageText(memoryUsage, memoryUsed, memoryTotal));
            if (_activeSensorItems.Contains(SensorItem.MemoryTemperature)) UpdateValue(_memoryTemperatureBar, _memoryTemperatureLabel, 100, memoryTemp, GetTemperatureText(memoryTemp));

            // --- Battery ---
            if (_activeSensorItems.Contains(SensorItem.BatteryState)) UpdateBatteryStatus(_batteryStateLabel, batteryInfo);
            if (_activeSensorItems.Contains(SensorItem.BatteryLevel)) UpdateValue(_batteryLevelBar, _batteryLevelLabel, 100, batteryInfo?.BatteryPercentage ?? 0, batteryInfo != null ? $"{batteryInfo.Value.BatteryPercentage}{Resource.Percent}" : "-");

            UpdateCardVisibility(_cpuCard, [SensorItem.CpuUtilization, SensorItem.CpuFrequency, SensorItem.CpuFanSpeed, SensorItem.CpuTemperature, SensorItem.CpuPower]);
            UpdateCardVisibility(_gpuCard, [SensorItem.GpuUtilization, SensorItem.GpuVramUtilization, SensorItem.GpuFrequency, SensorItem.GpuFanSpeed, SensorItem.GpuCoreTemperature, SensorItem.GpuVramTemperature, SensorItem.GpuPower]);
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

    private void AdjustCardWidths()
    {
        var width = ActualWidth;
        if (width <= 0) return;

        var allCards = new FrameworkElement[] { _cpuCard, _gpuCard, _motherboardCard, _memoryDiskCard };
        var visibleCards = allCards.Where(c => c.Visibility == Visibility.Visible).ToList();
        var count = visibleCards.Count;
        if (count == 0) return;
        if (count == _lastVisibleCardCount && Math.Abs(width - _lastAdjustedWidth) < 1) return;

        _lastVisibleCardCount = count;
        _lastAdjustedWidth = width;

        const double cardMargin = 8;
        var cardsPerRow = Math.Max(1, Math.Min(count, (int)(width / (200 + cardMargin))));
        var cardWidth = Math.Max(200, (width - cardsPerRow * cardMargin) / cardsPerRow);

        foreach (var card in visibleCards)
            card.Width = cardWidth;
    }

    private Task<string> GetProcessedCpuName() => _sensorsGroupControllers.GetCpuNameAsync();

    private Task<string> GetProcessedGpuName() => _sensorsGroupControllers.GetGpuNameAsync();

    private string GetTemperatureText(double temperature)
    {
        if (double.IsNaN(temperature) || temperature < 0) return "-";
        if (_applicationSettings.Store.TemperatureUnit == TemperatureUnit.F)
        {
            var fahrenheit = temperature * 9.0 / 5.0 + 32.0;
            return $"{fahrenheit:0}{Resource.Fahrenheit}";
        }
        return $"{temperature:0}{Resource.Celsius}";
    }

    private string GetMemoryUsageText(double memoryUsage, double memoryUsed, double memoryTotal)
    {
        if (_hardwareSensorSettings.Store.DisplayMemoryInGigabytes)
        {
            if (memoryUsed >= 0 && memoryTotal > 0) return $"{memoryUsed:F1}/{memoryTotal:F1} {Resource.GB}";
            if (memoryUsed >= 0) return $"{memoryUsed:F1} {Resource.GB}";
            return "-";
        }

        return memoryUsage >= 0 ? $"{memoryUsage:0}{Resource.Percent}" : "-";
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
