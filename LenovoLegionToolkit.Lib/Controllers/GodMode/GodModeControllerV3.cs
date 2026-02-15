using LenovoLegionToolkit.Lib.Extensions;
using LenovoLegionToolkit.Lib.Settings;
using LenovoLegionToolkit.Lib.SoftwareDisabler;
using LenovoLegionToolkit.Lib.System.Management;
using LenovoLegionToolkit.Lib.Utils;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Management;
using System.Threading.Tasks;

namespace LenovoLegionToolkit.Lib.Controllers.GodMode;

public class GodModeControllerV3(
    GodModeSettings settings,
    VantageDisabler vantageDisabler,
    LegionZoneDisabler legionZoneDisabler,
    LegionSpaceDisabler legionSpaceDisabler)
    : AbstractGodModeController(settings)
{
    private const uint CAPABILITY_ID_MASK = 0xFFFF00FF;
    private const int BIOS_OC_MODE_ENABLED = 3;

    public override Task<bool> NeedsVantageDisabledAsync() => Task.FromResult(true);
    public override Task<bool> NeedsLegionSpaceDisabledAsync() => Task.FromResult(true);
    public override Task<bool> NeedsLegionZoneDisabledAsync() => Task.FromResult(true);

    public override async Task ApplyStateAsync()
    {
        if (await vantageDisabler.GetStatusAsync().ConfigureAwait(false) == SoftwareStatus.Enabled)
        {
            Log.Instance.Trace($"Can't correctly apply state when Vantage is running.");
            return;
        }

        if (await legionSpaceDisabler.GetStatusAsync().ConfigureAwait(false) == SoftwareStatus.Enabled)
        {
            Log.Instance.Trace($"Can't correctly apply state when Legion Space is running.");
            return;
        }

        if (await legionZoneDisabler.GetStatusAsync().ConfigureAwait(false) == SoftwareStatus.Enabled)
        {
            Log.Instance.Trace($"Can't correctly apply state when Legion Zone is running.");
            return;
        }

        Log.Instance.Trace($"Applying state...");

        var (presetId, preset) = await GetActivePresetAsync().ConfigureAwait(false);
        var isOcEnabled = await IsBiosOcEnabledAsync().ConfigureAwait(false);
        var overclockingData = new Dictionary<CPUOverclockingID, StepperValue?>
        {
            { CPUOverclockingID.PrecisionBoostOverdriveScaler, preset.PrecisionBoostOverdriveScaler },
            { CPUOverclockingID.PrecisionBoostOverdriveBoostFrequency, preset.PrecisionBoostOverdriveBoostFrequency },
            { CPUOverclockingID.AllCoreCurveOptimizer, preset.AllCoreCurveOptimizer },
        };

        var defaultPresets = await GetDefaultsInOtherPowerModesAsync().ConfigureAwait(false);
        var defaultPerformancePreset = defaultPresets.GetValueOrNull(PowerModeState.Extreme);
        var settings = CreateSettingsDictionary(preset, defaultPerformancePreset);

        var failAllowedSettings = new[]
        {
            CapabilityID.GPUPowerBoost,
            CapabilityID.GPUConfigurableTGP,
            CapabilityID.GPUTemperatureLimit,
            CapabilityID.GPUTotalProcessingPowerTargetOnAcOffsetFromBaseline,
            CapabilityID.GPUToCPUDynamicBoost,
        };

        var fanTable = preset.FanTable ?? await GetDefaultFanTableAsync().ConfigureAwait(false);
        var fanFullSpeed = preset.FanFullSpeed ?? false;

        foreach (var (id, value) in settings)
        {
            await ApplySettingWithErrorHandling(id, value, failAllowedSettings.Contains(id)).ConfigureAwait(false);
        }

        await HandleFanSettings(fanTable, fanFullSpeed).ConfigureAwait(false);

        if (isOcEnabled && preset.EnableOverclocking == true)
        {
            await SetCPUOverclockingMode(true).ConfigureAwait(false);
            foreach (var (id, value) in overclockingData)
            {
                if (value.HasValue)
                {
                    Log.Instance.Trace($"Applying {id}: {value}...");
                    await SetOCValueAsync(id, 17, value.Value).ConfigureAwait(false);
                }
            }
        }
        else if (isOcEnabled && preset.EnableOverclocking == false)
        {
            await SetCPUOverclockingMode(false).ConfigureAwait(false);
            Log.Instance.Trace($"Overclocking is disabled.");
        }

        RaisePresetChanged(presetId);
        Log.Instance.Trace($"State applied. [name={preset.Name}, id={presetId}]");
    }

    private async Task ApplySettingWithErrorHandling(CapabilityID id, int? value, bool isFailAllowed)
    {
        if (!value.HasValue) return;

        try
        {
            Log.Instance.Trace($"Applying {id}: {value}...");
            await SetValueAsync(id, value.Value).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Log.Instance.Trace($"Failed to apply {id}. [value={value}]", ex);
            if (!isFailAllowed)
                throw;
        }
    }

    private async Task HandleFanSettings(FanTable fanTable, bool fanFullSpeed)
    {
        if (fanFullSpeed)
        {
            try
            {
                Log.Instance.Trace($"Applying Fan Full Speed {fanFullSpeed}...");
                await SetFanFullSpeedAsync(fanFullSpeed).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Log.Instance.Trace($"Apply failed. [setting=fanFullSpeed]", ex);
                throw;
            }
        }
        else
        {
            try
            {
                Log.Instance.Trace($"Making sure Fan Full Speed is false...");
                await SetFanFullSpeedAsync(false).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Log.Instance.Trace($"Apply failed. [setting=fanFullSpeed]", ex);
                throw;
            }

            try
            {
                Log.Instance.Trace($"Applying Fan Table {fanTable}...");
                if (!await IsValidFanTableAsync(fanTable).ConfigureAwait(false))
                {
                    Log.Instance.Trace($"Fan table invalid, replacing with default...");
                    fanTable = await GetDefaultFanTableAsync().ConfigureAwait(false);
                }
                await SetFanTable(fanTable).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Log.Instance.Trace($"Apply failed. [setting=fanTable]", ex);
                throw;
            }
        }
    }

    private static Dictionary<CapabilityID, int?> CreateSettingsDictionary(
        GodModeSettings.GodModeSettingsStore.Preset preset,
        GodModeDefaults? defaultPerformancePreset)
    {
        return new Dictionary<CapabilityID, int?>
        {
            { CapabilityID.CPULongTermPowerLimit, preset.CPULongTermPowerLimit?.Value ?? defaultPerformancePreset?.CPULongTermPowerLimit },
            { CapabilityID.CPUShortTermPowerLimit, preset.CPUShortTermPowerLimit?.Value ?? defaultPerformancePreset?.CPUShortTermPowerLimit },
            { CapabilityID.CPUPeakPowerLimit, preset.CPUPeakPowerLimit?.Value ?? defaultPerformancePreset?.CPUPeakPowerLimit },
            { CapabilityID.CPUCrossLoadingPowerLimit, preset.CPUCrossLoadingPowerLimit?.Value ?? defaultPerformancePreset?.CPUCrossLoadingPowerLimit },
            { CapabilityID.CPUPL1Tau, preset.CPUPL1Tau?.Value ?? defaultPerformancePreset?.CPUPL1Tau },
            { CapabilityID.APUsPPTPowerLimit, preset.APUsPPTPowerLimit?.Value ?? defaultPerformancePreset?.APUsPPTPowerLimit },
            { CapabilityID.CPUTemperatureLimit, preset.CPUTemperatureLimit?.Value ?? defaultPerformancePreset?.CPUTemperatureLimit },
            { CapabilityID.GPUPowerBoost, preset.GPUPowerBoost?.Value ?? defaultPerformancePreset?.GPUPowerBoost },
            { CapabilityID.GPUConfigurableTGP, preset.GPUConfigurableTGP?.Value ?? defaultPerformancePreset?.GPUConfigurableTGP },
            { CapabilityID.GPUTemperatureLimit, preset.GPUTemperatureLimit?.Value ?? defaultPerformancePreset?.GPUTemperatureLimit },
            { CapabilityID.GPUTotalProcessingPowerTargetOnAcOffsetFromBaseline, preset.GPUTotalProcessingPowerTargetOnAcOffsetFromBaseline?.Value ?? defaultPerformancePreset?.GPUTotalProcessingPowerTargetOnAcOffsetFromBaseline },
            { CapabilityID.GPUToCPUDynamicBoost, preset.GPUToCPUDynamicBoost?.Value ?? defaultPerformancePreset?.GPUToCPUDynamicBoost },
        };
    }

    public override async Task<Dictionary<Guid, GodModeSettings.GodModeSettingsStore.Preset>> GetGodModePresetsAsync()
    {
        return await base.GetGodModePresetsAsync().ConfigureAwait(false);
    }

    public override Task<FanTable> GetMinimumFanTableAsync()
    {
        var fanTable = new FanTable([1, 1, 1, 1, 1, 1, 1, 1, 3, 5]);
        return Task.FromResult(fanTable);
    }

    public override async Task<Dictionary<PowerModeState, GodModeDefaults>> GetDefaultsInOtherPowerModesAsync()
    {
        try
        {
            Log.Instance.Trace($"Getting defaults in other power modes...");
            var result = new Dictionary<PowerModeState, GodModeDefaults>();
            
            // Retry WMI calls up to 5 times with 2s delay (handles post-reboot timing issues)
            RangeCapability[] allCapabilityData = [];
            for (int attempt = 1; attempt <= 5; attempt++)
            {
                try
                {
                    allCapabilityData = (await WMI.LenovoCapabilityData01.ReadAsync().ConfigureAwait(false)).ToArray();
                    break; // Success
                }
                catch (ManagementException ex) when (attempt < 5)
                {
                    Log.Instance.Trace($"WMI read attempt {attempt} failed, retrying in 2s...", ex);
                    await Task.Delay(2000).ConfigureAwait(false);
                }
            }

            foreach (var powerMode in new[] { PowerModeState.Quiet, PowerModeState.Balance, PowerModeState.Performance, PowerModeState.Extreme })
            {
                var defaults = new GodModeDefaults
                {
                    CPULongTermPowerLimit = GetDefaultCapabilityIdValueInPowerMode(allCapabilityData, CapabilityID.CPULongTermPowerLimit, powerMode),
                    CPUShortTermPowerLimit = GetDefaultCapabilityIdValueInPowerMode(allCapabilityData, CapabilityID.CPUShortTermPowerLimit, powerMode),
                    CPUPeakPowerLimit = GetDefaultCapabilityIdValueInPowerMode(allCapabilityData, CapabilityID.CPUPeakPowerLimit, powerMode),
                    CPUCrossLoadingPowerLimit = GetDefaultCapabilityIdValueInPowerMode(allCapabilityData, CapabilityID.CPUCrossLoadingPowerLimit, powerMode),
                    CPUPL1Tau = GetDefaultCapabilityIdValueInPowerMode(allCapabilityData, CapabilityID.CPUPL1Tau, powerMode),
                    APUsPPTPowerLimit = GetDefaultCapabilityIdValueInPowerMode(allCapabilityData, CapabilityID.APUsPPTPowerLimit, powerMode),
                    CPUTemperatureLimit = GetDefaultCapabilityIdValueInPowerMode(allCapabilityData, CapabilityID.CPUTemperatureLimit, powerMode),
                    GPUPowerBoost = GetDefaultCapabilityIdValueInPowerMode(allCapabilityData, CapabilityID.GPUPowerBoost, powerMode),
                    GPUConfigurableTGP = GetDefaultCapabilityIdValueInPowerMode(allCapabilityData, CapabilityID.GPUConfigurableTGP, powerMode),
                    GPUTemperatureLimit = GetDefaultCapabilityIdValueInPowerMode(allCapabilityData, CapabilityID.GPUTemperatureLimit, powerMode),
                    GPUTotalProcessingPowerTargetOnAcOffsetFromBaseline = GetDefaultCapabilityIdValueInPowerMode(allCapabilityData, CapabilityID.GPUTotalProcessingPowerTargetOnAcOffsetFromBaseline, powerMode),
                    GPUToCPUDynamicBoost = GetDefaultCapabilityIdValueInPowerMode(allCapabilityData, CapabilityID.GPUToCPUDynamicBoost, powerMode),
                    FanTable = await GetDefaultFanTableAsync().ConfigureAwait(false),
                    FanFullSpeed = false,
                    PrecisionBoostOverdriveScaler = 0,
                    PrecisionBoostOverdriveBoostFrequency = 0,
                    AllCoreCurveOptimizer = 0,
                    EnableAllCoreCurveOptimizer = false,
                    EnableOverclocking = false,
                };
                result[powerMode] = defaults;
            }

            Log.Instance.Trace($"Defaults in other power modes retrieved:");
            foreach (var (powerMode, defaults) in result)
                Log.Instance.Trace($" - {powerMode}: {defaults}");

            return result;
        }
        catch (Exception ex)
        {
            Log.Instance.Trace($"Failed to get defaults in other power modes.", ex);
            return [];
        }
    }

    public override Task RestoreDefaultsInOtherPowerModeAsync(PowerModeState _) => Task.CompletedTask;

    protected override async Task<GodModePreset> GetDefaultStateAsync()
    {
        var mi = await Compatibility.GetMachineInformationAsync().ConfigureAwait(false);
        var isAmdDevice = mi.Properties.IsAmdDevice;

        // Retry WMI calls up to 5 times with 2s delay (handles post-reboot timing issues)
        RangeCapability[] allCapabilityData = [];
        DiscreteCapability[] allDiscreteData = [];
        
        for (int attempt = 1; attempt <= 5; attempt++)
        {
            try
            {
                allCapabilityData = (await WMI.LenovoCapabilityData01.ReadAsync().ConfigureAwait(false)).ToArray();
                allDiscreteData = (await WMI.LenovoDiscreteData.ReadAsync().ConfigureAwait(false)).ToArray();
                break; // Success
            }
            catch (ManagementException ex) when (attempt < 5)
            {
                Log.Instance.Trace($"WMI read attempt {attempt} failed, retrying in 2s...", ex);
                await Task.Delay(2000).ConfigureAwait(false);
            }
        }

        var capabilityData = allCapabilityData
            .Where(d => Enum.IsDefined(d.Id))
            .ToArray();

        var discreteData = allDiscreteData
            .Where(d => Enum.IsDefined(d.Id))
            .GroupBy(d => d.Id, d => d.Value, (id, values) => (id, values))
            .ToDictionary(d => d.id, d => d.values.ToArray());

        var stepperValues = new Dictionary<CapabilityID, StepperValue>();

        foreach (var c in capabilityData)
        {
            var value = await GetValueAsync(c.Id).OrNullIfException().ConfigureAwait(false) ?? c.DefaultValue;
            var steps = discreteData.GetValueOrDefault(c.Id) ?? [];

            if (c.Step == 0 && steps.Length < 1)
            {
                Log.Instance.Trace($"Skipping {c.Id}... [idRaw={(int)c.Id:X}, defaultValue={c.DefaultValue}, min={c.Min}, max={c.Max}, step={c.Step}, steps={string.Join(", ", steps)}]");
                continue;
            }

            Log.Instance.Trace($"Creating StepperValue {c.Id}... [idRaw={(int)c.Id:X}, defaultValue={c.DefaultValue}, min={c.Min}, max={c.Max}, step={c.Step}, steps={string.Join(", ", steps)}]");
            var stepperValue = new StepperValue(value, c.Min, c.Max, c.Step, steps, c.DefaultValue);
            stepperValues[c.Id] = stepperValue;
        }

        var fanTableData = await GetFanTableDataAsync().ConfigureAwait(false);
        var (precisionBoostScaler, precisionBoostFrequency, coreCurveOptimizer) = CreateAmdOverclockingValues(isAmdDevice);

        var preset = new GodModePreset
        {
            Name = "Default",
            CPULongTermPowerLimit = stepperValues.GetValueOrNull(CapabilityID.CPULongTermPowerLimit),
            CPUShortTermPowerLimit = stepperValues.GetValueOrNull(CapabilityID.CPUShortTermPowerLimit),
            CPUPeakPowerLimit = stepperValues.GetValueOrNull(CapabilityID.CPUPeakPowerLimit),
            CPUCrossLoadingPowerLimit = stepperValues.GetValueOrNull(CapabilityID.CPUCrossLoadingPowerLimit),
            CPUPL1Tau = stepperValues.GetValueOrNull(CapabilityID.CPUPL1Tau),
            APUsPPTPowerLimit = stepperValues.GetValueOrNull(CapabilityID.APUsPPTPowerLimit),
            CPUTemperatureLimit = stepperValues.GetValueOrNull(CapabilityID.CPUTemperatureLimit),
            GPUPowerBoost = stepperValues.GetValueOrNull(CapabilityID.GPUPowerBoost),
            GPUConfigurableTGP = stepperValues.GetValueOrNull(CapabilityID.GPUConfigurableTGP),
            GPUTemperatureLimit = stepperValues.GetValueOrNull(CapabilityID.GPUTemperatureLimit),
            GPUTotalProcessingPowerTargetOnAcOffsetFromBaseline = stepperValues.GetValueOrNull(CapabilityID.GPUTotalProcessingPowerTargetOnAcOffsetFromBaseline),
            GPUToCPUDynamicBoost = stepperValues.GetValueOrNull(CapabilityID.GPUToCPUDynamicBoost),
            FanTableInfo = fanTableData is null ? null : new FanTableInfo(fanTableData, await GetDefaultFanTableAsync().ConfigureAwait(false)),
            FanFullSpeed = await GetFanFullSpeedAsync().ConfigureAwait(false),
            MinValueOffset = 0,
            MaxValueOffset = 0,
            PrecisionBoostOverdriveScaler = precisionBoostScaler,
            PrecisionBoostOverdriveBoostFrequency = precisionBoostFrequency,
            AllCoreCurveOptimizer = coreCurveOptimizer,
            EnableAllCoreCurveOptimizer = false,
            EnableOverclocking = false,
        };

        Log.Instance.Trace($"Default state retrieved: {preset}");
        return preset;
    }

    private static (StepperValue?, StepperValue?, StepperValue?) CreateAmdOverclockingValues(bool isAmdDevice)
    {
        if (!isAmdDevice)
            return (null, null, null);

        return (new StepperValue(0, 0, 7, 1, [], 0),
            new StepperValue(0, 0, 200, 1, [], 0),
            new StepperValue(0, 0, 20, 1, [], 0));
    }

    private static CapabilityID AdjustCapabilityIdForPowerMode(CapabilityID id, PowerModeState powerMode)
    {
        var idRaw = (uint)id & CAPABILITY_ID_MASK;
        var powerModeRaw = ((uint)powerMode + 1) << 8;
        return (CapabilityID)(idRaw + powerModeRaw);
    }

    private static int? GetDefaultCapabilityIdValueInPowerMode(IEnumerable<RangeCapability> capabilities, CapabilityID id, PowerModeState powerMode)
    {
        var adjustedId = AdjustCapabilityIdForPowerMode(id, powerMode);
        var value = capabilities
            .Where(c => c.Id == adjustedId)
            .Select(c => c.DefaultValue)
            .DefaultIfEmpty(-1)
            .First();
        return value < 0 ? null : value;
    }

    #region Get/Set Value

    private static Task<int> GetValueAsync(CapabilityID id)
    {
        var idRaw = (uint)id & CAPABILITY_ID_MASK;
        return WMI.LenovoOtherMethod.GetFeatureValueAsync(idRaw);
    }

    private static Task SetValueAsync(CapabilityID id, StepperValue value) => SetValueAsync(id, value.Value);

    private static Task SetValueAsync(CapabilityID id, int value)
    {
        var idRaw = (uint)id & CAPABILITY_ID_MASK;
        return WMI.LenovoOtherMethod.SetFeatureValueAsync(idRaw, value);
    }

    private static Task SetOCValueAsync(CPUOverclockingID id, byte mode, StepperValue value)
    {
        return WMI.LenovoCpuMethod.CPUSetOCDataAsync(mode, (uint)id, value.Value);
    }

    private static Task SetCPUOverclockingMode(bool enable)
    {
        return WMI.LenovoOtherMethod.SetFeatureValueAsync((uint)CapabilityID.CPUOverclockingEnable, enable ? 1 : 0);
    }

    private static async Task<bool> IsBiosOcEnabledAsync()
    {
        try
        {
            var result = await WMI.LenovoGameZoneData.GetBIOSOCMode().ConfigureAwait(false);
            return result == BIOS_OC_MODE_ENABLED;
        }
        catch (ManagementException)
        {
            return false;
        }
    }

    #endregion

    #region Fan Table

    private static async Task<FanTableData[]?> GetFanTableDataAsync(PowerModeState powerModeState = PowerModeState.GodMode)
    {
        Log.Instance.Trace($"Reading fan table data...");
        var data = await WMI.LenovoFanTableData.ReadAsync().ConfigureAwait(false);
        var mi = await Compatibility.GetMachineInformationAsync().ConfigureAwait(false);

        var fanTableData = data
            .Where(d => d.mode == (int)powerModeState + 1)
            .Select(d =>
            {
                var type = (d.fanId, d.sensorId) switch
                {
                    (1, 1) => FanTableType.CPU,
                    (2, 5) => FanTableType.GPU,
                    (1, 4) => FanTableType.PCH,
                    _ => FanTableType.Unknown,
                };
                return new FanTableData(type, d.fanId, d.sensorId, d.fanTableData, d.sensorTableData);
            })
            .ToArray();

        if (!IsValidFanTableData(fanTableData))
        {
            Log.Instance.Trace($"Bad fan table: {string.Join(", ", fanTableData)}");
            return null;
        }

        Log.Instance.Trace($"Fan table data: {string.Join(", ", fanTableData)}");
        return fanTableData;
    }

    private static bool IsValidFanTableData(FanTableData[]? fanTableData)
    {
        return fanTableData?.All(ftd =>
            ftd.Type != FanTableType.Unknown &&
            ftd.FanSpeeds?.Length == 10 &&
            ftd.Temps?.Length == 10) ?? false;
    }

    private static Task SetFanTable(FanTable fanTable) => WMI.LenovoFanMethod.FanSetTableAsync(fanTable.GetBytes());

    #endregion

    #region Fan Full Speed

    private static async Task<bool> GetFanFullSpeedAsync()
    {
        var value = await WMI.LenovoOtherMethod.GetFeatureValueAsync(CapabilityID.FanFullSpeed).ConfigureAwait(false);
        return value != 0;
    }

    private static Task SetFanFullSpeedAsync(bool enabled) => WMI.LenovoOtherMethod.SetFeatureValueAsync(CapabilityID.FanFullSpeed, enabled ? 1 : 0);

    #endregion
}
