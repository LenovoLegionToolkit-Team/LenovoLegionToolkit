using System.Collections.Generic;

namespace LenovoLegionToolkit.Lib.Controllers.GodMode;

public sealed record GodModePlatformConfiguration
{
    public GodModePlatform Platform { get; init; }
    public uint CapabilityIdMask { get; init; } = 0xFFFF00FF;
    public FanTable MinimumFanTable { get; init; } = new([1, 1, 1, 1, 1, 1, 1, 1, 3, 5]);
    public List<GodModeCapabilityEntry> Capabilities { get; init; } = [];

    public bool UseCapabilityDataForDefaults { get; init; } = true;
    public bool SupportsPerModeDefaults { get; init; } = true;

    public static GodModePlatformConfiguration LegacyLegion { get; } = new()
    {
        Platform = GodModePlatform.LegacyLegion,
    };

    public static GodModePlatformConfiguration Legion { get; } = new()
    {
        Platform = GodModePlatform.Legion,
        CapabilityIdMask = 0xFFFF00FF,
        MinimumFanTable = new FanTable([1, 1, 1, 1, 1, 1, 1, 1, 3, 5]),
        Capabilities =
        [
            new() { CapabilityId = CapabilityID.CPULongTermPowerLimit, PropertyName = nameof(GodModePreset.CPULongTermPowerLimit) },
            new() { CapabilityId = CapabilityID.CPUShortTermPowerLimit, PropertyName = nameof(GodModePreset.CPUShortTermPowerLimit) },
            new() { CapabilityId = CapabilityID.CPUPeakPowerLimit, PropertyName = nameof(GodModePreset.CPUPeakPowerLimit) },
            new() { CapabilityId = CapabilityID.CPUCrossLoadingPowerLimit, PropertyName = nameof(GodModePreset.CPUCrossLoadingPowerLimit) },
            new() { CapabilityId = CapabilityID.CPUPL2Tau, PropertyName = nameof(GodModePreset.CPUPL2Tau) },
            new() { CapabilityId = CapabilityID.APUsPPTPowerLimit, PropertyName = nameof(GodModePreset.APUsPPTPowerLimit) },
            new() { CapabilityId = CapabilityID.CPUTemperatureLimit, PropertyName = nameof(GodModePreset.CPUTemperatureLimit) },
            new() { CapabilityId = CapabilityID.GPUPowerBoost, PropertyName = nameof(GodModePreset.GPUPowerBoost), FailAllowed = true },
            new() { CapabilityId = CapabilityID.GPUConfigurableTGP, PropertyName = nameof(GodModePreset.GPUConfigurableTGP), FailAllowed = true },
            new() { CapabilityId = CapabilityID.GPUTemperatureLimit, PropertyName = nameof(GodModePreset.GPUTemperatureLimit), FailAllowed = true },
            new() { CapabilityId = CapabilityID.GPUTotalProcessingPowerTargetOnAcOffsetFromBaseline, PropertyName = nameof(GodModePreset.GPUTotalProcessingPowerTargetOnAcOffsetFromBaseline), FailAllowed = true },
            new() { CapabilityId = CapabilityID.GPUToCPUDynamicBoost, PropertyName = nameof(GodModePreset.GPUToCPUDynamicBoost), FailAllowed = true },
            new() { CapabilityId = CapabilityID.FanFullSpeed, PropertyName = nameof(GodModePreset.FanFullSpeed) },
        ],
    };

    public static GodModePlatformConfiguration NonGaming { get; } = new()
    {
        Platform = GodModePlatform.NonGaming,
        CapabilityIdMask = 0xFFFF00FF,
        MinimumFanTable = new FanTable([1, 1, 1, 1, 1, 1, 1, 1, 3, 5]),
        UseCapabilityDataForDefaults = false,
        SupportsPerModeDefaults = false,
        Capabilities =
        [
            new() { CapabilityId = NonGamingCapabilityID.CPUShortTermPowerLimit, PropertyName = nameof(GodModePreset.CPUShortTermPowerLimit), Min = 0, Max = 255, Step = 1, DefaultValue = 0 },
            new() { CapabilityId = NonGamingCapabilityID.CPULongTermPowerLimit, PropertyName = nameof(GodModePreset.CPULongTermPowerLimit), Min = 0, Max = 255, Step = 1, DefaultValue = 0 },
            new() { CapabilityId = NonGamingCapabilityID.CPUTemperatureLimit, PropertyName = nameof(GodModePreset.CPUTemperatureLimit), Min = 0, Max = 100, Step = 1, DefaultValue = 100 },
            new() { CapabilityId = NonGamingCapabilityID.CPUPL2Tau, PropertyName = nameof(GodModePreset.CPUPL2Tau), Steps = [20, 24, 28, 32, 40, 48, 56, 64, 80, 96, 112, 128, 160], DefaultValue = 56 },
            new() { CapabilityId = NonGamingCapabilityID.GPUConfigurableTGP, PropertyName = nameof(GodModePreset.GPUConfigurableTGP), Steps = [55, 65, 75, 85, 95, 105, 115], DefaultValue = 55 },
            new() { CapabilityId = NonGamingCapabilityID.GPUPowerBoost, PropertyName = nameof(GodModePreset.GPUPowerBoost), Steps = [0, 5, 10, 15, 20, 25], DefaultValue = 0 },
            new() { CapabilityId = NonGamingCapabilityID.GPUTemperatureLimit, PropertyName = nameof(GodModePreset.GPUTemperatureLimit), Min = 0, Max = 100, Step = 1, DefaultValue = 100 },
            new() { CapabilityId = NonGamingCapabilityID.GPUTotalProcessingPowerTargetOnAcOffsetFromBaseline, PropertyName = nameof(GodModePreset.GPUTotalProcessingPowerTargetOnAcOffsetFromBaseline), Min = 0, Max = 255, Step = 1, DefaultValue = 0, FailAllowed = true },
            new() { CapabilityId = NvApiCapabilityID.GPUTotalProcessingPowerTargetOnAcOffsetFromBaseline, PropertyName = nameof(GodModePreset.GPUTotalProcessingPowerTargetOnAcOffsetFromBaselineNVAPI), Min = 0, Max = 500, Step = 1, DefaultValue = 0, FailAllowed = true },
            new() { CapabilityId = NonGamingCapabilityID.GPUToCPUDynamicBoost, PropertyName = nameof(GodModePreset.GPUToCPUDynamicBoost), Min = 0, Max = 255, Step = 1, DefaultValue = 0 },
            new() { CapabilityId = NonGamingCapabilityID.FanFullSpeed, PropertyName = nameof(GodModePreset.FanFullSpeed), Min = 0, Max = 1, Step = 1, DefaultValue = 0, FailAllowed = true },
        ],
    };
}
