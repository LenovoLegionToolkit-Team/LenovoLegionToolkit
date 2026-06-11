using LenovoLegionToolkit.Lib.Extensions;
using LenovoLegionToolkit.Lib.System;
using LenovoLegionToolkit.Lib.Utils;
using Microsoft.Win32;
using System;
using System.Runtime.InteropServices;
using System.ServiceProcess;
using System.Threading;
using System.Threading.Tasks;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Storage.FileSystem;
using Registry = Microsoft.Win32.Registry;

namespace LenovoLegionToolkit.Lib.Features;

public class ITSModeFeatureDriver : AbstractDriverFeature<ITSMode>
{
    private const uint DYTC_CMD_GET_DISPATCHER_MODE = 14;
    private const uint DYTC_REG_ITSSETTING = 0x10;
    private const uint DYTC_FLAG_ITS_MODE = 0x2000;
    private const uint DYTC_FLAG_EPM_MODE = 0x4000;

    private const uint DYTC_MODE_EPM = 0;
    private const uint DYTC_MODE_INTELLIGENT = 1;
    private const uint DYTC_MODE_BSM = 5;

    private const uint MODE_SHIFT = 20;

    private const string REG_KEY_DISPATCHER = @"SYSTEM\CurrentControlSet\Services\LenovoProcessManagement\Performance\PowerSlider";
    private const string REG_KEY_LITSSVC_MMC = @"SYSTEM\CurrentControlSet\Services\LITSSVC\LNBITS\IC\MMC";

    private const string VAL_VERSION = "Version";
    private const string VAL_AUTO_SETTING = "AutomaticModeSetting";
    private const string VAL_CURRENT_SETTING = "CurrentSetting";
    private const string VAL_ITS_FN_CAP = "ITS_FN_Capability";
    private const string VAL_ITS_CUR_SET = "ITS_CurrentSetting";
    private const string VAL_ITS_CUR_SET_V = "ITS_CurrentSettingV";

    private const uint DISPATCHER_VERSION_3 = 8192U;

    private const string DISPATCHER_SERVICE_NAME = "LenovoProcessManagement";
    private const string ITS_SERVICE_NAME = "LITSSVC";

    private enum ServiceControlCode : int
    {
        LegacyDisable = 134,
        LegacyEnable = 135,
        LegacyCool = 146,
        LegacyHighPerformance = 148,

        ModernIntelligent = 163,
        ModernBsm = 164,
        ModernEpm = 165,
        ModernGeek = 172,
    }

    private bool? _isModern;
    private ITSMode _lastMode;

    public ITSModeFeatureDriver()
        : base(Drivers.GetEnergy, Drivers.IOCTL_DYTC, useDriverQueue: true) { }

    public override async Task<bool> IsSupportedAsync()
    {
        try
        {
            if (AppFlags.Instance.Debug)
                return true;

            if (!IsEnergyDriverPresent())
                return false;

            _ = await GetStateInternalAsync(bypassQueue: true).ConfigureAwait(false);
            return true;
        }
        catch
        {
            return false;
        }
    }

    protected override Task<ITSMode> GetStateInternalAsync(bool bypassQueue)
    {
        return Task.FromResult(GetStateFromRegistry());
    }

    protected override uint GetInBufferValue() => DYTC_CMD_GET_DISPATCHER_MODE;

    protected override Task<ITSMode> FromInternalAsync(uint raw)
    {
        return Task.FromResult(ITSMode.None);
    }

    public override async Task SetStateAsync(ITSMode state)
    {
        if (state == ITSMode.None)
        {
            Log.Instance.Trace($"Can't set ITS mode to None, operation aborted.");
            return;
        }

        Log.Instance.Trace($"Setting ITS mode to: {state} [driver path]");

        if (GetIsModern())
            await SetStateViaEnergyDrvAsync(state).ConfigureAwait(false);
        else
            SetStateViaLegacyService(state);

        if (!await WaitForModeAsync(state, CancellationToken.None).ConfigureAwait(false))
            throw new InvalidOperationException($"ITS mode did not change to {state}.");

        _lastMode = state;
        Log.Instance.Trace($"ITS mode set successfully to: {state}");
    }

    protected override Task<uint[]> ToInternalAsync(ITSMode state)
    {
        return Task.FromResult(Array.Empty<uint>());
    }

    private async Task SetStateViaEnergyDrvAsync(ITSMode state)
    {
        uint modeValue = MapITSModeToDYTC(state);
        uint command = DYTC_FLAG_ITS_MODE | (modeValue << (int)MODE_SHIFT) | DYTC_REG_ITSSETTING;

        Log.Instance.Trace($"EnergyDrv DYTC SET: modeValue={modeValue}, command=0x{command:X8}");
        await SendCodeAsync(DriverHandle(), ControlCode, command, bypassQueue: false).ConfigureAwait(false);
    }

    private static void SetStateViaLegacyService(ITSMode state)
    {
        ServiceControlCode code = state switch
        {
            ITSMode.ItsAuto => ServiceControlCode.LegacyEnable,
            ITSMode.MmcCool => ServiceControlCode.LegacyCool,
            ITSMode.MmcPerformance => ServiceControlCode.LegacyHighPerformance,
            _ => throw new ArgumentOutOfRangeException(nameof(state), $"Legacy path does not support {state}")
        };

        Log.Instance.Trace($"Legacy ControlService: {ITS_SERVICE_NAME} → {code} ({(int)code})");
        using var sc = new ServiceController(ITS_SERVICE_NAME);
        sc.ExecuteCommand((int)code);
    }

    private ITSMode GetStateFromRegistry()
    {
        uint dispatcherVersion = ReadRegistryVersion(REG_KEY_DISPATCHER);

        if (dispatcherVersion >= DISPATCHER_VERSION_3)
        {
            using var key = Registry.LocalMachine.OpenSubKey(REG_KEY_DISPATCHER, writable: false);
            if (key != null)
            {
                int capability = ReadRegistryInt(key, VAL_ITS_FN_CAP, 0);
                bool useVersioned = (capability & 0x10) != 0;
                string settingKey = useVersioned ? VAL_ITS_CUR_SET_V : VAL_ITS_CUR_SET;

                int current = ReadRegistryInt(key, settingKey, -1);
                Log.Instance.Trace($"ITS mode read (modern): {settingKey}={current}");

                return current switch
                {
                    0 => ITSMode.ItsAuto,
                    1 => ITSMode.MmcCool,
                    3 => ITSMode.MmcPerformance,
                    4 => ITSMode.MmcGeek,
                    _ => ITSMode.None
                };
            }
        }
        else
        {
            using var key = Registry.LocalMachine.OpenSubKey(REG_KEY_LITSSVC_MMC, writable: false);
            if (key != null)
            {
                int auto = ReadRegistryInt(key, VAL_AUTO_SETTING, -1);
                int current = ReadRegistryInt(key, VAL_CURRENT_SETTING, -1);
                Log.Instance.Trace($"ITS mode read (legacy): Auto={auto}, Current={current}");

                if (auto == 2 && current == 0) return ITSMode.ItsAuto;
                if (auto == 1 && current == 1) return ITSMode.MmcCool;
                if (auto == 1 && current == 3) return ITSMode.MmcPerformance;
            }
        }

        return ITSMode.None;
    }

    private static uint MapITSModeToDYTC(ITSMode state)
    {
        return state switch
        {
            ITSMode.MmcPerformance => DYTC_MODE_EPM,
            ITSMode.ItsAuto => DYTC_MODE_INTELLIGENT,
            ITSMode.MmcCool => DYTC_MODE_BSM,
            ITSMode.MmcGeek => DYTC_MODE_EPM,
            _ => throw new ArgumentOutOfRangeException(nameof(state), $"No DYTC mapping for {state}")
        };
    }

    private bool GetIsModern()
    {
        _isModern ??= ReadRegistryVersion(REG_KEY_DISPATCHER) >= DISPATCHER_VERSION_3;
        return _isModern.Value;
    }

    private static uint ReadRegistryVersion(string subKey)
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(subKey, writable: false);
            return key != null ? (uint)ReadRegistryInt(key, VAL_VERSION, 0) : 0;
        }
        catch (Exception ex)
        {
            Log.Instance.Trace($"ReadRegistryVersion({subKey}) failed", ex);
            return 0;
        }
    }

    private static int ReadRegistryInt(RegistryKey key, string valueName, int defaultValue)
    {
        var value = key.GetValue(valueName, defaultValue);
        if (value is int i) return i;
        if (value is long l) return (int)l;
        if (value is uint u) return (int)u;
        try { return Convert.ToInt32(value); }
        catch { return defaultValue; }
    }

    private static bool IsEnergyDriverPresent()
    {
        try
        {
            using var handle = PInvoke.CreateFile(
                @"\\.\EnergyDrv",
                0,
                FILE_SHARE_MODE.FILE_SHARE_READ | FILE_SHARE_MODE.FILE_SHARE_WRITE,
                null,
                FILE_CREATION_DISPOSITION.OPEN_EXISTING,
                FILE_FLAGS_AND_ATTRIBUTES.FILE_ATTRIBUTE_NORMAL,
                null);

            if (!handle.IsInvalid)
                return true;

            var error = Marshal.GetLastWin32Error();
            return error == (int)WIN32_ERROR.ERROR_ACCESS_DENIED;
        }
        catch (Exception ex)
        {
            Log.Instance.Trace($"IsEnergyDriverPresent failed", ex);
            return false;
        }
    }

    private async Task<bool> WaitForModeAsync(ITSMode expected, CancellationToken token)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(3);
        do
        {
            if (GetStateFromRegistry() == expected)
                return true;

            await Task.Delay(200, token).ConfigureAwait(false);
        }
        while (DateTimeOffset.UtcNow < deadline && !token.IsCancellationRequested);

        return false;
    }
}
