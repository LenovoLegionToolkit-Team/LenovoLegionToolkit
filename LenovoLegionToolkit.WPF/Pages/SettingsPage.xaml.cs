using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using LenovoLegionToolkit.Lib;
using LenovoLegionToolkit.Lib.Controllers;
using LenovoLegionToolkit.Lib.Controllers.Sensors;
using LenovoLegionToolkit.Lib.Extensions;
using LenovoLegionToolkit.Lib.Features;
using LenovoLegionToolkit.Lib.Integrations;
using LenovoLegionToolkit.Lib.Settings;
using LenovoLegionToolkit.Lib.SoftwareDisabler;
using LenovoLegionToolkit.Lib.System;
using LenovoLegionToolkit.Lib.System.Management;
using LenovoLegionToolkit.Lib.Utils;
using LenovoLegionToolkit.WPF.CLI;
using LenovoLegionToolkit.WPF.Extensions;
using LenovoLegionToolkit.WPF.Resources;
using LenovoLegionToolkit.WPF.Utils;
using LenovoLegionToolkit.WPF.Windows;
using LenovoLegionToolkit.WPF.Windows.Dashboard;
using LenovoLegionToolkit.WPF.Windows.FloatingGadgets;
using LenovoLegionToolkit.WPF.Windows.Settings;
using LenovoLegionToolkit.WPF.Windows.Utils;
using Microsoft.Win32;

namespace LenovoLegionToolkit.WPF.Pages;

public partial class SettingsPage
{
    private readonly ApplicationSettings _settings = IoCContainer.Resolve<ApplicationSettings>();
    private readonly IntegrationsSettings _integrationsSettings = IoCContainer.Resolve<IntegrationsSettings>();

    private readonly VantageDisabler _vantageDisabler = IoCContainer.Resolve<VantageDisabler>();
    private readonly LegionSpaceDisabler _legionSpaceDisabler = IoCContainer.Resolve<LegionSpaceDisabler>();
    private readonly LegionZoneDisabler _legionZoneDisabler = IoCContainer.Resolve<LegionZoneDisabler>();
    private readonly FnKeysDisabler _fnKeysDisabler = IoCContainer.Resolve<FnKeysDisabler>();
    private readonly PowerModeFeature _powerModeFeature = IoCContainer.Resolve<PowerModeFeature>();
    private readonly RGBKeyboardBacklightController _rgbKeyboardBacklightController = IoCContainer.Resolve<RGBKeyboardBacklightController>();
    private readonly ThemeManager _themeManager = IoCContainer.Resolve<ThemeManager>();
    private readonly HWiNFOIntegration _hwinfoIntegration = IoCContainer.Resolve<HWiNFOIntegration>();
    private readonly IpcServer _ipcServer = IoCContainer.Resolve<IpcServer>();
    private readonly UpdateChecker _updateChecker = IoCContainer.Resolve<UpdateChecker>();
    private readonly UpdateCheckSettings _updateCheckSettings = IoCContainer.Resolve<UpdateCheckSettings>();

    private bool _isRefreshing;

    private Custom? CustomWindow;
    private EditSensorGroupWindow? EditSensorGroupWindow;

    public SettingsPage()
    {
        InitializeComponent();

        IsVisibleChanged += SettingsPage_IsVisibleChanged;

        _themeManager.ThemeApplied += ThemeManager_ThemeApplied;
    }

    private async void SettingsPage_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (IsVisible)
            await RefreshAsync();
    }

    private void ThemeManager_ThemeApplied(object? sender, EventArgs e)
    {
        if (!_isRefreshing)
            UpdateAccentColorPicker();
    }

    private async Task RefreshAsync()
    {
        _isRefreshing = true;

        var loadingTask = Task.Delay(TimeSpan.FromMilliseconds(500));

        var languages = LocalizationHelper.Languages.OrderBy(LocalizationHelper.LanguageDisplayName, StringComparer.InvariantCultureIgnoreCase).ToArray();
        var language = await LocalizationHelper.GetLanguageAsync();
        if (languages.Length > 1)
        {
            _langComboBox.SetItems(languages, language, LocalizationHelper.LanguageDisplayName);
            _langComboBox.Visibility = Visibility.Visible;
        }
        else
        {
            _langCardControl.Visibility = Visibility.Collapsed;
        }

        _temperatureComboBox.SetItems(Enum.GetValues<TemperatureUnit>(), _settings.Store.TemperatureUnit, t => t switch
        {
            TemperatureUnit.C => Resource.Celsius,
            TemperatureUnit.F => Resource.Fahrenheit,
            _ => new ArgumentOutOfRangeException(nameof(t))
        });
        _themeComboBox.SetItems(Enum.GetValues<Theme>(), _settings.Store.Theme, t => t.GetDisplayName());

        UpdateAccentColorPicker();
        _accentColorSourceComboBox.SetItems(Enum.GetValues<AccentColorSource>(), _settings.Store.AccentColorSource, t => t.GetDisplayName());

        _autorunComboBox.SetItems(Enum.GetValues<AutorunState>(), Autorun.State, t => t.GetDisplayName());
        _minimizeToTrayToggle.IsChecked = _settings.Store.MinimizeToTray;
        _minimizeOnCloseToggle.IsChecked = _settings.Store.MinimizeOnClose;
        _useNewSensorDashboardToggle.IsChecked = _settings.Store.UseNewSensorDashboard;
        _lockWindowSizeToggle.IsChecked = _settings.Store.LockWindowSize;

        _backgroundImageOpacitySlider.Value = _settings.Store.Opacity;

        var vantageStatus = await _vantageDisabler.GetStatusAsync();
        _vantageCard.Visibility = vantageStatus != SoftwareStatus.NotFound ? Visibility.Visible : Visibility.Collapsed;
        _vantageToggle.IsChecked = vantageStatus == SoftwareStatus.Disabled;

        var legionSpaceStatus = await _legionSpaceDisabler.GetStatusAsync();
        _legionSpaceCard.Visibility = legionSpaceStatus != SoftwareStatus.NotFound ? Visibility.Visible : Visibility.Collapsed;
        _legionSpaceToggle.IsChecked = legionSpaceStatus == SoftwareStatus.Disabled;

        var legionZoneStatus = await _legionZoneDisabler.GetStatusAsync();
        _legionZoneCard.Visibility = legionZoneStatus != SoftwareStatus.NotFound ? Visibility.Visible : Visibility.Collapsed;
        _legionZoneToggle.IsChecked = legionZoneStatus == SoftwareStatus.Disabled;

        var fnKeysStatus = await _fnKeysDisabler.GetStatusAsync();
        _fnKeysCard.Visibility = fnKeysStatus != SoftwareStatus.NotFound ? Visibility.Visible : Visibility.Collapsed;
        _fnKeysToggle.IsChecked = fnKeysStatus == SoftwareStatus.Disabled;

        _smartFnLockComboBox.SetItems([ModifierKey.None, ModifierKey.Alt, ModifierKey.Alt | ModifierKey.Ctrl | ModifierKey.Shift],
            _settings.Store.SmartFnLockFlags,
            m => m is ModifierKey.None ? Resource.Off : m.GetFlagsDisplayName(ModifierKey.None));

        _smartKeySinglePressActionCard.Visibility = fnKeysStatus != SoftwareStatus.Enabled ? Visibility.Visible : Visibility.Collapsed;
        _smartKeyDoublePressActionCard.Visibility = fnKeysStatus != SoftwareStatus.Enabled ? Visibility.Visible : Visibility.Collapsed;

        _notificationsCard.Visibility = fnKeysStatus != SoftwareStatus.Enabled ? Visibility.Visible : Visibility.Collapsed;
        _excludeRefreshRatesCard.Visibility = fnKeysStatus != SoftwareStatus.Enabled ? Visibility.Visible : Visibility.Collapsed;
        _synchronizeBrightnessToAllPowerPlansToggle.IsChecked = _settings.Store.SynchronizeBrightnessToAllPowerPlans;
        _onBatterySinceResetToggle.IsChecked = _settings.Store.ResetBatteryOnSinceTimerOnReboot;

        _bootLogoCard.Visibility = await BootLogo.IsSupportedAsync() ? Visibility.Visible : Visibility.Collapsed;

        if (_updateChecker.Disable)
        {
            _updateTextBlock.Visibility = Visibility.Collapsed;
            _checkUpdatesCard.Visibility = Visibility.Collapsed;
            _updateCheckFrequencyCard.Visibility = Visibility.Collapsed;
            _updateMethodCard.Visibility = Visibility.Collapsed;
            __updateMethodComboBox.Visibility = Visibility.Collapsed;
        }
        else
        {
            _checkUpdatesButton.Visibility = Visibility.Visible;
            _updateCheckFrequencyComboBox.Visibility = Visibility.Visible;
            _updateCheckFrequencyComboBox.SetItems(Enum.GetValues<UpdateCheckFrequency>(), _updateCheckSettings.Store.UpdateCheckFrequency, t => t.GetDisplayName());
            __updateMethodComboBox.Visibility = Visibility.Visible;
            __updateMethodComboBox.SetItems(Enum.GetValues<UpdateMethod>(), _settings.Store.UpdateMethod, t => t.GetDisplayName());
        }

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
        _powerModeMappingCard.Visibility = isPowerModeFeatureSupported ? Visibility.Visible : Visibility.Collapsed;
        _powerModesCard.Visibility = _settings.Store.PowerModeMappingMode == PowerModeMappingMode.WindowsPowerMode && isPowerModeFeatureSupported ? Visibility.Visible : Visibility.Collapsed;
        _windowsPowerPlansCard.Visibility = _settings.Store.PowerModeMappingMode == PowerModeMappingMode.WindowsPowerPlan && isPowerModeFeatureSupported ? Visibility.Visible : Visibility.Collapsed;
        _windowsPowerPlansControlPanelCard.Visibility = _settings.Store.PowerModeMappingMode == PowerModeMappingMode.WindowsPowerPlan && isPowerModeFeatureSupported ? Visibility.Visible : Visibility.Collapsed;
        _enableLoggingToggle.IsChecked = _settings.Store.EnableLogging;

        _onBatterySinceResetToggle.Visibility = Visibility.Visible;

        _hwinfoIntegrationToggle.IsChecked = _integrationsSettings.Store.HWiNFO;
        _cliInterfaceToggle.IsChecked = _integrationsSettings.Store.CLI;
        _cliPathToggle.IsChecked = SystemPath.HasCLI();

        _floatingGadgetsToggle.Visibility = Visibility.Visible;
        _floatingGadgetsStyleComboBox.Visibility = Visibility.Visible;
        _floatingGadgetsToggle.IsChecked = _settings.Store.ShowFloatingGadgets;

        _floatingGadgetsStyleComboBox.SelectedIndex = _settings.Store.SelectedStyleIndex;
        _floatingGadgetsInterval.Text = _settings.Store.FloatingGadgetsRefreshInterval.ToString();

        await loadingTask;

        _temperatureComboBox.Visibility = Visibility.Visible;
        _themeComboBox.Visibility = Visibility.Visible;
        _autorunComboBox.Visibility = Visibility.Visible;
        _minimizeToTrayToggle.Visibility = Visibility.Visible;
        _minimizeOnCloseToggle.Visibility = Visibility.Visible;
        _enableLoggingToggle.Visibility = Visibility.Visible;
        _useNewSensorDashboardToggle.Visibility = Visibility.Visible;
        _lockWindowSizeToggle.Visibility = Visibility.Visible;
        _vantageToggle.Visibility = Visibility.Visible;
        _legionSpaceToggle.Visibility = Visibility.Visible;
        _legionZoneToggle.Visibility = Visibility.Visible;
        _fnKeysToggle.Visibility = Visibility.Visible;
        _smartFnLockComboBox.Visibility = Visibility.Visible;
        _synchronizeBrightnessToAllPowerPlansToggle.Visibility = Visibility.Visible;
        _godModeFnQSwitchableToggle.Visibility = Visibility.Visible;
        _powerModeMappingComboBox.Visibility = Visibility.Visible;
        _hwinfoIntegrationToggle.Visibility = Visibility.Visible;
        _cliInterfaceToggle.Visibility = Visibility.Visible;
        _cliPathToggle.Visibility = Visibility.Visible;
        _floatingGadgetsToggle.Visibility = Visibility.Visible;
        _floatingGadgetsInterval.Visibility = Visibility.Visible;
        _selectBackgroundImageButton.Visibility = Visibility.Visible;
        _clearBackgroundImageButton.Visibility = Visibility.Visible;
        _backgroundImageOpacitySlider.Visibility = Visibility.Visible;

        _isRefreshing = false;
    }

    private async void LangComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isRefreshing)
            return;

        if (!_langComboBox.TryGetSelectedItem(out CultureInfo? cultureInfo) || cultureInfo is null)
            return;

        await LocalizationHelper.SetLanguageAsync(cultureInfo);

        App.Current.RestartMainWindow();
    }

    private void TemperatureComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isRefreshing)
            return;

        if (!_temperatureComboBox.TryGetSelectedItem(out TemperatureUnit temperatureUnit))
            return;

        _settings.Store.TemperatureUnit = temperatureUnit;
        _settings.SynchronizeStore();
    }

    private void ThemeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isRefreshing)
            return;

        if (!_themeComboBox.TryGetSelectedItem(out Theme state))
            return;

        _settings.Store.Theme = state;
        _settings.SynchronizeStore();

        _themeManager.Apply();
    }

    private void AccentColorPicker_Changed(object sender, EventArgs e)
    {
        if (_isRefreshing)
            return;

        if (_settings.Store.AccentColorSource != AccentColorSource.Custom)
            return;

        _settings.Store.AccentColor = _accentColorPicker.SelectedColor.ToRGBColor();
        _settings.SynchronizeStore();

        _themeManager.Apply();
    }

    private void AccentColorSourceComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isRefreshing)
            return;

        if (!_accentColorSourceComboBox.TryGetSelectedItem(out AccentColorSource state))
            return;

        _settings.Store.AccentColorSource = state;
        _settings.SynchronizeStore();

        UpdateAccentColorPicker();

        _themeManager.Apply();
    }

    private void UpdateAccentColorPicker()
    {
        _accentColorPicker.Visibility = _settings.Store.AccentColorSource == AccentColorSource.Custom ? Visibility.Visible : Visibility.Collapsed;
        _accentColorPicker.SelectedColor = _themeManager.GetAccentColor().ToColor();
    }

    private void AutorunComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isRefreshing)
            return;

        if (!_autorunComboBox.TryGetSelectedItem(out AutorunState state))
            return;

        Autorun.Set(state);
    }

    private void SmartFnLockComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isRefreshing)
            return;

        if (!_smartFnLockComboBox.TryGetSelectedItem(out ModifierKey modifierKey))
            return;

        _settings.Store.SmartFnLockFlags = modifierKey;
        _settings.SynchronizeStore();
    }

    private void SmartKeySinglePressActionCard_Click(object sender, RoutedEventArgs e)
    {
        if (_isRefreshing)
            return;

        var window = new SelectSmartKeyPipelinesWindow { Owner = Window.GetWindow(this) };
        window.ShowDialog();
    }

    private void SmartKeyDoublePressActionCard_Click(object sender, RoutedEventArgs e)
    {
        if (_isRefreshing)
            return;

        var window = new SelectSmartKeyPipelinesWindow(isDoublePress: true) { Owner = Window.GetWindow(this) };
        window.ShowDialog();
    }

    private void MinimizeToTrayToggle_Click(object sender, RoutedEventArgs e)
    {
        if (_isRefreshing)
            return;

        var state = _minimizeToTrayToggle.IsChecked;
        if (state is null)
            return;

        _settings.Store.MinimizeToTray = state.Value;
        _settings.SynchronizeStore();
    }

    private void UseNewSensorDashboard_Toggle(object sender, RoutedEventArgs e)
    {
        if (_isRefreshing)
            return;

        var state = _useNewSensorDashboardToggle.IsChecked;
        if (state is null)
            return;

        var feature = IoCContainer.Resolve<SensorsGroupController>();

        if (state.Value && !PawnIOHelper.IsPawnIOInnstalled())
        {
            var dialog = new DialogWindow
            {
                Title = Resource.MainWindow_PawnIO_Warning_Title,
                Content = Resource.MainWindow_PawnIO_Warning_Message,
                Owner = Application.Current.MainWindow
            };

            dialog.ShowDialog();

            if (dialog.Result.Item1)
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "https://pawnio.eu/",
                    UseShellExecute = true
                });
                SnackbarHelper.Show(Resource.SettingsPage_UseNewDashboard_Switch_Title,
                                  Resource.SettingsPage_UseNewDashboard_Restart_Message,
                                  SnackbarType.Success);
                _settings.Store.UseNewSensorDashboard = state.Value;
                _settings.SynchronizeStore();
            }
            else
            {
                _useNewSensorDashboardToggle.IsChecked = false;
                _settings.Store.UseNewSensorDashboard = false;
                _settings.SynchronizeStore();
            }
        }
        else
        {
            SnackbarHelper.Show(Resource.SettingsPage_UseNewDashboard_Switch_Title,
                                  Resource.SettingsPage_UseNewDashboard_Restart_Message,
                                  SnackbarType.Success);
            _settings.Store.UseNewSensorDashboard = state.Value;
            _settings.SynchronizeStore();
        }
    }

    private async void EnableLoggingToggle_Click(object sender, RoutedEventArgs e)
    {
        if (_isRefreshing)
            return;

        if (App.Current.MainWindow is not MainWindow mainWindow)
            return;

        var state = _enableLoggingToggle.IsChecked;
        if (state is null)
            return;

        const string logSuffix = " [LOGGING ENABLED]";

        await mainWindow.InvokeIfRequired(() =>
        {
            if (state.Value)
            {
                if (!mainWindow._title.Text.EndsWith(logSuffix))
                {
                    mainWindow._title.Text += logSuffix;
                }
            }
            else
            {
                mainWindow._title.Text = mainWindow._title.Text.Replace(logSuffix, string.Empty);
            }
        });

        Log.Instance.IsTraceEnabled = state.Value;
        _settings.Store.EnableLogging = state.Value;
        _settings.SynchronizeStore();

        mainWindow._openLogIndicator.Visibility = Utils.BooleanToVisibilityConverter.Convert(_settings.Store.EnableLogging);
    }

    private void LockWindowSizeToggle_Click(object sender, RoutedEventArgs e)
    {
        if (_isRefreshing)
            return;

        var state = _lockWindowSizeToggle.IsChecked;
        if (state is null)
            return;

        _settings.Store.LockWindowSize = state.Value;
        _settings.SynchronizeStore();
    }

    private void MinimizeOnCloseToggle_Click(object sender, RoutedEventArgs e)
    {
        if (_isRefreshing)
            return;

        var state = _minimizeOnCloseToggle.IsChecked;
        if (state is null)
            return;

        _settings.Store.MinimizeOnClose = state.Value;
        _settings.SynchronizeStore();
    }

    private async void VantageToggle_Click(object sender, RoutedEventArgs e)
    {
        if (_isRefreshing)
            return;

        _vantageToggle.IsEnabled = false;

        var state = _vantageToggle.IsChecked;
        if (state is null)
            return;

        if (state.Value)
        {
            try
            {
                await _vantageDisabler.DisableAsync();
            }
            catch
            {
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
            return;

        if (state.Value)
        {
            try
            {
                await _legionZoneDisabler.DisableAsync();
            }
            catch
            {
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
            return;

        if (state.Value)
        {
            try
            {
                await _legionSpaceDisabler.DisableAsync();
            }
            catch
            {
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
                await SnackbarHelper.ShowAsync(Resource.SettingsPage_EnableLegionSpace_Error_Title, Resource.SettingsPage_EnableLegionSpace_Error_Message, SnackbarType.Error);
                return;
            }
        }

        _legionSpaceToggle.IsEnabled = true;
    }

    private async void FnKeysToggle_Click(object sender, RoutedEventArgs e)
    {
        if (_isRefreshing)
            return;

        _fnKeysToggle.IsEnabled = false;

        var state = _fnKeysToggle.IsChecked;
        if (state is null)
            return;

        if (state.Value)
        {
            try
            {
                await _fnKeysDisabler.DisableAsync();
            }
            catch
            {
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
                await SnackbarHelper.ShowAsync(Resource.SettingsPage_EnableLenovoHotkeys_Error_Title, Resource.SettingsPage_EnableLenovoHotkeys_Error_Message, SnackbarType.Error);
                return;
            }
        }

        _fnKeysToggle.IsEnabled = true;

        _smartKeySinglePressActionCard.Visibility = state.Value ? Visibility.Visible : Visibility.Collapsed;
        _smartKeyDoublePressActionCard.Visibility = state.Value ? Visibility.Visible : Visibility.Collapsed;
        _notificationsCard.Visibility = state.Value ? Visibility.Visible : Visibility.Collapsed;
        _excludeRefreshRatesCard.Visibility = state.Value ? Visibility.Visible : Visibility.Collapsed;
    }

    private void NotificationsCard_Click(object sender, RoutedEventArgs e)
    {
        if (_isRefreshing)
            return;

        var window = new NotificationsSettingsWindow { Owner = Window.GetWindow(this) };
        window.ShowDialog();
    }

    private void ExcludeRefreshRates_Click(object sender, RoutedEventArgs e)
    {
        if (_isRefreshing)
            return;

        var window = new ExcludeRefreshRatesWindow { Owner = Window.GetWindow(this) };
        window.ShowDialog();
    }

    private void SynchronizeBrightnessToAllPowerPlansToggle_Click(object sender, RoutedEventArgs e)
    {
        if (_isRefreshing)
            return;

        var state = _synchronizeBrightnessToAllPowerPlansToggle.IsChecked;
        if (state is null)
            return;

        _settings.Store.SynchronizeBrightnessToAllPowerPlans = state.Value;
        _settings.SynchronizeStore();
    }

    private void BootLogo_Click(object sender, RoutedEventArgs e)
    {
        if (_isRefreshing)
            return;

        var window = new BootLogoWindow { Owner = Window.GetWindow(this) };
        window.ShowDialog();
    }

    private async void CheckUpdates_Click(object sender, RoutedEventArgs e)
    {
        if (_isRefreshing)
            return;

        if (App.Current.MainWindow is not MainWindow mainWindow)
            return;

        mainWindow.CheckForUpdates(true);
        await SnackbarHelper.ShowAsync(Resource.SettingsPage_CheckUpdates_Started_Title, type: SnackbarType.Info);
    }

    private void UpdateCheckFrequencyComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isRefreshing)
            return;

        if (!_updateCheckFrequencyComboBox.TryGetSelectedItem(out UpdateCheckFrequency frequency))
            return;

        _updateCheckSettings.Store.UpdateCheckFrequency = frequency;
        _updateCheckSettings.SynchronizeStore();
        _updateChecker.UpdateMinimumTimeSpanForRefresh();
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
        _powerModesCard.Visibility = _settings.Store.PowerModeMappingMode == PowerModeMappingMode.WindowsPowerMode && isPowerModeFeatureSupported ? Visibility.Visible : Visibility.Collapsed;
        _windowsPowerPlansCard.Visibility = _settings.Store.PowerModeMappingMode == PowerModeMappingMode.WindowsPowerPlan && isPowerModeFeatureSupported ? Visibility.Visible : Visibility.Collapsed;
        _windowsPowerPlansControlPanelCard.Visibility = _settings.Store.PowerModeMappingMode == PowerModeMappingMode.WindowsPowerPlan && isPowerModeFeatureSupported ? Visibility.Visible : Visibility.Collapsed;
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

    private async void HWiNFOIntegrationToggle_Click(object sender, RoutedEventArgs e)
    {
        if (_isRefreshing)
            return;

        _integrationsSettings.Store.HWiNFO = _hwinfoIntegrationToggle.IsChecked ?? false;
        _integrationsSettings.SynchronizeStore();

        await _hwinfoIntegration.StartStopIfNeededAsync();
    }

    private async void CLIInterfaceToggle_Click(object sender, RoutedEventArgs e)
    {
        if (_isRefreshing)
            return;

        _integrationsSettings.Store.CLI = _cliInterfaceToggle.IsChecked ?? false;
        _integrationsSettings.SynchronizeStore();

        await _ipcServer.StartStopIfNeededAsync();
    }

    private void CLIPathToggle_Click(object sender, RoutedEventArgs e)
    {
        if (_isRefreshing)
            return;

        SystemPath.SetCLI(_cliPathToggle.IsChecked ?? false);
    }

    private void UpdateMethodComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isRefreshing)
            return;

        if (!__updateMethodComboBox.TryGetSelectedItem(out UpdateMethod updateMethod))
            return;

        _settings.Store.UpdateMethod = updateMethod;
        _settings.SynchronizeStore();
    }

    private void FloatingGadgets_Click(object sender, RoutedEventArgs e)
    {
        if (_isRefreshing)
            return;

        try
        {
            var state = _floatingGadgetsToggle.IsChecked;
            if (state is null)
                return;

            if (state.Value)
            {
                if (!PawnIOHelper.IsPawnIOInnstalled())
                {
                    var dialog = new DialogWindow
                    {
                        Title = Resource.MainWindow_PawnIO_Warning_Title,
                        Content = Resource.MainWindow_PawnIO_Warning_Message,
                        Owner = Application.Current.MainWindow
                    };

                    dialog.ShowDialog();

                    if (dialog.Result.Item1)
                    {
                        Process.Start(new ProcessStartInfo
                        {
                            FileName = "https://pawnio.eu/",
                            UseShellExecute = true
                        });
                    }

                    _floatingGadgetsToggle.IsChecked = false;
                    _settings.Store.ShowFloatingGadgets = false;
                    _settings.SynchronizeStore();
                    return;
                }
            }

            Window? floatingGadget = null;

            if (state.Value)
            {
                if (App.Current.FloatingGadget == null)
                {
                    if (_settings.Store.SelectedStyleIndex == 0)
                    {
                        floatingGadget = new FloatingGadget();
                    }
                    else if (_settings.Store.SelectedStyleIndex == 1)
                    {
                        floatingGadget = new FloatingGadgetUpper();
                    }

                    if (floatingGadget != null)
                    {
                        App.Current.FloatingGadget = floatingGadget;
                        App.Current.FloatingGadget.Show();
                    }
                }
                else
                {
                    bool needsStyleUpdate = false;

                    if (_settings.Store.SelectedStyleIndex == 0 && App.Current.FloatingGadget.GetType() != typeof(FloatingGadget))
                    {
                        needsStyleUpdate = true;
                    }
                    else if (_settings.Store.SelectedStyleIndex == 1 && App.Current.FloatingGadget.GetType() != typeof(FloatingGadgetUpper))
                    {
                        needsStyleUpdate = true;
                    }

                    if (needsStyleUpdate)
                    {
                        App.Current.FloatingGadget.Close();

                        if (_settings.Store.SelectedStyleIndex == 0)
                        {
                            floatingGadget = new FloatingGadget();
                        }
                        else if (_settings.Store.SelectedStyleIndex == 1)
                        {
                            floatingGadget = new FloatingGadgetUpper();
                        }

                        if (floatingGadget != null)
                        {
                            App.Current.FloatingGadget = floatingGadget;
                            App.Current.FloatingGadget.Show();
                        }
                    }
                    else
                    {
                        if (!App.Current.FloatingGadget.IsVisible)
                        {
                            App.Current.FloatingGadget.Show();
                        }
                    }
                }
            }
            else
            {
                if (App.Current.FloatingGadget != null)
                {
                    App.Current.FloatingGadget.Hide();
                }
            }

            _settings.Store.ShowFloatingGadgets = state.Value;
            _settings.SynchronizeStore();
        }
        catch (Exception ex)
        {
            Log.Instance.Trace($"FloatingGadgets_Click error: {ex.Message}");

            _floatingGadgetsToggle.IsChecked = false;
            _settings.Store.ShowFloatingGadgets = false;
            _settings.SynchronizeStore();
        }
    }

    private void FloatingGadgetsInput_ValueChanged(object sender, RoutedEventArgs e)
    {
        if (_isRefreshing)
            return;

        _settings.Store.FloatingGadgetsRefreshInterval = int.TryParse(_floatingGadgetsInterval.Text, out var interval) ? interval : 1;
        _settings.SynchronizeStore();
    }

    private void SelectBackgroundImageButton_Click(object sender, RoutedEventArgs e)
    {
        if (_isRefreshing)
            return;

        var state = _onBatterySinceResetToggle.IsChecked;
        if (state is null)
            return;

        OpenFileDialog openFileDialog = new OpenFileDialog
        {
            Filter = $"{Resource.SettingsPage_Select_BackgroundImage_ImageFile}|*.jpg;*.jpeg;*.png;*.bmp|{Resource.SettingsPage_Select_BackgroundImage_AllFiles}|*.*",
            Title = $"{Resource.SettingsPage_Select_BackgroundImage_ImageFile}"
        };

        string filePath = string.Empty;
        try
        {
            if (openFileDialog.ShowDialog() == true)
            {
                filePath = openFileDialog.FileName;
                App.MainWindowInstance!.SetMainWindowBackgroundImage(filePath);

                _settings.Store.BackGroundImageFilePath = filePath;
                _settings.SynchronizeStore();
            }
        }
        catch (Exception ex)
        {
            SnackbarHelper.Show(Resource.Warning, ex.Message, SnackbarType.Error);
            Log.Instance.Trace($"Exception occured when executing SetBackgroundImage().", ex);
        }
    }

    private void OpacitySlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_isRefreshing)
            return;

        App.MainWindowInstance!.SetWindowOpacity(e.NewValue);
        _settings.Store.Opacity = e.NewValue;
        _settings.SynchronizeStore();
    }

    private void ClearBackgroundImageButton_Click(object sender, RoutedEventArgs e)
    {
        if (_isRefreshing)
            return;

        SnackbarHelper.Show(Resource.SettingsPage_ClearBackgroundImage_Title, Resource.SettingsPage_UseNewDashboard_Restart_Message, SnackbarType.Success);

        _settings.Store.BackGroundImageFilePath = string.Empty;
        _settings.SynchronizeStore();
    }

    private void StyleComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isRefreshing)
            return;

        try
        {
            _settings.Store.SelectedStyleIndex = _floatingGadgetsStyleComboBox.SelectedIndex;
            _settings.SynchronizeStore();

            if (_settings.Store.ShowFloatingGadgets && App.Current.FloatingGadget != null)
            {
                var styleTypeMapping = new Dictionary<int, Type>
                {
                    [0] = typeof(FloatingGadget),
                    [1] = typeof(FloatingGadgetUpper)
                };

                var constructorMapping = new Dictionary<int, Func<Window>>
                {
                    [0] = () => new FloatingGadget(),
                    [1] = () => new FloatingGadgetUpper()
                };

                int selectedStyle = _settings.Store.SelectedStyleIndex;
                if (styleTypeMapping.TryGetValue(selectedStyle, out Type? targetType) &&
                    App.Current.FloatingGadget.GetType() != targetType)
                {
                    App.Current.FloatingGadget.Close();

                    if (constructorMapping.TryGetValue(selectedStyle, out Func<Window>? constructor))
                    {
                        App.Current.FloatingGadget = constructor();
                        App.Current.FloatingGadget.Show();
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Log.Instance.Trace($"StyleComboBox_SelectionChanged error: {ex.Message}");

            _isRefreshing = true;
            _floatingGadgetsStyleComboBox.SelectedIndex = _settings.Store.SelectedStyleIndex;
            _isRefreshing = false;
        }
    }

    private void CustomButton_Click(object sender, RoutedEventArgs e)
    {
        if (_isRefreshing)
            return;

        CustomWindow = ShowOrActivateWindow(CustomWindow,
            () => new Custom { Owner = Window.GetWindow(this) },
            w => w.BringToForeground());
    }

    private void DashboardCustomButton_Click(object sender, RoutedEventArgs e)
    {
        if (_isRefreshing)
            return;

        EditSensorGroupWindow = ShowOrActivateWindow(EditSensorGroupWindow,
            () => new EditSensorGroupWindow { Owner = Window.GetWindow(this) },
            w => w.BringToForeground());
    }

    private void ArgumentWindowButton_Click(object sender, RoutedEventArgs e)
    {
        if (_isRefreshing)
            return;

        ArgumentWindow.ShowInstance();
    }

    #region Helper
    private static bool IsWindowValid(Window? window)
    {
        return window is not null
        && !window.Dispatcher.HasShutdownStarted
        && !window.Dispatcher.HasShutdownFinished
        && window.IsLoaded;
    }

    private T ShowOrActivateWindow<T>(T? window, Func<T> factory, Action<T>? bringToForeground = null)
    where T : Window
    {
        if (window is null || !IsWindowValid(window))
            window = factory();

        if (window.IsVisible)
        {
            if (window.WindowState == WindowState.Minimized)
                window.WindowState = WindowState.Normal;

            try
            {
                if (bringToForeground is not null && window.ShowActivated)
                {
                    bringToForeground(window);
                }
                else
                {
                    window.Activate();
                }
            }
            catch { }

            return window;
        }

        try
        {
            window.Show();
            return window;
        }
        catch (InvalidOperationException)
        {
            window = factory();
            window.Show();
            return window;
        }
    }
    #endregion
}
