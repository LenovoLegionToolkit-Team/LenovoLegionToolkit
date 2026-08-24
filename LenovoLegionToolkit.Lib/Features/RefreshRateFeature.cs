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
                var targetInfo = activePath?.TargetsInfo.FirstOrDefault(t => displayTarget is null || t.DisplayTarget == displayTarget);

                if (targetInfo is not null && targetInfo.IsVirtualModeSupportedByPath && (targetInfo.IsBoostRefreshRate || targetInfo.IsDynamicRefreshRateSupported))
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

            return new RefreshRate(0);
        }

        var currentSettings = display.DisplayScreen.CurrentSetting;
        var reportedFrequency = currentSettings.Frequency;
        var displaySource = display.DisplayScreen.ToPathDisplaySource();
        var displayTarget = display.ToPathDisplayTarget();

        var pathInfos = WindowsDisplayAPI.DisplayConfig.PathInfo.GetActivePaths(virtualModeAware: true);
        var activePath = pathInfos.FirstOrDefault(p => p.DisplaySource == displaySource && (displayTarget is null || p.TargetsInfo.Any(t => t.DisplayTarget == displayTarget)));

        var target = activePath?.TargetsInfo.FirstOrDefault(t => displayTarget is null || t.DisplayTarget == displayTarget);
        if (target is not null && target.IsBoostRefreshRate)
        {
            var allStates = await GetAllStatesAsync().ConfigureAwait(false);
            var dynamicState = allStates.FirstOrDefault(r => r.IsDynamic && r.Frequency == reportedFrequency);
            if (dynamicState.IsDynamic)
            {
                Log.Instance.Trace($"Current refresh rate is {dynamicState}");
                return dynamicState;
            }

            var defaultLowFreq = GetDynamicLowFrequency(reportedFrequency, allStates.Where(r => !r.IsDynamic).Select(r => r.Frequency));
            var inferredDynamicState = new RefreshRate(reportedFrequency, isDynamic: true, baseFrequency: defaultLowFreq);
            Log.Instance.Trace($"Current refresh rate is {inferredDynamicState}");
            return inferredDynamicState;
        }

        Log.Instance.Trace($"Current refresh rate is {reportedFrequency}Hz");

        return new RefreshRate(reportedFrequency);
    }

    public async Task SetStateAsync(RefreshRate state)
    {
        var display = await InternalDisplay.GetAsync().ConfigureAwait(false);
        if (display is null)
        {
            Log.Instance.Trace($"Display not found");

            return;
        }

        var currentSettings = display.DisplayScreen.CurrentSetting;
        var physicalFrequency = state.IsDynamic ? (int?)state.Frequency : null;
        var targetFrequency = state.IsDynamic ? state.BaseFrequency : state.Frequency;

        Log.Instance.Trace($"Current display settings: {currentSettings.ToExtendedString()} (reported: {state})");

        var matchingSetting = display.DisplayScreen.GetPossibleSettings()
            .Where(dps => Match(dps, currentSettings))
            .FirstOrDefault(dps => dps.Frequency == targetFrequency);

        if (matchingSetting is not null)
        {
            var targetSetting = new DisplaySetting(
                matchingSetting.Resolution,
                currentSettings.Position,
                matchingSetting.ColorDepth,
                matchingSetting.Frequency,
                matchingSetting.IsInterlaced,
                currentSettings.Orientation,
                currentSettings.OutputScalingMode
            );

            Log.Instance.Trace($"Setting display to {targetSetting.ToExtendedString()}...");

            await display.SetSettingsUsingPathInfoAsync(targetSetting, state.IsDynamic, physicalFrequency.GetValueOrDefault()).ConfigureAwait(false);

            Log.Instance.Trace($"Display set to {targetSetting.ToExtendedString()}");
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
