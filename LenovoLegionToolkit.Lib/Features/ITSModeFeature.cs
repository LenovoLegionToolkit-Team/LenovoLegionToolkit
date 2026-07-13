using LenovoLegionToolkit.Lib.Extensions;
using LenovoLegionToolkit.Lib.Listeners;
using LenovoLegionToolkit.Lib.Messaging;
using LenovoLegionToolkit.Lib.Messaging.Messages;
using LenovoLegionToolkit.Lib.Resources;
using LenovoLegionToolkit.Lib.Settings;
using LenovoLegionToolkit.Lib.System;
using LenovoLegionToolkit.Lib.Utils;
using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Management;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.ServiceProcess;
using System.Threading;
using System.Threading.Tasks;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Storage.FileSystem;
using Registry = Microsoft.Win32.Registry;

namespace LenovoLegionToolkit.Lib.Features;

public partial class ITSModeFeature : IFeature<ITSMode>
{
    #region Magic Constants
    private const string REG_KEY_LITSSVC_BASE = @"SYSTEM\CurrentControlSet\Services\LITSSVC\LNBITS\IC";
    private const string REG_KEY_LITSSVC_MMC = @"SYSTEM\CurrentControlSet\Services\LITSSVC\LNBITS\IC\MMC";
    private const string REG_KEY_DISPATCHER = @"SYSTEM\CurrentControlSet\Services\LenovoProcessManagement\Performance\PowerSlider";

    private const string VAL_VERSION = "Version";
    private const string VAL_CAPABILITY = "Capability";
    private const string VAL_AUTO_SETTING = "AutomaticModeSetting";
    private const string VAL_CURRENT_SETTING = "CurrentSetting";
    private const string VAL_ITS_FN_CAP = "ITS_FN_Capability";
    private const string VAL_ITS_CUR_SET = "ITS_CurrentSetting";
    private const string VAL_ITS_CUR_SET_V = "ITS_CurrentSettingV";

    private const string DISPATCHER_SERVICE_NAME = "LenovoProcessManagement";
    private const string ITS_SERVICE_NAME = "LITSSVC";

    private const uint DISPATCHER_VERSION_3 = 8192U;
    #endregion

    private readonly ITSModeListener _listener;
    private bool? _supportsGeekMode;

    public ITSMode LastItsMode { get; set; } = ITSMode.None;

    public ITSModeFeature(ITSModeListener listener)
    {
        _listener = listener;
        MessagingCenter.Subscribe<ITSModeToggleRequestMessage>(this, OnToggleRequestAsync);
    }

    private async void OnToggleRequestAsync(ITSModeToggleRequestMessage msg)
    {
        try
        {
            if (!await IsSupportedAsync().ConfigureAwait(false))
                return;

            var result = await ToggleItsMode().ConfigureAwait(false);

            if (result != ITSMode.None)
                await _listener.OnChangedAsync(result).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Log.Instance.Trace($"Error handling ITS mode toggle request", ex);
        }
    }

    private void SaveCurrentStateToSettings(ITSMode state)
    {
        var settings = IoCContainer.Resolve<ITSModeSettings>();
        settings.Store.LastState = state;
        settings.SynchronizeStore();
    }

    public async Task<bool> IsSupportedAsync()
    {
        if (AppFlags.Instance.Debug)
        {
            return true;
        }

        var machineInfo = await Compatibility.GetMachineInformationAsync().ConfigureAwait(false);
        return machineInfo.Properties.SupportsITSMode;
    }

    public Task<ITSMode[]> GetAllStatesAsync()
    {
        var modes = Enum.GetValues<ITSMode>().Cast<ITSMode>();

        if (!SupportsGeekMode())
            modes = modes.Where(mode => mode != ITSMode.MmcGeek);

        return Task.FromResult(modes.Where(mode => mode != ITSMode.None).ToArray());
    }

    public async Task<ITSMode> GetStateAsync()
    {
        try
        {
            return await GetITSModeEx().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Log.Instance.Trace($"Failed to get ITS mode", ex);
            return ITSMode.None;
        }
    }

    public async Task SetStateAsync(ITSMode state)
    {
        if (state == ITSMode.None)
        {
            Log.Instance.Trace($"Can't set ITS mode to None, operation aborted.");
            return;
        }

        Log.Instance.Trace($"Setting ITS mode to: {state}");

        try
        {
            await SetITSModeExAsync(state).ConfigureAwait(false);

            if (!await WaitForITSModeAsync(state, CancellationToken.None).ConfigureAwait(false))
            {
                throw new InvalidOperationException($"ITS mode did not change to {state}.");
            }

            LastItsMode = state;

            Log.Instance.Trace($"ITS mode set successfully to: {state}");

            SaveCurrentStateToSettings(state);
        }
        catch (Exception ex)
        {
            Log.Instance.Trace($"Failed to set ITS mode to {state}", ex);
            throw;
        }
    }

    public async Task<bool> RestoreStateAsync()
    {
        try
        {
            if (!await IsSupportedAsync().ConfigureAwait(false))
            {
                return false;
            }

            var currentState = await GetStateAsync().ConfigureAwait(false);
            var settings = IoCContainer.Resolve<ITSModeSettings>();
            var savedState = settings.Store.LastState;

            if (savedState != ITSMode.None && savedState != currentState)
            {
                Log.Instance.Trace($"Restoring saved ITS mode: {savedState}");
                await SetStateAsync(savedState).ConfigureAwait(false);
            }
            else
            {
                await SetStateAsync(currentState).ConfigureAwait(false);

                if (savedState != currentState)
                {
                    settings.Store.LastState = currentState;
                    settings.SynchronizeStore();
                }
            }

            return true;
        }
        catch (Exception ex)
        {
            Log.Instance.Trace($"Couldn't ensure ITS mode state.", ex);
            return false;
        }
    }

    public async Task<ITSMode> ToggleItsMode()
    {
        try
        {
            var currentState = await GetStateAsync().ConfigureAwait(false);

            var supportedStates = await GetAllStatesAsync().ConfigureAwait(false);
            var supportedSet = new HashSet<ITSMode>(supportedStates);

            var settings = IoCContainer.Resolve<ITSModeSettings>();
            var userOrder = settings.Store.FnQModeOrder;
            if (userOrder == null || userOrder.Count == 0)
                userOrder = [ITSMode.MmcCool, ITSMode.ItsAuto, ITSMode.MmcPerformance, ITSMode.MmcGeek];
            var disabledSet = new HashSet<ITSMode>(settings.Store.DisabledModes ?? []);

            var isConnected = await Power.IsPowerAdapterConnectedAsync().ConfigureAwait(false) == PowerAdapterStatus.Connected;

            var availableStates = userOrder
                .Where(s => supportedSet.Contains(s) && !disabledSet.Contains(s) && (isConnected || s != ITSMode.MmcGeek))
                .ToArray();

            if (availableStates.Length == 0) return ITSMode.None;

            ITSMode nextState;

            var currentIndex = Array.IndexOf(availableStates, currentState);

            if (currentIndex >= 0)
            {
                nextState = availableStates[(currentIndex + 1) % availableStates.Length];
            }
            else
            {
                if (!isConnected && currentState == ITSMode.MmcGeek)
                {
                    Log.Instance.Trace($"Currently in MMC Geek mode but on battery. Resetting to fallback state.");
                }

                nextState = (LastItsMode != ITSMode.None && availableStates.Contains(LastItsMode))
                    ? LastItsMode
                    : availableStates[0];
            }

            if (currentState == nextState)
            {
                return currentState;
            }

            Log.Instance.Trace($"Toggling ITS mode: {currentState} -> {nextState}");
            await SetStateAsync(nextState).ConfigureAwait(false);

            return nextState;
        }
        catch (Exception ex)
        {
            Log.Instance.Trace($"Failed to toggle ITS mode", ex);
            return ITSMode.None;
        }
    }

    private bool SupportsGeekMode()
    {
        _supportsGeekMode ??= GetDispatcherVersionEx() >= DISPATCHER_VERSION_3;
        return _supportsGeekMode.Value;
    }

    private bool ShowGeekAsCreatorMode()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(REG_KEY_DISPATCHER, false);
            if (key != null)
            {
                int capability = ReadRegistryInt(key, VAL_ITS_FN_CAP, 0);
                return (capability & 0x20) != 0;
            }
        }
        catch (Exception ex)
        {
            Log.Instance.Trace($"ShowGeekAsCreatorMode() failed", ex);
        }
        return false;
    }

    public string GetITSModeDisplayName(ITSMode mode)
    {
        if (mode == ITSMode.MmcGeek && ShowGeekAsCreatorMode())
            return Resource.ITSMode_Intelligent_Creator;
        return mode.GetDisplayName();
    }

    public async Task<ITSMode> GetITSModeEx()
    {
        try
        {
            if (!IsEnergyDriverPresentEx())
            {
                Log.Instance.Trace($"EnergyDrv not found, returning None.");
                return ITSMode.None;
            }

            var dispatcherVersion = GetDispatcherVersionEx();

            if (dispatcherVersion >= DISPATCHER_VERSION_3)
            {
                using RegistryKey? key = Registry.LocalMachine.OpenSubKey(REG_KEY_DISPATCHER, false);
                if (key != null)
                {
                    int capability = ReadRegistryInt(key, VAL_ITS_FN_CAP, 0);
                    bool useVersioned = (capability & 0x10) != 0;

                    int currentSetting = ReadRegistryInt(key, useVersioned ? VAL_ITS_CUR_SET_V : VAL_ITS_CUR_SET, -1);
                    if (currentSetting == -1 && useVersioned)
                        currentSetting = ReadRegistryInt(key, VAL_ITS_CUR_SET, -1);
                    else if (currentSetting == -1 && !useVersioned)
                        currentSetting = ReadRegistryInt(key, VAL_ITS_CUR_SET_V, -1);

                    Log.Instance.Trace($"ITS mode check: {(useVersioned ? VAL_ITS_CUR_SET_V : VAL_ITS_CUR_SET)}={currentSetting}");

                    return currentSetting switch
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
                using RegistryKey? key = Registry.LocalMachine.OpenSubKey(REG_KEY_LITSSVC_MMC, false);
                if (key != null)
                {
                    int autoSetting = ReadRegistryInt(key, VAL_AUTO_SETTING, -1);
                    int currentSetting = ReadRegistryInt(key, VAL_CURRENT_SETTING, -1);

                    Log.Instance.Trace($"ITS mode check: Legacy Auto={autoSetting}, Current={currentSetting}");

                    if (autoSetting == 2 && currentSetting == 0)
                    {
                        return ITSMode.ItsAuto;
                    }
                    else if (autoSetting == 1 && currentSetting == 1)
                    {
                        return ITSMode.MmcCool;
                    }
                    else if (autoSetting == 1 && currentSetting == 3)
                    {
                        return ITSMode.MmcPerformance;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Log.Instance.Trace($"GetITSModeEx failed", ex);
        }

        return ITSMode.None;
    }

    private Task SetITSModeExAsync(ITSMode mode)
    {
        try
        {
            var dispatcherVersion = GetDispatcherVersionEx();

            string targetServiceName;
            ITSModeServiceControlMessage targetMessage;

            if (dispatcherVersion >= DISPATCHER_VERSION_3)
            {
                targetServiceName = DISPATCHER_SERVICE_NAME;
                targetMessage = mode switch
                {
                    ITSMode.ItsAuto => ITSModeServiceControlMessage.IntelligentCoolingIntelligent,
                    ITSMode.MmcCool => ITSModeServiceControlMessage.IntelligentCoolingBsm,
                    ITSMode.MmcPerformance => ITSModeServiceControlMessage.IntelligentCoolingEpm,
                    ITSMode.MmcGeek => ITSModeServiceControlMessage.IntelligentCoolingGeek,
                    _ => throw new ArgumentOutOfRangeException(nameof(mode))
                };
            }
            else
            {
                targetServiceName = ITS_SERVICE_NAME;
                targetMessage = mode switch
                {
                    ITSMode.ItsAuto => ITSModeServiceControlMessage.IntelligentCoolingEnable,
                    ITSMode.MmcCool => ITSModeServiceControlMessage.IntelligentCoolingCool,
                    ITSMode.MmcPerformance => ITSModeServiceControlMessage.IntelligentCoolingHighPerformance,
                    _ => throw new ArgumentOutOfRangeException(nameof(mode))
                };
            }

            Log.Instance.Trace($"Setting ITS mode via service: {targetServiceName} ({targetMessage})");
            ControlService(targetServiceName, targetMessage);
            return Task.CompletedTask;
        }
        catch (Exception ex)
        {
            Log.Instance.Trace($"{nameof(SetITSModeExAsync)} failed", ex);
            throw;
        }
    }

    private int GetDispatcherVersionEx() => ReadRegistryVersion(REG_KEY_DISPATCHER, nameof(GetDispatcherVersionEx));

    private int GetITSVersionEx() => ReadRegistryVersion(REG_KEY_LITSSVC_BASE, nameof(GetITSVersionEx));

    private bool HasDispatcherDeviceNodeEx()
    {
        try
        {
            string query = "SELECT PNPDeviceID FROM Win32_PnPEntity WHERE PNPDeviceID LIKE 'ACPI\\IDEA200C%'";

            using ManagementObjectSearcher searcher = new ManagementObjectSearcher(query);
            using ManagementObjectCollection collection = searcher.Get();
            return collection.Count > 0;
        }
        catch (Exception ex)
        {
            Log.Instance.Trace($"HasDispatcherDeviceNodeEx failed", ex);
        }
        return false;
    }

    private bool IsEnergyDriverPresentEx()
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
            {
                return true;
            }

            var error = Marshal.GetLastWin32Error();
            return error == (int)WIN32_ERROR.ERROR_ACCESS_DENIED;
        }
        catch (Exception ex)
        {
            Log.Instance.Trace($"Energy driver check failed", ex);
            return false;
        }
    }

    private void ControlService(string serviceName, ITSModeServiceControlMessage message)
    {
        try
        {
            using ServiceController serviceController = new ServiceController(serviceName);
            Log.Instance.Trace($"Service {serviceName} status: {serviceController.Status}");

            serviceController.ExecuteCommand((int)message);
            Log.Instance.Trace($"Service {serviceName} successfully executed command: {message}");
        }
        catch (Exception ex)
        {
            Log.Instance.Trace($"Failed to control service", ex);
            throw;
        }
    }

    private int ReadRegistryVersion(string subKey, string caller)
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(subKey, writable: false);
            return key != null ? ReadRegistryInt(key, VAL_VERSION, 0) : 0;
        }
        catch (Exception ex)
        {
            Log.Instance.Trace($"{caller} failed on reading registry", ex);
            return 0;
        }
    }

    private int ReadRegistryInt(RegistryKey key, string valueName, int defaultValue)
    {
        var value = key.GetValue(valueName, defaultValue);

        if (value is int i) return i;
        if (value is long l) return (int)l;
        if (value is uint u) return (int)u;

        try
        {
            return Convert.ToInt32(value);
        }
        catch (Exception ex)
        {
            Log.Instance.Trace($"Unexpected type for registry value {valueName}: {value?.GetType().Name ?? "null"}", ex);
            return defaultValue;
        }
    }

    private async Task<bool> WaitForITSModeAsync(ITSMode expected, CancellationToken token)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(3);

        do
        {
            if (await GetITSModeEx().ConfigureAwait(false) == expected)
            {
                return true;
            }

            await Task.Delay(200, token).ConfigureAwait(false);
        }
        while (DateTimeOffset.UtcNow < deadline && !token.IsCancellationRequested);

        return false;
    }
}
