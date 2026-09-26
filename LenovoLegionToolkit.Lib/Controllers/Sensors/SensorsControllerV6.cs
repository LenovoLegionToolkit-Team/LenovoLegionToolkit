using System;
using System.Threading.Tasks;
using LenovoLegionToolkit.Lib.System.Management;
using LenovoLegionToolkit.Lib.Utils;

namespace LenovoLegionToolkit.Lib.Controllers.Sensors;

public class SensorsControllerV6(GPUController gpuController) : AbstractSensorsController(gpuController)
{
    private const int CPU_SENSOR_ID = 4;
    private const int GPU_SENSOR_ID = 5;
    private const int PCH_SENSOR_ID_1 = 1;
    private const int PCH_SENSOR_ID_2 = 5;
    private const int CPU_FAN_ID = 1;
    private const int GPU_FAN_ID = 2;
    private const int PCH_FAN_ID_1 = 4;
    private const int PCH_FAN_ID_2 = 5;

    public override async Task<bool> IsSupportedAsync()
    {
        try
        {
            var result = await WMI.LenovoFanTableData.ExistsAsync(CPU_SENSOR_ID, CPU_FAN_ID).ConfigureAwait(false);
            result &= await WMI.LenovoFanTableData.ExistsAsync(GPU_SENSOR_ID, GPU_FAN_ID).ConfigureAwait(false);
            result &= await WMI.LenovoFanTableData.ExistsAsync(PCH_SENSOR_ID_1, PCH_FAN_ID_1).ConfigureAwait(false);
            result &= await WMI.LenovoFanTableData.ExistsAsync(PCH_SENSOR_ID_2, PCH_FAN_ID_2).ConfigureAwait(false);

            if (result)
                _ = await GetDataAsync().ConfigureAwait(false);

            return result;
        }
        catch (Exception ex)
        {
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
        var value = await WMI.LenovoOtherMethod.GetFeatureValueAsync(CapabilityID.PchCurrentTemperature).ConfigureAwait(false);
        return value < 1 ? -1 : value;
    }

    protected override Task<int> GetCpuCurrentFanSpeedAsync() => WMI.LenovoOtherMethod.GetFeatureValueAsync(CapabilityID.CpuCurrentFanSpeed);

    protected override Task<int> GetGpuCurrentFanSpeedAsync() => WMI.LenovoOtherMethod.GetFeatureValueAsync(CapabilityID.GpuCurrentFanSpeed);

    protected override Task<int> GetPchCurrentFanSpeedAsync() => WMI.LenovoOtherMethod.GetFeatureValueAsync(CapabilityID.PchCurrentFanSpeed);

    protected override Task<int> GetCpuMaxFanSpeedAsync() => WMI.LenovoFanMethod.GetCurrentFanMaxSpeedAsync(CPU_SENSOR_ID, CPU_FAN_ID);

    protected override Task<int> GetGpuMaxFanSpeedAsync() => WMI.LenovoFanMethod.GetCurrentFanMaxSpeedAsync(GPU_SENSOR_ID, GPU_FAN_ID);

    protected override async Task<int> GetPchMaxFanSpeedAsync()
    {
        var first = await WMI.LenovoFanMethod.GetCurrentFanMaxSpeedAsync(PCH_SENSOR_ID_1, PCH_FAN_ID_1).ConfigureAwait(false);
        var second = await WMI.LenovoFanMethod.GetCurrentFanMaxSpeedAsync(PCH_SENSOR_ID_2, PCH_FAN_ID_2).ConfigureAwait(false);
        return Math.Max(first, second);
    }
}
