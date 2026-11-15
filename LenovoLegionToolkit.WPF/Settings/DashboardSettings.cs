using LenovoLegionToolkit.Lib.Settings;

namespace LenovoLegionToolkit.WPF.Settings;

public class DashboardSettings() : AbstractSettings<DashboardSettings.DashboardSettingsStore>("dashboard.json")
{
    public const double DefaultSensorsRefreshIntervalSeconds = 2;

    public class DashboardSettingsStore
    {
        public bool ShowSensors { get; set; } = true;
        public double SensorsRefreshIntervalSeconds { get; set; } = DefaultSensorsRefreshIntervalSeconds;
        public DashboardGroup[]? Groups { get; set; }
    }

    protected override DashboardSettingsStore Default => new();
}
