using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using LenovoLegionToolkit.Lib;
using LenovoLegionToolkit.Lib.Extensions;
using LenovoLegionToolkit.Lib.Settings;
using LenovoLegionToolkit.WPF.Extensions;
using LenovoLegionToolkit.WPF.Resources;
using Wpf.Ui.Controls;

namespace LenovoLegionToolkit.WPF.Windows.Settings;

public partial class NotificationsSettingsWindow
{
    private readonly NotificationSettings _settings = IoCContainer.Resolve<NotificationSettings>();

    private IEnumerable<CardControl> Cards =>
    [
        _notificationPositionCard,
        _notificationDurationCard,
        _updateAvailableCard,
        _capsLockCard,
        _numLockCard,
        _fnLockCard,
        _touchpadLockCard,
        _keyboardBacklightCard,
        _cameraLockCard,
        _microphoneCard,
        _airplaneModeCard,
        _powerModeCard,
        _itsModeCard,
        _refreshRateCard,
        _acAdapterCard,
        _smartKeyCard,
        _automationCard
    ];

    public NotificationsSettingsWindow()
    {
        InitializeComponent();

        _dontShowNotificationsToggle.IsChecked = _settings.Store.DontShowNotifications;
        _notificationAlwaysOnTopToggle.IsChecked = _settings.Store.NotificationAlwaysOnTop;
        _notificationOnAllScreensToggle.IsChecked = _settings.Store.NotificationOnAllScreens;

        _notificationPositionComboBox.SetItems(Enum.GetValues<NotificationPosition>(), _settings.Store.NotificationPosition, v => v.GetDisplayName());
        _notificationDurationComboBox.SetItems(Enum.GetValues<NotificationDuration>(), _settings.Store.NotificationDuration, v => v.GetDisplayName());

        _updateAvailableToggle.IsChecked = _settings.Store.Notifications.UpdateAvailable;
        _capsLockToggle.IsChecked = _settings.Store.Notifications.CapsLock;
        _numLockToggle.IsChecked = _settings.Store.Notifications.NumLock;
        _fnLockToggle.IsChecked = _settings.Store.Notifications.FnLock;
        _touchpadLockToggle.IsChecked = _settings.Store.Notifications.TouchpadLock;
        _keyboardBacklightToggle.IsChecked = _settings.Store.Notifications.KeyboardBacklight;
        _cameraLockToggle.IsChecked = _settings.Store.Notifications.CameraLock;
        _microphoneToggle.IsChecked = _settings.Store.Notifications.Microphone;
        _airplaneModeToggle.IsChecked = _settings.Store.Notifications.AirplaneMode;
        _powerModeToggle.IsChecked = _settings.Store.Notifications.PowerMode;
        _itsModeToggle.IsChecked = _settings.Store.Notifications.ITSMode;
        _refreshRateToggle.IsChecked = _settings.Store.Notifications.RefreshRate;
        _acAdapterToggle.IsChecked = _settings.Store.Notifications.ACAdapter;
        _smartKeyToggle.IsChecked = _settings.Store.Notifications.SmartKey;
        _automationToggle.IsChecked = _settings.Store.Notifications.AutomationNotification;

        RefreshCards();
    }

    private void RefreshCards()
    {
        var notificationsDisabled = _dontShowNotificationsToggle.IsChecked ?? false;

        foreach (var card in Cards)
            card.IsEnabled = !notificationsDisabled;
    }

    private void DontShowNotificationsToggle_Click(object sender, RoutedEventArgs e)
    {
        var state = _dontShowNotificationsToggle.IsChecked;
        if (state is null)
            return;

        _settings.Store.DontShowNotifications = state.Value;
        _settings.SynchronizeStore();

        RefreshCards();
    }

    private void NotificationAlwaysOnTopToggle_Click(object sender, RoutedEventArgs e)
    {
        var state = _notificationAlwaysOnTopToggle.IsChecked;
        if (state is null)
            return;

        _settings.Store.NotificationAlwaysOnTop = state.Value;
        _settings.SynchronizeStore();
    }

    private void NotificationOnAllScreensToggle_Click(object sender, RoutedEventArgs e)
    {
        var state = _notificationOnAllScreensToggle.IsChecked;
        if (state is null)
            return;

        _settings.Store.NotificationOnAllScreens = state.Value;
        _settings.SynchronizeStore();
    }

    private void NotificationPositionComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_notificationPositionComboBox.TryGetSelectedItem(out NotificationPosition state))
            return;

        _settings.Store.NotificationPosition = state;
        _settings.SynchronizeStore();
    }

    private void NotificationDurationComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_notificationDurationComboBox.TryGetSelectedItem(out NotificationDuration state))
            return;

        _settings.Store.NotificationDuration = state;
        _settings.SynchronizeStore();
    }


    private void CapsLockToggle_Click(object sender, RoutedEventArgs e)
    {
        var state = _capsLockToggle.IsChecked;
        if (state is null)
            return;

        _settings.Store.Notifications.CapsLock = state.Value;
        _settings.SynchronizeStore();
    }

    private void NumLockToggle_Click(object sender, RoutedEventArgs e)
    {
        var state = _numLockToggle.IsChecked;
        if (state is null)
            return;

        _settings.Store.Notifications.NumLock = state.Value;
        _settings.SynchronizeStore();
    }

    private void FnLockToggle_Click(object sender, RoutedEventArgs e)
    {
        var state = _fnLockToggle.IsChecked;
        if (state is null)
            return;

        _settings.Store.Notifications.FnLock = state.Value;
        _settings.SynchronizeStore();
    }

    private void TouchpadLockToggle_Click(object sender, RoutedEventArgs e)
    {
        var state = _touchpadLockToggle.IsChecked;
        if (state is null)
            return;

        _settings.Store.Notifications.TouchpadLock = state.Value;
        _settings.SynchronizeStore();
    }

    private void KeyboardBacklightToggle_Click(object sender, RoutedEventArgs e)
    {
        var state = _keyboardBacklightToggle.IsChecked;
        if (state is null)
            return;

        _settings.Store.Notifications.KeyboardBacklight = state.Value;
        _settings.SynchronizeStore();
    }

    private void CameraLockToggle_Click(object sender, RoutedEventArgs e)
    {
        var state = _cameraLockToggle.IsChecked;
        if (state is null)
            return;

        _settings.Store.Notifications.CameraLock = state.Value;
        _settings.SynchronizeStore();
    }

    private void MicrophoneToggle_Click(object sender, RoutedEventArgs e)
    {
        var state = _microphoneToggle.IsChecked;
        if (state is null)
            return;

        _settings.Store.Notifications.Microphone = state.Value;
        _settings.SynchronizeStore();
    }

    private void AirplaneModeToggle_Click(object sender, RoutedEventArgs e)
    {
        var state = _airplaneModeToggle.IsChecked;
        if (state is null)
            return;

        _settings.Store.Notifications.AirplaneMode = state.Value;
        _settings.SynchronizeStore();
    }

    private void PowerModeToggle_Click(object sender, RoutedEventArgs e)
    {
        var state = _powerModeToggle.IsChecked;
        if (state is null)
            return;

        _settings.Store.Notifications.PowerMode = state.Value;
        _settings.SynchronizeStore();
    }

    private void ITSModeToggle_Click(object sender, RoutedEventArgs e)
    {
        var state = _itsModeToggle.IsChecked;
        if (state is null)
            return;

        _settings.Store.Notifications.ITSMode = state.Value;
        _settings.SynchronizeStore();
    }

    private void RefreshRateToggle_Click(object sender, RoutedEventArgs e)
    {
        var state = _refreshRateToggle.IsChecked;
        if (state is null)
            return;

        _settings.Store.Notifications.RefreshRate = state.Value;
        _settings.SynchronizeStore();
    }

    private void ACAdapterToggle_Click(object sender, RoutedEventArgs e)
    {
        var state = _acAdapterToggle.IsChecked;
        if (state is null)
            return;

        _settings.Store.Notifications.ACAdapter = state.Value;
        _settings.SynchronizeStore();
    }

    private void SmartKeyToggle_Click(object sender, RoutedEventArgs e)
    {
        var state = _smartKeyToggle.IsChecked;
        if (state is null)
            return;

        _settings.Store.Notifications.SmartKey = state.Value;
        _settings.SynchronizeStore();
    }

    private void UpdateAvailableToggle_Click(object sender, RoutedEventArgs e)
    {
        var state = _updateAvailableToggle.IsChecked;
        if (state is null)
            return;

        _settings.Store.Notifications.UpdateAvailable = state.Value;
        _settings.SynchronizeStore();
    }

    private void AutomationToggle_Click(object sender, RoutedEventArgs e)
    {
        var state = _automationToggle.IsChecked;
        if (state is null)
            return;

        _settings.Store.Notifications.AutomationNotification = state.Value;
        _settings.SynchronizeStore();
    }

    private void OpenCustomizeWindow(string title, (NotificationType, string)[] types) =>
        new NotificationTypeCustomizationWindow(title, types, _settings) { Owner = this }.ShowDialog();

    private void PowerModeCustomizeButton_Click(object sender, RoutedEventArgs e) =>
        OpenCustomizeWindow(Resource.NotificationsSettingsWindow_PowerMode,
        [
                (NotificationType.PowerModeQuiet,       PowerModeState.Quiet.GetDisplayName()),
                (NotificationType.PowerModeBalance,     PowerModeState.Balance.GetDisplayName()),
                (NotificationType.PowerModePerformance, PowerModeState.Performance.GetDisplayName()),
                (NotificationType.PowerModeExtreme,     PowerModeState.Extreme.GetDisplayName()),
                (NotificationType.PowerModeGodMode,     PowerModeState.GodMode.GetDisplayName())
        ]);

    private void ITSModeCustomizeButton_Click(object sender, RoutedEventArgs e) =>
        OpenCustomizeWindow(Resource.NotificationsSettingsWindow_ITSMode,
        [
                (NotificationType.ITSModeAuto,        ITSMode.ItsAuto.GetDisplayName()),
                (NotificationType.ITSModeCool,        ITSMode.MmcCool.GetDisplayName()),
                (NotificationType.ITSModePerformance, ITSMode.MmcPerformance.GetDisplayName()),
                (NotificationType.ITSModeGeek,        ITSMode.MmcGeek.GetDisplayName())
        ]);

    private void ACAdapterCustomizeButton_Click(object sender, RoutedEventArgs e) =>
        OpenCustomizeWindow(Resource.NotificationsSettingsWindow_ACAdapter,
        [
                (NotificationType.ACAdapterConnected, Resource.Notification_ACAdapterConnected),
                (NotificationType.ACAdapterConnectedLowWattage, Resource.Notification_ACAdapterConnectedLowWattage),
                (NotificationType.ACAdapterDisconnected, Resource.Notification_ACAdapterDisconnected)
        ]);

    private void KeyboardBacklightCustomizeButton_Click(object sender, RoutedEventArgs e) =>
        OpenCustomizeWindow(Resource.NotificationsSettingsWindow_KeyboardBacklight,
        [
                (NotificationType.RGBKeyboardBacklightChanged,    Resource.Notification_RGBKeyboardBacklightChanged),
                (NotificationType.RGBKeyboardBacklightOff,        Resource.Notification_RGBKeyboardBacklightOff),
                (NotificationType.SpectrumBacklightChanged,       Resource.Notification_SpectrumBacklightChanged),
                (NotificationType.SpectrumBacklightOff,           Resource.Notification_SpectrumBacklightOff),
                (NotificationType.SpectrumBacklightPresetChanged, Resource.Notification_SpectrumBacklightPresetChanged),
                (NotificationType.WhiteKeyboardBacklightChanged,  Resource.Notification_WhiteKeyboardBacklightChanged),
                (NotificationType.WhiteKeyboardBacklightChangedSpecial, Resource.Notification_WhiteKeyboardBacklightSpecial),
                (NotificationType.WhiteKeyboardBacklightOff,      Resource.Notification_WhiteKeyboardBacklightOff),
                (NotificationType.PanelLogoLightingOn,  Resource.Notification_PanelLogoLightingOn),
                (NotificationType.PanelLogoLightingOff, Resource.Notification_PanelLogoLightingOff),
                (NotificationType.PortLightingOn,  Resource.Notification_PortLightingOn),
                (NotificationType.PortLightingOff, Resource.Notification_PortLightingOff)
        ]);

    private void CapsLockCustomizeButton_Click(object sender, RoutedEventArgs e) =>
        OpenCustomizeWindow(Resource.NotificationsSettingsWindow_CapsLock,
        [
                (NotificationType.CapsLockOn, Resource.Notification_CapsLockOn),
                (NotificationType.CapsLockOff, Resource.Notification_CapsLockOff)
        ]);

    private void NumLockCustomizeButton_Click(object sender, RoutedEventArgs e) =>
        OpenCustomizeWindow(Resource.NotificationsSettingsWindow_NumLock,
        [
                (NotificationType.NumLockOn, Resource.Notification_NumLockOn),
                (NotificationType.NumLockOff, Resource.Notification_NumLockOff)
        ]);

    private void FnLockCustomizeButton_Click(object sender, RoutedEventArgs e) =>
        OpenCustomizeWindow(Resource.NotificationsSettingsWindow_FnLock,
        [
                (NotificationType.FnLockOn, Resource.Notification_FnLockOn),
                (NotificationType.FnLockOff, Resource.Notification_FnLockOff)
        ]);

    private void TouchpadLockCustomizeButton_Click(object sender, RoutedEventArgs e) =>
        OpenCustomizeWindow(Resource.NotificationsSettingsWindow_TouchpadLock,
        [
                (NotificationType.TouchpadOn, Resource.Notification_TouchpadOn),
                (NotificationType.TouchpadOff, Resource.Notification_TouchpadOff)
        ]);

    private void CameraLockCustomizeButton_Click(object sender, RoutedEventArgs e) =>
        OpenCustomizeWindow(Resource.NotificationsSettingsWindow_Camera,
        [
                (NotificationType.CameraOn, Resource.Notification_CameraOn),
                (NotificationType.CameraOff, Resource.Notification_CameraOff)
        ]);

    private void MicrophoneCustomizeButton_Click(object sender, RoutedEventArgs e) =>
        OpenCustomizeWindow(Resource.NotificationsSettingsWindow_Microphone,
        [
                (NotificationType.MicrophoneOn, Resource.Notification_MicrophoneOn),
                (NotificationType.MicrophoneOff, Resource.Notification_MicrophoneOff)
        ]);

    private void AirplaneModeCustomizeButton_Click(object sender, RoutedEventArgs e) =>
        OpenCustomizeWindow(Resource.NotificationsSettingsWindow_AirplaneMode,
        [
                (NotificationType.AirplaneModeOn, Resource.Notification_AirplaneModeOn),
                (NotificationType.AirplaneModeOff, Resource.Notification_AirplaneModeOff)
        ]);

    private void RefreshRateCustomizeButton_Click(object sender, RoutedEventArgs e) =>
        OpenCustomizeWindow(Resource.NotificationsSettingsWindow_RefreshRate,
        [(NotificationType.RefreshRate, Resource.NotificationsSettingsWindow_RefreshRate)]);

    private void SmartKeyCustomizeButton_Click(object sender, RoutedEventArgs e) =>
        OpenCustomizeWindow(Resource.NotificationsSettingsWindow_SmartKey,
        [
            (NotificationType.SmartKeySinglePress, Resource.Notification_SmartKeySinglePress),
            (NotificationType.SmartKeyDoublePress, Resource.Notification_SmartKeyDoublePress)
        ]);

    private void UpdateAvailableCustomizeButton_Click(object sender, RoutedEventArgs e) =>
        OpenCustomizeWindow(Resource.NotificationsSettingsWindow_Updates_Title,
        [(NotificationType.UpdateAvailable, Resource.NotificationsSettingsWindow_Updates_Title)]);

    private void AutomationCustomizeButton_Click(object sender, RoutedEventArgs e) =>
        OpenCustomizeWindow(Resource.NotificationsSettingsWindow_Automation,
        [(NotificationType.AutomationNotification, Resource.NotificationsSettingsWindow_Automation)]);
}
