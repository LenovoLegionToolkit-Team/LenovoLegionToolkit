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
using LenovoLegionToolkit.Lib.Settings;
using LenovoLegionToolkit.Lib.System;
using LenovoLegionToolkit.Lib.Utils;
using LenovoLegionToolkit.WPF.Extensions;
using LenovoLegionToolkit.WPF.Resources;
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
    private readonly AutomationProcessor _automationProcessor = IoCContainer.Resolve<AutomationProcessor>();

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
        _floatingGadgetsToggle.IsChecked = _settings.Store.ShowFloatingGadgets;
        _floatingGadgetsStyleComboBox.SelectedIndex = _settings.Store.SelectedStyleIndex;
        _floatingGadgetsInterval.Value = _settings.Store.FloatingGadgetsRefreshInterval;

        _autorunComboBox.Visibility = Visibility.Visible;
        _minimizeToTrayToggle.Visibility = Visibility.Visible;
        _minimizeOnCloseToggle.Visibility = Visibility.Visible;
        _enableLoggingToggle.Visibility = Visibility.Visible;
        _useNewSensorDashboardToggle.Visibility = Visibility.Visible;
        _lockWindowSizeToggle.Visibility = Visibility.Visible;
        _floatingGadgetsToggle.Visibility = Visibility.Visible;
        _floatingGadgetsStyleComboBox.Visibility = Visibility.Visible;
        _floatingGadgetsInterval.Visibility = Visibility.Visible;

        _isRefreshing = false;

        return Task.CompletedTask;
    }

    private void AutorunComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isRefreshing)
            return;

        if (!_autorunComboBox.TryGetSelectedItem(out AutorunState state))
            return;

        Autorun.Set(state);
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
        AppFlags.Instance.IsTraceEnabled = state.Value;
        _settings.Store.EnableLogging = state.Value;
        _settings.SynchronizeStore();

        mainWindow._openLogIndicator.Visibility = Utils.BooleanToVisibilityConverter.Convert(_settings.Store.EnableLogging);
    }

    private async void UseNewSensorDashboard_Toggle(object sender, RoutedEventArgs e)
    {
        if (_isRefreshing)
            return;

        var state = _useNewSensorDashboardToggle.IsChecked;
        if (state is null)
            return;

        if (state.Value && !PawnIOHelper.IsPawnIOInnstalled())
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

            _useNewSensorDashboardToggle.IsChecked = false;
        }
        else
        {
            await SnackbarHelper.ShowAsync(Resource.SettingsPage_UseNewDashboard_Switch_Title,
                Resource.SettingsPage_UseNewDashboard_Restart_Message);
            _settings.Store.UseNewSensorDashboard = state.Value;
            _settings.SynchronizeStore();
        }
    }

    private void DashboardCustomButton_Click(object sender, RoutedEventArgs e)
    {
        if (_isRefreshing)
            return;

        EditSensorGroupWindow.ShowInstance();
    }

    private void DetectionModeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isRefreshing)
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
        if (_isRefreshing)
            return;

        ArgumentWindow.ShowInstance();
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

        _settings.Store.FloatingGadgetsRefreshInterval = (int)(_floatingGadgetsInterval.Value ?? 1);
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

        CustomGadgetWindow.ShowInstance();
    }
}
