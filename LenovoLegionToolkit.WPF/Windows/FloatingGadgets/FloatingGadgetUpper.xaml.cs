using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using LenovoLegionToolkit.Lib;
using LenovoLegionToolkit.Lib.Controllers.Sensors;
using LenovoLegionToolkit.Lib.Messaging;
using LenovoLegionToolkit.Lib.Messaging.Messages;
using LenovoLegionToolkit.Lib.Settings;
using LenovoLegionToolkit.Lib.Utils;
using LenovoLegionToolkit.WPF.Resources;

namespace LenovoLegionToolkit.WPF.Windows.FloatingGadgets;

public partial class FloatingGadgetUpper
{
    #region Win32 Constants
    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_TRANSPARENT = 0x00000020;
    private const int WS_EX_TOOLWINDOW = 0x00000080;
    private const int WS_EX_NOACTIVATE = 0x08000000;
    #endregion

    #region Threshold Constants
    private const int UI_UPDATE_THROTTLE_MS = 500;
    private const int FPS_RED_LINE = 30;
    private const double MAX_FRAME_TIME_MS = 10.0;

    private const long FRAMETIME_TIMEOUT_TICKS = 2 * 10_000_000;

    private const double USAGE_YELLOW = 70;
    private const double USAGE_RED = 90;
    private const double MEM_USAGE_YELLOW = 75;
    private const double MEM_USAGE_RED = 80;

    private const double CPU_TEMP_YELLOW = 75;
    private const double CPU_TEMP_RED = 90;
    private const double GPU_TEMP_YELLOW = 70;
    private const double GPU_TEMP_RED = 80;
    private const double MEM_TEMP_YELLOW = 60;
    private const double MEM_TEMP_RED = 75;
    private const double PCH_TEMP_YELLOW = 60;
    private const double PCH_TEMP_RED = 75;
    #endregion

    #region Data Structures
    private readonly struct SensorSnapshot
    {
        public double CpuUsage { get; init; }
        public double CpuFrequency { get; init; }
        public double CpuPClock { get; init; }
        public double CpuEClock { get; init; }
        public double CpuTemp { get; init; }
        public double CpuPower { get; init; }
        public int CpuFanSpeed { get; init; }

        public double GpuUsage { get; init; }
        public double GpuFrequency { get; init; }
        public double GpuTemp { get; init; }
        public double GpuVramTemp { get; init; }
        public double GpuPower { get; init; }
        public int GpuFanSpeed { get; init; }

        public double MemUsage { get; init; }
        public double MemTemp { get; init; }

        public double PchTemp { get; init; }
        public int PchFanSpeed { get; init; }
    }

    private readonly struct FpsDisplayData
    {
        public string? FpsText { get; init; }
        public Brush? FpsBrush { get; init; }
        public string? LowFpsText { get; init; }
        public Brush? LowFpsBrush { get; init; }
        public string? FrameTimeText { get; init; }
        public Brush? FrameTimeBrush { get; init; }
    }
    #endregion

    private readonly ApplicationSettings _settings = IoCContainer.Resolve<ApplicationSettings>();
    private readonly FloatingGadgetSettings _floatingGadgetSettings = IoCContainer.Resolve<FloatingGadgetSettings>();
    private readonly SensorsController _controller = IoCContainer.Resolve<SensorsController>();
    private readonly SensorsGroupController _sensorsGroupControllers = IoCContainer.Resolve<SensorsGroupController>();
    private readonly FpsSensorController _fpsController = IoCContainer.Resolve<FpsSensorController>();

    private readonly SemaphoreSlim _refreshLock = new(1, 1);
    private readonly StringBuilder _stringBuilder = new(64);

    private DateTime _lastUpdate = DateTime.MinValue;

    private long _lastValidFpsTick;
    private long _lastFpsUiUpdateTick;

    private bool _positionSet;   
    private bool _fpsMonitoringStarted;

    private HashSet<FloatingGadgetItem> _activeItems;

    private readonly Dictionary<FrameworkElement, (List<FloatingGadgetItem> Items, Rectangle? Separator)> _gadgetGroups;
    private readonly Dictionary<FloatingGadgetItem, FrameworkElement> _itemsMap;

    public FloatingGadgetUpper()
    {
        InitializeComponent();

        if (!AppFlags.Instance.EnableHardwareAcceleration && !_settings.Store.EnableHardwareAcceleration)
            RenderOptions.ProcessRenderMode = RenderMode.SoftwareOnly;

        _activeItems = new HashSet<FloatingGadgetItem>(_floatingGadgetSettings.Store.Items);

        _itemsMap = new()
        {
            { FloatingGadgetItem.Fps, _fps },
            { FloatingGadgetItem.LowFps, _lowFps },
            { FloatingGadgetItem.FrameTime, _frameTime },
            { FloatingGadgetItem.CpuUtilization, _cpuUsage },
            { FloatingGadgetItem.CpuFrequency, _cpuFrequency },
            { FloatingGadgetItem.CpuPCoreFrequency, _cpuPFrequency },
            { FloatingGadgetItem.CpuECoreFrequency, _cpuEFrequency },
            { FloatingGadgetItem.CpuTemperature, _cpuTemperature },
            { FloatingGadgetItem.CpuPower, _cpuPower },
            { FloatingGadgetItem.CpuFan, _cpuFanSpeed },
            { FloatingGadgetItem.GpuUtilization, _gpuUsage },
            { FloatingGadgetItem.GpuFrequency, _gpuFrequency },
            { FloatingGadgetItem.GpuTemperature, _gpuTemperature },
            { FloatingGadgetItem.GpuVramTemperature, _gpuVramTemperature },
            { FloatingGadgetItem.GpuPower, _gpuPower },
            { FloatingGadgetItem.GpuFan, _gpuFanSpeed },
            { FloatingGadgetItem.MemoryUtilization, _memUsage },
            { FloatingGadgetItem.MemoryTemperature, _memTemperature },
            { FloatingGadgetItem.PchTemperature, _pchTemperature },
            { FloatingGadgetItem.PchFan, _pchFanSpeed },
        };

        _gadgetGroups = new()
        {
            { _fpsGroup, ([FloatingGadgetItem.Fps, FloatingGadgetItem.LowFps, FloatingGadgetItem.FrameTime], _separatorFps) },
            { _cpuGroup, ([FloatingGadgetItem.CpuUtilization, FloatingGadgetItem.CpuFrequency, FloatingGadgetItem.CpuPCoreFrequency, FloatingGadgetItem.CpuECoreFrequency, FloatingGadgetItem.CpuTemperature, FloatingGadgetItem.CpuPower, FloatingGadgetItem.CpuFan], _separatorCpu) },
            { _gpuGroup, ([FloatingGadgetItem.GpuUtilization, FloatingGadgetItem.GpuFrequency, FloatingGadgetItem.GpuTemperature, FloatingGadgetItem.GpuVramTemperature, FloatingGadgetItem.GpuPower, FloatingGadgetItem.GpuFan], _separatorGpu) },
            { _memoryGroup, ([FloatingGadgetItem.MemoryUtilization, FloatingGadgetItem.MemoryTemperature], _separatorMemory) },
            { _pchGroup, ([FloatingGadgetItem.PchTemperature, FloatingGadgetItem.PchFan], null) }
        };

        if (_floatingGadgetSettings.Store.Items.Count == 0)
        {
            _floatingGadgetSettings.Store.Items = Enum.GetValues<FloatingGadgetItem>().ToList();
            _floatingGadgetSettings.SynchronizeStore();
            _activeItems = new HashSet<FloatingGadgetItem>(_floatingGadgetSettings.Store.Items);
        }

        IsVisibleChanged += FloatingGadget_IsVisibleChanged;
        SourceInitialized += OnSourceInitialized;
        Closed += FloatingGadget_Closed;
        Loaded += OnLoaded;
        ContentRendered += OnContentRendered;

        InitializeComponentSpecifics();
        SubscribeEvents();
        _fpsController.FpsDataUpdated += OnFpsDataUpdated;

        UpdateGadgetControlsVisibility();
    }

    [DllImport("user32.dll")]
    private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll")]
    private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

    private async void InitializeComponentSpecifics()
    {
        var mi = await Compatibility.GetMachineInformationAsync();
        if (mi.Properties.IsAmdDevice)
        {
            _pchName.Text = Resource.SensorsControl_Motherboard_Title;
        }
    }

    private void SubscribeEvents()
    {
        MessagingCenter.Subscribe<FloatingGadgetElementChangedMessage>(this, (message) =>
        {
            Dispatcher.Invoke(() =>
            {
                if (App.Current.FloatingGadget == null) return;

                var newItemsSet = new HashSet<FloatingGadgetItem>(message.Items);
                if (_activeItems.SetEquals(newItemsSet))
                {
                    return;
                }

                _activeItems = newItemsSet;
                UpdateGadgetControlsVisibility();
            });
        });
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        if (PresentationSource.FromVisual(this) is not HwndSource source)
        {
            return;
        }

        var hwnd = source.Handle;
        var extendedStyle = GetWindowLong(hwnd, GWL_EXSTYLE);
        SetWindowLong(hwnd, GWL_EXSTYLE, extendedStyle | WS_EX_TRANSPARENT | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE);
    }

    private void OnLoaded(object? sender, RoutedEventArgs e)
        => Dispatcher.BeginInvoke(new Action(SetWindowPosition), DispatcherPriority.Loaded);

    private void OnContentRendered(object? sender, EventArgs e)
    {
        if (!_positionSet)
            Dispatcher.BeginInvoke(new Action(SetWindowPosition), DispatcherPriority.Render);
    }

    private void FloatingGadget_IsVisibleChanged(object? sender, DependencyPropertyChangedEventArgs e)
    {
        if (IsVisible)
        {
            CheckAndUpdateFpsMonitoring();
            UpdateGadgetControlsVisibility();

            _sensorsGroupControllers.SensorsUpdated += OnSensorsUpdated;
            _sensorsGroupControllers.Start(this, TimeSpan.FromSeconds(_floatingGadgetSettings.Store.FloatingGadgetsRefreshInterval));
        }
        else
        {
            CheckAndUpdateFpsMonitoring();
            _sensorsGroupControllers.Stop(this);
            _sensorsGroupControllers.SensorsUpdated -= OnSensorsUpdated;
        }
    }

    private void FloatingGadget_Closed(object? sender, EventArgs e)
    {
        _refreshLock.Dispose();

        _fpsController.FpsDataUpdated -= OnFpsDataUpdated;
        _fpsController.Dispose();

        App.Current.FloatingGadget = null;
    }

    private void SetWindowPosition()
    {
        if (double.IsNaN(ActualWidth) || ActualWidth <= 0)
        {
            return;
        }

        var screenWidth = SystemParameters.PrimaryScreenWidth;
        var workArea = SystemParameters.WorkArea;

        Left = (screenWidth - ActualWidth) / 2 - 35;
        Top = workArea.Top;
        _positionSet = true;
    }

    private void UpdateGadgetControlsVisibility()
    {
        bool isHybrid = _sensorsGroupControllers.IsHybrid;

        foreach (var (item, element) in _itemsMap)
        {
            bool shouldShow = _activeItems.Contains(item);

            if (isHybrid)
            {
                if (item == FloatingGadgetItem.CpuFrequency) shouldShow = false;
            }
            else
            {
                if (item is FloatingGadgetItem.CpuPCoreFrequency or FloatingGadgetItem.CpuECoreFrequency)
                {
                    shouldShow = false;
                }
            }

            element.Visibility = shouldShow ? Visibility.Visible : Visibility.Collapsed;
        }

        var visibleGroups = new List<FrameworkElement>();

        foreach (var (groupPanel, (items, _)) in _gadgetGroups)
        {
            bool isGroupActive = items.Any(item =>
            {
                if (!_activeItems.Contains(item)) return false;

                if (isHybrid)
                {
                    if (item == FloatingGadgetItem.CpuFrequency) return false;
                }
                else
                {
                    if (item is FloatingGadgetItem.CpuPCoreFrequency or FloatingGadgetItem.CpuECoreFrequency) return false;
                }

                return true;
            });

            groupPanel.Visibility = isGroupActive ? Visibility.Visible : Visibility.Collapsed;
            if (isGroupActive) visibleGroups.Add(groupPanel);
        }

        foreach (var (groupPanel, (_, separator)) in _gadgetGroups)
        {
            if (separator == null) continue;

            int index = visibleGroups.IndexOf(groupPanel);
            separator.Visibility = (index >= 0 && index < visibleGroups.Count - 1)
                ? Visibility.Visible
                : Visibility.Collapsed;
        }

        CheckAndUpdateFpsMonitoring();
    }

    private async void CheckAndUpdateFpsMonitoring()
    {
        bool shouldMonitor = IsVisible && ShouldMonitorFps();

        switch (shouldMonitor)
        {
            case true when !_fpsMonitoringStarted:
                await StartFpsMonitoringAsync();
                _fpsMonitoringStarted = true;
                break;
            case false when _fpsMonitoringStarted:
                StopFpsMonitoring();
                _fpsMonitoringStarted = false;
                break;
        }
    }

    private bool ShouldMonitorFps() =>
        _activeItems.Contains(FloatingGadgetItem.Fps) ||
        _activeItems.Contains(FloatingGadgetItem.LowFps) ||
        _activeItems.Contains(FloatingGadgetItem.FrameTime);

    private void UpdateTextBlock(System.Windows.Controls.TextBlock tb, double value, string format, double yellowThreshold = double.MaxValue, double redThreshold = double.MaxValue)
    {
        if (tb.Visibility != Visibility.Visible) return;

        string text;
        Brush foreground = Brushes.White;

        if (double.IsNaN(value) || value < 0)
        {
            text = "-";
        }
        else
        {
            _stringBuilder.Clear();
            _stringBuilder.AppendFormat(format, value);
            text = _stringBuilder.ToString();

            if (yellowThreshold != double.MaxValue)
            {
                foreground = SeverityBrush(value, yellowThreshold, redThreshold);
            }
        }

        SetTextIfChanged(tb, text);
        SetForegroundIfChanged(tb, foreground);
    }

    private void UpdateTextBlock(System.Windows.Controls.TextBlock tb, int value)
    {
        if (tb.Visibility != Visibility.Visible) return;

        string text = value < 0 ? "-" : $"{value}RPM";
        SetTextIfChanged(tb, text);
    }

    private void UpdateSensorData(SensorSnapshot data)
    {
        UpdateTextBlock(_cpuFrequency, data.CpuFrequency, "{0}MHz");
        UpdateTextBlock(_cpuPFrequency, data.CpuPClock, "{0:F0}MHz");
        UpdateTextBlock(_cpuEFrequency, data.CpuEClock, "{0:F0}MHz");
        UpdateTextBlock(_cpuUsage, data.CpuUsage, "{0:F0}%", USAGE_YELLOW, USAGE_RED);
        UpdateTextBlock(_cpuTemperature, data.CpuTemp, "{0:F0}°C", CPU_TEMP_YELLOW, CPU_TEMP_RED);
        UpdateTextBlock(_cpuPower, data.CpuPower, "{0:F1}W");
        UpdateTextBlock(_cpuFanSpeed, data.CpuFanSpeed);

        UpdateTextBlock(_gpuFrequency, data.GpuFrequency, "{0}MHz");
        UpdateTextBlock(_gpuUsage, data.GpuUsage, "{0:F0}%", USAGE_YELLOW, USAGE_RED);
        UpdateTextBlock(_gpuTemperature, data.GpuTemp, "{0:F0}°C", GPU_TEMP_YELLOW, GPU_TEMP_RED);
        UpdateTextBlock(_gpuVramTemperature, data.GpuVramTemp, "{0:F0}°C", GPU_TEMP_YELLOW, GPU_TEMP_RED);
        UpdateTextBlock(_gpuPower, data.GpuPower, "{0:F1}W");
        UpdateTextBlock(_gpuFanSpeed, data.GpuFanSpeed);

        UpdateTextBlock(_memUsage, data.MemUsage, "{0:F0}%", MEM_USAGE_YELLOW, MEM_USAGE_RED);
        UpdateTextBlock(_memTemperature, data.MemTemp, "{0:F0}°C", MEM_TEMP_YELLOW, MEM_TEMP_RED);

        UpdateTextBlock(_pchTemperature, data.PchTemp, "{0:F0}°C", PCH_TEMP_YELLOW, PCH_TEMP_RED);
        UpdateTextBlock(_pchFanSpeed, data.PchFanSpeed);
    }

    private static Brush SeverityBrush(double value, double yellowThreshold, double redThreshold)
    {
        if (value >= redThreshold) return Brushes.Red;
        return value >= yellowThreshold ? Brushes.Goldenrod : Brushes.White;
    }

    private static void SetTextIfChanged(System.Windows.Controls.TextBlock tb, string text)
    {
        if (!string.Equals(tb.Text, text, StringComparison.Ordinal))
            tb.Text = text;
    }

    private static void SetForegroundIfChanged(System.Windows.Controls.TextBlock tb, Brush brush)
    {
        if (!ReferenceEquals(tb.Foreground, brush))
        {
            tb.Foreground = brush;
        }
    }

    #region FPS Monitoring
    private async Task StartFpsMonitoringAsync()
    {
        try { await _fpsController.StartMonitoringAsync(); }
        catch (Exception ex) { Log.Instance.Trace($"Failed to start FPS monitoring", ex); }
    }

    private void StopFpsMonitoring()
    {
        try { _fpsController.StopMonitoring(); }
        catch (Exception ex) { Log.Instance.Trace($"Failed to stop FPS monitoring", ex); }
    }

    private void OnFpsDataUpdated(object? sender, FpsSensorController.FpsData fpsData)
    {
        if (!_fpsMonitoringStarted) return;
        if (string.IsNullOrWhiteSpace(fpsData.Fps)) return;

        long currentTick = DateTime.Now.Ticks;

        int.TryParse(fpsData.Fps?.Trim(), out var fpsVal);
        int.TryParse(fpsData.LowFps?.Trim(), out var lowVal);
        double.TryParse(fpsData.FrameTime?.Trim(), out var ftVal);

        bool isSampleValid = fpsVal > 0;

        string? fpsText = null, lowText = null, ftText = null;
        Brush? fpsBrush = null, lowBrush = null, ftBrush = null;

        if (isSampleValid)
        {
            long elapsedTicks = currentTick - _lastFpsUiUpdateTick;
            if (elapsedTicks < TimeSpan.FromMilliseconds(UI_UPDATE_THROTTLE_MS).Ticks)
            {
                return;
            }

            _lastFpsUiUpdateTick = currentTick;
            _lastValidFpsTick = currentTick;

            const string dash = "-";

            fpsText = fpsVal.ToString();
            fpsBrush = (fpsVal < FPS_RED_LINE) ? Brushes.Red : Brushes.White;

            lowText = (lowVal > 0) ? lowVal.ToString() : dash;
            lowBrush = (lowVal > 0 && (fpsVal - lowVal) >= 30) ? Brushes.Red : Brushes.White;

            if (ftVal > 0.1)
            {
                ftText = $"{ftVal,5:F1}ms";
                ftBrush = (ftVal > MAX_FRAME_TIME_MS) ? Brushes.Red : Brushes.White;
            }
            else
            {
                ftText = dash;
                ftBrush = Brushes.White;
            }
        }
        else
        {
            if (currentTick - _lastValidFpsTick > FRAMETIME_TIMEOUT_TICKS)
            {
                const string dash = "-";
                fpsText = dash;
                fpsBrush = Brushes.White;
                lowText = dash;
                lowBrush = Brushes.White;
                ftText = dash;
                ftBrush = Brushes.White;

                _lastFpsUiUpdateTick = currentTick;
            }
            else
            {
                return;
            }
        }

        var displayData = new FpsDisplayData
        {
            FpsText = fpsText,
            FpsBrush = fpsBrush,
            LowFpsText = lowText,
            LowFpsBrush = lowBrush,
            FrameTimeText = ftText,
            FrameTimeBrush = ftBrush
        };

        Dispatcher.BeginInvoke(() => UpdateFpsDisplay(displayData), DispatcherPriority.Normal);
    }

    private void UpdateFpsDisplay(FpsDisplayData data)
    {
        if (data.FpsText != null)
        {
            SetTextIfChanged(_fps, data.FpsText);
            if (data.FpsBrush != null) SetForegroundIfChanged(_fps, data.FpsBrush);
        }

        if (data.LowFpsText != null)
        {
            SetTextIfChanged(_lowFps, data.LowFpsText);
            if (data.LowFpsBrush != null) SetForegroundIfChanged(_lowFps, data.LowFpsBrush);
        }

        if (data.FrameTimeText == null)
        {
            return;
        }

        SetTextIfChanged(_frameTime, data.FrameTimeText);
        if (data.FrameTimeBrush != null) SetForegroundIfChanged(_frameTime, data.FrameTimeBrush);
    }
    #endregion

    private async void OnSensorsUpdated(object? sender, EventArgs e)
    {
        var interval = TimeSpan.FromSeconds(_floatingGadgetSettings.Store.FloatingGadgetsRefreshInterval);
        if (DateTime.Now - _lastUpdate < interval) return;

        _lastUpdate = DateTime.Now;

        try
        {
            var dataTask = _controller.GetDataAsync();
            var cpuUsageTask = _sensorsGroupControllers.GetCpuUsageAsync();
            var cpuPowerTask = _sensorsGroupControllers.GetCpuPowerAsync();
            var cpuTempTask = _sensorsGroupControllers.GetCpuTemperatureAsync();
            var gpuClockTask = _sensorsGroupControllers.GetGpuCoreClockAsync();
            var gpuPowerTask = _sensorsGroupControllers.GetGpuPowerAsync();
            var gpuTempTask = _sensorsGroupControllers.GetGpuTemperatureAsync();
            var gpuVramTask = _sensorsGroupControllers.GetGpuVramTemperatureAsync();
            var memUsageTask = _sensorsGroupControllers.GetMemoryUsageAsync();
            var memTempTask = _sensorsGroupControllers.GetHighestMemoryTemperatureAsync();

            var cpuClockTask = !_sensorsGroupControllers.IsHybrid ? _sensorsGroupControllers.GetCpuCoreClockAsync() : Task.FromResult(float.NaN);
            var cpuPClockTask = _sensorsGroupControllers.IsHybrid ? _sensorsGroupControllers.GetCpuPCoreClockAsync() : Task.FromResult(float.NaN);
            var cpuEClockTask = _sensorsGroupControllers.IsHybrid ? _sensorsGroupControllers.GetCpuECoreClockAsync() : Task.FromResult(float.NaN);

            await Task.WhenAll(dataTask, cpuUsageTask, cpuPowerTask, cpuTempTask, gpuClockTask, gpuPowerTask, gpuTempTask, gpuVramTask, memUsageTask, memTempTask, cpuPClockTask, cpuEClockTask);

            var mainData = await dataTask;

            var snapshot = new SensorSnapshot
            {
                CpuUsage = await cpuUsageTask,
                CpuFrequency = await cpuClockTask,
                CpuPClock = await cpuPClockTask,
                CpuEClock = await cpuEClockTask,
                CpuTemp = await cpuTempTask,
                CpuPower = await cpuPowerTask,
                CpuFanSpeed = mainData.CPU.FanSpeed,

                GpuUsage = mainData.GPU.Utilization,
                GpuFrequency = await gpuClockTask,
                GpuTemp = await gpuTempTask,
                GpuVramTemp = await gpuVramTask,
                GpuPower = await gpuPowerTask,
                GpuFanSpeed = mainData.GPU.FanSpeed,

                MemUsage = await memUsageTask,
                MemTemp = await memTempTask,

                PchTemp = mainData.PCH.Temperature,
                PchFanSpeed = mainData.PCH.FanSpeed
            };

            await Dispatcher.BeginInvoke(() => UpdateSensorData(snapshot), DispatcherPriority.Normal);
        }
        catch (Exception ex)
        {
           Log.Instance.Trace($"Exception occurred when executing OnSensorsUpdated()", ex);
        }
    }
}