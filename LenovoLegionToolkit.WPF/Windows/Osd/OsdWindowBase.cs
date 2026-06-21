using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;
using LenovoLegionToolkit.Lib;
using LenovoLegionToolkit.Lib.Controllers.Sensors;
using LenovoLegionToolkit.Lib.Listeners;
using LenovoLegionToolkit.Lib.Messaging;
using LenovoLegionToolkit.Lib.Messaging.Messages;
using LenovoLegionToolkit.Lib.Settings;
using LenovoLegionToolkit.Lib.Utils;
using LenovoLegionToolkit.WPF.Resources;
using LenovoLegionToolkit.WPF.Settings;
using LenovoLegionToolkit.WPF.Extensions;
using WpfScreenHelper;

namespace LenovoLegionToolkit.WPF.Windows.Osd;

public abstract class OsdWindowBase : Window
{
    public static readonly uint TaskbarCreatedMsg = WindowExtensions.RegisterWindowMessage("TaskbarCreated");

    #region Threshold Constants

    protected int _uiUpdateThrottleMs = 0;
    protected const double MAX_FRAME_TIME_MS = 10.0;
    protected const long FRAMETIME_TIMEOUT_TICKS = 2 * 10_000_000;

    protected Brush _categoryBrush = Brushes.White;
    protected Brush _labelBrush = Brushes.White;
    protected Brush _valueBrush = Brushes.White;
    protected Brush _warningBrush = Brushes.Goldenrod;
    protected Brush _criticalBrush = Brushes.Red;

    #endregion

    #region Services

    protected readonly OsdSettings _OsdSettings = IoCContainer.Resolve<OsdSettings>();
    protected readonly SensorsController _controller = IoCContainer.Resolve<SensorsController>();
    protected readonly SensorsGroupController _sensorsGroupControllers = IoCContainer.Resolve<SensorsGroupController>();
    protected readonly FpsSensorController _fpsController = IoCContainer.Resolve<FpsSensorController>();
    protected readonly HardwareSensorSettings _hardwareSensorSettings = IoCContainer.Resolve<HardwareSensorSettings>();
    protected readonly ApplicationSettings _applicationSettings = IoCContainer.Resolve<ApplicationSettings>();
    protected readonly SessionLockUnlockListener _sessionListener = IoCContainer.Resolve<SessionLockUnlockListener>();

    #endregion

    #region State

    private readonly SemaphoreSlim _refreshLock = new(1, 1);
    protected readonly StringBuilder _stringBuilder = new(64);

    protected DateTime _lastUpdate = DateTime.MinValue;
    private long _lastValidFpsTick;
    private long _lastFpsUiUpdateTick;

    private CancellationTokenSource? _cts;
    private IDisposable? _sensorSubscription;
    protected bool _positionSet;
    private bool _fpsMonitoringStarted;
    private bool _hasLenovoController;
    private bool _wasVisibleBeforeLock;

    protected HashSet<OsdItem> _activeItems = [];
    protected Dictionary<OsdItem, FrameworkElement> _itemsMap = [];
    protected Dictionary<FrameworkElement, (List<OsdItem> Items, FrameworkElement? Separator)> _measurementGroups = [];

    #endregion

    #region Initialization

    protected void InitOsd()
    {
        _activeItems = new HashSet<OsdItem>(_OsdSettings.Store.Items);
        ShowInTaskbar = false;

        IsVisibleChanged += OnVisibilityChanged;
        SourceInitialized += OnSourceInitialized;
        Closed += OnWindowClosed;
        Loaded += OnLoaded;
        LocationChanged += OnLocationChanged;
        MouseLeftButtonDown += OnMouseLeftButtonDown;
        SizeChanged += OnSizeChanged;

        SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;
        _sessionListener.Changed += OnSessionLockStateChanged;

        InitializeComponentSpecifics();
        SubscribeEvents();
        _fpsController.FpsDataUpdated += OnFpsDataUpdated;

        ApplyAppearanceSettings();
        UpdateMeasurementControlsVisibility();
    }

    private async void InitializeComponentSpecifics()
    {
        try
        {
            _hasLenovoController = await _controller.IsSupportedAsync();
        }
        catch
        {
            _hasLenovoController = false;
        }

        var mi = await Compatibility.GetMachineInformationAsync();
        if (!IsLoaded)
            return;

        if (mi.Properties.IsAmdDevice)
            OnAmdDeviceDetected();
    }

    protected abstract void OnAmdDeviceDetected();

    private void SubscribeEvents()
    {
        MessagingCenter.Subscribe<OsdElementChangedMessage>(this, (message) =>
        {
            Dispatcher.Invoke(() =>
            {
                if (App.Current.OsdWindow == null) return;

                var newItemsSet = new HashSet<OsdItem>(message.Items);
                if (_activeItems.SetEquals(newItemsSet)) return;

                _activeItems = newItemsSet;
                UpdateMeasurementControlsVisibility();
                if (IsVisible && _applicationSettings.Store.EnableHardwareSensors)
                    StartHardwareSensorUpdates();
            });
        });

        MessagingCenter.Subscribe<OsdAppearanceChangedMessage>(this, _ =>
        {
            Dispatcher.Invoke(ApplyAppearanceSettings);
        });
    }

    #endregion

    #region Window Events

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        this.SetClickThrough(_OsdSettings.Store.IsLocked);

        if (PresentationSource.FromVisual(this) is HwndSource source)
        {
            source.RemoveHook(WindowExtensions.WndProcHook);
            source.AddHook(WindowExtensions.WndProcHook);
        }
    }

    private void OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (!_OsdSettings.Store.IsLocked && e.ChangedButton == MouseButton.Left)
        {
            DragMove();

            var screen = WpfScreenHelper.Screen.FromWindow(this);
            if (screen != null)
            {
                var workArea = screen.WpfWorkingArea;

                double snapThreshold = _OsdSettings.Store.SnapThreshold;

                double left = this.Left;
                double top = this.Top;
                double width = this.ActualWidth > 0 ? this.ActualWidth : this.Width;
                double height = this.ActualHeight > 0 ? this.ActualHeight : this.Height;

                if (Math.Abs(left - workArea.Left) < snapThreshold)
                {
                    left = workArea.Left;
                }
                else if (Math.Abs(workArea.Right - (left + width)) < snapThreshold)
                {
                    left = workArea.Right - width;
                }

                if (Math.Abs(top - workArea.Top) < snapThreshold)
                {
                    top = workArea.Top;
                }
                else if (Math.Abs(workArea.Bottom - (top + height)) < snapThreshold)
                {
                    top = workArea.Bottom - height;
                }

                if (left < workArea.Left) left = workArea.Left;
                if (left + width > workArea.Right) left = workArea.Right - width;

                if (top < workArea.Top) top = workArea.Top;
                if (top + height > workArea.Bottom) top = workArea.Bottom - height;

                this.Left = left;
                this.Top = top;

                _OsdSettings.SynchronizeStore();
            }
        }
    }

    private async void OnLoaded(object? sender, RoutedEventArgs e)
    {
        await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Render);

        UpdateLayout();
        SetWindowPosition();
    }

    private void OnSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (SavedPositionX.HasValue)
            return;

        if (!_positionSet)
            return;

        var workArea = SystemParameters.WorkArea;

        Left = workArea.Left + (workArea.Width - ActualWidth) / 2;
    }

    protected virtual void SetWindowPosition()
    {
        if (SavedPositionX.HasValue && SavedPositionY.HasValue)
        {
            var savedX = SavedPositionX.Value;
            var savedY = SavedPositionY.Value;

            if (IsPositionOnScreen(savedX, savedY))
            {
                Left = savedX;
                Top = savedY;
                _positionSet = true;
                return;
            }

            SavedPositionX = null;
            SavedPositionY = null;
            _OsdSettings.SynchronizeStore();
        }

        SetDefaultWindowPosition();
    }

    protected abstract void SetDefaultWindowPosition();
    protected abstract double? SavedPositionX { get; set; }
    protected abstract double? SavedPositionY { get; set; }

    public void RecalculatePosition()
    {
        SavedPositionX = null;
        SavedPositionY = null;
        _OsdSettings.SynchronizeStore();

        SetDefaultWindowPosition();
    }

    private const int MONITOR_DEFAULTTONULL = 0;

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromPoint(POINT pt, int dwFlags);

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X;
        public int Y;
    }

    private static bool IsPositionOnScreen(double x, double y)
    {
        var pt = new POINT { X = (int)x, Y = (int)y };
        return MonitorFromPoint(pt, MONITOR_DEFAULTTONULL) != IntPtr.Zero;
    }

    private void OnLocationChanged(object? sender, EventArgs e)
    {
        if (!IsLoaded) return;

        SavedPositionX = Left;
        SavedPositionY = Top;
    }

    private void OnDisplaySettingsChanged(object? sender, EventArgs e)
    {
        Dispatcher.BeginInvoke(() =>
        {
            if (!IsPositionOnScreen(Left, Top))
            {
                SetDefaultWindowPosition();
            }
        });
    }

    private void OnSessionLockStateChanged(object? sender, SessionLockUnlockListener.ChangedEventArgs e)
    {
        Dispatcher.BeginInvoke(() =>
        {
            if (e.Locked)
            {
                if (IsVisible)
                {
                    _wasVisibleBeforeLock = true;
                    Hide();
                }
            }
            else
            {
                if (_wasVisibleBeforeLock)
                {
                    _wasVisibleBeforeLock = false;
                    Show();
                }
            }
        });
    }

    private async void OnVisibilityChanged(object? sender, DependencyPropertyChangedEventArgs e)
    {
        if (IsVisible)
        {
            _sensorsGroupControllers.ShowAverageCpuFrequency = _hardwareSensorSettings.Store.ShowCpuAverageFrequency;

            _cts?.Cancel();
            _cts?.Dispose();
            _cts = new CancellationTokenSource();

            CheckAndUpdateFpsMonitoring();
            UpdateMeasurementControlsVisibility();

            StartHardwareSensorUpdates();

            await TheRing(_cts.Token);
        }
        else
        {
            _cts?.Cancel();
            _sensorSubscription?.Dispose();
            CheckAndUpdateFpsMonitoring();
        }
    }

    private void StartHardwareSensorUpdates()
    {
        _sensorSubscription?.Dispose();

        if (!_applicationSettings.Store.EnableHardwareSensors)
            return;

        if (_activeItems.Count == 0)
            return;

        _sensorSubscription = _sensorsGroupControllers.Subscribe(TimeSpan.FromSeconds(_OsdSettings.Store.OsdRefreshInterval), _activeItems);
    }

    private void OnWindowClosed(object? sender, EventArgs e)
    {
        SavedPositionX = Left;
        SavedPositionY = Top;
        _OsdSettings.SynchronizeStore();

        SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;
        _sessionListener.Changed -= OnSessionLockStateChanged;

        _cts?.Cancel();
        _cts?.Dispose();
        _refreshLock.Dispose();

        _fpsController.FpsDataUpdated -= OnFpsDataUpdated;
        _fpsController.Dispose();

        MessagingCenter.Unsubscribe(this);

        App.Current.OsdWindow = null;
    }

    protected virtual void ApplyAppearanceSettings()
    {
        var converter = new BrushConverter();

        _categoryBrush = (Brush)converter.ConvertFromString(_OsdSettings.Store.CategoryColor)!;
        _labelBrush = (Brush)converter.ConvertFromString(_OsdSettings.Store.LabelColor)!;
        _valueBrush = (Brush)converter.ConvertFromString(_OsdSettings.Store.ValueColor)!;
        _warningBrush = (Brush)converter.ConvertFromString(_OsdSettings.Store.WarningColor)!;
        _criticalBrush = (Brush)converter.ConvertFromString(_OsdSettings.Store.CriticalColor)!;

        this.SetClickThrough(_OsdSettings.Store.IsLocked);

        if (!SavedPositionX.HasValue || !SavedPositionY.HasValue)
        {
            SetDefaultWindowPosition();
        }
    }

    protected void ApplyCornerRadius(Border border)
    {
        if (border != null)
        {
            border.CornerRadius = new CornerRadius(
                _OsdSettings.Store.CornerRadiusTop,
                _OsdSettings.Store.CornerRadiusTop,
                _OsdSettings.Store.CornerRadiusBottom,
                _OsdSettings.Store.CornerRadiusBottom);
        }
    }

    #endregion

    #region Visibility

    protected void UpdateMeasurementControlsVisibility()
    {
        bool isHybrid = _sensorsGroupControllers.IsHybrid;

        foreach (var (item, element) in _itemsMap)
        {
            bool shouldShow = _activeItems.Contains(item);

            if (isHybrid)
            {
                if (item == OsdItem.CpuFrequency) shouldShow = false;
            }
            else
            {
                if (item is OsdItem.CpuPCoreFrequency or OsdItem.CpuECoreFrequency)
                    shouldShow = false;
            }

            element.Visibility = shouldShow ? Visibility.Visible : Visibility.Collapsed;
            OnItemVisibilityChanged(element, shouldShow);
        }

        var visibleGroups = new List<FrameworkElement>();

        foreach (var (groupPanel, (items, _)) in _measurementGroups)
        {
            bool isGroupActive = items.Any(item =>
            {
                if (!_activeItems.Contains(item)) return false;
                if (isHybrid && item == OsdItem.CpuFrequency) return false;
                if (!isHybrid && item is OsdItem.CpuPCoreFrequency or OsdItem.CpuECoreFrequency) return false;
                return true;
            });

            groupPanel.Visibility = isGroupActive ? Visibility.Visible : Visibility.Collapsed;
            if (isGroupActive) visibleGroups.Add(groupPanel);
        }

        foreach (var (groupPanel, (_, separator)) in _measurementGroups)
        {
            if (separator == null) continue;

            int index = visibleGroups.IndexOf(groupPanel);
            separator.Visibility = (index >= 0 && index < visibleGroups.Count - 1)
                ? Visibility.Visible
                : Visibility.Collapsed;
        }

        CheckAndUpdateFpsMonitoring();
        this.EscalateZBand();
    }

    protected virtual void OnItemVisibilityChanged(FrameworkElement element, bool visible) { }

    #endregion

    #region UI Helpers

    protected void UpdateTextBlock(TextBlock tb, double value, string format,
        double warningThreshold = double.MaxValue, double criticalThreshold = double.MaxValue)
    {
        if (tb.Visibility != Visibility.Visible) return;

        string text;
        Brush foreground = _valueBrush;

        if (double.IsNaN(value) || value < 0)
        {
            text = "-";
        }
        else
        {
            _stringBuilder.Clear();
            _stringBuilder.AppendFormat(format, value);
            text = _stringBuilder.ToString();

            if (warningThreshold != double.MaxValue)
                foreground = SeverityBrush(value, warningThreshold, criticalThreshold);
        }

        SetTextIfChanged(tb, text);
        SetForegroundIfChanged(tb, foreground);
    }

    protected void UpdateTextBlock(TextBlock tb, int value, string? suffix = null)
    {
        if (suffix == null)
            suffix = $" {Resource.RPM}";
        if (tb.Visibility != Visibility.Visible) return;
        SetTextIfChanged(tb, value < 0 ? "-" : $"{value}{suffix}");
        SetForegroundIfChanged(tb, _valueBrush);
    }

    protected Brush SeverityBrush(double value, double warningThreshold, double criticalThreshold)
    {
        if (value >= criticalThreshold) return _criticalBrush;
        return value >= warningThreshold ? _warningBrush : _valueBrush;
    }

    protected string GetMemoryDisplayText(double usage, double used, double total)
    {
        if (_hardwareSensorSettings.Store.DisplayMemoryInGigabytes)
        {
            if (used >= 0 && total > 0) return $"{used:F1}/{total:F1} {Resource.GB}";
            if (used >= 0) return $"{used:F1} {Resource.GB}";
            return "-";
        }

        return usage >= 0 ? $"{usage:F0}{Resource.Percent}" : "-";
    }

    protected string GetMemoryDisplayText(SensorSnapshot data) => GetMemoryDisplayText(data.MemUsage, data.MemUsed, data.MemTotal);

    protected string GetGpuVramDisplayText(SensorSnapshot data) => GetMemoryDisplayText(data.GpuVramUsage, data.GpuVramUsed, data.GpuVramTotal);

    protected string GetTemperatureFormat(double rawCelsius)
    {
        if (double.IsNaN(rawCelsius) || rawCelsius < 0) return "-";

        if (_applicationSettings.Store.TemperatureUnit == TemperatureUnit.F)
        {
            var fahrenheit = rawCelsius * 9.0 / 5.0 + 32.0;
            return $"{fahrenheit:F0}{Resource.Fahrenheit}";
        }

        return $"{rawCelsius:F0}{Resource.Celsius}";
    }

    protected void UpdateTemperatureTextBlock(TextBlock tb, double rawCelsius,
        double warningThreshold = double.MaxValue, double criticalThreshold = double.MaxValue)
    {
        if (tb.Visibility != Visibility.Visible) return;

        var text = GetTemperatureFormat(rawCelsius);
        var foreground = _valueBrush;

        if (warningThreshold != double.MaxValue && !double.IsNaN(rawCelsius) && rawCelsius >= 0)
            foreground = SeverityBrush(rawCelsius, warningThreshold, criticalThreshold);

        SetTextIfChanged(tb, text);
        SetForegroundIfChanged(tb, foreground);
    }

    protected static void SetTextIfChanged(TextBlock tb, string text)
    {
        if (!string.Equals(tb.Text, text, StringComparison.Ordinal))
            tb.Text = text;
    }

    protected static void SetForegroundIfChanged(TextBlock tb, Brush brush)
    {
        if (!ReferenceEquals(tb.Foreground, brush))
            tb.Foreground = brush;
    }

    #endregion

    #region FPS Monitoring

    private async void CheckAndUpdateFpsMonitoring()
    {
        bool shouldMonitor = IsVisible && ShouldMonitorFps();

        switch (shouldMonitor)
        {
            case true when !_fpsMonitoringStarted:
                _fpsMonitoringStarted = true;
                await StartFpsMonitoringAsync();
                break;
            case false when _fpsMonitoringStarted:
                _fpsMonitoringStarted = false;
                StopFpsMonitoring();
                break;
        }
    }

    private bool ShouldMonitorFps() =>
        _activeItems.Contains(OsdItem.Fps) ||
        _activeItems.Contains(OsdItem.LowFps) ||
        _activeItems.Contains(OsdItem.FrameTime);

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
            var intervalTicks = TimeSpan.FromSeconds(_OsdSettings.Store.OsdRefreshInterval).Ticks;
            if (elapsedTicks < intervalTicks) return;

            _lastFpsUiUpdateTick = currentTick;
            _lastValidFpsTick = currentTick;

            const string dash = "-";

            fpsText = fpsVal.ToString();
            fpsBrush = (fpsVal < _OsdSettings.Store.FpsThresholdCritical) ? _criticalBrush : _valueBrush;

            lowText = (lowVal > 0) ? lowVal.ToString() : dash;
            lowBrush = (lowVal > 0 && (fpsVal - lowVal) >= _OsdSettings.Store.LowFpsDeltaThreshold) ? _criticalBrush : _valueBrush;

            if (ftVal > 0.1)
            {
                ftText = $"{ftVal,5:F1}ms";
                ftBrush = (ftVal > MAX_FRAME_TIME_MS) ? _criticalBrush : _valueBrush;
            }
            else
            {
                ftText = dash;
                ftBrush = _valueBrush;
            }
        }
        else
        {
            if (currentTick - _lastValidFpsTick > FRAMETIME_TIMEOUT_TICKS)
            {
                const string dash = "-";
                fpsText = dash; fpsBrush = _valueBrush;
                lowText = dash; lowBrush = _valueBrush;
                ftText = dash; ftBrush = _valueBrush;
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

    protected abstract void UpdateFpsDisplay(FpsDisplayData data);

    #endregion

    #region Main Loop & Data Refresh

    private async Task TheRing(CancellationToken token)
    {
        try
        {
            await _refreshLock.WaitAsync(-1, token);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        try
        {
            while (!token.IsCancellationRequested)
            {
                var loopStart = DateTime.Now;
                try
                {
                    await RefreshSensorsDataAsync(token);
                }
                catch (OperationCanceledException) { break; }
                catch (Exception ex)
                {
                    Log.Instance.Trace($"Exception occurred when executing TheRing()", ex);
                    await Task.Delay(1000, token);
                }

                var elapsed = DateTime.Now - loopStart;
                var delay = TimeSpan.FromSeconds(_OsdSettings.Store.OsdRefreshInterval) - elapsed;
                if (delay > TimeSpan.Zero)
                {
                    await Task.Delay(delay, token);
                }
            }
        }
        finally
        {
            try { _refreshLock.Release(); } catch (ObjectDisposedException) { }
        }
    }

    private async Task RefreshSensorsDataAsync(CancellationToken token)
    {
        var gs = _sensorsGroupControllers.Snapshot;

        SensorsData? mainData = null;
        if (_hasLenovoController)
        {
            mainData = await _controller.GetDataAsync();
        }

        if (token.IsCancellationRequested) return;

        if (_uiUpdateThrottleMs > 0 && (DateTime.Now - _lastUpdate).TotalMilliseconds < _uiUpdateThrottleMs) return;

        _lastUpdate = DateTime.Now;

        var snapshot = new SensorSnapshot
        {
            CpuUsage = gs.CpuUsage,
            CpuFrequency = _sensorsGroupControllers.ShowAverageCpuFrequency ? gs.CpuAvgClock : gs.CpuMaxClock,
            CpuPClock = _sensorsGroupControllers.ShowAverageCpuFrequency ? gs.CpuPAvgClock : gs.CpuPClock,
            CpuEClock = _sensorsGroupControllers.ShowAverageCpuFrequency ? gs.CpuEAvgClock : gs.CpuEClock,
            CpuTemp = gs.CpuTemp,
            CpuPower = gs.CpuPower,
            CpuFanSpeed = mainData?.CPU.FanSpeed ?? -1,

            GpuUsage = gs.GpuUsage,
            GpuFrequency = gs.GpuClock,
            GpuTemp = gs.GpuTemp,
            GpuVramUsage = gs.GpuVramUtilization,
            GpuVramUsed = gs.GpuVramUsed,
            GpuVramTotal = gs.GpuVramTotal,
            GpuVramTemp = gs.GpuVramTemp,
            GpuPower = gs.GpuPower,
            GpuFanSpeed = mainData?.GPU.FanSpeed ?? -1,

            MemUsage = gs.MemUsage,
            MemUsed = gs.MemUsed,
            MemTotal = gs.MemTotal,
            MemTemp = (float)gs.MemMaxTemp,

            PchTemp = mainData?.PCH.Temperature ?? -1,
            PchFanSpeed = mainData?.PCH.FanSpeed ?? -1,

            Disk1Temp = gs.SsdTemps.Item1,
            Disk2Temp = gs.SsdTemps.Item2
        };

        await Dispatcher.BeginInvoke(() => UpdateSensorData(snapshot), DispatcherPriority.Normal);
    }

    protected abstract void UpdateSensorData(SensorSnapshot data);

    #endregion
}
