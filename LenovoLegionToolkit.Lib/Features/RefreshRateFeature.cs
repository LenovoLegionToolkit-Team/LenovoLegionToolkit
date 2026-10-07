using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using LenovoLegionToolkit.Lib.Extensions;
using LenovoLegionToolkit.Lib.System;
using LenovoLegionToolkit.Lib.Utils;
using WindowsDisplayAPI;
using WindowsDisplayAPI.DisplayConfig;
using WindowsDisplayAPI.Native.DeviceContext;

namespace LenovoLegionToolkit.Lib.Features;

public class RefreshRateFeature : IFeature<RefreshRate>
{
    public Task<bool> IsSupportedAsync() => Task.FromResult(true);

    public async Task<RefreshRate[]> GetAllStatesAsync()
    {
        Log.Instance.Trace($"Getting all refresh rates...");

        var (display, frequencies) = await GetFrequenciesAsync();
        if (display is null)
        {
            Log.Instance.Trace($"Display not found");

            return [];
        }

        Log.Instance.Trace($"Display found: {display}");

        var currentSettings = display.DisplayScreen.CurrentSetting;

        Log.Instance.Trace($"Current display settings: {currentSettings.ToExtendedString()}");

        var result = frequencies.Select(freq => new RefreshRate(freq)).ToList();

        if (OSExtensions.GetCurrent() == OS.Windows11 && result.Count > 0)
        {
            var maxFreq = result.Max(r => r.Frequency);
            if (maxFreq >= 120 && display.IsInternal)
            {
                var targetInfo = GetTargetInfo(display);

                if (targetInfo is not null && (targetInfo.IsBoostRefreshRate || targetInfo.IsDynamicRefreshRateSupported))
                {
                    var lowFreq = targetInfo.DisplayTarget.GetDynamicLowFrequency(frequencies);
                    if (lowFreq > 0 && lowFreq < maxFreq)
                    {
                        result.Add(new RefreshRate(maxFreq, isDynamic: true, baseFrequency: lowFreq));
                    }
                }
            }
        }

        Log.Instance.Trace($"Possible refresh rates are {string.Join(", ", result)}");

        return result.ToArray();
    }

    public async Task<RefreshRate[]> GetAllDynamicStatesAsync()
    {
        Log.Instance.Trace($"Getting all dynamic refresh rates...");

        if (OSExtensions.GetCurrent() != OS.Windows11)
        {
            return [];
        }

        var (display, frequencies) = await GetFrequenciesAsync();
        if (display is null || !display.IsInternal || frequencies.Length == 0)
        {
            return [];
        }

        var targetInfo = GetTargetInfo(display);

        if (targetInfo is null || (!targetInfo.IsBoostRefreshRate && !targetInfo.IsDynamicRefreshRateSupported))
        {
            return [];
        }

        var result = new List<RefreshRate>();

        foreach (var frequency in frequencies)
        {
            var lowFrequency = targetInfo.DisplayTarget.GetDynamicLowFrequency(frequencies.Where(f => f <= frequency));
            if (lowFrequency > 0 && lowFrequency < frequency)
            {
                result.Add(new RefreshRate(frequency, isDynamic: true, baseFrequency: lowFrequency));
            }
        }

        Log.Instance.Trace($"Dynamic refresh rates are {string.Join(", ", result)}");

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

        var reportedFrequency = display.DisplayScreen.CurrentSetting.Frequency;
        var target = GetTargetInfo(display);

        if (target is not null && target.IsBoostRefreshRate)
        {
            var dynamicStates = (await GetAllStatesAsync().ConfigureAwait(false))
                .Concat(await GetAllDynamicStatesAsync().ConfigureAwait(false))
                .Where(r => r.IsDynamic)
                .Distinct()
                .ToArray();

            var physicalFrequency = target.SignalInfo is null ? 0 : (int)(target.SignalInfo.VerticalSyncFrequencyInMillihertz / 1000);

            var dynamicState = dynamicStates.FirstOrDefault(r => Math.Abs(r.Frequency - physicalFrequency) <= 1 && Math.Abs(r.BaseFrequency - reportedFrequency) <= 1);
            if (!dynamicState.IsDynamic)
            {
                dynamicState = dynamicStates.FirstOrDefault(r => Math.Abs(r.BaseFrequency - reportedFrequency) <= 1);
            }

            if (!dynamicState.IsDynamic)
            {
                dynamicState = dynamicStates.FirstOrDefault(r => Math.Abs(r.Frequency - reportedFrequency) <= 1);
            }

            if (!dynamicState.IsDynamic)
            {
                dynamicState = dynamicStates.FirstOrDefault();
            }

            if (dynamicState.IsDynamic)
            {
                Log.Instance.Trace($"Current refresh rate is {dynamicState}");
                return dynamicState;
            }
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

    private static async Task<(Display? Display, int[] Frequencies)> GetFrequenciesAsync()
    {
        var display = await InternalDisplay.GetAsync().ConfigureAwait(false);
        if (display is null)
        {
            return (null, []);
        }

        var currentSettings = display.DisplayScreen.CurrentSetting;

        var frequencies = display.DisplayScreen.GetPossibleSettings()
            .Where(dps => Match(dps, currentSettings))
            .Select(dps => dps.Frequency)
            .Distinct()
            .OrderBy(freq => freq)
            .ToArray();

        return (display, frequencies);
    }

    private static PathTargetInfo? GetTargetInfo(Display display)
    {
        var displaySource = display.DisplayScreen.ToPathDisplaySource();
        var displayTarget = display.ToPathDisplayTarget();

        var pathInfos = WindowsDisplayAPI.DisplayConfig.PathInfo.GetActivePaths(virtualModeAware: true);
        var activePath = pathInfos.FirstOrDefault(p => p.DisplaySource == displaySource && (displayTarget is null || p.TargetsInfo.Any(t => t.DisplayTarget == displayTarget)));

        return activePath?.TargetsInfo.FirstOrDefault(t => displayTarget is null || t.DisplayTarget == displayTarget);
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
