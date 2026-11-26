using System;
using System.Threading.Tasks;
using System.Globalization;
using System.Windows.Controls;
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

        UpdateValueTextBoxes();

        _contentGrid.IsEnabled = true;
    }

    private void CoreSlider_OnValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e) =>
        UpdateValueTextBoxes();

    private void MemorySlider_OnValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e) =>
        UpdateValueTextBoxes();

    private void UpdateValueTextBoxes()
    {
        if (_coreValue != null && _coreSlider != null)
            _coreValue.Text = ((int)_coreSlider.Value).ToString("+0;-0;0", CultureInfo.CurrentCulture);
        if (_memoryValue != null && _memorySlider != null)
            _memoryValue.Text = ((int)_memorySlider.Value).ToString("+0;-0;0", CultureInfo.CurrentCulture);
        if (_voltageCapValue != null && _voltageCapSlider != null)
            _voltageCapValue.Text = _voltageCapSlider.Value <= _voltageCapSlider.Minimum
                ? Resource.Off
                : ((int)_voltageCapSlider.Value).ToString(CultureInfo.CurrentCulture);
        if (_voltageLockValue != null && _voltageLockSlider != null)
            _voltageLockValue.Text = _voltageLockSlider.Value <= _voltageLockSlider.Minimum
                ? Resource.Off
                : ((int)_voltageLockSlider.Value).ToString(CultureInfo.CurrentCulture);
    }

    private void ValueTextBox_OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            if (sender is UIElement element)
                element.MoveFocus(new TraversalRequest(FocusNavigationDirection.Next));
            e.Handled = true;
        }
        else if (e.Key == Key.Escape && sender is TextBox textBox)
        {
            UpdateValueTextBoxes();
            textBox.SelectAll();
            e.Handled = true;
        }
    }

    private void CoreValue_OnLostFocus(object sender, RoutedEventArgs e) =>
        CommitNumericValue(_coreValue, _coreSlider, allowOff: false);

    private void MemoryValue_OnLostFocus(object sender, RoutedEventArgs e) =>
        CommitNumericValue(_memoryValue, _memorySlider, allowOff: false);

    private void VoltageCapValue_OnLostFocus(object sender, RoutedEventArgs e) =>
        CommitNumericValue(_voltageCapValue, _voltageCapSlider, allowOff: true);

    private void VoltageLockValue_OnLostFocus(object sender, RoutedEventArgs e) =>
        CommitNumericValue(_voltageLockValue, _voltageLockSlider, allowOff: true);

    private void CommitNumericValue(TextBox textBox, Slider slider, bool allowOff)
    {
        if (textBox == null || slider == null)
            return;

        var input = textBox.Text.Trim();
        if (allowOff && input.Equals(Resource.Off, StringComparison.OrdinalIgnoreCase))
        {
            slider.Value = slider.Minimum;
            UpdateValueTextBoxes();
            return;
        }

        if (!int.TryParse(input, NumberStyles.Integer, CultureInfo.CurrentCulture, out var value))
        {
            textBox.Text = allowOff && slider.Value <= slider.Minimum
                ? Resource.Off
                : ((int)slider.Value).ToString(CultureInfo.CurrentCulture);
            return;
        }

        // Treat every integer representation of zero (for example, +0 or 00) as Off.
        if (allowOff && value == 0)
        {
            slider.Value = slider.Minimum;
            UpdateValueTextBoxes();
            return;
        }

        var minimum = allowOff ? (int)slider.Minimum + (int)slider.TickFrequency : (int)slider.Minimum;
        slider.Value = Math.Clamp(value, minimum, (int)slider.Maximum);
        UpdateValueTextBoxes();
    }

    private void VoltageCapSlider_OnValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_voltageCapValue == null || _voltageCapSlider == null) return;

        UpdateValueTextBoxes();
        if (_voltageCapSlider.Value > _voltageCapSlider.Minimum)
        {
            if (_voltageLockSlider != null && _voltageLockSlider.Value > _voltageLockSlider.Minimum)
            {
                _voltageLockSlider.Value = _voltageLockSlider.Minimum;
            }
        }
    }

    private void VoltageLockSlider_OnValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_voltageLockValue == null || _voltageLockSlider == null) return;

        UpdateValueTextBoxes();
        if (_voltageLockSlider.Value > _voltageLockSlider.Minimum)
        {
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
