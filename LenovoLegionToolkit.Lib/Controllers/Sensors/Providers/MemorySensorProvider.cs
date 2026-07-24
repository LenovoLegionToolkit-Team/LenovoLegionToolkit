using System;
using System.Collections.Generic;
using System.Linq;
using LibreHardwareMonitor.Hardware;

namespace LenovoLegionToolkit.Lib.Controllers.Sensors.Providers;

public class MemorySensorProvider : ISensorProvider
{
    private const float MinTemp = 0f;
    private const float MaxTemp = 150f;

    private const string MEM_TOTAL = "Total Memory";
    private const string MEM_USED = "Memory Used";
    private const string MEM_AVAILABLE = "Memory Available";

    private static readonly string[] TempKeywords = ["DIMM", "SPD", "Temperature"];

    private ISensor? _loadSensor, _usedSensor, _availableSensor;
    private readonly List<ISensor> _tempSensors = [];
    private float _cachedTotal = -1;

    public HardwareUpdateScope Scope => HardwareUpdateScope.Memory;
    public IReadOnlySet<SensorItem> ProvidedSensorItems { get; } = new HashSet<SensorItem>
    {
        SensorItem.MemoryUtilization, SensorItem.MemoryUsed, SensorItem.MemoryTotal, SensorItem.MemoryTemperature,
    };
    public IReadOnlySet<OsdItem> ProvidedOsdItems { get; } = new HashSet<OsdItem>
    {
        OsdItem.MemoryUtilization, OsdItem.MemoryTemperature,
    };

    public bool IsAvailable { get; private set; }
    public IReadOnlyDictionary<SensorItem, float> Values { get; private set; } = new Dictionary<SensorItem, float>();

    public void Discover(IReadOnlyList<IHardware> hardware)
    {
        _loadSensor = null; _usedSensor = null; _availableSensor = null;
        _tempSensors.Clear();
        _cachedTotal = -1;
        IsAvailable = false;

        var mem = hardware.FirstOrDefault(h => h is { HardwareType: HardwareType.Memory, Name: MEM_TOTAL });
        if (mem?.Sensors == null) return;
        IsAvailable = true;

        _loadSensor = mem.Sensors.FirstOrDefault(s => s.SensorType == SensorType.Load);
        _usedSensor = mem.Sensors.FirstOrDefault(s => s.SensorType == SensorType.Data && s.Name.Contains(MEM_USED, StringComparison.OrdinalIgnoreCase));
        _availableSensor = mem.Sensors.FirstOrDefault(s => s.SensorType == SensorType.Data && s.Name.Contains(MEM_AVAILABLE, StringComparison.OrdinalIgnoreCase));

        foreach (var hw in hardware.Where(h => h.HardwareType == HardwareType.Memory))
        {
            if (hw.Sensors == null) continue;
            foreach (var s in hw.Sensors)
            {
                if (s.SensorType != SensorType.Temperature) continue;
                for (int ki = 0; ki < TempKeywords.Length; ki++)
                {
                    if (s.Name.Contains(TempKeywords[ki], StringComparison.OrdinalIgnoreCase))
                    {
                        _tempSensors.Add(s);
                        break;
                    }
                }
            }
        }
    }

    public void Read()
    {
        var values = new Dictionary<SensorItem, float>();

        float usage = _loadSensor?.Value ?? -1;
        float used = _usedSensor?.Value ?? -1;
        float total = (_usedSensor?.Value ?? 0) + (_availableSensor?.Value ?? 0);
        if (total <= 0) total = -1;

        if (used >= 0 && total > 0)
        {
            if (usage < 0) usage = (used / total) * 100f;
        }
        else if (_cachedTotal > 0 && usage >= 0)
        {
            total = _cachedTotal;
            used = (usage / 100f) * total;
        }

        double maxTemp = _tempSensors.Count > 0
            ? (double)_tempSensors.Max(s => { var v = s.Value ?? 0; return v > MinTemp && v < MaxTemp ? v : -1; })
            : -1.0;

        values[SensorItem.MemoryUtilization] = usage;
        values[SensorItem.MemoryUsed] = used;
        values[SensorItem.MemoryTotal] = total;
        values[SensorItem.MemoryTemperature] = (float)maxTemp;

        Values = values;
    }

    public void Reset()
    {
        _loadSensor = null; _usedSensor = null; _availableSensor = null;
        _tempSensors.Clear(); _cachedTotal = -1;
        IsAvailable = false;
        Values = new Dictionary<SensorItem, float>();
    }
}
