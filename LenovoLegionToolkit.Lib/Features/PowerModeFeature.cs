using LenovoLegionToolkit.Lib.Controllers;
using LenovoLegionToolkit.Lib.Controllers.GodMode;
using LenovoLegionToolkit.Lib.Listeners;
using LenovoLegionToolkit.Lib.System;
using LenovoLegionToolkit.Lib.System.Management;
using LenovoLegionToolkit.Lib.Utils;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using static LenovoLegionToolkit.Lib.Settings.GodModeSettings;

namespace LenovoLegionToolkit.Lib.Features;

public class PowerModeUnavailableWithoutACException(PowerModeState powerMode) : Exception
{
    public PowerModeState PowerMode { get; } = powerMode;
}

public class PowerModeFeature(
    GodModeController godModeController,
    WindowsPowerModeController windowsPowerModeController,
    WindowsPowerPlanController windowsPowerPlanController,
    ThermalModeListener thermalModeListener,
    PowerModeListener powerModeListener)
    : AbstractWmiFeature<PowerModeState>(WMI.LenovoGameZoneData.GetSmartFanModeAsync, WMI.LenovoGameZoneData.SetSmartFanModeAsync, WMI.LenovoGameZoneData.IsSupportSmartFanAsync, 1)
{
    public bool AllowAllPowerModesOnBattery { get; set; }
    public PowerModeState LastPowerModeState { get; set; }

    public override async Task<PowerModeState[]> GetAllStatesAsync()
    {
        var mi = await Compatibility.GetMachineInformationAsync().ConfigureAwait(false);

        var states = new List<PowerModeState>
        {
            PowerModeState.Quiet,
            PowerModeState.Balance,
            PowerModeState.Performance
        };

        if (mi.Properties.SupportsExtremeMode)
        {
            states.Add(PowerModeState.Extreme);
        }

        if (mi.Properties.SupportsGodMode)
        {
            states.Add(PowerModeState.GodMode);
        }

        return states.ToArray();
    }

    public override async Task SetStateAsync(PowerModeState state)
    {
        var allStates = await GetAllStatesAsync().ConfigureAwait(false);
        if (!allStates.Contains(state))
            throw new InvalidOperationException($"Unsupported power mode {state}");

        if (state is PowerModeState.Performance or PowerModeState.GodMode or PowerModeState.Extreme
            && !AllowAllPowerModesOnBattery
            && await Power.IsPowerAdapterConnectedAsync().ConfigureAwait(false) is PowerAdapterStatus.Disconnected)
            throw new PowerModeUnavailableWithoutACException(state);

        var currentState = await GetStateAsync().ConfigureAwait(false);

        Log.Instance.Trace($"Switching power mode: {currentState} -> {state}");

        var mi = await Compatibility.GetMachineInformationAsync().ConfigureAwait(false);

        if (mi.Properties.HasQuietToPerformanceModeSwitchingBug && currentState == PowerModeState.Quiet && state == PowerModeState.Performance)
        {
            Log.Instance.Trace($"Workaround: Quiet->Performance bug, routing via Balance");
            thermalModeListener.SuppressNext();
            await base.SetStateAsync(PowerModeState.Balance).ConfigureAwait(false);
            await Task.Delay(TimeSpan.FromMilliseconds(500)).ConfigureAwait(false);
        }

        if (mi.Properties.HasGodModeToOtherModeSwitchingBug && currentState == PowerModeState.GodMode && state != PowerModeState.GodMode)
        {
            Log.Instance.Trace($"Workaround: GodMode->other bug, routing via {state} intermediate");
            thermalModeListener.SuppressNext();

            switch (state)
            {
                case PowerModeState.Quiet:
                    await base.SetStateAsync(PowerModeState.Performance).ConfigureAwait(false);
                    break;
                case PowerModeState.Balance:
                    await base.SetStateAsync(PowerModeState.Quiet).ConfigureAwait(false);
                    break;
                case PowerModeState.Performance:
                    await base.SetStateAsync(PowerModeState.Balance).ConfigureAwait(false);
                    break;
                case PowerModeState.Extreme:
                    await base.SetStateAsync(PowerModeState.Extreme).ConfigureAwait(false);
                    break;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(500)).ConfigureAwait(false);
        }

        var sw = Stopwatch.StartNew();
        thermalModeListener.SuppressNext();
        Log.Instance.Trace($"Calling SetSmartFanModeAsync({(int)(object)state + 1})...");
        await base.SetStateAsync(state).ConfigureAwait(false);
        Log.Instance.Trace($"SetSmartFanModeAsync completed [elapsed={sw.ElapsedMilliseconds}ms]");

        Log.Instance.Trace($"Calling PowerModeListener.NotifyAsync({state})...");
        await powerModeListener.NotifyAsync(state).ConfigureAwait(false);
        Log.Instance.Trace($"PowerModeListener.NotifyAsync completed");

        var thermalMode = await WMI.LenovoGameZoneData.GetThermalModeAsync().ConfigureAwait(false);
        Log.Instance.Trace($"Thermal Mode after switch: {(ThermalModeState)thermalMode} [expected={(ThermalModeState)(int)(object)state}]");
    }

    public async Task SuspendMode(PowerModeState state)
    {
        LastPowerModeState = await GetStateAsync().ConfigureAwait(false);

        await SetStateAsync(state).ConfigureAwait(false);
    }

    public async Task EnsureCorrectWindowsPowerSettingsAreSetAsync(GodModeSettingsStore.Preset? preset = null, bool skipThrottle = false)
    {
        var state = await GetStateAsync().ConfigureAwait(false);
        await windowsPowerModeController.SetPowerModeAsync(state, preset, skipThrottle).ConfigureAwait(false);
        await windowsPowerPlanController.SetPowerPlanAsync(state, true, preset, skipThrottle).ConfigureAwait(false);
    }

    public async Task EnsureGodModeStateIsAppliedAsync()
    {
        var state = await GetStateAsync().ConfigureAwait(false);
        if (state != PowerModeState.GodMode)
        {
            await EnsureCorrectWindowsPowerSettingsAreSetAsync().ConfigureAwait(false);
            return;
        }

        await godModeController.ApplyStateAsync().ConfigureAwait(false);
    }
}
