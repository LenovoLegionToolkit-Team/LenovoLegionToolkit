using System;
using System.Threading.Tasks;
using LenovoLegionToolkit.Lib.System.Management;

namespace LenovoLegionToolkit.Lib.Utils;

public static class SpecialKeyLedHelper
{
    private static MachineInformation? _cachedMachineInformation;

    public static async Task SetLedAsync(SpecialKeyLedState state)
    {
        _cachedMachineInformation ??= await Compatibility.GetMachineInformationAsync().ConfigureAwait(false);

        try
        {
            if (_cachedMachineInformation.Value.LegionSeries == LegionSeries.ThinkBook ||
                _cachedMachineInformation.Value.LegionSeries == LegionSeries.IdeaPad ||
                _cachedMachineInformation.Value.LegionSeries == LegionSeries.Motorola ||
                _cachedMachineInformation.Value.LegionSeries == LegionSeries.YOGA)
            {
                await WMI.LenovoUtilityData.SetFeatureAsync(state).ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            Log.Instance.Trace($"LED sync failed [state={state}]", ex);
        }
    }
}
