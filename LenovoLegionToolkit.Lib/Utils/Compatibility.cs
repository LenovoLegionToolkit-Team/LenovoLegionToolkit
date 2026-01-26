using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.System.Power;
using LenovoLegionToolkit.Lib.Controllers.GodMode;
using LenovoLegionToolkit.Lib.Controllers.Sensors;
using LenovoLegionToolkit.Lib.Extensions;
using LenovoLegionToolkit.Lib.System;
using LenovoLegionToolkit.Lib.System.Management;
using Newtonsoft.Json;

// ReSharper disable StringLiteralTypo

namespace LenovoLegionToolkit.Lib.Utils;

public static partial class Compatibility
{

    [GeneratedRegex("^[A-Z0-9]{4}")]
    private static partial Regex BiosPrefixRegex();

    [GeneratedRegex("[0-9]{2}")]
    private static partial Regex BiosVersionRegex();

    private const string ALLOWED_VENDOR = "LENOVO";
    private static readonly string FakeMachineInformationPath = Path.Combine(Folders.AppData, "fake_mi.json");
    public static bool FakeMachineInformationMode { get; private set; } = false;

    private static readonly string[] AllowedModelsPrefix = [
        // Legion Go
        "8APU1",

        // Worldwide variants
        "17ACH",
        "17ARH",
        "17ITH",
        "17IMH",

        "16ACH",
        "16ADR",
        "16AFR",
        "16AHP",
        "16APH",
        "16ARH",
        "16ARP",
        "16ARX",
        "16IAH",
        "16IAX",
        "16IMH",
        "16IRH",
        "16IRX",
        "16ITH",

        "18IAX",
        "NX",

        "15ACH",
        "15AHP",
        "15AKP",
        "15APH",
        "15ARH",
        "15ARP",
        "15IAH",
        "15IAX",
        "15IHU",
        "15IMH",
        "15IRH",
        "15IRX",
        "15ITH",

        "14APH",
        "14IRP",
        "14AKP",

        // Chinese variants
        "G5000",
        "R9000",
        "R7000",
        "Y9000",
        "Y7000",
            
        // Limited compatibility
        "17IR",
        "15IR",
        "15IC",
        "15IK",

        // ThinkBooks
        "ThinkBook"
    ];

    private static readonly Dictionary<string, LegionSeries> MachineTypeMap = new()
    {
        { "83F0", LegionSeries.Legion_5 }, { "83F1", LegionSeries.Legion_5 }, { "83M0", LegionSeries.Legion_5 },
        { "83NX", LegionSeries.Legion_5 }, { "83N2", LegionSeries.Legion_5 }, { "83LY", LegionSeries.Legion_5 },
        { "83DG", LegionSeries.Legion_5 }, { "83EW", LegionSeries.Legion_5 }, { "83EG", LegionSeries.Legion_5 },
        { "83JJ", LegionSeries.Legion_5 }, { "82RC", LegionSeries.Legion_5 }, { "82RB", LegionSeries.Legion_5 },
        { "82TB", LegionSeries.Legion_5 }, { "83EF", LegionSeries.Legion_5 }, { "82RE", LegionSeries.Legion_5 },
        { "82RD", LegionSeries.Legion_5 },

        { "83DH", LegionSeries.Legion_Slim_5 }, { "83EX", LegionSeries.Legion_Slim_5 }, { "82Y5", LegionSeries.Legion_Slim_5 },
        { "82Y9", LegionSeries.Legion_Slim_5 }, { "82YA", LegionSeries.Legion_Slim_5 }, { "83D6", LegionSeries.Legion_Slim_5 },

        { "83LT", LegionSeries.Legion_Pro_5 }, { "83F3", LegionSeries.Legion_Pro_5 }, { "83DF", LegionSeries.Legion_Pro_5 },
        { "83F2", LegionSeries.Legion_Pro_5 }, { "83LU", LegionSeries.Legion_Pro_5 }, { "82WM", LegionSeries.Legion_Pro_5 },
        { "83NN", LegionSeries.Legion_Pro_5 }, { "82WK", LegionSeries.Legion_Pro_5 }, { "82JQ", LegionSeries.Legion_Pro_5},

        { "83KY", LegionSeries.Legion_7 }, { "83FD", LegionSeries.Legion_7 }, { "82UH", LegionSeries.Legion_7 },
        { "82TD", LegionSeries.Legion_7 }, { "82N6", LegionSeries.Legion_7 },

        { "83RU", LegionSeries.Legion_Pro_7 }, { "83F5", LegionSeries.Legion_Pro_7 }, { "83DE", LegionSeries.Legion_Pro_7 },
        { "82WR", LegionSeries.Legion_Pro_7 }, { "82WQ", LegionSeries.Legion_Pro_7 }, { "82WS", LegionSeries.Legion_Pro_7 },

        { "83G0", LegionSeries.Legion_9 }, { "83EY", LegionSeries.Legion_9 },

        { "83E1", LegionSeries.Legion_Go }
    };

    private static readonly (string Keyword, LegionSeries Series)[] ModelKeywordMap =
    [
        ("LOQ", LegionSeries.LOQ),
        ("IdeaPad Gaming", LegionSeries.IdeaPad_Gaming),
        ("IdeaPad", LegionSeries.IdeaPad),
        ("YOGA", LegionSeries.YOGA),
        ("Lenovo Slim", LegionSeries.Lenovo_Slim),
        ("ThinkBook", LegionSeries.ThinkBook)
    ];

    private static MachineInformation? _machineInformation;
    private static FakeMachineInformation? _fakeMachineInformation;

    public static Task<bool> CheckBasicCompatibilityAsync() => WMI.LenovoGameZoneData.ExistsAsync();

    public static async Task<(bool isCompatible, MachineInformation machineInformation)> IsCompatibleAsync()
    {
        var mi = await GetMachineInformationAsync().ConfigureAwait(false);

        if (!await CheckBasicCompatibilityAsync().ConfigureAwait(false) || !mi.Vendor.Equals(ALLOWED_VENDOR, StringComparison.InvariantCultureIgnoreCase))
            return (false, mi);

        if (!File.Exists(FakeMachineInformationPath))
            return AllowedModelsPrefix.Any(allowedModel =>
                mi.Model.Contains(allowedModel, StringComparison.InvariantCultureIgnoreCase))
                ? (true, mi)
                : (false, mi);

        var jsonString = await File.ReadAllTextAsync(FakeMachineInformationPath).ConfigureAwait(false);
        _fakeMachineInformation = JsonConvert.DeserializeObject<FakeMachineInformation>(jsonString);
        FakeMachineInformationMode = true;

        return AllowedModelsPrefix.Any(allowedModel => mi.Model.Contains(allowedModel, StringComparison.InvariantCultureIgnoreCase)) ? (true, mi) : (false, mi);
    }

    public static Task<FakeMachineInformation?> GetFakeMachineInformationAsync()
    {
        return Task.FromResult(_fakeMachineInformation);
    }

    public static async Task<MachineInformation> GetMachineInformationAsync()
    {
        if (_machineInformation != null) return _machineInformation.Value;

        var (vendor, machineType, model, serialNumber) = await GetModelDataAsync().ConfigureAwait(false);
        var generation = GetMachineGeneration(model);
        var legionSeries = GetLegionSeries(model, machineType);
        var (biosVersion, biosVersionRaw) = GetBIOSVersion();
        var supportedPowerModes = (await GetSupportedPowerModesAsync().ConfigureAwait(false)).ToArray();
        var smartFanVersion = await GetSmartFanVersionAsync().ConfigureAwait(false);
        var legionZoneVersion = await GetLegionZoneVersionAsync().ConfigureAwait(false);
        var features = await GetFeaturesAsync().ConfigureAwait(false);

        var machineInformation = new MachineInformation
        {
            Generation = generation,
            LegionSeries = legionSeries,
            Vendor = vendor,
            MachineType = machineType,
            Model = model,
            SerialNumber = serialNumber,
            BiosVersion = biosVersion,
            BiosVersionRaw = biosVersionRaw,
            SupportedPowerModes = supportedPowerModes,
            SmartFanVersion = smartFanVersion,
            LegionZoneVersion = legionZoneVersion,
            Features = features,
            Properties = new()
            {
                SupportsAlwaysOnAc = GetAlwaysOnAcStatus(),
                SupportsExtremeMode = GetSupportsExtremeMode(supportedPowerModes, smartFanVersion, legionZoneVersion),
                SupportsGodModeV1 = GetSupportsGodModeV1(supportedPowerModes, smartFanVersion, legionZoneVersion, biosVersion),
                SupportsGodModeV2 = GetSupportsGodModeV2(supportedPowerModes, smartFanVersion, legionZoneVersion),
                SupportsGodModeV3 = GetSupportsGodModeV3(supportedPowerModes, smartFanVersion, legionZoneVersion, generation, model, machineType),
                SupportsGodModeV4 = GetSupportsGodModeV4(supportedPowerModes, smartFanVersion, legionZoneVersion),
                SupportsGSync = await GetSupportsGSyncAsync().ConfigureAwait(false),
                SupportsIGPUMode = await GetSupportsIGPUModeAsync().ConfigureAwait(false),
                SupportsAIMode = await GetSupportsAIModeAsync().ConfigureAwait(false),
                SupportsBootLogoChange = GetSupportBootLogoChange(),
                SupportsITSMode = GetSupportITSMode(model),
                HasQuietToPerformanceModeSwitchingBug = GetHasQuietToPerformanceModeSwitchingBug(biosVersion),
                HasGodModeToOtherModeSwitchingBug = GetHasGodModeToOtherModeSwitchingBug(biosVersion),
                HasReapplyParameterIssue = GetHasReapplyParameterIssue(model, machineType),
                HasSpectrumProfileSwitchingBug = GetHasSpectrumProfileSwitchingBug(model, machineType),
                IsExcludedFromLenovoLighting = GetIsExcludedFromLenovoLighting(biosVersion, generation, legionSeries),
                IsExcludedFromPanelLogoLenovoLighting = GetIsExcludedFromPanelLenovoLighting(machineType, model),
                HasAlternativeFullSpectrumLayout = GetHasAlternativeFullSpectrumLayout(machineType),
                IsAmdDevice = GetIsAmdDevice(model),
                IsChineseModel = GetIsChineseModel(model),
            }
        };

        _machineInformation = machineInformation;
        return _machineInformation.Value;
    }


    private static Task<(string, string, string, string)> GetModelDataAsync() => WMI.Win32.ComputerSystemProduct.ReadAsync();

    private static (BiosVersion?, string?) GetBIOSVersion()
    {
        var registryValue = Registry.GetValue("HKEY_LOCAL_MACHINE", "HARDWARE\\DESCRIPTION\\System\\BIOS", "BIOSVersion", string.Empty);
        var result = registryValue?.ToString()?.Trim() ?? string.Empty;

        var prefixRegex = BiosPrefixRegex();
        var versionRegex = BiosVersionRegex();

        var prefix = prefixRegex.Match(result).Value;
        var versionString = versionRegex.Match(result).Value;

        if (!int.TryParse(versionString, out var version))
        {
            return (null, null);
        }

        return (new BiosVersion(prefix, version), result);
    }

    private static bool GetIsChineseModel(string model)
    {
        string[] chineseModelIndicators = [
            "R7000",
            "R9000",
            "Y7000",
            "Y9000"
        ];
        return chineseModelIndicators.Any(model.Contains);
    }

    private static bool GetIsAmdDevice(string model)
    {
        if (string.IsNullOrEmpty(model)) return false;

        var regex = new Regex(@"(?<platform>[AI])[A-Z]{2}\d+", RegexOptions.RightToLeft);

        var match = regex.Match(model.ToUpperInvariant());

        if (!match.Success)
        {
            return false;
        }

        string platform = match.Groups["platform"].Value;
        return platform == "A";
    }

    private static async Task<MachineInformation.FeatureData> GetFeaturesAsync()
    {
        try
        {
            var capabilities = await WMI.LenovoCapabilityData00.ReadAsync().ConfigureAwait(false);
            return new(MachineInformation.FeatureData.SourceType.CapabilityData, capabilities);
        }
        catch { /* Ignored. */ }

        try
        {
            var featureFlags = await WMI.LenovoOtherMethod.GetLegionDeviceSupportFeatureAsync().ConfigureAwait(false);

            return new(MachineInformation.FeatureData.SourceType.Flags)
            {
                [CapabilityID.IGPUMode] = featureFlags.IsBitSet(0),
                [CapabilityID.NvidiaGPUDynamicDisplaySwitching] = featureFlags.IsBitSet(4),
                [CapabilityID.InstantBootAc] = featureFlags.IsBitSet(5),
                [CapabilityID.InstantBootUsbPowerDelivery] = featureFlags.IsBitSet(6),
                [CapabilityID.AMDSmartShiftMode] = featureFlags.IsBitSet(7),
                [CapabilityID.AMDSkinTemperatureTracking] = featureFlags.IsBitSet(8),
                [CapabilityID.FlipToStart] = true,
                [CapabilityID.OverDrive] = true
            };
        }
        catch { /* Ignored. */ }

        return MachineInformation.FeatureData.Unknown;
    }

    private static async Task<IEnumerable<PowerModeState>> GetSupportedPowerModesAsync()
    {
        try
        {
            var powerModes = new List<PowerModeState>();

            var value = await WMI.LenovoOtherMethod.GetFeatureValueAsync(CapabilityID.SupportedPowerModes).ConfigureAwait(false);

            // 0    Quiet
            // 1    Balance
            // 2    Performance
            // 3    Extreme
            // 16   Custom

            if (value.IsBitSet(0))
                powerModes.Add(PowerModeState.Quiet);
            if (value.IsBitSet(1))
                powerModes.Add(PowerModeState.Balance);
            if (value.IsBitSet(2))
                powerModes.Add(PowerModeState.Performance);
            if (value.IsBitSet(3))
                powerModes.Add(PowerModeState.Extreme);
            if (value.IsBitSet(16))
                powerModes.Add(PowerModeState.GodMode);

            return powerModes;
        }
        catch { /* Ignored. */ }

        try
        {
            var powerModes = new List<PowerModeState>();

            var result = await WMI.LenovoOtherMethod.GetSupportThermalModeAsync().ConfigureAwait(false);

            // 0    Quiet
            // 1    Balance
            // 2    Performance
            // 3    Extreme
            // 16   Custom

            if (result.IsBitSet(0))
                powerModes.Add(PowerModeState.Quiet);
            if (result.IsBitSet(1))
                powerModes.Add(PowerModeState.Balance);
            if (result.IsBitSet(2))
                powerModes.Add(PowerModeState.Performance);
            if (result.IsBitSet(3))
                powerModes.Add(PowerModeState.Extreme);
            if (result.IsBitSet(16))
                powerModes.Add(PowerModeState.GodMode);

            return powerModes;
        }
        catch { /* Ignored. */ }

        return [];
    }

    private static async Task<int> GetSmartFanVersionAsync()
    {
        try
        {
            return await WMI.LenovoGameZoneData.IsSupportSmartFanAsync().ConfigureAwait(false);
        }
        catch { /* Ignored. */ }

        return -1;
    }

    private static async Task<int> GetLegionZoneVersionAsync()
    {
        try
        {
            return await WMI.LenovoOtherMethod.GetFeatureValueAsync(CapabilityID.LegionZoneSupportVersion).ConfigureAwait(false);
        }
        catch { /* Ignored. */ }

        try
        {
            return await WMI.LenovoOtherMethod.GetSupportLegionZoneVersionAsync().ConfigureAwait(false);
        }
        catch { /* Ignored. */ }

        return -1;
    }

    private static unsafe (bool status, bool connectivity) GetAlwaysOnAcStatus()
    {
        var capabilities = new SYSTEM_POWER_CAPABILITIES();
        var result = PInvoke.CallNtPowerInformation(POWER_INFORMATION_LEVEL.SystemPowerCapabilities,
            null,
            0,
            &capabilities,
            (uint)Marshal.SizeOf<SYSTEM_POWER_CAPABILITIES>());

        if (result.SeverityCode == NTSTATUS.Severity.Success)
            return (false, false);

        return (capabilities.AoAc, capabilities.AoAcConnectivitySupported);
    }

    private static bool GetSupportsExtremeMode(IEnumerable<PowerModeState> supportedPowerModes, int smartFanVersion, int legionZoneVersion)
    {
        if (!supportedPowerModes.Contains(PowerModeState.Extreme))
            return false;

        return smartFanVersion is 6 or 7 or 8 || legionZoneVersion is 3 or 4 or 5;
    }

    private static bool GetSupportsGodModeV1(IEnumerable<PowerModeState> supportedPowerModes, int smartFanVersion, int legionZoneVersion, BiosVersion? biosVersion)
    {
        if (!supportedPowerModes.Contains(PowerModeState.GodMode))
            return false;

        var affectedBiosVersions = new BiosVersion[]
        {
            new("G9CN", 24),
            new("GKCN", 46),
            new("H1CN", 39),
            new("HACN", 31),
            new("HHCN", 20)
        };

        if (affectedBiosVersions.Any(bv => biosVersion?.IsLowerThan(bv) ?? false))
            return false;

        return smartFanVersion is 4 or 5 || legionZoneVersion is 1 or 2;
    }

    private static bool GetSupportsGodModeV2(IEnumerable<PowerModeState> supportedPowerModes, int smartFanVersion, int legionZoneVersion)
    {
        if (!supportedPowerModes.Contains(PowerModeState.GodMode))
            return false;

        return smartFanVersion is 6 or 7 || legionZoneVersion is 3 or 4;
    }

    private static bool GetSupportsGodModeV3(IEnumerable<PowerModeState> supportedPowerModes, int smartFanVersion, int legionZoneVersion, int gen, string model, string machineType)
    {
        if (!supportedPowerModes.Contains(PowerModeState.GodMode))
        {
            return false;
        }

        var affectedSeries = new LegionSeries[]
        {
            LegionSeries.Legion_5,
            LegionSeries.Legion_7
        };

        var affectedModels = new string[]
        {
            "Legion 5", // Y7000P
            "Legion 7", // Y9000X, Not Y9000P.
            "Legion Pro 5 16IAX10H", // Y7000P With RTX 5070TI
            "LOQ",
            "Y7000",
            "R7000"
        };

        var isAffectedSeries = affectedSeries.Any(m => GetLegionSeries(model, machineType) == m);
        var isAffectedModel = affectedModels.Any(model.Contains);
        var isSupportedVersion = smartFanVersion is 8 or 9 || legionZoneVersion is 5 or 6;

        return (isAffectedSeries || isAffectedModel) && isSupportedVersion && gen >= 10;
    }

    private static bool GetSupportsGodModeV4(IEnumerable<PowerModeState> supportedPowerModes, int smartFanVersion, int legionZoneVersion)
    {
        if (!supportedPowerModes.Contains(PowerModeState.GodMode))
            return false;

        // In theory, All models that has denied by GetSupportsGodModeV3() will be supported by GodModeControllerV4.

        return smartFanVersion is 8 or 9 || legionZoneVersion is 5 or 6;
    }


    private static async Task<bool> GetSupportsGSyncAsync()
    {
        try
        {
            return await WMI.LenovoGameZoneData.IsSupportGSyncAsync().ConfigureAwait(false) > 0;
        }
        catch
        {
            return false;
        }
    }

    private static async Task<bool> GetSupportsIGPUModeAsync()
    {
        try
        {
            return await WMI.LenovoGameZoneData.IsSupportIGPUModeAsync().ConfigureAwait(false) > 0;
        }
        catch
        {
            return false;
        }
    }

    private static async Task<bool> GetSupportsAIModeAsync()
    {
        try
        {
            await WMI.LenovoGameZoneData.GetIntelligentSubModeAsync().ConfigureAwait(false);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static bool GetSupportBootLogoChange()
    {
        // I don't know why. Which means every model should support Boot Logo Change.
        // return smartFanVersion < 9;
        return true;
    }

    private static bool GetSupportITSMode(string model)
    {
        var lower = model.ToLowerInvariant();
        if (lower.Contains("IdeaPad Gaming".ToLowerInvariant()))
        {
            return false;
        }
        return lower.Contains("IdeaPad".ToLowerInvariant()) || lower.Contains("ThinkBook".ToLowerInvariant()) || lower.Contains("Lenovo Slim".ToLowerInvariant());
    }

    private static int GetMachineGeneration(string model)
    {
        var platformMatch = Regex.Match(model, @"(?<=[A-Z]{3})(?<gen>\d{1,2})", RegexOptions.IgnoreCase);
        if (platformMatch.Success)
        {
            return int.Parse(platformMatch.Groups["gen"].Value);
        }

        var gMatch = Regex.Match(model, @"g(?<gen>\d+)", RegexOptions.IgnoreCase);
        if (gMatch.Success) return int.Parse(gMatch.Groups["gen"].Value);

        var matches = Regex.Matches(model, @"(?<!\d)\d{1,2}(?!\d)");
        foreach (Match m in matches)
        {
            int val = int.Parse(m.Value);
            if (val >= 14 && val <= 18) continue;
            return val;
        }

        return 0;
    }

    private static LegionSeries GetLegionSeries(string model, string machineType)
    {
        if (MachineTypeMap.TryGetValue(machineType, out var series))
        {
            return series;
        }

        foreach (var (keyword, legionSeries) in ModelKeywordMap)
        {
            if (model.Contains(keyword, StringComparison.OrdinalIgnoreCase))
            {
                return legionSeries;
            }
        }

        return LegionSeries.Unknown;
    }

    private static bool GetHasQuietToPerformanceModeSwitchingBug(BiosVersion? biosVersion)
    {
        var affectedBiosVersions = new BiosVersion[]
        {
            new("J2CN", null)
        };

        return affectedBiosVersions.Any(bv => biosVersion?.IsHigherOrEqualThan(bv) ?? false);
    }

    private static bool GetHasGodModeToOtherModeSwitchingBug(BiosVersion? biosVersion)
    {
        var affectedBiosVersions = new BiosVersion[]
        {
            new("K1CN", null)
        };

        return affectedBiosVersions.Any(bv => biosVersion?.IsHigherOrEqualThan(bv) ?? false);
    }

    private static bool GetHasReapplyParameterIssue(string? machineModel, string machineType)
    {
        if (string.IsNullOrEmpty(machineModel))
        {
            return false;
        }

        var affectedSeries = new LegionSeries[]
        {
            LegionSeries.Legion_5,
            LegionSeries.Legion_7,
            LegionSeries.Legion_9,
        };

        return affectedSeries.Any(series => GetLegionSeries(machineModel, machineType) == series);
    }

    private static bool GetHasSpectrumProfileSwitchingBug(string? machineModel, string machineType)
    {
        if (string.IsNullOrEmpty(machineModel))
        {
            return false;
        }

        var affectedSeries = new LegionSeries[]
        {
            LegionSeries.Legion_5,
            LegionSeries.Legion_Pro_5,
        };

        var affectedModel = new List<string>
        {
            "16IRX10",
            "16IAX10",
            "16IAX10H",
            "15IRX10",
            "15AHP10"
        };

        bool isAffectedModel = affectedModel.Any(m => machineModel.Contains(m, StringComparison.OrdinalIgnoreCase));
        bool isAffectedSeries = affectedSeries.Any(s => GetLegionSeries(machineModel, machineType) == s);

        return isAffectedModel && isAffectedSeries;
    }

    // Legion 7 Gen 6 uses firmware-controlled RGB. I'm trying to add support but no luck. 
    // HID / Lenovo Lighting commands are accepted but ignored by firmware,
    // so keyboard RGB cannot be controlled reliably here.
    private static bool GetIsExcludedFromLenovoLighting(BiosVersion? biosVersion, int generation, LegionSeries series)
    {
        if (series == LegionSeries.Legion_7 && generation == 6)
        {
            if (Log.Instance.IsTraceEnabled)
                Log.Instance.Trace($"Legion 7 Gen 6: keyboard RGB is firmware-controlled, Lenovo Lighting disabled");
            return true;
        }

        var affectedBiosVersions = new BiosVersion[] { new("GKCN", 54) };
        return affectedBiosVersions.Any(bv => biosVersion?.IsLowerThan(bv) ?? false);
    }

    private static bool GetIsExcludedFromPanelLenovoLighting(string machineType, string model)
    {
        (string machineType, string model)[] excludedModels =
        [
            ("82JH", "15ITH6H"),
            ("82JK", "15ITH6"),
            ("82JM", "17ITH6H"),
            ("82JN", "17ITH6"),
            ("82JU", "15ACH6H"),
            ("82JW", "15ACH6"),
            ("82JY", "17ACH6H"),
            ("82K0", "17ACH6"),
            ("82K1", "15IHU6"),
            ("82K2", "15ACH6"),
            ("82NW", "15ACH6A")
        ];

        return excludedModels.Where(m =>
        {
            var result = machineType.Contains(m.machineType);
            result &= model.Contains(m.model);
            return result;
        }).Any();
    }

    private static bool GetHasAlternativeFullSpectrumLayout(string machineType)
    {
        var machineTypes = new[]
        {
            "83G0", // Gen 9
            "83AG"  // Gen 8
        };
        return machineTypes.Contains(machineType);
    }

    public static bool IsLegion(LegionSeries series)
    {
        return series switch
        {
            LegionSeries.Legion_5 => true,
            LegionSeries.Legion_Pro_5 => true,
            LegionSeries.Legion_Slim_5 => true,
            LegionSeries.Legion_7 => true,
            LegionSeries.Legion_Pro_7 => true,
            LegionSeries.Legion_9 => true,
            LegionSeries.Legion_Go => true,
            LegionSeries.LOQ => true,
            _ => false
        };
    }

    public static bool GetIsOverdriverSupported()
    {
        var gen = _machineInformation?.Generation;
        var series = _machineInformation?.LegionSeries;

        return (series is not (LegionSeries.Legion_7 or LegionSeries.Legion_Pro_7)) || !(gen >= 10);
    }

    public static async Task PrintMachineInfoAsync()
    {
        if (!Log.Instance.IsTraceEnabled)
            return;

        var info = await GetMachineInformationAsync().ConfigureAwait(false);

        var sb = new StringBuilder();
        sb.AppendLine("Retrieved machine information:");
        sb.Append(FormatMachineInformation(info));

        Log.Instance.Trace($"{sb}");
    }

    private static string FormatMachineInformation(MachineInformation info)
    {
        var sb = new StringBuilder();
        var properties = typeof(MachineInformation).GetProperties(BindingFlags.Public | BindingFlags.Instance);

        foreach (var prop in properties)
        {
            if (prop.Name == "SerialNumber")
                continue;

            try
            {
                var value = prop.GetValue(info);
                if (value == null)
                {
                    sb.AppendLine($" * {prop.Name}: 'null'");
                    continue;
                }
                var formattedLines = FormatPropertyValue(prop.Name, value, 0);
                foreach (var line in formattedLines)
                {
                    sb.AppendLine(line);
                }
            }
            catch (Exception ex)
            {
                sb.AppendLine($" * {prop.Name}: <Error: {ex.Message}>");
            }
        }

        return sb.ToString();
    }

    private static List<string> FormatPropertyValue(string propertyName, object? value, int indentLevel)
    {
        var lines = new List<string>();
        var indent = new string(' ', indentLevel * 4);
        var prefix = indentLevel == 0 ? " * " : $"    {indent}* ";

        if (value == null)
        {
            lines.Add($"{prefix}{propertyName}: 'null'");
            return lines;
        }

        var type = value.GetType();

        switch (propertyName)
        {
            case "BiosVersion" when value is BiosVersion biosVersion:
                lines.Add($"{prefix}BIOS: 'Prefix: {biosVersion.Prefix}, Version: {biosVersion.Version}'");
                return lines;

            case "SupportedPowerModes" when value is PowerModeState[] powerModes:
                var modes = string.Join(", ", powerModes);
                lines.Add($"{prefix}{propertyName}: '{modes}'");
                return lines;

            case "Features" when value is MachineInformation.FeatureData features:
                var featureStr = features.Source == MachineInformation.FeatureData.SourceType.Unknown
                    ? "Unknown"
                    : $"{features.Source}: {string.Join(", ", features.All)}";
                lines.Add($"{prefix}{propertyName}: {featureStr}");
                return lines;

            case "Properties" when value is MachineInformation.PropertyData properties:
                lines.Add($"{prefix}{propertyName}:");
                var propProperties = typeof(MachineInformation.PropertyData).GetProperties(BindingFlags.Public | BindingFlags.Instance);
                foreach (var prop in propProperties)
                {
                    try
                    {
                        var propValue = prop.GetValue(properties);
                        if (propValue == null)
                        {
                            lines.Add($"    {indent}    * {prop.Name}: 'null'");
                            continue;
                        }
                        var propLines = FormatPropertyValue(prop.Name, propValue, indentLevel + 1);
                        lines.AddRange(propLines);
                    }
                    catch (Exception ex)
                    {
                        lines.Add($"    {indent}    * {prop.Name}: <Error: {ex.Message}>");
                    }
                }
                return lines;
        }

        if (type.FullName?.StartsWith("System.ValueTuple") == true)
        {
            var fields = type.GetFields();
            var tupleValues = fields.Select(f => f.GetValue(value)?.ToString() ?? "null");
            lines.Add($"{prefix}{propertyName}: '({string.Join(", ", tupleValues)})'");
            return lines;
        }

        if (value is IEnumerable enumerable && type != typeof(string))
        {
            var items = enumerable.Cast<object>().ToList();
            if (items.Count == 0)
            {
                lines.Add($"{prefix}{propertyName}: 'None'");
            }
            else
            {
                var itemStr = string.Join(", ", items);
                lines.Add($"{prefix}{propertyName}: '[{itemStr}]'");
            }
            return lines;
        }

        lines.Add($"{prefix}{propertyName}: '{value}'");
        return lines;
    }

    public static async Task PrintControllerVersionAsync()
    {
        if (_machineInformation is
            {
                LegionSeries: LegionSeries.Legion_5 or
                LegionSeries.Legion_Pro_5 or
                LegionSeries.Legion_7 or
                LegionSeries.Legion_Pro_7 or
                LegionSeries.Legion_9
            })
        {
            SensorsController sensorsController = IoCContainer.Resolve<SensorsController>();

            var sensorCtrl = await sensorsController.GetControllerAsync().ConfigureAwait(true);
            var sensorsControllerTypeName = sensorCtrl?.GetType().Name ?? "Null SensorsController or Result";
            Log.Instance.Trace($"Using {sensorsControllerTypeName}");

            GodModeController godModeController = IoCContainer.Resolve<GodModeController>();

            var godModeCtrl = await godModeController.GetControllerAsync().ConfigureAwait(true);
            var godModeControllerTypeName = godModeCtrl?.GetType().Name ?? "Null GodModeController or Result";
            Log.Instance.Trace($"Using {godModeControllerTypeName}");
        }
    }
}
