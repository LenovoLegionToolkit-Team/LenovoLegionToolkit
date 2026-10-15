using System;
using System.Collections.Generic;
using System.Linq;
using LenovoLegionToolkit.Lib.Utils;

namespace LenovoLegionToolkit.Lib.Settings;

public class OsdSettings() : AbstractSettings<OsdSettings.OsdSettingsStore>("osd.json")
{
    public class OsdSettingsStore
    {
        public bool ShowOsd { get; set; }
        public double OsdRefreshInterval { get; set; } = 1;
        public int SelectedStyleIndex { get; set; } = 0;
        public List<OsdItem> Items { get; set; } = Enum.GetValues<OsdItem>().ToList();

        public double BackgroundOpacity { get; set; } = 0.6;
        public string BackgroundColor { get; set; } = "#1E1E1E";
        public int FontSize { get; set; } = 12;
        public int CornerRadiusTop { get; set; } = 6;
        public int CornerRadiusBottom { get; set; } = 6;
        public bool IsLocked { get; set; } = false;
        public OsdPosition? PanelPosition { get; set; }
        public OsdPosition? BarPosition { get; set; }

        public int TempThresholdWarning { get; set; } = 75;
        public int TempThresholdCritical { get; set; } = 90;
        public int UsageThresholdWarning { get; set; } = 70;
        public int UsageThresholdCritical { get; set; } = 90;
        public int FpsThresholdCritical { get; set; } = 30;
        public int LowFpsDeltaThreshold { get; set; } = 30;

        public string CategoryColor { get; set; } = "#2196F3";
        public string LabelColor { get; set; } = "#ADFF2F";
        public string ValueColor { get; set; } = "#FFFFFF";
        public string WarningColor { get; set; } = "#FFFF00";
        public string CriticalColor { get; set; } = "#FF0000";
        public int SnapThreshold { get; set; } = 20;
    }
}

public class OsdPosition
{
    public double XRatio { get; set; }
    public double YRatio { get; set; }
    public string? MonitorName { get; set; }
}
