using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using LenovoLegionToolkit.Lib;
using LenovoLegionToolkit.Lib.Extensions;
using LenovoLegionToolkit.Lib.Settings;
using LenovoLegionToolkit.Lib.SoftwareDisabler;
using LenovoLegionToolkit.WPF.Extensions;
using LenovoLegionToolkit.WPF.Resources;
using LenovoLegionToolkit.WPF.Utils;
using LenovoLegionToolkit.WPF.Windows.Settings;

namespace LenovoLegionToolkit.WPF.Controls.Settings;

public partial class SettingsSpecialKeyControl
{
    private readonly ApplicationSettings _settings = IoCContainer.Resolve<ApplicationSettings>();
    private readonly FnKeysDisabler _fnKeysDisabler = IoCContainer.Resolve<FnKeysDisabler>();
    private readonly FullscreenFnLockController _fullscreenFnLockController = IoCContainer.Resolve<FullscreenFnLockController>();

    private bool _isRefreshing;

    public SettingsSpecialKeyControl()
    {
        InitializeComponent();

        _fullscreenFnLockHeader.Title = Resource.ResourceManager.GetString("SettingsPage_FullscreenFnLock_Title") ?? "Automatic Fn Lock";
        _fullscreenFnLockHeader.Subtitle = Resource.ResourceManager.GetString("SettingsPage_FullscreenFnLock_Message") ?? "Enable Fn Lock while a fullscreen app is focused and disable it when focus leaves.";
        AutomationProperties.SetName(_fullscreenFnLockModeComboBox, _fullscreenFnLockHeader.Title);
    }

    public void UpdateFnKeysVisibility(SoftwareStatus fnKeysStatus)
    {
        if (_isRefreshing)
            return;

        var visible = fnKeysStatus != SoftwareStatus.Enabled ? Visibility.Visible : Visibility.Collapsed;
        _smartFnLockComboBox.Visibility = Visibility.Visible;
        _fullscreenFnLockModeComboBox.Visibility = Visibility.Visible;
        _excludeRefreshRatesCard.Visibility = visible;
    }

    public async Task RefreshAsync()
    {
        _isRefreshing = true;

        _smartFnLockComboBox.SetItems([ModifierKey.None, ModifierKey.Alt, ModifierKey.Alt | ModifierKey.Ctrl | ModifierKey.Shift],
            _settings.Store.SmartFnLockFlags,
            m => m is ModifierKey.None ? Resource.Off : m.GetFlagsDisplayName(ModifierKey.None));

        _fullscreenFnLockModeComboBox.SetItems(Enum.GetValues<FullscreenFnLockMode>(),
            _settings.Store.FullscreenFnLockMode,
            GetFullscreenFnLockModeDisplayName);

        var fnKeysStatus = await _fnKeysDisabler.GetStatusAsync();
        var visible = fnKeysStatus != SoftwareStatus.Enabled ? Visibility.Visible : Visibility.Collapsed;

        _smartFnLockComboBox.Visibility = Visibility.Visible;
        _fullscreenFnLockModeComboBox.Visibility = Visibility.Visible;
        _excludeRefreshRatesCard.Visibility = visible;

        _isRefreshing = false;
    }

    private void FullscreenFnLockModeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isRefreshing)
            return;

        if (!_fullscreenFnLockModeComboBox.TryGetSelectedItem(out FullscreenFnLockMode mode))
            return;

        _settings.Store.FullscreenFnLockMode = mode;
        _settings.SynchronizeStore();
        _fullscreenFnLockController.Refresh();
    }

    private static string GetFullscreenFnLockModeDisplayName(FullscreenFnLockMode mode)
    {
        var resourceName = $"FullscreenFnLockMode_{mode}";
        return Resource.ResourceManager.GetString(resourceName) ?? mode switch
        {
            FullscreenFnLockMode.Off => "Off",
            FullscreenFnLockMode.Media => "Fullscreen media",
            FullscreenFnLockMode.AnyApplication => "Any fullscreen app",
            _ => mode.ToString()
        };
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
