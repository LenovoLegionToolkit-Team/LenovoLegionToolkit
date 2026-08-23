using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using LenovoLegionToolkit.Lib.Extensions;
using LenovoLegionToolkit.Lib.System;
using LenovoLegionToolkit.Lib.Utils;
using WindowsDisplayAPI;
using WindowsDisplayAPI.Native.DeviceContext;

namespace LenovoLegionToolkit.Lib.Features;

public class RefreshRateFeature : IFeature<RefreshRate>
{
    private const int MinimumDrrFrequency = 60;

    public Task<bool> IsSupportedAsync() => Task.FromResult(true);

    public async Task<RefreshRate[]> GetAllStatesAsync()
    {
        Log.Instance.Trace($"Getting all refresh rates...");

        var display = await InternalDisplay.GetAsync().ConfigureAwait(false);
        if (display is null)
        {
            Log.Instance.Trace($"Display not found");

            return [];
        }

        Log.Instance.Trace($"Display found: {display}");

        var currentSettings = display.DisplayScreen.CurrentSetting;

        Log.Instance.Trace($"Current display settings: {currentSettings.ToExtendedString()}");

        var result = display.DisplayScreen.GetPossibleSettings()
            .Where(dps => Match(dps, currentSettings))
            .Select(dps => dps.Frequency)
            .Distinct()
            .OrderBy(freq => freq)
            .Select(freq => new RefreshRate(freq))
            .ToList();

        if (OSExtensions.GetCurrent() == OS.Windows11 && result.Count > 0)
        {
            var maxFreq = result.Max(r => r.Frequency);
            if (maxFreq > MinimumDrrFrequency)
            {
                var displaySource = display.DisplayScreen.ToPathDisplaySource();
                var displayTarget = display.ToPathDisplayTarget();
                var pathInfos = WindowsDisplayAPI.DisplayConfig.PathInfo.GetActivePaths(virtualModeAware: true);
                var activePath = pathInfos.FirstOrDefault(p => p.DisplaySource == displaySource && (displayTarget is null || p.TargetsInfo.Any(t => t.DisplayTarget == displayTarget)));
                if (activePath is not null && activePath.TargetsInfo.Where(t => displayTarget is null || t.DisplayTarget == displayTarget).Any(t => t.IsVirtualModeSupportedByPath))
                {
                    var lowFreq = GetDynamicLowFrequency(maxFreq, result.Select(r => r.Frequency));
                    result.Add(new RefreshRate(maxFreq, isDynamic: true, baseFrequency: lowFreq));
                }
            }
        }

        Log.Instance.Trace($"Possible refresh rates are {string.Join(", ", result)}");

        return result.ToArray();
    }

    public async Task<RefreshRate> GetStateAsync()
    {
        Log.Instance.Trace($"Getting current refresh rate...");

        var display = await InternalDisplay.GetAsync().ConfigureAwait(false);
        if (display is null)
        {
            Log.Instance.Trace($"Display not found");

            return default(RefreshRate);
        }

        var displaySource = display.DisplayScreen.ToPathDisplaySource();
        var displayTarget = display.ToPathDisplayTarget();
        var pathInfos = WindowsDisplayAPI.DisplayConfig.PathInfo.GetActivePaths(virtualModeAware: true);
        var activePath = pathInfos.FirstOrDefault(p => p.DisplaySource == displaySource && (displayTarget is null || p.TargetsInfo.Any(t => t.DisplayTarget == displayTarget)));
        if (activePath is not null)
        {
            var target = activePath.TargetsInfo.FirstOrDefault(t => (displayTarget is null || t.DisplayTarget == displayTarget) && (t.IsCurrentlyInUse || t.IsPathActive));
            if (target is not null)
            {
                if (target.IsBoostRefreshRate)
                {
                    var currentSettings = display.DisplayScreen.CurrentSetting;
                    var possibleFreqs = display.DisplayScreen.GetPossibleSettings()
                        .Where(dps => Match(dps, currentSettings))
                        .Select(dps => dps.Frequency)
                        .ToArray();
                    var maxFreq = possibleFreqs.DefaultIfEmpty(MinimumDrrFrequency).Max();
                    var lowFreq = GetDynamicLowFrequency(maxFreq, possibleFreqs);
                    var drrResult = new RefreshRate(maxFreq, isDynamic: true, baseFrequency: lowFreq);
                    Log.Instance.Trace($"Dynamic Refresh Rate (DRR) is active. Reporting rate: {drrResult}");
                    return drrResult;
                }

                var freq = (int)(target.FrequencyInMillihertz / 1000);
                var result = new RefreshRate(freq);

                Log.Instance.Trace($"Current refresh rate is {result}");

                return result;
            }
        }

        var currentSettingsFallback = display.DisplayScreen.CurrentSetting;
        var fallbackResult = new RefreshRate(currentSettingsFallback.Frequency);

        Log.Instance.Trace($"Current refresh rate is {fallbackResult}");

        return fallbackResult;
    }

    public async Task SetStateAsync(RefreshRate state)
    {
        var display = await InternalDisplay.GetAsync().ConfigureAwait(false);
        if (display is null)
        {
            Log.Instance.Trace($"Display not found");
            throw new InvalidOperationException("Display not found");
        }

        var currentSettings = display.DisplayScreen.CurrentSetting;
        var currentState = await GetStateAsync().ConfigureAwait(false);

        Log.Instance.Trace($"Current display settings: {currentSettings.ToExtendedString()} (reported: {currentState})");

        if (currentState == state)
        {
            Log.Instance.Trace($"Frequency already set to {state}");
            return;
        }

        var possibleSettings = display.DisplayScreen.GetPossibleSettings();
        var targetFrequency = state.Frequency;
        var physicalFrequency = 0;

        if (state.IsDynamic)
        {
            var availableFrequencies = possibleSettings
                .Where(dps => Match(dps, currentSettings))
                .Select(dps => dps.Frequency)
                .Distinct();
            targetFrequency = GetDynamicLowFrequency(state.Frequency, availableFrequencies);
            physicalFrequency = state.Frequency;
        }

        var newSettings = possibleSettings
            .Where(dps => Match(dps, currentSettings))
            .Where(dps => dps.Frequency == targetFrequency)
            .Select(dps => new DisplaySetting(dps, currentSettings.Position, currentSettings.Orientation, DisplayFixedOutput.Default))
            .FirstOrDefault();

        if (newSettings is not null)
        {
            Log.Instance.Trace($"Setting display to {newSettings.ToExtendedString()}...");

            await display.SetSettingsUsingPathInfoAsync(newSettings, state.IsDynamic, physicalFrequency).ConfigureAwait(false);

            Log.Instance.Trace($"Display set to {newSettings.ToExtendedString()}");
        }
        else
        {
            Log.Instance.Trace($"Could not find matching settings for frequency {state}");
        }
    }

    private static int GetDynamicLowFrequency(int maxFrequency, IEnumerable<int> availableFrequencies)
    {
        if (availableFrequencies.Contains(maxFrequency / 2))
        {
            return maxFrequency / 2;
        }
        if (availableFrequencies.Contains(MinimumDrrFrequency))
        {
            return MinimumDrrFrequency;
        }
        return availableFrequencies.Min();
    }

    private static bool Match(DisplayPossibleSetting dps, DisplayPossibleSetting ds)
    {
        if (dps.IsTooSmall())
            return false;

        var result = true;
        result &= dps.Resolution == ds.Resolution;
        result &= dps.ColorDepth == ds.ColorDepth;
        result &= dps.IsInterlaced == ds.IsInterlaced;
        return result;
    }
}
