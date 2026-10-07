using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using LenovoLegionToolkit.Lib;
using LenovoLegionToolkit.Lib.Extensions;
using LenovoLegionToolkit.Lib.Settings;
using LenovoLegionToolkit.Lib.SoftwareDisabler;
using LenovoLegionToolkit.WPF.Extensions;
using LenovoLegionToolkit.WPF.Resources;
using LenovoLegionToolkit.WPF.Windows.Settings;

namespace LenovoLegionToolkit.WPF.Controls.Settings;

public partial class SettingsSpecialKeyControl
{
    private readonly ApplicationSettings _settings = IoCContainer.Resolve<ApplicationSettings>();
    private readonly FnKeysDisabler _fnKeysDisabler = IoCContainer.Resolve<FnKeysDisabler>();

    private bool _isRefreshing;

    public SettingsSpecialKeyControl()
    {
        InitializeComponent();
    }

    public void UpdateFnKeysVisibility(SoftwareStatus fnKeysStatus)
    {
        if (_isRefreshing)
            return;

        _smartFnLockComboBox.Visibility = Visibility.Visible;
        _fnKeysWarningInfoBar.IsOpen = fnKeysStatus == SoftwareStatus.Enabled;
    }

    public async Task RefreshAsync()
    {
        _isRefreshing = true;

        _smartFnLockComboBox.SetItems([ModifierKey.None, ModifierKey.Alt, ModifierKey.Alt | ModifierKey.Ctrl | ModifierKey.Shift],
            _settings.Store.SmartFnLockFlags,
            m => m is ModifierKey.None ? Resource.Off : m.GetFlagsDisplayName(ModifierKey.None));

        var fnKeysStatus = await _fnKeysDisabler.GetStatusAsync();

        _smartFnLockComboBox.Visibility = Visibility.Visible;
        _fnKeysWarningInfoBar.IsOpen = fnKeysStatus == SoftwareStatus.Enabled;

        _isRefreshing = false;
    }

    private void SpecialKeys_Click(object sender, RoutedEventArgs e)
    {
        if (_isRefreshing)
            return;

        var window = new SpecialKeysWindow { Owner = Window.GetWindow(this) };
        window.ShowDialog();
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

    private void ExcludeRefreshRates_Click(object sender, RoutedEventArgs e)
    {
        if (_isRefreshing)
            return;

        var window = new ExcludeRefreshRatesWindow { Owner = Window.GetWindow(this) };
        window.ShowDialog();
    }

    private void KeyDiscovery_Click(object sender, RoutedEventArgs e)
    {
        if (_isRefreshing)
            return;

        var window = new KeyDiscoveryWindow { Owner = Window.GetWindow(this) };
        window.ShowDialog();
    }
}
