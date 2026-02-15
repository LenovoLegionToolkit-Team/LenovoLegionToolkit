using LenovoLegionToolkit.Lib;
using LenovoLegionToolkit.Lib.Controllers.GodMode;
using LenovoLegionToolkit.Lib.Extensions;
using LenovoLegionToolkit.Lib.Features;
using LenovoLegionToolkit.Lib.SoftwareDisabler;
using LenovoLegionToolkit.Lib.System.Management;
using LenovoLegionToolkit.Lib.Utils;
using LenovoLegionToolkit.WPF.Extensions;
using LenovoLegionToolkit.WPF.Resources;
using LenovoLegionToolkit.WPF.Controls.Dashboard.GodMode;
using LenovoLegionToolkit.WPF.Utils;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Management;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;

namespace LenovoLegionToolkit.WPF.Windows.Dashboard;

public partial class GodModeSettingsWindow
{
    private readonly PowerModeFeature _powerModeFeature = IoCContainer.Resolve<PowerModeFeature>();
    private readonly GodModeController _godModeController = IoCContainer.Resolve<GodModeController>();

    private readonly VantageDisabler _vantageDisabler = IoCContainer.Resolve<VantageDisabler>();
    private readonly LegionSpaceDisabler _legionSpaceDisabler = IoCContainer.Resolve<LegionSpaceDisabler>();
    private readonly LegionZoneDisabler _legionZoneDisabler = IoCContainer.Resolve<LegionZoneDisabler>();

    private GodModeState? _state;
    private Dictionary<PowerModeState, GodModeDefaults>? _defaults;
    private bool _isRefreshing;

    private readonly dynamic _fanCurveControl;

    private const int BIOS_OC_MODE_ENABLED = 3;

    public GodModeSettingsWindow()
    {
        InitializeComponent();

        IsVisibleChanged += GodModeSettingsWindow_IsVisibleChanged;

        var mi = Compatibility.GetMachineInformationAsync().GetAwaiter().GetResult();
        int contentIndex = _fanCurveControlStackPanel.Children.IndexOf(_fanCurveButton);

        if (mi.Properties.SupportsGodModeV3 || mi.Properties.SupportsGodModeV4)
            _fanCurveControl = new Controls.FanCurveControlV2();
        else
            _fanCurveControl = new Controls.FanCurveControl();

        _fanCurveControlStackPanel.Children.Insert(contentIndex, _fanCurveControl);
    }

    private async void GodModeSettingsWindow_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (IsVisible)
            await RefreshAsync();
    }

    private async Task RefreshAsync()
    {
        if (_isRefreshing) return;
        _isRefreshing = true;

        const int maxRetries = 5;
        Exception? lastException = null;

        for (int attempt = 1; attempt <= maxRetries; attempt++)
        {
            try
            {
                _loader.IsLoading = true;
                _buttonsStackPanel.Visibility = Visibility.Hidden;

                var tasks = new List<Task>
                {
                    Task.Delay(500),
                    _godModeController.GetStateAsync().ContinueWith(t => _state = t.Result),
                    _godModeController.GetDefaultsInOtherPowerModesAsync().ContinueWith(t => _defaults = t.Result)
                };

                var vantageTask = _godModeController.NeedsVantageDisabledAsync();
                var legionSpace = _godModeController.NeedsLegionSpaceDisabledAsync();
                var legionZoneTask = _godModeController.NeedsLegionZoneDisabledAsync();

                await Task.WhenAll(tasks.Concat([vantageTask, legionSpace, legionZoneTask]));

                _vantageRunningWarningInfoBar.IsOpen = vantageTask.Result && await _vantageDisabler.GetStatusAsync() == SoftwareStatus.Enabled;
                _legionSpaceRunningWarningInfoBar.IsOpen = legionSpace.Result && await _legionSpaceDisabler.GetStatusAsync() == SoftwareStatus.Enabled;
                _legionZoneRunningWarningInfoBar.IsOpen = legionZoneTask.Result && await _legionZoneDisabler.GetStatusAsync() == SoftwareStatus.Enabled;

                if (_state is null || _defaults is null)
                    throw new InvalidOperationException("Failed to load state or defaults.");

                await SetStateAsync(_state.Value);

                _loadButton.Visibility = _defaults.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
                _buttonsStackPanel.Visibility = Visibility.Visible;
                _loader.IsLoading = false;
                _isRefreshing = false;
                return; // Success - exit
            }
            catch (Exception ex) when (attempt < maxRetries)
            {
                Log.Instance.Trace($"Load attempt {attempt} failed, retrying in 3s...", ex);
                lastException = ex;
                await Task.Delay(3000); // Wait before retry
            }
            catch (Exception ex)
            {
                lastException = ex;
            }
        }

        // All retries failed
        Log.Instance.Trace($"Couldn't load settings after {maxRetries} attempts.", lastException);
        await _snackBar.ShowAsync(Resource.GodModeSettingsWindow_Error_Load_Title, lastException?.Message ?? "Unknown error");
        _isRefreshing = false;
        Close();
    }

    private async Task<bool> ApplyAsync()
    {
        try
        {
            if (!_state.HasValue) throw new InvalidOperationException("State is null");

            var activePresetId = _state.Value.ActivePresetId;
            var preset = _state.Value.Presets[activePresetId];

            var newPreset = new GodModePreset
            {
                Name = preset.Name,
                PowerPlanGuid = preset.PowerPlanGuid,
                PowerMode = preset.PowerMode,
                CPULongTermPowerLimit = preset.CPULongTermPowerLimit?.WithValue(_cpuLongTermPowerLimitControl.Value),
                CPUShortTermPowerLimit = preset.CPUShortTermPowerLimit?.WithValue(_cpuShortTermPowerLimitControl.Value),
                CPUPeakPowerLimit = preset.CPUPeakPowerLimit?.WithValue(_cpuPeakPowerLimitControl.Value),
                CPUCrossLoadingPowerLimit = preset.CPUCrossLoadingPowerLimit?.WithValue(_cpuCrossLoadingLimitControl.Value),
                CPUPL1Tau = preset.CPUPL1Tau?.WithValue(_cpuPL1TauControl.Value),
                APUsPPTPowerLimit = preset.APUsPPTPowerLimit?.WithValue(_apuSPPTPowerLimitControl.Value),
                CPUTemperatureLimit = preset.CPUTemperatureLimit?.WithValue(_cpuTemperatureLimitControl.Value),
                GPUPowerBoost = preset.GPUPowerBoost?.WithValue(_gpuPowerBoostControl.Value),
                GPUConfigurableTGP = preset.GPUConfigurableTGP?.WithValue(_gpuConfigurableTGPControl.Value),
                GPUTemperatureLimit = preset.GPUTemperatureLimit?.WithValue(_gpuTemperatureLimitControl.Value),
                GPUTotalProcessingPowerTargetOnAcOffsetFromBaseline = preset.GPUTotalProcessingPowerTargetOnAcOffsetFromBaseline?.WithValue(_gpuTotalProcessingPowerTargetOnAcOffsetFromBaselineControl.Value),
                GPUToCPUDynamicBoost = preset.GPUToCPUDynamicBoost?.WithValue(_gpuToCpuDynamicBoostControl.Value),
                FanTableInfo = preset.FanTableInfo is not null ? _fanCurveControl.GetFanTableInfo() : null,
                FanFullSpeed = preset.FanFullSpeed is not null ? _fanFullSpeedToggle.IsChecked : null,
                MaxValueOffset = preset.MaxValueOffset is not null ? (int?)_maxValueOffsetNumberBox.Value : null,
                MinValueOffset = preset.MinValueOffset is not null ? (int?)_minValueOffsetNumberBox.Value : null,
                PrecisionBoostOverdriveScaler = preset.PrecisionBoostOverdriveScaler?.WithValue(_cpuPrecisionBoostOverdriveScaler.Value),
                PrecisionBoostOverdriveBoostFrequency = preset.PrecisionBoostOverdriveBoostFrequency?.WithValue(_cpuPrecisionBoostOverdriveBoostFrequency.Value),
                AllCoreCurveOptimizer = preset.AllCoreCurveOptimizer?.WithValue(_cpuAllCoreCurveOptimizer.Value),
                EnableAllCoreCurveOptimizer = _toggleCoreCurveCard.Visibility == Visibility.Visible ? _coreCurveToggle.IsChecked : preset.EnableAllCoreCurveOptimizer,
                EnableOverclocking = _toggleOcCard.Visibility == Visibility.Visible ? _overclockingToggle.IsChecked : preset.EnableOverclocking,
            };

            var newPresets = new Dictionary<Guid, GodModePreset>(_state.Value.Presets)
            {
                [activePresetId] = newPreset
            };

            var newState = new GodModeState
            {
                ActivePresetId = activePresetId,
                Presets = newPresets.AsReadOnlyDictionary(),
            };

            if (await _powerModeFeature.GetStateAsync() != PowerModeState.GodMode)
                await _powerModeFeature.SetStateAsync(PowerModeState.GodMode);

            await _godModeController.SetStateAsync(newState);
            await _godModeController.ApplyStateAsync();

            return true;
        }
        catch (Exception ex)
        {
            Log.Instance.Trace($"Couldn't apply settings", ex);
            await _snackBar.ShowAsync(Resource.GodModeSettingsWindow_Error_Apply_Title, ex.Message);
            return false;
        }
    }

    private async Task SetStateAsync(GodModeState state)
    {
        _cpuLongTermPowerLimitControl.ValueChanged -= CpuLongTermPowerLimitSlider_ValueChanged;
        _cpuShortTermPowerLimitControl.ValueChanged -= CpuShortTermPowerLimitSlider_ValueChanged;

        try
        {
            var activePresetId = state.ActivePresetId;
            var preset = state.Presets[activePresetId];

            _presetsComboBox.SetItems(state.Presets.OrderBy(kv => kv.Value.Name), new(activePresetId, preset), kv => kv.Value.Name);
            _deletePresetsButton.IsEnabled = state.Presets.Count > 1;

            _cpuLongTermPowerLimitControl.Set(preset.CPULongTermPowerLimit);
            _cpuShortTermPowerLimitControl.Set(preset.CPUShortTermPowerLimit);
            _cpuPeakPowerLimitControl.Set(preset.CPUPeakPowerLimit);
            _cpuCrossLoadingLimitControl.Set(preset.CPUCrossLoadingPowerLimit);
            _cpuPL1TauControl.Set(preset.CPUPL1Tau);
            _apuSPPTPowerLimitControl.Set(preset.APUsPPTPowerLimit);
            _cpuTemperatureLimitControl.Set(preset.CPUTemperatureLimit);

            _cpuPrecisionBoostOverdriveScaler.Set(preset.PrecisionBoostOverdriveScaler);
            _cpuPrecisionBoostOverdriveBoostFrequency.Set(preset.PrecisionBoostOverdriveBoostFrequency);
            _cpuAllCoreCurveOptimizer.Set(preset.AllCoreCurveOptimizer);

            _gpuPowerBoostControl.Set(preset.GPUPowerBoost);
            _gpuConfigurableTGPControl.Set(preset.GPUConfigurableTGP);
            _gpuTemperatureLimitControl.Set(preset.GPUTemperatureLimit);
            _gpuTotalProcessingPowerTargetOnAcOffsetFromBaselineControl.Set(preset.GPUTotalProcessingPowerTargetOnAcOffsetFromBaseline);
            _gpuToCpuDynamicBoostControl.Set(preset.GPUToCPUDynamicBoost);

            if (preset.FanTableInfo.HasValue)
            {
                var minimum = await _godModeController.GetMinimumFanTableAsync();
                _fanCurveControl.SetFanTableInfo(preset.FanTableInfo.Value, minimum);
                _fanCurveCardControl.Visibility = Visibility.Visible;
            }
            else
            {
                _fanCurveCardControl.Visibility = Visibility.Collapsed;
            }

            if (preset.FanFullSpeed.HasValue)
            {
                _fanCurveCardControl.IsEnabled = !preset.FanFullSpeed.Value;
                _fanFullSpeedToggle.IsChecked = preset.FanFullSpeed.Value;
                _fanFullSpeedCardControl.Visibility = Visibility.Visible;
            }
            else
            {
                _fanCurveCardControl.IsEnabled = true;
                _fanFullSpeedCardControl.Visibility = Visibility.Collapsed;
            }

            if (preset.MaxValueOffset.HasValue)
            {
                _maxValueOffsetNumberBox.Value = preset.MaxValueOffset;
                _maxValueOffsetCardControl.Visibility = Visibility.Visible;
            }
            else
            {
                _maxValueOffsetCardControl.Visibility = Visibility.Collapsed;
            }

            if (preset.MinValueOffset.HasValue)
            {
                _minValueOffsetNumberBox.Value = preset.MinValueOffset;
                _minValueOffsetCardControl.Visibility = Visibility.Visible;
            }
            else
            {
                _minValueOffsetCardControl.Visibility = Visibility.Collapsed;
            }

            bool cpuSectionVisible = _cpuLongTermPowerLimitControl.Visibility == Visibility.Visible ||
                                     _cpuShortTermPowerLimitControl.Visibility == Visibility.Visible ||
                                     _cpuPeakPowerLimitControl.Visibility == Visibility.Visible ||
                                     _cpuCrossLoadingLimitControl.Visibility == Visibility.Visible ||
                                     _cpuPL1TauControl.Visibility == Visibility.Visible ||
                                     _apuSPPTPowerLimitControl.Visibility == Visibility.Visible ||
                                     _cpuTemperatureLimitControl.Visibility == Visibility.Visible;

            bool gpuSectionVisible = _gpuPowerBoostControl.Visibility == Visibility.Visible ||
                                     _gpuConfigurableTGPControl.Visibility == Visibility.Visible ||
                                     _gpuTemperatureLimitControl.Visibility == Visibility.Visible ||
                                     _gpuTotalProcessingPowerTargetOnAcOffsetFromBaselineControl.Visibility == Visibility.Visible ||
                                     _gpuToCpuDynamicBoostControl.Visibility == Visibility.Visible;

            bool fanSectionVisible = _fanCurveCardControl.Visibility == Visibility.Visible ||
                                     _fanFullSpeedCardControl.Visibility == Visibility.Visible;

            bool advancedSectionVisible = _maxValueOffsetCardControl.Visibility == Visibility.Visible ||
                                          _minValueOffsetCardControl.Visibility == Visibility.Visible;

            _cpuSectionTitle.Visibility = cpuSectionVisible ? Visibility.Visible : Visibility.Collapsed;
            _gpuSectionTitle.Visibility = gpuSectionVisible ? Visibility.Visible : Visibility.Collapsed;
            _fanSectionTitle.Visibility = fanSectionVisible ? Visibility.Visible : Visibility.Collapsed;
            _advancedSectionTitle.Visibility = advancedSectionVisible ? Visibility.Visible : Visibility.Collapsed;
            _advancedSectionMessage.Visibility = advancedSectionVisible ? Visibility.Visible : Visibility.Collapsed;

            _overclockingToggle.IsChecked = preset.EnableOverclocking;
            _coreCurveToggle.IsChecked = preset.EnableAllCoreCurveOptimizer;

            if (preset.EnableOverclocking.HasValue)
                await UpdateOverclockingVisibilityAsync();

        }
        finally
        {
            _cpuLongTermPowerLimitControl.ValueChanged += CpuLongTermPowerLimitSlider_ValueChanged;
            _cpuShortTermPowerLimitControl.ValueChanged += CpuShortTermPowerLimitSlider_ValueChanged;
        }
    }

    private async Task UpdateOverclockingVisibilityAsync()
    {
        var mi = await Compatibility.GetMachineInformationAsync();
        var isLegionOptimizeEnabled = await IsBiosOcEnabledAsync();

        _toggleOcCard.Visibility = (isLegionOptimizeEnabled && mi.Properties.IsAmdDevice) ? Visibility.Visible : Visibility.Collapsed;

        var ocVisible = (isLegionOptimizeEnabled && _overclockingToggle.IsChecked == true)
            ? Visibility.Visible
            : Visibility.Collapsed;

        _cpuPrecisionBoostOverdriveScaler.Visibility = ocVisible;
        _cpuPrecisionBoostOverdriveBoostFrequency.Visibility = ocVisible;

        _toggleCoreCurveCard.Visibility = ocVisible;

        await UpdateCoreCurveVisibilityAsync();
    }

    private async Task UpdateCoreCurveVisibilityAsync()
    {
        bool isCurveEnabled = _toggleCoreCurveCard.Visibility == Visibility.Visible &&
                              _coreCurveToggle.IsChecked == true;

        _cpuAllCoreCurveOptimizer.Visibility = isCurveEnabled
            ? Visibility.Visible
            : Visibility.Collapsed;

        await Task.CompletedTask;
    }

    private async void SetDefaults(GodModeDefaults defaults)
    {
        SetVal<int>(_cpuLongTermPowerLimitControl, defaults.CPULongTermPowerLimit, v => _cpuLongTermPowerLimitControl.Value = v);
        SetVal<int>(_cpuShortTermPowerLimitControl, defaults.CPUShortTermPowerLimit, v => _cpuShortTermPowerLimitControl.Value = v);
        SetVal<int>(_cpuPeakPowerLimitControl, defaults.CPUPeakPowerLimit, v => _cpuPeakPowerLimitControl.Value = v);
        SetVal<int>(_cpuCrossLoadingLimitControl, defaults.CPUCrossLoadingPowerLimit, v => _cpuCrossLoadingLimitControl.Value = v);
        SetVal<int>(_cpuPL1TauControl, defaults.CPUPL1Tau, v => _cpuPL1TauControl.Value = v);
        SetVal<int>(_apuSPPTPowerLimitControl, defaults.APUsPPTPowerLimit, v => _apuSPPTPowerLimitControl.Value = v);
        SetVal<int>(_cpuTemperatureLimitControl, defaults.CPUTemperatureLimit, v => _cpuTemperatureLimitControl.Value = v);

        SetVal<int>(_cpuPrecisionBoostOverdriveScaler, defaults.PrecisionBoostOverdriveScaler, v => _cpuPrecisionBoostOverdriveScaler.Value = v);
        SetVal<int>(_cpuPrecisionBoostOverdriveBoostFrequency, defaults.PrecisionBoostOverdriveBoostFrequency, v => _cpuPrecisionBoostOverdriveBoostFrequency.Value = v);
        SetVal<int>(_cpuAllCoreCurveOptimizer, defaults.AllCoreCurveOptimizer, v => _cpuAllCoreCurveOptimizer.Value = v);

        SetVal<bool>(_overclockingToggle, defaults.EnableOverclocking, v => _overclockingToggle.IsChecked = v);
        SetVal<bool>(_coreCurveToggle, defaults.EnableAllCoreCurveOptimizer, v => _coreCurveToggle.IsChecked = v);
        SetVal<int>(_cpuAllCoreCurveOptimizer, defaults.AllCoreCurveOptimizer, v => _cpuAllCoreCurveOptimizer.Value = v);
        SetVal<bool>(_overclockingToggle, defaults.EnableOverclocking, v => {
            _overclockingToggle.IsChecked = v;
            _ = UpdateOverclockingVisibilityAsync();
        });

        SetVal<int>(_gpuPowerBoostControl, defaults.GPUPowerBoost, v => _gpuPowerBoostControl.Value = v);
        SetVal<int>(_gpuConfigurableTGPControl, defaults.GPUConfigurableTGP, v => _gpuConfigurableTGPControl.Value = v);
        SetVal<int>(_gpuTemperatureLimitControl, defaults.GPUTemperatureLimit, v => _gpuTemperatureLimitControl.Value = v);
        SetVal<int>(_gpuTotalProcessingPowerTargetOnAcOffsetFromBaselineControl, defaults.GPUTotalProcessingPowerTargetOnAcOffsetFromBaseline, v => _gpuTotalProcessingPowerTargetOnAcOffsetFromBaselineControl.Value = v);
        SetVal<int>(_gpuToCpuDynamicBoostControl, defaults.GPUToCPUDynamicBoost, v => _gpuToCpuDynamicBoostControl.Value = v);

        if (_fanCurveCardControl.Visibility == Visibility.Visible && defaults.FanTable is { } fanTable)
        {
            var state = await _godModeController.GetStateAsync();
            var preset = state.Presets[state.ActivePresetId];
            var data = preset.FanTableInfo?.Data;

            if (data is not null)
            {
                var defaultFanTableInfo = new FanTableInfo(data, fanTable);
                var minimum = await _godModeController.GetMinimumFanTableAsync();
                _fanCurveControl.SetFanTableInfo(defaultFanTableInfo, minimum);
            }
        }

        if (_fanFullSpeedCardControl.Visibility == Visibility.Visible && defaults.FanFullSpeed is { } fanFullSpeed)
            _fanFullSpeedToggle.IsChecked = fanFullSpeed;

        if (_maxValueOffsetCardControl.Visibility == Visibility.Visible)
            _maxValueOffsetNumberBox.Text = "0";

        if (_minValueOffsetCardControl.Visibility == Visibility.Visible)
            _minValueOffsetNumberBox.Text = "0";
    }

    private async void PresetsComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_state.HasValue) return;

        if (!_presetsComboBox.TryGetSelectedItem<KeyValuePair<Guid, GodModePreset>>(out var item)) return;

        if (_state.Value.ActivePresetId == item.Key) return;

        _state = _state.Value with { ActivePresetId = item.Key };
        await SetStateAsync(_state.Value);
    }

    private async void EditPresetsButton_Click(object sender, RoutedEventArgs e)
    {
        if (!_state.HasValue) return;

        var activePresetId = _state.Value.ActivePresetId;
        var presets = _state.Value.Presets;
        var preset = presets[activePresetId];

        var result = await MessageBoxHelper.ShowInputAsync(this, Resource.GodModeSettingsWindow_EditPreset_Title, Resource.GodModeSettingsWindow_EditPreset_Message, preset.Name);
        if (string.IsNullOrEmpty(result)) return;

        var newPresets = new Dictionary<Guid, GodModePreset>(presets)
        {
            [activePresetId] = preset with { Name = result }
        };
        _state = _state.Value with { Presets = newPresets.AsReadOnlyDictionary() };
        await SetStateAsync(_state.Value);
    }

    private async void DeletePresetsButton_Click(object sender, RoutedEventArgs e)
    {
        if (!_state.HasValue || _state.Value.Presets.Count <= 1) return;

        var activePresetId = _state.Value.ActivePresetId;
        var presets = _state.Value.Presets;

        var newPresets = new Dictionary<Guid, GodModePreset>(presets);
        newPresets.Remove(activePresetId);
        var newActivePresetId = newPresets.OrderBy(kv => kv.Value.Name).First().Key;

        _state = new GodModeState
        {
            ActivePresetId = newActivePresetId,
            Presets = newPresets.AsReadOnlyDictionary()
        };
        await SetStateAsync(_state.Value);
    }

    private async void AddPresetsButton_Click(object sender, RoutedEventArgs e)
    {
        if (!_state.HasValue) return;

        var result = await MessageBoxHelper.ShowInputAsync(this, Resource.GodModeSettingsWindow_EditPreset_Title, Resource.GodModeSettingsWindow_EditPreset_Message);
        if (string.IsNullOrEmpty(result)) return;

        var activePresetId = _state.Value.ActivePresetId;
        var presets = _state.Value.Presets;
        var preset = presets[activePresetId];

        var newActivePresetId = Guid.NewGuid();
        var newPreset = preset with { Name = result };
        var newPresets = new Dictionary<Guid, GodModePreset>(presets)
        {
            [newActivePresetId] = newPreset
        };

        _state = new GodModeState
        {
            ActivePresetId = newActivePresetId,
            Presets = newPresets.AsReadOnlyDictionary()
        };

        await SetStateAsync(_state.Value);
    }

    private async void DefaultFanCurve_Click(object sender, RoutedEventArgs e)
    {
        var state = await _godModeController.GetStateAsync();
        var preset = state.Presets[state.ActivePresetId];
        var data = preset.FanTableInfo?.Data;

        if (data is null) return;

        var defaultFanTable = await _godModeController.GetDefaultFanTableAsync();
        var defaultFanTableInfo = new FanTableInfo(data, defaultFanTable);
        var minimum = await _godModeController.GetMinimumFanTableAsync();
        _fanCurveControl.SetFanTableInfo(defaultFanTableInfo, minimum);
    }

    private void LoadButton_Click(object sender, RoutedEventArgs e)
    {
        if (_defaults is null || _defaults.IsEmpty())
        {
            _loadButton.Visibility = Visibility.Collapsed;
            return;
        }

        var contextMenu = new ContextMenu
        {
            PlacementTarget = _loadButton,
            Placement = PlacementMode.Bottom,
        };

        foreach (var d in _defaults.OrderBy(x => x.Key))
        {
            var menuItem = new MenuItem { Header = d.Key.GetDisplayName() };
            menuItem.Click += (_, _) => SetDefaults(d.Value);
            contextMenu.Items.Add(menuItem);
        }

        _loadButton.ContextMenu = contextMenu;
        _loadButton.ContextMenu.IsOpen = true;
    }

    private async void SaveAndCloseButton_Click(object sender, RoutedEventArgs e)
    {
        if (await ApplyAsync()) Close();
    }

    private async void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        await ApplyAsync();
        await RefreshAsync();
    }

    private void CpuLongTermPowerLimitSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_isRefreshing) return;
        if (_cpuLongTermPowerLimitControl.Value > _cpuShortTermPowerLimitControl.Value)
            _cpuShortTermPowerLimitControl.Value = _cpuLongTermPowerLimitControl.Value;
    }

    private void CpuShortTermPowerLimitSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_isRefreshing) return;
        if (_cpuLongTermPowerLimitControl.Value > _cpuShortTermPowerLimitControl.Value)
            _cpuLongTermPowerLimitControl.Value = _cpuShortTermPowerLimitControl.Value;
    }

    private void FanFullSpeedToggle_Click(object sender, RoutedEventArgs e)
    {
        _fanCurveCardControl.IsEnabled = !(_fanFullSpeedToggle.IsChecked ?? false);
    }

    private async void OverclockingToggle_Click(object sender, RoutedEventArgs e)
    {
        if (_isRefreshing)
        {
            return;
        }

        await UpdateOverclockingVisibilityAsync();
    }

    private void SetVal<T>(Control control, T? value, Action<T> setter) where T : struct
    {
        if (control.Visibility == Visibility.Visible && value.HasValue) setter(value.Value);
    }

    private async void CoreCurveToggle_Click(object sender, RoutedEventArgs e)
    {
        if (_isRefreshing)
        {
            return;
        }

        await UpdateCoreCurveVisibilityAsync();
    }

    private static async Task<bool> IsBiosOcEnabledAsync()
    {
        try
        {
            var result = await WMI.LenovoGameZoneData.GetBIOSOCMode().ConfigureAwait(false);
            return result == BIOS_OC_MODE_ENABLED;
        }
        catch (ManagementException)
        {
            return false;
        }
    }

    #region Quick Templates

    /// <summary>
    /// Silent Surf: Quiet but usable - 25W CPU, 70°C temp limit
    /// Fan Curve: Very slow ramp, audible only under load
    /// </summary>
    /// <summary>
    /// Silent Surf: Minimum Power
    /// </summary>
    private void QuickTemplateSilentSurf_Click(object sender, RoutedEventArgs e)
    {
        // Safe relative minimums
        int pl1 = (int)Math.Max(25, _cpuLongTermPowerLimitControl.Minimum);
        int pl2 = (int)Math.Max(35, _cpuShortTermPowerLimitControl.Minimum);
        int temp = (int)Math.Clamp(75, _cpuTemperatureLimitControl.Minimum, _cpuTemperatureLimitControl.Maximum);
        
        // Expanded values
        int peak = (int)Math.Max(45, _cpuPeakPowerLimitControl.Minimum);
        int cross = (int)Math.Max(25, _cpuCrossLoadingLimitControl.Minimum);
        int dynBoost = (int)Math.Max(5, _gpuPowerBoostControl.Minimum); // 5W Dynamic Boost
        int gpuToCpu = (int)Math.Max(5, _gpuToCpuDynamicBoostControl.Minimum); // Minimal GPU→CPU transfer
        int? gpuOffset = _gpuTotalProcessingPowerTargetOnAcOffsetFromBaselineControl.Visibility == Visibility.Visible
            ? (int)_gpuTotalProcessingPowerTargetOnAcOffsetFromBaselineControl.Minimum
            : null;

        // Fan curve values are indices 0-10 (not percentages!)
        // 0 = off, 1-10 = increasing speed levels
        ushort[] silentCurve = [0, 0, 1, 1, 2, 2, 3, 3, 4, 5];

        ApplyQuickTemplate(
            cpuLongTerm: pl1,
            cpuShortTerm: pl2,
            cpuPeak: peak,
            cpuCrossLoad: cross,
            cpuTempLimit: temp,
            gpuTgp: null,
            gpuDynBoost: dynBoost,
            gpuToCpuDynBoost: gpuToCpu,
            gpuPowerOffset: gpuOffset,
            gpuTempLimit: null,
            fanFullSpeed: false,
            fanCurve: silentCurve
        );

        // Also disable Turbo Boost for maximum silence
        _ = Task.Run(() => SetTurboBoostMode(0)); // 0 = Disabled

        _ = _snackBar.ShowAsync("🔇 Silent Surf", $"Applied: {pl1}W PL1, {temp}°C, Turbo OFF.");
    }

    /// <summary>
    /// Daily Work: Balanced
    /// </summary>
    private void QuickTemplateDailyWork_Click(object sender, RoutedEventArgs e)
    {
        int pl1 = GetRelativeValue(_cpuLongTermPowerLimitControl, 0.35);
        int pl2 = GetRelativeValue(_cpuShortTermPowerLimitControl, 0.45);
        int peak = GetRelativeValue(_cpuPeakPowerLimitControl, 0.50);
        int cross = GetRelativeValue(_cpuCrossLoadingLimitControl, 0.40);
        int temp = (int)Math.Clamp(85, _cpuTemperatureLimitControl.Minimum, _cpuTemperatureLimitControl.Maximum);
        
        int? gpu = _gpuConfigurableTGPControl.Visibility == Visibility.Visible 
            ? GetRelativeValue(_gpuConfigurableTGPControl, 0.50) 
            : null;
        int? dynBoost = _gpuPowerBoostControl.Visibility == Visibility.Visible
            ? GetRelativeValue(_gpuPowerBoostControl, 0.50)
            : null;
        int? gpuToCpu = _gpuToCpuDynamicBoostControl.Visibility == Visibility.Visible
            ? GetRelativeValue(_gpuToCpuDynamicBoostControl, 0.50)
            : null;
        int? gpuOffset = _gpuTotalProcessingPowerTargetOnAcOffsetFromBaselineControl.Visibility == Visibility.Visible
            ? GetRelativeValue(_gpuTotalProcessingPowerTargetOnAcOffsetFromBaselineControl, 0.50)
            : null;

        // Fan curve values are indices 0-10 (not percentages!)
        ushort[] dailyCurve = [0, 1, 2, 3, 4, 5, 5, 6, 7, 8];

        ApplyQuickTemplate(
            cpuLongTerm: pl1,
            cpuShortTerm: pl2,
            cpuPeak: peak,
            cpuCrossLoad: cross,
            cpuTempLimit: temp,
            gpuTgp: gpu,
            gpuDynBoost: dynBoost,
            gpuToCpuDynBoost: gpuToCpu,
            gpuPowerOffset: gpuOffset,
            gpuTempLimit: 83,
            fanFullSpeed: false,
            fanCurve: dailyCurve
        );

        // Restore Turbo Boost (in case Silent Surf disabled it)
        _ = Task.Run(() => SetTurboBoostMode(2)); // 2 = Aggressive

        _ = _snackBar.ShowAsync("💼 Daily Work", $"Applied: {pl1}W PL1, {temp}°C, Turbo ON.");
    }

    /// <summary>
    /// Gaming Cool: High Perf
    /// </summary>
    private void QuickTemplateGamingCool_Click(object sender, RoutedEventArgs e)
    {
        int pl1 = GetRelativeValue(_cpuLongTermPowerLimitControl, 0.75);
        int pl2 = GetRelativeValue(_cpuShortTermPowerLimitControl, 0.85);
        int peak = GetRelativeValue(_cpuPeakPowerLimitControl, 0.90);
        int cross = GetRelativeValue(_cpuCrossLoadingLimitControl, 0.80);
        int temp = (int)Math.Clamp(87, _cpuTemperatureLimitControl.Minimum, _cpuTemperatureLimitControl.Maximum);
        
        int? gpu = _gpuConfigurableTGPControl.Visibility == Visibility.Visible 
            ? GetRelativeValue(_gpuConfigurableTGPControl, 0.85) 
            : null;
        int? dynBoost = _gpuPowerBoostControl.Visibility == Visibility.Visible
            ? GetRelativeValue(_gpuPowerBoostControl, 0.90)
            : null;
        int? gpuToCpu = _gpuToCpuDynamicBoostControl.Visibility == Visibility.Visible
            ? GetRelativeValue(_gpuToCpuDynamicBoostControl, 0.90)
            : null;
        int? gpuOffset = _gpuTotalProcessingPowerTargetOnAcOffsetFromBaselineControl.Visibility == Visibility.Visible
            ? GetRelativeValue(_gpuTotalProcessingPowerTargetOnAcOffsetFromBaselineControl, 0.90)
            : null;

        // Fan curve values are indices 0-10 (not percentages!)
        // Max 8 to avoid vacuum cleaner noise (no 100%)
        ushort[] gamingCurve = [2, 2, 3, 4, 5, 6, 7, 8, 8, 8];

        ApplyQuickTemplate(
            cpuLongTerm: pl1,
            cpuShortTerm: pl2,
            cpuPeak: peak,
            cpuCrossLoad: cross,
            cpuTempLimit: temp,
            gpuTgp: gpu, 
            gpuDynBoost: dynBoost,
            gpuToCpuDynBoost: gpuToCpu,
            gpuPowerOffset: gpuOffset,
            gpuTempLimit: 83,
            fanFullSpeed: false,
            fanCurve: gamingCurve
        );

        // Restore Turbo Boost (in case Silent Surf disabled it)
        _ = Task.Run(() => SetTurboBoostMode(2)); // 2 = Aggressive

        _ = _snackBar.ShowAsync("🎮 Gaming Cool", $"Applied: {pl1}W PL1, {temp}°C, Turbo ON.");
    }

    private int GetRelativeValue(Controls.Dashboard.GodMode.GodModeValueControl control, double ratio)
    {
        // Use EffectiveMinimum/Maximum to handle both Slider and ComboBox modes
        var min = control.EffectiveMinimum;
        var max = control.EffectiveMaximum;
        
        // Safety check if not initialized
        if (max <= min) return max;

        var range = max - min;
        return (int)(min + (range * ratio));
    }

    /// <summary>
    /// Applies recommended values to the sliders, respecting min/max limits.
    /// Also sets the fan curve if provided.
    /// </summary>
    private async void ApplyQuickTemplate(
        int? cpuLongTerm,
        int? cpuShortTerm,
        int? cpuPeak,
        int? cpuCrossLoad,
        int? cpuTempLimit,
        int? gpuTgp,
        int? gpuDynBoost,
        int? gpuToCpuDynBoost,
        int? gpuPowerOffset,
        int? gpuTempLimit,
        bool? fanFullSpeed,
        ushort[]? fanCurve = null)
    {
        // CPU Power Limits
        if (cpuLongTerm.HasValue) _cpuLongTermPowerLimitControl.Value = ClampToSlider(_cpuLongTermPowerLimitControl, cpuLongTerm.Value);
        if (cpuShortTerm.HasValue) _cpuShortTermPowerLimitControl.Value = ClampToSlider(_cpuShortTermPowerLimitControl, cpuShortTerm.Value);
        if (cpuPeak.HasValue) _cpuPeakPowerLimitControl.Value = ClampToSlider(_cpuPeakPowerLimitControl, cpuPeak.Value);
        if (cpuCrossLoad.HasValue) _cpuCrossLoadingLimitControl.Value = ClampToSlider(_cpuCrossLoadingLimitControl, cpuCrossLoad.Value);
        if (cpuTempLimit.HasValue) _cpuTemperatureLimitControl.Value = ClampToSlider(_cpuTemperatureLimitControl, cpuTempLimit.Value);

        // GPU Settings
        if (gpuTgp.HasValue && _gpuConfigurableTGPControl.Visibility == Visibility.Visible)
            _gpuConfigurableTGPControl.Value = ClampToSlider(_gpuConfigurableTGPControl, gpuTgp.Value);
        
        if (gpuDynBoost.HasValue && _gpuPowerBoostControl.Visibility == Visibility.Visible)
            _gpuPowerBoostControl.Value = ClampToSlider(_gpuPowerBoostControl, gpuDynBoost.Value);
        
        if (gpuToCpuDynBoost.HasValue && _gpuToCpuDynamicBoostControl.Visibility == Visibility.Visible)
            _gpuToCpuDynamicBoostControl.Value = ClampToSlider(_gpuToCpuDynamicBoostControl, gpuToCpuDynBoost.Value);
        
        if (gpuPowerOffset.HasValue && _gpuTotalProcessingPowerTargetOnAcOffsetFromBaselineControl.Visibility == Visibility.Visible)
            _gpuTotalProcessingPowerTargetOnAcOffsetFromBaselineControl.Value = ClampToSlider(_gpuTotalProcessingPowerTargetOnAcOffsetFromBaselineControl, gpuPowerOffset.Value);
            
        if (gpuTempLimit.HasValue && _gpuTemperatureLimitControl.Visibility == Visibility.Visible)
            _gpuTemperatureLimitControl.Value = ClampToSlider(_gpuTemperatureLimitControl, gpuTempLimit.Value);

        // Fan Full Speed
        if (fanFullSpeed.HasValue && _fanFullSpeedCardControl.Visibility == Visibility.Visible)
        {
            _fanFullSpeedToggle.IsChecked = fanFullSpeed.Value;
            _fanCurveCardControl.IsEnabled = !fanFullSpeed.Value;
        }

        // Fan Curve
        // fanCurve is array of 10 fan speed values (0-100%) for temperature points
        if (fanCurve is not null && _fanCurveCardControl.Visibility == Visibility.Visible)
        {
            try
            {
                var state = await _godModeController.GetStateAsync();
                var preset = state.Presets[state.ActivePresetId];
                var data = preset.FanTableInfo?.Data;

                if (data is not null)
                {
                    var fanTable = new FanTable(fanCurve);
                    var fanTableInfo = new FanTableInfo(data, fanTable);
                    var minimum = await _godModeController.GetMinimumFanTableAsync();
                    _fanCurveControl.SetFanTableInfo(fanTableInfo, minimum);
                }
            }
            catch (Exception ex)
            {
                Log.Instance.Trace($"[QuickTemplate] Failed to set fan curve: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// Clamps a value to the slider's min/max range
    /// </summary>
    /// <summary>
    /// Clamps a value to the slider's min/max range
    /// </summary>
    private static int ClampToSlider(Controls.Dashboard.GodMode.GodModeValueControl control, int value)
    {
        // Clamp to the control's actual min/max to avoid it resetting to default
        var min = (int)control.Minimum;
        var max = (int)control.Maximum;
        
        // If min/max are 0, it might mean not initialized or combobox, but we handle sliders mostly.
        if (min == 0 && max == 0) return value;

        return Math.Clamp(value, min, max);
    }

    private void QuickTemplatesButton_Click(object sender, RoutedEventArgs e)
    {
        var contextMenu = new ContextMenu
        {
            PlacementTarget = _quickTemplatesButton,
            Placement = PlacementMode.Bottom,
        };

        var silentItem = new MenuItem { Header = "🔇 Silent Surf (15W)" };
        silentItem.Click += QuickTemplateSilentSurf_Click;
        contextMenu.Items.Add(silentItem);

        var dailyItem = new MenuItem { Header = "💼 Daily Work (35W)" };
        dailyItem.Click += QuickTemplateDailyWork_Click;
        contextMenu.Items.Add(dailyItem);

        var gamingItem = new MenuItem { Header = "🎮 Gaming Cool (54W)" };
        gamingItem.Click += QuickTemplateGamingCool_Click;
        contextMenu.Items.Add(gamingItem);

        _quickTemplatesButton.ContextMenu = contextMenu;
        _quickTemplatesButton.ContextMenu.IsOpen = true;
    }

    // P/Invoke for Turbo Boost control
    [System.Runtime.InteropServices.DllImport("powrprof.dll")]
    private static extern uint PowerWriteACValueIndex(
        IntPtr RootPowerKey,
        ref Guid SchemeGuid,
        ref Guid SubGroupOfPowerSettingsGuid,
        ref Guid PowerSettingGuid,
        uint AcValueIndex);

    [System.Runtime.InteropServices.DllImport("powrprof.dll")]
    private static extern uint PowerGetActiveScheme(IntPtr UserRootPowerKey, out IntPtr ActivePolicyGuid);

    [System.Runtime.InteropServices.DllImport("powrprof.dll")]
    private static extern uint PowerSetActiveScheme(IntPtr UserRootPowerKey, ref Guid SchemeGuid);

    [System.Runtime.InteropServices.DllImport("kernel32.dll")]
    private static extern IntPtr LocalFree(IntPtr hMem);

    private static readonly Guid GUID_PROCESSOR_SETTINGS = new("54533251-82be-4824-96c1-47b60b740d00");
    private static readonly Guid GUID_PERFBOOSTMODE = new("be337238-0d82-4146-a960-4f3749d470c7");

    /// <summary>
    /// Sets Turbo Boost mode: 0=Disabled, 1=Enabled, 2=Aggressive, 3=EfficientEnabled, 4=EfficientAggressive
    /// </summary>
    private static void SetTurboBoostMode(uint mode)
    {
        try
        {
            if (PowerGetActiveScheme(IntPtr.Zero, out IntPtr activePolicyGuidPtr) != 0) return;
            
            try
            {
                var schemeGuid = System.Runtime.InteropServices.Marshal.PtrToStructure<Guid>(activePolicyGuidPtr);
                var subGroup = GUID_PROCESSOR_SETTINGS;
                var setting = GUID_PERFBOOSTMODE;

                // Set value
                PowerWriteACValueIndex(IntPtr.Zero, ref schemeGuid, ref subGroup, ref setting, mode);
                // Apply changes
                PowerSetActiveScheme(IntPtr.Zero, ref schemeGuid);
                
                Log.Instance.Trace($"[GodModeSettings] Set Turbo Boost mode to {mode}");
            }
            finally
            {
                LocalFree(activePolicyGuidPtr);
            }
        }
        catch (Exception ex)
        {
            Log.Instance.Trace($"[GodModeSettings] Failed to set Turbo Boost: {ex.Message}");
        }
    }

    #endregion
}