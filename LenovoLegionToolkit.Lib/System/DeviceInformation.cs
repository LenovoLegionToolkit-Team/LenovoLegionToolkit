using LenovoLegionToolkit.Lib.Utils;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Management;
using System.Runtime.InteropServices;
using System.Text;
using NvAPIWrapper;
using NvAPIWrapper.GPU;

namespace LenovoLegionToolkit.Lib.System;

public static class DeviceInformation
{
    #region Win32 Display P/Invoke

    private struct ScreenSettings { public string DeviceID; public int Width; public int Height; public int RefreshRate; }

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern bool EnumDisplaySettings(string lpszDeviceName, int iModeNum, ref DEVMODE lpDevMode);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern bool EnumDisplayDevices(string? lpDevice, uint iDevNum, ref DISPLAY_DEVICE lpDisplayDevice, uint dwFlags);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct DISPLAY_DEVICE
    {
        public uint cb;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string DeviceName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceString;
        public uint StateFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceID;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceKey;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct DEVMODE
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmDeviceName;
        public ushort dmSpecVersion; public ushort dmDriverVersion; public ushort dmSize; public ushort dmDriverExtra;
        public uint dmFields; public int dmPositionX; public int dmPositionY; public int dmDisplayOrientation;
        public uint dmDisplayFixedOutput; public short dmColor; public short dmDuplex; public short dmYResolution;
        public short dmTTOption; public short dmCollate;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmFormName;
        public ushort dmLogPixels; public uint dmBitsPerPel; public uint dmPelsWidth; public uint dmPelsHeight;
        public uint dmDisplayFlags; public uint dmDisplayFrequency;
    }

    private const int ENUM_CURRENT_SETTINGS = -1;
    private const uint DISPLAY_DEVICE_ACTIVE = 0x1;

    #endregion

    private static readonly Lazy<CpuHardwareInfo> _cpuInfo = new(FetchCpuInfo);
    private static readonly Lazy<List<GpuHardwareInfo>> _gpuInfos = new(FetchGpuInfos);
    private static readonly Lazy<List<DiskHardwareInfo>> _diskInfos = new(FetchDiskInfos);
    private static readonly Lazy<List<MemoryHardwareInfo>> _memoryInfos = new(FetchMemoryInfos);
    private static readonly Lazy<List<DisplayHardwareInfo>> _displayInfos = new(FetchDisplayInfos);
    private static readonly Lazy<MotherboardHardwareInfo> _motherboardInfo = new(FetchMotherboardInfo);
    private static readonly Lazy<List<LogicalDriveInfo>> _logicalDrives = new(FetchLogicalDrives);
    private static readonly Lazy<List<NetworkHardwareInfo>> _networkAdapters = new(FetchNetworkAdapters);

    public static CpuHardwareInfo GetCpuInfo() => _cpuInfo.Value;
    public static List<GpuHardwareInfo> GetGpuInfos() => _gpuInfos.Value.ToList();
    public static List<DiskHardwareInfo> GetDiskInfos() => _diskInfos.Value.ToList();
    public static List<MemoryHardwareInfo> GetMemoryInfos() => _memoryInfos.Value.ToList();
    public static List<DisplayHardwareInfo> GetDisplayInfos() => _displayInfos.Value.ToList();
    public static MotherboardHardwareInfo GetMotherboardInfo() => _motherboardInfo.Value;
    public static List<LogicalDriveInfo> GetLogicalDrives() => _logicalDrives.Value.ToList();
    public static List<NetworkHardwareInfo> GetNetworkAdapters() => _networkAdapters.Value.ToList();

    #region Fetching Implementation

    private static CpuHardwareInfo FetchCpuInfo()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT Name, NumberOfCores, NumberOfLogicalProcessors FROM Win32_Processor");
            using var collection = searcher.Get();

            foreach (var obj in collection)
            {
                string? name = obj["Name"]?.ToString()?.Trim();
                uint cores = obj["NumberOfCores"] is not null ? Convert.ToUInt32(obj["NumberOfCores"]) : 0;
                uint threads = obj["NumberOfLogicalProcessors"] is not null ? Convert.ToUInt32(obj["NumberOfLogicalProcessors"]) : 0;

                return new CpuHardwareInfo(name, cores, threads);
            }
        }
        catch (Exception ex)
        {
            Log.Instance.Trace($"Unable to get CPU info from WMI. Reason: {ex}");
        }

        return new CpuHardwareInfo("CPU Not Found", 0, 0);
    }

    private static List<GpuHardwareInfo> FetchGpuInfos()
    {
        var gpus = new List<GpuHardwareInfo>();

        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT Name, AdapterRAM FROM Win32_VideoController");
            using var collection = searcher.Get();

            foreach (var obj in collection)
            {
                string name = obj["Name"]?.ToString()?.Trim() ?? "";
                if (string.IsNullOrEmpty(name) ||
                   (!name.Contains("NVIDIA", StringComparison.OrdinalIgnoreCase) &&
                    !name.Contains("AMD", StringComparison.OrdinalIgnoreCase) &&
                    !name.Contains("INTEL", StringComparison.OrdinalIgnoreCase)))
                {
                    continue;
                }

                ulong ramBytes = obj["AdapterRAM"] != null ? Convert.ToUInt64(obj["AdapterRAM"]) : 0;
                string vramStr = "Shared/Dynamic";

                if (ramBytes > 0 && ramBytes < 4294967295)
                {
                    double ramGigabytes = ramBytes / 1_073_741_824.0;
                    vramStr = ramGigabytes <= 0.1 ? "Shared/Dynamic" : ramGigabytes.ToString("F1");
                }

                gpus.Add(new GpuHardwareInfo(name, vramStr));
            }
        }
        catch (Exception ex)
        {
            Log.Instance.Trace($"Unable to get baseline GPU info. Reason: {ex}");
        }

        try
        {
            NVIDIA.Initialize();
            var physicalGPUs = PhysicalGPU.GetPhysicalGPUs();

            foreach (var nvGpu in physicalGPUs)
            {
                string nvName = nvGpu.FullName;
                double vramGb = nvGpu.MemoryInformation.DedicatedVideoMemoryInkB / (1024.0 * 1024.0);
                string trueVramStr = vramGb.ToString("F1");

                var existingIndex = gpus.FindIndex(g => g.Name != null && g.Name.Contains("NVIDIA", StringComparison.OrdinalIgnoreCase) && (g.Name.Contains(nvName) || nvName.Contains(g.Name)));

                if (existingIndex >= 0)
                {
                    gpus[existingIndex] = new GpuHardwareInfo(nvName, trueVramStr);
                }
                else
                {
                    gpus.Add(new GpuHardwareInfo(nvName, trueVramStr));
                }
            }
        }
        catch (Exception ex)
        {
            Log.Instance.Trace($"NVAPIWrapper failed or not supported. Reason: {ex}");
        }

        return gpus;
    }

    private static List<DiskHardwareInfo> FetchDiskInfos()
    {
        var disks = new List<DiskHardwareInfo>();
        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT Model, Size FROM Win32_DiskDrive");
            using var collection = searcher.Get();

            foreach (var obj in collection)
            {
                string? model = obj["Model"]?.ToString()?.Trim();
                ulong bytes = obj["Size"] is not null ? Convert.ToUInt64(obj["Size"]) : 0;
                double gigabytes = bytes / 1_073_741_824.0;

                disks.Add(new DiskHardwareInfo(model, gigabytes));
            }
        }
        catch (Exception ex)
        {
            Log.Instance.Trace($"Unable to get disk info from WMI. Reason: {ex}");
        }
        return disks;
    }

    private static List<MemoryHardwareInfo> FetchMemoryInfos()
    {
        var memoryList = new List<MemoryHardwareInfo>();
        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT Capacity, Speed, MemoryType, SMBIOSMemoryType, Manufacturer FROM Win32_PhysicalMemory");
            using var collection = searcher.Get();

            int slot = 1;
            foreach (var obj in collection)
            {
                ulong bytes = obj["Capacity"] is not null ? Convert.ToUInt64(obj["Capacity"]) : 0;
                double gigabytes = bytes / 1_073_741_824.0;

                string? speed = obj["Speed"]?.ToString();

                uint typeCode = Convert.ToUInt32(obj["SMBIOSMemoryType"] ?? obj["MemoryType"] ?? 0u);
                string generation = GetMemoryGeneration(typeCode);

                string? rawManufacturer = obj["Manufacturer"]?.ToString()?.Trim() ?? "";

                if (string.IsNullOrWhiteSpace(rawManufacturer) ||
                    rawManufacturer.StartsWith("00") ||
                    rawManufacturer.Equals("Unknown", StringComparison.OrdinalIgnoreCase))
                {
                    rawManufacturer = null;
                }

                memoryList.Add(new MemoryHardwareInfo(slot++, generation, gigabytes, speed, rawManufacturer));
            }
        }
        catch (Exception ex)
        {
            Log.Instance.Trace($"Unable to get memory info from WMI. Reason: {ex}");
        }
        return memoryList;
    }

    private static List<DisplayHardwareInfo> FetchDisplayInfos()
    {
        var displays = new List<DisplayHardwareInfo>();
        try
        {
            var activeSettingsList = new List<ScreenSettings>();
            DISPLAY_DEVICE adapterDevice = new();
            adapterDevice.cb = (uint)Marshal.SizeOf(adapterDevice);
            uint adapterIdx = 0;

            while (EnumDisplayDevices(null, adapterIdx++, ref adapterDevice, 0))
            {
                if ((adapterDevice.StateFlags & DISPLAY_DEVICE_ACTIVE) == 0) continue;

                DISPLAY_DEVICE monitorDevice = new();
                monitorDevice.cb = (uint)Marshal.SizeOf(monitorDevice);
                uint monitorIdx = 0;

                while (EnumDisplayDevices(adapterDevice.DeviceName, monitorIdx++, ref monitorDevice, 0))
                {
                    if ((monitorDevice.StateFlags & DISPLAY_DEVICE_ACTIVE) != 0)
                    {
                        DEVMODE vMode = new();
                        vMode.dmSize = (ushort)Marshal.SizeOf(vMode);

                        if (EnumDisplaySettings(adapterDevice.DeviceName, ENUM_CURRENT_SETTINGS, ref vMode))
                        {
                            int hz = (int)vMode.dmDisplayFrequency;
                            if (hz <= 1) hz = 60;

                            activeSettingsList.Add(new ScreenSettings
                            {
                                DeviceID = monitorDevice.DeviceID,
                                Width = (int)vMode.dmPelsWidth,
                                Height = (int)vMode.dmPelsHeight,
                                RefreshRate = hz
                            });
                        }
                    }
                }
            }

            var wmiMonitors = GetWmiMonitorsWithInstanceNames();
            int index = 1;

            foreach (var screen in activeSettingsList)
            {
                string modelName = "Generic Display";

                if (!string.IsNullOrEmpty(screen.DeviceID))
                {
                    string[] targetParts = screen.DeviceID.Split('\\');
                    if (targetParts.Length > 1)
                    {
                        string pnpSubId = targetParts[1];
                        var matched = wmiMonitors.FirstOrDefault(m => m.InstanceName.Contains(pnpSubId, StringComparison.OrdinalIgnoreCase));

                        if (!string.IsNullOrWhiteSpace(matched.FriendlyName))
                        {
                            modelName = matched.FriendlyName;
                        }
                    }
                }

                displays.Add(new DisplayHardwareInfo(index++, modelName, screen.Width, screen.Height, screen.RefreshRate));
            }
        }
        catch (Exception ex)
        {
            Log.Instance.Trace($"Unable to get display info accurately. Reason: {ex}");
        }
        return displays;
    }

    private static List<(string InstanceName, string FriendlyName)> GetWmiMonitorsWithInstanceNames()
    {
        var result = new List<(string InstanceName, string FriendlyName)>();
        try
        {
            using var searcher = new ManagementObjectSearcher("root\\WMI", "SELECT InstanceName, UserFriendlyName FROM WmiMonitorID");
            using var collection = searcher.Get();

            foreach (var obj in collection)
            {
                string instanceName = obj["InstanceName"]?.ToString() ?? "";
                string friendlyName = "Generic Display";

                if (obj["UserFriendlyName"] is ushort[] nameArray)
                {
                    var sb = new StringBuilder();
                    foreach (var i in nameArray)
                    {
                        if (i == 0) break;
                        sb.Append((char)i);
                    }
                    string parsedName = sb.ToString().Trim();
                    if (!string.IsNullOrWhiteSpace(parsedName))
                    {
                        friendlyName = parsedName;
                    }
                }

                if (!string.IsNullOrEmpty(instanceName))
                {
                    result.Add((instanceName, friendlyName));
                }
            }
        }
        catch { /* Muted */ }
        return result;
    }

    private static MotherboardHardwareInfo FetchMotherboardInfo()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT Manufacturer, Product FROM Win32_BaseBoard");
            using var collection = searcher.Get();

            foreach (var obj in collection)
            {
                string? manufacturer = obj["Manufacturer"]?.ToString()?.Trim();
                string? product = obj["Product"]?.ToString()?.Trim();

                return new MotherboardHardwareInfo(manufacturer, product);
            }
        }
        catch (Exception ex)
        {
            Log.Instance.Trace($"Unable to get Motherboard info from WMI. Reason: {ex}");
        }

        return new MotherboardHardwareInfo(null, null);
    }

    private static string? GetMemoryGeneration(uint typeCode) => typeCode switch
    {
        26 => "DDR4",
        34 => "DDR5",
        _ => null
    };

    private static List<LogicalDriveInfo> FetchLogicalDrives()
    {
        var drives = new List<LogicalDriveInfo>();
        try
        {
            foreach (var drive in global::System.IO.DriveInfo.GetDrives())
            {
                if (drive.IsReady && drive.DriveType == global::System.IO.DriveType.Fixed)
                {
                    double sizeGb = drive.TotalSize / 1_073_741_824.0;
                    double freeGb = drive.AvailableFreeSpace / 1_073_741_824.0;
                    drives.Add(new LogicalDriveInfo(drive.Name, drive.VolumeLabel, sizeGb, freeGb));
                }
            }
        }
        catch (Exception ex)
        {
            Log.Instance.Trace($"Unable to get logical drives. Reason: {ex}");
        }
        return drives;
    }

    private static List<NetworkHardwareInfo> FetchNetworkAdapters()
    {
        var adapters = new List<NetworkHardwareInfo>();
        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT Name, MACAddress FROM Win32_NetworkAdapter WHERE PhysicalAdapter = True AND MACAddress IS NOT NULL");
            using var collection = searcher.Get();

            foreach (var obj in collection)
            {
                string? name = obj["Name"]?.ToString()?.Trim();
                string? mac = obj["MACAddress"]?.ToString()?.Trim();
                adapters.Add(new NetworkHardwareInfo(name, mac));
            }
        }
        catch (Exception ex)
        {
            Log.Instance.Trace($"Unable to get network adapters from WMI. Reason: {ex}");
        }
        return adapters;
    }

    #endregion
}

public record CpuHardwareInfo(string? Name, uint CoreCount, uint ThreadCount)
{
    public string this[int index] => index switch
    {
        0 => Name ?? "",
        1 => CoreCount.ToString(),
        2 => ThreadCount.ToString(),
        _ => throw new ArgumentOutOfRangeException(nameof(index))
    };
}

public record GpuHardwareInfo(string? Name, string? Vram)
{
    public string this[int index] => index switch
    {
        0 => Name ?? "",
        1 => Vram ?? "",
        _ => throw new ArgumentOutOfRangeException(nameof(index))
    };
}

public record DiskHardwareInfo(string? Model, double SizeGb)
{
    public string this[int index] => index switch
    {
        0 => Model ?? "",
        1 => SizeGb.ToString("F2"),
        _ => throw new ArgumentOutOfRangeException(nameof(index))
    };
}

public record MemoryHardwareInfo(int Slot, string? Generation, double CapacityGb, string? SpeedMhz, string? Manufacturer)
{
    public string this[int index] => index switch
    {
        0 => Slot.ToString(),
        1 => Generation ?? "",
        2 => CapacityGb.ToString("F0"),
        3 => SpeedMhz ?? "",
        4 => Manufacturer ?? "",
        _ => throw new ArgumentOutOfRangeException(nameof(index))
    };
}

public record DisplayHardwareInfo(int Index, string? Model, int Width, int Height, int RefreshRate)
{
    public string this[int index] => index switch
    {
        0 => Index.ToString(),
        1 => Model ?? "",
        2 => $"{Width}x{Height}",
        3 => RefreshRate.ToString(),
        _ => throw new ArgumentOutOfRangeException(nameof(index))
    };
}

public record MotherboardHardwareInfo(string? Manufacturer, string? Product)
{
    public string this[int index] => index switch
    {
        0 => Manufacturer ?? "",
        1 => Product ?? "",
        _ => throw new ArgumentOutOfRangeException(nameof(index))
    };
}

public record LogicalDriveInfo(string Name, string Label, double SizeGb, double FreeGb)
{
    public double UsedGb => SizeGb - FreeGb;

    public string this[int index] => index switch
    {
        0 => Name,
        1 => Label,
        2 => SizeGb.ToString("F2"),
        3 => FreeGb.ToString("F2"),
        _ => throw new ArgumentOutOfRangeException(nameof(index))
    };
}

public record NetworkHardwareInfo(string? Name, string? MacAddress)
{
    public string this[int index] => index switch
    {
        0 => Name ?? "",
        1 => MacAddress ?? "",
        _ => throw new ArgumentOutOfRangeException(nameof(index))
    };
}