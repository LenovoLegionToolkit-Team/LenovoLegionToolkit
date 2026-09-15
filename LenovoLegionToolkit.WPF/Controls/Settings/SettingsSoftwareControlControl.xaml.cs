using System;
using System.Threading.Tasks;
using System.Windows;
using LenovoLegionToolkit.Lib;
using LenovoLegionToolkit.Lib.Controllers;
using LenovoLegionToolkit.Lib.SoftwareDisabler;
using LenovoLegionToolkit.Lib.Utils;
using LenovoLegionToolkit.WPF.Resources;
using LenovoLegionToolkit.WPF.Utils;

namespace LenovoLegionToolkit.WPF.Controls.Settings;

public partial class SettingsSoftwareControlControl
{
    private readonly VantageDisabler _vantageDisabler = IoCContainer.Resolve<VantageDisabler>();
    private readonly LegionSpaceDisabler _legionSpaceDisabler = IoCContainer.Resolve<LegionSpaceDisabler>();
    private readonly LegionZoneDisabler _legionZoneDisabler = IoCContainer.Resolve<LegionZoneDisabler>();
    private readonly SmartEngineDisabler _smartEngineDisabler = IoCContainer.Resolve<SmartEngineDisabler>();
    private readonly FnKeysDisabler _fnKeysDisabler = IoCContainer.Resolve<FnKeysDisabler>();
    private readonly RGBKeyboardBacklightController _rgbKeyboardBacklightController = IoCContainer.Resolve<RGBKeyboardBacklightController>();

    private bool _isRefreshing;

    public event EventHandler<SoftwareStatus>? FnKeysStatusChanged;

    public SettingsSoftwareControlControl()
    {
        InitializeComponent();
    }

    public async Task RefreshAsync()
    {
        _isRefreshing = true;

        var vantageStatus = await _vantageDisabler.GetStatusAsync();
        _vantageCard.Visibility = vantageStatus != SoftwareStatus.NotFound ? Visibility.Visible : Visibility.Collapsed;
        _vantageToggle.IsChecked = vantageStatus == SoftwareStatus.Disabled;

        var legionSpaceStatus = await _legionSpaceDisabler.GetStatusAsync();
        _legionSpaceCard.Visibility = legionSpaceStatus != SoftwareStatus.NotFound ? Visibility.Visible : Visibility.Collapsed;
        _legionSpaceToggle.IsChecked = legionSpaceStatus == SoftwareStatus.Disabled;

        var legionZoneStatus = await _legionZoneDisabler.GetStatusAsync();
        _legionZoneCard.Visibility = legionZoneStatus != SoftwareStatus.NotFound ? Visibility.Visible : Visibility.Collapsed;
        _legionZoneToggle.IsChecked = legionZoneStatus == SoftwareStatus.Disabled;


        var smartEngineStatus = await _smartEngineDisabler.GetStatusAsync();
        _smartEngineCard.Visibility = smartEngineStatus != SoftwareStatus.NotFound ? Visibility.Visible : Visibility.Collapsed;
        _smartEngineToggle.IsChecked = smartEngineStatus == SoftwareStatus.Disabled;

        var fnKeysStatus = await _fnKeysDisabler.GetStatusAsync();
        _fnKeysCard.Visibility = fnKeysStatus != SoftwareStatus.NotFound ? Visibility.Visible : Visibility.Collapsed;
        _fnKeysToggle.IsChecked = fnKeysStatus == SoftwareStatus.Disabled;

        _vantageToggle.Visibility = Visibility.Visible;
        _legionSpaceToggle.Visibility = Visibility.Visible;
        _legionZoneToggle.Visibility = Visibility.Visible;
        _smartEngineToggle.Visibility = Visibility.Visible;
        _fnKeysToggle.Visibility = Visibility.Visible;

        _isRefreshing = false;

        FnKeysStatusChanged?.Invoke(this, fnKeysStatus);
    }

    private async void VantageToggle_Click(object sender, RoutedEventArgs e)
    {
        if (_isRefreshing)
            return;

        _vantageToggle.IsEnabled = false;

        var state = _vantageToggle.IsChecked;
        if (state is null)
        {
            _vantageToggle.IsEnabled = true;
            return;
        }

        if (state.Value)
        {
            try
            {
                await _vantageDisabler.DisableAsync();
            }
            catch
            {
                _vantageToggle.IsEnabled = true;
                await SnackbarHelper.ShowAsync(Resource.SettingsPage_DisableVantage_Error_Title, Resource.SettingsPage_DisableVantage_Error_Message, SnackbarType.Error);
                return;
            }

            try
            {
                if (await _rgbKeyboardBacklightController.IsSupportedAsync())
                {
                    Log.Instance.Trace($"Setting light control owner and restoring preset...");

                    await _rgbKeyboardBacklightController.SetLightControlOwnerAsync(true, true);
                }
            }
            catch (Exception ex)
            {
                Log.Instance.Trace($"Couldn't set light control owner or current preset.", ex);
            }

            try
            {
                var controller = IoCContainer.Resolve<SpectrumKeyboardBacklightController>();
                if (await controller.IsSupportedAsync())
                {
                    Log.Instance.Trace($"Starting Aurora if needed...");

                    var result = await controller.StartAuroraIfNeededAsync();
                    if (result)
                    {
                        Log.Instance.Trace($"Aurora started.");
                    }
                    else
                    {
                        Log.Instance.Trace($"Aurora not needed.");
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Instance.Trace($"Couldn't start Aurora if needed.", ex);
            }
        }
        else
        {
            try
            {
                if (await _rgbKeyboardBacklightController.IsSupportedAsync())
                {
                    Log.Instance.Trace($"Setting light control owner...");

                    await _rgbKeyboardBacklightController.SetLightControlOwnerAsync(false);
                }
            }
            catch (Exception ex)
            {
                Log.Instance.Trace($"Couldn't set light control owner.", ex);
            }

            try
            {
                if (IoCContainer.TryResolve<SpectrumKeyboardBacklightController>() is { } spectrumKeyboardBacklightController)
                {
                    Log.Instance.Trace($"Making sure Aurora is stopped...");

                    if (await spectrumKeyboardBacklightController.IsSupportedAsync())
                        await spectrumKeyboardBacklightController.StopAuroraIfNeededAsync();
                }
            }
            catch (Exception ex)
            {
                Log.Instance.Trace($"Couldn't stop Aurora.", ex);
            }

            try
            {
                await _vantageDisabler.EnableAsync();
            }
            catch
            {
                _vantageToggle.IsEnabled = true;
                await SnackbarHelper.ShowAsync(Resource.SettingsPage_EnableVantage_Error_Title, Resource.SettingsPage_EnableVantage_Error_Message, SnackbarType.Error);
                return;
            }
        }

        _vantageToggle.IsEnabled = true;
    }

    private async void LegionZoneToggle_Click(object sender, RoutedEventArgs e)
    {
        if (_isRefreshing)
            return;

        _legionZoneToggle.IsEnabled = false;

        var state = _legionZoneToggle.IsChecked;
        if (state is null)
        {
            _legionZoneToggle.IsEnabled = true;
            return;
        }

        if (state.Value)
        {
            try
            {
                await _legionZoneDisabler.DisableAsync();
            }
            catch
            {
                _legionZoneToggle.IsEnabled = true;
                await SnackbarHelper.ShowAsync(Resource.SettingsPage_DisableLegionZone_Error_Title, Resource.SettingsPage_DisableLegionZone_Error_Message, SnackbarType.Error);
                return;
            }
        }
        else
        {
            try
            {
                await _legionZoneDisabler.EnableAsync();
            }
            catch
            {
                _legionZoneToggle.IsEnabled = true;
                await SnackbarHelper.ShowAsync(Resource.SettingsPage_EnableLegionZone_Error_Title, Resource.SettingsPage_EnableLegionZone_Error_Message, SnackbarType.Error);
                return;
            }
        }

        _legionZoneToggle.IsEnabled = true;
    }

    private async void LegionSpaceToggle_Click(object sender, RoutedEventArgs e)
    {
        if (_isRefreshing)
            return;

        _legionSpaceToggle.IsEnabled = false;

        var state = _legionSpaceToggle.IsChecked;
        if (state is null)
        {
            _legionSpaceToggle.IsEnabled = true;
            return;
        }

        if (state.Value)
        {
            try
            {
                await _legionSpaceDisabler.DisableAsync();
            }
            catch
            {
                _legionSpaceToggle.IsEnabled = true;
                await SnackbarHelper.ShowAsync(Resource.SettingsPage_DisableLegionSpace_Error_Title, Resource.SettingsPage_DisableLegionSpace_Error_Message, SnackbarType.Error);
                return;
            }
        }
        else
        {
            try
            {
                await _legionSpaceDisabler.EnableAsync();
            }
            catch
            {
                _legionSpaceToggle.IsEnabled = true;
                await SnackbarHelper.ShowAsync(Resource.SettingsPage_EnableLegionSpace_Error_Title, Resource.SettingsPage_EnableLegionSpace_Error_Message, SnackbarType.Error);
                return;
            }
        }

        _legionSpaceToggle.IsEnabled = true;
    }

    private async void SmartEngineToggle_Click(object sender, RoutedEventArgs e)
    {
        if (_isRefreshing)
            return;

        _smartEngineToggle.IsEnabled = false;

        var state = _smartEngineToggle.IsChecked;
        if (state is null)
        {
            _smartEngineToggle.IsEnabled = true;
            return;
        }

        if (state.Value)
        {
            try
            {
                await _smartEngineDisabler.DisableAsync();
            }
            catch
            {
                _smartEngineToggle.IsEnabled = true;
                await SnackbarHelper.ShowAsync(Resource.SettingsPage_DisableSmartEngine_Error_Title, Resource.SettingsPage_DisableSmartEngine_Error_Message, SnackbarType.Error);
                return;
            }
        }
        else
        {
            try
            {
                await _smartEngineDisabler.EnableAsync();
            }
            catch
            {
                _smartEngineToggle.IsEnabled = true;
                await SnackbarHelper.ShowAsync(Resource.SettingsPage_EnableSmartEngine_Error_Title, Resource.SettingsPage_EnableSmartEngine_Error_Message, SnackbarType.Error);
                return;
            }
        }

        _smartEngineToggle.IsEnabled = true;
    }

    private async void FnKeysToggle_Click(object sender, RoutedEventArgs e)
    {
        if (_isRefreshing)
            return;

        _fnKeysToggle.IsEnabled = false;

        var state = _fnKeysToggle.IsChecked;
        if (state is null)
        {
            _fnKeysToggle.IsEnabled = true;
            return;
        }

        if (state.Value)
        {
            try
            {
                await _fnKeysDisabler.DisableAsync();
            }
            catch
            {
                _fnKeysToggle.IsEnabled = true;
                await SnackbarHelper.ShowAsync(Resource.SettingsPage_DisableLenovoHotkeys_Error_Title, Resource.SettingsPage_DisableLenovoHotkeys_Error_Message, SnackbarType.Error);
                return;
            }
        }
        else
        {
            try
            {
                await _fnKeysDisabler.EnableAsync();
            }
            catch
            {
                _fnKeysToggle.IsEnabled = true;
                await SnackbarHelper.ShowAsync(Resource.SettingsPage_EnableLenovoHotkeys_Error_Title, Resource.SettingsPage_EnableLenovoHotkeys_Error_Message, SnackbarType.Error);
                return;
            }
        }

        _fnKeysToggle.IsEnabled = true;

        var fnKeysStatus = state.Value ? SoftwareStatus.Disabled : SoftwareStatus.Enabled;
        FnKeysStatusChanged?.Invoke(this, fnKeysStatus);
    }
}
