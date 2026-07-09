using System;
using System.Collections.Generic;
using System.Linq;
using System.Management;
using System.Threading.Tasks;
using LenovoLegionToolkit.Lib.Extensions;
using LenovoLegionToolkit.Lib.Features;
using LenovoLegionToolkit.Lib.Settings;
using LenovoLegionToolkit.Lib.SoftwareDisabler;
using LenovoLegionToolkit.Lib.System.Management;
using LenovoLegionToolkit.Lib.Utils;

namespace LenovoLegionToolkit.Lib.Controllers.GodMode;

public class GodModeController(
    GodModeSettings settings,
    VantageDisabler vantageDisabler,
    LegionZoneDisabler legionZoneDisabler,
    LegionSpaceDisabler legionSpaceDisabler)
    : IGodModeController
{
    private const uint CAPABILITY_ID_MASK = 0xFFFF00FF;
    private const int BIOS_OC_MODE_ENABLED = 3;

    public event EventHandler<Guid>? PresetChanged;

    private GodModePlatformConfiguration? _config;
    private MachineInformation? _mi;
    private bool _hasCapabilityErrors;

    public bool HasCapabilityErrors() => _hasCapabilityErrors;

    #region Software Disablers

    public Task<bool> NeedsVantageDisabledAsync() => Task.FromResult(true);

    public async Task<bool> NeedsLegionZoneDisabledAsync()
    {
        var config = await GetConfigAsync().ConfigureAwait(false);
        if (config.Platform == GodModePlatform.NonGaming)
            return false;
        return true;
    }

    public async Task<bool> NeedsLegionSpaceDisabledAsync()
    {
        var config = await GetConfigAsync().ConfigureAwait(false);
        if (config.Platform is GodModePlatform.LegacyLegion or GodModePlatform.NonGaming)
            return false;
        var mi = await GetMachineInformationAsync().ConfigureAwait(false);
        return mi.SmartFanVersion >= 8;
    }

    #endregion

    #region Active Preset

    public Task<Guid> GetActivePresetIdAsync() => Task.FromResult(settings.Store.ActivePresetId);

    public Task<string?> GetActivePresetNameAsync()
    {
        var store = settings.Store;
        var name = store.Presets
            .Where(p => p.Key == store.ActivePresetId)
            .Select(p => p.Value.Name)
            .FirstOrDefault();
        return Task.FromResult(name);
    }

    public async Task<(Guid, GodModeSettings.GodModeSettingsStore.Preset)> GetActivePresetAsync()
    {
        if (!IsValidStore(settings.Store))
        {
            Log.Instance.Trace($"Invalid store, generating default one.");
            var state = await GetStateAsync().ConfigureAwait(false);
            await SetStateAsync(state).ConfigureAwait(false);
        }

        var activePresetId = settings.Store.ActivePresetId;
        var presets = settings.Store.Presets;

        if (presets.TryGetValue(activePresetId, out var activePreset))
        {
            return (activePresetId, activePreset);
        }

        throw new InvalidOperationException($"Preset with ID {activePresetId} not found");
    }

    public virtual async Task<Dictionary<Guid, GodModeSettings.GodModeSettingsStore.Preset>> GetGodModePresetsAsync()
    {
        return await Task.FromResult(settings.Store.Presets).ConfigureAwait(false);
    }

    #endregion

    #region State Get/Set

    public async Task<GodModeState> GetStateAsync()
    {
        Log.Instance.Trace($"Getting state...");

        var store = settings.Store;
        var config = await GetConfigAsync().ConfigureAwait(false);
        var defaultState = await GetDefaultStateAsync(config).ConfigureAwait(false);

        if (!IsValidStore(store))
        {
            Log.Instance.Trace($"Loading default state...");

            var id = Guid.NewGuid();
            return new GodModeState
            {
                ActivePresetId = id,
                Presets = new Dictionary<Guid, GodModePreset> { { id, defaultState } }.AsReadOnlyDictionary()
            };
        }

        Log.Instance.Trace($"Loading state from store...");
        return await LoadStateFromStoreAsync(store, defaultState, config).ConfigureAwait(false);
    }

    public Task SetStateAsync(GodModeState state)
    {
        Log.Instance.Trace($"Setting state...");

        var activePresetId = state.ActivePresetId;
        var presets = new Dictionary<Guid, GodModeSettings.GodModeSettingsStore.Preset>();

        foreach (var (id, preset) in state.Presets)
        {
            presets.Add(id, new()
            {
                Name = preset.Name,
                CPULongTermPowerLimit = preset.CPULongTermPowerLimit,
                CPUShortTermPowerLimit = preset.CPUShortTermPowerLimit,
                CPUPeakPowerLimit = preset.CPUPeakPowerLimit,
                CPUCrossLoadingPowerLimit = preset.CPUCrossLoadingPowerLimit,
                CPUPL2Tau = preset.CPUPL2Tau,
                APUsPPTPowerLimit = preset.APUsPPTPowerLimit,
                CPUTemperatureLimit = preset.CPUTemperatureLimit,
                GPUPowerBoost = preset.GPUPowerBoost,
                GPUConfigurableTGP = preset.GPUConfigurableTGP,
                GPUTemperatureLimit = preset.GPUTemperatureLimit,
                GPUTotalProcessingPowerTargetOnAcOffsetFromBaseline = preset.GPUTotalProcessingPowerTargetOnAcOffsetFromBaseline,
                GPUToCPUDynamicBoost = preset.GPUToCPUDynamicBoost,
                FanTable = preset.FanTableInfo?.Table,
                FanFullSpeed = preset.FanFullSpeed,
                MinValueOffset = preset.MinValueOffset,
                MaxValueOffset = preset.MaxValueOffset,
                PrecisionBoostOverdriveScaler = preset.PrecisionBoostOverdriveScaler,
                PrecisionBoostOverdriveBoostFrequency = preset.PrecisionBoostOverdriveBoostFrequency,
                AllCoreCurveOptimizer = preset.AllCoreCurveOptimizer,
                EnableAllCoreCurveOptimizer = preset.EnableAllCoreCurveOptimizer,
                EnableOverclocking = preset.EnableOverclocking,
                Overrides = new Dictionary<PowerOverrideKey, string>(preset.Overrides ?? []),
            });
        }

        settings.Store.ActivePresetId = activePresetId;
        settings.Store.Presets = presets;
        settings.SynchronizeStore();

        Log.Instance.Trace($"State saved.");
        return Task.CompletedTask;
    }

    #endregion

    #region Apply State

    public async Task ApplyStateAsync()
    {
        var config = await GetConfigAsync().ConfigureAwait(false);

        if (config.Platform == GodModePlatform.LegacyLegion)
        {
            await ApplyStateLegacyAsync().ConfigureAwait(false);
            return;
        }

        await ApplyStateDataDrivenAsync(config).ConfigureAwait(false);
    }

    private async Task ApplyStateLegacyAsync()
    {
        if (await legionZoneDisabler.GetStatusAsync().ConfigureAwait(false) == SoftwareStatus.Enabled)
        {
            Log.Instance.Trace($"Can't correctly apply state when Legion Zone is running.");
            return;
        }

        Log.Instance.Trace($"Applying state...");

        var (presetId, preset) = await GetActivePresetAsync().ConfigureAwait(false);
        var fanTable = preset.FanTable ?? await GetDefaultFanTableAsync().ConfigureAwait(false);
        var fanFullSpeed = preset.FanFullSpeed ?? false;

        var cpuSettings = new (string Name, StepperValue? Value, Func<int, Task> Set)[]
        {
            ("cpuLongTermPowerLimit", preset.CPULongTermPowerLimit, WMI.LenovoCpuMethod.CPUSetLongTermPowerLimitAsync),
            ("cpuShortTermPowerLimit", preset.CPUShortTermPowerLimit, WMI.LenovoCpuMethod.CPUSetShortTermPowerLimitAsync),
            ("cpuPeakPowerLimit", preset.CPUPeakPowerLimit, v => WMI.LenovoCpuMethod.CPUSetPeakPowerLimitAsync(v)),
            ("cpuCrossLoadingPowerLimit", preset.CPUCrossLoadingPowerLimit, v => WMI.LenovoCpuMethod.CPUSetCrossLoadingPowerLimitAsync(v)),
            ("apuSPPTPowerLimit", preset.APUsPPTPowerLimit, v => WMI.LenovoCpuMethod.SetAPUSPPTPowerLimitAsync(v)),
            ("cpuTemperatureLimit", preset.CPUTemperatureLimit, v => WMI.LenovoCpuMethod.CPUSetTemperatureControlAsync(v)),
        };

        var gpuSettings = new (string Name, StepperValue? Value, Func<int, Task> Set)[]
        {
            ("gpuPowerBoost", preset.GPUPowerBoost, v => WMI.LenovoGpuMethod.GPUSetPPABPowerLimitAsync(v)),
            ("gpuConfigurableTgp", preset.GPUConfigurableTGP, v => WMI.LenovoGpuMethod.GPUSetCTGPPowerLimitAsync(v)),
            ("gpuTemperatureLimit", preset.GPUTemperatureLimit, v => WMI.LenovoGpuMethod.GPUSetTemperatureLimitAsync(v)),
        };

        foreach (var (name, value, set) in cpuSettings)
        {
            await TryApplyAsync(name, value, set, rethrow: true).ConfigureAwait(false);
        }

        foreach (var (name, value, set) in gpuSettings)
        {
            await TryApplyAsync(name, value, set, rethrow: false).ConfigureAwait(false);
        }

        await ApplyFanLegacyAsync(fanTable, fanFullSpeed).ConfigureAwait(false);

        await RaisePresetChanged(presetId);
        Log.Instance.Trace($"State applied. [name={preset.Name}, id={presetId}]");
    }

    private static async Task TryApplyAsync(string name, StepperValue? value, Func<int, Task> set, bool rethrow)
    {
        if (value == null)
            return;
        try
        {
            Log.Instance.Trace($"Applying {name}: {value}");
            await set(value.Value.Value).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Log.Instance.Trace($"Apply failed. [setting={name}]", ex);
            if (rethrow)
            {
                throw;
            }
        }
    }

    private async Task ApplyFanLegacyAsync(FanTable fanTable, bool fanFullSpeed)
    {
        if (fanFullSpeed)
        {
            try
            {
                Log.Instance.Trace($"Applying Fan Full Speed...");
                await WMI.LenovoFanMethod.FanSetFullSpeedAsync(1).ConfigureAwait(false);
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
                await WMI.LenovoFanMethod.FanSetFullSpeedAsync(0).ConfigureAwait(false);
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
                await WMI.LenovoFanMethod.FanSetTableAsync(fanTable.GetBytes()).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Log.Instance.Trace($"Apply failed. [setting=fanTable]", ex);
                throw;
            }
        }
    }

    private async Task ApplyStateDataDrivenAsync(GodModePlatformConfiguration config)
    {
        var mi = await GetMachineInformationAsync().ConfigureAwait(false);

        if (await vantageDisabler.GetStatusAsync().ConfigureAwait(false) == SoftwareStatus.Enabled)
        {
            Log.Instance.Trace($"Can't correctly apply state when Vantage is running.");
            return;
        }

        if (config.Platform == GodModePlatform.Legion)
        {
            if (mi.SmartFanVersion >= 8)
            {
                if (await legionSpaceDisabler.GetStatusAsync().ConfigureAwait(false) == SoftwareStatus.Enabled)
                {
                    Log.Instance.Trace($"Can't correctly apply state when Legion Space is running.");
                    return;
                }
            }

            if (await legionZoneDisabler.GetStatusAsync().ConfigureAwait(false) == SoftwareStatus.Enabled)
            {
                Log.Instance.Trace($"Can't correctly apply state when Legion Zone is running.");
                return;
            }
        }

        Log.Instance.Trace($"Applying state...");

        var (presetId, preset) = await GetActivePresetAsync().ConfigureAwait(false);

        var defaultPresets = await GetDefaultsInOtherPowerModesAsync().ConfigureAwait(false);
        var defaultPerformancePreset = mi.Properties.SupportsExtremeMode
            ? defaultPresets.GetValueOrNull(PowerModeState.Extreme)
            : defaultPresets.GetValueOrNull(PowerModeState.Performance);

        var failAllowedIds = config.Capabilities
            .Where(c => c.FailAllowed)
            .Select(c => c.RawId)
            .ToHashSet();

        var fanTable = preset.FanTable ?? await GetDefaultFanTableAsync().ConfigureAwait(false);
        var fanFullSpeed = preset.FanFullSpeed ?? false;

        foreach (var cap in config.Capabilities)
        {
            var stepperValue = GetStepperValueForStorePreset(preset, cap.PropertyName);
            var value = stepperValue?.Value;
            var defaultValue = GetDefaultValueFromDefaults(defaultPerformancePreset, cap.PropertyName);

            if (value.HasValue)
            {
                try
                {
                    Log.Instance.Trace($"Applying {cap.PropertyName} ({cap.RawId:X}): {value}...");
                    await SetValueAsync(cap.RawId, value.Value, config.CapabilityIdMask).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    Log.Instance.Trace($"Failed to apply {cap.PropertyName} ({cap.RawId:X}). [value={value}]", ex);
                    if (!failAllowedIds.Contains(cap.RawId))
                    {
                        throw;
                    }
                }
            }
            else if (defaultValue.HasValue)
            {
                try
                {
                    Log.Instance.Trace($"Applying default {cap.PropertyName} ({cap.RawId:X}): {defaultValue}...");
                    await SetValueAsync(cap.RawId, defaultValue.Value, config.CapabilityIdMask).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    Log.Instance.Trace($"Failed to apply default {cap.PropertyName} ({cap.RawId:X}). [value={defaultValue}]", ex);
                    if (!failAllowedIds.Contains(cap.RawId))
                    {
                        throw;
                    }
                }
            }
        }

        if (preset.FanTable != null && config.Platform != GodModePlatform.NonGaming)
        {
            await ApplyFanSettingsAsync(config, fanTable, fanFullSpeed).ConfigureAwait(false);
        }

        if (config.Platform == GodModePlatform.Legion)
        {
            await ApplyOcIfNeededAsync(preset).ConfigureAwait(false);
        }

        await RaisePresetChanged(presetId);
        Log.Instance.Trace($"State applied. [name={preset.Name}, id={presetId}]");
    }

    private async Task ApplyFanSettingsAsync(GodModePlatformConfiguration config, FanTable fanTable, bool fanFullSpeed)
    {
        if (fanFullSpeed)
        {
            try
            {
                Log.Instance.Trace($"Applying Fan Full Speed...");
                await SetFanFullSpeedAsync(config, true).ConfigureAwait(false);
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
                await SetFanFullSpeedAsync(config, false).ConfigureAwait(false);
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
                await WMI.LenovoFanMethod.FanSetTableAsync(fanTable.GetBytes()).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Log.Instance.Trace($"Apply failed. [setting=fanTable]", ex);
                throw;
            }
        }
    }

    private async Task ApplyOcIfNeededAsync(GodModeSettings.GodModeSettingsStore.Preset preset)
    {
        var isOcEnabled = await IsBiosOcEnabledAsync().ConfigureAwait(false);

        if (isOcEnabled && preset.EnableOverclocking == true)
        {
            await WMI.LenovoOtherMethod.SetFeatureValueAsync((uint)CapabilityID.CPUOverclockingEnable, 1).ConfigureAwait(false);

            if (preset.PrecisionBoostOverdriveScaler is { } pboScaler)
            {
                Log.Instance.Trace($"Applying PrecisionBoostOverdriveScaler: {pboScaler}...");
                await WMI.LenovoCpuMethod.CPUSetOCDataAsync(17, (uint)CPUOverclockingID.PrecisionBoostOverdriveScaler, pboScaler.Value).ConfigureAwait(false);
            }
            if (preset.PrecisionBoostOverdriveBoostFrequency is { } pboFreq)
            {
                Log.Instance.Trace($"Applying PrecisionBoostOverdriveBoostFrequency: {pboFreq}...");
                await WMI.LenovoCpuMethod.CPUSetOCDataAsync(17, (uint)CPUOverclockingID.PrecisionBoostOverdriveBoostFrequency, pboFreq.Value).ConfigureAwait(false);
            }
            if (preset.AllCoreCurveOptimizer is { } coreCurve && preset.EnableAllCoreCurveOptimizer == true)
            {
                Log.Instance.Trace($"Applying AllCoreCurveOptimizer: {coreCurve}...");
                await WMI.LenovoCpuMethod.CPUSetOCDataAsync(17, (uint)CPUOverclockingID.AllCoreCurveOptimizer, coreCurve.Value).ConfigureAwait(false);
            }
        }
        else if (isOcEnabled && preset.EnableOverclocking == false)
        {
            await WMI.LenovoOtherMethod.SetFeatureValueAsync((uint)CapabilityID.CPUOverclockingEnable, 0).ConfigureAwait(false);
            Log.Instance.Trace($"Overclocking is disabled.");
        }
    }

    #endregion

    #region Get Default State

    private async Task<GodModePreset> GetDefaultStateAsync(GodModePlatformConfiguration config)
    {
        return config.Platform switch
        {
            GodModePlatform.LegacyLegion => await GetDefaultStateLegacyAsync().ConfigureAwait(false),
            _ => await GetDefaultStateDataDrivenAsync(config).ConfigureAwait(false),
        };
    }

    private async Task<GodModePreset> GetDefaultStateLegacyAsync()
    {
        var fanTableData = await GetFanTableDataLegacyAsync().ConfigureAwait(false);

        var preset = new GodModePreset
        {
            Name = "Default",
            CPULongTermPowerLimit = await GetCPULongTermPowerLimitLegacyAsync().OrNullIfException().ConfigureAwait(false),
            CPUShortTermPowerLimit = await GetCPUShortTermPowerLimitLegacyAsync().OrNullIfException().ConfigureAwait(false),
            CPUPeakPowerLimit = await GetCPUPeakPowerLimitLegacyAsync().OrNullIfException().ConfigureAwait(false),
            CPUCrossLoadingPowerLimit = await GetCPUCrossLoadingPowerLimitLegacyAsync().OrNullIfException().ConfigureAwait(false),
            APUsPPTPowerLimit = await GetAPUSPPTPowerLimitLegacyAsync().OrNullIfException().ConfigureAwait(false),
            CPUTemperatureLimit = await GetCPUTemperatureLimitLegacyAsync().OrNullIfException().ConfigureAwait(false),
            GPUPowerBoost = await GetGPUPowerBoostLegacyAsync().OrNullIfException().ConfigureAwait(false),
            GPUConfigurableTGP = await GetGPUConfigurableTGPLegacyAsync().OrNullIfException().ConfigureAwait(false),
            GPUTemperatureLimit = await GetGPUTemperatureLimitLegacyAsync().OrNullIfException().ConfigureAwait(false),
            FanTableInfo = fanTableData == null ? null : new FanTableInfo(fanTableData, await GetDefaultFanTableAsync().ConfigureAwait(false)),
            FanFullSpeed = await WMI.LenovoFanMethod.FanGetFullSpeedAsync().ConfigureAwait(false),
            MinValueOffset = 0,
            MaxValueOffset = 0
        };

        Log.Instance.Trace($"Default state retrieved (legacy): {preset}");
        return preset;
    }

    private async Task<GodModePreset> GetDefaultStateDataDrivenAsync(GodModePlatformConfiguration config)
    {
        var mi = await GetMachineInformationAsync().ConfigureAwait(false);
        var isAmdDevice = mi.Properties.IsAmdDevice;
        var stepperValues = new Dictionary<uint, StepperValue>();

        if (config.UseCapabilityDataForDefaults)
        {
            var allCapabilityData = await WMI.LenovoCapabilityData01.ReadAsync().ConfigureAwait(false);
            allCapabilityData = allCapabilityData.ToArray();

            var knownIds = config.Capabilities.Select(c => c.RawId).ToHashSet();
            var capabilityData = allCapabilityData
                .Where(d => knownIds.Contains((uint)d.Id))
                .ToArray();

            var allDiscreteData = await WMI.LenovoDiscreteData.ReadAsync().ConfigureAwait(false);
            allDiscreteData = allDiscreteData.ToArray();

            var discreteData = allDiscreteData
                .Where(d => knownIds.Contains((uint)d.Id))
                .GroupBy(d => d.Id, d => d.Value, (id, values) => (id, values))
                .ToDictionary(d => (uint)d.id, d => d.values.ToArray());

            foreach (var c in capabilityData)
            {
                var rawId = (uint)c.Id;
                var value = await GetValueAsync(rawId, config.CapabilityIdMask).OrNullIfException().ConfigureAwait(false) ?? c.DefaultValue;
                var steps = discreteData.GetValueOrDefault(rawId) ?? [];

                if (c.Step == 0 && steps.Length < 1)
                {
                    Log.Instance.Trace($"Skipping {rawId:X}... [defaultValue={c.DefaultValue}, min={c.Min}, max={c.Max}, step={c.Step}]");
                    continue;
                }

                Log.Instance.Trace($"Creating StepperValue {rawId:X}... [defaultValue={c.DefaultValue}, min={c.Min}, max={c.Max}, step={c.Step}, steps={string.Join(", ", steps)}]");
                stepperValues[rawId] = new StepperValue(value, c.Min, c.Max, c.Step, steps, c.DefaultValue);
            }
        }
        else
        {
            foreach (var cap in config.Capabilities)
            {
                try
                {
                    var rawValue = await GetValueAsync(cap.RawId, config.CapabilityIdMask).ConfigureAwait(false);

                    if (cap.Steps.Length > 0)
                    {
                        var value = rawValue;
                        if (!cap.Steps.Contains(value))
                        {
                            value = cap.Steps.MinBy(v => Math.Abs((long)v - value));
                        }

                        stepperValues[cap.RawId] = new StepperValue(value, 0, 0, 0, cap.Steps, cap.DefaultValue);
                    }
                    else
                    {
                        stepperValues[cap.RawId] = new StepperValue(rawValue, cap.Min, cap.Max, cap.Step, [], cap.DefaultValue);
                    }
                }
                catch (Exception ex)
                {
                    Log.Instance.Trace($"Failed to read {cap.PropertyName} ({cap.RawId:X}), skipping.", ex);
                    if (cap.PropertyName != nameof(GodModePreset.FanFullSpeed))
                    {
                        _hasCapabilityErrors = true;
                    }
                }
            }
        }

        var fanTableData = await TryGetFanTableDataAsync(config).ConfigureAwait(false);

        var (pboScaler, pboFreq, coreCurve) = CreateAmdOcDefaults(isAmdDevice);
        FanTableInfo? fanTableInfo = null;
        if (fanTableData != null)
        {
            fanTableInfo = new FanTableInfo(fanTableData, await GetDefaultFanTableAsync().ConfigureAwait(false));
        }
        var fanFullSpeed = await GetFanFullSpeedAsync(config).ConfigureAwait(false);

        var enableOverclocking = false;
        var enableAllCoreCurve = false;

        if (config.Platform == GodModePlatform.Legion && isAmdDevice)
        {
            try
            {
                var ocMode = await WMI.LenovoOtherMethod.GetFeatureValueAsync((uint)CapabilityID.CPUOverclockingEnable).ConfigureAwait(false);
                enableOverclocking = ocMode == 1;
            }
            catch { /* Ignore */ }
        }

        var preset = PopulatePreset(config, stepperValues, fanTableInfo, fanFullSpeed, 0, 0, pboScaler, pboFreq, coreCurve, enableAllCoreCurve, enableOverclocking);

        Log.Instance.Trace($"Default state retrieved: {preset}");
        return preset;
    }

    private static GodModePreset PopulatePreset(
        GodModePlatformConfiguration config,
        Dictionary<uint, StepperValue> stepperValues,
        FanTableInfo? fanTableInfo,
        bool? fanFullSpeed,
        int minValueOffset,
        int maxValueOffset,
        StepperValue? pboScaler,
        StepperValue? pboFreq,
        StepperValue? coreCurve,
        bool enableAllCoreCurve,
        bool enableOverclocking)
    {
        StepperValue? sv(uint id) => stepperValues.GetValueOrNull(id);

        return new GodModePreset
        {
            Name = "Default",
            CPULongTermPowerLimit = Sv(config, sv, nameof(GodModePreset.CPULongTermPowerLimit)),
            CPUShortTermPowerLimit = Sv(config, sv, nameof(GodModePreset.CPUShortTermPowerLimit)),
            CPUPeakPowerLimit = Sv(config, sv, nameof(GodModePreset.CPUPeakPowerLimit)),
            CPUCrossLoadingPowerLimit = Sv(config, sv, nameof(GodModePreset.CPUCrossLoadingPowerLimit)),
            CPUPL2Tau = Sv(config, sv, nameof(GodModePreset.CPUPL2Tau)),
            APUsPPTPowerLimit = Sv(config, sv, nameof(GodModePreset.APUsPPTPowerLimit)),
            CPUTemperatureLimit = Sv(config, sv, nameof(GodModePreset.CPUTemperatureLimit)),
            GPUPowerBoost = Sv(config, sv, nameof(GodModePreset.GPUPowerBoost)),
            GPUConfigurableTGP = Sv(config, sv, nameof(GodModePreset.GPUConfigurableTGP)),
            GPUTemperatureLimit = Sv(config, sv, nameof(GodModePreset.GPUTemperatureLimit)),
            GPUTotalProcessingPowerTargetOnAcOffsetFromBaseline = Sv(config, sv, nameof(GodModePreset.GPUTotalProcessingPowerTargetOnAcOffsetFromBaseline)),
            GPUToCPUDynamicBoost = Sv(config, sv, nameof(GodModePreset.GPUToCPUDynamicBoost)),
            FanTableInfo = fanTableInfo,
            FanFullSpeed = fanFullSpeed,
            MinValueOffset = minValueOffset,
            MaxValueOffset = maxValueOffset,
            PrecisionBoostOverdriveScaler = pboScaler,
            PrecisionBoostOverdriveBoostFrequency = pboFreq,
            AllCoreCurveOptimizer = coreCurve,
            EnableAllCoreCurveOptimizer = enableAllCoreCurve,
            EnableOverclocking = enableOverclocking,
        };
    }

    private static StepperValue? Sv(GodModePlatformConfiguration config, Func<uint, StepperValue?> sv, string propertyName)
    {
        var cap = config.Capabilities.FirstOrDefault(c => c.PropertyName == propertyName);
        if (cap == null)
            return null;
        return sv(cap.RawId);
    }

    private static (StepperValue?, StepperValue?, StepperValue?) CreateAmdOcDefaults(bool isAmdDevice)
    {
        if (!isAmdDevice)
            return (null, null, null);
        return (
            new StepperValue(0, 0, 7, 1, [], 0),
            new StepperValue(0, 0, 200, 1, [], 0),
            new StepperValue(0, 0, 20, 1, [], 0)
        );
    }

    #endregion

    #region Property Access Helpers

    private static StepperValue? GetStepperValueForStorePreset(GodModeSettings.GodModeSettingsStore.Preset preset, string propertyName)
    {
        return propertyName switch
        {
            nameof(GodModePreset.CPULongTermPowerLimit) => preset.CPULongTermPowerLimit,
            nameof(GodModePreset.CPUShortTermPowerLimit) => preset.CPUShortTermPowerLimit,
            nameof(GodModePreset.CPUPeakPowerLimit) => preset.CPUPeakPowerLimit,
            nameof(GodModePreset.CPUCrossLoadingPowerLimit) => preset.CPUCrossLoadingPowerLimit,
            nameof(GodModePreset.CPUPL2Tau) => preset.CPUPL2Tau,
            nameof(GodModePreset.APUsPPTPowerLimit) => preset.APUsPPTPowerLimit,
            nameof(GodModePreset.CPUTemperatureLimit) => preset.CPUTemperatureLimit,
            nameof(GodModePreset.GPUPowerBoost) => preset.GPUPowerBoost,
            nameof(GodModePreset.GPUConfigurableTGP) => preset.GPUConfigurableTGP,
            nameof(GodModePreset.GPUTemperatureLimit) => preset.GPUTemperatureLimit,
            nameof(GodModePreset.GPUTotalProcessingPowerTargetOnAcOffsetFromBaseline) => preset.GPUTotalProcessingPowerTargetOnAcOffsetFromBaseline,
            nameof(GodModePreset.GPUToCPUDynamicBoost) => preset.GPUToCPUDynamicBoost,
            nameof(GodModePreset.FanFullSpeed) when preset.FanFullSpeed != null => new StepperValue(preset.FanFullSpeed.Value ? 1 : 0, 0, 1, 1, [], 0),
            _ => null,
        };
    }

    private static int? GetDefaultValueFromDefaults(GodModeDefaults? defaults, string propertyName)
    {
        if (defaults is not { } d)
            return null;
        return propertyName switch
        {
            nameof(GodModeDefaults.CPULongTermPowerLimit) => d.CPULongTermPowerLimit,
            nameof(GodModeDefaults.CPUShortTermPowerLimit) => d.CPUShortTermPowerLimit,
            nameof(GodModeDefaults.CPUPeakPowerLimit) => d.CPUPeakPowerLimit,
            nameof(GodModeDefaults.CPUCrossLoadingPowerLimit) => d.CPUCrossLoadingPowerLimit,
            nameof(GodModeDefaults.CPUPL2Tau) => d.CPUPL2Tau,
            nameof(GodModeDefaults.APUsPPTPowerLimit) => d.APUsPPTPowerLimit,
            nameof(GodModeDefaults.CPUTemperatureLimit) => d.CPUTemperatureLimit,
            nameof(GodModeDefaults.GPUPowerBoost) => d.GPUPowerBoost,
            nameof(GodModeDefaults.GPUConfigurableTGP) => d.GPUConfigurableTGP,
            nameof(GodModeDefaults.GPUTemperatureLimit) => d.GPUTemperatureLimit,
            nameof(GodModeDefaults.GPUTotalProcessingPowerTargetOnAcOffsetFromBaseline) => d.GPUTotalProcessingPowerTargetOnAcOffsetFromBaseline,
            nameof(GodModeDefaults.GPUToCPUDynamicBoost) => d.GPUToCPUDynamicBoost,
            _ => null,
        };
    }

    #endregion

    #region Get Defaults in Other Power Modes

    public async Task<Dictionary<PowerModeState, GodModeDefaults>> GetDefaultsInOtherPowerModesAsync()
    {
        var config = await GetConfigAsync().ConfigureAwait(false);
        var mi = await GetMachineInformationAsync().ConfigureAwait(false);

        return config.Platform switch
        {
            GodModePlatform.LegacyLegion => await GetDefaultsLegacyAsync().ConfigureAwait(false),
            GodModePlatform.Legion => await GetDefaultsLegionAsync(mi).ConfigureAwait(false),
            GodModePlatform.NonGaming => [],
            _ => []
        };
    }

    public async Task RestoreDefaultsInOtherPowerModeAsync(PowerModeState state)
    {
        var config = await GetConfigAsync().ConfigureAwait(false);
        if (config.Platform != GodModePlatform.LegacyLegion)
            return;

        try
        {
            Log.Instance.Trace($"Restoring defaults for {state}...");
            var result = await GetDefaultsLegacyAsync().ConfigureAwait(false);

            if (!result.TryGetValue(state, out var defaults))
            {
                Log.Instance.Trace($"Defaults for {state} not found. Skipping...");
                return;
            }

            if (defaults.CPULongTermPowerLimit is { } longTermPowerLimit)
            {
                await WMI.LenovoCpuMethod.CPUSetLongTermPowerLimitAsync(longTermPowerLimit).ConfigureAwait(false);
            }
            if (defaults.CPUShortTermPowerLimit is { } shortTermPowerLimit)
            {
                await WMI.LenovoCpuMethod.CPUSetShortTermPowerLimitAsync(shortTermPowerLimit).ConfigureAwait(false);
            }
            if (defaults.CPUPeakPowerLimit is { } peakPowerLimit)
            {
                await WMI.LenovoCpuMethod.CPUSetPeakPowerLimitAsync(peakPowerLimit).ConfigureAwait(false);
            }
            if (defaults.CPUCrossLoadingPowerLimit is { } crossLoadingPowerLimit)
            {
                await WMI.LenovoCpuMethod.CPUSetCrossLoadingPowerLimitAsync(crossLoadingPowerLimit).ConfigureAwait(false);
            }
            if (defaults.APUsPPTPowerLimit is { } spptPowerLimit)
            {
                await WMI.LenovoCpuMethod.SetAPUSPPTPowerLimitAsync(spptPowerLimit).ConfigureAwait(false);
            }
            if (defaults.CPUTemperatureLimit is { } cpuTemperatureLimit)
            {
                await WMI.LenovoCpuMethod.CPUSetTemperatureControlAsync(cpuTemperatureLimit).ConfigureAwait(false);
            }
            if (defaults.GPUPowerBoost is { } gpuPowerBoost)
            {
                await WMI.LenovoGpuMethod.GPUSetPPABPowerLimitAsync(gpuPowerBoost).ConfigureAwait(false);
            }
            if (defaults.GPUConfigurableTGP is { } configurableTgp)
            {
                await WMI.LenovoGpuMethod.GPUSetCTGPPowerLimitAsync(configurableTgp).ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            Log.Instance.Trace($"Failed to restore defaults for {state}.", ex);
        }
    }

    private async Task<Dictionary<PowerModeState, GodModeDefaults>> GetDefaultsLegacyAsync()
    {
        try
        {
            Log.Instance.Trace($"Getting defaults in other power modes...");
            var defaultFanTable = await GetDefaultFanTableAsync().ConfigureAwait(false);
            var result = await WMI.LenovoDefaultValueInDifferentModeData.ReadAsync().ConfigureAwait(false);

            return result.Select(d =>
                {
                    var powerMode = (PowerModeState)(d.Mode - 1);
                    var defaults = new GodModeDefaults
                    {
                        CPULongTermPowerLimit = d.CPULongTermPowerLimit,
                        CPUShortTermPowerLimit = d.CPUShortTermPowerLimit,
                        CPUPeakPowerLimit = d.CPUPeakPowerLimit,
                        CPUCrossLoadingPowerLimit = d.CPUCrossLoadingPowerLimit,
                        APUsPPTPowerLimit = d.APUsPPTPowerLimit,
                        CPUTemperatureLimit = d.CPUTemperatureLimit,
                        GPUPowerBoost = d.GPUPowerBoost,
                        GPUConfigurableTGP = d.GPUConfigurableTGP,
                        GPUTemperatureLimit = d.GPUTemperatureLimit,
                        FanTable = defaultFanTable,
                        FanFullSpeed = false
                    };
                    return (powerMode, defaults);
                })
                .Where(d => d.powerMode is PowerModeState.Quiet or PowerModeState.Balance or PowerModeState.Performance)
                .DistinctBy(d => d.powerMode)
                .ToDictionary(d => d.powerMode, d => d.defaults);
        }
        catch (Exception ex)
        {
            Log.Instance.Trace($"Failed to get defaults.", ex);
            return [];
        }
    }

    private async Task<Dictionary<PowerModeState, GodModeDefaults>> GetDefaultsLegionAsync(MachineInformation mi)
    {
        try
        {
            Log.Instance.Trace($"Getting defaults in other power modes...");
            var result = new Dictionary<PowerModeState, GodModeDefaults>();
            var allCapabilityData = await WMI.LenovoCapabilityData01.ReadAsync().ConfigureAwait(false);
            allCapabilityData = allCapabilityData.ToArray();

            var powerModes = new List<PowerModeState> { PowerModeState.Quiet, PowerModeState.Balance, PowerModeState.Performance };
            if (mi.Properties.SupportsExtremeMode)
                powerModes.Add(PowerModeState.Extreme);

            foreach (var powerMode in powerModes)
            {
                var defaults = new GodModeDefaults
                {
                    CPULongTermPowerLimit = GetDefVal(allCapabilityData, CapabilityID.CPULongTermPowerLimit, powerMode),
                    CPUShortTermPowerLimit = GetDefVal(allCapabilityData, CapabilityID.CPUShortTermPowerLimit, powerMode),
                    CPUPeakPowerLimit = GetDefVal(allCapabilityData, CapabilityID.CPUPeakPowerLimit, powerMode),
                    CPUCrossLoadingPowerLimit = GetDefVal(allCapabilityData, CapabilityID.CPUCrossLoadingPowerLimit, powerMode),
                    CPUPL2Tau = GetDefVal(allCapabilityData, CapabilityID.CPUPL2Tau, powerMode),
                    APUsPPTPowerLimit = GetDefVal(allCapabilityData, CapabilityID.APUsPPTPowerLimit, powerMode),
                    CPUTemperatureLimit = GetDefVal(allCapabilityData, CapabilityID.CPUTemperatureLimit, powerMode),
                    GPUPowerBoost = GetDefVal(allCapabilityData, CapabilityID.GPUPowerBoost, powerMode),
                    GPUConfigurableTGP = GetDefVal(allCapabilityData, CapabilityID.GPUConfigurableTGP, powerMode),
                    GPUTemperatureLimit = GetDefVal(allCapabilityData, CapabilityID.GPUTemperatureLimit, powerMode),
                    GPUTotalProcessingPowerTargetOnAcOffsetFromBaseline = GetDefVal(allCapabilityData, CapabilityID.GPUTotalProcessingPowerTargetOnAcOffsetFromBaseline, powerMode),
                    GPUToCPUDynamicBoost = GetDefVal(allCapabilityData, CapabilityID.GPUToCPUDynamicBoost, powerMode),
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

            Log.Instance.Trace($"Defaults in other power modes retrieved.");
            return result;
        }
        catch (Exception ex)
        {
            Log.Instance.Trace($"Failed to get defaults.", ex);
            return [];
        }
    }

    #endregion

    #region Fan Tables

    public Task<FanTable> GetDefaultFanTableAsync()
    {
        var fanTable = new FanTable([1, 2, 3, 4, 5, 6, 7, 8, 9, 10]);
        return Task.FromResult(fanTable);
    }

    public async Task<FanTable> GetMinimumFanTableAsync()
    {
        var config = await GetConfigAsync().ConfigureAwait(false);
        return config.Platform switch
        {
            GodModePlatform.LegacyLegion => new FanTable([0, 0, 0, 0, 0, 0, 0, 1, 3, 5]),
            _ => config.MinimumFanTable,
        };
    }

    private static async Task<FanTableData[]?> TryGetFanTableDataAsync(GodModePlatformConfiguration config)
    {
        if (config.Platform is not (GodModePlatform.Legion or GodModePlatform.NonGaming))
        {
            return null;
        }

        try
        {
            return await GetFanTableDataLegionAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Log.Instance.Trace($"Failed to read fan table data.", ex);
            return null;
        }
    }

    private static async Task<FanTableData[]?> GetFanTableDataLegacyAsync()
    {
        Log.Instance.Trace($"Reading fan table data...");
        var data = await WMI.LenovoFanTableData.ReadAsync().ConfigureAwait(false);

        var fanTableData = data
            .Select(d =>
            {
                var type = (d.fanId, d.sensorId) switch
                {
                    (0, 3) => FanTableType.CPU,
                    (1, 4) => FanTableType.GPU,
                    (0, 0) => FanTableType.CPUSensor,
                    _ => FanTableType.Unknown,
                };
                return new FanTableData(type, d.fanId, d.sensorId, d.fanTableData, d.sensorTableData);
            })
            .ToArray();

        if (fanTableData.Length != 3) { Log.Instance.Trace($"Bad fan table length"); return null; }
        if (fanTableData.Count(ftd => ftd.FanSpeeds.Length == 10) != 3) { Log.Instance.Trace($"Bad fan table speeds length"); return null; }
        if (fanTableData.Count(ftd => ftd.Temps.Length == 10) != 3) { Log.Instance.Trace($"Bad fan table temps length"); return null; }

        Log.Instance.Trace($"Fan table data retrieved.");
        return fanTableData;
    }

    private static async Task<FanTableData[]?> GetFanTableDataLegionAsync(PowerModeState powerModeState = PowerModeState.GodMode)
    {
        Log.Instance.Trace($"Reading fan table data...");
        var data = await WMI.LenovoFanTableData.ReadAsync().ConfigureAwait(false);
        var mi = await Compatibility.GetMachineInformationAsync().ConfigureAwait(false);

        var fanTableData = data
            .Where(d => d.mode == (int)powerModeState + 1)
            .Select(d =>
            {
                var type = ResolveFanTableType(d.fanId, d.sensorId, mi.SmartFanVersion, mi);
                return new FanTableData(type, d.fanId, d.sensorId, d.fanTableData, d.sensorTableData);
            })
            .ToArray();

        if (!IsValidFanTableData(fanTableData))
        {
            Log.Instance.Trace($"Bad fan table: {string.Join(", ", fanTableData)}");
            return null;
        }

        Log.Instance.Trace($"Fan table data retrieved.");
        return fanTableData;
    }

    private static FanTableType ResolveFanTableType(byte fanId, byte sensorId, int smartFanVersion, MachineInformation mi)
    {
        if (smartFanVersion <= 7)
        {
            return (fanId, sensorId, smartFanVersion) switch
            {
                (1, 1, 8) => FanTableType.CPU,
                (2, 5, 8) => FanTableType.GPU,
                (4, 4, 8) => FanTableType.GPU2,
                (1, 4, <= 8) => FanTableType.CPU,
                (1, 1, <= 8) => FanTableType.CPUSensor,
                (2, 5, <= 8) => FanTableType.GPU,
                (3, 5, <= 8) => FanTableType.GPU2,
                _ => FanTableType.Unknown,
            };
        }

        var isV3Model = GetIsV3StyleModel(mi);
        if (isV3Model)
        {
            return (fanId, sensorId) switch
            {
                (1, 1) => FanTableType.CPU,
                (2, 5) => FanTableType.GPU,
                (1, 4) => FanTableType.PCH,
                _ => FanTableType.Unknown,
            };
        }

        return (fanId, sensorId) switch
        {
            (1, 1) or (1, 4) => FanTableType.CPU,
            (2, 5) => FanTableType.GPU,
            (4, 4) or (5, 5) or (4, 1) => FanTableType.PCH,
            _ => FanTableType.Unknown,
        };
    }

    private static bool GetIsV3StyleModel(MachineInformation mi)
    {
        var affectedSeries = new[] { LegionSeries.Legion_5, LegionSeries.Legion_7 };
        var affectedModels = new[] { "Legion 5", "Legion 7", "Legion Pro 5 16IAX10H", "LOQ", "Y7000", "R7000" };
        var isAffectedSeries = affectedSeries.Any(s => Compatibility.GetLegionSeries(mi.Model, mi.MachineType) == s);
        var isAffectedModel = affectedModels.Any(m => mi.Model.Contains(m));
        return (isAffectedSeries || isAffectedModel) && mi.Generation >= 10;
    }

    private static bool IsValidFanTableData(FanTableData[]? fanTableData)
    {
        return fanTableData?.All(ftd =>
            ftd.Type != FanTableType.Unknown &&
            ftd.FanSpeeds?.Length == 10 &&
            ftd.Temps?.Length == 10) ?? false;
    }

    #endregion

    #region Fan Full Speed

    private static async Task<bool?> GetFanFullSpeedAsync(GodModePlatformConfiguration config)
    {
        try
        {
            var fanFullSpeedCap = config.Capabilities.FirstOrDefault(c => c.PropertyName == nameof(GodModePreset.FanFullSpeed));
            if (fanFullSpeedCap == null)
                return await WMI.LenovoFanMethod.FanGetFullSpeedAsync().ConfigureAwait(false);
            var value = await GetValueAsync(fanFullSpeedCap.RawId, config.CapabilityIdMask).ConfigureAwait(false);
            return value != 0;
        }
        catch (Exception ex)
        {
            Log.Instance.Trace($"Failed to read FanFullSpeed.", ex);
            return null;
        }
    }

    private static async Task SetFanFullSpeedAsync(GodModePlatformConfiguration config, bool enabled)
    {
        try
        {
            var fanFullSpeedCap = config.Capabilities.FirstOrDefault(c => c.PropertyName == nameof(GodModePreset.FanFullSpeed));
            if (fanFullSpeedCap == null)
            {
                await WMI.LenovoFanMethod.FanSetFullSpeedAsync(enabled ? 1 : 0).ConfigureAwait(false);
            }
            else
            {
                await SetValueAsync(fanFullSpeedCap.RawId, enabled ? 1 : 0, config.CapabilityIdMask).ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            Log.Instance.Trace($"Failed to set FanFullSpeed.", ex);
        }
    }

    #endregion

    #region Capability R/W Helpers

    private static Task<int> GetValueAsync(uint rawId, uint mask)
    {
        var idRaw = rawId & mask;
        return WMI.LenovoOtherMethod.GetFeatureValueAsync(idRaw);
    }

    private static Task SetValueAsync(uint rawId, int value, uint mask)
    {
        var idRaw = rawId & mask;
        return WMI.LenovoOtherMethod.SetFeatureValueAsync(idRaw, value);
    }

    private static CapabilityID AdjustCapabilityIdForPowerMode(CapabilityID id, PowerModeState powerMode)
    {
        var idRaw = (uint)id & CAPABILITY_ID_MASK;
        var powerModeRaw = ((uint)powerMode + 1) << 8;
        return (CapabilityID)(idRaw + powerModeRaw);
    }

    private static int? GetDefVal(IEnumerable<RangeCapability> capabilities, CapabilityID id, PowerModeState powerMode)
    {
        var adjustedId = AdjustCapabilityIdForPowerMode(id, powerMode);
        var value = capabilities
            .Where(c => c.Id == adjustedId)
            .Select(c => c.DefaultValue)
            .DefaultIfEmpty(-1)
            .First();
        return value < 0 ? null : value;
    }

    #endregion

    #region State Store Helpers

    private static bool IsValidStore(GodModeSettings.GodModeSettingsStore store) => store.Presets.Count != 0 && store.Presets.ContainsKey(store.ActivePresetId);

    private async Task<GodModeState> LoadStateFromStoreAsync(GodModeSettings.GodModeSettingsStore store, GodModePreset defaultState, GodModePlatformConfiguration config)
    {
        var states = new Dictionary<Guid, GodModePreset>();
        var mi = await GetMachineInformationAsync().ConfigureAwait(false);
        var isAmdDevice = mi.Properties.IsAmdDevice;

        foreach (var (id, preset) in store.Presets)
        {
            StepperValue? pboScaler = null, pboFreq = null, coreCurve = null;

            var pboSettings = new StepperValue?[]
            {
                preset.PrecisionBoostOverdriveScaler,
                preset.PrecisionBoostOverdriveBoostFrequency,
                preset.AllCoreCurveOptimizer
            };

            if (pboSettings.Any(s => s == null) && isAmdDevice)
            {
                pboScaler = new StepperValue(0, 0, 7, 1, [], 0);
                pboFreq = new StepperValue(0, 0, 200, 1, [], 0);
                coreCurve = new StepperValue(0, 0, 20, 1, [], 0);
            }

            states.Add(id, new GodModePreset
            {
                Name = preset.Name,
                CPULongTermPowerLimit = CreateStepperValue(defaultState.CPULongTermPowerLimit, preset.CPULongTermPowerLimit, preset.MinValueOffset, preset.MaxValueOffset),
                CPUShortTermPowerLimit = CreateStepperValue(defaultState.CPUShortTermPowerLimit, preset.CPUShortTermPowerLimit, preset.MinValueOffset, preset.MaxValueOffset),
                CPUPeakPowerLimit = CreateStepperValue(defaultState.CPUPeakPowerLimit, preset.CPUPeakPowerLimit, preset.MinValueOffset, preset.MaxValueOffset),
                CPUCrossLoadingPowerLimit = CreateStepperValue(defaultState.CPUCrossLoadingPowerLimit, preset.CPUCrossLoadingPowerLimit, preset.MinValueOffset, preset.MaxValueOffset),
                CPUPL2Tau = CreateStepperValue(defaultState.CPUPL2Tau, preset.CPUPL2Tau, preset.MinValueOffset, preset.MaxValueOffset),
                APUsPPTPowerLimit = CreateStepperValue(defaultState.APUsPPTPowerLimit, preset.APUsPPTPowerLimit, preset.MinValueOffset, preset.MaxValueOffset),
                CPUTemperatureLimit = CreateStepperValue(defaultState.CPUTemperatureLimit, preset.CPUTemperatureLimit, preset.MinValueOffset, preset.MaxValueOffset),
                GPUPowerBoost = CreateStepperValue(defaultState.GPUPowerBoost, preset.GPUPowerBoost, preset.MinValueOffset, preset.MaxValueOffset),
                GPUConfigurableTGP = CreateStepperValue(defaultState.GPUConfigurableTGP, preset.GPUConfigurableTGP, preset.MinValueOffset, preset.MaxValueOffset),
                GPUTemperatureLimit = CreateStepperValue(defaultState.GPUTemperatureLimit, preset.GPUTemperatureLimit, preset.MinValueOffset, preset.MaxValueOffset),
                GPUTotalProcessingPowerTargetOnAcOffsetFromBaseline = CreateStepperValue(defaultState.GPUTotalProcessingPowerTargetOnAcOffsetFromBaseline, preset.GPUTotalProcessingPowerTargetOnAcOffsetFromBaseline, preset.MinValueOffset, preset.MaxValueOffset),
                GPUToCPUDynamicBoost = CreateStepperValue(defaultState.GPUToCPUDynamicBoost, preset.GPUToCPUDynamicBoost),
                FanTableInfo = await GetFanTableInfoAsync(preset, defaultState.FanTableInfo?.Data).ConfigureAwait(false),
                FanFullSpeed = preset.FanFullSpeed,
                MinValueOffset = preset.MinValueOffset ?? defaultState.MinValueOffset,
                MaxValueOffset = preset.MaxValueOffset ?? defaultState.MaxValueOffset,
                PrecisionBoostOverdriveScaler = (isAmdDevice && pboScaler != null) ? pboScaler : preset.PrecisionBoostOverdriveScaler,
                PrecisionBoostOverdriveBoostFrequency = (isAmdDevice && pboFreq != null) ? pboFreq : preset.PrecisionBoostOverdriveBoostFrequency,
                AllCoreCurveOptimizer = (isAmdDevice && coreCurve != null) ? coreCurve : preset.AllCoreCurveOptimizer,
                EnableAllCoreCurveOptimizer = (isAmdDevice && preset.EnableAllCoreCurveOptimizer == null) ? false : preset.EnableAllCoreCurveOptimizer,
                EnableOverclocking = (isAmdDevice && preset.EnableOverclocking == null) ? false : preset.EnableOverclocking,
                Overrides = new Dictionary<PowerOverrideKey, string>(preset.Overrides ?? []),
            });
        }

        return new GodModeState
        {
            ActivePresetId = store.ActivePresetId,
            Presets = states.AsReadOnlyDictionary()
        };
    }

    private static StepperValue? CreateStepperValue(StepperValue? state, StepperValue? store = null, int? minValueOffset = 0, int? maxValueOffset = 0)
    {
        if (state is not { } stateValue)
            return null;

        if (stateValue.Steps.Length > 0)
        {
            var value = store?.Value ?? stateValue.Value;
            var steps = stateValue.Steps;
            var defaultValue = stateValue.DefaultValue;

            if (!steps.Contains(value))
            {
                var valueTemp = value;
                value = steps.MinBy(v => Math.Abs((long)v - valueTemp));
            }

            return new(value, 0, 0, 0, steps, defaultValue);
        }

        if (stateValue.Step > 0)
        {
            var value = store?.Value ?? stateValue.Value;
            var min = Math.Max(0, stateValue.Min + (minValueOffset ?? 0));
            var max = stateValue.Max + (maxValueOffset ?? 0);
            var step = stateValue.Step;
            var defaultValue = stateValue.DefaultValue;

            value = MathExtensions.RoundNearest(value, step);

            if (value < min || value > max)
                value = defaultValue ?? Math.Clamp(value, min, max);

            return new(value, min, max, step, [], defaultValue);
        }

        return null;
    }

    protected async Task<bool> IsValidFanTableAsync(FanTable fanTable)
    {
        var minimumFanTable = await GetMinimumFanTableAsync().ConfigureAwait(false);
        var minimum = minimumFanTable.GetTable();
        return fanTable.GetTable().Where((t, i) => t < minimum[i] || t > 10u).IsEmpty();
    }

    private async Task<FanTableInfo?> GetFanTableInfoAsync(GodModeSettings.GodModeSettingsStore.Preset preset, FanTableData[]? fanTableData)
    {
        Log.Instance.Trace($"Getting fan table info...");
        if (fanTableData == null) { Log.Instance.Trace($"Fan table data == null"); return null; }
        Log.Instance.Trace($"Fan table data retrieved.");

        var fanTable = preset.FanTable ?? await GetDefaultFanTableAsync().ConfigureAwait(false);
        if (!await IsValidFanTableAsync(fanTable).ConfigureAwait(false))
        {
            Log.Instance.Trace($"Fan table invalid, replacing with default...");
            fanTable = await GetDefaultFanTableAsync().ConfigureAwait(false);
        }
        return new FanTableInfo(fanTableData, fanTable);
    }

    #endregion

    #region Event

    protected async Task RaisePresetChanged(Guid presetId)
    {
        var (_, preset) = await GetActivePresetAsync().ConfigureAwait(false);
        try
        {
            var feature = IoCContainer.Resolve<PowerModeFeature>();
            await feature.EnsureCorrectWindowsPowerSettingsAreSetAsync(preset).ConfigureAwait(false);
        }
        catch { /* feature may not be available in all contexts */ }
        PresetChanged?.Invoke(this, presetId);
    }

    #endregion

    #region Init

    private async Task<GodModePlatformConfiguration> GetConfigAsync()
    {
        if (_config != null)
        {
            return _config;
        }

        var mi = await GetMachineInformationAsync().ConfigureAwait(false);
        _config = mi.Properties.GodModePlatform switch
        {
            GodModePlatform.LegacyLegion => GodModePlatformConfiguration.Legion,
            GodModePlatform.Legion => GodModePlatformConfiguration.Legion,
            GodModePlatform.NonGaming => GodModePlatformConfiguration.NonGaming,
            _ => throw new InvalidOperationException("Unsupported GodMode platform"),
        };
        return _config;
    }

    private async Task<MachineInformation> GetMachineInformationAsync()
    {
        if (_mi.HasValue)
            return _mi.Value;
        _mi = await Compatibility.GetMachineInformationAsync().ConfigureAwait(false);
        return _mi.Value;
    }

    #endregion

    #region Legacy V1 WMI Helpers

    private static async Task<StepperValue> GetCPULongTermPowerLimitLegacyAsync()
    {
        var defaultValue = await WMI.LenovoCpuMethod.CPUGetDefaultPowerLimitAsync().OrNullIfException().ConfigureAwait(false);
        var (value, min, max, step) = await WMI.LenovoCpuMethod.CPUGetLongTermPowerLimitAsync().ConfigureAwait(false);
        return new(value, min, max, step, [], defaultValue?.longTerm);
    }

    private static async Task<StepperValue> GetCPUShortTermPowerLimitLegacyAsync()
    {
        var defaultValue = await WMI.LenovoCpuMethod.CPUGetDefaultPowerLimitAsync().OrNullIfException().ConfigureAwait(false);
        var (value, min, max, step) = await WMI.LenovoCpuMethod.CPUGetShortTermPowerLimitAsync().ConfigureAwait(false);
        return new(value, min, max, step, [], defaultValue?.shortTerm);
    }

    private static async Task<StepperValue> GetCPUPeakPowerLimitLegacyAsync()
    {
        var (value, min, max, step, defaultValue) = await WMI.LenovoCpuMethod.CPUGetPeakPowerLimitAsync().ConfigureAwait(false);
        return new(value, min, max, step, [], defaultValue);
    }

    private static async Task<StepperValue> GetCPUCrossLoadingPowerLimitLegacyAsync()
    {
        var (value, min, max, step, defaultValue) = await WMI.LenovoCpuMethod.CPUGetCrossLoadingPowerLimitAsync().ConfigureAwait(false);
        return new(value, min, max, step, [], defaultValue);
    }

    private static async Task<StepperValue> GetAPUSPPTPowerLimitLegacyAsync()
    {
        var (value, min, max, step, defaultValue) = await WMI.LenovoCpuMethod.GetAPUSPPTPowerLimitAsync().ConfigureAwait(false);
        return new(value, min, max, step, [], defaultValue);
    }

    private static async Task<StepperValue> GetCPUTemperatureLimitLegacyAsync()
    {
        var (value, min, max, step, defaultValue) = await WMI.LenovoCpuMethod.CPUGetTemperatureControlAsync().ConfigureAwait(false);
        return new(value, min, max, step, [], defaultValue);
    }

    private static async Task<StepperValue> GetGPUConfigurableTGPLegacyAsync()
    {
        var defaultValue = await WMI.LenovoGpuMethod.GPUGetDefaultPPABcTGPPowerLimit().OrNullIfException().ConfigureAwait(false);
        var (value, min, max, step) = await WMI.LenovoGpuMethod.GPUGetCTGPPowerLimitAsync().ConfigureAwait(false);
        return new(value, min, max, step, [], defaultValue?.ctgp);
    }

    private static async Task<StepperValue> GetGPUPowerBoostLegacyAsync()
    {
        var defaultValue = await WMI.LenovoGpuMethod.GPUGetDefaultPPABcTGPPowerLimit().OrNullIfException().ConfigureAwait(false);
        var (value, min, max, step) = await WMI.LenovoGpuMethod.GPUGetPPABPowerLimitAsync().ConfigureAwait(false);
        return new(value, min, max, step, [], defaultValue?.ppab);
    }

    private static async Task<StepperValue> GetGPUTemperatureLimitLegacyAsync()
    {
        var (value, min, max, step, defaultValue) = await WMI.LenovoGpuMethod.GPUGetTemperatureLimitAsync().ConfigureAwait(false);
        return new(value, min, max, step, [], defaultValue);
    }

    #endregion

    #region OC Helpers

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
}
