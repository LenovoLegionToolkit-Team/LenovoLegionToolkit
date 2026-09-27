using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using LenovoLegionToolkit.Lib.System.Management;
using LenovoLegionToolkit.Lib.Utils;

namespace LenovoLegionToolkit.Lib.Controllers.Sensors;

public class SensorsController(GPUController gpuController) : AbstractSensorsController(gpuController)
{
    private enum ValueSource
    {
        None,
        Capability,
        FanMethod
    }

    private enum MaxFanSpeedSource
    {
        CurrentTable,
        DefaultTable,
        FanTestData
    }

    private readonly record struct FanTableEndpoint(int SensorId, int FanId);

    private sealed record Profile(
        FanTableEndpoint Cpu,
        FanTableEndpoint Gpu,
        FanTableEndpoint[] Required,
        FanTableEndpoint[] PchFans,
        ValueSource TemperatureSource,
        ValueSource FanSpeedSource,
        MaxFanSpeedSource MaxFanSpeedSource,
        int CpuTemperatureSensorId,
        int GpuTemperatureSensorId,
        bool HasPchTemperature);

    private static readonly Profile[] FanTableProfiles =
    [
        new(
            Cpu: new(4, 1),
            Gpu: new(5, 2),
            Required: [],
            PchFans: [new(1, 4), new(5, 5)],
            TemperatureSource: ValueSource.Capability,
            FanSpeedSource: ValueSource.Capability,
            MaxFanSpeedSource: MaxFanSpeedSource.CurrentTable,
            CpuTemperatureSensorId: 0,
            GpuTemperatureSensorId: 0,
            HasPchTemperature: true),
        new(
            Cpu: new(1, 1),
            Gpu: new(5, 2),
            Required: [],
            PchFans: [new(4, 4)],
            TemperatureSource: ValueSource.Capability,
            FanSpeedSource: ValueSource.Capability,
            MaxFanSpeedSource: MaxFanSpeedSource.CurrentTable,
            CpuTemperatureSensorId: 0,
            GpuTemperatureSensorId: 0,
            HasPchTemperature: true),
        new(
            Cpu: new(1, 1),
            Gpu: new(5, 2),
            Required: [new(4, 1)],
            PchFans: [],
            TemperatureSource: ValueSource.Capability,
            FanSpeedSource: ValueSource.Capability,
            MaxFanSpeedSource: MaxFanSpeedSource.CurrentTable,
            CpuTemperatureSensorId: 0,
            GpuTemperatureSensorId: 0,
            HasPchTemperature: true),
        new(
            Cpu: new(4, 1),
            Gpu: new(5, 2),
            Required: [],
            PchFans: [],
            TemperatureSource: ValueSource.Capability,
            FanSpeedSource: ValueSource.Capability,
            MaxFanSpeedSource: MaxFanSpeedSource.CurrentTable,
            CpuTemperatureSensorId: 0,
            GpuTemperatureSensorId: 0,
            HasPchTemperature: false),
        new(
            Cpu: new(3, 0),
            Gpu: new(4, 1),
            Required: [],
            PchFans: [],
            TemperatureSource: ValueSource.FanMethod,
            FanSpeedSource: ValueSource.FanMethod,
            MaxFanSpeedSource: MaxFanSpeedSource.CurrentTable,
            CpuTemperatureSensorId: 3,
            GpuTemperatureSensorId: 4,
            HasPchTemperature: false),
        new(
            Cpu: new(0, 0),
            Gpu: new(0, 1),
            Required: [],
            PchFans: [],
            TemperatureSource: ValueSource.FanMethod,
            FanSpeedSource: ValueSource.FanMethod,
            MaxFanSpeedSource: MaxFanSpeedSource.DefaultTable,
            CpuTemperatureSensorId: 3,
            GpuTemperatureSensorId: 4,
            HasPchTemperature: false)
    ];

    private static readonly Profile FanTestDataProfile = new(
        Cpu: new(0, 1),
        Gpu: new(0, 2),
        Required: [],
        PchFans: [],
        TemperatureSource: ValueSource.None,
        FanSpeedSource: ValueSource.Capability,
        MaxFanSpeedSource: MaxFanSpeedSource.FanTestData,
        CpuTemperatureSensorId: 0,
        GpuTemperatureSensorId: 0,
        HasPchTemperature: false);

    private Profile? _profile;

    public override bool HasPchFan => _profile?.PchFans.Length > 0;

    public override async Task<bool> IsSupportedAsync()
    {
        if (_profile is not null)
        {
            return true;
        }

        var profile = await FindFanTableProfileAsync().ConfigureAwait(false);
        if (profile is null && await HasFanTestDataAsync().ConfigureAwait(false))
        {
            profile = FanTestDataProfile;
        }

        if (profile is null)
        {
            return false;
        }

        try
        {
            _profile = profile;
            _ = await GetDataAsync().ConfigureAwait(false);
            return true;
        }
        catch (Exception ex)
        {
            _profile = null;
            Log.Instance.Trace($"Error checking support. [type={GetType().Name}]", ex);
            return false;
        }
    }

    public async Task<ISensorsController?> GetControllerAsync() =>
        await IsSupportedAsync().ConfigureAwait(false) ? this : null;

    public override async Task<SensorsData> GetDataAsync()
    {
        await IsSupportedAsync().ConfigureAwait(false);
        return await base.GetDataAsync().ConfigureAwait(false);
    }

    public override async Task<FanSpeedTable> GetFanSpeedsAsync()
    {
        await IsSupportedAsync().ConfigureAwait(false);
        return await base.GetFanSpeedsAsync().ConfigureAwait(false);
    }

    protected override Task<int> GetCpuCurrentTemperatureAsync() =>
        GetTemperatureAsync(GetProfile().TemperatureSource, CapabilityID.CpuCurrentTemperature, GetProfile().CpuTemperatureSensorId);

    protected override Task<int> GetGpuCurrentTemperatureAsync() =>
        GetTemperatureAsync(GetProfile().TemperatureSource, CapabilityID.GpuCurrentTemperature, GetProfile().GpuTemperatureSensorId);

    protected override Task<int> GetPchCurrentTemperatureAsync() => GetProfile().HasPchTemperature
        ? GetCapabilityValueAsync(CapabilityID.PchCurrentTemperature)
        : Task.FromResult(-1);

    protected override Task<int> GetCpuCurrentFanSpeedAsync() =>
        GetFanSpeedAsync(GetProfile().FanSpeedSource, CapabilityID.CpuCurrentFanSpeed, GetProfile().Cpu.FanId);

    protected override Task<int> GetGpuCurrentFanSpeedAsync() =>
        GetFanSpeedAsync(GetProfile().FanSpeedSource, CapabilityID.GpuCurrentFanSpeed, GetProfile().Gpu.FanId);

    protected override Task<int> GetPchCurrentFanSpeedAsync() => HasPchFan
        ? GetCapabilityValueAsync(CapabilityID.PchCurrentFanSpeed)
        : Task.FromResult(-1);

    protected override Task<int> GetCpuMaxFanSpeedAsync() => GetMaxFanSpeedAsync(GetProfile(), GetProfile().Cpu);

    protected override Task<int> GetGpuMaxFanSpeedAsync() => GetMaxFanSpeedAsync(GetProfile(), GetProfile().Gpu);

    protected override async Task<int> GetPchMaxFanSpeedAsync()
    {
        var profile = GetProfile();
        if (profile.PchFans.Length == 0)
            return -1;

        var speeds = await Task.WhenAll(profile.PchFans.Select(endpoint =>
            GetMaxFanSpeedAsync(profile, endpoint))).ConfigureAwait(false);
        return speeds.Max();
    }

    private static async Task<Profile?> FindFanTableProfileAsync()
    {
        try
        {
            var data = await WMI.LenovoFanTableData.ReadAsync().ConfigureAwait(false);
            var endpoints = data
                .Select(item => new FanTableEndpoint(item.sensorId, item.fanId))
                .ToHashSet();

            return FanTableProfiles.FirstOrDefault(profile => GetRequiredEndpoints(profile).All(endpoints.Contains));
        }
        catch
        {
            return null;
        }
    }

    private static async Task<bool> HasFanTestDataAsync()
    {
        try
        {
            return await WMI.LenovoFanTestData.ExistsAsync(FanTestDataProfile.Cpu.FanId).ConfigureAwait(false) ||
                   await WMI.LenovoFanTestData.ExistsAsync(FanTestDataProfile.Gpu.FanId).ConfigureAwait(false);
        }
        catch
        {
            return false;
        }
    }

    private static async Task<int> GetTemperatureAsync(ValueSource source, CapabilityID capabilityId, int sensorId)
    {
        var value = source switch
        {
            ValueSource.None => -1,
            ValueSource.Capability => await WMI.LenovoOtherMethod.GetFeatureValueAsync(capabilityId).ConfigureAwait(false),
            ValueSource.FanMethod => await WMI.LenovoFanMethod.FanGetCurrentSensorTemperatureAsync(sensorId).ConfigureAwait(false),
            _ => -1
        };

        return value < 1 ? -1 : value;
    }

    private static Task<int> GetFanSpeedAsync(ValueSource source, CapabilityID capabilityId, int fanId) => source switch
    {
        ValueSource.Capability => WMI.LenovoOtherMethod.GetFeatureValueAsync(capabilityId),
        ValueSource.FanMethod => WMI.LenovoFanMethod.FanGetCurrentFanSpeedAsync(fanId),
        _ => Task.FromResult(-1)
    };

    private static Task<int> GetCapabilityValueAsync(CapabilityID capabilityId) =>
        WMI.LenovoOtherMethod.GetFeatureValueAsync(capabilityId);

    private static Task<int> GetMaxFanSpeedAsync(Profile profile, FanTableEndpoint endpoint) => profile.MaxFanSpeedSource switch
    {
        MaxFanSpeedSource.CurrentTable => WMI.LenovoFanMethod.GetCurrentFanMaxSpeedAsync(endpoint.SensorId, endpoint.FanId),
        MaxFanSpeedSource.DefaultTable => WMI.LenovoFanMethod.GetDefaultFanMaxSpeedAsync(endpoint.SensorId, endpoint.FanId),
        MaxFanSpeedSource.FanTestData => WMI.LenovoFanTestData.GetFanMaxSpeedAsync(endpoint.FanId),
        _ => Task.FromResult(-1)
    };

    private Profile GetProfile() => _profile ?? throw new InvalidOperationException("No sensor profile selected");

    private static IEnumerable<FanTableEndpoint> GetRequiredEndpoints(Profile profile)
    {
        yield return profile.Cpu;
        yield return profile.Gpu;

        foreach (var endpoint in profile.PchFans)
        {
            yield return endpoint;
        }

        foreach (var endpoint in profile.Required)
        {
            yield return endpoint;
        }
    }
}
