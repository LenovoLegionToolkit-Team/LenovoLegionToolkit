using System.Collections.Generic;
using System.Linq;
using LibreHardwareMonitor.Hardware;

namespace LenovoLegionToolkit.Lib.Controllers.Sensors.Providers;

public class StorageSensorProvider : ISensorProvider
{
    private const float MinTemp = 0f;
    private const float MaxTemp = 150f;

    private readonly List<ISensor> _sensors = [];

    public HardwareUpdateScope Scope => HardwareUpdateScope.Storage;
    public IReadOnlySet<SensorItem> ProvidedSensorItems { get; } = new HashSet<SensorItem>
    {
        SensorItem.Disk1Temperature, SensorItem.Disk2Temperature,
    };
    public IReadOnlySet<OsdItem> ProvidedOsdItems { get; } = new HashSet<OsdItem>
    {
        OsdItem.Disk1Temperature, OsdItem.Disk2Temperature,
    };

    public bool IsAvailable => _sensors.Count > 0;
    public IReadOnlyDictionary<SensorItem, float> Values { get; private set; } = new Dictionary<SensorItem, float>();

    private static readonly SensorItem[] DiskItems = [SensorItem.Disk1Temperature, SensorItem.Disk2Temperature];

    public void Discover(IReadOnlyList<IHardware> hardware)
    {
        _sensors.Clear();

        foreach (var storage in hardware.Where(h => h.HardwareType == HardwareType.Storage))
        {
            var t = storage.Sensors?.FirstOrDefault(s => s.SensorType == SensorType.Temperature);
            if (t != null)
                _sensors.Add(t);
        }
    }

    public void Read()
    {
        var values = new Dictionary<SensorItem, float>
        {
            [SensorItem.Disk1Temperature] = -1,
            [SensorItem.Disk2Temperature] = -1,
        };

        for (int i = 0; i < DiskItems.Length && i < _sensors.Count; i++)
        {
            var v = _sensors[i].Value ?? -1;
            values[DiskItems[i]] = v > MinTemp && v < MaxTemp ? v : -1;
        }

        Values = values;
    }

    public void Reset()
    {
        _sensors.Clear();
        Values = new Dictionary<SensorItem, float>();
    }
}
