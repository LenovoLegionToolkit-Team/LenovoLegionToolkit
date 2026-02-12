using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using LenovoLegionToolkit.Lib;
using LenovoLegionToolkit.Lib.Integrations;
using LenovoLegionToolkit.Lib.Settings;
using LenovoLegionToolkit.Lib.System;
using LenovoLegionToolkit.Lib.Utils;
using LenovoLegionToolkit.WPF.CLI;
using LenovoLegionToolkit.WPF.Resources;
using LenovoLegionToolkit.WPF.Utils;
using LenovoLegionToolkit.WPF.Windows.FloatingGadgets;
using LenovoLegionToolkit.WPF.Windows.Utils;
using CustomGadgetWindow = LenovoLegionToolkit.WPF.Windows.FloatingGadgets.Custom;

namespace LenovoLegionToolkit.WPF.Controls.Settings;

public partial class SettingsIntegrationsControl
{
    private readonly ApplicationSettings _settings = IoCContainer.Resolve<ApplicationSettings>();
    private readonly IntegrationsSettings _integrationsSettings = IoCContainer.Resolve<IntegrationsSettings>();
    private readonly HWiNFOIntegration _hwinfoIntegration = IoCContainer.Resolve<HWiNFOIntegration>();
    private readonly IpcServer _ipcServer = IoCContainer.Resolve<IpcServer>();

    private bool _isRefreshing;

    public SettingsIntegrationsControl()
    {
        InitializeComponent();
    }

    public Task RefreshAsync()
    {
        _isRefreshing = true;

        _hwinfoIntegrationToggle.IsChecked = _integrationsSettings.Store.HWiNFO;
        _cliInterfaceToggle.IsChecked = _integrationsSettings.Store.CLI;
        _cliPathToggle.IsChecked = SystemPath.HasCLI();

        _floatingGadgetsToggle.Visibility = Visibility.Visible;
        _floatingGadgetsStyleComboBox.Visibility = Visibility.Visible;
        _floatingGadgetsToggle.IsChecked = _settings.Store.ShowFloatingGadgets;

        _floatingGadgetsStyleComboBox.SelectedIndex = _settings.Store.SelectedStyleIndex;
        _floatingGadgetsInterval.Text = _settings.Store.FloatingGadgetsRefreshInterval.ToString();

        _hwinfoIntegrationToggle.Visibility = Visibility.Visible;
        _cliInterfaceToggle.Visibility = Visibility.Visible;
        _cliPathToggle.Visibility = Visibility.Visible;
        _floatingGadgetsInterval.Visibility = Visibility.Visible;

        _isRefreshing = false;

        return Task.CompletedTask;
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
