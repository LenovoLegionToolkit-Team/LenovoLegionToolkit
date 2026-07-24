using System;
using System.Collections.Generic;
using System.Linq;
using LenovoLegionToolkit.Lib.Extensions;
using LibreHardwareMonitor.Hardware;

namespace LenovoLegionToolkit.Lib.Controllers.Sensors.Providers;

public class CpuSensorProvider : ISensorProvider
{
    private const float MinTemp = 0f;
    private const float MaxTemp = 150f;

    private static readonly string[] TempFallback = ["Average", "Package", "Tdie", "Core"];
    private static readonly string[] PowerFallback = ["CPU Package", "CPU PPT", "PPT"];

    private static readonly SensorSlot[] Slots =
    [
        new(SensorItem.CpuTemperature, SensorType.Temperature, "Tctl"),
        new(SensorItem.CpuUtilization, SensorType.Load,        "Total"),
        new(SensorItem.CpuPower,       SensorType.Power,       "Package"),
    ];

    private readonly PowerMonitor _powerMonitor = new();
    private readonly Dictionary<SensorItem, ISensor> _sensors = [];
    private List<ISensor> _coreClocks = [];
    private List<ISensor> _pCoreClocks = [];
    private List<ISensor> _eCoreClocks = [];

    private ISensor? _avgVoltageSensor;
    private List<ISensor> _coreVoltageSensors = [];
    public CpuVoltageMode VoltageMode { get; set; }
    public int VoltageCoreIndex { get; set; }

    public int AvailableCoreCount => _coreVoltageSensors.Count;

    public HardwareUpdateScope Scope => HardwareUpdateScope.Cpu;
    public IReadOnlySet<SensorItem> ProvidedSensorItems { get; } = new HashSet<SensorItem>
    {
        SensorItem.CpuUtilization, SensorItem.CpuFrequency, SensorItem.CpuTemperature, SensorItem.CpuPower,
    };
    public IReadOnlySet<OsdItem> ProvidedOsdItems { get; } = new HashSet<OsdItem>
    {
        OsdItem.CpuFrequency, OsdItem.CpuPCoreFrequency, OsdItem.CpuECoreFrequency,
        OsdItem.CpuUtilization, OsdItem.CpuTemperature, OsdItem.CpuPower, OsdItem.CpuVoltage,
    };

    public bool IsAvailable { get; private set; }
    public bool IsHybrid { get; private set; }
    public IReadOnlyDictionary<SensorItem, float> Values { get; private set; } = new Dictionary<SensorItem, float>();
    public event Action? SensorResetNeeded;

    public CpuSensorProvider()
    {
        _powerMonitor.ResetNeeded += () => SensorResetNeeded?.Invoke();
    }

    public void Discover(IReadOnlyList<IHardware> hardware)
    {
        _sensors.Clear();
        _coreClocks = [];
        _pCoreClocks = [];
        _eCoreClocks = [];
        _powerMonitor.Reset();
        IsAvailable = false;
        IsHybrid = false;

        var cpu = hardware.FirstOrDefault(h => h.HardwareType == HardwareType.Cpu);
        if (cpu?.Sensors == null) return;
        IsAvailable = true;

        foreach (var slot in Slots)
        {
            _sensors[slot.Item] = cpu.Sensors.FirstOrDefault(s => s.SensorType == slot.Type && s.Name.Contains(slot.NamePattern))!;
        }

        _sensors[SensorItem.CpuTemperature] ??= cpu.Sensors.FindByKeyword(SensorType.Temperature, TempFallback)!;
        _sensors[SensorItem.CpuPower]       ??= cpu.Sensors.FindPowerSensor(PowerFallback)!;
        _sensors[SensorItem.CpuUtilization] ??= cpu.Sensors.FirstOrDefault(s => s.SensorType == SensorType.Load)!;

        _avgVoltageSensor = null;
        _coreVoltageSensors = [];
        foreach (var sensor in cpu.Sensors.Where(s => s.SensorType == SensorType.Voltage && s.Name.Contains("Core", StringComparison.OrdinalIgnoreCase)))
        {
            if (sensor.Name.Contains('#'))
                _coreVoltageSensors.Add(sensor);
            else
                _avgVoltageSensor = sensor;
        }
        _avgVoltageSensor ??= cpu.Sensors.FirstOrDefault(s => s.SensorType == SensorType.Voltage)!;

        var coreList = new List<ISensor>();
        var pCoreList = new List<ISensor>();
        var eCoreList = new List<ISensor>();

        foreach (var sensor in cpu.Sensors.Where(s => s.SensorType == SensorType.Clock))
        {
            if (sensor.Name.Contains("P-Core", StringComparison.OrdinalIgnoreCase))
            {
                pCoreList.Add(sensor);
            }
            else if (sensor.Name.Contains("E-Core", StringComparison.OrdinalIgnoreCase))
            {
                eCoreList.Add(sensor);
            }
            else if (sensor.Name.Contains("Core", StringComparison.OrdinalIgnoreCase) &&
                     !sensor.Name.Contains("Average", StringComparison.OrdinalIgnoreCase) &&
                     !sensor.Name.Contains("Effective", StringComparison.OrdinalIgnoreCase))
            {
                coreList.Add(sensor);
            }
        }

        _coreClocks = coreList;
        _pCoreClocks = pCoreList;
        _eCoreClocks = eCoreList;
        IsHybrid = pCoreList.Count > 0;
    }

    public void Read()
    {
        var values = new Dictionary<SensorItem, float>();

        foreach (var slot in Slots)
        {
            float val = _sensors.TryGetValue(slot.Item, out var sensor) && sensor != null ? sensor.Value ?? -1 : -1;
            if (slot.Item == SensorItem.CpuPower)
            {
                val = _powerMonitor.Read(val);
            }
            else if (slot.Item == SensorItem.CpuTemperature)
            {
                val = val > MinTemp && val < MaxTemp ? val : -1;
            }

            values[slot.Item] = val;
        }

        values[SensorItem.CpuVoltage] = VoltageMode switch
        {
            CpuVoltageMode.Maximum when _coreVoltageSensors.Count > 0 => _coreVoltageSensors.Max(s => s.Value) ?? -1,
            CpuVoltageMode.Core when VoltageCoreIndex < _coreVoltageSensors.Count => _coreVoltageSensors[VoltageCoreIndex].Value ?? -1,
            CpuVoltageMode.Core => -1,
            _ => _avgVoltageSensor?.Value ?? -1,
        };

        _coreClocks.Accum(values, SensorItem.CpuMaxFrequency, SensorItem.CpuAverageFrequency);
        _pCoreClocks.Accum(values, SensorItem.CpuPMaxFrequency, SensorItem.CpuPAverageFrequency);
        _eCoreClocks.Accum(values, SensorItem.CpuEMaxFrequency, SensorItem.CpuEAverageFrequency);

        values[SensorItem.CpuFrequency] = IsHybrid
            ? values.GetValueOrDefault(SensorItem.CpuPMaxFrequency, -1)
            : values.GetValueOrDefault(SensorItem.CpuMaxFrequency, -1);

        Values = values;
    }

    public void Reset()
    {
        _sensors.Clear();
        _coreClocks = [];
        _pCoreClocks = [];
        _eCoreClocks = [];
        _powerMonitor.Reset();
        _avgVoltageSensor = null;
        _coreVoltageSensors = [];
        IsAvailable = false; IsHybrid = false;
        Values = new Dictionary<SensorItem, float>();
    }

}
