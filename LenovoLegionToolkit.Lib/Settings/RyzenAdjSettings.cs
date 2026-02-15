namespace LenovoLegionToolkit.Lib.Settings;

/// <summary>
/// Settings for RyzenAdj (AMD CPU tuning via SMU).
/// Stored separately from GodModeSettings for independent control.
/// </summary>
public class RyzenAdjSettings() : AbstractSettings<RyzenAdjSettings.RyzenAdjSettingsStore>("ryzenadjsettings.json")
{
    public class RyzenAdjSettingsStore
    {
        /// <summary>
        /// Whether RyzenAdj is enabled.
        /// </summary>
        public bool Enabled { get; set; } = false;

        /// <summary>
        /// Automatically apply settings on application startup.
        /// </summary>
        public bool AutoApplyOnStartup { get; set; } = false;

        /// <summary>
        /// Automatically re-apply settings when power mode changes.
        /// </summary>
        public bool AutoApplyOnPowerModeChange { get; set; } = false;

        // === Power Limits ===
        
        /// <summary>
        /// Whether STAPM limit is enabled.
        /// </summary>
        public bool StapmLimitEnabled { get; set; } = false;

        /// <summary>
        /// STAPM (Sustained) power limit in watts.
        /// </summary>
        public int? StapmLimit { get; set; }

        /// <summary>
        /// Whether Slow PPT limit is enabled.
        /// </summary>
        public bool SlowLimitEnabled { get; set; } = false;

        /// <summary>
        /// Slow PPT power limit in watts.
        /// </summary>
        public int? SlowLimit { get; set; }

        /// <summary>
        /// Whether Fast PPT limit is enabled.
        /// </summary>
        public bool FastLimitEnabled { get; set; } = false;

        /// <summary>
        /// Fast PPT (boost) power limit in watts.
        /// </summary>
        public int? FastLimit { get; set; }

        // === Temperature ===

        /// <summary>
        /// Whether CPU temperature limit is enabled.
        /// </summary>
        public bool CpuTempLimitEnabled { get; set; } = false;

        /// <summary>
        /// CPU temperature limit in Celsius (75-98).
        /// </summary>
        public int? CpuTempLimit { get; set; }

        // === Undervolting ===

        /// <summary>
        /// Whether CPU undervolting is enabled.
        /// </summary>
        public bool CpuUndervoltEnabled { get; set; } = false;

        /// <summary>
        /// CPU All-Core Curve Optimizer offset (-40 to 0).
        /// Negative values = undervolting.
        /// </summary>
        public int? CpuUndervolt { get; set; }

        /// <summary>
        /// Whether iGPU undervolting is enabled.
        /// </summary>
        public bool IgpuUndervoltEnabled { get; set; } = false;

        /// <summary>
        /// iGPU Curve Optimizer offset (-30 to 0).
        /// Negative values = undervolting.
        /// </summary>
        public int? IgpuUndervolt { get; set; }

        /// <summary>
        /// User has acknowledged the UV warning dialog.
        /// </summary>
        public bool UndervoltWarningAcknowledged { get; set; } = false;

        // Future expansion fields (reserved)
        // public int? SkinTempLimit { get; set; }
        // public int? ApuSlowLimit { get; set; }
        // public int? VrmCurrentLimit { get; set; }
        // public int? SocPowerLimit { get; set; }
        // public int? GfxMaxFrequency { get; set; }
    }
}
