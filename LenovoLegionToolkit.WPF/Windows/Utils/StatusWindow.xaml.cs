using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
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
using Wpf.Ui.Appearance;
using Wpf.Ui.Common;

namespace LenovoLegionToolkit.WPF.Windows.Utils;

public partial class StatusWindow
{
    private readonly ApplicationSettings _settings = IoCContainer.Resolve<ApplicationSettings>();
    private readonly SemaphoreSlim _refreshLock = new(1, 1);
    private readonly CancellationTokenSource _cancellationTokenSource = new();

    private DateTime _lastUpdate = DateTime.MinValue;
    private const int UI_UPDATE_THROTTLE_MS = 100;
    private const int MAX_RETRY_COUNT = 3;
    private const int RETRY_DELAY_MS = 1000;
    private int _currentRetryCount;

    private readonly PowerModeFeature _powerModeFeature = IoCContainer.Resolve<PowerModeFeature>();
    private readonly ITSModeFeature _itsModeFeature = IoCContainer.Resolve<ITSModeFeature>();
    private readonly GodModeController _godModeController = IoCContainer.Resolve<GodModeController>();
    private readonly GPUController _gpuController = IoCContainer.Resolve<GPUController>();
    private readonly BatteryFeature _batteryFeature = IoCContainer.Resolve<BatteryFeature>();
    private readonly UpdateChecker _updateChecker = IoCContainer.Resolve<UpdateChecker>();
    private readonly UpdateCheckSettings _updateCheckerSettings = IoCContainer.Resolve<UpdateCheckSettings>();
    private readonly SensorsController _sensorsController = IoCContainer.Resolve<SensorsController>();
    private readonly SensorsGroupController _sensorsGroupController = IoCContainer.Resolve<SensorsGroupController>();

    private MachineInformation? _machineInfo;
    private Type? _cachedControllerType;

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
        double gpuPower = -1)
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
    }

    public static async Task<StatusWindow> CreateAsync()
    {
        var window = new StatusWindow();
        await window.InitializeAsync();
        return window;
    }

    private async Task InitializeAsync()
    {
        _machineInfo ??= await Compatibility.GetMachineInformationAsync().ConfigureAwait(false);
        var token = _cancellationTokenSource.Token;
        try
        {
            var initialData = await GetStatusWindowDataAsync(token, skipRetry: true);
            ApplyDataToUI(initialData);
        }
        catch { }
    }

    public StatusWindow()
    {
        InitializeComponent();
        Loaded += StatusWindow_Loaded;
        IsVisibleChanged += StatusWindow_IsVisibleChanged;
        WindowStyle = WindowStyle.None;
        WindowStartupLocation = WindowStartupLocation.Manual;
        WindowBackdropType = BackgroundType.None;
        ResizeMode = ResizeMode.NoResize;
        SizeToContent = SizeToContent.WidthAndHeight;
        Focusable = false;
        Topmost = true;
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

        var useSensors = _settings.Store.UseNewSensorDashboard;
        var sensorVis = useSensors ? Visibility.Visible : Visibility.Collapsed;

        _cpuGrid.Visibility = sensorVis;
        _cpuFreqAndTempDesc.Visibility = sensorVis;
        _cpuFreqAndTempLabel.Visibility = sensorVis;
        _cpuFanAndPowerDesc.Visibility = sensorVis;
        _cpuFanAndPowerLabel.Visibility = sensorVis;

        // Use cached controller type to avoid blocking UI thread
        var isV4V5 = _cachedControllerType == typeof(SensorsControllerV4) ||
                     _cachedControllerType == typeof(SensorsControllerV5);
        _systemFanGrid.Visibility = (useSensors && isV4V5) ? Visibility.Visible : Visibility.Collapsed;

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
        _machineInfo ??= await Compatibility.GetMachineInformationAsync().ConfigureAwait(false);

        var token = _cancellationTokenSource.Token;
        if (_settings.Store.UseNewSensorDashboard) _ = Task.Run(async () => await TheRing(token), token);
    }

    private void StatusWindow_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (!IsVisible) _cancellationTokenSource.Cancel();
    }

    private async Task<StatusWindowData> GetStatusWindowDataAsync(CancellationToken token, bool skipRetry = false)
    {
        _machineInfo ??= await Compatibility.GetMachineInformationAsync().ConfigureAwait(false);

        var tasks = new List<Task>();

        PowerModeState? state = null; ITSMode? mode = null; string? godModePresetName = null;
        GPUStatus? gpuStatus = null; BatteryInformation? batteryInfo = null; BatteryState? batteryState = null;
        bool hasUpdate = false; SensorsData? sensorsData = null; double cpuPower = -1; double gpuPower = -1;

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

        tasks.Add(Task.Run(async () => { try { if (_gpuController.IsSupported()) gpuStatus = await _gpuController.RefreshNowAsync().WaitAsync(token); } catch { } }, token));
        tasks.Add(Task.Run(() => { try { batteryInfo = Battery.GetBatteryInformation(); } catch { } }, token));
        tasks.Add(Task.Run(async () => { try { if (await _batteryFeature.IsSupportedAsync().WaitAsync(token)) batteryState = await _batteryFeature.GetStateAsync().WaitAsync(token); } catch { } }, token));
        tasks.Add(Task.Run(async () => { try { if (_updateCheckerSettings.Store.UpdateCheckFrequency != UpdateCheckFrequency.Never) hasUpdate = await _updateChecker.CheckAsync(false).WaitAsync(token) is not null; } catch { } }, token));

        await Task.WhenAll(tasks).ConfigureAwait(false);

        if (!_settings.Store.UseNewSensorDashboard) return new(state, mode, godModePresetName, gpuStatus, batteryInfo, batteryState, hasUpdate, sensorsData, cpuPower, gpuPower);

        try
        {
            if (await _sensorsController.IsSupportedAsync().WaitAsync(token))
            {
                // Cache the controller type for synchronous UI access
                var controller = await _sensorsController.GetControllerAsync().WaitAsync(token);
                _cachedControllerType = controller?.GetType();
                
                sensorsData = await _sensorsController.GetDataAsync().WaitAsync(token);
            }
            if (await _sensorsGroupController.IsSupportedAsync().WaitAsync(token) is LibreHardwareMonitorInitialState.Success or LibreHardwareMonitorInitialState.Initialized)
            {
                await _sensorsGroupController.UpdateAsync().WaitAsync(token);
                cpuPower = await _sensorsGroupController.GetCpuPowerAsync().WaitAsync(token);
                gpuPower = await _sensorsGroupController.GetGpuPowerAsync().WaitAsync(token);
                if (!skipRetry && (cpuPower <= 0 || (gpuStatus?.State == GPUState.Active && gpuPower <= 0)))
                {
                    for (int i = 0; i < MAX_RETRY_COUNT; i++)
                    {
                        await Task.Delay(RETRY_DELAY_MS, token);
                        await _sensorsGroupController.UpdateAsync().WaitAsync(token);
                        cpuPower = await _sensorsGroupController.GetCpuPowerAsync().WaitAsync(token);
                        gpuPower = await _sensorsGroupController.GetGpuPowerAsync().WaitAsync(token);
                        if (cpuPower > 0 && (gpuStatus?.State != GPUState.Active || gpuPower > 0)) break;
                    }
                }
            }
        }
        catch { /* Ignore */ }

        return new(state, mode, godModePresetName, gpuStatus, batteryInfo, batteryState, hasUpdate, sensorsData, cpuPower, gpuPower);
    }

    private async Task TheRing(CancellationToken token)
    {
        await _refreshLock.WaitAsync(token);
        try
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    var data = await GetStatusWindowDataAsync(token);
                    token.ThrowIfCancellationRequested();
                    if ((DateTime.Now - _lastUpdate).TotalMilliseconds >= UI_UPDATE_THROTTLE_MS)
                    {
                        _lastUpdate = DateTime.Now;
                        await Dispatcher.InvokeAsync(() => ApplyDataToUI(data), System.Windows.Threading.DispatcherPriority.Normal, token);
                    }
                    _currentRetryCount = 0;
                    await Task.Delay(TimeSpan.FromSeconds(_settings.Store.FloatingGadgetsRefreshInterval), token);
                }
                catch (OperationCanceledException) { break; }
                catch { if (++_currentRetryCount < MAX_RETRY_COUNT) { await Task.Delay(RETRY_DELAY_MS, token); continue; } await Task.Delay(TimeSpan.FromSeconds(_settings.Store.FloatingGadgetsRefreshInterval), token); }
            }
        }
        finally { _refreshLock.Release(); }
    }

    private void ApplyDataToUI(StatusWindowData data)
    {
        UpdateUiLayout(data.GPUStatus);
        RefreshPowerMode(data.PowerModeState, data.ITSMode, data.GodModePresetName);

        var useSensors = _settings.Store.UseNewSensorDashboard;

        if (useSensors)
        {
            UpdateFreqAndTemp(_cpuFreqAndTempLabel, data.SensorsData?.CPU.CoreClock ?? -1, data.SensorsData?.CPU.Temperature ?? -1);
            UpdateFanAndPower(_cpuFanAndPowerLabel, data.SensorsData?.CPU.FanSpeed ?? -1, data.CpuPower);
            // Use cached controller type to avoid blocking UI thread
            if (_cachedControllerType == typeof(SensorsControllerV4) ||
                _cachedControllerType == typeof(SensorsControllerV5))
            {
                UpdateSystemFan(_systemFanLabel, data.SensorsData?.PCH.FanSpeed ?? -1);
            }
        }

        if (data.GPUStatus.HasValue)
        {
            _gpuPowerStateValueLabel.Content = data.GPUStatus.Value.PerformanceState ?? "-";
            if (useSensors && data.GPUStatus.Value.State != GPUState.PoweredOff)
            {
                UpdateFreqAndTemp(_gpuFreqAndTempLabel, data.SensorsData?.GPU.CoreClock ?? -1, data.SensorsData?.GPU.Temperature ?? -1);
                UpdateFanAndPower(_gpuFanAndPowerLabel, data.SensorsData?.GPU.FanSpeed ?? -1, data.GpuPower);
            }
        }

        RefreshBattery(data.BatteryInformation, data.BatteryState);
        RefreshUpdate(data.HasUpdate);
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
        _batteryValueLabel.Content = $"{info.BatteryPercentage}%";
        _batteryModeValueLabel.Content = batteryState.GetDisplayName();
        _batteryDischargeValueLabel.Content = $"{info.DischargeRate / 1000.0:+0.00;-0.00;0.00} W";
        _batteryMinDischargeValueLabel.Content = $"{info.MinDischargeRate / 1000.0:+0.00;-0.00;0.00} W";
        _batteryMaxDischargeValueLabel.Content = $"{info.MaxDischargeRate / 1000.0:+0.00;-0.00;0.00} W";
    }

    private void RefreshUpdate(bool hasUpdate) => _updateIndicator.Visibility = hasUpdate ? Visibility.Visible : Visibility.Collapsed;
    private static string GetTemperatureText(double t) { var s = IoCContainer.Resolve<ApplicationSettings>(); return t <= 0 ? "-" : (s.Store.TemperatureUnit == TemperatureUnit.F ? $"{(t * 9 / 5 + 32):0}{Resource.Fahrenheit}" : $"{t:0}{Resource.Celsius}"); }
    private static void UpdateFreqAndTemp(System.Windows.Controls.Label l, double f, double t) => l.Content = (t < 0 || f < 0) ? "-" : $"{f:0}Mhz | {GetTemperatureText(t)}";
    private static void UpdateFanAndPower(System.Windows.Controls.Label l, double f, double p) => l.Content = (f < 0 || p < 0) ? "-" : $"{f:0}RPM | {p:0}W";
    private static void UpdateSystemFan(System.Windows.Controls.Label l, double f) => l.Content = f < 0 ? "-" : $"{f:0}RPM";

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