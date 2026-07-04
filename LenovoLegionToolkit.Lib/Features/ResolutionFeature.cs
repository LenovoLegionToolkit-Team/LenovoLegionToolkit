using System;
using System.Linq;
using System.Threading.Tasks;
using LenovoLegionToolkit.Lib.Extensions;
using LenovoLegionToolkit.Lib.System;
using LenovoLegionToolkit.Lib.Utils;
using WindowsDisplayAPI;
using WindowsDisplayAPI.Native.DeviceContext;

namespace LenovoLegionToolkit.Lib.Features;

public class ResolutionFeature : IFeature<Resolution>
{
    public Task<bool> IsSupportedAsync() => Task.FromResult(true);

    public async Task<Resolution[]> GetAllStatesAsync()
    {
        Log.Instance.Trace($"Getting all resolutions...");

        var display = await InternalDisplay.GetAsync().ConfigureAwait(false);
        if (display is null)
        {
            Log.Instance.Trace($"Built in display not found");

            return [];
        }

        Log.Instance.Trace($"Built in display found: {display}");

        var currentSettings = display.DisplayScreen.CurrentSetting;

        Log.Instance.Trace($"Current built in display settings: {currentSettings.ToExtendedString()}");

        var result = display.DisplayScreen.GetPossibleSettings()
            .Where(dps => Match(dps, currentSettings))
            .Select(dps => dps.Resolution)
            .Select(res => new Resolution(res))
            .Distinct()
            .OrderByDescending(res => res)
            .ToArray();

        Log.Instance.Trace($"Possible resolutions are {string.Join(", ", result)}");

        return result;
    }

    public async Task<Resolution> GetStateAsync()
    {
        Log.Instance.Trace($"Getting current resolution...");

        var display = await InternalDisplay.GetAsync().ConfigureAwait(false);
        if (display is null)
        {
            Log.Instance.Trace($"Built in display not found");

            return default(Resolution);
        }

        var currentSettings = display.DisplayScreen.CurrentSetting;
        var result = new Resolution(currentSettings.Resolution);

        Log.Instance.Trace($"Current resolution is {result} [currentSettings={currentSettings.ToExtendedString()}]");

        return result;
    }

    public async Task SetStateAsync(Resolution state)
    {
        var display = await InternalDisplay.GetAsync().ConfigureAwait(false);
        if (display is null)
        {
            Log.Instance.Trace($"Built in display not found");
            throw new InvalidOperationException("Built in display not found");
        }

        var currentSettings = display.DisplayScreen.CurrentSetting;

        if (currentSettings.Resolution == state)
        {
            Log.Instance.Trace($"Resolution already set to {state}");
            return;
        }

        var possibleSettings = display.DisplayScreen.GetPossibleSettings();

        Log.Instance.Trace($"Current built in display settings: {currentSettings.ToExtendedString()}");

        var newSettings = possibleSettings
            .Where(dps => Match(dps, currentSettings))
            .Where(dps => dps.Resolution == state)
            .Select(dps => new DisplaySetting(dps, currentSettings.Position, currentSettings.Orientation, DisplayFixedOutput.Default))
            .FirstOrDefault();

        if (newSettings is not null)
        {
            Log.Instance.Trace($"Setting display to {newSettings.ToExtendedString()}");

            display.SetSettingsUsingPathInfo(newSettings);

            Log.Instance.Trace($"Display set to {newSettings.ToExtendedString()}");
        }
        else
        {
            Log.Instance.Trace($"Could not find matching settings for resolution {state}");
        }
    }

    private static bool Match(DisplayPossibleSetting dps, DisplayPossibleSetting ds)
    {
        if (dps.IsTooSmall())
            return false;

        var result = true;
        result &= dps.Frequency == ds.Frequency;
        result &= dps.ColorDepth == ds.ColorDepth;
        result &= dps.IsInterlaced == ds.IsInterlaced;
        return result;
    }
}