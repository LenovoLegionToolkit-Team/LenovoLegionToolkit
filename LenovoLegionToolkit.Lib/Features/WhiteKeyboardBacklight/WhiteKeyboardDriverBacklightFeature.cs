using System;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using LenovoLegionToolkit.Lib.Extensions;
using LenovoLegionToolkit.Lib.System;
using LenovoLegionToolkit.Lib.Utils;

namespace LenovoLegionToolkit.Lib.Features.WhiteKeyboardBacklight;

public class WhiteKeyboardDriverBacklightFeature()
    : AbstractDriverFeature<WhiteKeyboardBacklightState>(Drivers.GetEnergy, Drivers.IOCTL_ENERGY_KEYBOARD)
{
    private const uint READ_CAPABILITY = 0x0001;
    private const uint READ_STATUS = 0x0002;
    private const uint WRITE_STATUS = 0x0003;

    private const uint VALID_MASK = 0x0001;
    private const uint LEVEL_MASK = 0x000F;
    private const int LEVEL_SHIFT = 16;

    public override async Task<bool> IsSupportedAsync()
    {
        try
        {
            if (AppFlags.Instance.Debug)
            {
                return true;
            }

            return await GetMaxLevelAsync().ConfigureAwait(false) >= 1;
        }
        catch
        {
            return false;
        }
    }

    public override async Task<WhiteKeyboardBacklightState[]> GetAllStatesAsync()
    {
        var maxLevel = (int)await GetMaxLevelAsync().ConfigureAwait(false);
        return Enumerable.Range(0, maxLevel + 1).Select(level => (WhiteKeyboardBacklightState)level).ToArray();
    }

    protected override uint GetInBufferValue() => GetGuard(GetCapability()) | READ_STATUS;

    protected override async Task<uint[]> ToInternalAsync(WhiteKeyboardBacklightState state)
    {
        var capability = await GetCapabilityAsync().ConfigureAwait(false);

        if ((uint)state > (capability >> 1))
            throw new InvalidOperationException("Invalid state");

        uint[] result = [GetGuard(capability) | ((uint)state << LEVEL_SHIFT) | WRITE_STATUS];
        return result;
    }

    protected override Task<WhiteKeyboardBacklightState> FromInternalAsync(uint state)
    {
        if ((state & VALID_MASK) != VALID_MASK)
            throw new InvalidOperationException("Invalid state");

        return Task.FromResult((WhiteKeyboardBacklightState)((state & LEVEL_MASK) >> 1));
    }

    private static uint GetGuard(uint capability) => (capability & ~VALID_MASK) << 3;

    private async Task<uint> GetMaxLevelAsync() => (await GetCapabilityAsync().ConfigureAwait(false)) >> 1;

    private async Task<uint> GetCapabilityAsync()
    {
        var capability = await SendCodeAsync(DriverHandle(), ControlCode, READ_CAPABILITY).ConfigureAwait(false);

        if ((capability & VALID_MASK) != VALID_MASK)
            throw new InvalidOperationException("Invalid capability");

        return capability;
    }

    private uint GetCapability()
    {
        if (!PInvokeExtensions.DeviceIoControl(DriverHandle(), ControlCode, READ_CAPABILITY, out uint capability))
            throw new InvalidOperationException($"DeviceIoControl failed, error: {Marshal.GetLastWin32Error()}");

        if ((capability & VALID_MASK) != VALID_MASK)
            throw new InvalidOperationException("Invalid capability");

        return capability;
    }
}
