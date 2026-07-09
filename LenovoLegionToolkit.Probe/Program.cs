using System.Management;
using System.Text;

using LenovoLegionToolkit.Lib;
using LenovoLegionToolkit.Lib.System.Management;

Console.OutputEncoding = Encoding.UTF8;

Console.WriteLine(@"============================================================================");
Console.WriteLine(@"Probe - Lenovo Gaming Hardware Information Gatherer");
Console.WriteLine(@"============================================================================");
Console.WriteLine(@"This tool scans your system for known Lenovo gaming features (Legion, LOQ, IdeaPad).");
Console.WriteLine(@"Press any key to start scanning...");
Console.ReadKey();
Console.WriteLine();

string GetFullException(Exception ex)
{
    var sb = new StringBuilder();
    sb.AppendLine($"[Exception]: {ex.GetType().Name}");
    sb.AppendLine($"[Message]: {ex.Message}");
    sb.AppendLine($"[StackTrace]: {ex.StackTrace}");

    if (ex.InnerException != null)
    {
        sb.AppendLine("\n--- Inner Exception ---");
        sb.Append(GetFullException(ex.InnerException));
    }
    return sb.ToString();
}

/// <summary>
/// Logs an exception with full details including inner exceptions.
/// </summary>
void LogError(Exception ex)
{
    Console.ForegroundColor = ConsoleColor.Red;
    Console.WriteLine(GetFullException(ex));
    Console.ResetColor();
}

/// <summary>
/// Logs a success message in green.
/// </summary>
void LogSuccess(string message)
{
    Console.ForegroundColor = ConsoleColor.Green;
    Console.WriteLine(message);
    Console.ResetColor();
}

/// <summary>
/// Logs a warning message in yellow.
/// </summary>
void LogWarning(string message)
{
    Console.ForegroundColor = ConsoleColor.Yellow;
    Console.WriteLine(message);
    Console.ResetColor();
}

/// <summary>
/// Tries to get a capability value and logs the result.
/// </summary>
/// <param name="id">The capability ID.</param>
/// <param name="description">The human-readable description.</param>
/// <param name="supported">The set of supported capability IDs.</param>
/// <returns>The capability value if retrieved, otherwise null.</returns>
async Task<int?> TryGetCapability(CapabilityID id, string description, HashSet<CapabilityID> supported)
{
    var isRegistered = supported.Contains(id);
    try
    {
        var value = await WMI.LenovoOtherMethod.GetFeatureValueAsync(id).ConfigureAwait(false);
        if (isRegistered)
        {
            LogSuccess($"  [{id}] {description}: {value}");
        }
        else
        {
            LogWarning($"  [{id}] {description}: {value} (not registered)");
        }
        return value;
    }
    catch
    {
        LogWarning($"  [{id}] {description}: Not Supported");
        return null;
    }
}

// Section 1: System Information
Console.WriteLine(@">>> Section 1: System Information");
Console.WriteLine(@"----------------------------------------------------------------------------");

try
{
    using var csSearcher = new ManagementObjectSearcher("SELECT * FROM Win32_ComputerSystem");
    foreach (var obj in csSearcher.Get())
    {
        Console.WriteLine($"  Manufacturer: {obj["Manufacturer"]}");
        Console.WriteLine($"  Model: {obj["Model"]}");
        Console.WriteLine($"  System Type: {obj["SystemType"]}");
    }
}
catch (Exception ex) { LogError(ex); }

try
{
    using var biosSearcher = new ManagementObjectSearcher("SELECT * FROM Win32_BIOS");
    foreach (var obj in biosSearcher.Get())
    {
        Console.WriteLine($"  BIOS Version: {obj["SMBIOSBIOSVersion"]}");
        Console.WriteLine($"  BIOS Release: {obj["ReleaseDate"]}");
    }
}
catch (Exception ex) { LogError(ex); }

try
{
    using var skuSearcher = new ManagementObjectSearcher("SELECT * FROM Win32_ComputerSystemProduct");
    foreach (var obj in skuSearcher.Get())
    {
        Console.WriteLine($"  SKU: {obj["Version"]}");
    }
}
catch (Exception ex) { LogError(ex); }

try
{
    using var osSearcher = new ManagementObjectSearcher("SELECT * FROM Win32_OperatingSystem");
    foreach (var obj in osSearcher.Get())
    {
        Console.WriteLine($"  OS: {obj["Caption"]}");
        Console.WriteLine($"  Build: {obj["BuildNumber"]}");
    }
}
catch (Exception ex) { LogError(ex); }

// Section 2: GameZone WMI
Console.WriteLine();
Console.WriteLine(@">>> Section 2: GameZone WMI");
Console.WriteLine(@"----------------------------------------------------------------------------");

try
{
    var exists = await WMI.LenovoGameZoneData.ExistsAsync().ConfigureAwait(false);
    if (exists)
    {
        LogSuccess("  LENOVO_GAMEZONE_DATA: Available");
    }
    else
    {
        LogWarning("  LENOVO_GAMEZONE_DATA: Not Available");
    }
}
catch (Exception ex) { LogError(ex); }

try
{
    var smartFan = await WMI.LenovoGameZoneData.IsSupportSmartFanAsync().ConfigureAwait(false);
    Console.WriteLine($"  Smart Fan Supported: {smartFan}");
}
catch (Exception ex) { LogError(ex); }

try
{
    var gsync = await WMI.LenovoGameZoneData.IsSupportGSyncAsync().ConfigureAwait(false);
    Console.WriteLine($"  G-Sync Support Value: {gsync}");
}
catch (Exception ex) { LogError(ex); }

try
{
    var igpu = await WMI.LenovoGameZoneData.IsSupportIGPUModeAsync().ConfigureAwait(false);
    Console.WriteLine($"  iGPU Mode Support Value: {igpu}");
}
catch (Exception ex) { LogError(ex); }

// Section 3: Supported Capabilities (LENOVO_CAPABILITY_DATA_00)
Console.WriteLine();
Console.WriteLine(@">>> Section 3: Supported Capabilities");
Console.WriteLine(@"----------------------------------------------------------------------------");

HashSet<CapabilityID> supportedCapabilities = [];
try
{
    var capabilities = await WMI.LenovoCapabilityData00.ReadAsync().ConfigureAwait(false);
    supportedCapabilities = capabilities.ToHashSet();

    if (supportedCapabilities.Count > 0)
    {
        LogSuccess($"  LENOVO_CAPABILITY_DATA_00: {supportedCapabilities.Count} capabilities registered");

        var knownCaps = supportedCapabilities
            .Where(c => Enum.IsDefined(typeof(CapabilityID), c))
            .OrderBy(c => c.ToString());
        var unknownCaps = supportedCapabilities
            .Where(c => !Enum.IsDefined(typeof(CapabilityID), c))
            .OrderBy(c => (int)c);

        Console.WriteLine("  Known:");
        foreach (var cap in knownCaps)
        {
            Console.WriteLine($"    - {cap}");
        }

        if (unknownCaps.Any())
        {
            Console.WriteLine($"  Unknown ({unknownCaps.Count()}):");
            foreach (var cap in unknownCaps)
            {
                Console.WriteLine($"    - 0x{(int)cap:X8}");
            }
        }
    }
    else
    {
        LogWarning("  LENOVO_CAPABILITY_DATA_00: No capabilities found");
    }
}
catch (ManagementException)
{
    LogWarning("  LENOVO_CAPABILITY_DATA_00: WMI class not available (older model?)");
}
catch (Exception ex) { LogError(ex); }

// God Mode Settings (LENOVO_CAPABILITY_DATA_01)
try
{
    var rangeCapabilities = await WMI.LenovoCapabilityData01.ReadAsync().ConfigureAwait(false);
    var rangeList = rangeCapabilities.ToList();

    if (rangeList.Count > 0)
    {
        Console.WriteLine();
        LogSuccess($"  LENOVO_CAPABILITY_DATA_01: {rangeList.Count} God Mode settings");
        foreach (var cap in rangeList.OrderBy(c => c.Id.ToString()))
        {
            var name = Enum.IsDefined(typeof(CapabilityID), cap.Id)
                ? cap.Id.ToString()
                : $"0x{(int)cap.Id:X8}";
            Console.WriteLine($"    - {name}: [{cap.Min}-{cap.Max}] step={cap.Step} default={cap.DefaultValue}");
            supportedCapabilities.Add(cap.Id);
        }
    }
}
catch (ManagementException)
{
    LogWarning("  LENOVO_CAPABILITY_DATA_01: Not available (no God Mode support)");
}
catch (Exception ex) { LogError(ex); }

// Discrete Settings (LENOVO_DISCRETE_DATA)
try
{
    var discreteCapabilities = await WMI.LenovoDiscreteData.ReadAsync().ConfigureAwait(false);
    var discreteList = discreteCapabilities.ToList();

    if (discreteList.Count > 0)
    {
        Console.WriteLine();
        LogSuccess($"  LENOVO_DISCRETE_DATA: {discreteList.Count} Discrete settings (grouped)");

        var grouped = discreteList
            .GroupBy(c => c.Id)
            .OrderBy(g => g.Key.ToString());

        foreach (var group in grouped)
        {
            var name = Enum.IsDefined(typeof(CapabilityID), group.Key)
                ? group.Key.ToString()
                : $"0x{(int)group.Key:X8}";

            var values = string.Join(", ", group.Select(g => g.Value).OrderBy(v => v));
            Console.WriteLine($"    - {name}: [{values}]");

            supportedCapabilities.Add(group.Key);
        }
    }
}
catch (ManagementException)
{
    LogWarning("  LENOVO_DISCRETE_DATA: Not available");
}
catch (Exception ex) { LogError(ex); }

// Section 4: Power Modes
Console.WriteLine();
Console.WriteLine(@">>> Section 4: Power Modes");
Console.WriteLine(@"----------------------------------------------------------------------------");

await TryGetCapability(CapabilityID.SupportedPowerModes, "Supported Power Modes", supportedCapabilities);

try
{
    var result = await WMI.LenovoOtherMethod.GetSupportThermalModeAsync().ConfigureAwait(false);
    LogSuccess(@$"  Supported Thermal Modes: {result}");
}
catch (ManagementException)
{
    LogWarning("  Supported Thermal Modes: Not Available");
}
catch (Exception ex) { LogError(ex); }

// Section 5: GPU & Display
Console.WriteLine();
Console.WriteLine(@">>> Section 5: GPU & Display");
Console.WriteLine(@"----------------------------------------------------------------------------");

await TryGetCapability(CapabilityID.IGPUMode, "iGPU Mode", supportedCapabilities);
await TryGetCapability(CapabilityID.IGPUModeChangeStatus, "iGPU Mode Change Status", supportedCapabilities);
await TryGetCapability(CapabilityID.GPUStatus, "GPU Status", supportedCapabilities);
await TryGetCapability(CapabilityID.GPUDidVid, "GPU DID/VID", supportedCapabilities);
await TryGetCapability(CapabilityID.NvidiaGPUDynamicDisplaySwitching, "Nvidia Dynamic Display Switching", supportedCapabilities);
await TryGetCapability(CapabilityID.OverDrive, "OverDrive", supportedCapabilities);
await TryGetCapability(CapabilityID.AutoSwitchRefreshRate, "Auto Switch Refresh Rate", supportedCapabilities);

// Section 6: CPU & GPU Power Limits
Console.WriteLine();
Console.WriteLine(@">>> Section 6: CPU & GPU Power Limits");
Console.WriteLine(@"----------------------------------------------------------------------------");

await TryGetCapability(CapabilityID.CPUShortTermPowerLimit, "CPU PL1", supportedCapabilities);
await TryGetCapability(CapabilityID.CPULongTermPowerLimit, "CPU PL2", supportedCapabilities);
await TryGetCapability(CapabilityID.CPUPeakPowerLimit, "CPU Peak Power Limit", supportedCapabilities);
await TryGetCapability(CapabilityID.CPUTemperatureLimit, "CPU Temperature Limit", supportedCapabilities);
await TryGetCapability(CapabilityID.APUsPPTPowerLimit, "APU sPPT Power Limit", supportedCapabilities);
await TryGetCapability(CapabilityID.CPUCrossLoadingPowerLimit, "CPU Cross Loading Power Limit", supportedCapabilities);
await TryGetCapability(CapabilityID.CPUPL2Tau, "CPU PL2 Tau", supportedCapabilities);
await TryGetCapability(CapabilityID.CPUOverclockingEnable, "CPU Overclocking Enable", supportedCapabilities);
await TryGetCapability(CapabilityID.GPUPowerBoost, "GPU Power Boost", supportedCapabilities);
await TryGetCapability(CapabilityID.GPUConfigurableTGP, "GPU Configurable TGP", supportedCapabilities);
await TryGetCapability(CapabilityID.GPUTemperatureLimit, "GPU Temperature Limit", supportedCapabilities);
await TryGetCapability(CapabilityID.GPUTotalProcessingPowerTargetOnAcOffsetFromBaseline, "GPU TPP Target AC Offset", supportedCapabilities);
await TryGetCapability(CapabilityID.GPUToCPUDynamicBoost, "GPU to CPU Dynamic Boost", supportedCapabilities);

// Section 7: Default Power Limits (LENOVO_DEFAULT_VALUE_IN_DIFFERENT_MODE_DATA)
Console.WriteLine();
Console.WriteLine(@">>> Section 7: Default Power Limits");
Console.WriteLine(@"----------------------------------------------------------------------------");

try
{
    var defaults = await WMI.LenovoDefaultValueInDifferentModeData.ReadAsync().ConfigureAwait(false);
    var defaultList = defaults.ToList();

    if (defaultList.Count > 0)
    {
        LogSuccess($"  LENOVO_DEFAULT_VALUE_IN_DIFFERENT_MODE_DATA: Available");
        foreach (var data in defaultList.OrderBy(d => d.Mode))
        {
            var modeName = data.Mode switch
            {
                0 => "Quiet",
                1 => "Balance",
                2 => "Performance",
                3 => "Extreme",
                _ => $"Mode {data.Mode}"
            };
            Console.WriteLine($"  [{modeName}]");
            Console.WriteLine($"    PL1: {data.CPUShortTermPowerLimit}, PL2: {data.CPULongTermPowerLimit}, CrossLoad: {data.CPUCrossLoadingPowerLimit}");
            Console.WriteLine($"    GPU TGP: {data.GPUConfigurableTGP}, Boost: {data.GPUPowerBoost}");
            Console.WriteLine($"    Temp Limits - CPU: {data.CPUTemperatureLimit}, GPU: {data.GPUTemperatureLimit}");
        }
    }
    else
    {
        LogWarning("  LENOVO_DEFAULT_VALUE_IN_DIFFERENT_MODE_DATA: No data returned");
    }
}
catch (Exception)
{
    LogWarning("  LENOVO_DEFAULT_VALUE_IN_DIFFERENT_MODE_DATA: Not Available");
}

// Section 8: Fan Table
Console.WriteLine();
Console.WriteLine(@">>> Section 8: Fan Table");
Console.WriteLine(@"----------------------------------------------------------------------------");

try
{
    var data = await WMI.LenovoFanTableData.ReadAsync().ConfigureAwait(false);
    var fanTableData = data
        .Where(d => d.mode == 255)
        .Select(d =>
        {
            var type = (d.fanId, d.sensorId) switch
            {
                (1, 1) or (1, 4) => FanTableType.CPU,
                (2, 5) => FanTableType.GPU,
                (4, 4) or (5, 5) or (4, 1) => FanTableType.PCH,
                _ => FanTableType.Unknown,
            };
            return new FanTableData(type, d.fanId, d.sensorId, d.fanTableData, d.sensorTableData);
        })
        .ToArray();

    if (fanTableData.Length == 0)
    {
        LogWarning(@"  No Fan Table data found for custom mode.");
    }
    else
    {
        foreach (var item in fanTableData)
        {
            Console.WriteLine(@$"  Type: {item.Type,-8} | FanId: {item.FanId} | SensorId: {item.SensorId}");
            Console.WriteLine(@$"  FanSpeeds: [{string.Join(", ", item.FanSpeeds)}]");
            Console.WriteLine(@$"  Temps:     [{string.Join(", ", item.Temps)}]");
            Console.WriteLine();
        }
    }
}
catch (Exception ex) { LogError(ex); }

// Section 9: Fan Control & Sensors
Console.WriteLine(@">>> Section 9: Fan Control & Sensors");
Console.WriteLine(@"----------------------------------------------------------------------------");

await TryGetCapability(CapabilityID.FanFullSpeed, "Fan Full Speed", supportedCapabilities);
await TryGetCapability(CapabilityID.CpuCurrentFanSpeed, "CPU Current Fan Speed", supportedCapabilities);
await TryGetCapability(CapabilityID.GpuCurrentFanSpeed, "GPU Current Fan Speed", supportedCapabilities);
await TryGetCapability(CapabilityID.PchCurrentFanSpeed, "PCH Current Fan Speed", supportedCapabilities);
await TryGetCapability(CapabilityID.CpuCurrentTemperature, "CPU Current Temperature", supportedCapabilities);
await TryGetCapability(CapabilityID.GpuCurrentTemperature, "GPU Current Temperature", supportedCapabilities);
await TryGetCapability(CapabilityID.PchCurrentTemperature, "PCH Current Temperature", supportedCapabilities);

// Section 10: Battery
Console.WriteLine();
Console.WriteLine(@">>> Section 10: Battery");
Console.WriteLine(@"----------------------------------------------------------------------------");

try
{
    using var batterySearcher = new ManagementObjectSearcher("SELECT * FROM Win32_Battery");
    var batteries = batterySearcher.Get();
    var batteryCount = 0;
    foreach (var battery in batteries)
    {
        batteryCount++;
        Console.WriteLine($"  Battery Name: {battery["Name"]}");
        Console.WriteLine($"  Status: {battery["Status"]}");
        Console.WriteLine($"  Estimated Charge: {battery["EstimatedChargeRemaining"]}%");
    }
    if (batteryCount == 0)
    {
        LogWarning("  No battery detected");
    }
}
catch (Exception ex) { LogError(ex); }

// Section 11: Lighting
Console.WriteLine();
Console.WriteLine(@">>> Section 11: Lighting");
Console.WriteLine(@"----------------------------------------------------------------------------");

try
{
    using var lightingSearcher = new ManagementObjectSearcher(@"root\WMI", "SELECT * FROM LENOVO_LIGHTING_DATA");
    var lightingItems = lightingSearcher.Get();
    var lightingCount = 0;

    foreach (var item in lightingItems)
    {
        lightingCount++;
        var lightingId = item["Lighting_ID"];
        var controlInterface = item["Control_Interface"];
        var lightingType = item["Lighting_Type"];
        LogSuccess($"  Zone: ID={lightingId}, Interface={controlInterface}, Type={lightingType}");
    }

    if (lightingCount == 0)
        Console.WriteLine("  LENOVO_LIGHTING_DATA: No lighting zones found");
    else
        Console.WriteLine($"  Total: {lightingCount} lighting zone(s)");
}
catch (ManagementException)
{
    Console.WriteLine("  LENOVO_LIGHTING_DATA: WMI class not available");
}
catch (Exception ex) { LogError(ex); }

// Section 12: HID Devices
Console.WriteLine();
Console.WriteLine(@">>> Section 12: HID Devices");
Console.WriteLine(@"----------------------------------------------------------------------------");

try
{
    using var searcher = new ManagementObjectSearcher(@"Select * From Win32_PnPEntity Where DeviceID Like 'HID%'");
    var found = false;

    foreach (var device in searcher.Get())
    {
        var deviceID = device["DeviceID"]?.ToString() ?? "";

        if (deviceID.Contains("17EF", StringComparison.OrdinalIgnoreCase) ||
            deviceID.Contains("048D", StringComparison.OrdinalIgnoreCase))
        {
            found = true;
            var name = device["Name"]?.ToString() ?? "Unknown HID Device";
            var status = device["Status"]?.ToString() ?? "Unknown";

            Console.WriteLine(@$"  [Device]: {name}");
            Console.WriteLine(@$"   [ID]:     {deviceID}");
            Console.WriteLine(@$"   [Status]: {status}");
            Console.WriteLine(new string('-', 60));
        }
    }

    if (!found)
    {
        LogWarning(@"  No Lenovo HID devices found.");
    }
}
catch (Exception ex) { LogError(ex); }

// Section 13: Additional Features
Console.WriteLine(@">>> Section 13: Additional Features");
Console.WriteLine(@"----------------------------------------------------------------------------");

await TryGetCapability(CapabilityID.FlipToStart, "Flip To Start", supportedCapabilities);
await TryGetCapability(CapabilityID.InstantBootAc, "Instant Boot (AC)", supportedCapabilities);
await TryGetCapability(CapabilityID.InstantBootUsbPowerDelivery, "Instant Boot (USB PD)", supportedCapabilities);
await TryGetCapability(CapabilityID.AIChip, "AI Chip", supportedCapabilities);
await TryGetCapability(CapabilityID.GodModeFnQSwitchable, "God Mode Fn+Q Switchable", supportedCapabilities);
await TryGetCapability(CapabilityID.LegionZoneSupportVersion, "Legion Zone Support Version", supportedCapabilities);
await TryGetCapability(CapabilityID.AMDSmartShiftMode, "AMD Smart Shift Mode", supportedCapabilities);
await TryGetCapability(CapabilityID.AMDSkinTemperatureTracking, "AMD Skin Temperature Tracking", supportedCapabilities);

// Section 14: Intelligent Apps (LENOVO_INTELLIGENT_OP_LIST)
Console.WriteLine();
Console.WriteLine(@">>> Section 14: Intelligent Apps");
Console.WriteLine(@"----------------------------------------------------------------------------");

try
{
    var apps = await WMI.LenovoIntelligentOPList.ReadAsync().ConfigureAwait(false);

    if (apps.Count > 0)
    {
        LogSuccess($"  LENOVO_INTELLIGENT_OP_LIST: {apps.Count} apps registered");
        foreach (var app in apps.OrderBy(a => a.Key))
        {
            Console.WriteLine($"    - {app.Key} (Mode: {app.Value})");
        }
    }
    else
    {
        LogWarning("  LENOVO_INTELLIGENT_OP_LIST: No apps found");
    }
}
catch (ManagementException)
{
    LogWarning("  LENOVO_INTELLIGENT_OP_LIST: Not available");
}
catch (Exception ex) { LogError(ex); }

// Summary
Console.WriteLine();
Console.WriteLine(@"============================================================================");
Console.WriteLine(@"Scan Complete!");
Console.WriteLine(@"============================================================================");
Console.WriteLine(@"Green = Supported/Available, Yellow = Not Registered/Info, Red = Error/Not Available");
Console.WriteLine();
Console.WriteLine(@"Press any key to exit...");
Console.ReadKey();
