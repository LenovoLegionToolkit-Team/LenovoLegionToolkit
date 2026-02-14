using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using LenovoLegionToolkit.Lib;
using LenovoLegionToolkit.Lib.Extensions;
using LenovoLegionToolkit.Lib.Settings;
using LenovoLegionToolkit.Lib.Utils;
using LenovoLegionToolkit.WPF.Extensions;
using LenovoLegionToolkit.WPF.Resources;
using LenovoLegionToolkit.WPF.Utils;
using LenovoLegionToolkit.WPF.Windows;

namespace LenovoLegionToolkit.WPF.Controls.Settings;

public partial class SettingsUpdateControl
{
    private readonly ApplicationSettings _settings = IoCContainer.Resolve<ApplicationSettings>();
    private readonly UpdateChecker _updateChecker = IoCContainer.Resolve<UpdateChecker>();
    private readonly UpdateCheckSettings _updateCheckSettings = IoCContainer.Resolve<UpdateCheckSettings>();

    private bool _isRefreshing;

    public SettingsUpdateControl()
    {
        InitializeComponent();
    }

    public Task RefreshAsync()
    {
        _isRefreshing = true;

        if (_updateChecker.Disable)
        {
            _checkUpdatesCard.Visibility = Visibility.Collapsed;
            _updateCheckFrequencyCard.Visibility = Visibility.Collapsed;
            _updateChannelCard.Visibility = Visibility.Collapsed;
        }
        else
        {
            _checkUpdatesButton.Visibility = Visibility.Visible;
            _updateCheckFrequencyComboBox.Visibility = Visibility.Visible;
            _updateCheckFrequencyComboBox.SetItems(Enum.GetValues<UpdateCheckFrequency>(), _updateCheckSettings.Store.UpdateCheckFrequency, t => t.GetDisplayName());
            __updateChannelComboBox.Visibility = Visibility.Visible;
            __updateChannelComboBox.SetItems(Enum.GetValues<UpdateChannel>(), _settings.Store.UpdateChannel, t => t.GetDisplayName());
        }

        _isRefreshing = false;

        return Task.CompletedTask;
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

    private void UpdateChannelComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isRefreshing)
            return;

        if (!__updateChannelComboBox.TryGetSelectedItem(out UpdateChannel updateChannel))
            return;

        _settings.Store.UpdateChannel = updateChannel;
        _settings.SynchronizeStore();
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
}
