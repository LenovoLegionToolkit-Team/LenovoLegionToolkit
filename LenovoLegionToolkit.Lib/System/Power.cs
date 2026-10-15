using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Windows.Win32;
using LenovoLegionToolkit.Lib.System.Management;
using LenovoLegionToolkit.Lib.Utils;

namespace LenovoLegionToolkit.Lib.System;

public static class Power
{
    public static async Task<PowerAdapterStatus> IsPowerAdapterConnectedAsync()
    {
        if (!PInvoke.GetSystemPowerStatus(out var sps))
            return PowerAdapterStatus.Connected;

        var adapterConnected = sps.ACLineStatus == 1;
        var acFitForOc = await IsAcFitForOc().ConfigureAwait(false) ?? true;
        var chargingNormally = await IsChargingNormally().ConfigureAwait(false) ?? true;

        return (adapterConnected, acFitForOc && chargingNormally) switch
        {
            (true, false) => PowerAdapterStatus.ConnectedLowWattage,
            (true, _) => PowerAdapterStatus.Connected,
            (false, _) => PowerAdapterStatus.Disconnected,
        };
    }

    public static bool IsBatterySaverEnabled()
    {
        if (!PInvoke.GetSystemPowerStatus(out var sps))
            return false;

        return sps.SystemStatusFlag == 1;
    }

    public static async Task RestartAsync()
    {
        Log.Instance.Trace($"Restarting...");

        await CMD.RunAsync("shutdown", "/r /t 0").ConfigureAwait(false);
    }

    public static async Task ShutdownAsync()
    {
        Log.Instance.Trace($"Shutting down...");

        await CMD.RunAsync("shutdown", "/s /t 0").ConfigureAwait(false);
    }

    public static Task SleepAsync()
    {
        Log.Instance.Trace($"Sleeping...");

        SetSuspendState(false, true, false);
        return Task.CompletedTask;
    }

    public static Task HibernateAsync()
    {
        Log.Instance.Trace($"Hibernating...");

        SetSuspendState(true, true, false);
        return Task.CompletedTask;
    }

    [DllImport("powrprof.dll", SetLastError = true)]
    private static extern bool SetSuspendState([MarshalAs(UnmanagedType.Bool)] bool bHibernate, [MarshalAs(UnmanagedType.Bool)] bool bForce, [MarshalAs(UnmanagedType.Bool)] bool bWakeupEventsDisabled);

    private static async Task<bool?> IsAcFitForOc()
    {
        try
        {
            var result = await WMI.LenovoGameZoneData.IsACFitForOCAsync().ConfigureAwait(false);

            Log.Instance.Trace($"Mode = {result}");

            return result == 1;
        }
        catch
        {
            return null;
        }
    }

    private static async Task<bool?> IsChargingNormally()
    {
        try
        {
            var result = await WMI.LenovoGameZoneData.GetPowerChargeModeAsync().ConfigureAwait(false);

            Log.Instance.Trace($"Mode = {result}");

            return result == 1;
        }
        catch
        {
            return null;
        }
    }
}