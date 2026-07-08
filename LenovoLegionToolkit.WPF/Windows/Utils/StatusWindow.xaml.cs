using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Forms;
using System.Windows.Media;
using LenovoLegionToolkit.Lib;
using LenovoLegionToolkit.Lib.Controllers;
using LenovoLegionToolkit.Lib.Controllers.GodMode;
using LenovoLegionToolkit.Lib.Controllers.Sensors;
using LenovoLegionToolkit.Lib.Extensions;
using LenovoLegionToolkit.Lib.Features;
using LenovoLegionToolkit.Lib.Settings;
using LenovoLegionToolkit.Lib.System;
using LenovoLegionToolkit.Lib.Utils;
using LenovoLegionToolkit.WPF.Extensions;
using LenovoLegionToolkit.WPF.Resources;
using LenovoLegionToolkit.WPF.Settings;
using Wpf.Ui.Appearance;
using Wpf.Ui.Common;
using System.Windows.Threading;
using Label = System.Windows.Controls.Label;

namespace LenovoLegionToolkit.WPF.Windows.Utils;

public partial class StatusWindow
{
    private readonly ApplicationSettings _settings = IoCContainer.Resolve<ApplicationSettings>();
    private readonly DashboardSettings _dashboardSettings = IoCContainer.Resolve<DashboardSettings>();
    private readonly SensorsControlSettings _sensorsControlSettings = IoCContainer.Resolve<SensorsControlSettings>();
    private readonly SemaphoreSlim _refreshLock = new(1, 1);

    private readonly CancellationTokenSource _cancellationTokenSource = new();

    private DateTime _lastUpdate = DateTime.MinValue;
    private const int UI_UPDATE_THROTTLE_MS = 100;
    private const int MAX_RETRY_COUNT = 3;
    private const int RETRY_DELAY_MS = 1000;

    private readonly PowerModeFeature _powerModeFeature = IoCContainer.Resolve<PowerModeFeature>();
    private readonly ITSModeFeature _itsModeFeature = IoCContainer.Resolve<ITSModeFeature>();
    private readonly GodModeController _godModeController = IoCContainer.Resolve<GodModeController>();
    private readonly GPUController _gpuController = IoCContainer.Resolve<GPUController>();
    private readonly BatteryFeature _batteryFeature = IoCContainer.Resolve<BatteryFeature>();
    private readonly UpdateChecker _updateChecker = IoCContainer.Resolve<UpdateChecker>();
    private readonly UpdateSettings _updateSettings = IoCContainer.Resolve<UpdateSettings>();
    private readonly SensorsController _sensorsController = IoCContainer.Resolve<SensorsController>();
    private readonly SensorsGroupController _sensorsGroupController = IoCContainer.Resolve<SensorsGroupController>();
    private readonly HardwareSensorSettings _hardwareSensorSettings = IoCContainer.Resolve<HardwareSensorSettings>();

    private MachineInformation? _machineInfo;
    private Type? _cachedControllerType;
    private bool _isSpecialModel;

    private bool IsSpecialModel => _isSpecialModel;

    private void SetMachineInformation(MachineInformation info)
    {
        _machineInfo = info;
        _isSpecialModel = info.LegionSeries is > LegionSeries.Legion_9 and not LegionSeries.Unknown;
    }

    private readonly struct StatusWindowData(
        PowerModeState? powerModeState,
        ITSMode? itsMode,
        string? godModePresetName,
        GPUStatus? gpuStatus,
        BatteryInformation? batteryInformation,
        BatteryState? batteryState,
        bool hasUpdate,
        SensorsData? sensorData = null,
        double cpuPower = -1,
        double gpuPower = -1,
        double cpuClock = -1,
        double cpuTemp = -1,
        double gpuClock = -1,
        double gpuTemp = -1)
    {
        public PowerModeState? PowerModeState { get; } = powerModeState;
        public ITSMode? ITSMode { get; } = itsMode;
        public string? GodModePresetName { get; } = godModePresetName;
        public GPUStatus? GPUStatus { get; } = gpuStatus;
        public BatteryInformation? BatteryInformation { get; } = batteryInformation;
        public BatteryState? BatteryState { get; } = batteryState;
        public bool HasUpdate { get; } = hasUpdate;
        public SensorsData? SensorsData { get; } = sensorData;
        public double CpuPower { get; } = cpuPower;
        public double GpuPower { get; } = gpuPower;
        public double CpuClock { get; } = cpuClock;
        public double CpuTemp { get; } = cpuTemp;
        public double GpuClock { get; } = gpuClock;
        public double GpuTemp { get; } = gpuTemp;
    }

    public static async Task<StatusWindow> CreateAsync()
    {
        var window = new StatusWindow();
        await window.InitializeAsync();
        return window;
    }

    internal async Task InitializeAsync()
    {
        if (!_machineInfo.HasValue)
        {
            SetMachineInformation(await Compatibility.GetMachineInformationAsync().ConfigureAwait(false));
        }

        // Populate model + BIOS + Serial + MachineType labels
        try
        {
            var mi = await Compatibility.GetMachineInformationAsync().ConfigureAwait(false);
            var model = mi.Model;
            var biosVersion = mi.BiosVersionRaw;
            var serialNumber = mi.SerialNumber;
            var machineType = mi.MachineType;

            if (Compatibility.FakeMachineInformationMode)
            {
                var fakeMi = await Compatibility.GetFakeMachineInformationAsync().ConfigureAwait(false);
                if (fakeMi.HasValue)
                {
                    model = fakeMi.Value.Model ?? model;
                    biosVersion = fakeMi.Value.BiosVersion ?? biosVersion;
                    serialNumber = fakeMi.Value.SerialNumber ?? serialNumber;
                    machineType = fakeMi.Value.MachineType ?? machineType;
                }
            }

            Dispatcher.Invoke(() =>
            {
                _deviceModelLabel.Content = model;
                _biosVersionLabel.Content = biosVersion;
                _serialNumberLabel.Content = serialNumber;
                _machineTypeLabel.Content = machineType;
                _osVersionLabel.Content = Environment.OSVersion.ToString();
            });
        }
        catch { /* Non-critical — labels remain empty */ }

        var token = _cancellationTokenSource.Token;
        try
        {
            var initialData = await GetStatusWindowDataAsync(token, skipRetry: true);
            await Dispatcher.InvokeAsync(() => ApplyDataToUI(initialData));
        }
        catch { }
    }

    /// <summary>
    /// Shows the StatusWindow near the current mouse cursor and auto-closes after <paramref name="durationMs"/> milliseconds.
    /// </summary>
    public void ShowNearCursor(int durationMs = 4000)
    {
        MoveBottomRightEdgeOfWindowToMousePosition();
        Show();

        var timer = new System.Windows.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(durationMs)
        };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            if (IsVisible)
                Close();
        };
        timer.Start();
    }

    public void ShowCentered()
    {
        _actionButtons.Visibility = Visibility.Visible;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Show();
        this.Activate();
        this.Focus();
        this.Topmost = true;
        this.Topmost = false;
    }

    private void CopyButton_Click(object sender, RoutedEventArgs e)
    {
        var text = $"Device Model: {_deviceModelLabel.Content}\n" +
                   $"BIOS Version: {_biosVersionLabel.Content}\n" +
                   $"Serial Number: {_serialNumberLabel.Content}\n" +
                   $"Machine Type: {_machineTypeLabel.Content}\n" +
                   $"OS Version: {_osVersionLabel.Content}";
        try
        {
            System.Windows.Clipboard.SetText(text);
        }
        catch { }
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        if (IsVisible)
            Close();
    }

    private void TopBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left)
            DragMove();
    }

    public void Highlight()
    {
        this.Activate();
        this.Focus();
        this.Topmost = true;
        this.Topmost = false;
        
        var animation = new System.Windows.Media.Animation.DoubleAnimation
        {
            From = 0.5,
            To = 1.0,
            Duration = new Duration(TimeSpan.FromMilliseconds(300)),
            AutoReverse = false
        };
        BeginAnimation(OpacityProperty, animation);
    }

    public StatusWindow()
    {
        InitializeComponent();
        Loaded += StatusWindow_Loaded;
        IsVisibleChanged += StatusWindow_IsVisibleChanged;
        WindowStyle = WindowStyle.None;
        WindowStartupLocation = WindowStartupLocation.Manual;
        ResizeMode = ResizeMode.NoResize;
        SizeToContent = SizeToContent.WidthAndHeight;
        Focusable = false;
        ExtendsContentIntoTitleBar = true;
        ShowInTaskbar = false;
        ShowActivated = false;

#if DEBUG
        _title.Text += " [DEBUG]";
#else
        var version = System.Reflection.Assembly.GetEntryAssembly()?.GetName().Version;
        if (version == new Version(0, 0, 1, 0) || version?.Build == 99) _title.Text += " [BETA]";
#endif
        if (Log.Instance.IsTraceEnabled) _title.Text += " [LOGGING ENABLED]";
    }

    private void UpdateUiLayout(GPUStatus? gpuStatus)
    {
        if (!_machineInfo.HasValue) return;

        var useSensors = _settings.Store.EnableHardwareSensors;
        var sensorVis = useSensors ? Visibility.Visible : Visibility.Collapsed;

        _cpuGrid.Visibility = sensorVis;
        _cpuFreqAndTempDesc.Visibility = sensorVis;
        _cpuFreqAndTempLabel.Visibility = sensorVis;
        _cpuFanAndPowerDesc.Visibility = sensorVis;
        _cpuFanAndPowerLabel.Visibility = sensorVis;

        var isV5 = _cachedControllerType == typeof(SensorsControllerV5);
        _systemFanGrid.Visibility = (useSensors && isV5 && !IsSpecialModel) ? Visibility.Visible : Visibility.Collapsed;

        if (gpuStatus.HasValue)
        {
            _gpuGrid.Visibility = Visibility.Visible;
            var isGpuOn = gpuStatus.Value.State != GPUState.PoweredOff;
            var gpuDetailsVis = isGpuOn ? Visibility.Visible : Visibility.Collapsed;
            var gpuSensorVis = (useSensors && isGpuOn) ? Visibility.Visible : Visibility.Collapsed;

            _gpuPowerStateValue.Visibility = gpuDetailsVis;
            _gpuPowerStateValueLabel.Visibility = gpuDetailsVis;
            _gpuFreqAndTempDesc.Visibility = gpuSensorVis;
            _gpuFreqAndTempLabel.Visibility = gpuSensorVis;
            _gpuFanAndPowerDesc.Visibility = gpuSensorVis;
            _gpuFanAndPowerLabel.Visibility = gpuSensorVis;
        }
        else
        {
            _gpuGrid.Visibility = Visibility.Collapsed;
        }
    }

    private async void StatusWindow_Loaded(object sender, RoutedEventArgs e)
    {
        MoveBottomRightEdgeOfWindowToMousePosition();
        if (!_machineInfo.HasValue)
        {
            SetMachineInformation(await Compatibility.GetMachineInformationAsync().ConfigureAwait(false));
        }
    }

    private async void StatusWindow_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (IsVisible)
        {
            var refreshInterval = _settings.Store.UseNewSensorDashboard
                ? _sensorsControlSettings.Store.SensorsRefreshIntervalSeconds
                : _dashboardSettings.Store.SensorsRefreshIntervalSeconds;

            _sensorsGroupController.Start(this, TimeSpan.FromSeconds(refreshInterval));

            var token = _cancellationTokenSource.Token;
            while (!token.IsCancellationRequested)
            {
                try
                {
                    var data = await GetStatusWindowDataAsync(token);
                    if (token.IsCancellationRequested) break;

                    await Dispatcher.InvokeAsync(() => ApplyDataToUI(data), DispatcherPriority.Normal, token);
                }
                catch (Exception ex)
                {
                    Log.Instance.Trace($"StatusWindow update failed: {ex}");
                }
                await Task.Delay(TimeSpan.FromSeconds(1), token).ConfigureAwait(false);
            }
        }
        else
        {
            _sensorsGroupController.Stop(this);
            _cancellationTokenSource.Cancel();
        }
    }

    private async void OnSensorsUpdated(HardwareSensorSnapshot snapshot)
    {
        var token = _cancellationTokenSource.Token;
        try
        {
            var data = await GetStatusWindowDataAsync(token, snapshot: snapshot);
            if (token.IsCancellationRequested) return;

            if ((DateTime.Now - _lastUpdate).TotalMilliseconds >= UI_UPDATE_THROTTLE_MS)
            {
                _lastUpdate = DateTime.Now;
                await Dispatcher.InvokeAsync(() => ApplyDataToUI(data), DispatcherPriority.Normal, token);
            }
        }
        catch (Exception ex)
        {
            Log.Instance.Trace($"StatusWindow update failed: {ex}");
        }
    }

    private async Task<StatusWindowData> GetStatusWindowDataAsync(CancellationToken token, bool skipRetry = false, HardwareSensorSnapshot? snapshot = null)
    {
        if (!_machineInfo.HasValue)
        {
            SetMachineInformation(await Compatibility.GetMachineInformationAsync().ConfigureAwait(false));
        }

        PowerModeState? state = null;
        ITSMode? mode = null;
        string? godModePresetName = null;
        GPUStatus? gpuStatus = null;
        BatteryInformation? batteryInfo = null;
        BatteryState? batteryState = null;
        bool hasUpdate = false;
        SensorsData? sensorsData = null;
        double cpuPower = -1, gpuPower = -1, cpuClock = -1, cpuTemp = -1, gpuClock = -1, gpuTemp = -1;

        var tasks = new List<Task>();

        tasks.Add(Task.Run(async () => {
            try
            {
                if (await _powerModeFeature.IsSupportedAsync().WaitAsync(token))
                {
                    state = await _powerModeFeature.GetStateAsync().WaitAsync(token);
                    if (state == PowerModeState.GodMode) godModePresetName = await _godModeController.GetActivePresetNameAsync().WaitAsync(token);
                }
                else if (await _itsModeFeature.IsSupportedAsync().WaitAsync(token))
                {
                    mode = await _itsModeFeature.GetStateAsync().WaitAsync(token);
                }
            }
            catch { /* Ignore */ }
        }, token));

        tasks.Add(Task.Run(async () => { try { if (_gpuController.IsSupported()) gpuStatus = await _gpuController.GetLastKnownStatusAsync().WaitAsync(token); } catch { } }, token));
        tasks.Add(Task.Run(() => { try { batteryInfo = Battery.GetBatteryInformation(); } catch { } }, token));
        tasks.Add(Task.Run(async () => { try { if (await _batteryFeature.IsSupportedAsync().WaitAsync(token)) batteryState = await _batteryFeature.GetStateAsync().WaitAsync(token); } catch { } }, token));
        
        hasUpdate = _updateChecker.UpdateFromServer is not null;

        await Task.WhenAll(tasks).ConfigureAwait(false);

        if (_settings.Store.EnableHardwareSensors)
        {
            try
            {
                if (!IsSpecialModel && await _sensorsController.IsSupportedAsync().WaitAsync(token))
                {
                    var controller = await _sensorsController.GetControllerAsync().WaitAsync(token);
                    _cachedControllerType = controller?.GetType();

                    sensorsData = await _sensorsController.GetDataAsync().WaitAsync(token);
                }

                if (_sensorsGroupController.IsLibreHardwareMonitorInitialized())
                {
                    var gs = snapshot ?? _sensorsGroupController.Snapshot;
                    cpuPower = gs.CpuPower;
                    gpuPower = gs.GpuPower;
                    cpuTemp = gs.CpuTemp;
                    gpuClock = gs.GpuClock;
                    gpuTemp = gs.GpuTemp;

                    if (_sensorsGroupController.IsHybrid)
                    {
                        cpuClock = _hardwareSensorSettings.Store.ShowCpuAverageFrequency
                            ? gs.CpuPAvgClock
                            : gs.CpuPClock;
                    }
                    else
                    {
                        cpuClock = _hardwareSensorSettings.Store.ShowCpuAverageFrequency
                            ? gs.CpuAvgClock
                            : gs.CpuMaxClock;
                    }
                }
            }
            catch { /* Ignore */ }
        }

        return new(state, mode, godModePresetName, gpuStatus, batteryInfo, batteryState, hasUpdate, sensorsData, cpuPower, gpuPower, cpuClock, cpuTemp, gpuClock, gpuTemp);
    }

    private void ApplyDataToUI(StatusWindowData data)
    {
        UpdateUiLayout(data.GPUStatus);
        RefreshPowerMode(data.PowerModeState, data.ITSMode, data.GodModePresetName);

        var useSensors = _settings.Store.EnableHardwareSensors;

        if (useSensors)
        {
            ApplySensorsData(data);
        }

        if (data.GPUStatus.HasValue)
        {
            _gpuPowerStateValueLabel.Content = data.GPUStatus.Value.PerformanceState ?? "-";
        }

        RefreshBattery(data.BatteryInformation, data.BatteryState);
        RefreshUpdate(data.HasUpdate);

        _loadingRing.Visibility = Visibility.Collapsed;
        _loadingOverlay.Visibility = Visibility.Collapsed;
    }

    private void ApplySensorsData(StatusWindowData data)
    {

        _cpuFreqAndTempDesc.Content = Resource.StatusWindow_Frequency_And_Temperature;
        UpdateFreqAndTemp(_cpuFreqAndTempLabel, data.CpuClock, data.CpuTemp);

        if (IsSpecialModel)
        {
            _cpuFanAndPowerDesc.Content = Resource.SensorsControl_CPU_Power;
            UpdatePowerOnly(_cpuFanAndPowerLabel, data.CpuPower);
        }
        else
        {
            _cpuFanAndPowerDesc.Content = Resource.StatusWindow_Fan_And_Power;
            UpdateFanAndPower(_cpuFanAndPowerLabel, data.SensorsData?.CPU.FanSpeed ?? -1, data.CpuPower);
        }

        if (_cachedControllerType == typeof(SensorsControllerV4) || _cachedControllerType == typeof(SensorsControllerV5))
        {
            UpdateSystemFan(_systemFanLabel, data.SensorsData?.PCH.FanSpeed ?? -1);
        }

        if (data.GPUStatus.HasValue && data.GPUStatus.Value.State != GPUState.PoweredOff)
        {
            _gpuFreqAndTempDesc.Content = Resource.StatusWindow_Frequency_And_Temperature;
            UpdateFreqAndTemp(_gpuFreqAndTempLabel, data.GpuClock, data.GpuTemp);

            if (IsSpecialModel)
            {
                _gpuFanAndPowerDesc.Content = Resource.SensorsControl_GPU_Power;
                UpdatePowerOnly(_gpuFanAndPowerLabel, data.GpuPower);
            }
            else
            {
                _gpuFanAndPowerDesc.Content = Resource.StatusWindow_Fan_And_Power;
                UpdateFanAndPower(_gpuFanAndPowerLabel, data.SensorsData?.GPU.FanSpeed ?? -1, data.GpuPower);
            }
        }
    }

    private void RefreshPowerMode(PowerModeState? powerModeState, ITSMode? itsMode, string? godModePresetName)
    {
        if (powerModeState != null)
        {
            _powerModeValueLabel.Content = powerModeState.GetDisplayName();
            _powerModeValueIndicator.Fill = powerModeState?.GetSolidColorBrush() ?? new SolidColorBrush(Colors.Transparent);
            var isGod = powerModeState == PowerModeState.GodMode;
            _powerModePresetLabel.Visibility = isGod ? Visibility.Visible : Visibility.Collapsed;
            _powerModePresetValueLabel.Visibility = isGod ? Visibility.Visible : Visibility.Collapsed;
            _powerModePresetValueLabel.Content = godModePresetName ?? "-";
        }
        else
        {
            _powerModeValueLabel.Content = itsMode?.GetDisplayName() ?? "-";
            _powerModeValueIndicator.Fill = itsMode?.GetSolidColorBrush() ?? Brushes.Transparent;
            _powerModePresetLabel.Visibility = _powerModePresetValueLabel.Visibility = Visibility.Collapsed;
        }
    }

    private void RefreshBattery(BatteryInformation? batteryInfo, BatteryState? batteryState)
    {
        if (!batteryInfo.HasValue || !batteryState.HasValue) { _batteryIcon.Symbol = SymbolRegular.Battery024; _batteryValueLabel.Content = "-"; return; }
        var info = batteryInfo.Value;
        _batteryIcon.Symbol = (int)Math.Round(info.BatteryPercentage / 10.0) switch { 10 => SymbolRegular.Battery1024, 9 => SymbolRegular.Battery924, 8 => SymbolRegular.Battery824, 7 => SymbolRegular.Battery724, 6 => SymbolRegular.Battery624, 5 => SymbolRegular.Battery524, 4 => SymbolRegular.Battery424, 3 => SymbolRegular.Battery324, 2 => SymbolRegular.Battery224, 1 => SymbolRegular.Battery124, _ => SymbolRegular.Battery024 };
        if (info.IsCharging) _batteryIcon.Symbol = batteryState == BatteryState.Conservation ? SymbolRegular.BatterySaver24 : SymbolRegular.BatteryCharge24;
        if (info.IsLowBattery) _batteryValueLabel.SetResourceReference(ForegroundProperty, "SystemFillColorCautionBrush"); else _batteryValueLabel.ClearValue(ForegroundProperty);
        _batteryValueLabel.Content = $"{info.BatteryPercentage}{Resource.Percent}";
        _batteryModeValueLabel.Content = batteryState.GetDisplayName();
        
        _batteryDischargeLabel.Content = info.IsCharging ? Resource.StatusTrayPopup_ChargeRate : Resource.StatusTrayPopup_DischargeRate;
        _batteryDischargeValueLabel.Content = $"{(Math.Abs(info.DischargeRate) / 1000.0):0.00} {Resource.Watt}";
        
        _batteryMinDischargeValueLabel.Content = $"{info.MinDischargeRate / 1000.0:+0.00;-0.00;0.00} {Resource.Watt}";
        _batteryMaxDischargeValueLabel.Content = $"{info.MaxDischargeRate / 1000.0:+0.00;-0.00;0.00} {Resource.Watt}";
    }

    private void RefreshUpdate(bool hasUpdate) => _updateIndicator.Visibility = hasUpdate ? Visibility.Visible : Visibility.Collapsed;
    private string GetTemperatureText(double t) => (double.IsNaN(t) || t < 0) ? "-" : (_settings.Store.TemperatureUnit == TemperatureUnit.F ? $"{(t * 9 / 5 + 32):0}{Resource.Fahrenheit}" : $"{t:0}{Resource.Celsius}");
    private void UpdateFreqAndTemp(Label l, double f, double t) => l.Content = (t < 0 || f < 0) ? "-" : $"{f:0}{Resource.MHz} | {GetTemperatureText(t)}";
    private static void UpdateFanAndPower(Label l, double f, double p) => l.Content = (f < 0 || p < 0) ? "-" : $"{f:0}{Resource.RPM} | {p:0}{Resource.Watt}";
    private static void UpdatePowerOnly(Label l, double p) => l.Content = p < 0 ? "-" : $"{p:0}{Resource.Watt}";
    private static void UpdateSystemFan(Label l, double f) => l.Content = f < 0 ? "-" : $"{f:0}{Resource.RPM}";

    private void MoveBottomRightEdgeOfWindowToMousePosition()
    {
        UpdateLayout();

        var source = PresentationSource.FromVisual(this);
        if (source?.CompositionTarget == null)
        {
            Left = 0;
            Top = 0;
            return;
        }

        var matrix = source.CompositionTarget.TransformFromDevice;

        var mousePoint = Control.MousePosition;

        var screen = Screen.FromPoint(mousePoint);
        var workingArea = screen.WorkingArea;

        var mouseLogical = matrix.Transform(new Point(mousePoint.X, mousePoint.Y));
        var screenLeftTop = matrix.Transform(new Point(workingArea.Left, workingArea.Top));
        var screenRightBottom = matrix.Transform(new Point(workingArea.Right, workingArea.Bottom));

        const double offset = 8;

        if (mouseLogical.X + offset + ActualWidth > screenRightBottom.X)
            Left = mouseLogical.X - ActualWidth - offset;
        else
            Left = mouseLogical.X + offset;

        if (mouseLogical.Y + offset + ActualHeight > screenRightBottom.Y)
            Top = mouseLogical.Y - ActualHeight - offset;
        else
            Top = mouseLogical.Y + offset;

        if (Left < screenLeftTop.X) Left = screenLeftTop.X;
        if (Top < screenLeftTop.Y) Top = screenLeftTop.Y;
    }
}