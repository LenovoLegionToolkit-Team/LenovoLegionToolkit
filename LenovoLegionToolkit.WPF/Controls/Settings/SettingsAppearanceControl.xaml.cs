using System;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using LenovoLegionToolkit.Lib;
using LenovoLegionToolkit.Lib.Extensions;
using LenovoLegionToolkit.Lib.Settings;
using LenovoLegionToolkit.Lib.System;
using LenovoLegionToolkit.Lib.Utils;
using LenovoLegionToolkit.WPF.Extensions;
using LenovoLegionToolkit.WPF.Resources;
using LenovoLegionToolkit.WPF.Utils;
using LenovoLegionToolkit.WPF.Windows.Utils;
using Microsoft.Win32;

namespace LenovoLegionToolkit.WPF.Controls.Settings;

public partial class SettingsAppearanceControl
{
    private readonly ApplicationSettings _settings = IoCContainer.Resolve<ApplicationSettings>();
    private readonly ThemeManager _themeManager = IoCContainer.Resolve<ThemeManager>();

    private bool _isRefreshing;

    public SettingsAppearanceControl()
    {
        InitializeComponent();
        _themeManager.ThemeApplied += ThemeManager_ThemeApplied;
    }

    private void ThemeManager_ThemeApplied(object? sender, EventArgs e)
    {
        if (!_isRefreshing)
            UpdateAccentColorPicker();
    }

    public async Task RefreshAsync()
    {
        _isRefreshing = true;

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

        _backgroundImageOpacitySlider.Value = _settings.Store.Opacity;

        _temperatureComboBox.Visibility = Visibility.Visible;
        _themeComboBox.Visibility = Visibility.Visible;
        _selectBackgroundImageButton.Visibility = Visibility.Visible;
        _clearBackgroundImageButton.Visibility = Visibility.Visible;
        _backgroundImageOpacitySlider.Visibility = Visibility.Visible;
        _backdropTypeComboBox.SetItems(Enum.GetValues<WindowBackdropType>(), _settings.Store.BackdropType, t => t.GetDisplayName());
        _hardwareAccelerationToggle.IsChecked = _settings.Store.EnableHardwareAcceleration;

        if (!Displays.HasMultipleGpus())
        {
            _gpuPreferenceComboBox.Visibility = Visibility.Collapsed;
        }
        else
        {
            _gpuPreferenceComboBox.Visibility = Visibility.Visible;
            var exePath = Environment.ProcessPath ?? string.Empty;
            var prefString = LenovoLegionToolkit.Lib.System.Registry.GetValue("HKEY_CURRENT_USER", @"SOFTWARE\Microsoft\DirectX\UserGpuPreferences", exePath, string.Empty);
            if (prefString.Contains("GpuPreference=1"))
                _gpuPreferenceComboBox.SelectedIndex = 1;
            else if (prefString.Contains("GpuPreference=2"))
                _gpuPreferenceComboBox.SelectedIndex = 2;
            else
                _gpuPreferenceComboBox.SelectedIndex = 0;
        }

        _isRefreshing = false;
    }

    private void LangComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isRefreshing)
            return;

        if (!_langComboBox.TryGetSelectedItem(out CultureInfo? cultureInfo) || cultureInfo is null)
            return;

        LocalizationHelper.SetLanguageAsync(cultureInfo).ContinueWith(_ =>
            Dispatcher.Invoke(() => App.Current.RestartMainWindow()),
            TaskScheduler.Default);
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

    private void SelectBackgroundImageButton_Click(object sender, RoutedEventArgs e)
    {
        if (_isRefreshing)
            return;

        OpenFileDialog openFileDialog = new OpenFileDialog
        {
            Filter = $"{Resource.SettingsPage_Select_BackgroundImage_ImageFile}|*.jpg;*.jpeg;*.png;*.bmp|{Resource.SettingsPage_Select_BackgroundImage_AllFiles}|*.*",
            Title = $"{Resource.SettingsPage_Select_BackgroundImage_ImageFile}"
        };

        try
        {
            if (openFileDialog.ShowDialog() == true)
            {
                var filePath = openFileDialog.FileName;
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

        SnackbarHelper.Show(Resource.SettingsPage_ClearBackgroundImage_Title, Resource.SettingsPage_RestartRequired_Message, SnackbarType.Success);

        _settings.Store.BackGroundImageFilePath = string.Empty;
        _settings.SynchronizeStore();
    }

    private void HardwareAccelerationToggle_Checked(object sender, RoutedEventArgs e)
    {
        if (_isRefreshing)
            return;

        _settings.Store.EnableHardwareAcceleration = true;
        _settings.SynchronizeStore();

        SnackbarHelper.Show(Resource.SettingsPage_HardwareAcceleration_Title, Resource.SettingsPage_RestartRequired_Message, SnackbarType.Success);
    }

    private void HardwareAccelerationToggle_Unchecked(object sender, RoutedEventArgs e)
    {
        if (_isRefreshing)
            return;

        _settings.Store.EnableHardwareAcceleration = false;
        _settings.SynchronizeStore();

        SnackbarHelper.Show(Resource.SettingsPage_HardwareAcceleration_Title, Resource.SettingsPage_RestartRequired_Message, SnackbarType.Success);
    }

    private void BackdropTypeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isRefreshing)
            return;

        if (!_backdropTypeComboBox.TryGetSelectedItem(out WindowBackdropType state))
            return;

        _settings.Store.BackdropType = state;
        _settings.SynchronizeStore();

        SnackbarHelper.Show(Resource.SettingsPage_WindowBackdropType_Title, Resource.SettingsPage_RestartRequired_Message, SnackbarType.Success);
    }

    private void GpuPreferenceComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isRefreshing)
            return;

        var exePath = Environment.ProcessPath;
        if (string.IsNullOrEmpty(exePath))
            return;

        if (_gpuPreferenceComboBox.SelectedIndex == 1)
        {
            LenovoLegionToolkit.Lib.System.Registry.SetValue("HKEY_CURRENT_USER", @"SOFTWARE\Microsoft\DirectX\UserGpuPreferences", exePath, "GpuPreference=1;", false, Microsoft.Win32.RegistryValueKind.String);
        }
        else if (_gpuPreferenceComboBox.SelectedIndex == 2)
        {
            LenovoLegionToolkit.Lib.System.Registry.SetValue("HKEY_CURRENT_USER", @"SOFTWARE\Microsoft\DirectX\UserGpuPreferences", exePath, "GpuPreference=2;", false, Microsoft.Win32.RegistryValueKind.String);
        }
        else
        {
            try
            {
                using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"SOFTWARE\Microsoft\DirectX\UserGpuPreferences", true);
                key?.DeleteValue(exePath, false);
            }
            catch (Exception ex)
            {
                Log.Instance.Trace($"Failed to reset GPU preference.", ex);
            }
        }

        SnackbarHelper.Show(Resource.SettingsPage_HardwareAcceleration_Title, Resource.SettingsPage_RestartRequired_Message, SnackbarType.Success);
    }
}

