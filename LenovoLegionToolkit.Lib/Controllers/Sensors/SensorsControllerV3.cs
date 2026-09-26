using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using LenovoLegionToolkit.Lib.System.Management;
using LenovoLegionToolkit.Lib.Utils;

namespace LenovoLegionToolkit.Lib.Controllers.Sensors;

public class SensorsControllerV3(GPUController gpuController) : AbstractSensorsController(gpuController)
{
    private readonly record struct FanTableEndpoint(int SensorId, int FanId);

    private sealed record Profile(
        FanTableEndpoint Cpu,
        FanTableEndpoint Gpu,
        FanTableEndpoint[] Required,
        FanTableEndpoint[] PchFans,
        bool HasPchTemperature);

    private static readonly Profile[] Profiles =
    [
        new(new(4, 1), new(5, 2), [], [new(1, 4), new(5, 5)], true),
        new(new(1, 1), new(5, 2), [], [new(4, 4)], true),
        new(new(1, 1), new(5, 2), [new(4, 1)], [], true),
        new(new(4, 1), new(5, 2), [], [], false)
    ];

    private Profile? _profile;

    public override bool HasPchFan => _profile?.PchFans.Length > 0;

    public override async Task<bool> IsSupportedAsync()
    {
        try
        {
            var data = await WMI.LenovoFanTableData.ReadAsync().ConfigureAwait(false);
            var endpoints = data
                .Select(item => new FanTableEndpoint(item.sensorId, item.fanId))
                .ToHashSet();

            foreach (var profile in Profiles)
            {
                if (!GetRequiredEndpoints(profile).All(endpoints.Contains))
                    continue;

                _profile = profile;
                _ = await GetDataAsync().ConfigureAwait(false);
                return true;
            }

            _profile = null;
            return false;
        }
        catch (Exception ex)
        {
            _profile = null;
            Log.Instance.Trace($"Error checking support. [type={GetType().Name}]", ex);
            return false;
        }
    }

    protected override async Task<int> GetCpuCurrentTemperatureAsync()
    {
        var value = await WMI.LenovoOtherMethod.GetFeatureValueAsync(CapabilityID.CpuCurrentTemperature).ConfigureAwait(false);
        return value < 1 ? -1 : value;
    }

    protected override async Task<int> GetGpuCurrentTemperatureAsync()
    {
        var value = await WMI.LenovoOtherMethod.GetFeatureValueAsync(CapabilityID.GpuCurrentTemperature).ConfigureAwait(false);
        return value < 1 ? -1 : value;
    }

    protected override async Task<int> GetPchCurrentTemperatureAsync()
    {
        if (_profile?.HasPchTemperature != true)
            return -1;

        var value = await WMI.LenovoOtherMethod.GetFeatureValueAsync(CapabilityID.PchCurrentTemperature).ConfigureAwait(false);
        return value < 1 ? -1 : value;
    }

    protected override Task<int> GetCpuCurrentFanSpeedAsync() => WMI.LenovoOtherMethod.GetFeatureValueAsync(CapabilityID.CpuCurrentFanSpeed);

    protected override Task<int> GetGpuCurrentFanSpeedAsync() => WMI.LenovoOtherMethod.GetFeatureValueAsync(CapabilityID.GpuCurrentFanSpeed);

    protected override Task<int> GetPchCurrentFanSpeedAsync() => HasPchFan
        ? WMI.LenovoOtherMethod.GetFeatureValueAsync(CapabilityID.PchCurrentFanSpeed)
        : Task.FromResult(-1);

    protected override Task<int> GetCpuMaxFanSpeedAsync()
    {
        var endpoint = GetProfile().Cpu;
        return WMI.LenovoFanMethod.GetCurrentFanMaxSpeedAsync(endpoint.SensorId, endpoint.FanId);
    }

    protected override Task<int> GetGpuMaxFanSpeedAsync()
    {
        var endpoint = GetProfile().Gpu;
        return WMI.LenovoFanMethod.GetCurrentFanMaxSpeedAsync(endpoint.SensorId, endpoint.FanId);
    }

    protected override async Task<int> GetPchMaxFanSpeedAsync()
    {
        var endpoints = GetProfile().PchFans;
        if (endpoints.Length == 0)
            return -1;

        var speeds = await Task.WhenAll(endpoints.Select(endpoint =>
            WMI.LenovoFanMethod.GetCurrentFanMaxSpeedAsync(endpoint.SensorId, endpoint.FanId))).ConfigureAwait(false);
        return speeds.Max();
    }

    private Profile GetProfile() => _profile ?? throw new InvalidOperationException("No sensor profile selected");

    private static IEnumerable<FanTableEndpoint> GetRequiredEndpoints(Profile profile)
    {
        yield return profile.Cpu;
        yield return profile.Gpu;

        foreach (var endpoint in profile.PchFans)
            yield return endpoint;

        foreach (var endpoint in profile.Required)
            yield return endpoint;
    }
}
