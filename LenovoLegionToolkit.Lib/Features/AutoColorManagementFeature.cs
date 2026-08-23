using System;
using System.Threading.Tasks;
using LenovoLegionToolkit.Lib.Extensions;
using LenovoLegionToolkit.Lib.System;
using LenovoLegionToolkit.Lib.Utils;

namespace LenovoLegionToolkit.Lib.Features;

public class AutoColorManagementFeature(RefreshRateFeature refreshRateFeature) : IFeature<AutoColorManagementState>
{
    public async Task<bool> IsSupportedAsync()
    {
        try
        {
            if (AppFlags.Instance.Debug)
            {
                return true;
            }

            Log.Instance.Trace($"Checking Auto Color Management (ACM) support...");

            var display = await InternalDisplay.GetAsync().ConfigureAwait(false);
            if (display is null)
            {
                Log.Instance.Trace($"Display not found");

                return false;
            }

            var isSupported = display.GetAdvancedColorInfo().AutoColorManagementSupported;

            Log.Instance.Trace($"Auto Color Management (ACM) support: {isSupported}");

            return isSupported;
        }
        catch (Exception ex)
        {
            Log.Instance.Trace($"Failed to check Auto Color Management support", ex);

            return false;
        }
    }

    public Task<AutoColorManagementState[]> GetAllStatesAsync() => Task.FromResult(Enum.GetValues<AutoColorManagementState>());

    public async Task<AutoColorManagementState> GetStateAsync()
    {
        Log.Instance.Trace($"Getting current Auto Color Management state...");

        var display = await InternalDisplay.GetAsync().ConfigureAwait(false);

        if (display is null)
            throw new InvalidOperationException("Display not found");

        var result = display.GetAdvancedColorInfo().AutoColorManagementEnabled ? AutoColorManagementState.On : AutoColorManagementState.Off;

        Log.Instance.Trace($"Auto Color Management is {result}");

        return result;
    }

    public async Task SetStateAsync(AutoColorManagementState state)
    {
        var currentState = await GetStateAsync().ConfigureAwait(false);

        if (currentState == state)
        {
            Log.Instance.Trace($"Auto Color Management already set to {state}");
            return;
        }

        var display = await InternalDisplay.GetAsync().ConfigureAwait(false);

        if (display is null)
            throw new InvalidOperationException("Display not found");

        var currentRefreshRate = await refreshRateFeature.GetStateAsync().ConfigureAwait(false);

        Log.Instance.Trace($"Setting display Auto Color Management to {state}");

        display.SetWcgState(state == AutoColorManagementState.On);

        await refreshRateFeature.SetStateAsync(currentRefreshRate).ConfigureAwait(false);
    }
}
