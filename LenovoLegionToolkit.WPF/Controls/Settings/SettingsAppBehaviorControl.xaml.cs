using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using LenovoLegionToolkit.Lib;
using LenovoLegionToolkit.Lib.Automation;
using LenovoLegionToolkit.Lib.Controllers.Sensors;
using LenovoLegionToolkit.Lib.Extensions;
using LenovoLegionToolkit.Lib.Messaging;
using LenovoLegionToolkit.Lib.Messaging.Messages;
using LenovoLegionToolkit.Lib.Settings;
using LenovoLegionToolkit.Lib.System;
using LenovoLegionToolkit.Lib.Utils;
using LenovoLegionToolkit.WPF.Extensions;
using LenovoLegionToolkit.WPF.Resources;
using LenovoLegionToolkit.WPF.Settings;
using LenovoLegionToolkit.WPF.Utils;
using LenovoLegionToolkit.WPF.Windows;
using LenovoLegionToolkit.WPF.Windows.Dashboard;
using LenovoLegionToolkit.WPF.Windows.Osd;
using LenovoLegionToolkit.WPF.Windows.Settings;
using LenovoLegionToolkit.WPF.Windows.Utils;
using OsdSettingsAlias = LenovoLegionToolkit.WPF.Windows.Osd.OsdSettingsWindow;

namespace LenovoLegionToolkit.WPF.Controls.Settings;

public partial class SettingsAppBehaviorControl
{
    private readonly ApplicationSettings _settings = IoCContainer.Resolve<ApplicationSettings>();
    private readonly DashboardSettings _dashboardSettings = IoCContainer.Resolve<DashboardSettings>();
    private readonly AutomationProcessor _automationProcessor = IoCContainer.Resolve<AutomationProcessor>();
    private readonly OsdSettings _OsdSettings = IoCContainer.Resolve<OsdSettings>();
    private readonly SensorsControlSettings _sensorsControlSettings = IoCContainer.Resolve<SensorsControlSettings>();
    private readonly HardwareSensorSettings _hardwareSensorSettings = IoCContainer.Resolve<HardwareSensorSettings>();

    private bool _isRefreshing = true;

    public SettingsAppBehaviorControl()
    {
        InitializeComponent();
    }

    public Task RefreshAsync()
    {
        _isRefreshing = true;

        _autorunComboBox.SetItems(Enum.GetValues<AutorunState>(), Autorun.State, t => t.GetDisplayName());
        _minimizeToTrayToggle.IsChecked = _settings.Store.MinimizeToTray;
        _minimizeOnCloseToggle.IsChecked = _settings.Store.MinimizeOnClose;
        _useNewSensorDashboardToggle.IsChecked = _settings.Store.UseNewSensorDashboard;
        _lockWindowSizeToggle.IsChecked = _settings.Store.LockWindowSize;
        _alwaysOnTopToggle.IsChecked = _settings.Store.AlwaysOnTop;
        _compactModeToggle.IsChecked = _settings.Store.CompactMode;
        _enableLoggingToggle.IsChecked = _settings.Store.EnableLogging;

        var useGpu = _settings.Store.GameDetection.UseDiscreteGPU;
        var useStore = _settings.Store.GameDetection.UseGameConfigStore;
        var useGameMode = _settings.Store.GameDetection.UseEffectiveGameMode;

        ComboBoxItem? selectedItem;
        if (useGpu && useStore && useGameMode)
            selectedItem = _detectionModeComboBox.Items[0] as ComboBoxItem;
        else if (useGpu && !useStore && !useGameMode)
            selectedItem = _detectionModeComboBox.Items[1] as ComboBoxItem;
        else if (!useGpu && useStore && !useGameMode)
            selectedItem = _detectionModeComboBox.Items[2] as ComboBoxItem;
        else if (!useGpu && !useStore && useGameMode)
            selectedItem = _detectionModeComboBox.Items[3] as ComboBoxItem;
        else
            selectedItem = _detectionModeComboBox.Items[0] as ComboBoxItem;

        _detectionModeComboBox.SelectedItem = selectedItem;

        _osdToggle.IsChecked = _OsdSettings.Store.ShowOsd;

        _autorunComboBox.Visibility = Visibility.Visible;
        _minimizeToTrayToggle.Visibility = Visibility.Visible;
        _minimizeOnCloseToggle.Visibility = Visibility.Visible;
        _enableLoggingToggle.Visibility = Visibility.Visible;
        _useNewSensorDashboardToggle.Visibility = Visibility.Visible;
        _hardwareSensorsToggle.Visibility = Visibility.Visible;
        _lockWindowSizeToggle.Visibility = Visibility.Visible;
        _alwaysOnTopToggle.Visibility = Visibility.Visible;
        _compactModeToggle.Visibility = Visibility.Visible;
        _osdToggle.Visibility = Visibility.Visible;

        _hardwareSensorsToggle.IsChecked = _settings.Store.EnableHardwareSensors;

        if (_settings.Store.EnableHardwareSensors)
        {
            _useNewSensorDashboardCardControl.Visibility = Visibility.Visible;
            _osdCardControl.Visibility = Visibility.Visible;
        }
        else
        {
            _useNewSensorDashboardCardControl.Visibility = Visibility.Collapsed;
            _osdCardControl.Visibility = Visibility.Collapsed;
        }

        if (PawnIOHelper.IsPawnIOInstalled())
        {
            _hardwareSensorsCardHeader.Warning = string.Empty;
        }
        else
        {
            _hardwareSensorsCardHeader.Warning = Resource.SettingsPage_HardwareSensors_PawnIOWarning;
        }

        _scriptConsoleCard.Visibility = !(AppFlags.Instance.Debug = true) ? Visibility.Hidden : Visibility.Visible;

        _isRefreshing = false;

        return Task.CompletedTask;
    }

    private void AutorunComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isRefreshing || !IsLoaded)
            return;

        if (!_autorunComboBox.TryGetSelectedItem(out AutorunState state))
            return;

        Autorun.Set(state);
    }

    private void MinimizeToTrayToggle_Click(object sender, RoutedEventArgs e)
    {
        if (_isRefreshing || !IsLoaded)
            return;

        var state = _minimizeToTrayToggle.IsChecked;
        if (state is null)
            return;

        _settings.Store.MinimizeToTray = state.Value;
        _settings.SynchronizeStore();
    }

    private void MinimizeOnCloseToggle_Click(object sender, RoutedEventArgs e)
    {
        if (_isRefreshing || !IsLoaded)
            return;

        var state = _minimizeOnCloseToggle.IsChecked;
        if (state is null)
            return;

        _settings.Store.MinimizeOnClose = state.Value;
        _settings.SynchronizeStore();
    }

    private void LockWindowSizeToggle_Click(object sender, RoutedEventArgs e)
    {
        if (_isRefreshing || !IsLoaded)
            return;

        var state = _lockWindowSizeToggle.IsChecked;
        if (state is null)
            return;

        _settings.Store.LockWindowSize = state.Value;
        _settings.SynchronizeStore();

        App.MainWindowInstance?.ApplyWindowLock(state.Value);
    }

    private void CompactModeToggle_Click(object sender, RoutedEventArgs e)
    {
        if (_isRefreshing || !IsLoaded)
            return;

        var state = _compactModeToggle.IsChecked;
        if (state is null)
            return;

        _settings.Store.CompactMode = state.Value;
        _settings.SynchronizeStore();

        SnackbarHelper.Show(Resource.SettingsPage_CompactMode_Title, Resource.SettingsPage_RestartRequired_Message, SnackbarType.Success);
    }

    private void AlwaysOnTopToggle_Click(object sender, RoutedEventArgs e)
    {
        if (_isRefreshing || !IsLoaded)
            return;

        var state = _alwaysOnTopToggle.IsChecked;
        if (state is null)
            return;

        _settings.Store.AlwaysOnTop = state.Value;
        _settings.SynchronizeStore();

        if (App.MainWindowInstance is { } w)
            w.Topmost = state.Value;
    }

    private async void EnableLoggingToggle_Click(object sender, RoutedEventArgs e)
    {
        if (_isRefreshing || !IsLoaded)
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
        AppFlags.Instance.IsTraceEnabled = state.Value;
        _settings.Store.EnableLogging = state.Value;
        _settings.SynchronizeStore();

        await Compatibility.PrintMachineInfoAsync().ConfigureAwait(false);

        mainWindow._openLogIndicator.Visibility = Utils.BooleanToVisibilityConverter.Convert(_settings.Store.EnableLogging);
    }

    private void NotificationsCard_Click(object sender, RoutedEventArgs e)
    {
        if (_isRefreshing || !IsLoaded)
            return;

        var window = new NotificationsSettingsWindow { Owner = Window.GetWindow(this) };
        window.ShowDialog();
    }

    private void BackupSettings_Click(object sender, RoutedEventArgs e)
    {
        if (_isRefreshing || !IsLoaded)
            return;

        var window = new SettingsBackupWindow { Owner = Window.GetWindow(this) };
        window.ShowDialog();
    }

    private async void HardwareSensors_Toggle(object sender, RoutedEventArgs e)
    {
        e.Handled = true;

        if (_isRefreshing || !IsLoaded)
            return;

        var state = _hardwareSensorsToggle.IsChecked;
        if (state is null)
            return;

        if (state.Value)
        {
            if (!PawnIOHelper.IsPawnIOInstalled())
            {
                await PawnIOHelper.TryShowPawnIONotFoundDialogAsync().ConfigureAwait(false);

                _hardwareSensorsToggle.IsChecked = false;
                return;
            }

            var sensorsController = IoCContainer.Resolve<SensorsGroupController>();
            if (!sensorsController.IsLibreHardwareMonitorInitialized())
            {
                await sensorsController.IsSupportedAsync();
            }
        }
        else
        {
            _useNewSensorDashboardToggle.IsChecked = false;
            _settings.Store.UseNewSensorDashboard = false;
            
            _osdToggle.IsChecked = false;
            _OsdSettings.Store.ShowOsd = false;
            if (App.Current.OsdWindow != null)
            {
                App.Current.OsdWindow.Hide();
            }
            _OsdSettings.SynchronizeStore();
        }

        _settings.Store.EnableHardwareSensors = state.Value;
        _settings.SynchronizeStore();

        if (state.Value)
        {
            _hardwareSensorSettings.EnsureFileExists();
        }

        _useNewSensorDashboardCardControl.Visibility = state.Value ? Visibility.Visible : Visibility.Collapsed;
        _osdCardControl.Visibility = state.Value ? Visibility.Visible : Visibility.Collapsed;
        
        MessagingCenter.Publish(new SensorDashboardSwappedMessage());
    }

    private async void UseNewSensorDashboard_Toggle(object sender, RoutedEventArgs e)
    {
        e.Handled = true;

        if (_isRefreshing || !IsLoaded)
            return;

        var state = _useNewSensorDashboardToggle.IsChecked;
        if (state is null)
            return;

        _settings.Store.UseNewSensorDashboard = state.Value;
        _settings.SynchronizeStore();

        if (state.Value)
        {
            _sensorsControlSettings.EnsureFileExists();
            _hardwareSensorSettings.EnsureFileExists();
        }

        MessagingCenter.Publish(new SensorDashboardSwappedMessage());
    }

    private void DashboardCustomButton_Click(object sender, RoutedEventArgs e)
    {
        if (_isRefreshing || !IsLoaded)
            return;

        EditSensorGroupWindow.ShowInstance();
    }

    private void DetectionModeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isRefreshing || !IsLoaded)
            return;

        if (_detectionModeComboBox.SelectedItem is not ComboBoxItem item || item.Tag is not string tag)
            return;

        var (useGpu, useStore, useGameMode) = tag switch
        {
            "Auto" => (true, true, true),
            "Gpu" => (true, false, false),
            "Store" => (false, true, false),
            "GameMode" => (false, false, true),
            _ => (true, true, true)
        };

        _settings.Store.GameDetection.UseDiscreteGPU = useGpu;
        _settings.Store.GameDetection.UseGameConfigStore = useStore;
        _settings.Store.GameDetection.UseEffectiveGameMode = useGameMode;
        _settings.SynchronizeStore();

        Task.Run(async () =>
        {
            try
            {
                await _automationProcessor.RestartListenersAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Log.Instance.Trace($"Failed to restart listeners after detection mode change.", ex);
            }
        });
    }

    private void ExcludeProcesses_Click(object sender, RoutedEventArgs e)
    {
        var window = new ExcludeProcessesWindow { Owner = Window.GetWindow(this) };
        window.ShowDialog();
    }

    private void ArgumentWindowButton_Click(object sender, RoutedEventArgs e)
    {
        if (_isRefreshing || !IsLoaded)
            return;

        ArgumentWindow.ShowInstance();
    }

    private void ScriptConsoleButton_Click(object sender, RoutedEventArgs e)
    {
        if (_isRefreshing || !IsLoaded)
            return;

        ScriptWindow.ShowInstance();
    }

    private void OsdToggle_Click(object sender, RoutedEventArgs e)
    {
        e.Handled = true;

        if (_isRefreshing || !IsLoaded)
            return;

        try
        {
            var state = _osdToggle.IsChecked;
            if (state is null)
                return;

            Window? OsdPanelWindow = null;

            if (state.Value)
            {
                if (App.Current.OsdWindow == null)
                {
                    if (_OsdSettings.Store.SelectedStyleIndex == 0)
                    {
                        OsdPanelWindow = new OsdPanelWindow();
                    }
                    else if (_OsdSettings.Store.SelectedStyleIndex == 1)
                    {
                        OsdPanelWindow = new OsdBarWindow();
                    }

                    if (OsdPanelWindow != null)
                    {
                        App.Current.OsdWindow = OsdPanelWindow;
                        App.Current.OsdWindow.Show();
                    }
                }
                else
                {
                    bool needsStyleUpdate = false;

                    if (_OsdSettings.Store.SelectedStyleIndex == 0 && App.Current.OsdWindow.GetType() != typeof(OsdPanelWindow))
                    {
                        needsStyleUpdate = true;
                    }
                    else if (_OsdSettings.Store.SelectedStyleIndex == 1 && App.Current.OsdWindow.GetType() != typeof(OsdBarWindow))
                    {
                        needsStyleUpdate = true;
                    }

                    if (needsStyleUpdate)
                    {
                        App.Current.OsdWindow.Close();

                        if (_OsdSettings.Store.SelectedStyleIndex == 0)
                        {
                            OsdPanelWindow = new OsdPanelWindow();
                        }
                        else if (_OsdSettings.Store.SelectedStyleIndex == 1)
                        {
                            OsdPanelWindow = new OsdBarWindow();
                        }

                        if (OsdPanelWindow != null)
                        {
                            App.Current.OsdWindow = OsdPanelWindow;
                            App.Current.OsdWindow.Show();
                        }
                    }
                    else
                    {
                        if (!App.Current.OsdWindow.IsVisible)
                        {
                            App.Current.OsdWindow.Show();
                        }
                    }
                }
            }
            else
            {
                if (App.Current.OsdWindow != null)
                {
                    App.Current.OsdWindow.Hide();
                }
            }

            _OsdSettings.Store.ShowOsd = state.Value;
            _OsdSettings.SynchronizeStore();
        }
        catch (Exception ex)
        {
            Log.Instance.Trace($"Osd_Click error: {ex.Message}");

            _osdToggle.IsChecked = false;
            _OsdSettings.Store.ShowOsd = false;
            _OsdSettings.SynchronizeStore();
        }
    }



    private void CustomButton_Click(object sender, RoutedEventArgs e)
    {
        if (_isRefreshing || !IsLoaded)
            return;

        OsdSettingsAlias.ShowInstance();
    }
    
    private void SensorSettingsButton_Click(object sender, RoutedEventArgs e)
    {
        if (_isRefreshing || !IsLoaded)
            return;

        var window = new SensorSettingsWindow { Owner = Window.GetWindow(this) };
        window.ShowDialog();
    }

    private async void ResetSettings_Click(object sender, RoutedEventArgs e)
    {
        if (_isRefreshing || !IsLoaded)
            return;

        var confirmed = await MessageBoxHelper.ShowAsync(
            Resource.SettingsPage_ResetSettings_Title,
            Resource.SettingsPage_ResetSettings_Confirm
        );

        if (!confirmed)
            return;

        try
        {
            App.IsRestoringSettings = true;

            var files = Directory.GetFiles(Folders.AppData);
            foreach (var file in files)
            {
                try
                {
                    File.Delete(file);
                }
                catch (Exception ex)
                {
                    Log.Instance.Trace($"Failed to delete {file} during reset.", ex);
                }
            }

            Process.Start(new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = true });
            Application.Current.Shutdown();
        }
        catch (Exception ex)
        {
            Log.Instance.Trace($"Failed to reset settings.", ex);
        }
    }
}
