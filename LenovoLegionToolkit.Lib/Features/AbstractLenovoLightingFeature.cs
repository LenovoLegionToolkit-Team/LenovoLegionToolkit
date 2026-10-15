using System;
using System.Threading.Tasks;
using LenovoLegionToolkit.Lib.System.Management;
using LenovoLegionToolkit.Lib.Utils;

namespace LenovoLegionToolkit.Lib.Features;

public abstract class AbstractLenovoLightingFeature<T>(int lightingID, int controlInterface, int type) : IFeature<T> where T : struct, Enum, IComparable
{
    public bool ForceDisable { get; set; }

    public virtual async Task<bool> IsSupportedAsync()
    {
        if (AppFlags.Instance.Debug)
        {
            return true;
        }

        if (ForceDisable)
            return false;

        try
        {
            var mi = await Compatibility.GetMachineInformationAsync().ConfigureAwait(false);
            if (mi.Properties.IsExcludedFromLenovoLighting)
                return false;

            var isSupported = await WMI.LenovoLightingData.ExistsAsync(lightingID, controlInterface, type).ConfigureAwait(false);

            if (!isSupported)
            {
                Log.Instance.Trace($"Control interface not found [feature={GetType().Name}]");
                return false;
            }

            _ = await GetStateAsync().ConfigureAwait(false);

            Log.Instance.Trace($"Supported [feature={GetType().Name}]");

            return true;
        }
        catch (Exception ex)
        {
            Log.Instance.Trace($"Failed to check support [feature={GetType().Name}]", ex);

            return false;
        }
    }

    public virtual Task<T[]> GetAllStatesAsync() => Task.FromResult(Enum.GetValues<T>());

    public async Task<T> GetStateAsync()
    {
        Log.Instance.Trace($"Getting state... [feature={GetType().Name}]");

        var (stateType, level) = await WMI.LenovoLightingMethod.GetLightingCurrentStatusAsync(lightingID).ConfigureAwait(false);
        var result = FromInternal(stateType, level);

        Log.Instance.Trace($"State is {result} [feature={GetType().Name}]");

        return result;
    }

    public async Task SetStateAsync(T state)
    {
        Log.Instance.Trace($"Setting state to {state}... [feature={GetType().Name}]");

        var (stateType, level) = ToInternal(state);

        await WMI.LenovoLightingMethod.SetLightingCurrentStatusAsync(lightingID, stateType, level).ConfigureAwait(false);

        Log.Instance.Trace($"Set state to {state} [feature={GetType().Name}]");
    }

    protected abstract T FromInternal(int stateType, int level);

    protected abstract (int stateType, int level) ToInternal(T state);
}
