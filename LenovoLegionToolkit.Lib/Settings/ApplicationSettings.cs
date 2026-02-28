using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using static LenovoLegionToolkit.Lib.Settings.ApplicationSettings;

namespace LenovoLegionToolkit.Lib.Settings;

public class ApplicationSettings : AbstractSettings<ApplicationSettingsStore>
{
    public class Notifications
    {
        public bool UpdateAvailable { get; set; } = true;
        public bool CapsNumLock { get; set; }
        public bool FnLock { get; set; }
        public bool TouchpadLock { get; set; } = true;
        public bool KeyboardBacklight { get; set; } = true;
        public bool CameraLock { get; set; } = true;
        public bool Microphone { get; set; } = true;
        public bool PowerMode { get; set; }
        public bool RefreshRate { get; set; } = true;
        public bool ACAdapter { get; set; }
        public bool SmartKey { get; set; }
        public bool AutomationNotification { get; set; } = true;
        public bool ITSMode { get; set; } = true;
        /// <summary>
        /// Show notifications when a new process starts using the discrete GPU.
        /// </summary>
        public bool GpuProcesses { get; set; } = true;
    }

    public class ApplicationSettingsStore
    {
        public Theme Theme { get; set; }
        public RGBColor? AccentColor { get; set; }
        public AccentColorSource AccentColorSource { get; set; }
        public PowerModeMappingMode PowerModeMappingMode { get; set; } = PowerModeMappingMode.Disabled;
        public Dictionary<PowerModeState, Guid> PowerPlans { get; set; } = [];
        public Dictionary<PowerModeState, WindowsPowerMode> PowerModes { get; set; } = [];
        public bool MinimizeToTray { get; set; } = true;
        public bool MinimizeOnClose { get; set; }
        public WindowSize? WindowSize { get; set; }
        public bool DontShowNotifications { get; set; }
        public NotificationPosition NotificationPosition { get; set; } = NotificationPosition.BottomCenter;
        public NotificationDuration NotificationDuration { get; set; } = NotificationDuration.Normal;
        public bool NotificationAlwaysOnTop { get; set; }
        public bool NotificationOnAllScreens { get; set; }
        public Notifications Notifications { get; set; } = new();
        public TemperatureUnit TemperatureUnit { get; set; }
        public List<RefreshRate> ExcludedRefreshRates { get; set; } = [];
        public WarrantyInfo? WarrantyInfo { get; set; }
        public Guid? SmartKeySinglePressActionId { get; set; }
        public Guid? SmartKeyDoublePressActionId { get; set; }
        public List<Guid> SmartKeySinglePressActionList { get; set; } = [];
        public List<Guid> SmartKeyDoublePressActionList { get; set; } = [];
        public bool SynchronizeBrightnessToAllPowerPlans { get; set; }
        public ModifierKey SmartFnLockFlags { get; set; }
        public bool ResetBatteryOnSinceTimerOnReboot { get; set; }
        public bool UseNewSensorDashboard { get; set; }
        public bool EnableHardwareSensors { get; set; }
        public bool LockWindowSize { get; set; }
        public bool EnableLogging { get; set; }
        public string BackGroundImageFilePath { get; set; } = string.Empty;
        public double Opacity { get; set; } = 1.0f;
        public List<string> ExcludedProcesses { get; set; } = [];
        public GameDetectionSettings GameDetection { get; set; } = new();
        public bool DynamicLightingWarningDontShowAgain { get; set; }
        public bool CustomModeWarningDontShowAgain { get; set; }
        public bool EnableHardwareAcceleration { get; set; }
        public WindowBackdropType BackdropType { get; set; } = WindowBackdropType.Mica;
        public bool HighPerformanceUI { get; set; }
        public int KeyboardBacklightTimeout { get; set; }
    }

    public class GameDetectionSettings
    {
        public bool UseDiscreteGPU { get; set; } = true;
        public bool UseGameConfigStore { get; set; } = true;
        public bool UseEffectiveGameMode { get; set; } = true;
    }

    public ApplicationSettings() : base("settings.json")
    {
        JsonSerializerSettings.Converters.Add(new LegacyPowerPlanInstanceIdToGuidConverter());
    }
}

internal class LegacyPowerPlanInstanceIdToGuidConverter : JsonConverter
{
    public override bool CanWrite => false;

    public override bool CanConvert(Type objectType) => objectType == typeof(Guid);

    public override void WriteJson(JsonWriter writer, object? value, JsonSerializer serializer) => throw new InvalidOperationException();

    public override object ReadJson(JsonReader reader, Type objectType, object? existingValue, JsonSerializer serializer)
    {
        var value = reader.Value?.ToString() ?? string.Empty;

        const string prefix = "Microsoft:PowerPlan\\{";
        const string suffix = "}";

        var prefixIndex = value.Contains(prefix, StringComparison.InvariantCulture);
        var suffixIndex = value.IndexOf(suffix, StringComparison.InvariantCulture);

        if (prefixIndex && suffixIndex > 0)
        {
            value = value[..suffixIndex];
            value = value[prefix.Length..];
        }

        return Guid.Parse(value);
    }
}
