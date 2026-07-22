using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using LenovoLegionToolkit.Lib.Extensions;
using LenovoLegionToolkit.Lib.Settings;
using LenovoLegionToolkit.Lib.SoftwareDisabler;
using LenovoLegionToolkit.Lib.System;
using LenovoLegionToolkit.Lib.Utils;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.System.Power;
using static LenovoLegionToolkit.Lib.Settings.GodModeSettings;

namespace LenovoLegionToolkit.Lib.Controllers;

public class WindowsPowerPlanController(ApplicationSettings settings, VantageDisabler vantageDisabler, IMainThreadDispatcher mainThreadDispatcher)
{
    public static readonly Guid DefaultPowerPlan = Guid.Parse("381b4222-f694-41f0-9685-ff5bb260df2e");

    private readonly ThrottleLastDispatcher _overlayDispatcher = new(TimeSpan.FromSeconds(2), nameof(WindowsPowerPlanController));
    private readonly SemaphoreSlim _lock = new(1, 1);

    public IEnumerable<WindowsPowerPlan> GetPowerPlans()
    {
        var activePowerPlanGuid = GetActivePowerPlanGuid();
        foreach (var powerPlanGuid in GetPowerPlanGuids())
        {
            var powerPlanName = GetPowerPlanName(powerPlanGuid);
            yield return new WindowsPowerPlan(powerPlanGuid, powerPlanName, powerPlanGuid == activePowerPlanGuid);
        }
    }

    public async Task SetPowerPlanAsync(PowerModeState powerModeState, bool alwaysActivateDefaults = false, GodModeSettingsStore.Preset? preset = null, bool skipThrottle = false)
    {
        await _lock.WaitAsync().ConfigureAwait(false);
        try
        {
            if (settings.Store.PowerModeMappingMode is not PowerModeMappingMode.WindowsPowerPlan)
            {
                Log.Instance.Trace($"Ignoring... [powerModeMappingMode={settings.Store.PowerModeMappingMode}]");
                return;
            }

            Log.Instance.Trace($"Activating... [powerModeState={powerModeState}, alwaysActivateDefaults={alwaysActivateDefaults}]");

            var powerPlanId = preset?.Overrides.TryGetGuid(PowerOverrideKey.PowerPlan) ?? settings.Store.PowerPlans.GetValueOrDefault(powerModeState);

            var isDefault = false;

            if (powerPlanId == Guid.Empty)
            {
                Log.Instance.Trace($"Power plan for power mode {powerModeState} was not found in settings");

                powerPlanId = DefaultPowerPlan;
                isDefault = true;
            }

            Log.Instance.Trace($"Power plan to be activated is {powerPlanId} [isDefault={isDefault}]");

            if (!await ShouldSetPowerPlanAsync(alwaysActivateDefaults, isDefault).ConfigureAwait(false))
            {
                Log.Instance.Trace($"Power plan {powerPlanId} will not be activated [isDefault={isDefault}]");
                return;
            }

            var powerPlans = GetPowerPlans().ToArray();

            Log.Instance.Trace($"Available power plans:");
            foreach (var powerPlan in powerPlans)
                Log.Instance.Trace($" - {powerPlan}");

            var powerPlanToActivate = powerPlans.FirstOrDefault(pp => pp.Guid == powerPlanId);
            if (powerPlanToActivate.Equals(default(WindowsPowerPlan)))
            {
                Log.Instance.Trace($"Power plan {powerPlanId} was not found");
                return;
            }

            if (powerPlanToActivate.IsActive)
            {
                Log.Instance.Trace($"Power plan {powerPlanToActivate.Guid} is already active. [name={powerPlanToActivate.Name}]");

                await ApplyBalanceOverlayIfNeededAsync(powerPlanToActivate.Guid, powerModeState, isDefault, preset, skipThrottle).ConfigureAwait(false);
                return;
            }

            try
            {
                SetActivePowerPlan(powerPlanToActivate.Guid);
                Log.Instance.Trace($"Power plan {powerPlanToActivate.Guid} activated. [name={powerPlanToActivate.Name}]");
            }
            catch (Exception ex)
            {
                Log.Instance.Trace($"Failed to set active power plan. [guid={powerPlanToActivate.Guid}]", ex);
                return;
            }

            await ApplyBalanceOverlayIfNeededAsync(powerPlanToActivate.Guid, powerModeState, isDefault, preset).ConfigureAwait(false);
        }
        finally
        {
            _lock.Release();
        }
    }

    private async Task ApplyBalanceOverlayIfNeededAsync(Guid activePowerPlanGuid, PowerModeState powerModeState, bool isDefault, GodModeSettingsStore.Preset? preset = null, bool skipThrottle = false)
    {
        if (!PowerPlanExtensions.IsPlanBasedOnBalanced(activePowerPlanGuid))
        {
            Log.Instance.Trace($"Active power plan is not based on Balanced plan, skipping overlay. [guid={activePowerPlanGuid}]");
            return;
        }

        if (Power.IsBatterySaverEnabled())
        {
            Log.Instance.Trace($"Battery saver is on - will not set overlay scheme.");
            return;
        }

        var presetBalanceAc = preset?.Overrides.TryGetEnum<WindowsPowerMode>(PowerOverrideKey.PowerPlanBalanceOnAc);
        var presetBalanceDc = preset?.Overrides.TryGetEnum<WindowsPowerMode>(PowerOverrideKey.PowerPlanBalanceOnDc);
        var isPreset = presetBalanceAc.HasValue || presetBalanceDc.HasValue;

        var acMode = WindowsPowerMode.Balanced;
        var dcMode = WindowsPowerMode.Balanced;

        if (!isDefault)
        {
            acMode = (isPreset ? presetBalanceAc : settings.Store.Overrides.GetPowerPlanBalanceOnAc(powerModeState)) ?? WindowsPowerMode.Balanced;
            dcMode = (isPreset ? presetBalanceDc : settings.Store.Overrides.GetPowerPlanBalanceOnDc(powerModeState)) ?? WindowsPowerMode.Balanced;
        }

        if (isPreset)
            Log.Instance.Trace($"Using preset Balance overlay. [acMode={acMode}, dcMode={dcMode}]");
        else
            Log.Instance.Trace($"Using per-mode Balance overlay. [powerModeState={powerModeState}, acMode={acMode}, dcMode={dcMode}]");

        var adapterStatus = await Power.IsPowerAdapterConnectedAsync().ConfigureAwait(false);
        var isAc = adapterStatus != PowerAdapterStatus.Disconnected;

        var activeMode = isAc ? acMode : dcMode;
        var guidToApply = WindowsPowerModeController.GuidForWindowsPowerMode(activeMode);

        var acRegistryGuid = WindowsPowerModeController.GuidForWindowsPowerMode(acMode);
        var dcRegistryGuid = WindowsPowerModeController.GuidForWindowsPowerMode(dcMode);

        if (skipThrottle)
        {
            await _overlayDispatcher.DispatchImmediateAsync(() => ExecuteBalanceOverlayDispatch(guidToApply, acRegistryGuid, dcRegistryGuid)).ConfigureAwait(false);
        }
        else
        {
            await _overlayDispatcher.DispatchAsync(() => ExecuteBalanceOverlayDispatch(guidToApply, acRegistryGuid, dcRegistryGuid)).ConfigureAwait(false);
        }
    }

    public async Task SetPowerPlanAsync(ITSMode itsMode, bool alwaysActivateDefaults = false, bool skipThrottle = false)
    {
        await _lock.WaitAsync().ConfigureAwait(false);
        try
        {
            if (settings.Store.PowerModeMappingMode is not PowerModeMappingMode.WindowsPowerPlan)
            {
                Log.Instance.Trace($"Ignoring... [powerModeMappingMode={settings.Store.PowerModeMappingMode}]");
                return;
            }

            Log.Instance.Trace($"Activating... [itsMode={itsMode}, alwaysActivateDefaults={alwaysActivateDefaults}]");

            var powerPlanId = settings.Store.ITSPowerPlans.GetValueOrDefault(itsMode);

            var isDefault = false;

            if (powerPlanId == Guid.Empty)
            {
                Log.Instance.Trace($"Power plan for ITS mode {itsMode} was not found in settings");

                powerPlanId = DefaultPowerPlan;
                isDefault = true;
            }

            Log.Instance.Trace($"Power plan to be activated is {powerPlanId} [isDefault={isDefault}]");

            if (!await ShouldSetPowerPlanAsync(alwaysActivateDefaults, isDefault).ConfigureAwait(false))
            {
                Log.Instance.Trace($"Power plan {powerPlanId} will not be activated [isDefault={isDefault}]");
                return;
            }

            var powerPlans = GetPowerPlans().ToArray();

            Log.Instance.Trace($"Available power plans:");
            foreach (var powerPlan in powerPlans)
                Log.Instance.Trace($" - {powerPlan}");

            var powerPlanToActivate = powerPlans.FirstOrDefault(pp => pp.Guid == powerPlanId);
            if (powerPlanToActivate.Equals(default(WindowsPowerPlan)))
            {
                Log.Instance.Trace($"Power plan {powerPlanId} was not found");
                return;
            }

            if (powerPlanToActivate.IsActive)
            {
                Log.Instance.Trace($"Power plan {powerPlanToActivate.Guid} is already active. [name={powerPlanToActivate.Name}]");

                await ApplyBalanceOverlayIfNeededAsync(powerPlanToActivate.Guid, itsMode, isDefault, skipThrottle).ConfigureAwait(false);
                return;
            }

            try
            {
                SetActivePowerPlan(powerPlanToActivate.Guid);
                Log.Instance.Trace($"Power plan {powerPlanToActivate.Guid} activated. [name={powerPlanToActivate.Name}]");
            }
            catch (Exception ex)
            {
                Log.Instance.Trace($"Failed to set active power plan. [guid={powerPlanToActivate.Guid}]", ex);
                return;
            }

            await ApplyBalanceOverlayIfNeededAsync(powerPlanToActivate.Guid, itsMode, isDefault).ConfigureAwait(false);
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task SetBalancedPowerPlanAsync(bool skipThrottle = false)
    {
        await _lock.WaitAsync().ConfigureAwait(false);
        try
        {
            if (Power.IsBatterySaverEnabled())
            {
                Log.Instance.Trace($"Battery saver is on - will not set overlay scheme.");
                return;
            }

            try
            {
                SetActivePowerPlan(DefaultPowerPlan);
                Log.Instance.Trace($"Balanced power plan activated. [guid={DefaultPowerPlan}]");
            }
            catch (Exception ex)
            {
                Log.Instance.Trace($"Failed to set balanced power plan. [guid={DefaultPowerPlan}]", ex);
                return;
            }

            var balancedGuid = Guid.Empty;

            if (skipThrottle)
            {
                await _overlayDispatcher.DispatchImmediateAsync(() => ExecuteBalanceOverlayDispatch(balancedGuid, balancedGuid, balancedGuid)).ConfigureAwait(false);
            }
            else
            {
                await _overlayDispatcher.DispatchAsync(() => ExecuteBalanceOverlayDispatch(balancedGuid, balancedGuid, balancedGuid)).ConfigureAwait(false);
            }

            Log.Instance.Trace($"Balanced power plan set.");
        }
        finally
        {
            _lock.Release();
        }
    }

    private async Task ApplyBalanceOverlayIfNeededAsync(Guid activePowerPlanGuid, ITSMode itsMode, bool isDefault, bool skipThrottle = false)
    {
        if (!PowerPlanExtensions.IsPlanBasedOnBalanced(activePowerPlanGuid))
        {
            Log.Instance.Trace($"Active power plan is not based on Balanced plan, skipping overlay. [guid={activePowerPlanGuid}]");
            return;
        }

        if (Power.IsBatterySaverEnabled())
        {
            Log.Instance.Trace($"Battery saver is on - will not set overlay scheme.");
            return;
        }

        var acMode = WindowsPowerMode.Balanced;
        var dcMode = WindowsPowerMode.Balanced;

        if (!isDefault)
        {
            acMode = settings.Store.ITSOverrides.GetPowerPlanBalanceOnAc(itsMode) ?? WindowsPowerMode.Balanced;
            dcMode = settings.Store.ITSOverrides.GetPowerPlanBalanceOnDc(itsMode) ?? WindowsPowerMode.Balanced;
        }
        Log.Instance.Trace($"Using per-mode Balance overlay. [itsMode={itsMode}, acMode={acMode}, dcMode={dcMode}]");

        var adapterStatus = await Power.IsPowerAdapterConnectedAsync().ConfigureAwait(false);
        var isAc = adapterStatus != PowerAdapterStatus.Disconnected;

        var activeMode = isAc ? acMode : dcMode;
        var guidToApply = WindowsPowerModeController.GuidForWindowsPowerMode(activeMode);

        var acRegistryGuid = WindowsPowerModeController.GuidForWindowsPowerMode(acMode);
        var dcRegistryGuid = WindowsPowerModeController.GuidForWindowsPowerMode(dcMode);

        if (skipThrottle)
        {
            await _overlayDispatcher.DispatchImmediateAsync(() => ExecuteBalanceOverlayDispatch(guidToApply, acRegistryGuid, dcRegistryGuid)).ConfigureAwait(false);
        }
        else
        {
            await _overlayDispatcher.DispatchAsync(() => ExecuteBalanceOverlayDispatch(guidToApply, acRegistryGuid, dcRegistryGuid)).ConfigureAwait(false);
        }
    }

    private Task ExecuteBalanceOverlayDispatch(Guid guidToApply, Guid acRegistryGuid, Guid dcRegistryGuid)
    {
        mainThreadDispatcher.Dispatch(() =>
        {
            try
            {
                WindowsPowerModeController.ApplyActiveOverlayScheme(guidToApply);
                Log.Instance.Trace($"Balance overlay scheme applied. [guidToApply={guidToApply}]");
            }
            catch (Exception ex)
            {
                Log.Instance.Trace($"Failed to apply balance overlay scheme.", ex);
            }
        });

        WindowsPowerModeController.SetActiveOverlayRegistryForAc(acRegistryGuid);
        WindowsPowerModeController.SetActiveOverlayRegistryForDc(dcRegistryGuid);

        return Task.CompletedTask;
    }

    public void SetPowerPlanParameter(WindowsPowerPlan windowsPowerPlan, Brightness brightness)
    {
        try
        {
            PInvoke.PowerWriteACValueIndex(NullSafeHandle.Null, windowsPowerPlan.Guid, PInvoke.GUID_VIDEO_SUBGROUP, PInvokeExtensions.DISPLAY_BRIGTHNESS_SETTING_GUID, brightness.Value);
        }
        catch (Exception ex)
        {
            Log.Instance.Trace($"Failed to set AC brightness for power plan. [guid={windowsPowerPlan.Guid}]", ex);
        }

        try
        {
            PInvoke.PowerWriteDCValueIndex(NullSafeHandle.Null, windowsPowerPlan.Guid, PInvoke.GUID_VIDEO_SUBGROUP, PInvokeExtensions.DISPLAY_BRIGTHNESS_SETTING_GUID, brightness.Value);
        }
        catch (Exception ex)
        {
            Log.Instance.Trace($"Failed to set DC brightness for power plan. [guid={windowsPowerPlan.Guid}]", ex);
        }
    }

    private async Task<bool> ShouldSetPowerPlanAsync(bool alwaysActivateDefaults, bool isDefault)
    {
        if (isDefault && alwaysActivateDefaults)
        {
            Log.Instance.Trace($"Power plan is default and always activate defaults is set");
            return true;
        }

        var status = await vantageDisabler.GetStatusAsync().ConfigureAwait(false);
        if (status is SoftwareStatus.NotFound or SoftwareStatus.Disabled)
        {
            Log.Instance.Trace($"Vantage is not active / disabled [status={status}]");
            return true;
        }

        Log.Instance.Trace($"Criteria for activation not met [isDefault={isDefault}, alwaysActivateDefaults={alwaysActivateDefaults}, status={status}]");
        return false;
    }

    private static List<Guid> GetPowerPlanGuids()
    {
        var list = new List<Guid>();

        try
        {
            var bufferSize = (uint)Marshal.SizeOf<Guid>();
            var buffer = new byte[bufferSize];

            uint index = 0;
            while (PInvoke.PowerEnumerate(null, null, null, POWER_DATA_ACCESSOR.ACCESS_SCHEME, index, buffer, ref bufferSize) == WIN32_ERROR.ERROR_SUCCESS)
            {
                try
                {
                    list.Add(new Guid(buffer));
                }
                catch (Exception ex)
                {
                    Log.Instance.Trace($"Failed to parse power plan Guid at index {index}.", ex);
                }

                index++;
            }
        }
        catch (Exception ex)
        {
            Log.Instance.Trace($"Failed to enumerate power plans.", ex);
        }

        return list;
    }

    public static string GetPowerPlanName(Guid powerPlanGuid)
    {
        var nameSize = 2048u;
        var buffer = new byte[nameSize];

        try
        {
            if (PInvoke.PowerReadFriendlyName(null, powerPlanGuid, null, null, buffer, ref nameSize) != WIN32_ERROR.ERROR_SUCCESS)
                PInvokeExtensions.ThrowIfWin32Error("PowerReadFriendlyName");

            return global::System.Text.Encoding.Unicode.GetString(buffer, 0, (int)nameSize).TrimEnd('\0');
        }
        catch
        {
            return powerPlanGuid.ToString();
        }
    }

    private static unsafe Guid GetActivePowerPlanGuid()
    {
        try
        {
            if (PInvoke.PowerGetActiveScheme(null, out var guid) != WIN32_ERROR.ERROR_SUCCESS)
                PInvokeExtensions.ThrowIfWin32Error("PowerGetActiveScheme");

            return *guid;
        }
        catch (Exception ex)
        {
            Log.Instance.Trace($"Failed to get active power plan guid.", ex);
            return DefaultPowerPlan;
        }
    }

    private static void SetActivePowerPlan(Guid powerPlanGuid)
    {
        if (PInvoke.PowerSetActiveScheme(null, powerPlanGuid) != WIN32_ERROR.ERROR_SUCCESS)
            PInvokeExtensions.ThrowIfWin32Error("PowerSetActiveScheme");
    }
}
