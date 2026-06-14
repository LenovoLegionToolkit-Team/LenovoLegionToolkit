using System;
using System.Linq;
using System.Threading.Tasks;
using LenovoLegionToolkit.Lib.System.Management;
using LenovoLegionToolkit.Lib.Utils;

namespace LenovoLegionToolkit.Lib.Features.Hybrid;

public class BiosHybridModeFeature
{
    private const string GRAPHICS_DEVICE = "GraphicsDevice";
    private const string UMA_GRAPHICS = "UMA Graphics";
    private const string SWITCHABLE_GRAPHICS = "Switchable Graphics";
    private const string DISCRETE_GRAPHICS = "Discrete Graphics";

    public async Task<bool> IsSupportedAsync()
    {
        try
        {
            var exist = await WMI.LenovoBiosSetting.ExistAsync().ConfigureAwait(false);
            if (!exist)
                return false;

            var selections = await WMI.LenovoBiosSetting.GetBiosSelectionsAsync(GRAPHICS_DEVICE).ConfigureAwait(false);
            return selections.Any(item => item.Contains("UMA", StringComparison.OrdinalIgnoreCase));
        }
        catch (Exception ex)
        {
            Log.Instance.Trace($"Failed to check UMA support", ex);
            return false;
        }
    }

    public async Task<bool> IsUMAEnabledAsync()
    {
        try
        {
            var setting = await WMI.LenovoBiosSetting.GetBiosSettingAsync(GRAPHICS_DEVICE).ConfigureAwait(false);
            return !string.IsNullOrEmpty(setting) && setting.Contains("UMA", StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex)
        {
            Log.Instance.Trace($"Failed to read GraphicsDevice", ex);
            return false;
        }
    }

    public Task SetUMAAsync() => SetGraphicsDeviceAsync(UMA_GRAPHICS);

    public Task SetSwitchableAsync() => SetGraphicsDeviceAsync(SWITCHABLE_GRAPHICS);

    public Task SetDiscreteAsync() => SetGraphicsDeviceAsync(DISCRETE_GRAPHICS);

    public async Task SetStateAsync(HybridModeState state)
    {
        await SetGraphicsDeviceAsync(GetBiosValueForState(state)).ConfigureAwait(false);
    }

    public async Task<HybridModeState> GetStateAsync()
    {
        var setting = await WMI.LenovoBiosSetting.GetBiosSettingAsync(GRAPHICS_DEVICE).ConfigureAwait(false);
        return GetStateFromBiosValue(setting);
    }

    public async Task<HybridModeState[]> GetAllStatesAsync()
    {
        var mi = await Compatibility.GetMachineInformationAsync().ConfigureAwait(false);

        if (mi.Properties.SupportsGSync && await IsSupportedAsync().ConfigureAwait(false))
        {
            return (mi.Properties.SupportsIGPUMode) switch
            {
                true => [HybridModeState.On, HybridModeState.OnIGPUOnly, HybridModeState.OnAuto, HybridModeState.Off, HybridModeState.UMA],
                false => [HybridModeState.On, HybridModeState.Off, HybridModeState.UMA]
            };
        }

        return [];
    }

    private async Task SetGraphicsDeviceAsync(string value)
    {
        await WMI.LenovoBiosSetting.SetBiosSettingAsync(GRAPHICS_DEVICE, value).ConfigureAwait(false);
        await WMI.LenovoBiosSetting.SaveBiosSettingAsync().ConfigureAwait(false);
    }

    private string GetBiosValueForState(HybridModeState state)
    {
        return state switch
        {
            HybridModeState.On => SWITCHABLE_GRAPHICS,
            HybridModeState.OnIGPUOnly => SWITCHABLE_GRAPHICS,
            HybridModeState.OnAuto => SWITCHABLE_GRAPHICS,
            HybridModeState.UMA => UMA_GRAPHICS,
            HybridModeState.Off => DISCRETE_GRAPHICS,
            _ => throw new ArgumentOutOfRangeException(nameof(state), $"Unsupported state: {state}")
        };
    }

    private HybridModeState GetStateFromBiosValue(string biosValue)
    {
        return biosValue switch
        {
            SWITCHABLE_GRAPHICS => HybridModeState.On,
            UMA_GRAPHICS => HybridModeState.UMA,
            DISCRETE_GRAPHICS => HybridModeState.Off,
            _ => throw new ArgumentOutOfRangeException(nameof(biosValue), $"Unsupported BIOS value: {biosValue}")
        };
    }
}
