using System;
using System.Linq;
using System.Threading.Tasks;
using LenovoLegionToolkit.Lib.System;
using LenovoLegionToolkit.Lib.Utils;
using WindowsDisplayAPI.Native.DisplayConfig;

namespace LenovoLegionToolkit.Lib.Features;

public class DpiScaleFeature : IFeature<DpiScale>
{
    public Task<bool> IsSupportedAsync() => Task.FromResult(true);

    public async Task<DpiScale[]> GetAllStatesAsync()
    {
        Log.Instance.Trace($"Getting all DPI scales...");

        var display = await InternalDisplay.GetAsync().ConfigureAwait(false);
        var pds = display?.DisplayScreen?.ToPathDisplaySource();
        if (pds is null)
        {
            Log.Instance.Trace($"Built in display not found");

            return [];
        }

        var max = (int)pds.MaximumDPIScale;

        var result = Enum.GetValues<DisplayConfigSourceDPIScale>()
            .Select(s => (int)s)
            .Where(s => s <= max)
            .OrderBy(s => s)
            .Select(s => new DpiScale(s))
            .ToArray();

        var currentDpiScale = (int)pds.CurrentDPIScale;

        Log.Instance.Trace($"Current DPI scale is {currentDpiScale}");

        return result;
    }

    public async Task<DpiScale> GetStateAsync()
    {
        Log.Instance.Trace($"Getting current DPI scale...");

        var display = await InternalDisplay.GetAsync().ConfigureAwait(false);
        var pds = display?.DisplayScreen?.ToPathDisplaySource();
        if (pds is null)
        {
            Log.Instance.Trace($"Built in display not found");

            return default(DpiScale);
        }

        var result = (int)pds.CurrentDPIScale;

        Log.Instance.Trace($"Current DPI scale is {result}");

        return new DpiScale(result);
    }

    public async Task SetStateAsync(DpiScale state)
    {
        var display = await InternalDisplay.GetAsync().ConfigureAwait(false);
        var pds = display?.DisplayScreen?.ToPathDisplaySource();
        if (pds is null)
        {
            Log.Instance.Trace($"Built in display not found");
            throw new InvalidOperationException("Built in display not found");
        }

        if ((int)pds.CurrentDPIScale == state.Scale)
        {
            Log.Instance.Trace($"DPI scale already set to {state.Scale}");
        }

        if (!Enum.IsDefined(typeof(DisplayConfigSourceDPIScale), (uint)state.Scale))
        {
            Log.Instance.Trace($"DPI scale {state.Scale} not found");
            return;
        }

        Log.Instance.Trace($"Setting DPI scale to {state.Scale}");

        pds.CurrentDPIScale = (DisplayConfigSourceDPIScale)state.Scale;
    }
}