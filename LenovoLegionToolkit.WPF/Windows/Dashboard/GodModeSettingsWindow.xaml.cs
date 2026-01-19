using System;
using System.Collections.Generic;
using System.Linq;
using System.Management;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using LenovoLegionToolkit.Lib;
using LenovoLegionToolkit.Lib.Controllers.GodMode;
using LenovoLegionToolkit.Lib.Extensions;
using LenovoLegionToolkit.Lib.Features;
using LenovoLegionToolkit.Lib.SoftwareDisabler;
using LenovoLegionToolkit.Lib.System.Management;
using LenovoLegionToolkit.Lib.Utils;
using LenovoLegionToolkit.WPF.Extensions;
using LenovoLegionToolkit.WPF.Resources;
using LenovoLegionToolkit.WPF.Utils;

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
        }
        catch (Exception ex)
        {
            Log.Instance.Trace($"Couldn't load settings.", ex);
            await _snackBar.ShowAsync(Resource.GodModeSettingsWindow_Error_Load_Title, ex.Message);
            Close();
        }
        finally
        {
            _isRefreshing = false;
        }
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
}