using System;
using System.Threading.Tasks;
using System.Windows;
using LenovoLegionToolkit.Lib;
using LenovoLegionToolkit.Lib.Controllers;
using LenovoLegionToolkit.Lib.Settings;
using LenovoLegionToolkit.Lib.Utils;

namespace LenovoLegionToolkit.WPF.Controls.Dashboard;

public partial class PowerButtonLedControl
{
    private readonly PowerButtonLedController _powerButtonLed = IoCContainer.Resolve<PowerButtonLedController>();
    private readonly ApplicationSettings _settings = IoCContainer.Resolve<ApplicationSettings>();

    private bool _isUpdating;

    public PowerButtonLedControl()
    {
        InitializeComponent();

        _brightnessSlider.Minimum = PowerButtonLedController.MinBrightness;
        _brightnessSlider.Maximum = PowerButtonLedController.MaxBrightness;
    }

    protected override async Task OnRefreshAsync()
    {
        if (!await _powerButtonLed.IsSupportedAsync())
            throw new NotSupportedException();

        var syncEnabled = await _powerButtonLed.GetSyncEnabledAsync();

        try
        {
            _isUpdating = true;

            _toggle.IsChecked = syncEnabled;
            _brightnessSlider.Value = _settings.Store.PowerButtonLedBrightness;
            _brightnessSlider.IsEnabled = !syncEnabled;
        }
        finally
        {
            _isUpdating = false;
        }

        _brightnessSlider.Visibility = Visibility.Visible;
        _toggle.Visibility = Visibility.Visible;
    }

    protected override void OnFinishedLoading() { }

    private async void Toggle_Click(object sender, RoutedEventArgs e)
    {
        if (IsRefreshing || _isUpdating)
            return;

        var exceptionOccurred = false;

        try
        {
            _toggle.IsEnabled = false;
            _brightnessSlider.IsEnabled = false;

            var enabled = _toggle.IsChecked == true;

            await _powerButtonLed.SetSyncEnabledAsync(enabled);

            if (!enabled)
                await _powerButtonLed.SetBrightnessAsync(_settings.Store.PowerButtonLedBrightness);
        }
        catch (Exception ex)
        {
            exceptionOccurred = true;

            Log.Instance.Trace($"Failed to change power button light sync.", ex);
        }
        finally
        {
            _toggle.IsEnabled = true;
            _brightnessSlider.IsEnabled = _toggle.IsChecked != true;
        }

        if (exceptionOccurred)
            await RefreshAsync();
    }

    private async void BrightnessSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!IsLoaded || IsRefreshing || _isUpdating)
            return;

        var exceptionOccurred = false;

        try
        {
            var brightness = (int)_brightnessSlider.Value;

            await _powerButtonLed.SetBrightnessAsync(brightness);
            _settings.Store.PowerButtonLedBrightness = brightness;
            _settings.SynchronizeStore();
        }
        catch (Exception ex)
        {
            exceptionOccurred = true;

            Log.Instance.Trace($"Failed to set power button light brightness.", ex);
        }

        if (exceptionOccurred)
            await RefreshAsync();
    }
}
