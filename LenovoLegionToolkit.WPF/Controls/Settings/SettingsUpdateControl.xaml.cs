using System;
using System.Linq;
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
    private readonly UpdateSettings _updateSettings = IoCContainer.Resolve<UpdateSettings>();

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
            _updateMethodCard.Visibility = Visibility.Collapsed;
        }
        else
        {
            _checkUpdatesButton.Visibility = Visibility.Visible;
            _updateCheckFrequencyComboBox.Visibility = Visibility.Visible;
            _updateCheckFrequencyComboBox.SetItems(Enum.GetValues<UpdateCheckFrequency>(), _updateSettings.Store.UpdateCheckFrequency, t => t.GetDisplayName());
            _updateMethodComboBox.Visibility = Visibility.Visible;
            _updateMethodComboBox.SetItems(Enum.GetValues<UpdateMethod>(), _updateSettings.Store.UpdateMethod, t => t.GetDisplayName());
            _updateChannelComboBox.Visibility = Visibility.Visible;
            var channels = Enum.GetValues<UpdateChannel>()
                .Where(c => c != UpdateChannel.Test || AppFlags.Instance.Debug);
            _updateChannelComboBox.SetItems(channels, _updateSettings.Store.UpdateChannel, t => t.GetDisplayName());
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

        if (!_updateChannelComboBox.TryGetSelectedItem(out UpdateChannel updateChannel))
            return;

        _updateSettings.Store.UpdateChannel = updateChannel;
        _updateSettings.SynchronizeStore();
    }

    private void UpdateMethodComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isRefreshing)
            return;

        if (!_updateMethodComboBox.TryGetSelectedItem(out UpdateMethod updateMethod))
            return;

        _updateSettings.Store.UpdateMethod = updateMethod;
        _updateSettings.SynchronizeStore();
    }

    private void UpdateCheckFrequencyComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isRefreshing)
            return;

        if (!_updateCheckFrequencyComboBox.TryGetSelectedItem(out UpdateCheckFrequency frequency))
            return;

        _updateSettings.Store.UpdateCheckFrequency = frequency;
        _updateSettings.SynchronizeStore();
        _updateChecker.UpdateMinimumTimeSpanForRefresh();
    }
}
