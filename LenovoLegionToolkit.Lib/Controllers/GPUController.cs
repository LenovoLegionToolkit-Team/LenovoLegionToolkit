using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using LenovoLegionToolkit.Lib.Extensions;
using LenovoLegionToolkit.Lib.Features.Hybrid;
using LenovoLegionToolkit.Lib.Resources;
using LenovoLegionToolkit.Lib.Settings;
using LenovoLegionToolkit.Lib.System;
using LenovoLegionToolkit.Lib.System.Management;
using LenovoLegionToolkit.Lib.Utils;
using NeoSmart.AsyncLock;
using NvAPIWrapper;
using NvAPIWrapper.GPU;
using NvAPIWrapper.Native.Exceptions;
using Resource = LenovoLegionToolkit.Lib.Resources.Resource;

namespace LenovoLegionToolkit.Lib.Controllers;

public class GPUController
{
    private readonly AsyncLock _lock = new();

    public int Interval { get; set; } = 5000;

    public enum GpuPreference
    {
        Default = 0,
        Integrated = 1,
        Discrete = 2
    }
    private Task? _refreshTask;
    private CancellationTokenSource? _refreshCancellationTokenSource;

    private GPUState _state = GPUState.Unknown;
    private List<Process> _processes = [];
    private List<Process> _allProcesses = [];
    private string? _gpuInstanceId;
    private string? _performanceState;

    public event EventHandler<GPUStatus>? Refreshed;
    public bool IsStarted { get => _refreshTask != null; }

    public bool IsSupported()
    {
        try
        {
            if (AppFlags.Instance.Debug)
            {
                return true;
            }

            NVAPI.Initialize();
            PhysicalGPU? gpu = NVAPI.GetGPU();
            return gpu is not null;
        }
        catch
        {
            return false;
        }
    }

    public async Task<GPUState> GetLastKnownStateAsync()
    {
        using (await _lock.LockAsync().ConfigureAwait(false))
            return _state;
    }

    public async Task<GPUStatus> RefreshNowAsync()
    {
        using (await _lock.LockAsync().ConfigureAwait(false))
        {
            await RefreshLoopAsync(0, CancellationToken.None, true).ConfigureAwait(false);
            return new GPUStatus(_state, _performanceState, _processes);
        }
    }

    public Task StartAsync(int delay = -1, int interval = 5_000)
    {
        if (IsStarted)
            return Task.CompletedTask;
        
        var startupDelay = delay >= 0 ? delay : IoCContainer.Resolve<ApplicationSettings>().Store.GPUMonitoringStartupDelay;

        _refreshCancellationTokenSource = new CancellationTokenSource();
        var token = _refreshCancellationTokenSource.Token;
        _refreshTask = Task.Run(() => RefreshLoopAsync(startupDelay, token), token);
        return Task.CompletedTask;
    }

    public async Task StopAsync(bool waitForFinish = false)
    {
        // Log.Instance.Trace($"Stopping... [refreshTask.isNull={_refreshTask is null}, _refreshCancellationTokenSource.IsCancellationRequested={_refreshCancellationTokenSource?.IsCancellationRequested}]");

        if (_refreshCancellationTokenSource is not null)
            await _refreshCancellationTokenSource.CancelAsync().ConfigureAwait(false);

        if (waitForFinish)
        {
            // Log.Instance.Trace($"Waiting to finish...");

            if (_refreshTask is not null)
            {
                try
                {
                    await _refreshTask.ConfigureAwait(false);
                }
                catch (OperationCanceledException) { }
            }

            // Log.Instance.Trace($"Finished");
        }

        _refreshCancellationTokenSource = null;
        _refreshTask = null;

        // Log.Instance.Trace($"Stopped");
    }

    public async Task RestartGPUAsync()
    {
        using (await _lock.LockAsync().ConfigureAwait(false))
        {
            Log.Instance.Trace($"Deactivating... [state={_state}, gpuInstanceId={_gpuInstanceId}]");

            if (_state is not GPUState.Active and not GPUState.Inactive)
                return;

            if (string.IsNullOrEmpty(_gpuInstanceId))
                return;

            await CMD.RunAsync("pnputil", $"/restart-device \"{_gpuInstanceId}\"").ConfigureAwait(false);

            Log.Instance.Trace($"Deactivating... [state= {_state}, gpuInstanceId={_gpuInstanceId}]");
        }
    }

    public async Task KillGPUProcessesAsync()
    {
        using (await _lock.LockAsync().ConfigureAwait(false))
        {
            Log.Instance.Trace($"Deactivating... [state= {_state}, gpuInstanceId={_gpuInstanceId}]");

            if (_state is not GPUState.Active)
                return;

            if (string.IsNullOrEmpty(_gpuInstanceId))
                return;

            foreach (var process in _processes)
            {
                try
                {
                    process.Kill(true);
                    await process.WaitForExitAsync().ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    Log.Instance.Trace($"Couldn't kill process. [pid={process.Id}, name={process.ProcessName}]", ex);
                }
            }

            Log.Instance.Trace($"Deactivating... [state=  {_state}, gpuInstanceId={_gpuInstanceId}]");
        }
    }

    private async Task RefreshLoopAsync(int delay, CancellationToken token, bool runOnce = false)
    {
        try
        {
            Log.Instance.Trace($"Initializing NVAPI...");

            NVAPI.Initialize();

            Log.Instance.Trace($"Initialized NVAPI");

            await Task.Delay(delay, token).ConfigureAwait(false);

            while (true)
            {
                token.ThrowIfCancellationRequested();

                using (await _lock.LockAsync(token).ConfigureAwait(false))
                {
                    await RefreshStateAsync().ConfigureAwait(false);
                    Refreshed?.Invoke(this, new GPUStatus(_state, _performanceState, _processes));
                }

                if (!runOnce && Interval > 0)
                    await Task.Delay(Interval, token).ConfigureAwait(false);
                else
                    break;
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Log.Instance.Trace($"Exception occurred", ex);

            throw;
        }
    }

    private async Task RefreshStateAsync()
    {
        _state = GPUState.Unknown;
        _processes = [];
        _allProcesses = [];
        _gpuInstanceId = null;
        _performanceState = null;

        var gpu = NVAPI.GetGPU();
        if (gpu is null)
        {
            _state = GPUState.NvidiaGpuNotFound;

            return;
        }

        try
        {
            var stateId = gpu.PerformanceStatesInfo.CurrentPerformanceState.StateId.ToString().GetUntilOrEmpty("_");
            _performanceState = Resource.GPUController_PoweredOn;
            if (!string.IsNullOrWhiteSpace(stateId))
                _performanceState += $", {stateId}";
        }
        catch (NVIDIAApiException ex) when ((int)ex.Status == -105 || (int)ex.Status == -220)
        {
            _state = GPUState.PoweredOff;
            _performanceState = Resource.GPUController_PoweredOff;

            Log.Instance.Trace($"Powered off [status={(int)ex.Status}, state={_state}, processes.Count={_processes.Count}, gpuInstanceId={_gpuInstanceId}]");

            return;
        }
        catch (Exception ex)
        {
            Log.Instance.Trace($"GPU status exception.", ex);

            _performanceState = null;
        }

        var pnpDeviceIdPart = NVAPI.GetGPUId(gpu);

        if (string.IsNullOrEmpty(pnpDeviceIdPart))
            throw new InvalidOperationException("pnpDeviceIdPart is null or empty");

        var gpuInstanceId = await WMI.Win32.PnpEntity.GetDeviceIDAsync(pnpDeviceIdPart).ConfigureAwait(false);
        var (allProcessNames, processNames) = NVAPIExtensions.GetActiveProcesses(gpu);
        var feature = IoCContainer.Resolve<HybridModeFeature>();

        _allProcesses = allProcessNames;

        if (await feature.GetStateAsync().ConfigureAwait(false) == HybridModeState.Off)
        {
            if (NVAPI.IsDisplayConnected(gpu))
            {
                _processes = processNames;
                _state = GPUState.MonitorConnected;

                // Comment due to annoying.
                //if (Log.Instance.IsTraceEnabled)
                //    Log.Instance.Trace($"Monitor connected [state={_state}, processes.Count={_processes.Count}, gpuInstanceId={_gpuInstanceId}]");
            }
        }
        else if (processNames.Count != 0)
        {
            _processes = processNames;
            _state = GPUState.Active;
            _gpuInstanceId = gpuInstanceId;

            Log.Instance.Trace($"Active [state={_state}, processes.Count={_processes.Count}, gpuInstanceId={_gpuInstanceId}, pnpDeviceIdPart={pnpDeviceIdPart}]");
        }
        else
        {
            _state = GPUState.Inactive;
            _gpuInstanceId = gpuInstanceId;

            Log.Instance.Trace($"Inactive [state={_state}, processes.Count={_processes.Count}, gpuInstanceId={_gpuInstanceId}]");
        }
    }

    public IReadOnlyList<Process> ActiveProcesses => _processes;
    public IReadOnlyList<Process> AllActiveProcesses => _allProcesses;

    public GpuPreference GetGpuPreference(string exePath)
    {
        var prefString = Registry.GetValue("HKEY_CURRENT_USER", @"SOFTWARE\Microsoft\DirectX\UserGpuPreferences", exePath, string.Empty);
        
        var isOurApp = string.Equals(exePath, Environment.ProcessPath, StringComparison.OrdinalIgnoreCase);
        var aumid = isOurApp ? GetAppUserModelId() : null;

        if (string.IsNullOrEmpty(prefString) && !string.IsNullOrEmpty(aumid))
            prefString = Registry.GetValue("HKEY_CURRENT_USER", @"SOFTWARE\Microsoft\DirectX\UserGpuPreferences", aumid, string.Empty);

        if (prefString.Contains("GpuPreference=1")) return GpuPreference.Integrated;
        if (prefString.Contains("GpuPreference=2")) return GpuPreference.Discrete;
        return GpuPreference.Default;
    }

    public void SetGpuPreference(string exePath, GpuPreference preference)
    {
        try
        {
            var isOurApp = string.Equals(exePath, Environment.ProcessPath, StringComparison.OrdinalIgnoreCase);
            var aumid = isOurApp ? GetAppUserModelId() : null;

            if (preference == GpuPreference.Default)
            {
                using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"SOFTWARE\Microsoft\DirectX\UserGpuPreferences", true);
                key?.DeleteValue(exePath, false);

                if (!string.IsNullOrEmpty(aumid))
                    key?.DeleteValue(aumid, false);
            }
            else
            {
                var value = preference == GpuPreference.Integrated ? "GpuPreference=1;" : "GpuPreference=2;";
                Registry.SetValue("HKEY_CURRENT_USER", @"SOFTWARE\Microsoft\DirectX\UserGpuPreferences", exePath, value, false, Microsoft.Win32.RegistryValueKind.String);

                if (!string.IsNullOrEmpty(aumid))
                    Registry.SetValue("HKEY_CURRENT_USER", @"SOFTWARE\Microsoft\DirectX\UserGpuPreferences", aumid, value, false, Microsoft.Win32.RegistryValueKind.String);
            }
        }
        catch (Exception ex)
        {
            Log.Instance.Trace($"Failed to set GPU preference for {exePath}.", ex);
        }
    }

    private static string? GetAppUserModelId()
    {
        try
        {
            return Windows.ApplicationModel.Package.Current.Id.FamilyName + "!App";
        }
        catch
        {
            return null;
        }
    }
}
