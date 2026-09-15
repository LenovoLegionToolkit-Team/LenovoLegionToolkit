using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using LenovoLegionToolkit.Lib.Controllers.GodMode;
using LenovoLegionToolkit.Lib.Extensions;
using LenovoLegionToolkit.Lib.System.Management;
using LenovoLegionToolkit.Lib.Utils;
using Newtonsoft.Json;

namespace LenovoLegionToolkit.Lib.Automation.Steps;

[method: JsonConstructor]
public class FanMaxSpeedAutomationStep(ToggleState state)
    : IAutomationStep<ToggleState>
{
    public ToggleState State { get; } = state;

    private readonly GodModeController _godModeController = IoCContainer.Resolve<GodModeController>();

    public async Task<bool> IsSupportedAsync()
    {
        if (AppFlags.Instance.Debug)
        {
            return true;
        }

        var mi = await Compatibility.GetMachineInformationAsync().ConfigureAwait(false);
        return await GetCurrentValueAsync(mi).ConfigureAwait(false) is not null;
    }

    public Task<ToggleState[]> GetAllStatesAsync() => Task.FromResult(Enum.GetValues<ToggleState>());

    public IAutomationStep DeepCopy() => new FanMaxSpeedAutomationStep(State);

    public async Task RunAsync(AutomationContext context, AutomationEnvironment environment, CancellationToken token)
    {
        var mi = await Compatibility.GetMachineInformationAsync().ConfigureAwait(false);

        bool? applied = mi.Properties.GodModePlatform switch
        {
            GodModePlatform.LegacyLegion => await HandleLegacyAsync(mi).ConfigureAwait(false),
            GodModePlatform.Legion or GodModePlatform.NonGaming => await HandleModernAsync(mi).ConfigureAwait(false),
            _ => null
        };

        if (applied.HasValue)
            await TryUpdatePresetAsync(applied.Value).ConfigureAwait(false);
    }

    private async Task<bool?> HandleLegacyAsync(MachineInformation mi)
    {
        var currentValue = await GetCurrentValueAsync(mi).ConfigureAwait(false);
        if (currentValue is null)
            return null;

        var currentSpeed = currentValue != 0;
        var targetState = State switch
        {
            ToggleState.On => true,
            ToggleState.Off => false,
            ToggleState.Toggle => !currentSpeed,
            _ => currentSpeed
        };

        if (currentSpeed != targetState)
            await WMI.LenovoFanMethod.FanSetFullSpeedAsync(targetState ? 1 : 0).ConfigureAwait(false);

        return targetState;
    }

    private async Task<bool?> HandleModernAsync(MachineInformation mi)
    {
        var idRaw = GetFanFullSpeedId(mi);

        var currentValue = await GetCurrentValueAsync(mi).ConfigureAwait(false);
        if (currentValue is null)
            return null;

        var targetValue = State switch
        {
            ToggleState.On => 1,
            ToggleState.Off => 0,
            ToggleState.Toggle => currentValue == 0 ? 1 : 0,
            _ => currentValue.Value
        };

        if (currentValue != targetValue)
            await WMI.LenovoOtherMethod.SetFeatureValueAsync(idRaw, targetValue).ConfigureAwait(false);

        return targetValue != 0;
    }

    private static async Task<int?> GetCurrentValueAsync(MachineInformation mi)
    {
        try
        {
            switch (mi.Properties.GodModePlatform)
            {
                case GodModePlatform.LegacyLegion:
                    return await WMI.LenovoFanMethod.FanGetFullSpeedAsync().ConfigureAwait(false) ? 1 : 0;
                case GodModePlatform.Legion:
                case GodModePlatform.NonGaming:
                    return await WMI.LenovoOtherMethod.GetFeatureValueAsync(GetFanFullSpeedId(mi)).ConfigureAwait(false);
                default:
                    return null;
            }
        }
        catch (Exception ex)
        {
            Log.Instance.Trace($"Failed to read Fan Full Speed. [platform={mi.Properties.GodModePlatform}]", ex);
            return null;
        }
    }

    private static uint GetFanFullSpeedId(MachineInformation mi)
    {
        uint fanFullSpeedId = mi.Properties.GodModePlatform switch
        {
            GodModePlatform.NonGaming => (uint)NonGamingCapabilityID.FanFullSpeed,
            _ => (uint)CapabilityID.FanFullSpeed,
        };

        return fanFullSpeedId & 0xFFFF00FF;
    }

    private async Task TryUpdatePresetAsync(bool targetState)
    {
        var state = await _godModeController.GetStateAsync().ConfigureAwait(false);
        var activePresetId = state.ActivePresetId;
        var preset = state.Presets[activePresetId];

        if (preset.FanFullSpeed is null)
            return;

        var updatedPreset = preset with { FanFullSpeed = targetState };
        var updatedPresets = new Dictionary<Guid, GodModePreset>(state.Presets)
        {
            [activePresetId] = updatedPreset
        };

        await _godModeController.SetStateAsync(new GodModeState
        {
            ActivePresetId = activePresetId,
            Presets = updatedPresets.AsReadOnlyDictionary()
        }).ConfigureAwait(false);
    }
}
