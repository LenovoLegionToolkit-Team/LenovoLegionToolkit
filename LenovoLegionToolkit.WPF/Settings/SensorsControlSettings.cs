using System.Linq;
using LenovoLegionToolkit.Lib;
using LenovoLegionToolkit.Lib.Settings;

namespace LenovoLegionToolkit.WPF.Settings;

public class SensorsControlSettings() : AbstractSettings<SensorsControlSettings.SensorsControlSettingsStore>("sensor_dashboard.json")
{
    public class SensorsControlSettingsStore
    {
        public bool ShowSensors { get; set; } = true;
        public double SensorsRefreshIntervalSeconds { get; set; } = 1;
        public SensorGroup[]? Groups { get; set; } = SensorGroup.DefaultGroups;
        public SensorItem[]? VisibleItems { get; set; } = SensorGroup.DefaultGroups.SelectMany(group => group.Items).ToArray();
    }

    public new void Reset()
    {
        Store = Default;
    }

    protected override SensorsControlSettingsStore Default => new();
}
