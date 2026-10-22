using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using LenovoLegionToolkit.Lib.Extensions;
using LenovoLegionToolkit.Lib.Utils;
using LibreHardwareMonitor.Hardware;

namespace LenovoLegionToolkit.Lib.Controllers.Sensors.Providers;

public partial class GpuSensorProvider : ISensorProvider
{
    private const float MinTemp = 0f;
    private const float MaxTemp = 150f;
    private const float MB_PER_GB = 1024f;
    private const float MIN_ACTIVE_GPU_POWER = 10f;

    private const string REGEX_AMD_GPU_INTEGRATED = @"AMD Radeon(?:\(TM\))?\s+\d+M";

    private const string INTEGRATED_IDENTIFIER = "integrated";

    [GeneratedRegex(REGEX_AMD_GPU_INTEGRATED, RegexOptions.IgnoreCase, "zh-CN")]
    private static partial Regex IsAmdIGpu();

    private static readonly string[] TempFallback = ["Average", "GPU Core", "Junction", "Hot Spot", "Temperature"];
    private static readonly string[] PowerFallback = ["GPU Power", "Board Power", "Total Graphics Power", "Power"];

    private static readonly SensorSlot[] Slots =
    [
        new(SensorItem.GpuUtilization,     SensorType.Load,        "Core"),
        new(SensorItem.GpuCoreTemperature, SensorType.Temperature, "Core"),
        new(SensorItem.GpuFrequency,       SensorType.Clock,       "Core"),
        new(SensorItem.GpuPower,           SensorType.Power,       ""),
        new(SensorItem.GpuVramTemperature, SensorType.Temperature, "GPU Memory Junction", dgpuOnly: true),
        new(SensorItem.GpuVramUtilization, SensorType.Data,        "D3D Dedicated Memory Used"),
        new(SensorItem.GpuVramTotal,       SensorType.Data,        "GPU Memory Total"),
    ];

    private readonly Dictionary<SensorItem, ISensor> _dgpuSensors = [];
    private readonly Dictionary<SensorItem, ISensor> _igpuSensors = [];
    private float _dgpuVramTotalRaw = -1;
    private float _igpuVramTotalRaw = -1;

    private IHardware? _dgpuHardware, _igpuHardware;

    public HardwareUpdateScope Scope => HardwareUpdateScope.Gpu;
    public IReadOnlySet<SensorItem> ProvidedSensorItems { get; } = new HashSet<SensorItem>
    {
        SensorItem.GpuUtilization, SensorItem.GpuFrequency, SensorItem.GpuCoreTemperature,
        SensorItem.GpuVramTemperature, SensorItem.GpuPower,
        SensorItem.GpuVramUtilization, SensorItem.GpuVramUsed, SensorItem.GpuVramTotal,
    };
    public IReadOnlySet<OsdItem> ProvidedOsdItems { get; } = new HashSet<OsdItem>
    {
        OsdItem.GpuFrequency, OsdItem.GpuUtilization, OsdItem.GpuTemperature,
        OsdItem.GpuVramUtilization, OsdItem.GpuVramTemperature, OsdItem.GpuPower,
    };

    public bool IsAvailable => HasDgpu || HasIgpu;
    public bool HasDgpu => _dgpuHardware is not null;
    public bool HasIgpu => _igpuHardware is not null;
    public IHardware? DgpuHardware => _dgpuHardware;
    public IHardware? IgpuHardware => _igpuHardware;

    public void Discover(IReadOnlyList<IHardware> hardware)
    {
        Reset();

        var gpus = hardware.Where(IsGpu).ToArray();

        _dgpuHardware = gpus.FirstOrDefault(IsDiscrete);
        _igpuHardware = gpus.FirstOrDefault(gpu => !IsDiscrete(gpu));

        DiscoverGpu(_dgpuHardware, _dgpuSensors, dgpu: true);
        DiscoverGpu(_igpuHardware, _igpuSensors, dgpu: false);

        Log.Instance.Trace($"Discovered GPUs. [dgpu={_dgpuHardware?.Name ?? "<null>"}, igpu={_igpuHardware?.Name ?? "<null>"}]");
    }

    private static bool IsGpu(IHardware hardware) =>
        hardware.HardwareType is HardwareType.GpuNvidia or HardwareType.GpuAmd or HardwareType.GpuIntel;

    private static bool IsDiscrete(IHardware gpu) => gpu.HardwareType switch
    {
        HardwareType.GpuAmd => !IsAmdIntegrated(gpu.Name),
        HardwareType.GpuIntel => !gpu.Identifier.ToString().Contains(INTEGRATED_IDENTIFIER, StringComparison.Ordinal),
        _ => true,
    };

    private static bool IsAmdIntegrated(string name) =>
        !string.IsNullOrWhiteSpace(name) && IsAmdIGpu().IsMatch(name);

    private static void DiscoverGpu(IHardware? gpu, Dictionary<SensorItem, ISensor> sensors, bool dgpu)
    {
        sensors.Clear();
        if (gpu?.Sensors == null)
            return;

        foreach (var slot in Slots)
        {
            if (slot.DgpuOnly && !dgpu)
                continue;

            var sensor = gpu.Sensors.FirstOrDefault(x => x.SensorType == slot.Type && (string.IsNullOrEmpty(slot.NamePattern) || x.Name.Contains(slot.NamePattern)));
            sensor ??= gpu.Sensors.FirstOrDefault(x => x.SensorType == slot.Type && x.Name.Contains(slot.NamePattern, StringComparison.OrdinalIgnoreCase));
            sensors[slot.Item] = sensor!;
        }

        sensors[SensorItem.GpuUtilization] ??= gpu.Sensors.FirstOrDefault(s => s.SensorType == SensorType.Load)!;
        sensors[SensorItem.GpuCoreTemperature] ??= gpu.Sensors.FindByKeyword(SensorType.Temperature, TempFallback)!;
        sensors[SensorItem.GpuFrequency] ??= gpu.Sensors.FirstOrDefault(s => s.SensorType == SensorType.Clock)!;
        sensors[SensorItem.GpuPower] ??= gpu.Sensors.FindPowerSensor(PowerFallback)!;
        sensors[SensorItem.GpuVramUtilization] ??= gpu.Sensors.FirstOrDefault(s => s.SensorType == SensorType.Data && s.Name.Contains("Used", StringComparison.OrdinalIgnoreCase))!;
        sensors[SensorItem.GpuVramTotal] ??= gpu.Sensors.FirstOrDefault(s => s.SensorType == SensorType.Data && s.Name.Contains("Total", StringComparison.OrdinalIgnoreCase))!;
    }

    public Dictionary<SensorItem, float> ReadDgpu() => ReadGpu(_dgpuSensors, ref _dgpuVramTotalRaw, dgpu: true);
    public Dictionary<SensorItem, float> ReadIgpu() => ReadGpu(_igpuSensors, ref _igpuVramTotalRaw, dgpu: false);

    public static Dictionary<SensorItem, float> ReadInactive()
    {
        return new Dictionary<SensorItem, float>
        {
            [SensorItem.GpuUtilization] = -1,
            [SensorItem.GpuCoreTemperature] = -1,
            [SensorItem.GpuFrequency] = -1,
            [SensorItem.GpuPower] = -1,
            [SensorItem.GpuVramTemperature] = -1,
            [SensorItem.GpuVramUtilization] = -1,
            [SensorItem.GpuVramUsed] = -1,
            [SensorItem.GpuVramTotal] = -1,
        };
    }

    private static Dictionary<SensorItem, float> ReadGpu(Dictionary<SensorItem, ISensor> sensors, ref float vramTotalRaw, bool dgpu)
    {
        var values = new Dictionary<SensorItem, float>();

        foreach (var slot in Slots)
        {
            if (slot.DgpuOnly && !dgpu) continue;
            if (!sensors.TryGetValue(slot.Item, out var sensor) || sensor == null) continue;

            var val = sensor.Value ?? -1;

            if (slot.Item is SensorItem.GpuCoreTemperature or SensorItem.GpuVramTemperature)
                val = val > MinTemp && val < MaxTemp ? val : -1;

            values[slot.Item] = val;
        }

        float raw = values.GetValueOrDefault(SensorItem.GpuVramUtilization, -1);
        if (vramTotalRaw <= 0 && sensors.TryGetValue(SensorItem.GpuVramTotal, out var vramTotalSensor) && vramTotalSensor != null)
            vramTotalRaw = vramTotalSensor.Value ?? -1;

        values[SensorItem.GpuVramUsed] = raw > 0 ? raw / MB_PER_GB : raw;
        values[SensorItem.GpuVramTotal] = vramTotalRaw > 0 ? vramTotalRaw / MB_PER_GB : -1;
        values[SensorItem.GpuVramUtilization] = (raw != -1 && vramTotalRaw > 0) ? (raw / vramTotalRaw) * 100f : -1;

        if (dgpu && values.TryGetValue(SensorItem.GpuPower, out var power))
            values[SensorItem.GpuPower] = power > MIN_ACTIVE_GPU_POWER ? power : -1;

        return values;
    }

    public void Reset()
    {
        _dgpuSensors.Clear(); _igpuSensors.Clear();
        _dgpuVramTotalRaw = -1; _igpuVramTotalRaw = -1;
        _dgpuHardware = null; _igpuHardware = null;
    }
}
