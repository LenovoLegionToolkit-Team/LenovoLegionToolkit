using System;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using LenovoLegionToolkit.Lib.Listeners;
using LenovoLegionToolkit.Lib.System;
using LenovoLegionToolkit.Lib.Utils;
using NeoSmart.AsyncLock;

namespace LenovoLegionToolkit.Lib.Features;

public class ITSModeFeatureDriver : AbstractDriverFeature<ITSMode>
{
    private const uint DYTC_CMD_GET_MODE = 2;
    private const uint DYTC_CMD_GET_FUNCTION_CAPABILITIES = 3;
    private const uint DYTC_CMD_GET_EXTENDED_CAPABILITIES = 10;
    private const uint DYTC_CMD_RESET = 0x000001FF;
    private const uint DYTC_CMD_SET_FUNCTION = 0x1;

    private const uint DYTC_RESPONSE_VALID_BIT = 0x1;
    private const uint DYTC_SET_FUNCTION_ENABLE_BIT = 0x100000;
    private const int DYTC_SET_FUNCTION_SHIFT = 12;
    private const int DYTC_SET_MODE_PARAMETER_SHIFT = 16;
    private const int DYTC_GET_FUNCTION_SHIFT = 8;
    private const int DYTC_GET_MODE_PARAMETER_SHIFT = 12;
    private const int DYTC_FUNCTION_CAPABILITY_SHIFT = 16;

    private const uint DYTC_MODE_PARAMETER_PERFORMANCE = 2;
    private const uint DYTC_MODE_PARAMETER_COOL = 3;
    private const uint DYTC_MODE_PARAMETER_INTELLIGENT_OR_DISABLE = 0xF;

    private const uint DYTC_FULL_SPEED_CAPABILITY_MASK = 0x00020001;
    private const uint DYTC_CMD_DISABLE_FULL_SPEED = 0x000F100B;
    private const uint DYTC_CMD_ENABLE_FULL_SPEED = 0x001F100B;
    private const uint DYTC_DEFAULT_FUNCTION_READBACK = 0x0001F001;
    private const int GEEK_ENABLE_REPEAT_COUNT = 2;
    private const uint REQUIRED_BASE_MODE_CAPABILITIES = DYTC_RESPONSE_VALID_BIT |
        (1U << (DYTC_FUNCTION_CAPABILITY_SHIFT + (int)DytcFunction.IntelligentCooling)) |
        (1U << (DYTC_FUNCTION_CAPABILITY_SHIFT + (int)DytcFunction.ItsModeControl));
    private const int STATE_TIMEOUT_MS = 3000;

    private static readonly ITSMode[] _allStatesWithGeek = [ITSMode.ItsAuto, ITSMode.MmcCool, ITSMode.MmcPerformance, ITSMode.MmcGeek];
    private static readonly ITSMode[] _allStatesWithoutGeek = [ITSMode.ItsAuto, ITSMode.MmcCool, ITSMode.MmcPerformance];

    private static readonly DytcFunction[] _functionDisableOrderBeforeModeSwitch =
    [
        DytcFunction.Auto_UnknownFunction15,
        DytcFunction.Auto_UnknownFunction8,
        DytcFunction.Auto_UnknownFunction7,
        DytcFunction.Auto_UnknownFunction6,
        DytcFunction.IntelligentCooling,
        DytcFunction.ItsModeControl,
    ];

    private readonly ITSModeListener _listener;
    private readonly AsyncLock _transitionLock = new();

    private volatile bool _geekModeEnabled;

    public ITSModeFeatureDriver(ITSModeListener listener)
        : base(Drivers.GetEnergy, Drivers.IOCTL_ENERGY_SMART_POWER, useDriverQueue: true)
    {
        _listener = listener;
    }

    public override async Task<bool> IsSupportedAsync()
    {
        try
        {
            if (AppFlags.Instance.Debug)
            {
                return true;
            }

            var capabilities = await SendCommandAsync(DYTC_CMD_GET_FUNCTION_CAPABILITIES, bypassQueue: true).ConfigureAwait(false);
            if ((capabilities & REQUIRED_BASE_MODE_CAPABILITIES) != REQUIRED_BASE_MODE_CAPABILITIES)
            {
                return false;
            }

            var raw = await SendCommandAsync(DYTC_CMD_GET_MODE, bypassQueue: true).ConfigureAwait(false);
            return (raw & DYTC_RESPONSE_VALID_BIT) != 0;
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
        var raw = await SendCommandAsync(DYTC_CMD_GET_MODE, bypassQueue).ConfigureAwait(false);
        var state = await FromInternalAsync(raw).ConfigureAwait(false);
        LastState = state;
        return state;
    }

    protected override uint GetInBufferValue() => DYTC_CMD_GET_MODE;

    protected override async Task<ITSMode> FromInternalAsync(uint raw)
    {
        if ((raw & DYTC_RESPONSE_VALID_BIT) == 0)
        {
            _geekModeEnabled = false;
            return ITSMode.None;
        }

        var function = GetCurrentFunction(raw);
        var modeParameter = (raw >> DYTC_GET_MODE_PARAMETER_SHIFT) & 0xF;

        ITSMode state;
        if (_geekModeEnabled && IsFullSpeedCompatibleReadback(raw) && await GetPowerAdapterStatusAsync().ConfigureAwait(false) == PowerAdapterStatus.Connected)
        {
            state = ITSMode.MmcGeek;
        }
        else if (function == DytcFunction.ItsModeControl && modeParameter == DYTC_MODE_PARAMETER_COOL)
        {
            state = ITSMode.MmcCool;
        }
        else if (function == DytcFunction.ItsModeControl && modeParameter == DYTC_MODE_PARAMETER_PERFORMANCE)
        {
            state = ITSMode.MmcPerformance;
        }
        else if (IsAutoModeFunction(function, modeParameter) || (function == DytcFunction.ItsModeControl && (raw >> 16) == 1))
        {
            state = ITSMode.ItsAuto;
        }
        else
        {
            state = ITSMode.None;
        }

        if (state != ITSMode.MmcGeek)
        {
            _geekModeEnabled = false;
        }

        Log.Instance.Trace($"EnergyDrv ITS mode read. [raw=0x{raw:X8}, function={function} ({(uint)function}), modeParameter={modeParameter}, state={state}, localGeek={_geekModeEnabled}]");
        return state;
    }

    public override async Task SetStateAsync(ITSMode state)
    {
        if (state == ITSMode.None)
        {
            Log.Instance.Trace($"Can't set ITS mode to None, operation aborted.");
            return;
        }

        if (!_allStatesWithGeek.Contains(state))
        {
            throw new InvalidOperationException($"Unsupported ITS mode {state}.");
        }

        using (await _transitionLock.LockAsync().ConfigureAwait(false))
        {
            await SetStateCoreAsync(state).ConfigureAwait(false);
        }
    }

    public override Task SetStateAsync(ITSMode state, bool verify) => SetStateAsync(state);

    private async Task SetStateCoreAsync(ITSMode state)
    {
        var geekSupported = await IsGeekModeAdvertisedAsync().ConfigureAwait(false);
        if (state == ITSMode.MmcGeek)
        {
            if (!geekSupported)
            {
                throw new InvalidOperationException("Geek mode is not advertised by EnergyDrv.");
            }

            await EnsureGeekPowerAsync().ConfigureAwait(false);
        }

        Log.Instance.Trace($"Setting ITS mode to: {state} [EnergyDrv]");

        if (geekSupported || _geekModeEnabled)
        {
            _geekModeEnabled = false;
            await SendCommandAsync(DYTC_CMD_DISABLE_FULL_SPEED).ConfigureAwait(false);
        }

        var resetRequired = false;
        if (state == ITSMode.ItsAuto)
        {
            var raw = await SendCommandAsync(DYTC_CMD_GET_MODE, bypassQueue: true).ConfigureAwait(false);
            var function = GetCurrentFunction(raw);
            resetRequired = function is not (DytcFunction.Manual or DytcFunction.Unknown4);
        }

        foreach (var function in _functionDisableOrderBeforeModeSwitch)
        {
            await SendCommandAsync(BuildDisableFunctionCommand(function)).ConfigureAwait(false);
        }

        if (resetRequired)
        {
            await SendCommandAsync(DYTC_CMD_RESET).ConfigureAwait(false);
        }

        foreach (var command in await ToInternalAsync(state).ConfigureAwait(false))
        {
            await SendCommandAsync(command).ConfigureAwait(false);
        }

        var baseState = state == ITSMode.MmcGeek ? ITSMode.MmcPerformance : state;
        if (!await WaitForModeAsync(baseState, CancellationToken.None).ConfigureAwait(false))
        {
            throw new InvalidOperationException($"ITS mode did not change to {baseState}.");
        }

        if (state == ITSMode.MmcGeek)
        {
            try
            {
                for (var i = 0; i < GEEK_ENABLE_REPEAT_COUNT; i++)
                {
                    await EnsureGeekPowerAsync().ConfigureAwait(false);
                    await SendCommandAsync(DYTC_CMD_ENABLE_FULL_SPEED).ConfigureAwait(false);
                }

                if (!await WaitForModeAsync(ITSMode.MmcGeek, CancellationToken.None).ConfigureAwait(false))
                {
                    throw new InvalidOperationException("Unexpected ITS readback after enabling Full Speed.");
                }

                await EnsureGeekPowerAsync().ConfigureAwait(false);
                _geekModeEnabled = true;
                Log.Instance.Trace($"EnergyDrv Full Speed enable sequence completed; overlay has no independent readback.");
            }
            catch
            {
                await TryDisableGeekModeAsync().ConfigureAwait(false);
                throw;
            }
        }

        LastState = state;
        await NotifyListenerAsync(state).ConfigureAwait(false);
        Log.Instance.Trace($"ITS mode set successfully to: {state} [EnergyDrv]");
    }

    protected override Task<uint[]> ToInternalAsync(ITSMode state)
    {
        var command = state switch
        {
            ITSMode.ItsAuto => BuildSetFunctionCommand(DytcFunction.IntelligentCooling, DYTC_MODE_PARAMETER_INTELLIGENT_OR_DISABLE),
            ITSMode.MmcCool => BuildSetFunctionCommand(DytcFunction.ItsModeControl, DYTC_MODE_PARAMETER_COOL),
            ITSMode.MmcPerformance or ITSMode.MmcGeek => BuildSetFunctionCommand(DytcFunction.ItsModeControl, DYTC_MODE_PARAMETER_PERFORMANCE),
            _ => throw new ArgumentOutOfRangeException(nameof(state))
        };
        return Task.FromResult(new[] { command });
    }

    public async Task<uint> GetExtendedCapabilitiesAsync() =>
        await SendCommandAsync(DYTC_CMD_GET_EXTENDED_CAPABILITIES, bypassQueue: true).ConfigureAwait(false);

    public async Task<bool> IsGeekModeAdvertisedAsync()
    {
        try
        {
            var capability = await GetExtendedCapabilitiesAsync().ConfigureAwait(false);
            var supported = (capability & DYTC_FULL_SPEED_CAPABILITY_MASK) == DYTC_FULL_SPEED_CAPABILITY_MASK;
            Log.Instance.Trace($"EnergyDrv Geek capability checked. [capability=0x{capability:X8}, supported={supported}]");
            return supported;
        }
        catch (Exception ex)
        {
            Log.Instance.Trace($"Failed to query optional EnergyDrv Geek capability.", ex);
            return false;
        }
    }

    private async Task EnsureGeekPowerAsync()
    {
        if (await GetPowerAdapterStatusAsync().ConfigureAwait(false) != PowerAdapterStatus.Connected)
        {
            throw new InvalidOperationException("Geek mode is unavailable without an AC power adapter.");
        }
    }

    protected virtual Task<uint> SendCommandAsync(uint command, bool bypassQueue = false) =>
        SendCodeAsync(DriverHandle(), ControlCode, command, bypassQueue);

    protected virtual Task<PowerAdapterStatus> GetPowerAdapterStatusAsync() => Power.IsPowerAdapterConnectedAsync();

    protected virtual async Task NotifyListenerAsync(ITSMode state)
    {
        try
        {
            await _listener.NotifyAsync(state).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Log.Instance.Trace($"Failed to notify ITS mode change. [state={state}]", ex);
        }
    }

    private async Task TryDisableGeekModeAsync()
    {
        try
        {
            await SendCommandAsync(DYTC_CMD_DISABLE_FULL_SPEED).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Log.Instance.Trace($"Failed to roll back EnergyDrv Geek mode.", ex);
        }
        finally
        {
            _geekModeEnabled = false;
        }
    }

    private static DytcFunction GetCurrentFunction(uint response) =>
        (DytcFunction)((response >> DYTC_GET_FUNCTION_SHIFT) & 0xF);

    private static bool IsFullSpeedCompatibleReadback(uint response) =>
        response == DYTC_DEFAULT_FUNCTION_READBACK ||
        ((response & DYTC_RESPONSE_VALID_BIT) != 0 &&
         GetCurrentFunction(response) == DytcFunction.ItsModeControl &&
         ((response >> DYTC_GET_MODE_PARAMETER_SHIFT) & 0xF) == DYTC_MODE_PARAMETER_PERFORMANCE);

    private static uint BuildDisableFunctionCommand(DytcFunction function) =>
        BuildSetFunctionCommand(function, DYTC_MODE_PARAMETER_INTELLIGENT_OR_DISABLE, enableFunction: false);

    private static uint BuildSetFunctionCommand(DytcFunction function, uint modeParameter, bool enableFunction = true) =>
        DYTC_CMD_SET_FUNCTION |
        ((uint)function << DYTC_SET_FUNCTION_SHIFT) |
        (modeParameter << DYTC_SET_MODE_PARAMETER_SHIFT) |
        (enableFunction ? DYTC_SET_FUNCTION_ENABLE_BIT : 0);

    private static bool IsAutoModeFunction(DytcFunction function, uint modeParameter) =>
        function is DytcFunction.FirmwareDefault or DytcFunction.IntelligentCooling or
            DytcFunction.Auto_UnknownFunction6 or DytcFunction.Auto_UnknownFunction7 or
            DytcFunction.Auto_UnknownFunction8 or DytcFunction.Auto_UnknownFunction15 ||
        (function == DytcFunction.Manual && modeParameter <= 3) ||
        (function == DytcFunction.Auto_UnknownFunction13 && modeParameter is 1 or 2);

    private async Task<bool> WaitForModeAsync(ITSMode expected, CancellationToken token)
    {
        var stopwatch = Stopwatch.StartNew();
        do
        {
            token.ThrowIfCancellationRequested();

            var matches = expected == ITSMode.MmcGeek
                ? IsFullSpeedCompatibleReadback(await SendCommandAsync(DYTC_CMD_GET_MODE, bypassQueue: true).ConfigureAwait(false))
                : await GetStateInternalAsync(bypassQueue: true).ConfigureAwait(false) == expected;
            if (matches)
            {
                return true;
            }

            await Task.Delay(200, token).ConfigureAwait(false);
        }
        while (stopwatch.ElapsedMilliseconds < STATE_TIMEOUT_MS);

        return false;
    }
}
