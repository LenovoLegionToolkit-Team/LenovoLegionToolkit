using LenovoLegionToolkit.Lib.Utils;
using NvAPIWrapper;
using NvAPIWrapper.Display;
using NvAPIWrapper.GPU;
using NvAPIWrapper.Native.Exceptions;
using NvAPIWrapper.Native.GPU;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Management;

namespace LenovoLegionToolkit.Lib.System;

internal static class NVAPI
{
    public static bool IsInitialized { get; set; }
    private static bool? _hasNvidiaCache = null;

    public static void SetCache(bool? value) => _hasNvidiaCache = value;

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
        }
        catch (NVIDIAApiException ex)
        {
            _hasNvidiaCache = false;

            if ((int)ex.Status != -101 && (int)ex.Status != -6)
            {
                Log.Instance.Trace($"Exception in Initialize. Status: {(int)ex.Status}", ex);
            }
        }
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
            return Display.GetDisplays().Any(d => d.PhysicalGPUs.Contains(gpu, PhysicalGPUEqualityComparer.Instance));
        }
        catch (NVIDIAApiException)
        {
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

    private class PhysicalGPUEqualityComparer : IEqualityComparer<PhysicalGPU>
    {
        public static readonly PhysicalGPUEqualityComparer Instance = new();

        private PhysicalGPUEqualityComparer() { }

        public bool Equals(PhysicalGPU? x, PhysicalGPU? y) => x?.GPUId == y?.GPUId;

        public int GetHashCode(PhysicalGPU obj) => obj.GPUId.GetHashCode();
    }
}
