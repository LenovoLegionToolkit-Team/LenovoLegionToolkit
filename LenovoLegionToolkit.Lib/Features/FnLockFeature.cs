using System;
using System.Threading.Tasks;
using LenovoLegionToolkit.Lib.Extensions;
using LenovoLegionToolkit.Lib.System;
using LenovoLegionToolkit.Lib.Utils;

namespace LenovoLegionToolkit.Lib.Features;

public class FnLockFeature() : AbstractDriverFeature<FnLockState>(Drivers.GetEnergy, Drivers.IOCTL_ENERGY_SETTINGS, useDriverQueue: true)
{
    protected override uint GetInBufferValue() => 0x2;

    protected override async Task<uint[]> ToInternalAsync(FnLockState state)
    {
        var hardwareState = await TranslateStateAsync(state).ConfigureAwait(false);
        var lockOn = hardwareState switch
        {
            FnLockState.On => true,
            FnLockState.Off => false,
            _ => throw new InvalidOperationException("Invalid state"),
        };

        return lockOn ? [0xE] : [0xF];
    }

    protected override async Task<FnLockState> FromInternalAsync(uint state)
    {
        var hardwareState = state.GetNthBit(10) ? FnLockState.On : FnLockState.Off;
        return await TranslateStateAsync(hardwareState).ConfigureAwait(false);
    }

    private static async Task<FnLockState> TranslateStateAsync(FnLockState state)
    {
        var mi = await Compatibility.GetMachineInformationAsync().ConfigureAwait(false);
        if (mi.LegionSeries <= LegionSeries.Legion_Legacy || mi.LegionSeries == LegionSeries.LOQ)
        {
            return state;
        }

        return state switch
        {
            FnLockState.On => FnLockState.Off,
            FnLockState.Off => FnLockState.On,
            _ => throw new InvalidOperationException("Invalid state"),
        };
    }
}
