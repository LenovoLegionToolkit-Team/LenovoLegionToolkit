using System;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using LenovoLegionToolkit.Lib;
using LenovoLegionToolkit.Lib.Extensions;
using LenovoLegionToolkit.Lib.Features;
using LenovoLegionToolkit.Lib.Settings;
using LenovoLegionToolkit.Lib.System.Management;
using LenovoLegionToolkit.Lib.Utils;
using LenovoLegionToolkit.WPF.Extensions;
using LenovoLegionToolkit.WPF.Resources;
using LenovoLegionToolkit.WPF.Windows.Settings;

namespace LenovoLegionToolkit.WPF.Controls.Settings;

public partial class SettingsPowerControl
{
    private readonly ApplicationSettings _settings = IoCContainer.Resolve<ApplicationSettings>();
    private readonly PowerModeFeature _powerModeFeature = IoCContainer.Resolve<PowerModeFeature>();
    private readonly ITSModeFeature _itsModeFeature = IoCContainer.Resolve<ITSModeFeature>();

    private bool _isRefreshing;

    public SettingsPowerControl()
    {
        InitializeComponent();
    }

    public async Task RefreshAsync()
    {
        _isRefreshing = true;

        try
        {
            var mi = await Compatibility.GetMachineInformationAsync();
            if (mi.Features[CapabilityID.GodModeFnQSwitchable])
            {
                _godModeFnQSwitchableCard.Visibility = Visibility.Visible;
                _godModeFnQSwitchableToggle.IsChecked = await WMI.LenovoOtherMethod.GetFeatureValueAsync(CapabilityID.GodModeFnQSwitchable) == 1;
            }
            else
            {
                _godModeFnQSwitchableCard.Visibility = Visibility.Collapsed;
            }
        }
        catch (Exception ex)
        {
            _godModeFnQSwitchableCard.Visibility = Visibility.Collapsed;

            Log.Instance.Trace($"Failed to get GodModeFnQSwitchable status.", ex);
        }

        _powerModeMappingComboBox.SetItems(Enum.GetValues<PowerModeMappingMode>(), _settings.Store.PowerModeMappingMode, t => t.GetDisplayName());

        var isPowerModeFeatureSupported = await _powerModeFeature.IsSupportedAsync();
        var isITSModeFeatureSupported = await _itsModeFeature.IsSupportedAsync();
        var isAnyPowerFeatureSupported = isPowerModeFeatureSupported || isITSModeFeatureSupported;
        _powerModeMappingCard.Visibility = isAnyPowerFeatureSupported ? Visibility.Visible : Visibility.Collapsed;
        _powerModesCard.Visibility = _settings.Store.PowerModeMappingMode == PowerModeMappingMode.WindowsPowerMode && isAnyPowerFeatureSupported ? Visibility.Visible : Visibility.Collapsed;
        _windowsPowerPlansCard.Visibility = _settings.Store.PowerModeMappingMode == PowerModeMappingMode.WindowsPowerPlan && isAnyPowerFeatureSupported ? Visibility.Visible : Visibility.Collapsed;
        _windowsPowerPlansControlPanelCard.Visibility = _settings.Store.PowerModeMappingMode == PowerModeMappingMode.WindowsPowerPlan && isAnyPowerFeatureSupported ? Visibility.Visible : Visibility.Collapsed;

        if (isITSModeFeatureSupported && _settings.Store.PowerModeMappingMode != PowerModeMappingMode.Disabled)
            _powerModeMappingCardHeader.Warning = Resource.SettingsPage_PowerModeMapping_ITSWarning;
        else
            _powerModeMappingCardHeader.Warning = string.Empty;

        _onBatterySinceResetToggle.IsChecked = _settings.Store.ResetBatteryOnSinceTimerOnReboot;
        _onBatterySinceResetToggle.Visibility = Visibility.Visible;

        _godModeFnQSwitchableToggle.Visibility = Visibility.Visible;
        _powerModeMappingComboBox.Visibility = Visibility.Visible;

        _isRefreshing = false;
    }

    private async void GodModeFnQSwitchableToggle_Click(object sender, RoutedEventArgs e)
    {
        if (_isRefreshing)
            return;

        var state = _godModeFnQSwitchableToggle.IsChecked;
        if (state is null)
            return;

        _godModeFnQSwitchableToggle.IsEnabled = false;

        await WMI.LenovoOtherMethod.SetFeatureValueAsync(CapabilityID.GodModeFnQSwitchable, state.Value ? 1 : 0);

        _godModeFnQSwitchableToggle.IsEnabled = true;
    }

    private async void PowerModeMappingComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isRefreshing)
            return;

        if (!_powerModeMappingComboBox.TryGetSelectedItem(out PowerModeMappingMode powerModeMappingMode))
            return;

        _settings.Store.PowerModeMappingMode = powerModeMappingMode;
        _settings.SynchronizeStore();

        var isPowerModeFeatureSupported = await _powerModeFeature.IsSupportedAsync();
        var isITSModeFeatureSupported = await _itsModeFeature.IsSupportedAsync();
        var isAnyPowerFeatureSupported = isPowerModeFeatureSupported || isITSModeFeatureSupported;
        _powerModesCard.Visibility = _settings.Store.PowerModeMappingMode == PowerModeMappingMode.WindowsPowerMode && isAnyPowerFeatureSupported ? Visibility.Visible : Visibility.Collapsed;
        _windowsPowerPlansCard.Visibility = _settings.Store.PowerModeMappingMode == PowerModeMappingMode.WindowsPowerPlan && isAnyPowerFeatureSupported ? Visibility.Visible : Visibility.Collapsed;
        _windowsPowerPlansControlPanelCard.Visibility = _settings.Store.PowerModeMappingMode == PowerModeMappingMode.WindowsPowerPlan && isAnyPowerFeatureSupported ? Visibility.Visible : Visibility.Collapsed;

        if (isITSModeFeatureSupported && powerModeMappingMode != PowerModeMappingMode.Disabled)
            _powerModeMappingCardHeader.Warning = Resource.SettingsPage_PowerModeMapping_ITSWarning;
        else
            _powerModeMappingCardHeader.Warning = string.Empty;

        if (powerModeMappingMode != PowerModeMappingMode.Disabled)
            await _powerModeFeature.EnsureCorrectWindowsPowerSettingsAreSetAsync();
    }

    private void WindowsPowerPlans_Click(object sender, RoutedEventArgs e)
    {
        if (_isRefreshing)
            return;

        var window = new WindowsPowerPlansWindow { Owner = Window.GetWindow(this) };
        window.ShowDialog();
    }

    private void PowerModes_Click(object sender, RoutedEventArgs e)
    {
        if (_isRefreshing)
            return;

        var window = new WindowsPowerModesWindow { Owner = Window.GetWindow(this) };
        window.ShowDialog();
    }

    private void WindowsPowerPlansControlPanel_Click(object sender, RoutedEventArgs e)
    {
        Process.Start("control", "/name Microsoft.PowerOptions");
    }

    private void OnBatterySinceResetToggle_Click(object sender, RoutedEventArgs e)
    {
        if (_isRefreshing)
            return;

        var state = _onBatterySinceResetToggle.IsChecked;
        if (state is null)
            return;

        _settings.Store.ResetBatteryOnSinceTimerOnReboot = state.Value;
        _settings.SynchronizeStore();
    }
}
