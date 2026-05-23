using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using static LenovoLegionToolkit.Lib.Settings.ApplicationSettings;

namespace LenovoLegionToolkit.Lib.Settings;

public class ApplicationSettings : AbstractSettings<ApplicationSettingsStore>
{
    public class ApplicationSettingsStore
    {
        public Theme Theme { get; set; }
        public RGBColor? AccentColor { get; set; }
        public AccentColorSource AccentColorSource { get; set; }
        public PowerModeMappingMode PowerModeMappingMode { get; set; } = PowerModeMappingMode.Disabled;
        public Dictionary<PowerModeState, Guid> PowerPlans { get; set; } = [];
        public Dictionary<PowerModeState, WindowsPowerMode> PowerModes { get; set; } = [];
        public Dictionary<PowerModeState, Dictionary<PowerOverrideKey, string>> Overrides { get; set; } = [];

        public Dictionary<ITSMode, Guid> ITSPowerPlans { get; set; } = [];
        public Dictionary<ITSMode, WindowsPowerMode> ITSPowerModes { get; set; } = [];
        public Dictionary<ITSMode, Dictionary<PowerOverrideKey, string>> ITSOverrides { get; set; } = [];

        public bool MinimizeToTray { get; set; } = true;
        public bool MinimizeOnClose { get; set; }
        public WindowSize? WindowSize { get; set; }
        public TemperatureUnit TemperatureUnit { get; set; }
        public List<RefreshRate> ExcludedRefreshRates { get; set; } = [];
        public WarrantyInfo? WarrantyInfo { get; set; }
        public bool SynchronizeBrightnessToAllPowerPlans { get; set; }
        public ModifierKey SmartFnLockFlags { get; set; }
        public bool ResetBatteryOnSinceTimerOnReboot { get; set; }
        public bool UseNewSensorDashboard { get; set; }
        public bool EnableHardwareSensors { get; set; }
        public bool LockWindowSize { get; set; }
        public bool AlwaysOnTop { get; set; }
        public bool CompactMode { get; set; }
        public WindowPosition? WindowPosition { get; set; }
        public bool EnableLogging { get; set; }
        public string BackGroundImageFilePath { get; set; } = string.Empty;
        public double Opacity { get; set; } = 0.3;
        public int BackgroundImageBlur { get; set; } = 0;
        public BackgroundImageStretchMode BackgroundImageStretch { get; set; } = BackgroundImageStretchMode.Crop;
        public List<string> ExcludedProcesses { get; set; } = [];
        public GameDetectionSettings GameDetection { get; set; } = new();
        public bool DynamicLightingWarningDontShowAgain { get; set; }
        public bool CustomModeWarningDontShowAgain { get; set; }
        public bool EnableHardwareAcceleration { get; set; }
        public int GPUMonitoringInterval { get; set; } = 5000;
        public int GPUMonitoringStartupDelay { get; set; } = 1000;
        public int GPUKillProcessDelay { get; set; } = 500;
        public WindowBackdropType BackdropType { get; set; } = WindowBackdropType.Mica;
    }

    public class GameDetectionSettings
    {
        public bool UseDiscreteGPU { get; set; } = true;
        public bool UseGameConfigStore { get; set; } = true;
        public bool UseEffectiveGameMode { get; set; } = true;
    }

    public ApplicationSettings() : base("settings.json")
    {
    }
}
