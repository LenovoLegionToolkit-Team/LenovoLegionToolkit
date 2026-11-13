
using System;
using System.Linq;
using System.Threading.Tasks;
using LenovoLegionToolkit.Lib.Utils;
using WindowsDisplayAPI;
using WindowsDisplayAPI.DisplayConfig;

namespace LenovoLegionToolkit.Lib.Extensions;

public static class DisplayExtensions
{
    private const int DrrPreInitAttempts = 20;
    private const int DrrPreInitDelayMs = 50;
    private const double FrequencyToleranceHz = 1.0;

    public static async Task SetSettingsUsingPathInfoAsync(this Display display, DisplaySetting displaySetting, bool enableDrr = false, int physicalFrequency = 0)
    {
        var targetPhysicalFreq = physicalFrequency > 0 ? physicalFrequency : 0;
        var useDrr = enableDrr;

        try
        {
            var displaySource = display.DisplayScreen.ToPathDisplaySource();
            var pathInfos = PathInfo.GetActivePaths(virtualModeAware: true);

            var matchingPaths = pathInfos
                .Where(p => p.DisplaySource == displaySource)
                .ToArray();

            if (matchingPaths.Length == 0)
            {
                throw new InvalidOperationException("No active display path found for the selected display.");
            }

            var maxFrequency = display.DisplayScreen.GetPossibleSettings()
                .Where(setting =>
                    setting.Resolution == displaySetting.Resolution &&
                    setting.IsInterlaced == displaySetting.IsInterlaced)
                .Select(setting => setting.Frequency)
                .DefaultIfEmpty(0)
                .Max();

            if (targetPhysicalFreq <= 0)
            {
                targetPhysicalFreq = maxFrequency;
            }

            var isCloneTopology = matchingPaths.Length > 1 ||
                matchingPaths.Any(path => path.IsCloneMember || path.TargetsInfo.Length > 1);

            if (useDrr && isCloneTopology)
            {
                Log.Instance.Trace($"Clone display topology detected. Disabling DRR for this operation.");
                useDrr = false;
            }

            if (useDrr && (targetPhysicalFreq <= 0 || targetPhysicalFreq <= displaySetting.Frequency))
            {
                Log.Instance.Trace($"No suitable physical refresh rate for DRR. Using static refresh rate.");
                useDrr = false;
            }

            var targetInfo = matchingPaths
                .SelectMany(path => path.TargetsInfo)
                .FirstOrDefault();

            var currentPhysicalFreq = targetInfo != null
                ? (targetInfo.IsSignalInformationAvailable
                    ? targetInfo.SignalInfo.VerticalSyncFrequencyInMillihertz / 1000.0
                    : targetInfo.FrequencyInMillihertz / 1000.0)
                : 0.0;

            var needsPreInit = useDrr && (
                matchingPaths.Any(path => !path.IsModeInformationAvailable) ||
                targetInfo == null ||
                !targetInfo.IsSignalInformationAvailable ||
                !targetInfo.IsDesktopImageInformationAvailable ||
                !targetInfo.IsVirtualModeSupportedByPath ||
                !targetInfo.IsBoostRefreshRate ||
                Math.Abs(currentPhysicalFreq - targetPhysicalFreq) >= FrequencyToleranceHz
            );

            if (needsPreInit)
            {
                Log.Instance.Trace($"DRR pre-initialization required. Setting display to physical frequency {targetPhysicalFreq}Hz...");

                var physicalSetting = new DisplaySetting(
                    displaySetting.Resolution,
                    displaySetting.Position,
                    displaySetting.ColorDepth,
                    targetPhysicalFreq,
                    displaySetting.IsInterlaced,
                    displaySetting.Orientation,
                    displaySetting.OutputScalingMode
                );

                display.DisplayScreen.SetSettings(physicalSetting, apply: true);

                var stabilized = false;

                for (var attempt = 0; attempt < DrrPreInitAttempts; attempt++)
                {
                    await Task.Delay(DrrPreInitDelayMs).ConfigureAwait(false);

                    pathInfos = PathInfo.GetActivePaths(virtualModeAware: true);

                    targetInfo = pathInfos
                        .Where(p => p.DisplaySource == displaySource)
                        .SelectMany(p => p.TargetsInfo)
                        .FirstOrDefault();

                    if (targetInfo == null || !targetInfo.IsSignalInformationAvailable)
                    {
                        continue;
                    }

                    var loopPhysicalFreq = targetInfo.SignalInfo.VerticalSyncFrequencyInMillihertz / 1000.0;

                    if (Math.Abs(loopPhysicalFreq - targetPhysicalFreq) < FrequencyToleranceHz)
                    {
                        Log.Instance.Trace($"DRR pre-initialization stabilized at {loopPhysicalFreq:F2}Hz in {(attempt + 1) * DrrPreInitDelayMs}ms.");

                        stabilized = true;
                        break;
                    }
                }

                if (!stabilized)
                {
                    throw new InvalidOperationException("DRR pre-initialization stabilization timed out after 1 second.");
                }
            }

            // Refresh display paths after possible mode changes.
            pathInfos = PathInfo.GetActivePaths(virtualModeAware: true);

            matchingPaths = pathInfos
                .Where(p => p.DisplaySource == displaySource)
                .ToArray();

            if (matchingPaths.Length == 0)
            {
                throw new InvalidOperationException("Display path disappeared after initialization.");
            }

            isCloneTopology = matchingPaths.Length > 1 ||
                matchingPaths.Any(path => path.IsCloneMember || path.TargetsInfo.Length > 1);

            if (useDrr)
            {
                var canUseDrr = !isCloneTopology &&
                    matchingPaths.Length == 1 &&
                    matchingPaths[0].TargetsInfo.Length == 1;

                if (canUseDrr)
                {
                    var target = matchingPaths[0].TargetsInfo[0];

                    canUseDrr = target.IsVirtualModeSupportedByPath &&
                        target.IsSignalInformationAvailable &&
                        target.IsDynamicRefreshRateSupported;
                }

                if (!canUseDrr)
                {
                    Log.Instance.Trace($"DRR is not supported by the current display path. Using static refresh rate.");
                    useDrr = false;
                }
            }

            for (var i = 0; i < pathInfos.Length; i++)
            {
                var pathInfo = pathInfos[i];

                if (pathInfo.DisplaySource != displaySource)
                {
                    continue;
                }

                if (!pathInfo.IsModeInformationAvailable)
                {
                    throw new InvalidOperationException("Cannot apply display path: source mode information is unavailable.");
                }

                var targetsInfo = pathInfo.TargetsInfo;

                var pathTargetInfos = targetsInfo
                    .Select(target =>
                    {
                        if (useDrr && target.IsVirtualModeSupportedByPath && target.IsSignalInformationAvailable)
                        {
                            var virtualFreqMillihertz = (ulong)displaySetting.Frequency * 1000UL;

                            var size = new global::System.Drawing.Size(
                                displaySetting.Resolution.Width,
                                displaySetting.Resolution.Height
                            );

                            var rect = new global::System.Drawing.Rectangle(
                                0,
                                0,
                                displaySetting.Resolution.Width,
                                displaySetting.Resolution.Height
                            );

                            var desktopImage = new PathTargetDesktopImage(size, rect, rect);

                            return new PathTargetInfo(
                                target.DisplayTarget,
                                target.SignalInfo,
                                desktopImage,
                                target.Rotation,
                                target.Scaling,
                                target.IsVirtualModeSupportedByPath,
                                true,
                                virtualFreqMillihertz
                            );
                        }

                        if (isCloneTopology || pathInfo.IsCloneMember || targetsInfo.Length > 1)
                        {
                            // Let Windows select compatible target signal timings
                            // without forcing identical physical timings on all monitors.
                            return new PathTargetInfo(
                                target.DisplayTarget,
                                (ulong)displaySetting.Frequency * 1000UL,
                                target.ScanLineOrdering,
                                target.Rotation,
                                target.Scaling,
                                target.IsVirtualModeSupportedByPath
                            );
                        }

                        var staticSignalInfo = new PathTargetSignalInfo(displaySetting, displaySetting.Resolution);

                        return new PathTargetInfo(
                            target.DisplayTarget,
                            staticSignalInfo,
                            target.Rotation,
                            target.Scaling,
                            target.IsVirtualModeSupportedByPath,
                            false
                        );
                    })
                    .ToArray();

                // Preserve the existing clone group.
                if (pathInfo.IsCloneMember)
                {
                    pathInfos[i] = new PathInfo(
                        pathInfo.DisplaySource,
                        pathInfo.Position,
                        displaySetting.Resolution,
                        pathInfo.PixelFormat,
                        pathTargetInfos,
                        pathInfo.CloneGroupId
                    );
                }
                else
                {
                    pathInfos[i] = new PathInfo(
                        pathInfo.DisplaySource,
                        pathInfo.Position,
                        displaySetting.Resolution,
                        pathInfo.PixelFormat,
                        pathTargetInfos
                    );
                }
            }

            PathInfo.ApplyPathInfos(pathInfos, saveToDatabase: true);

            Log.Instance.Trace($"Display path settings applied. Requested frequency={displaySetting.Frequency}Hz, DRR requested={enableDrr}, DRR attempted={useDrr}, Clone topology={isCloneTopology}.");

            try
            {
                var appliedPaths = PathInfo.GetActivePaths(virtualModeAware: true);

                var appliedTargets = appliedPaths
                    .Where(p => p.DisplaySource == displaySource)
                    .SelectMany(p => p.TargetsInfo)
                    .ToArray();

                if (appliedTargets.Length == 0)
                {
                    Log.Instance.Trace($"Display verification: no matching target found.");
                }

                foreach (var appliedTarget in appliedTargets)
                {
                    var physicalHz = appliedTarget.IsSignalInformationAvailable
                        ? appliedTarget.SignalInfo.VerticalSyncFrequencyInMillihertz / 1000.0
                        : 0.0;

                    var virtualHz = appliedTarget.FrequencyInMillihertz / 1000.0;

                    Log.Instance.Trace($"Display verification: TargetId={appliedTarget.DisplayTarget.TargetId}, Physical={physicalHz:F2}Hz, Virtual={virtualHz:F2}Hz, DRR={appliedTarget.IsBoostRefreshRate}.");
                }

                if (useDrr && (appliedTargets.Length == 0 || appliedTargets.Any(t => !t.IsBoostRefreshRate)))
                {
                    Log.Instance.Trace($"DRR verification: boost was not confirmed. The display may still be transitioning.");
                }
            }
            catch (Exception verificationEx)
            {
                Log.Instance.Trace($"Display verification failed: {verificationEx.Message}", verificationEx);
            }
        }
        catch (Exception ex)
        {
            Log.Instance.Trace($"Display path update failed: {ex.Message}. Falling back to DisplayScreen.SetSettings.", ex);

            try
            {
                if (useDrr && targetPhysicalFreq > 0)
                {
                    var fallbackPhysicalSetting = new DisplaySetting(
                        displaySetting.Resolution,
                        displaySetting.Position,
                        displaySetting.ColorDepth,
                        targetPhysicalFreq,
                        displaySetting.IsInterlaced,
                        displaySetting.Orientation,
                        displaySetting.OutputScalingMode
                    );

                    display.DisplayScreen.SetSettings(fallbackPhysicalSetting, apply: true);
                }
                else
                {
                    display.DisplayScreen.SetSettings(displaySetting, apply: true);
                }

                Log.Instance.Trace($"Display settings fallback applied.");
            }
            catch (Exception fallbackEx)
            {
                Log.Instance.Trace($"Display settings fallback failed: {fallbackEx.Message}", fallbackEx);
                throw;
            }
        }
    }

    public static DisplayAdvancedColorInfo GetAdvancedColorInfo(this Display display)
    {
        var pathDisplayTarget = display.ToPathDisplayTarget();
        if (pathDisplayTarget is null)
            return default;

        return pathDisplayTarget.GetAdvancedColorInfo();
    }

    public static void SetAdvancedColorState(this Display display, bool state)
    {
        var pathDisplayTarget = display.ToPathDisplayTarget();
        pathDisplayTarget?.SetAdvancedColorState(state);
    }

    public static void SetHdrState(this Display display, bool state)
    {
        var pathDisplayTarget = display.ToPathDisplayTarget();
        pathDisplayTarget?.SetHdrState(state);
    }

    public static void SetWcgState(this Display display, bool state)
    {
        var pathDisplayTarget = display.ToPathDisplayTarget();
        pathDisplayTarget?.SetWcgState(state);
    }
}
