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
        new(new(4, 1), new(5, 2), [], [new(1, 4), new(5, 5)], ValueSource.Capability, ValueSource.Capability, MaxFanSpeedSource.CurrentTable, 0, 0, true),
        new(new(1, 1), new(5, 2), [], [new(4, 4)], ValueSource.Capability, ValueSource.Capability, MaxFanSpeedSource.CurrentTable, 0, 0, true),
        new(new(1, 1), new(5, 2), [new(4, 1)], [], ValueSource.Capability, ValueSource.Capability, MaxFanSpeedSource.CurrentTable, 0, 0, true),
        new(new(4, 1), new(5, 2), [], [], ValueSource.Capability, ValueSource.Capability, MaxFanSpeedSource.CurrentTable, 0, 0, false),
        new(new(3, 0), new(4, 1), [], [], ValueSource.FanMethod, ValueSource.FanMethod, MaxFanSpeedSource.CurrentTable, 3, 4, false),
        new(new(0, 0), new(0, 1), [], [], ValueSource.FanMethod, ValueSource.FanMethod, MaxFanSpeedSource.DefaultTable, 3, 4, false)
    ];

    private static readonly Profile FanTestDataProfile = new(
        new(0, 1),
        new(0, 2),
        [],
        [],
        ValueSource.None,
        ValueSource.Capability,
        MaxFanSpeedSource.FanTestData,
        0,
        0,
        false);

    private Profile? _profile;

    public override bool HasPchFan => _profile?.PchFans.Length > 0;

    public override async Task<bool> IsSupportedAsync()
    {
        if (_profile is not null)
            return true;

        var profile = await FindFanTableProfileAsync().ConfigureAwait(false);
        if (profile is null && await HasFanTestDataAsync().ConfigureAwait(false))
            profile = FanTestDataProfile;

        if (profile is null)
            return false;

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
