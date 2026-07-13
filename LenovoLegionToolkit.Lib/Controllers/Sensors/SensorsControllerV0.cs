using System;
using System.Threading.Tasks;
using LenovoLegionToolkit.Lib.System.Management;
using LenovoLegionToolkit.Lib.Utils;

namespace LenovoLegionToolkit.Lib.Controllers.Sensors;

public class SensorsControllerV0(GPUController gpuController) : AbstractSensorsController(gpuController)
{
    private const int CPU_FAN_ID = 1;
    private const int GPU_FAN_ID = 2;

    public override async Task<bool> IsSupportedAsync()
    {
        try
        {
            var anyFanExists = await WMI.LenovoFanTestData.ExistsAsync(CPU_FAN_ID).ConfigureAwait(false) || await WMI.LenovoFanTestData.ExistsAsync(GPU_FAN_ID).ConfigureAwait(false);
            if (!anyFanExists)
            {
                return false;
            }

            return true;
        }
        catch (Exception)
        {
            Log.Instance.Trace($"Error checking support. [type={GetType().Name}]");
            return false;
        }
    }

    protected override async Task<int> GetCpuCurrentTemperatureAsync()
    {
        return -1;
    }

    protected override async Task<int> GetGpuCurrentTemperatureAsync()
    {
        return -1;
    }

    protected override Task<int> GetCpuCurrentFanSpeedAsync() => WMI.LenovoOtherMethod.GetFeatureValueAsync(CapabilityID.CpuCurrentFanSpeed);

    protected override Task<int> GetGpuCurrentFanSpeedAsync() => WMI.LenovoOtherMethod.GetFeatureValueAsync(CapabilityID.GpuCurrentFanSpeed);

    protected override Task<int> GetCpuMaxFanSpeedAsync() => WMI.LenovoFanTestData.GetFanMaxSpeedAsync(CPU_FAN_ID);

    protected override Task<int> GetGpuMaxFanSpeedAsync() => WMI.LenovoFanTestData.GetFanMaxSpeedAsync(GPU_FAN_ID);

    public Task<int> GetCpuMinFanSpeedAsync() => WMI.LenovoFanTestData.GetFanMinSpeedAsync(CPU_FAN_ID);

    public Task<int> GetGpuMinFanSpeedAsync() => WMI.LenovoFanTestData.GetFanMinSpeedAsync(GPU_FAN_ID);
}
