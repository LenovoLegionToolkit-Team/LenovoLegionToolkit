using System.Collections.Generic;

namespace LenovoLegionToolkit.Lib.Settings;

public class NotificationSettings() : AbstractSettings<NotificationSettings.NotificationSettingsStore>("notification_settings.json")
{
    public class Notifications
    {
        public bool UpdateAvailable { get; set; } = true;
        public bool CapsLock { get; set; }
        public bool NumLock { get; set; }
        public bool FnLock { get; set; }
        public bool TouchpadLock { get; set; } = true;
        public bool KeyboardBacklight { get; set; } = true;
        public bool CameraLock { get; set; } = true;
        public bool AirplaneMode { get; set; } = true;
        public bool Microphone { get; set; } = true;
        public bool PowerMode { get; set; }
        public bool RefreshRate { get; set; } = true;
        public bool ACAdapter { get; set; }
        public bool SmartKey { get; set; }
        public bool AutomationNotification { get; set; } = true;
        public bool ITSMode { get; set; } = true;

        public Dictionary<NotificationType, int> IconOverrides { get; set; } = [];
        public Dictionary<NotificationType, RGBColor> ColorOverrides { get; set; } = [];
        public Dictionary<NotificationType, RGBColor> TextColorOverrides { get; set; } = [];
        public Dictionary<NotificationType, NotificationPosition> PositionOverrides { get; set; } = [];
        public Dictionary<NotificationType, NotificationDuration> DurationOverrides { get; set; } = [];
    }

    public class NotificationSettingsStore
    {
        public bool DontShowNotifications { get; set; }
        public NotificationPosition NotificationPosition { get; set; } = NotificationPosition.BottomCenter;
        public NotificationDuration NotificationDuration { get; set; } = NotificationDuration.Normal;
        public bool NotificationAlwaysOnTop { get; set; }
        public bool NotificationOnAllScreens { get; set; }
        public Notifications Notifications { get; set; } = new();
    }

    protected override NotificationSettingsStore Default => new();
}
