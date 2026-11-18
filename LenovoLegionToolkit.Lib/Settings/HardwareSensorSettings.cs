namespace LenovoLegionToolkit.Lib.Settings;

public sealed class HardwareSensorSettings : AbstractSettings<HardwareSensorSettings.HardwareSensorSettingsStore>
{
    public sealed record HardwareSensorSettingsStore
    {
        public bool SelectedGpuIsIgpu { get; init; }
        public bool ShowCpuAverageFrequency { get; init; }
        public bool DisplayMemoryInGigabytes { get; init; }
        public CpuVoltageMode CpuVoltageMode { get; init; }
        public int CpuVoltageCoreIndex { get; init; }
        public CpuTemperatureSource CpuTemperatureSource { get; init; }
    }

    public HardwareSensorSettings() : base("hardware_sensors.json")
    {
        Store = LoadStore() ?? Default;
    }

    public void Save(HardwareSensorSettingsStore settings)
    {
        Store = settings;
        base.Save();
    }
}
