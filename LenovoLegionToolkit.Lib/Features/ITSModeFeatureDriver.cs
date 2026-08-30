using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using LenovoLegionToolkit.Lib.System;
using LenovoLegionToolkit.Lib.Utils;
using Microsoft.Win32;
using Registry = Microsoft.Win32.Registry;

namespace LenovoLegionToolkit.Lib.Features;

public class ITSModeFeatureDriver : AbstractDriverFeature<ITSMode>
{
    private const uint GET_CURRENT_MODE = 2;
    private const uint GET_EXTENDED_CAPABILITIES = 10;
    private const uint RESET_MODE = 0x000001FF;

    private const uint COMMAND_VALID = 0x1;
    private const uint SET_COMMAND_VALID = 0x100000;
    private const int SET_FUNCTION_SHIFT = 12;
    private const int SET_MODE_SHIFT = 16;
    private const int GET_FUNCTION_SHIFT = 8;
    private const int GET_MODE_SHIFT = 12;

    private const uint FUNCTION_INTELLIGENT = 5;
    private const uint FUNCTION_ITS = 11;
    private const uint MODE_INTELLIGENT = 15;
    private const uint MODE_PERFORMANCE = 2;
    private const uint MODE_COOL = 3;

    private const uint GEEK_CAPABILITY_MASK = 0x00020001;
    private const uint GEEK_DISABLED = 0x000F100B;
    private const uint GEEK_ENABLED = 0x001F100B;

    private const string REG_KEY_DISPATCHER = @"SYSTEM\CurrentControlSet\Services\LenovoProcessManagement\Performance\PowerSlider";
    private const string VAL_VERSION = "Version";
    private const string VAL_ITS_CUR_SET_V = "ITS_CurrentSettingV";
    private const uint DISPATCHER_VERSION_3 = 8192U;

    private static readonly ITSMode[] _allStatesWithGeek = [ITSMode.ItsAuto, ITSMode.MmcCool, ITSMode.MmcPerformance, ITSMode.MmcGeek];
    private static readonly ITSMode[] _allStatesWithoutGeek = [ITSMode.ItsAuto, ITSMode.MmcCool, ITSMode.MmcPerformance];

    private volatile int _geekModeState = -1;

    public ITSModeFeatureDriver()
        : base(Drivers.GetEnergy, Drivers.IOCTL_ENERGY_SMART_POWER, useDriverQueue: true) { }

    public override async Task<bool> IsSupportedAsync()
    {
        try
        {
            if (AppFlags.Instance.Debug)
            {
                return true;
            }

            if (ReadDispatcherVersion() < DISPATCHER_VERSION_3)
            {
                return false;
            }

            var raw = await SendCodeAsync(DriverHandle(), ControlCode, GET_CURRENT_MODE, bypassQueue: true).ConfigureAwait(false);
            return (raw & COMMAND_VALID) != 0;
        }
        catch
        {
            return false;
        }
    }

    public override async Task<ITSMode[]> GetAllStatesAsync()
    {
        if (AppFlags.Instance.Debug)
        {
            return _allStatesWithGeek;
        }

        return await IsGeekModeAdvertisedAsync().ConfigureAwait(false) ? _allStatesWithGeek : _allStatesWithoutGeek;
    }

    protected override async Task<ITSMode> GetStateInternalAsync(bool bypassQueue)
    {
        var raw = await SendCodeAsync(DriverHandle(), ControlCode, GET_CURRENT_MODE, bypassQueue).ConfigureAwait(false);
        var state = await FromInternalAsync(raw).ConfigureAwait(false);
        LastState = state;
        return state;
    }

    protected override uint GetInBufferValue() => GET_CURRENT_MODE;

    protected override Task<ITSMode> FromInternalAsync(uint raw)
    {
        if ((raw & COMMAND_VALID) == 0)
        {
            return Task.FromResult(ITSMode.None);
        }

        var function = (raw >> GET_FUNCTION_SHIFT) & 0xF;
        var mode = (raw >> GET_MODE_SHIFT) & 0xF;

        ITSMode state;
        if (function == FUNCTION_ITS && mode == MODE_COOL)
        {
            state = ITSMode.MmcCool;
        }
        else if (function == FUNCTION_ITS && mode == MODE_PERFORMANCE)
        {
            state = IsGeekModeActive() ? ITSMode.MmcGeek : ITSMode.MmcPerformance;
        }
        else if (IsIntelligentFunction(function))
        {
            state = ITSMode.ItsAuto;
        }
        else
        {
            state = ITSMode.None;
        }

        Log.Instance.Trace($"EnergyDrv ITS mode read. [raw=0x{raw:X8}, function={function}, mode={mode}, state={state}]");
        return Task.FromResult(state);
    }

    public override async Task SetStateAsync(ITSMode state)
    {
        if (state == ITSMode.None)
        {
            Log.Instance.Trace($"Can't set ITS mode to None, operation aborted.");
            return;
        }

        if (!(await GetAllStatesAsync().ConfigureAwait(false)).Contains(state))
        {
            throw new InvalidOperationException($"Unsupported ITS mode {state}.");
        }

        if (state == ITSMode.MmcGeek &&
            await Power.IsPowerAdapterConnectedAsync().ConfigureAwait(false) != PowerAdapterStatus.Connected)
        {
            throw new InvalidOperationException("Geek mode is unavailable without an AC power adapter.");
        }

        Log.Instance.Trace($"Setting ITS mode to: {state} [EnergyDrv]");

        if (state != ITSMode.MmcGeek && IsGeekModeActive())
        {
            await SendCodeAsync(DriverHandle(), ControlCode, GEEK_DISABLED).ConfigureAwait(false);
            _geekModeState = 0;
        }

        if (state == ITSMode.ItsAuto)
        {
            await SendCodeAsync(DriverHandle(), ControlCode, RESET_MODE).ConfigureAwait(false);
        }

        foreach (var command in await ToInternalAsync(state).ConfigureAwait(false))
        {
            await SendCodeAsync(DriverHandle(), ControlCode, command).ConfigureAwait(false);
        }

        if (state == ITSMode.MmcGeek)
        {
            await SendCodeAsync(DriverHandle(), ControlCode, GEEK_ENABLED).ConfigureAwait(false);
            _geekModeState = 1;
        }

        if (!await WaitForModeAsync(state, CancellationToken.None).ConfigureAwait(false))
        {
            throw new InvalidOperationException($"ITS mode did not change to {state}.");
        }

        LastState = state;
        Log.Instance.Trace($"ITS mode set successfully to: {state} [EnergyDrv]");
    }

    protected override Task<uint[]> ToInternalAsync(ITSMode state)
    {
        var command = state switch
        {
            ITSMode.ItsAuto => BuildSetCommand(FUNCTION_INTELLIGENT, MODE_INTELLIGENT),
            ITSMode.MmcCool => BuildSetCommand(FUNCTION_ITS, MODE_COOL),
            ITSMode.MmcPerformance or ITSMode.MmcGeek => BuildSetCommand(FUNCTION_ITS, MODE_PERFORMANCE),
            _ => throw new ArgumentOutOfRangeException(nameof(state))
        };
        return Task.FromResult(new[] { command });
    }

    public async Task<uint> GetExtendedCapabilitiesAsync() =>
        await SendCodeAsync(DriverHandle(), ControlCode, GET_EXTENDED_CAPABILITIES, bypassQueue: true).ConfigureAwait(false);

    public async Task<bool> IsGeekModeAdvertisedAsync()
    {
        var capability = await GetExtendedCapabilitiesAsync().ConfigureAwait(false);
        var supported = (capability & GEEK_CAPABILITY_MASK) == GEEK_CAPABILITY_MASK;
        Log.Instance.Trace($"EnergyDrv Geek capability checked. [capability=0x{capability:X8}, supported={supported}]");
        return supported;
    }

    private bool IsGeekModeActive()
    {
        if (_geekModeState >= 0)
        {
            return _geekModeState == 1;
        }

        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(REG_KEY_DISPATCHER, writable: false);
            return key?.GetValue(VAL_ITS_CUR_SET_V) is int value && value == 4;
        }
        catch
        {
            return false;
        }
    }

    private static uint BuildSetCommand(uint function, uint mode) =>
        COMMAND_VALID |
        (function << SET_FUNCTION_SHIFT) |
        (mode << SET_MODE_SHIFT) |
        SET_COMMAND_VALID;

    private static bool IsIntelligentFunction(uint function) => function is 0 or 3 or 5 or 6 or 7 or 8;

    private static uint ReadDispatcherVersion()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(REG_KEY_DISPATCHER, writable: false);
            return key?.GetValue(VAL_VERSION) switch
            {
                int value => (uint)value,
                uint value => value,
                _ => 0
            };
        }
        catch (Exception ex)
        {
            Log.Instance.Trace($"Failed to read Dispatcher version.", ex);
            return 0;
        }
    }

    private async Task<bool> WaitForModeAsync(ITSMode expected, CancellationToken token)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(3);
        do
        {
            if (await GetStateInternalAsync(bypassQueue: true).ConfigureAwait(false) == expected)
            {
                return true;
            }

            await Task.Delay(200, token).ConfigureAwait(false);
        }
        while (DateTimeOffset.UtcNow < deadline && !token.IsCancellationRequested);

        return false;
    }
}
