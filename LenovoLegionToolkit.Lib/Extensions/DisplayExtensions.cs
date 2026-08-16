using System;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using LenovoLegionToolkit.Lib.Utils;
using Windows.Win32;
using Windows.Win32.Devices.Display;
using Windows.Win32.Graphics.Gdi;
using WindowsDisplayAPI;
using WindowsDisplayAPI.DisplayConfig;

namespace LenovoLegionToolkit.Lib.Extensions;

public static class DisplayExtensions
{
    private const int DrrPreInitAttempts = 20;
    private const int DrrPreInitDelayMs = 50;

    public static async Task SetSettingsUsingPathInfoAsync(this Display display, DisplaySetting displaySetting, bool enableDrr = false, int physicalFrequency = 0)
    {
        try
        {
            var displaySource = display.DisplayScreen.ToPathDisplaySource();
            var pathInfos = PathInfo.GetActivePaths(virtualModeAware: true);

            var targetInfo = pathInfos
                .Where(p => p.DisplaySource == displaySource)
                .SelectMany(p => p.TargetsInfo)
                .FirstOrDefault();

            var maxFrequency = display.DisplayScreen.GetPossibleSettings()
                .Select(dps => dps.Frequency)
                .DefaultIfEmpty(0)
                .Max();
            var targetPhysicalFreq = physicalFrequency > 0 ? physicalFrequency : maxFrequency;

            var currentPhysicalFreq = targetInfo != null
                ? (targetInfo.IsSignalInformationAvailable
                    ? (int)(targetInfo.SignalInfo.VerticalSyncFrequencyInMillihertz / 1000)
                    : (int)(targetInfo.FrequencyInMillihertz / 1000))
                : 0;

            var needsPreInit = enableDrr && maxFrequency > 0 && (
                targetInfo == null ||
                !targetInfo.IsDesktopImageInformationAvailable ||
                (physicalFrequency > 0 && (!targetInfo.IsBoostRefreshRate || currentPhysicalFreq != physicalFrequency))
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

                    var isSignalAvailable = targetInfo?.IsSignalInformationAvailable == true;
                    var loopPhysicalFreq = isSignalAvailable ? (int)(targetInfo!.SignalInfo!.VerticalSyncFrequencyInMillihertz / 1000) : 0;

                    if (targetInfo != null && isSignalAvailable && loopPhysicalFreq == targetPhysicalFreq)
                    {
                        Log.Instance.Trace($"DRR pre-initialization stabilized at {loopPhysicalFreq}Hz in {(attempt + 1) * DrrPreInitDelayMs}ms.");
                        stabilized = true;
                        break;
                    }
                }

                if (!stabilized)
                {
                    Log.Instance.Trace($"DRR pre-initialization stabilization timed out after 1 second.");
                }
            }

            for (var i = 0; i < pathInfos.Length; i++)
            {
                var pathInfo = pathInfos[i];

                if (pathInfo.DisplaySource == displaySource)
                {
                    var targetsInfo = pathInfo.TargetsInfo;
                    var pathTargetInfos = targetsInfo
                        .Select(target =>
                        {
                            if (enableDrr && target.IsVirtualModeSupportedByPath)
                            {
                                var virtualFreqMillihertz = (ulong)(displaySetting.Frequency * 1000);
                                var size = new global::System.Drawing.Size(displaySetting.Resolution.Width, displaySetting.Resolution.Height);
                                var rect = new global::System.Drawing.Rectangle(0, 0, displaySetting.Resolution.Width, displaySetting.Resolution.Height);
                                var desktopImage = new PathTargetDesktopImage(size, rect, rect);

                                return new PathTargetInfo(
                                    target.DisplayTarget,
                                    target.SignalInfo!,
                                    desktopImage,
                                    target.Rotation,
                                    target.Scaling,
                                    target.IsVirtualModeSupportedByPath,
                                    true,
                                    virtualFreqMillihertz
                                );
                            }
                            else
                            {
                                var staticSignalInfo = new PathTargetSignalInfo(displaySetting, displaySetting.Resolution);
                                return new PathTargetInfo(
                                    target.DisplayTarget,
                                    staticSignalInfo,
                                    target.Rotation,
                                    target.Scaling,
                                    target.IsVirtualModeSupportedByPath,
                                    false
                                );
                            }
                        })
                        .ToArray();

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
        }
        catch (Exception ex)
        {
            Log.Instance.Trace($"SetSettingsUsingPathInfoAsync failed. Falling back to DisplayScreen.SetSettings.", ex);
            display.DisplayScreen.SetSettings(displaySetting, apply: true);
        }
    }

    public static DisplayAdvancedColorInfo GetAdvancedColorInfo(this Display display)
    {
        var pathDisplayAdapter = display.Adapter.ToPathDisplayAdapter();
        var pathDisplayTarget = display.ToPathDisplayTarget();

        if (pathDisplayTarget is null || pathDisplayAdapter is null)
            return default;

        var getAdvancedColorInfo2 = new DISPLAYCONFIG_GET_ADVANCED_COLOR_INFO_2();
        getAdvancedColorInfo2.header.type = DISPLAYCONFIG_DEVICE_INFO_TYPE.DISPLAYCONFIG_DEVICE_INFO_GET_ADVANCED_COLOR_INFO_2;
        getAdvancedColorInfo2.header.size = (uint)Marshal.SizeOf(typeof(DISPLAYCONFIG_GET_ADVANCED_COLOR_INFO_2));
        getAdvancedColorInfo2.header.adapterId.HighPart = pathDisplayAdapter.AdapterId.HighPart;
        getAdvancedColorInfo2.header.adapterId.LowPart = pathDisplayAdapter.AdapterId.LowPart;
        getAdvancedColorInfo2.header.id = pathDisplayTarget.TargetId;

        if (PInvoke.DisplayConfigGetDeviceInfo(ref getAdvancedColorInfo2.header) == 0)
        {
            bool hdrSupported = getAdvancedColorInfo2.Anonymous.Anonymous.highDynamicRangeSupported;
            bool wideColorEnforced = getAdvancedColorInfo2.Anonymous.Anonymous.advancedColorLimitedByPolicy;
            bool advancedColorForceDisabled = false;
            bool advancedColorEnabled = hdrSupported && getAdvancedColorInfo2.activeColorMode == DISPLAYCONFIG_ADVANCED_COLOR_MODE.DISPLAYCONFIG_ADVANCED_COLOR_MODE_HDR;

            return new(hdrSupported,
                advancedColorEnabled,
                wideColorEnforced,
                advancedColorForceDisabled);
        }

        var getAdvancedColorInfo = new DISPLAYCONFIG_GET_ADVANCED_COLOR_INFO();
        getAdvancedColorInfo.header.type = DISPLAYCONFIG_DEVICE_INFO_TYPE.DISPLAYCONFIG_DEVICE_INFO_GET_ADVANCED_COLOR_INFO;
        getAdvancedColorInfo.header.size = (uint)Marshal.SizeOf(typeof(DISPLAYCONFIG_GET_ADVANCED_COLOR_INFO));
        getAdvancedColorInfo.header.adapterId.HighPart = pathDisplayAdapter.AdapterId.HighPart;
        getAdvancedColorInfo.header.adapterId.LowPart = pathDisplayAdapter.AdapterId.LowPart;
        getAdvancedColorInfo.header.id = pathDisplayTarget.TargetId;

        if (PInvoke.DisplayConfigGetDeviceInfo(ref getAdvancedColorInfo.header) != 0)
            PInvokeExtensions.ThrowIfWin32Error("GetAdvancedColorInfo");

        return new(getAdvancedColorInfo.Anonymous.value.GetNthBit(0),
            getAdvancedColorInfo.Anonymous.value.GetNthBit(1),
            getAdvancedColorInfo.Anonymous.value.GetNthBit(2),
            getAdvancedColorInfo.Anonymous.value.GetNthBit(3));
    }

    public static void SetAdvancedColorState(this Display display, bool state)
    {
        var pathDisplayAdapter = display.Adapter.ToPathDisplayAdapter();
        var pathDisplayTarget = display.ToPathDisplayTarget();

        if (pathDisplayTarget is null || pathDisplayAdapter is null)
            return;

        var setHdrState = new DISPLAYCONFIG_SET_HDR_STATE();
        setHdrState.header.type = DISPLAYCONFIG_DEVICE_INFO_TYPE.DISPLAYCONFIG_DEVICE_INFO_SET_HDR_STATE;
        setHdrState.header.size = (uint)Marshal.SizeOf(typeof(DISPLAYCONFIG_SET_HDR_STATE));
        setHdrState.header.adapterId.HighPart = pathDisplayAdapter.AdapterId.HighPart;
        setHdrState.header.adapterId.LowPart = pathDisplayAdapter.AdapterId.LowPart;
        setHdrState.header.id = pathDisplayTarget.TargetId;
        setHdrState.Anonymous.Anonymous.enableHdr = state;

        if (PInvoke.DisplayConfigSetDeviceInfo(setHdrState.header) == 0)
            return;

        var setAdvancedColorState = new DISPLAYCONFIG_SET_ADVANCED_COLOR_STATE();
        setAdvancedColorState.header.type = DISPLAYCONFIG_DEVICE_INFO_TYPE.DISPLAYCONFIG_DEVICE_INFO_SET_ADVANCED_COLOR_STATE;
        setAdvancedColorState.header.size = (uint)Marshal.SizeOf<DISPLAYCONFIG_SET_ADVANCED_COLOR_STATE>();
        setAdvancedColorState.header.adapterId.HighPart = pathDisplayAdapter.AdapterId.HighPart;
        setAdvancedColorState.header.adapterId.LowPart = pathDisplayAdapter.AdapterId.LowPart;
        setAdvancedColorState.header.id = pathDisplayTarget.TargetId;
        setAdvancedColorState.Anonymous.value = setAdvancedColorState.Anonymous.value.SetNthBit(0, state);

        if (PInvoke.DisplayConfigSetDeviceInfo(setAdvancedColorState.header) != 0)
            PInvokeExtensions.ThrowIfWin32Error("SetAdvancedColorState");
    }
}
