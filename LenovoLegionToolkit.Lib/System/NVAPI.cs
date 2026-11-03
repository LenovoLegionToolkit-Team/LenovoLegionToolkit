using System;
using System.Collections.Generic;
using System.Linq;
using System.Management;
using LenovoLegionToolkit.Lib.Utils;
using NvAPIWrapper;
using NvAPIWrapper.Display;
using NvAPIWrapper.GPU;
using NvAPIWrapper.Native;
using NvAPIWrapper.Native.Exceptions;
using NvAPIWrapper.Native.General;
using NvAPIWrapper.Native.GPU;
using NvAPIWrapper.Native.GPU.Structures;
using NvAPIWrapper.Native.Interfaces.GPU;

namespace LenovoLegionToolkit.Lib.System;

internal static class NVAPI
{
    private const int MAX_NVAPI_INIT_RETRIES = 3;

    public static bool IsInitialized { get; set; }
    private static bool? _hasNvidiaCache = null;
    private static int _nvApiInitRetries;

    public static void SetCache(bool? value)
    {
        _hasNvidiaCache = value;
        _nvApiInitRetries = 0;
    }

    public static void Initialize()
    {
        if (IsInitialized)
        {
            return;
        }

        switch (_hasNvidiaCache)
        {
            case false:
                return;
            case null:
            {
                var hasActive = HasActiveNvidiaGpu();
                if (hasActive == false)
                {
                    _hasNvidiaCache = false;
                    return;
                }

                break;
            }
        }

        try
        {
            NVIDIA.Initialize();
            IsInitialized = true;
            _hasNvidiaCache = true;
            _nvApiInitRetries = 0;
        }
        catch (NVIDIAApiException ex)
        {
            if ((ex.Status is Status.NvidiaDeviceNotFound or Status.ExpectedPhysicalGPUHandle)
                && _nvApiInitRetries++ < MAX_NVAPI_INIT_RETRIES)
            {
                _hasNvidiaCache = null;
                return;
            }

            _hasNvidiaCache = false;
            Log.Instance.Trace($"Exception in Initialize. Status: {(int)ex.Status}", ex);
        }
        catch (Exception ex)
        {
            _hasNvidiaCache = false;
            Log.Instance.Trace($"Failed to initialize NVAPI.", ex);
        }
    }

    public static bool IsAvailable()
    {
        Initialize();
        return GetGPU() is not null;
    }

    public static void Unload() => NVIDIA.Unload();

    public static bool? HasActiveNvidiaGpu()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT * FROM Win32_VideoController");
            using var collection = searcher.Get();

            bool foundButNotActive = false;

            foreach (var item in collection)
            {
                var pnpId = item["PNPDeviceID"]?.ToString()?.ToUpper();
                if (string.IsNullOrEmpty(pnpId) || !pnpId.Contains("VEN_10DE"))
                {
                    continue;
                }

                var errorCodeObj = item["ConfigManagerErrorCode"];
                if (errorCodeObj != null)
                {
                    uint errorCode = Convert.ToUInt32(errorCodeObj);
                    if (errorCode != 0)
                    {
                        Log.Instance.Trace($"NVIDIA GPU found but not active. ErrorCode: {errorCode}");
                        foundButNotActive = true;
                        continue;
                    }
                }

                var status = item["Status"]?.ToString();
                if (status != "OK")
                {
                    Log.Instance.Trace($"NVIDIA GPU found but Status is: {status}");
                    foundButNotActive = true;
                    continue;
                }

                return true;
            }

            if (foundButNotActive)
                return null;
        }
        catch (Exception ex)
        {
            Log.Instance.Trace($"Error checking for active NVIDIA GPU via WMI", ex);
            return null;
        }

        return false;
    }

    public static PhysicalGPU? GetGPU()
    {
        try
        {
            switch (_hasNvidiaCache)
            {
                case false:
                    return null;
                case null:
                {
                    var hasActive = HasActiveNvidiaGpu();
                    if (hasActive == false)
                    {
                        _hasNvidiaCache = false;
                        return null;
                    }

                    if (hasActive == true)
                    {
                        _hasNvidiaCache = true;
                        break;
                    }
                    
                    return null;
                }
            }

            var gpu = PhysicalGPU.GetPhysicalGPUs().FirstOrDefault(gpu => gpu.SystemType == SystemType.Laptop);

            if (gpu != null)
            {
                return gpu;
            }

            return null;
        }
        catch (NVIDIAApiException)
        {
            IsInitialized = false;

            return null;
        }
        catch (Exception)
        {
            return null;
        }
    }


    public static bool IsDisplayConnected(PhysicalGPU gpu)
    {
        try
        {
            if (Display.GetDisplays().Any(d => d.PhysicalGPUs.Contains(gpu, PhysicalGPUEqualityComparer.Instance)))
            {
                return true;
            }
        }
        catch (NVIDIAApiException)
        {
        }
        catch (Exception)
        {
        }

        try
        {
            return gpu.GetConnectedDisplayDevices(ConnectedIdsFlag.None).Length > 0;
        }
        catch
        {
            return false;
        }
    }

    public static PerformanceStateId? GetCurrentPerformanceState(PhysicalGPU gpu)
    {
        try
        {
            return GPUApi.GetCurrentPerformanceState(gpu.Handle);
        }
        catch (NVIDIAApiException ex) when (ex.Status == Status.PortIdNotFound || ex.Status == Status.GpuNotPowered)
        {
            return null;
        }
        catch (Exception ex)
        {
            Log.Instance.Trace($"Failed to get current performance state.", ex);
            return null;
        }
    }

    public static List<PerformanceStateId> GetSupportedPerformanceStates(PhysicalGPU gpu)
    {
        try
        {
            var statesInfo = GPUApi.GetPerformanceStates20(gpu.Handle);
            return statesInfo.PerformanceStates
                .Select(s => s.StateId)
                .Where(id => HasGraphicsClock(statesInfo, id))
                .Distinct()
                .OrderBy(s => (uint)s)
                .ToList();
        }
        catch (NVIDIAApiException ex) when (ex.Status == Status.PortIdNotFound || ex.Status == Status.GpuNotPowered)
        {
            return [];
        }
        catch (Exception ex)
        {
            Log.Instance.Trace($"Failed to get supported performance states.", ex);
            return [];
        }
    }

    private static bool HasGraphicsClock(IPerformanceStates20Info statesInfo, PerformanceStateId stateId)
        => statesInfo.Clocks.TryGetValue(stateId, out var clocks)
           && clocks.Any(c => c.DomainId == PublicClockDomain.Graphics);

    public static bool SetPerformanceState(PhysicalGPU gpu, PerformanceStateId stateId, PStatePinMode pinMode)
    {
        try
        {
            var frequencyInKhz = GetPStatePinFrequencyInKhz(gpu.Handle, stateId, pinMode);

            try
            {
                GPUApi.SetRatedTdpControl(gpu.Handle, PrivateRatedTdpControlV1.ClearRatedTdp());
            }
            catch (Exception ex)
            {
                Log.Instance.Trace($"Failed to set rated TDP control.", ex);
            }

            try
            {
                var clockLock = frequencyInKhz == 0
                    ? PrivateClockBoostLockV2.CreatePStateLock(stateId)
                    : PrivateClockBoostLockV2.CreatePStateAndFrequencyLock(stateId, frequencyInKhz);
                GPUApi.SetClockBoostLock(gpu.Handle, clockLock);
            }
            catch (Exception ex)
            {
                Log.Instance.Trace($"Failed to set clock boost lock.", ex);
            }

            return true;
        }
        catch (Exception ex)
        {
            Log.Instance.Trace($"Failed to set performance state to {stateId}.", ex);
            return false;
        }
    }

    public static bool SetRatedTdp(PhysicalGPU gpu)
    {
        try
        {
            try
            {
                GPUApi.SetClockBoostLock(gpu.Handle, PrivateClockBoostLockV2.CreateDynamicReset());
            }
            catch (Exception ex)
            {
                Log.Instance.Trace($"Failed to reset clock boost lock.", ex);
            }

            try
            {
                GPUApi.SetRatedTdpControl(gpu.Handle, PrivateRatedTdpControlV1.EnableRatedTdp());
            }
            catch (Exception ex)
            {
                Log.Instance.Trace($"Failed to set rated TDP control.", ex);
            }

            return true;
        }
        catch (Exception ex)
        {
            Log.Instance.Trace($"Failed to set rated TDP.", ex);
            return false;
        }
    }

    private static uint GetPStatePinFrequencyInKhz(PhysicalGPUHandle gpuHandle, PerformanceStateId stateId, PStatePinMode pinMode)
    {
        if (pinMode == PStatePinMode.None)
            return 0;

        try
        {
            var statesInfo = GPUApi.GetPerformanceStates20(gpuHandle);

            if (!statesInfo.Clocks.TryGetValue(stateId, out var clockEntries))
                return 0;

            var graphicsClock = clockEntries.FirstOrDefault(c => c.DomainId == PublicClockDomain.Graphics);
            if (graphicsClock is null)
                return 0;

            var frequencyRange = graphicsClock.FrequencyRange;
            if (frequencyRange is not null)
                return pinMode == PStatePinMode.Min
                    ? frequencyRange.MinimumFrequencyInkHz
                    : frequencyRange.MaximumFrequencyInkHz;

            return graphicsClock.SingleFrequency?.FrequencyInkHz ?? 0;
        }
        catch (Exception ex)
        {
            Log.Instance.Trace($"Failed to get performance states.", ex);
            return 0;
        }
    }

    public static bool ResetDynamicPerformanceStates(PhysicalGPU gpu)
    {
        try
        {
            try
            {
                GPUApi.SetRatedTdpControl(gpu.Handle, PrivateRatedTdpControlV1.ClearRatedTdp());
            }
            catch (Exception ex)
            {
                Log.Instance.Trace($"Failed to clear rated TDP control.", ex);
            }

            try
            {
                var resetLock = PrivateClockBoostLockV2.CreateDynamicReset();
                GPUApi.SetClockBoostLock(gpu.Handle, resetLock);
            }
            catch (Exception ex)
            {
                Log.Instance.Trace($"Failed to reset clock boost lock.", ex);
            }

            try
            {
                GPUApi.EnableDynamicPStates(gpu.Handle);
            }
            catch
            {
            }

            return true;
        }
        catch (Exception ex)
        {
            Log.Instance.Trace($"Failed to reset dynamic performance states.", ex);
            return false;
        }
    }

    public static string? GetGPUId(PhysicalGPU gpu)
    {
        try
        {
            return gpu.BusInformation.PCIIdentifiers.ToString();
        }
        catch (NVIDIAApiException)
        {
            return null;
        }
    }

    public static ICoprocInfo? GetCoprocInfo(PhysicalGPU gpu)
    {
        try
        {
            return gpu.GetCoprocInfo();
        }
        catch
        {
            return null;
        }
    }

    public static GC6DebugInfoV2? GetGC6DebugInfo(PhysicalGPU gpu)
    {
        try
        {
            return gpu.GetGC6DebugInfo();
        }
        catch
        {
            return null;
        }
    }

    private class PhysicalGPUEqualityComparer : IEqualityComparer<PhysicalGPU>
    {
        public static readonly PhysicalGPUEqualityComparer Instance = new();

        private PhysicalGPUEqualityComparer() { }

        public bool Equals(PhysicalGPU? x, PhysicalGPU? y) => x?.GPUId == y?.GPUId;

        public int GetHashCode(PhysicalGPU obj) => obj.GPUId.GetHashCode();
    }
}
