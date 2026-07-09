using LenovoLegionToolkit.Lib;
using LenovoLegionToolkit.Lib.Controllers.GodMode;
using LenovoLegionToolkit.Lib.Extensions;
using LenovoLegionToolkit.Lib.Features;
using LenovoLegionToolkit.Lib.Settings;
using LenovoLegionToolkit.Lib.SoftwareDisabler;
using LenovoLegionToolkit.Lib.System.Management;
using LenovoLegionToolkit.Lib.Utils;
using LenovoLegionToolkit.WPF.Controls;
using LenovoLegionToolkit.WPF.Extensions;
using LenovoLegionToolkit.WPF.Resources;
using LenovoLegionToolkit.WPF.Utils;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Management;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;

namespace LenovoLegionToolkit.WPF.Windows.Dashboard;

public partial class GodModeSettingsWindow
{
    private readonly PowerModeFeature _powerModeFeature = IoCContainer.Resolve<PowerModeFeature>();
    private readonly GodModeController _godModeController = IoCContainer.Resolve<GodModeController>();
    private readonly VantageDisabler _vantageDisabler = IoCContainer.Resolve<VantageDisabler>();
    private readonly LegionSpaceDisabler _legionSpaceDisabler = IoCContainer.Resolve<LegionSpaceDisabler>();
    private readonly LegionZoneDisabler _legionZoneDisabler = IoCContainer.Resolve<LegionZoneDisabler>();

    private Control? FanControl;

    private GodModeState? _state;
    private Dictionary<PowerModeState, GodModeDefaults>? _defaults;
    private bool _isRefreshing;

    private const int BIOS_OC_MODE_ENABLED = 3;

    public GodModeSettingsWindow()
    {
        InitializeComponent();
        IsVisibleChanged += GodModeSettingsWindow_IsVisibleChanged;
    }

    private Control InitializeFanControlContainer(MachineInformation mi)
    {
        int contentIndex = _fanCurveControlStackPanel.Children.IndexOf(_fanCurveButton);
        Control ctrl = mi.SmartFanVersion >= 6
            ? new Controls.FanCurveControlV2()
            : new Controls.FanCurveControl();
        _fanCurveControlStackPanel.Children.Insert(contentIndex, ctrl);

        return ctrl;
    }

    private async void GodModeSettingsWindow_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (IsVisible)
        {
            await RefreshAsync();
        }
    }

    private async Task RefreshAsync()
    {
        if (_isRefreshing)
        {
            return;
        }
        _isRefreshing = true;

        try
        {
            _loader.IsLoading = true;
            _buttonsStackPanel.Visibility = Visibility.Hidden;

            if (FanControl is null)
            {
                var mi = await Compatibility.GetMachineInformationAsync().ConfigureAwait(true);
                FanControl = InitializeFanControlContainer(mi);
            }
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
            _capabilityWarningInfoBar.IsOpen = _godModeController.HasCapabilityErrors();

            if (_state is null || _defaults is null)
            {
                throw new InvalidOperationException("Failed to load state or defaults.");
            }

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
            if (!_state.HasValue)
            {
                throw new InvalidOperationException("State is null");
            }

            var activePresetId = _state.Value.ActivePresetId;
            var preset = _state.Value.Presets[activePresetId];

            FanTableInfo? fanInfo = FanControl switch
            {
                Controls.FanCurveControl v1 => v1.GetFanTableInfo(),
                Controls.FanCurveControlV2 v2 => v2.GetFanTableInfo(),
                _ => preset.FanTableInfo
            };

            var newPreset = new GodModePreset
            {
                Name = preset.Name,
                CPULongTermPowerLimit = preset.CPULongTermPowerLimit?.WithValue(_cpuLongTermPowerLimitControl.Value),
                CPUShortTermPowerLimit = preset.CPUShortTermPowerLimit?.WithValue(_cpuShortTermPowerLimitControl.Value),
                CPUPeakPowerLimit = preset.CPUPeakPowerLimit?.WithValue(_cpuPeakPowerLimitControl.Value),
                CPUCrossLoadingPowerLimit = preset.CPUCrossLoadingPowerLimit?.WithValue(_cpuCrossLoadingLimitControl.Value),
                CPUPL2Tau = preset.CPUPL2Tau?.WithValue(_cpuPL2TauControl.Value),
                APUsPPTPowerLimit = preset.APUsPPTPowerLimit?.WithValue(_apuSPPTPowerLimitControl.Value),
                CPUTemperatureLimit = preset.CPUTemperatureLimit?.WithValue(_cpuTemperatureLimitControl.Value),
                GPUPowerBoost = preset.GPUPowerBoost?.WithValue(_gpuPowerBoostControl.Value),
                GPUConfigurableTGP = preset.GPUConfigurableTGP?.WithValue(_gpuConfigurableTGPControl.Value),
                GPUTemperatureLimit = preset.GPUTemperatureLimit?.WithValue(_gpuTemperatureLimitControl.Value),
                GPUTotalProcessingPowerTargetOnAcOffsetFromBaseline = preset.GPUTotalProcessingPowerTargetOnAcOffsetFromBaseline?.WithValue(_gpuTotalProcessingPowerTargetOnAcOffsetFromBaselineControl.Value),
                GPUToCPUDynamicBoost = preset.GPUToCPUDynamicBoost?.WithValue(_gpuToCpuDynamicBoostControl.Value),
                FanFullSpeed = preset.FanFullSpeed is not null ? _fanFullSpeedToggle.IsChecked : null,
                MaxValueOffset = preset.MaxValueOffset is not null ? (int?)_maxValueOffsetNumberBox.Value : null,
                MinValueOffset = preset.MinValueOffset is not null ? (int?)_minValueOffsetNumberBox.Value : null,
                PrecisionBoostOverdriveScaler = preset.PrecisionBoostOverdriveScaler?.WithValue(_cpuPrecisionBoostOverdriveScaler.Value),
                PrecisionBoostOverdriveBoostFrequency = preset.PrecisionBoostOverdriveBoostFrequency?.WithValue(_cpuPrecisionBoostOverdriveBoostFrequency.Value),
                AllCoreCurveOptimizer = preset.AllCoreCurveOptimizer?.WithValue(_cpuAllCoreCurveOptimizer.Value),
                EnableAllCoreCurveOptimizer = _toggleCoreCurveCard.Visibility == Visibility.Visible ? _coreCurveToggle.IsChecked : preset.EnableAllCoreCurveOptimizer,
                EnableOverclocking = _toggleOcCard.Visibility == Visibility.Visible ? _overclockingToggle.IsChecked : preset.EnableOverclocking,
                FanTableInfo = fanInfo,
                Overrides = preset.Overrides,
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

            var mi = await Compatibility.GetMachineInformationAsync();
            if (mi.Properties.GodModePlatform != GodModePlatform.NonGaming && await _powerModeFeature.GetStateAsync() != PowerModeState.GodMode)
            {
                await _powerModeFeature.SetStateAsync(PowerModeState.GodMode);
            }

            await _godModeController.SetStateAsync(newState);
            await _godModeController.ApplyStateAsync();

            if (_fnqTabItem.Visibility == Visibility.Visible)
            {
                _fnqLoopControl.Save();
            }

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
            _cpuPL2TauControl.Set(preset.CPUPL2Tau);
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
                FanTable minimum = await _godModeController.GetMinimumFanTableAsync();

                if (FanControl is Controls.FanCurveControl v1)
                {
                    v1.SetFanTableInfo(preset.FanTableInfo.Value, minimum);
                }
                else if (FanControl is Controls.FanCurveControlV2 v2)
                {
                    v2.SetFanTableInfo(preset.FanTableInfo.Value, minimum);
                }

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

            bool cpuVisible = _cpuLongTermPowerLimitControl.Visibility == Visibility.Visible || _cpuShortTermPowerLimitControl.Visibility == Visibility.Visible || _cpuPeakPowerLimitControl.Visibility == Visibility.Visible || _cpuCrossLoadingLimitControl.Visibility == Visibility.Visible || _cpuPL2TauControl.Visibility == Visibility.Visible || _apuSPPTPowerLimitControl.Visibility == Visibility.Visible || _cpuTemperatureLimitControl.Visibility == Visibility.Visible;
            bool gpuVisible = _gpuPowerBoostControl.Visibility == Visibility.Visible || _gpuConfigurableTGPControl.Visibility == Visibility.Visible || _gpuTemperatureLimitControl.Visibility == Visibility.Visible || _gpuTotalProcessingPowerTargetOnAcOffsetFromBaselineControl.Visibility == Visibility.Visible || _gpuToCpuDynamicBoostControl.Visibility == Visibility.Visible;
            bool fanVisible = _fanCurveCardControl.Visibility == Visibility.Visible || _fanFullSpeedCardControl.Visibility == Visibility.Visible;
            bool advVisible = _maxValueOffsetCardControl.Visibility == Visibility.Visible || _minValueOffsetCardControl.Visibility == Visibility.Visible;

            _cpuTabItem.Visibility = cpuVisible ? Visibility.Visible : Visibility.Collapsed;
            _gpuTabItem.Visibility = gpuVisible ? Visibility.Visible : Visibility.Collapsed;
            _fanTabItem.Visibility = fanVisible ? Visibility.Visible : Visibility.Collapsed;
            _advancedTabItem.Visibility = advVisible ? Visibility.Visible : Visibility.Collapsed;

            await UpdateFnQTabVisibilityAsync();
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
        var isBiosOcEnabled = await IsBiosOcEnabledAsync();
        var pboVisible = isBiosOcEnabled && mi.Properties.IsAmdDevice;
        _pboTabItem.Visibility = pboVisible ? Visibility.Visible : Visibility.Collapsed;
        _cpuPrecisionBoostOverdriveScaler.Visibility = pboVisible && _overclockingToggle.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        _cpuPrecisionBoostOverdriveBoostFrequency.Visibility = pboVisible && _overclockingToggle.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        _toggleCoreCurveCard.Visibility = pboVisible && _overclockingToggle.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        await UpdateCoreCurveVisibilityAsync();
    }

    private async Task UpdateCoreCurveVisibilityAsync()
    {
        bool isCurveEnabled = _toggleCoreCurveCard.Visibility == Visibility.Visible && _coreCurveToggle.IsChecked == true;
        _cpuAllCoreCurveOptimizer.Visibility = isCurveEnabled ? Visibility.Visible : Visibility.Collapsed;
        await Task.CompletedTask;
    }

    private async void SetDefaults(GodModeDefaults defaults)
    {
        SetVal(_cpuLongTermPowerLimitControl, defaults.CPULongTermPowerLimit, v => { _cpuLongTermPowerLimitControl.Value = v; });
        SetVal(_cpuShortTermPowerLimitControl, defaults.CPUShortTermPowerLimit, v => { _cpuShortTermPowerLimitControl.Value = v; });
        SetVal(_cpuPeakPowerLimitControl, defaults.CPUPeakPowerLimit, v => { _cpuPeakPowerLimitControl.Value = v; });
        SetVal(_cpuCrossLoadingLimitControl, defaults.CPUCrossLoadingPowerLimit, v => { _cpuCrossLoadingLimitControl.Value = v; });
        SetVal(_cpuPL2TauControl, defaults.CPUPL2Tau, v => { _cpuPL2TauControl.Value = v; });
        SetVal(_apuSPPTPowerLimitControl, defaults.APUsPPTPowerLimit, v => { _apuSPPTPowerLimitControl.Value = v; });
        SetVal(_cpuTemperatureLimitControl, defaults.CPUTemperatureLimit, v => { _cpuTemperatureLimitControl.Value = v; });
        SetVal(_cpuPrecisionBoostOverdriveScaler, defaults.PrecisionBoostOverdriveScaler, v => { _cpuPrecisionBoostOverdriveScaler.Value = v; });
        SetVal(_cpuPrecisionBoostOverdriveBoostFrequency, defaults.PrecisionBoostOverdriveBoostFrequency, v => { _cpuPrecisionBoostOverdriveBoostFrequency.Value = v; });
        SetVal(_cpuAllCoreCurveOptimizer, defaults.AllCoreCurveOptimizer, v => { _cpuAllCoreCurveOptimizer.Value = v; });
        SetVal(_overclockingToggle, defaults.EnableOverclocking, v =>
        {
            _overclockingToggle.IsChecked = v;
            _ = UpdateOverclockingVisibilityAsync();
        });
        SetVal(_coreCurveToggle, defaults.EnableAllCoreCurveOptimizer, v => { _coreCurveToggle.IsChecked = v; });
        SetVal(_gpuPowerBoostControl, defaults.GPUPowerBoost, v => { _gpuPowerBoostControl.Value = v; });
        SetVal(_gpuConfigurableTGPControl, defaults.GPUConfigurableTGP, v => { _gpuConfigurableTGPControl.Value = v; });
        SetVal(_gpuTemperatureLimitControl, defaults.GPUTemperatureLimit, v => { _gpuTemperatureLimitControl.Value = v; });
        SetVal(_gpuTotalProcessingPowerTargetOnAcOffsetFromBaselineControl, defaults.GPUTotalProcessingPowerTargetOnAcOffsetFromBaseline, v => { _gpuTotalProcessingPowerTargetOnAcOffsetFromBaselineControl.Value = v; });
        SetVal(_gpuToCpuDynamicBoostControl, defaults.GPUToCPUDynamicBoost, v => { _gpuToCpuDynamicBoostControl.Value = v; });

        if (_fanCurveCardControl.Visibility == Visibility.Visible && defaults.FanTable is { } fanTable)
        {
            var state = await _godModeController.GetStateAsync();
            var preset = state.Presets[state.ActivePresetId];
            if (preset.FanTableInfo?.Data is { } data)
            {
                FanTableInfo defaultTableInfo = new FanTableInfo(data, fanTable);
                FanTable minimum = await _godModeController.GetMinimumFanTableAsync();

                if (FanControl is Controls.FanCurveControl v1)
                {
                    v1.SetFanTableInfo(defaultTableInfo, minimum);
                }
                else if (FanControl is Controls.FanCurveControlV2 v2)
                {
                    v2.SetFanTableInfo(defaultTableInfo, minimum);
                }
            }
        }

        if (_fanFullSpeedCardControl.Visibility == Visibility.Visible && defaults.FanFullSpeed is { } ffs)
        {
            _fanFullSpeedToggle.IsChecked = ffs;
        }
        if (_maxValueOffsetCardControl.Visibility == Visibility.Visible)
        {
            _maxValueOffsetNumberBox.Text = "0";
        }
        if (_minValueOffsetCardControl.Visibility == Visibility.Visible)
        {
            _minValueOffsetNumberBox.Text = "0";
        }
    }

    private async void PresetsComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_state.HasValue)
        {
            return;
        }
        if (!_presetsComboBox.TryGetSelectedItem<KeyValuePair<Guid, GodModePreset>>(out var item))
        {
            return;
        }
        if (_state.Value.ActivePresetId == item.Key)
        {
            return;
        }
        _state = _state.Value with { ActivePresetId = item.Key };
        await SetStateAsync(_state.Value);
    }

    private async void EditPresetsButton_Click(object sender, RoutedEventArgs e)
    {
        if (!_state.HasValue)
        {
            return;
        }
        var activeId = _state.Value.ActivePresetId;
        var presets = _state.Value.Presets;
        var result = await MessageBoxHelper.ShowInputAsync(this, Resource.GodModeSettingsWindow_EditPreset_Title, Resource.GodModeSettingsWindow_EditPreset_Message, presets[activeId].Name);
        if (string.IsNullOrEmpty(result))
        {
            return;
        }
        var newPresets = new Dictionary<Guid, GodModePreset>(presets) { [activeId] = presets[activeId] with { Name = result } };
        _state = _state.Value with { Presets = newPresets.AsReadOnlyDictionary() };
        await SetStateAsync(_state.Value);
    }

    private async void DeletePresetsButton_Click(object sender, RoutedEventArgs e)
    {
        if (!_state.HasValue || _state.Value.Presets.Count <= 1)
        {
            return;
        }
        var newPresets = new Dictionary<Guid, GodModePreset>(_state.Value.Presets);
        newPresets.Remove(_state.Value.ActivePresetId);
        _state = new GodModeState
        {
            ActivePresetId = newPresets.OrderBy(kv => kv.Value.Name).First().Key,
            Presets = newPresets.AsReadOnlyDictionary()
        };
        await SetStateAsync(_state.Value);
    }

    private async void AddPresetsButton_Click(object sender, RoutedEventArgs e)
    {
        if (!_state.HasValue)
        {
            return;
        }
        var result = await MessageBoxHelper.ShowInputAsync(this, Resource.GodModeSettingsWindow_EditPreset_Title, Resource.GodModeSettingsWindow_EditPreset_Message);
        if (string.IsNullOrEmpty(result))
        {
            return;
        }
        var newId = Guid.NewGuid();
        var newPresets = new Dictionary<Guid, GodModePreset>(_state.Value.Presets) { [newId] = _state.Value.Presets[_state.Value.ActivePresetId] with { Name = result } };
        _state = new GodModeState
        {
            ActivePresetId = newId,
            Presets = newPresets.AsReadOnlyDictionary()
        };
        await SetStateAsync(_state.Value);
    }

    private async void DefaultFanCurve_Click(object sender, RoutedEventArgs e)
    {
        var state = await _godModeController.GetStateAsync();
        var preset = state.Presets[state.ActivePresetId];
        if (preset.FanTableInfo?.Data is not { } data)
        {
            return;
        }
        var defaultInfo = new FanTableInfo(data, await _godModeController.GetDefaultFanTableAsync());
        var minimum = await _godModeController.GetMinimumFanTableAsync();

        if (FanControl is Controls.FanCurveControl v1)
        {
            v1.SetFanTableInfo(defaultInfo, minimum);
        }
        else if (FanControl is Controls.FanCurveControlV2 v2)
        {
            v2.SetFanTableInfo(defaultInfo, minimum);
        }
    }

    private void LoadButton_Click(object sender, RoutedEventArgs e)
    {
        if (_defaults is null || _defaults.IsEmpty())
        {
            _loadButton.Visibility = Visibility.Collapsed;
            return;
        }
        var menu = new ContextMenu
        {
            PlacementTarget = _loadButton,
            Placement = PlacementMode.Bottom
        };
        foreach (var d in _defaults.OrderBy(x => x.Key))
        {
            var item = new MenuItem { Header = d.Key.GetDisplayName() };
            item.Click += (_, _) => { SetDefaults(d.Value); };
            menu.Items.Add(item);
        }
        _loadButton.ContextMenu = menu;
        _loadButton.ContextMenu.IsOpen = true;
    }

    private async void SaveAndCloseButton_Click(object sender, RoutedEventArgs e)
    {
        if (await ApplyAsync())
        {
            Close();
        }
    }

    private async void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        await ApplyAsync();
        await RefreshAsync();
    }

    private void CpuLongTermPowerLimitSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!_isRefreshing && _cpuLongTermPowerLimitControl.Value > _cpuShortTermPowerLimitControl.Value)
        {
            _cpuShortTermPowerLimitControl.Value = _cpuLongTermPowerLimitControl.Value;
        }
    }

    private void CpuShortTermPowerLimitSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!_isRefreshing && _cpuLongTermPowerLimitControl.Value > _cpuShortTermPowerLimitControl.Value)
        {
            _cpuShortTermPowerLimitControl.Value = _cpuLongTermPowerLimitControl.Value;
        }
    }

    private async void FanFullSpeedToggle_Click(object sender, RoutedEventArgs e)
    {
        bool enabled = _fanFullSpeedToggle.IsChecked ?? false;
        _fanCurveCardControl.IsEnabled = !enabled;
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
        if (control.Visibility == Visibility.Visible && value.HasValue)
        {
            setter(value.Value);
        }
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

    private async Task UpdateFnQTabVisibilityAsync()
    {
        var mi = await Compatibility.GetMachineInformationAsync();
        _fnqTabItem.Visibility = mi.Properties.GodModePlatform == GodModePlatform.NonGaming
            ? Visibility.Visible
            : Visibility.Collapsed;
    }
}