using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using LenovoLegionToolkit.Lib.Extensions;
using LenovoLegionToolkit.Lib.System;
using LenovoLegionToolkit.Lib.Utils;
using Microsoft.Win32.SafeHandles;

namespace LenovoLegionToolkit.Lib.Features;

public class ChargeThresholdFeature : IFeature<ChargeThreshold>
{
    private const string PWRMGRV_HIVE = "HKEY_LOCAL_MACHINE";
    private const string PWRMGRV_DATA_PATH = @"SOFTWARE\WOW6432Node\Lenovo\PWRMGRV\ConfKeys\Data";
    private const string BATTERY_KEY_PREFIX = "Battery";
    private const string BARCODE_NUMBER_VALUE = "Barcode Number";
    private const string CHARGE_START_CONTROL_VALUE = "ChargeStartControl";
    private const string CHARGE_STOP_CONTROL_VALUE = "ChargeStopControl";
    private const string CHARGE_START_PERCENTAGE_VALUE = "ChargeStartPercentage";
    private const string CHARGE_STOP_PERCENTAGE_VALUE = "ChargeStopPercentage";

    private const int BATTERY_INDEX_MIN = 1;
    private const int BATTERY_INDEX_MAX = 2;
    private const int DRIVER_REQUEST_INDEX_MASK = 0x3;
    private const int DRIVER_REQUEST_INDEX_SHIFT = 8;
    private const int THRESHOLD_VALUE_MIN = 2;
    private const int THRESHOLD_VALUE_MAX = 99;
    private const int THRESHOLD_MODE_AUTOMATIC = 0;
    private const int THRESHOLD_MODE_ENABLED = 1;

    private const int DEBUG_PLACEHOLDER_START = 50;
    private const int DEBUG_PLACEHOLDER_STOP = 60;

    public async Task<bool> IsSupportedAsync()
    {
        try
        {
            _ = await GetStateAsync().ConfigureAwait(false);

            return true;
        }
        catch (Exception ex)
        {
            Log.Instance.Trace($"Failed to check support [feature={GetType().Name}]", ex);

            return false;
        }
    }

    public Task<ChargeThreshold[]> GetAllStatesAsync() => Task.FromResult<ChargeThreshold[]>([]);

    public async Task<ChargeThreshold> GetStateAsync() => await Task.Run(GetState).ConfigureAwait(false);

    public async Task SetStateAsync(ChargeThreshold state)
    {
        var batteries = GetBatteries();

        if (batteries.Length == 0)
        {
            if (AppFlags.Instance.Debug)
            {
                Log.Instance.Trace($"Charge threshold is not supported, skipping.");

                return;
            }

            throw new InvalidOperationException("No battery with charge threshold support was found.");
        }

        foreach (var (_, key) in batteries)
        {
            Registry.SetValue(PWRMGRV_HIVE, key, CHARGE_START_PERCENTAGE_VALUE, state.Start);
            Registry.SetValue(PWRMGRV_HIVE, key, CHARGE_STOP_PERCENTAGE_VALUE, state.Stop);
            Registry.SetValue(PWRMGRV_HIVE, key, CHARGE_START_CONTROL_VALUE, state.Enabled ? 1 : 0);
            Registry.SetValue(PWRMGRV_HIVE, key, CHARGE_STOP_CONTROL_VALUE, state.Enabled ? 1 : 0);
        }

        var handle = Drivers.GetIbmPmDrv();

        foreach (var (index, _) in batteries)
        {
            if (state.Enabled)
            {
                SendCode(handle, Drivers.IOCTL_IBMPMDRV_CHARGE_THRESHOLD_STOP, ToDriverRequest(index, state.Stop));
                SendCode(handle, Drivers.IOCTL_IBMPMDRV_CHARGE_THRESHOLD_START, ToDriverRequest(index, state.Start));
                SendCode(handle, Drivers.IOCTL_IBMPMDRV_CHARGE_THRESHOLD_MODE, ToDriverRequest(index, THRESHOLD_MODE_ENABLED));
            }
            else
            {
                SendCode(handle, Drivers.IOCTL_IBMPMDRV_CHARGE_THRESHOLD_STOP, ToDriverRequest(index, 0));
                SendCode(handle, Drivers.IOCTL_IBMPMDRV_CHARGE_THRESHOLD_START, ToDriverRequest(index, 0));
                SendCode(handle, Drivers.IOCTL_IBMPMDRV_CHARGE_THRESHOLD_MODE, THRESHOLD_MODE_AUTOMATIC);
            }
        }

        var actual = await GetStateAsync().ConfigureAwait(false);

        if (!state.Equals(actual))
            Log.Instance.Trace($"Charge threshold mismatch, Actual: {actual}, Target: {state}");

        VerifyWithDriver(handle, batteries);
    }

    private static void VerifyWithDriver(SafeFileHandle handle, (int Index, string Key)[] batteries)
    {
        foreach (var (index, _) in batteries)
        {
            var request = (uint)index;

            var startOk = PInvokeExtensions.DeviceIoControl(handle, Drivers.IOCTL_IBMPMDRV_CHARGE_THRESHOLD_START_STATUS, request, out int startRaw);
            var stopOk = PInvokeExtensions.DeviceIoControl(handle, Drivers.IOCTL_IBMPMDRV_CHARGE_THRESHOLD_STOP_STATUS, request, out int stopRaw);

            Log.Instance.Trace($"Charge threshold driver readback. [index={index}, startOk={startOk}, stopOk={stopOk}, startRaw={startRaw:X8}, stopRaw={stopRaw:X8}]");
        }
    }

    private static ChargeThreshold GetState()
    {
        var batteries = GetBatteries();

        if (batteries.Length == 0)
        {
            if (AppFlags.Instance.Debug)
                return new ChargeThreshold(false, DEBUG_PLACEHOLDER_START, DEBUG_PLACEHOLDER_STOP);

            throw new InvalidOperationException("No battery with charge threshold support was found.");
        }

        var (_, key) = batteries[0];

        var start = Math.Clamp(Registry.GetValue(PWRMGRV_HIVE, key, CHARGE_START_PERCENTAGE_VALUE, 0), THRESHOLD_VALUE_MIN, THRESHOLD_VALUE_MAX);
        var stop = Math.Clamp(Registry.GetValue(PWRMGRV_HIVE, key, CHARGE_STOP_PERCENTAGE_VALUE, 0), THRESHOLD_VALUE_MIN, THRESHOLD_VALUE_MAX);
        var startControl = Registry.GetValue(PWRMGRV_HIVE, key, CHARGE_START_CONTROL_VALUE, 0);
        var stopControl = Registry.GetValue(PWRMGRV_HIVE, key, CHARGE_STOP_CONTROL_VALUE, 0);

        return new(startControl != 0 || stopControl != 0, start, stop);
    }

    private static (int Index, string Key)[] GetBatteries()
    {
        var result = new List<(int, string)>();

        foreach (var subKey in Registry.GetSubKeys(PWRMGRV_HIVE, PWRMGRV_DATA_PATH))
        {
            var name = subKey[(subKey.LastIndexOf('\\') + 1)..];

            if (!name.StartsWith(BATTERY_KEY_PREFIX, StringComparison.OrdinalIgnoreCase))
                continue;

            if (!int.TryParse(name[BATTERY_KEY_PREFIX.Length..], out var index))
                continue;

            if (index is < BATTERY_INDEX_MIN or > BATTERY_INDEX_MAX)
                continue;

            var barcode = Registry.GetValue(PWRMGRV_HIVE, subKey, BARCODE_NUMBER_VALUE, string.Empty);

            if (string.IsNullOrWhiteSpace(barcode))
                continue;

            var key = $@"{PWRMGRV_DATA_PATH}\{barcode}";

            if (!Registry.ValueExists(PWRMGRV_HIVE, key, CHARGE_START_PERCENTAGE_VALUE))
                continue;

            result.Add((index, key));
        }

        return [.. result];
    }

    private static uint ToDriverRequest(int index, int value) =>
        ((uint)(index & DRIVER_REQUEST_INDEX_MASK) << DRIVER_REQUEST_INDEX_SHIFT) | (uint)value;

    private static void SendCode(SafeFileHandle handle, uint controlCode, uint value)
    {
        if (!PInvokeExtensions.DeviceIoControl(handle, controlCode, value, out int result))
            PInvokeExtensions.ThrowIfWin32Error("DeviceIoControl, IBMPmDrv");

        if (result < 0)
            throw new InvalidOperationException($"Charge threshold was rejected. [controlCode={controlCode:X8}, value={value}, result={result:X8}]");

        Log.Instance.Trace($"Charge threshold accepted. [controlCode={controlCode:X8}, value={value}, result={result:X8}]");
    }
}
