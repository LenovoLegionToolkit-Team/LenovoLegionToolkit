using System;
using System.Collections.Generic;
using System.Diagnostics;
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
using LenovoLegionToolkit.WPF.Windows.FloatingGadgets;
using LenovoLegionToolkit.WPF.Windows.Settings;
using LenovoLegionToolkit.WPF.Windows.Utils;
using CustomGadgetWindow = LenovoLegionToolkit.WPF.Windows.FloatingGadgets.Custom;

namespace LenovoLegionToolkit.WPF.Controls.Settings;

public partial class SettingsAppBehaviorControl
{
    private readonly ApplicationSettings _settings = IoCContainer.Resolve<ApplicationSettings>();
    private readonly DashboardSettings _dashboardSettings = IoCContainer.Resolve<DashboardSettings>();
    private readonly AutomationProcessor _automationProcessor = IoCContainer.Resolve<AutomationProcessor>();
    private readonly FloatingGadgetSettings _floatingGadgetSettings = IoCContainer.Resolve<FloatingGadgetSettings>();

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
        _enableLoggingToggle.IsChecked = _settings.Store.EnableLogging;
        _highPerformanceUIToggle.IsChecked = _settings.Store.HighPerformanceUI;
        _keyboardBacklightTimeoutSlider.Value = _settings.Store.KeyboardBacklightTimeout;
        UpdateKeyboardTimeoutText(_settings.Store.KeyboardBacklightTimeout);

        // Game Detection
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

        // Floating Gadgets
        _floatingGadgetsToggle.IsChecked = _floatingGadgetSettings.Store.ShowFloatingGadgets;

        _autorunComboBox.Visibility = Visibility.Visible;
        _minimizeToTrayToggle.Visibility = Visibility.Visible;
        _minimizeOnCloseToggle.Visibility = Visibility.Visible;
        _enableLoggingToggle.Visibility = Visibility.Visible;
        _useNewSensorDashboardToggle.Visibility = Visibility.Visible;
        _hardwareSensorsToggle.Visibility = Visibility.Visible;
        _lockWindowSizeToggle.Visibility = Visibility.Visible;
        _floatingGadgetsToggle.Visibility = Visibility.Visible;
        _highPerformanceUIToggle.Visibility = Visibility.Visible;
        _keyboardBacklightTimeoutSlider.Visibility = Visibility.Visible;

        _hardwareSensorsToggle.IsChecked = _settings.Store.EnableHardwareSensors;

        if (_settings.Store.EnableHardwareSensors)
        {
            _useNewSensorDashboardCardControl.Visibility = Visibility.Visible;
            _floatingGadgetsCardControl.Visibility = Visibility.Visible;
        }
        else
        {
            _useNewSensorDashboardCardControl.Visibility = Visibility.Collapsed;
            _floatingGadgetsCardControl.Visibility = Visibility.Collapsed;
        }

        if (PawnIOHelper.IsPawnIOInstalled())
        {
            _hardwareSensorsCardHeader.Warning = string.Empty;
        }
        else
        {
            _hardwareSensorsCardHeader.Warning = Resource.SettingsPage_HardwareSensors_PawnIOWarning;
        }

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
                var result = await MessageBoxHelper.ShowAsync(
                    this,
                    Resource.MainWindow_PawnIO_Warning_Title,
                    Resource.MainWindow_PawnIO_Warning_Message,
                    Resource.Yes,
                    Resource.No);

                if (result)
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = "https://pawnio.eu/",
                        UseShellExecute = true
                    });
                }

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
            
            _floatingGadgetsToggle.IsChecked = false;
            _floatingGadgetSettings.Store.ShowFloatingGadgets = false;
            if (App.Current.FloatingGadget != null)
            {
                App.Current.FloatingGadget.Hide();
            }
        }

        _settings.Store.EnableHardwareSensors = state.Value;
        _settings.SynchronizeStore();
        _floatingGadgetSettings.SynchronizeStore();
        
        _useNewSensorDashboardCardControl.Visibility = state.Value ? Visibility.Visible : Visibility.Collapsed;
        _floatingGadgetsCardControl.Visibility = state.Value ? Visibility.Visible : Visibility.Collapsed;
        
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

    private void FloatingGadgets_Click(object sender, RoutedEventArgs e)
    {
        e.Handled = true;

        if (_isRefreshing || !IsLoaded)
            return;

        try
        {
            var state = _floatingGadgetsToggle.IsChecked;
            if (state is null)
                return;

            Window? floatingGadget = null;

            if (state.Value)
            {
                if (App.Current.FloatingGadget == null)
                {
                    if (_floatingGadgetSettings.Store.SelectedStyleIndex == 0)
                    {
                        floatingGadget = new FloatingGadget();
                    }
                    else if (_floatingGadgetSettings.Store.SelectedStyleIndex == 1)
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

                    if (_floatingGadgetSettings.Store.SelectedStyleIndex == 0 && App.Current.FloatingGadget.GetType() != typeof(FloatingGadget))
                    {
                        needsStyleUpdate = true;
                    }
                    else if (_floatingGadgetSettings.Store.SelectedStyleIndex == 1 && App.Current.FloatingGadget.GetType() != typeof(FloatingGadgetUpper))
                    {
                        needsStyleUpdate = true;
                    }

                    if (needsStyleUpdate)
                    {
                        App.Current.FloatingGadget.Close();

                        if (_floatingGadgetSettings.Store.SelectedStyleIndex == 0)
                        {
                            floatingGadget = new FloatingGadget();
                        }
                        else if (_floatingGadgetSettings.Store.SelectedStyleIndex == 1)
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

            _floatingGadgetSettings.Store.ShowFloatingGadgets = state.Value;
            _floatingGadgetSettings.SynchronizeStore();
        }
        catch (Exception ex)
        {
            Log.Instance.Trace($"FloatingGadgets_Click error: {ex.Message}");

            _floatingGadgetsToggle.IsChecked = false;
            _floatingGadgetSettings.Store.ShowFloatingGadgets = false;
            _floatingGadgetSettings.SynchronizeStore();
        }
    }



    private void CustomButton_Click(object sender, RoutedEventArgs e)
    {
        if (_isRefreshing || !IsLoaded)
            return;

        CustomGadgetWindow.ShowInstance();
    }
    
    private void SensorSettingsButton_Click(object sender, RoutedEventArgs e)
    {
        if (_isRefreshing || !IsLoaded)
            return;

        var window = new SensorSettingsWindow { Owner = Window.GetWindow(this) };
        window.ShowDialog();
    }

    private void HighPerformanceUIToggle_Click(object sender, RoutedEventArgs e)
    {
        if (_isRefreshing || !IsLoaded)
            return;

        var state = _highPerformanceUIToggle.IsChecked;
        if (state is null)
            return;

        _settings.Store.HighPerformanceUI = state.Value;
        _settings.SynchronizeStore();
    }

    private void KeyboardBacklightTimeoutSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_isRefreshing || !IsLoaded)
            return;

        var value = (int)e.NewValue;
        _settings.Store.KeyboardBacklightTimeout = value;
        _settings.SynchronizeStore();
        UpdateKeyboardTimeoutText(value);
    }

    private void UpdateKeyboardTimeoutText(int seconds)
    {
        _keyboardBacklightTimeoutText.Text = seconds == 0 ? "0 (Off)" : $"{seconds}s";
    }
}
