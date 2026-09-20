using System;
using System.Threading.Tasks;
using System.Windows;
using LenovoLegionToolkit.Lib;
using LenovoLegionToolkit.Lib.Controllers;
using LenovoLegionToolkit.Lib.SoftwareDisabler;
using LenovoLegionToolkit.Lib.Utils;
using LenovoLegionToolkit.WPF.Resources;
using LenovoLegionToolkit.WPF.Utils;
using Wpf.Ui.Controls;

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
        _vantageToggle.IsChecked = GetToggleState(vantageStatus, _vantageDisabler);

        var legionSpaceStatus = await _legionSpaceDisabler.GetStatusAsync();
        _legionSpaceCard.Visibility = legionSpaceStatus != SoftwareStatus.NotFound ? Visibility.Visible : Visibility.Collapsed;
        _legionSpaceToggle.IsChecked = GetToggleState(legionSpaceStatus, _legionSpaceDisabler);

        var legionZoneStatus = await _legionZoneDisabler.GetStatusAsync();
        _legionZoneCard.Visibility = legionZoneStatus != SoftwareStatus.NotFound ? Visibility.Visible : Visibility.Collapsed;
        _legionZoneToggle.IsChecked = GetToggleState(legionZoneStatus, _legionZoneDisabler);


        var smartEngineStatus = await _smartEngineDisabler.GetStatusAsync();
        _smartEngineCard.Visibility = smartEngineStatus != SoftwareStatus.NotFound ? Visibility.Visible : Visibility.Collapsed;
        _smartEngineToggle.IsChecked = GetToggleState(smartEngineStatus, _smartEngineDisabler);

        var fnKeysStatus = await _fnKeysDisabler.GetStatusAsync();
        _fnKeysCard.Visibility = fnKeysStatus != SoftwareStatus.NotFound ? Visibility.Visible : Visibility.Collapsed;
        _fnKeysToggle.IsChecked = GetToggleState(fnKeysStatus, _fnKeysDisabler);

        _vantageToggle.Visibility = Visibility.Visible;
        _legionSpaceToggle.Visibility = Visibility.Visible;
        _legionZoneToggle.Visibility = Visibility.Visible;
        _smartEngineToggle.Visibility = Visibility.Visible;
        _fnKeysToggle.Visibility = Visibility.Visible;

        _isRefreshing = false;

        FnKeysStatusChanged?.Invoke(this, fnKeysStatus);
    }

    private static bool GetToggleState(SoftwareStatus status, AbstractSoftwareDisabler disabler) =>
        SoftwareDisablerStateStore.ResolveToggleState(disabler.GetType().Name, status);

    private static async Task SyncToggleAsync(ToggleSwitch toggle, AbstractSoftwareDisabler disabler)
    {
        toggle.IsChecked = GetToggleState(await disabler.GetStatusAsync(), disabler);
        toggle.IsEnabled = true;

        if (disabler.LastFailureReason is null)
        {
            return;
        }

        await SnackbarHelper.ShowAsync(Resource.SettingsPage_DisableIncomplete_Title, disabler.LastFailureReason, SnackbarType.Warning);
    }

    private async void VantageToggle_Click(object sender, RoutedEventArgs e)
    {
        if (_isRefreshing)
        {
            return;
        }

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
            catch (Exception ex)
            {
                Log.Instance.Trace($"Couldn't change Vantage.", ex);

                await SnackbarHelper.ShowAsync(Resource.SettingsPage_DisableVantage_Error_Title, ex.Message, SnackbarType.Error);
                await SyncToggleAsync(_vantageToggle, _vantageDisabler);
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
                    {
                        await spectrumKeyboardBacklightController.StopAuroraIfNeededAsync();
                    }
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
            catch (Exception ex)
            {
                Log.Instance.Trace($"Couldn't change Vantage.", ex);

                await SnackbarHelper.ShowAsync(Resource.SettingsPage_EnableVantage_Error_Title, ex.Message, SnackbarType.Error);
                await SyncToggleAsync(_vantageToggle, _vantageDisabler);
                return;
            }
        }

        await SyncToggleAsync(_vantageToggle, _vantageDisabler);
    }

    private async void LegionZoneToggle_Click(object sender, RoutedEventArgs e)
    {
        if (_isRefreshing)
        {
            return;
        }

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
            catch (Exception ex)
            {
                Log.Instance.Trace($"Couldn't change LegionZone.", ex);

                await SnackbarHelper.ShowAsync(Resource.SettingsPage_DisableLegionZone_Error_Title, ex.Message, SnackbarType.Error);
                await SyncToggleAsync(_legionZoneToggle, _legionZoneDisabler);
                return;
            }
        }
        else
        {
            try
            {
                await _legionZoneDisabler.EnableAsync();
            }
            catch (Exception ex)
            {
                Log.Instance.Trace($"Couldn't change LegionZone.", ex);

                await SnackbarHelper.ShowAsync(Resource.SettingsPage_EnableLegionZone_Error_Title, ex.Message, SnackbarType.Error);
                await SyncToggleAsync(_legionZoneToggle, _legionZoneDisabler);
                return;
            }
        }

        await SyncToggleAsync(_legionZoneToggle, _legionZoneDisabler);
    }

    private async void LegionSpaceToggle_Click(object sender, RoutedEventArgs e)
    {
        if (_isRefreshing)
        {
            return;
        }

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
            catch (Exception ex)
            {
                Log.Instance.Trace($"Couldn't change LegionSpace.", ex);

                await SnackbarHelper.ShowAsync(Resource.SettingsPage_DisableLegionSpace_Error_Title, ex.Message, SnackbarType.Error);
                await SyncToggleAsync(_legionSpaceToggle, _legionSpaceDisabler);
                return;
            }
        }
        else
        {
            try
            {
                await _legionSpaceDisabler.EnableAsync();
            }
            catch (Exception ex)
            {
                Log.Instance.Trace($"Couldn't change LegionSpace.", ex);

                await SnackbarHelper.ShowAsync(Resource.SettingsPage_EnableLegionSpace_Error_Title, ex.Message, SnackbarType.Error);
                await SyncToggleAsync(_legionSpaceToggle, _legionSpaceDisabler);
                return;
            }
        }

        await SyncToggleAsync(_legionSpaceToggle, _legionSpaceDisabler);
    }

    private async void SmartEngineToggle_Click(object sender, RoutedEventArgs e)
    {
        if (_isRefreshing)
        {
            return;
        }

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
            catch (Exception ex)
            {
                Log.Instance.Trace($"Couldn't change SmartEngine.", ex);

                await SnackbarHelper.ShowAsync(Resource.SettingsPage_DisableSmartEngine_Error_Title, ex.Message, SnackbarType.Error);
                await SyncToggleAsync(_smartEngineToggle, _smartEngineDisabler);
                return;
            }
        }
        else
        {
            try
            {
                await _smartEngineDisabler.EnableAsync();
            }
            catch (Exception ex)
            {
                Log.Instance.Trace($"Couldn't change SmartEngine.", ex);

                await SnackbarHelper.ShowAsync(Resource.SettingsPage_EnableSmartEngine_Error_Title, ex.Message, SnackbarType.Error);
                await SyncToggleAsync(_smartEngineToggle, _smartEngineDisabler);
                return;
            }
        }

        await SyncToggleAsync(_smartEngineToggle, _smartEngineDisabler);
    }

    private async void FnKeysToggle_Click(object sender, RoutedEventArgs e)
    {
        if (_isRefreshing)
        {
            return;
        }

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
            catch (Exception ex)
            {
                Log.Instance.Trace($"Couldn't change FnKeys.", ex);

                await SnackbarHelper.ShowAsync(Resource.SettingsPage_DisableLenovoHotkeys_Error_Title, ex.Message, SnackbarType.Error);
                await SyncToggleAsync(_fnKeysToggle, _fnKeysDisabler);
                return;
            }
        }
        else
        {
            try
            {
                await _fnKeysDisabler.EnableAsync();
            }
            catch (Exception ex)
            {
                Log.Instance.Trace($"Couldn't change FnKeys.", ex);

                await SnackbarHelper.ShowAsync(Resource.SettingsPage_EnableLenovoHotkeys_Error_Title, ex.Message, SnackbarType.Error);
                await SyncToggleAsync(_fnKeysToggle, _fnKeysDisabler);
                return;
            }
        }

        await SyncToggleAsync(_fnKeysToggle, _fnKeysDisabler);

        var fnKeysStatus = state.Value ? SoftwareStatus.Disabled : SoftwareStatus.Enabled;
        FnKeysStatusChanged?.Invoke(this, fnKeysStatus);
    }
}
