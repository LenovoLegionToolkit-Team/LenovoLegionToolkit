using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Windows.Win32;
using Windows.Win32.Foundation;
using LenovoLegionToolkit.Lib.Extensions;
using LenovoLegionToolkit.Lib.Settings;
using LenovoLegionToolkit.Lib.System;
using LenovoLegionToolkit.Lib.Utils;
using static LenovoLegionToolkit.Lib.Settings.GodModeSettings;

namespace LenovoLegionToolkit.Lib.Controllers;

public partial class WindowsPowerModeController(ApplicationSettings settings, IMainThreadDispatcher mainThreadDispatcher)
{
    private const string POWER_SCHEMES_HIVE = "HKEY_LOCAL_MACHINE";
    private const string POWER_SCHEMES_SUBKEY = "SYSTEM\\CurrentControlSet\\Control\\Power\\User\\PowerSchemes";
    private const string ACTIVE_OVERLAY_AC_POWER_SCHEME_KEY = "ActiveOverlayAcPowerScheme";
    private const string ACTIVE_OVERLAY_DC_POWER_SCHEME_KEY = "ActiveOverlayDcPowerScheme";

    private static readonly Guid DefaultPowerPlan = Guid.Parse("381b4222-f694-41f0-9685-ff5bb260df2e");
    private static readonly Guid BestPowerEfficiency = Guid.Parse("961cc777-2547-4f9d-8174-7d86181b8a7a");
    private static readonly Guid BestPerformance = Guid.Parse("ded574b5-45a0-4f42-8737-46345c09c238");

    private readonly ThrottleLastDispatcher _dispatcher = new(TimeSpan.FromSeconds(2), nameof(WindowsPowerModeController));
    private readonly SemaphoreSlim _lock = new(1, 1);

    public async Task SetPowerModeAsync(PowerModeState powerModeState, GodModeSettingsStore.Preset? preset = null, bool skipThrottle = false)
    {
        await _lock.WaitAsync().ConfigureAwait(false);
        try
        {
            if (settings.Store.PowerModeMappingMode is not PowerModeMappingMode.WindowsPowerMode)
            {
                Log.Instance.Trace($"Ignoring... [powerModeMappingMode={settings.Store.PowerModeMappingMode}]");
                return;
            }

            Log.Instance.Trace($"Activating... [powerModeState={powerModeState}]");

            var defaultMode = settings.Store.PowerModes.GetValueOrDefault(powerModeState, WindowsPowerMode.Balanced);
            var powerModeOnAc = preset?.Overrides.TryGetEnum<WindowsPowerMode>(PowerOverrideKey.PowerModeOnAc) ?? settings.Store.Overrides.GetPowerModeOnAc(powerModeState) ?? defaultMode;
            var powerModeOnDc = preset?.Overrides.TryGetEnum<WindowsPowerMode>(PowerOverrideKey.PowerModeOnDc) ?? settings.Store.Overrides.GetPowerModeOnDc(powerModeState) ?? defaultMode;

            var acGuid = GuidForWindowsPowerMode(powerModeOnAc);
            var dcGuid = GuidForWindowsPowerMode(powerModeOnDc);

            if (Power.IsBatterySaverEnabled())
            {
                Log.Instance.Trace($"Battery saver is on - will not set overlay scheme.");
                return;
            }

            var adapterStatus = await Power.IsPowerAdapterConnectedAsync().ConfigureAwait(false);
            var activeGuid = adapterStatus != PowerAdapterStatus.Disconnected ? acGuid : dcGuid;

            if (skipThrottle)
            {
                await _dispatcher.DispatchImmediateAsync(() => ExecuteOverlayDispatch(activeGuid, acGuid, dcGuid)).ConfigureAwait(false);
            }
            else
            {
                await _dispatcher.DispatchAsync(() => ExecuteOverlayDispatch(activeGuid, acGuid, dcGuid)).ConfigureAwait(false);
            }

            Log.Instance.Trace($"Power mode activated... [powerModeState={powerModeState}, acGuid={acGuid}, dcGuid={dcGuid}]");
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task SetPowerModeAsync(ITSMode itsMode, bool skipThrottle = false)
    {
        await _lock.WaitAsync().ConfigureAwait(false);
        try
        {
            if (settings.Store.PowerModeMappingMode is not PowerModeMappingMode.WindowsPowerMode)
            {
                Log.Instance.Trace($"Ignoring... [powerModeMappingMode={settings.Store.PowerModeMappingMode}]");
                return;
            }

            Log.Instance.Trace($"Activating... [itsMode={itsMode}]");

            var defaultMode = settings.Store.ITSPowerModes.GetValueOrDefault(itsMode, WindowsPowerMode.Balanced);
            var powerModeOnAc = settings.Store.ITSOverrides.GetPowerModeOnAc(itsMode);
            var powerModeOnDc = settings.Store.ITSOverrides.GetPowerModeOnDc(itsMode);

            if (powerModeOnAc is null && powerModeOnDc is null)
            {
                Log.Instance.Trace($"Power mode is null. [itsMode={itsMode}]");
                return;
            }

            var acGuid = GuidForWindowsPowerMode(powerModeOnAc ?? defaultMode);
            var dcGuid = GuidForWindowsPowerMode(powerModeOnDc ?? defaultMode);

            if (Power.IsBatterySaverEnabled())
            {
                Log.Instance.Trace($"Battery saver is on - will not set overlay scheme.");
                return;
            }

            var adapterStatus = await Power.IsPowerAdapterConnectedAsync().ConfigureAwait(false);
            var activeGuid = adapterStatus != PowerAdapterStatus.Disconnected ? acGuid : dcGuid;

            if (skipThrottle)
            {
                await _dispatcher.DispatchImmediateAsync(() => ExecuteOverlayDispatch(activeGuid, acGuid, dcGuid)).ConfigureAwait(false);
            }
            else
            {
                await _dispatcher.DispatchAsync(() => ExecuteOverlayDispatch(activeGuid, acGuid, dcGuid)).ConfigureAwait(false);
            }

            Log.Instance.Trace($"Power mode activated... [itsMode={itsMode}, acGuid={acGuid}, dcGuid={dcGuid}]");
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task SetBalancedPowerModeAsync(bool skipThrottle = false)
    {
        await _lock.WaitAsync().ConfigureAwait(false);
        try
        {
            if (Power.IsBatterySaverEnabled())
            {
                Log.Instance.Trace($"Battery saver is on - will not set overlay scheme.");
                return;
            }

            var balancedGuid = Guid.Empty;

            if (skipThrottle)
            {
                await _dispatcher.DispatchImmediateAsync(() => ExecuteOverlayDispatch(balancedGuid, balancedGuid, balancedGuid)).ConfigureAwait(false);
            }
            else
            {
                await _dispatcher.DispatchAsync(() => ExecuteOverlayDispatch(balancedGuid, balancedGuid, balancedGuid)).ConfigureAwait(false);
            }

            Log.Instance.Trace($"Balanced power mode set.");
        }
        finally
        {
            _lock.Release();
        }
    }

    private Task ExecuteOverlayDispatch(Guid activeGuid, Guid acGuid, Guid dcGuid)
    {
        try
        {
            ActivateDefaultPowerPlanIfNeeded();
        }
        catch (Exception ex)
        {
            Log.Instance.Trace($"Failed to activate default power plan.", ex);
        }

        mainThreadDispatcher.Dispatch(() =>
        {
            try
            {
                var result = PowerSetActiveOverlayScheme(activeGuid);
                Log.Instance.Trace($"Overlay scheme set. [result={result}, activeGuid={activeGuid}]");
            }
            catch (Exception ex)
            {
                Log.Instance.Trace($"Failed to set active overlay scheme.", ex);
            }
        });

        try
        {
            SetActiveOverlayRegistryForAc(acGuid);
            SetActiveOverlayRegistryForDc(dcGuid);
        }
        catch (Exception ex)
        {
            Log.Instance.Trace($"Failed to update registry.", ex);
        }

        return Task.CompletedTask;
    }

    public static void SetActiveOverlayRegistryForAc(Guid guid)
    {
        try
        {
            Registry.SetValue(POWER_SCHEMES_HIVE, POWER_SCHEMES_SUBKEY, ACTIVE_OVERLAY_AC_POWER_SCHEME_KEY, guid, true);
            Log.Instance.Trace($"Set {ACTIVE_OVERLAY_AC_POWER_SCHEME_KEY} to {guid}");
        }
        catch (Exception ex)
        {
            Log.Instance.Trace($"Failed to set {ACTIVE_OVERLAY_AC_POWER_SCHEME_KEY} registry value.", ex);
        }
    }

    public static void SetActiveOverlayRegistryForDc(Guid guid)
    {
        try
        {
            Registry.SetValue(POWER_SCHEMES_HIVE, POWER_SCHEMES_SUBKEY, ACTIVE_OVERLAY_DC_POWER_SCHEME_KEY, guid, true);
            Log.Instance.Trace($"Set {ACTIVE_OVERLAY_DC_POWER_SCHEME_KEY} to {guid}");
        }
        catch (Exception ex)
        {
            Log.Instance.Trace($"Failed to set {ACTIVE_OVERLAY_DC_POWER_SCHEME_KEY} registry value.", ex);
        }
    }

    public static Guid GuidForWindowsPowerMode(WindowsPowerMode windowsPowerMode) => windowsPowerMode switch
    {
        WindowsPowerMode.BestPowerEfficiency => BestPowerEfficiency,
        WindowsPowerMode.Balanced => Guid.Empty,
        WindowsPowerMode.BestPerformance => BestPerformance,
        _ => throw new ArgumentOutOfRangeException(nameof(windowsPowerMode), windowsPowerMode, null)
    };

    public static string? WindowsPowerModeNameFromGuid(Guid guid)
    {
        if (guid == BestPerformance)
            return nameof(WindowsPowerMode.BestPerformance);
        if (guid == BestPowerEfficiency)
            return nameof(WindowsPowerMode.BestPowerEfficiency);
        if (guid == Guid.Empty)
            return nameof(WindowsPowerMode.Balanced);
        return null;
    }

    public static void ApplyActiveOverlayScheme(Guid guid)
    {
        var result = PowerSetActiveOverlayScheme(guid);
        Log.Instance.Trace($"Overlay scheme set. [result={result}, guid={guid}]");
    }

    private static unsafe void ActivateDefaultPowerPlanIfNeeded()
    {
        if (PInvoke.PowerGetActiveScheme(null, out var guid) != WIN32_ERROR.ERROR_SUCCESS)
            PInvokeExtensions.ThrowIfWin32Error("PowerGetActiveScheme");

        if (PowerPlanExtensions.IsPlanBasedOnBalanced(*guid))
        {
            Log.Instance.Trace($"Default power plan is already active.");
            return;
        }

        if (PInvoke.PowerSetActiveScheme(null, DefaultPowerPlan) != WIN32_ERROR.ERROR_SUCCESS)
            PInvokeExtensions.ThrowIfWin32Error("PowerSetActiveScheme");

        Log.Instance.Trace($"Activated default power plan.");
    }

    [LibraryImport("powrprof.dll", EntryPoint = "PowerSetActiveOverlayScheme")]
    private static partial uint PowerSetActiveOverlayScheme(Guid guid);

    [LibraryImport("powrprof.dll", EntryPoint = "PowerGetUserConfiguredACPowerMode")]
    internal static partial uint PowerGetUserConfiguredACPowerMode(out Guid guid);

    [LibraryImport("powrprof.dll", EntryPoint = "PowerGetUserConfiguredDCPowerMode")]
    internal static partial uint PowerGetUserConfiguredDCPowerMode(out Guid guid);
}
