using System;
using System.Threading.Tasks;
using LenovoLegionToolkit.Lib.System.Management;

namespace LenovoLegionToolkit.Lib.Controllers;

public sealed class PowerButtonLedController
{
    public const int DefaultKeyboardBrightness = 7;
    private static readonly int[] BrightnessMap = [1, 2, 2, 2, 3, 3, 3, 4, 4, 4];
    public async Task<bool> IsSupportedAsync()
    {
        var capability = await WMI.PowerButtonLed.GetCapabilityAsync().ConfigureAwait(false);

        return capability is { } value && (value & 6) == 6;
    }

    public async Task SetKeyboardBrightnessAsync(int brightness)
    {
        if (brightness is < 0 or > 9)
        {
            throw new ArgumentOutOfRangeException(nameof(brightness));
        }

        if (!await IsSupportedAsync().ConfigureAwait(false))
        {
            throw new NotSupportedException("Power-button LED read/write capability is unavailable.");
        }

        var level = BrightnessMap[brightness];
        var (stateType, _) = await WMI.PowerButtonLed.GetStatusAsync().ConfigureAwait(false);

        await WMI.PowerButtonLed.SetStatusAsync(stateType, level).ConfigureAwait(false);

        var (_, actualLevel) = await WMI.PowerButtonLed.GetStatusAsync().ConfigureAwait(false);
        if (actualLevel != level)
        {
            throw new InvalidOperationException($"Power-button LED brightness was not confirmed by firmware (requested {level}, read {actualLevel}).");
        }
    }
}
