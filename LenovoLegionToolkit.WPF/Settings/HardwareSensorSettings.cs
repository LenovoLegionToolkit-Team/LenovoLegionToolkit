using LenovoLegionToolkit.Lib;
using LenovoLegionToolkit.Lib.Settings;

namespace LenovoLegionToolkit.WPF.Settings;

public class HardwareSensorSettings() : AbstractSettings<HardwareSensorSettings.HardwareSensorSettingsStore>("hardware_sensors.json")
{
    public class HardwareSensorSettingsStore
    {
        public bool SelectedGpuIsIgpu { get; set; }
        public bool ShowCpuAverageFrequency { get; set; }
        public bool DisplayMemoryInGigabytes { get; set; }
        public CpuVoltageMode CpuVoltageMode { get; set; }
        public int CpuVoltageCoreIndex { get; set; }
        public CpuTemperatureSource CpuTemperatureSource { get; set; }
    }

    public new void Reset()
    {
        Store = Default;
    }

    protected override HardwareSensorSettingsStore Default => new();
}
