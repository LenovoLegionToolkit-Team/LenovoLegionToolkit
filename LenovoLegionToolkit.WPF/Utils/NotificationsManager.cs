using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using LenovoLegionToolkit.Lib;
using LenovoLegionToolkit.Lib.Messaging;
using LenovoLegionToolkit.Lib.Messaging.Messages;
using LenovoLegionToolkit.Lib.Settings;
using LenovoLegionToolkit.Lib.Utils;
using LenovoLegionToolkit.WPF.Extensions;
using LenovoLegionToolkit.WPF.Resources;
using LenovoLegionToolkit.WPF.Windows;
using LenovoLegionToolkit.WPF.Windows.Utils;
using Wpf.Ui.Common;
using Wpf.Ui.Controls;

namespace LenovoLegionToolkit.WPF.Utils;

public class NotificationsManager
{
    private static Dispatcher Dispatcher => Application.Current.Dispatcher;

    private readonly NotificationSettings _settings;

    private List<INotificationWindow?> _windows = [];

    public NotificationsManager(NotificationSettings settings)
    {
        _settings = settings;

        MessagingCenter.Subscribe<NotificationMessage>(this, OnNotificationReceived);
    }

    private void OnNotificationReceived(NotificationMessage notification)
    {
        Dispatcher.Invoke(() =>
        {
            Log.Instance.Trace($"Notification {notification} received");

            if (_settings.Store.DontShowNotifications)
            {
                Log.Instance.Trace($"Notifications are disabled.");

                return;
            }

            if (FullscreenHelper.IsAnyApplicationFullscreen() && !_settings.Store.NotificationAlwaysOnTop)
            {
                Log.Instance.Trace($"Some application is in fullscreen.");

                return;
            }

            var allow = notification.Type switch
            {
                NotificationType.ACAdapterConnected => _settings.Store.Notifications.ACAdapter,
                NotificationType.ACAdapterConnectedLowWattage => _settings.Store.Notifications.ACAdapter,
                NotificationType.ACAdapterDisconnected => _settings.Store.Notifications.ACAdapter,
                NotificationType.AirplaneModeOn => _settings.Store.Notifications.AirplaneMode,
                NotificationType.AirplaneModeOff => _settings.Store.Notifications.AirplaneMode,
                NotificationType.AutomationNotification => _settings.Store.Notifications.AutomationNotification,
                NotificationType.CapsLockOn => _settings.Store.Notifications.CapsLock,
                NotificationType.CapsLockOff => _settings.Store.Notifications.CapsLock,
                NotificationType.CameraOn => _settings.Store.Notifications.CameraLock,
                NotificationType.CameraOff => _settings.Store.Notifications.CameraLock,
                NotificationType.FnLockOn => _settings.Store.Notifications.FnLock,
                NotificationType.FnLockOff => _settings.Store.Notifications.FnLock,
                NotificationType.MicrophoneOn => _settings.Store.Notifications.Microphone,
                NotificationType.MicrophoneOff => _settings.Store.Notifications.Microphone,
                NotificationType.NumLockOn => _settings.Store.Notifications.NumLock,
                NotificationType.NumLockOff => _settings.Store.Notifications.NumLock,
                NotificationType.PanelLogoLightingOn => _settings.Store.Notifications.KeyboardBacklight,
                NotificationType.PanelLogoLightingOff => _settings.Store.Notifications.KeyboardBacklight,
                NotificationType.PortLightingOn => _settings.Store.Notifications.KeyboardBacklight,
                NotificationType.PortLightingOff => _settings.Store.Notifications.KeyboardBacklight,
                NotificationType.PowerModeQuiet => _settings.Store.Notifications.PowerMode,
                NotificationType.PowerModeBalance => _settings.Store.Notifications.PowerMode,
                NotificationType.PowerModePerformance => _settings.Store.Notifications.PowerMode,
                NotificationType.PowerModeGodMode => _settings.Store.Notifications.PowerMode,
                NotificationType.RefreshRate => _settings.Store.Notifications.RefreshRate,
                NotificationType.RGBKeyboardBacklightOff => _settings.Store.Notifications.KeyboardBacklight,
                NotificationType.RGBKeyboardBacklightChanged => _settings.Store.Notifications.KeyboardBacklight,
                NotificationType.SmartKeyDoublePress => _settings.Store.Notifications.SmartKey,
                NotificationType.SmartKeySinglePress => _settings.Store.Notifications.SmartKey,
                NotificationType.SpectrumBacklightChanged => _settings.Store.Notifications.KeyboardBacklight,
                NotificationType.SpectrumBacklightOff => _settings.Store.Notifications.KeyboardBacklight,
                NotificationType.SpectrumBacklightPresetChanged => _settings.Store.Notifications.KeyboardBacklight,
                NotificationType.TouchpadOn => _settings.Store.Notifications.TouchpadLock,
                NotificationType.TouchpadOff => _settings.Store.Notifications.TouchpadLock,
                NotificationType.UpdateAvailable => _settings.Store.Notifications.UpdateAvailable,
                NotificationType.WhiteKeyboardBacklightOff => _settings.Store.Notifications.KeyboardBacklight,
                NotificationType.WhiteKeyboardBacklightChanged => _settings.Store.Notifications.KeyboardBacklight,
                NotificationType.WhiteKeyboardBacklightChangedSpecial => _settings.Store.Notifications.KeyboardBacklight,
                NotificationType.ITSModeAuto => _settings.Store.Notifications.ITSMode,
                NotificationType.ITSModeCool => _settings.Store.Notifications.ITSMode,
                NotificationType.ITSModePerformance => _settings.Store.Notifications.ITSMode,
                NotificationType.ITSModeGeek => _settings.Store.Notifications.ITSMode,
                _ => throw new ArgumentException(nameof(notification.Type))
            };

            if (!allow)
            {
                Log.Instance.Trace($"Notification type {notification.Type} is disabled.");

                return;
            }

            var symbol = GetDefaultSymbol(notification.Type);

            SymbolRegular? overlaySymbol = GetDefaultOverlaySymbol(notification.Type);

            var text = notification.Type switch
            {
                NotificationType.ACAdapterConnected => Resource.Notification_ACAdapterConnected,
                NotificationType.ACAdapterConnectedLowWattage => Resource.Notification_ACAdapterConnectedLowWattage,
                NotificationType.ACAdapterDisconnected => Resource.Notification_ACAdapterDisconnected,
                NotificationType.AirplaneModeOn => Resource.Notification_AirplaneModeOn,
                NotificationType.AirplaneModeOff => Resource.Notification_AirplaneModeOff,
                NotificationType.AutomationNotification => string.Format("{0}", notification.Args),
                NotificationType.CapsLockOn => Resource.Notification_CapsLockOn,
                NotificationType.CapsLockOff => Resource.Notification_CapsLockOff,
                NotificationType.CameraOn => Resource.Notification_CameraOn,
                NotificationType.CameraOff => Resource.Notification_CameraOff,
                NotificationType.FnLockOn => Resource.Notification_FnLockOn,
                NotificationType.FnLockOff => Resource.Notification_FnLockOff,
                NotificationType.MicrophoneOn => Resource.Notification_MicrophoneOn,
                NotificationType.MicrophoneOff => Resource.Notification_MicrophoneOff,
                NotificationType.NumLockOn => Resource.Notification_NumLockOn,
                NotificationType.NumLockOff => Resource.Notification_NumLockOff,
                NotificationType.PanelLogoLightingOn => Resource.Notification_PanelLogoLightingOn,
                NotificationType.PanelLogoLightingOff => Resource.Notification_PanelLogoLightingOff,
                NotificationType.PortLightingOn => Resource.Notification_PortLightingOn,
                NotificationType.PortLightingOff => Resource.Notification_PortLightingOff,
                NotificationType.PowerModeQuiet => string.Format("{0}", notification.Args),
                NotificationType.PowerModeBalance => string.Format("{0}", notification.Args),
                NotificationType.PowerModePerformance => string.Format("{0}", notification.Args),
                NotificationType.PowerModeGodMode => string.Format("{0}", notification.Args),
                NotificationType.RefreshRate => string.Format("{0}", notification.Args),
                NotificationType.RGBKeyboardBacklightOff => string.Format("{0}", notification.Args),
                NotificationType.RGBKeyboardBacklightChanged => string.Format("{0}", notification.Args),
                NotificationType.SmartKeyDoublePress => string.Format("{0}", notification.Args),
                NotificationType.SmartKeySinglePress => string.Format("{0}", notification.Args),
                NotificationType.SpectrumBacklightChanged => string.Format(Resource.Notification_SpectrumKeyboardBacklight_Brightness, notification.Args),
                NotificationType.SpectrumBacklightOff => string.Format(Resource.Notification_SpectrumKeyboardBacklight_Backlight, notification.Args),
                NotificationType.SpectrumBacklightPresetChanged => string.Format(Resource.Notification_SpectrumKeyboardBacklight_Profile, notification.Args),
                NotificationType.TouchpadOn => Resource.Notification_TouchpadOn,
                NotificationType.TouchpadOff => Resource.Notification_TouchpadOff,
                NotificationType.UpdateAvailable => string.Format(Resource.Notification_UpdateAvailable, notification.Args),
                NotificationType.WhiteKeyboardBacklightOff => string.Format(Resource.Notification_WhiteKeyboardBacklight, notification.Args),
                NotificationType.WhiteKeyboardBacklightChanged => string.Format(Resource.Notification_WhiteKeyboardBacklight, notification.Args),
                NotificationType.WhiteKeyboardBacklightChangedSpecial => Resource.Notification_WhiteKeyboardBacklightSpecial,
                NotificationType.ITSModeAuto => string.Format("{0}", notification.Args),
                NotificationType.ITSModeCool => string.Format("{0}", notification.Args),
                NotificationType.ITSModePerformance => string.Format("{0}", notification.Args),
                NotificationType.ITSModeGeek => string.Format("{0}", notification.Args),
                _ => throw new ArgumentException(nameof(notification.Type))
            };

            Action<SymbolIcon>? symbolTransform = notification.Type switch
            {
                NotificationType.PowerModeQuiet => si => si.Foreground = PowerModeState.Quiet.GetSolidColorBrush(),
                NotificationType.PowerModePerformance => si => si.Foreground = PowerModeState.Performance.GetSolidColorBrush(),
                NotificationType.PowerModeExtreme => si => si.Foreground = PowerModeState.Extreme.GetSolidColorBrush(),
                NotificationType.PowerModeGodMode => si => si.Foreground = PowerModeState.GodMode.GetSolidColorBrush(),
                NotificationType.ITSModeAuto => si => si.Foreground = ITSMode.ItsAuto.GetSolidColorBrush(),
                NotificationType.ITSModeCool => si => si.Foreground = ITSMode.MmcCool.GetSolidColorBrush(),
                NotificationType.ITSModePerformance => si => si.Foreground = ITSMode.MmcPerformance.GetSolidColorBrush(),
                NotificationType.ITSModeGeek => si => si.Foreground = ITSMode.MmcGeek.GetSolidColorBrush(),
                _ => null
            };

            Action? clickAction = notification.Type switch
            {
                NotificationType.UpdateAvailable => UpdateAvailableAction,
                _ => null
            };

            if (_settings.Store.Notifications.IconOverrides.TryGetValue(notification.Type, out var iconOverride)
                && Enum.IsDefined(typeof(SymbolRegular), iconOverride))
                symbol = (SymbolRegular)iconOverride;

            if (_settings.Store.Notifications.ColorOverrides.TryGetValue(notification.Type, out var colorOverride))
                symbolTransform = si => si.Foreground = new SolidColorBrush(colorOverride.ToColor());

            if (symbolTransform is null && overlaySymbol is not null)
                symbolTransform = si => si.SetResourceReference(Control.ForegroundProperty, "TextFillColorSecondaryBrush");

            Brush? textColor = _settings.Store.Notifications.TextColorOverrides.TryGetValue(notification.Type, out var textColorOverride)
                ? new SolidColorBrush(textColorOverride.ToColor())
                : null;

            var effectiveDuration = _settings.Store.Notifications.DurationOverrides.TryGetValue(notification.Type, out var durOverride)
                ? durOverride
                : _settings.Store.NotificationDuration;

            var duration = effectiveDuration switch
            {
                NotificationDuration.Short  => notification.Type == NotificationType.RefreshRate ? 2000 : 500,
                NotificationDuration.Normal => notification.Type == NotificationType.RefreshRate ? 3500 : 1000,
                NotificationDuration.Long   => notification.Type == NotificationType.RefreshRate ? 5000 : 2500,
                _ => throw new ArgumentException(nameof(effectiveDuration))
            };

            var effectivePosition = _settings.Store.Notifications.PositionOverrides.TryGetValue(notification.Type, out var posOverride)
                ? posOverride
                : _settings.Store.NotificationPosition;

            ShowNotification(duration, symbol, overlaySymbol, symbolTransform, text, textColor, clickAction, effectivePosition);

            Log.Instance.Trace($"Notification {notification} shown.");
        });
    }

    private void ShowNotification(int duration, SymbolRegular symbol, SymbolRegular? overlaySymbol, Action<SymbolIcon>? symbolTransform, string text, Brush? textColor, Action? clickAction, NotificationPosition position)
    {
        if (App.Current.MainWindow is not MainWindow mainWindow)
            return;

        if (_windows.Count != 0)
        {
            foreach (var window in _windows)
                window?.Close(true);

            _windows.Clear();
        }

        ScreenHelper.UpdateScreenInfos();

        if (_settings.Store.NotificationOnAllScreens)
        {
            foreach (var screen in ScreenHelper.Screens)
            {
                ShowOnScreen(screen, duration, symbol, overlaySymbol, symbolTransform, text, textColor, clickAction, position);
            }
        }
        else
        {
            var primaryScreen = ScreenHelper.PrimaryScreen;
            if (primaryScreen.HasValue)
            {
                ShowOnScreen(primaryScreen.Value, duration, symbol, overlaySymbol, symbolTransform, text, textColor, clickAction, position);
            }
        }
    }

    private void ShowOnScreen(ScreenInfo screen, int duration, SymbolRegular symbol, SymbolRegular? overlaySymbol, Action<SymbolIcon>? symbolTransform, string text, Brush? textColor, Action? clickAction, NotificationPosition position)
    {
        var nw = new NotificationWindow(symbol, overlaySymbol, symbolTransform, text, textColor, clickAction, screen, position);
        if (_settings.Store.NotificationAlwaysOnTop)
        {
            nw.SourceInitialized += (_, _) => nw.EscalateZBand();
        }

        nw.Show(duration);
        _windows.Add(nw);
    }

    internal static SymbolRegular GetDefaultSymbol(NotificationType type) => type switch
    {
        NotificationType.ACAdapterConnected => SymbolRegular.BatteryCharge24,
        NotificationType.ACAdapterConnectedLowWattage => SymbolRegular.BatteryCharge24,
        NotificationType.ACAdapterDisconnected => SymbolRegular.BatteryCharge24,
        NotificationType.AirplaneModeOn => SymbolRegular.WifiOff24,
        NotificationType.AirplaneModeOff => SymbolRegular.Wifi124,
        NotificationType.AutomationNotification => SymbolRegular.Rocket24,
        NotificationType.CapsLockOn => SymbolRegular.KeyboardShiftUppercase24,
        NotificationType.CapsLockOff => SymbolRegular.KeyboardShiftUppercase24,
        NotificationType.CameraOn => SymbolRegular.Camera24,
        NotificationType.CameraOff => SymbolRegular.Camera24,
        NotificationType.FnLockOn => SymbolRegular.Keyboard24,
        NotificationType.FnLockOff => SymbolRegular.Keyboard24,
        NotificationType.MicrophoneOn => SymbolRegular.Mic24,
        NotificationType.MicrophoneOff => SymbolRegular.Mic24,
        NotificationType.NumLockOn => SymbolRegular.Keyboard12324,
        NotificationType.NumLockOff => SymbolRegular.Keyboard12324,
        NotificationType.PanelLogoLightingOn => SymbolRegular.LightbulbCircle24,
        NotificationType.PanelLogoLightingOff => SymbolRegular.LightbulbCircle24,
        NotificationType.PortLightingOn => SymbolRegular.UsbPlug24,
        NotificationType.PortLightingOff => SymbolRegular.UsbPlug24,
        NotificationType.PowerModeQuiet => SymbolRegular.Gauge24,
        NotificationType.PowerModeBalance => SymbolRegular.Gauge24,
        NotificationType.PowerModePerformance => SymbolRegular.Gauge24,
        NotificationType.PowerModeExtreme => SymbolRegular.Gauge24,
        NotificationType.PowerModeGodMode => SymbolRegular.Gauge24,
        NotificationType.RefreshRate => SymbolRegular.DesktopPulse24,
        NotificationType.RGBKeyboardBacklightOff => SymbolRegular.Lightbulb24,
        NotificationType.RGBKeyboardBacklightChanged => SymbolRegular.Lightbulb24,
        NotificationType.SmartKeyDoublePress => SymbolRegular.StarEmphasis24,
        NotificationType.SmartKeySinglePress => SymbolRegular.Star24,
        NotificationType.SpectrumBacklightChanged => SymbolRegular.Lightbulb24,
        NotificationType.SpectrumBacklightOff => SymbolRegular.Lightbulb24,
        NotificationType.SpectrumBacklightPresetChanged => SymbolRegular.Lightbulb24,
        NotificationType.TouchpadOn => SymbolRegular.Tablet24,
        NotificationType.TouchpadOff => SymbolRegular.Tablet24,
        NotificationType.UpdateAvailable => SymbolRegular.ArrowSync24,
        NotificationType.WhiteKeyboardBacklightOff => SymbolRegular.Lightbulb24,
        NotificationType.WhiteKeyboardBacklightChanged => SymbolRegular.Lightbulb24,
        NotificationType.WhiteKeyboardBacklightChangedSpecial => SymbolRegular.Lightbulb24,
        NotificationType.ITSModeAuto => SymbolRegular.Gauge24,
        NotificationType.ITSModeCool => SymbolRegular.Gauge24,
        NotificationType.ITSModePerformance => SymbolRegular.Gauge24,
        NotificationType.ITSModeGeek => SymbolRegular.Gauge24,
    };

    internal static SymbolRegular? GetDefaultOverlaySymbol(NotificationType type) => type switch
    {
        NotificationType.ACAdapterDisconnected => SymbolRegular.Line24,
        NotificationType.AirplaneModeOff => SymbolRegular.Line24,
        NotificationType.CapsLockOff => SymbolRegular.Line24,
        NotificationType.CameraOff => SymbolRegular.Line24,
        NotificationType.FnLockOff => SymbolRegular.Line24,
        NotificationType.MicrophoneOff => SymbolRegular.Line24,
        NotificationType.NumLockOff => SymbolRegular.Line24,
        NotificationType.PanelLogoLightingOff => SymbolRegular.Line24,
        NotificationType.PortLightingOff => SymbolRegular.Line24,
        NotificationType.RGBKeyboardBacklightOff => SymbolRegular.Line24,
        NotificationType.SpectrumBacklightOff => SymbolRegular.Line24,
        NotificationType.TouchpadOff => SymbolRegular.Line24,
        NotificationType.WhiteKeyboardBacklightOff => SymbolRegular.Line24,
        _ => null,
    };

    private static void UpdateAvailableAction()
    {
        if (App.Current.MainWindow is not MainWindow mainWindow)
            return;

        mainWindow.BringToForeground();
        mainWindow.ShowUpdateWindow();
    }
}
