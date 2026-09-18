using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using LenovoLegionToolkit.Lib;
using LenovoLegionToolkit.Lib.Controllers;
using LenovoLegionToolkit.WPF.Resources;
using LenovoLegionToolkit.WPF.Utils;

namespace LenovoLegionToolkit.WPF.Windows.Dashboard;

public partial class OverclockDiscreteGPUSettingsWindow
{

    private readonly GPUOverclockController _gpuOverclockController = IoCContainer.Resolve<GPUOverclockController>();

    public OverclockDiscreteGPUSettingsWindow()
    {
        InitializeComponent();

        var (enabled, info) = _gpuOverclockController.GetState();

        _applyCloseGrid.Visibility = enabled ? Visibility.Visible : Visibility.Collapsed;
        _saveGrid.Visibility = enabled ? Visibility.Collapsed : Visibility.Visible;
        _contentGrid.IsEnabled = false;

        Loaded += async (_, _) => await PopulateAsync(info);
    }

    private async Task PopulateAsync(GPUOverclockInfo info)
    {
        var ((minCoreDeltaMhz, maxCoreDeltaMhz), (minMemoryDeltaMhz, maxMemoryDeltaMhz), (minVoltageMv, maxVoltageMv)) =
            await Task.Run(_gpuOverclockController.GetRanges);

        _coreSlider.Minimum = minCoreDeltaMhz;
        _coreSlider.Maximum = maxCoreDeltaMhz;
        _coreSlider.Value = Math.Clamp(info.CoreDeltaMhz, minCoreDeltaMhz, maxCoreDeltaMhz);

        _memorySlider.Minimum = minMemoryDeltaMhz;
        _memorySlider.Maximum = maxMemoryDeltaMhz;
        _memorySlider.Value = Math.Clamp(info.MemoryDeltaMhz, minMemoryDeltaMhz, maxMemoryDeltaMhz);

        _voltageCapSlider.Minimum = minVoltageMv - _voltageCapSlider.TickFrequency;
        _voltageCapSlider.Maximum = maxVoltageMv;
        _voltageCapSlider.Value = info.VoltageCapMv >= minVoltageMv
            ? Math.Min(info.VoltageCapMv, maxVoltageMv)
            : _voltageCapSlider.Minimum;

        _voltageLockSlider.Minimum = minVoltageMv - _voltageLockSlider.TickFrequency;
        _voltageLockSlider.Maximum = maxVoltageMv;
        _voltageLockSlider.Value = info.VoltageLockMv >= minVoltageMv
            ? Math.Min(info.VoltageLockMv, maxVoltageMv)
            : _voltageLockSlider.Minimum;

        _coreLabel.Content = $"{(int)_coreSlider.Value:+0;-0;0} {Resource.MHz}";
        _memoryLabel.Content = $"{(int)_memorySlider.Value:+0;-0;0} {Resource.MHz}";
        _voltageCapLabel.Content = _voltageCapSlider.Value <= _voltageCapSlider.Minimum ? Resource.Off : $"{(int)_voltageCapSlider.Value} {Resource.mV}";
        _voltageLockLabel.Content = _voltageLockSlider.Value <= _voltageLockSlider.Minimum ? Resource.Off : $"{(int)_voltageLockSlider.Value} {Resource.mV}";

        _contentGrid.IsEnabled = true;
    }

    private void CoreSlider_OnValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_coreLabel != null && _coreSlider != null)
            _coreLabel.Content = $"{(int)_coreSlider.Value:+0;-0;0} {Resource.MHz}";
    }

    private void MemorySlider_OnValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_memoryLabel != null && _memorySlider != null)
            _memoryLabel.Content = $"{(int)_memorySlider.Value:+0;-0;0} {Resource.MHz}";
    }

    private void VoltageCapSlider_OnValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_voltageCapLabel == null || _voltageCapSlider == null) return;

        if (_voltageCapSlider.Value <= _voltageCapSlider.Minimum)
        {
            _voltageCapLabel.Content = Resource.Off;
        }
        else
        {
            _voltageCapLabel.Content = $"{(int)_voltageCapSlider.Value} {Resource.mV}";
            if (_voltageLockSlider != null && _voltageLockSlider.Value > _voltageLockSlider.Minimum)
            {
                _voltageLockSlider.Value = _voltageLockSlider.Minimum;
            }
        }
    }

    private void VoltageLockSlider_OnValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_voltageLockLabel == null || _voltageLockSlider == null) return;

        if (_voltageLockSlider.Value <= _voltageLockSlider.Minimum)
        {
            _voltageLockLabel.Content = Resource.Off;
        }
        else
        {
            _voltageLockLabel.Content = $"{(int)_voltageLockSlider.Value} {Resource.mV}";
            if (_voltageCapSlider != null && _voltageCapSlider.Value > _voltageCapSlider.Minimum)
            {
                _voltageCapSlider.Value = _voltageCapSlider.Minimum;
            }
        }
    }

    private async void ApplyButton_Click(object sender, RoutedEventArgs e)
    {
        Save();
        ShowApplyResult(await ApplyAsync());
    }

    private async void ApplyAndCloseButton_Click(object sender, RoutedEventArgs e)
    {
        Save();
        ShowApplyResult(await ApplyAsync());
        Close();
    }

    private static void ShowApplyResult(bool applied)
    {
        var message = applied ? Resource.Snackbar_SettingsApplied_Message : Resource.Snackbar_SettingsNotApplied_Message;
        var type = applied ? SnackbarType.Success : SnackbarType.Warning;

        SnackbarHelper.Show(Resource.OverclockDiscreteGPUSettingsWindow_Title, message, type);
    }

    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        Save();
        SnackbarHelper.Show(Resource.OverclockDiscreteGPUSettingsWindow_Title, Resource.Snackbar_SettingsApplied_Message, SnackbarType.Success);
        Close();
    }

    private void Save()
    {
        var (enabled, _) = _gpuOverclockController.GetState();
        int voltageLock = _voltageLockSlider.Value <= _voltageLockSlider.Minimum ? 0 : (int)_voltageLockSlider.Value;
        int voltageCap = _voltageCapSlider.Value <= _voltageCapSlider.Minimum ? 0 : (int)_voltageCapSlider.Value;
        var info = new GPUOverclockInfo((int)_coreSlider.Value, (int)_memorySlider.Value, voltageLock, voltageCap);

        _gpuOverclockController.SaveState(enabled, info);
    }

    private async Task<bool> ApplyAsync() => await _gpuOverclockController.ApplyStateAsync();
}
