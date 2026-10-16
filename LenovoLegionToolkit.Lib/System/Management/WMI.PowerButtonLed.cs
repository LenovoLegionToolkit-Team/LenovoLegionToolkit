using System.Threading.Tasks;

namespace LenovoLegionToolkit.Lib.System.Management;

public static partial class WMI
{
    public static class PowerButtonLed
    {
        private const int LightingId = 4;

        public static Task<int?> GetCapabilityAsync() =>
            LenovoCapabilityData00.GetCapabilityAsync((int)CapabilityID.PowerLedBrightness);

        public static Task<(int stateType, int level)> GetStatusAsync() =>
            LenovoLightingMethod.GetLightingCurrentStatusAsync(LightingId);

        public static Task SetStatusAsync(int stateType, int level) =>
            LenovoLightingMethod.SetLightingCurrentStatusAsync(LightingId, stateType, level);
    }
}
