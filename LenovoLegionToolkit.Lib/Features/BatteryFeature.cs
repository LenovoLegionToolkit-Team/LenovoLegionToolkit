using System;
using System.Threading.Tasks;
using LenovoLegionToolkit.Lib.System;
using LenovoLegionToolkit.Lib.Utils;

namespace LenovoLegionToolkit.Lib.Features;

public class BatteryFeature() : AbstractDriverFeature<BatteryState>(Drivers.GetEnergy, Drivers.IOCTL_ENERGY_BATTERY_CHARGE_MODE)
{
    private const string BATTERY_CHARGE_MODE_HIVE = "HKEY_CURRENT_USER";
    private const string BATTERY_CHARGE_MODE_PATH = "Software\\Lenovo\\VantageService\\AddinData\\IdeaNotebookAddin";
    private const string BATTERY_CHARGE_MODE_KEY = "BatteryChargeMode";
    private const string BATTERY_CHARGE_MODE_NORMAL = "Normal";
    private const string BATTERY_CHARGE_MODE_RAPID_CHARGE = "Quick";
    private const string BATTERY_CHARGE_MODE_CONSERVATION = "Storage";

    protected override uint GetInBufferValue() => 0xFF;

    public override Task<bool> IsSupportedAsync()
        => Battery.IsBatteryPresent ? base.IsSupportedAsync() : Task.FromResult(false);

    protected override Task<uint[]> ToInternalAsync(BatteryState state)
    {
        uint[] result = state switch
        {
            BatteryState.Conservation => new uint[] { 0x08, 0x03 },
            BatteryState.Normal       => new uint[] { 0x05, 0x08 },
            BatteryState.RapidCharge  => new uint[] { 0x05, 0x07 },
            _                         => throw new InvalidOperationException("Invalid state")
        };
        return Task.FromResult(result);
    }

    protected override Task<BatteryState> FromInternalAsync(uint state)
    {
        // Storage - bit 0x20
        if ((state & 0x20) != 0)
            return Task.FromResult(BatteryState.Conservation);

        // Express - bit 0x04
        if ((state & 0x04) != 0)
            return Task.FromResult(BatteryState.RapidCharge);

        return Task.FromResult(BatteryState.Normal);
    }

    public override async Task SetStateAsync(BatteryState state)
    {
        await base.SetStateAsync(state).ConfigureAwait(false);
        var actual = await GetStateAsync().ConfigureAwait(false);

        if (actual != state)
        {
            Log.Instance.Trace($"Battery Mode mismatch, Actual: {actual}, Target: {state}");
        }

        SetStateInRegistry(actual);
    }

    public async Task EnsureCorrectBatteryModeIsSetAsync()
    {
        var state = GetStateFromRegistry();
        if (!state.HasValue)
        {
            return;
        }

        if (await GetStateAsync().ConfigureAwait(false) == state.Value)
        {
            return;
        }

        await SetStateAsync(state.Value).ConfigureAwait(false);
    }

    private static BatteryState? GetStateFromRegistry()
    {
        var batteryModeString = Registry.GetValue(BATTERY_CHARGE_MODE_HIVE, BATTERY_CHARGE_MODE_PATH, BATTERY_CHARGE_MODE_KEY, string.Empty);
        return batteryModeString switch
        {
            BATTERY_CHARGE_MODE_NORMAL => BatteryState.Normal,
            BATTERY_CHARGE_MODE_RAPID_CHARGE => BatteryState.RapidCharge,
            BATTERY_CHARGE_MODE_CONSERVATION => BatteryState.Conservation,
            _ => null
        };
    }

    private static void SetStateInRegistry(BatteryState state)
    {
        var batteryModeString = state switch
        {
            BatteryState.Normal => BATTERY_CHARGE_MODE_NORMAL,
            BatteryState.RapidCharge => BATTERY_CHARGE_MODE_RAPID_CHARGE,
            BatteryState.Conservation => BATTERY_CHARGE_MODE_CONSERVATION,
            _ => null
        };

        if (batteryModeString is null)
            return;

        Registry.SetValue(BATTERY_CHARGE_MODE_HIVE, BATTERY_CHARGE_MODE_PATH, BATTERY_CHARGE_MODE_KEY, batteryModeString);
    }
}