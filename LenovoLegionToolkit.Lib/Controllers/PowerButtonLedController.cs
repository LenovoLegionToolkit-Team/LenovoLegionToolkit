using System;
using System.Threading.Tasks;
using LenovoLegionToolkit.Lib.System.Management;
using LenovoLegionToolkit.Lib.Utils;

namespace LenovoLegionToolkit.Lib.Controllers;

public sealed class PowerButtonLedController
{
    public const int MinBrightness = 1;
    public const int MaxBrightness = 4;

    private const int SyncOff = 0;
    private const int SyncOn = 2;

    public async Task<bool> IsSupportedAsync()
    {
        try
        {
            if (AppFlags.Instance.Debug)
            {
                return true;
            }

            var mi = await Compatibility.GetMachineInformationAsync().ConfigureAwait(false);

            return mi.Features.Source == MachineInformation.FeatureData.SourceType.CapabilityData &&
                   mi.Features[CapabilityID.PowerLedBrightness] &&
                   !mi.Properties.IsExcludedFromLenovoLighting;
        }
        catch
        {
            return false;
        }
    }

    public async Task<bool> GetSyncEnabledAsync()
    {
        var value = await WMI.LenovoOtherMethod.GetFeatureValueAsync(CapabilityID.PowerLedBrightness).ConfigureAwait(false);

        return value != SyncOff;
    }

    public async Task SetSyncEnabledAsync(bool enabled)
    {
        var target = enabled ? SyncOn : SyncOff;

        await WMI.LenovoOtherMethod.SetFeatureValueAsync(CapabilityID.PowerLedBrightness, target).ConfigureAwait(false);

        var value = await WMI.LenovoOtherMethod.GetFeatureValueAsync(CapabilityID.PowerLedBrightness).ConfigureAwait(false);
        if (value != target)
        {
            throw new InvalidOperationException($"Power button light sync was not confirmed by firmware (requested {target}, read {value}).");
        }
    }

    public async Task SetBrightnessAsync(int brightness)
    {
        if (brightness is < MinBrightness or > MaxBrightness)
        {
            throw new ArgumentOutOfRangeException(nameof(brightness));
        }

        var (stateType, _) = await WMI.PowerButtonLed.GetStatusAsync().ConfigureAwait(false);

        await WMI.PowerButtonLed.SetStatusAsync(stateType, brightness).ConfigureAwait(false);
    }
}
