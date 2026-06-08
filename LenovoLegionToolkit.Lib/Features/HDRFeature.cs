using System;
using System.Threading.Tasks;
using LenovoLegionToolkit.Lib.Extensions;
using LenovoLegionToolkit.Lib.System;
using LenovoLegionToolkit.Lib.Utils;

namespace LenovoLegionToolkit.Lib.Features;

public class HDRFeature(RefreshRateFeature refreshRateFeature) : IFeature<HDRState>
{
    public async Task<bool> IsSupportedAsync()
    {
        try
        {
            if (AppFlags.Instance.Debug)
            {
                return true;
            }

            Log.Instance.Trace($"Checking HDR support...");

            var display = await InternalDisplay.GetAsync().ConfigureAwait(false);
            if (display is null)
            {
                Log.Instance.Trace($"Built in display not found");

                return false;
            }

            var isSupported = display.GetAdvancedColorInfo().AdvancedColorSupported;

            Log.Instance.Trace($"HDR support: {isSupported}");

            return isSupported;
        }
        catch (Exception ex)
        {
            Log.Instance.Trace($"Failed to check HDR support", ex);

            return false;
        }
    }

    public async Task<bool> IsHdrBlockedAsync()
    {
        var display = await InternalDisplay.GetAsync().ConfigureAwait(false);

        if (display is null)
            throw new InvalidOperationException("Built in display not found");

        var result = display.GetAdvancedColorInfo().AdvancedColorForceDisabled;
        return result;
    }

    public Task<HDRState[]> GetAllStatesAsync() => Task.FromResult(Enum.GetValues<HDRState>());

    public async Task<HDRState> GetStateAsync()
    {
        Log.Instance.Trace($"Getting current HDR state...");

        var display = await InternalDisplay.GetAsync().ConfigureAwait(false);

        if (display is null)
            throw new InvalidOperationException("Built in display not found");

        var result = display.GetAdvancedColorInfo().AdvancedColorEnabled ? HDRState.On : HDRState.Off;

        Log.Instance.Trace($"HDR is {result}");

        return result;
    }

    public async Task SetStateAsync(HDRState state)
    {
        var currentState = await GetStateAsync().ConfigureAwait(false);

        if (currentState == state)
        {
            Log.Instance.Trace($"HDR already set to {state}");
            return;
        }

        var display = await InternalDisplay.GetAsync().ConfigureAwait(false);

        if (display is null)
            throw new InvalidOperationException("Built in display not found");

        var currentRefreshRate = await refreshRateFeature.GetStateAsync().ConfigureAwait(false);

        Log.Instance.Trace($"Setting display HDR to {state}");

        display.SetAdvancedColorState(state == HDRState.On);

        await refreshRateFeature.SetStateAsync(currentRefreshRate).ConfigureAwait(false);
    }
}