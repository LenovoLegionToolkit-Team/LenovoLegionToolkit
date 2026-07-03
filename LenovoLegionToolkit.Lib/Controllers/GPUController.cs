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
using NvAPIWrapper.DRS;
using NvAPIWrapper.Native.Exceptions;
using Resource = LenovoLegionToolkit.Lib.Resources.Resource;

namespace LenovoLegionToolkit.Lib.Controllers;

public class GPUController
{
    private readonly AsyncLock _stateLock = new();
    private readonly SemaphoreSlim _refreshGate = new(1, 1);

    public int Interval { get; set; } = 5000;

    public enum GpuPreference
    {
        Default = 0,
        Integrated = 1,
        Discrete = 2
    }

    private CancellationTokenSource? _refreshCancellationTokenSource;

    private GPUState _state = GPUState.Unknown;
    private List<Process> _processes = [];
    private List<Process> _allProcesses = [];
    private string? _gpuInstanceId;
    private string? _performanceState;

    public event EventHandler<GPUStatus>? Refreshed;
    public bool IsStarted => _refreshCancellationTokenSource is not null;

    public bool IsSupported()
    {
        try
        {
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
        using (await _stateLock.LockAsync().ConfigureAwait(false))
            return _state;
    }

    public async Task<GPUStatus> RefreshNowAsync()
    {
        await _refreshGate.WaitAsync().ConfigureAwait(false);
        try
        {
            await DoRefreshAsync(CancellationToken.None).ConfigureAwait(false);
        }
        finally
        {
            _refreshGate.Release();
        }

        using (await _stateLock.LockAsync().ConfigureAwait(false))
        {
            return new GPUStatus(_state, _performanceState, _processes);
        }
    }

    public Task StartAsync(int delay = -1, int interval = 5_000)
    {
        if (IsStarted)
            return Task.CompletedTask;

        Interval = interval;

        var startupDelay = delay >= 0 ? delay : IoCContainer.Resolve<ApplicationSettings>().Store.GPUMonitoringStartupDelay;

        _refreshCancellationTokenSource = new CancellationTokenSource();
        var token = _refreshCancellationTokenSource.Token;
        _ = RefreshLoopAsync(startupDelay, token);

        return Task.CompletedTask;
    }

    public async Task StopAsync(bool waitForFinish = false)
    {
        if (_refreshCancellationTokenSource is null)
            return;

        await _refreshCancellationTokenSource.CancelAsync().ConfigureAwait(false);

        await _refreshGate.WaitAsync().ConfigureAwait(false);
        _refreshGate.Release();

        _refreshCancellationTokenSource = null;
    }

    public async Task RestartGPUAsync()
    {
        string? gpuInstanceId;

        using (await _stateLock.LockAsync().ConfigureAwait(false))
        {
            if (_state is not GPUState.Active and not GPUState.Inactive)
                return;

            gpuInstanceId = _gpuInstanceId;
        }

        if (string.IsNullOrEmpty(gpuInstanceId))
            return;

        Log.Instance.Trace($"Restarting GPU... [gpuInstanceId={gpuInstanceId}]");

        await CMD.RunAsync("pnputil", $"/restart-device \"{gpuInstanceId}\"").ConfigureAwait(false);
    }

    public async Task KillGPUProcessesAsync()
    {
        List<Process> processes;
        string? gpuInstanceId;

        using (await _stateLock.LockAsync().ConfigureAwait(false))
        {
            if (_state is not GPUState.Active)
                return;

            processes = [.. _processes];
            gpuInstanceId = _gpuInstanceId;
        }

        if (string.IsNullOrEmpty(gpuInstanceId) || processes.Count == 0)
            return;

        Log.Instance.Trace($"Killing GPU processes... [gpuInstanceId={gpuInstanceId}]");

        foreach (var process in processes)
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
    }

    private async Task RefreshLoopAsync(int delay, CancellationToken token)
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

                await _refreshGate.WaitAsync(token).ConfigureAwait(false);
                try
                {
                    await DoRefreshAsync(token).ConfigureAwait(false);

                    using (await _stateLock.LockAsync(token).ConfigureAwait(false))
                        Refreshed?.Invoke(this, new GPUStatus(_state, _performanceState, _processes));
                }
                finally
                {
                    _refreshGate.Release();
                }

                if (Interval > 0)
                    await Task.Delay(Interval, token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            Log.Instance.Trace($"Exception occurred", ex);
            throw;
        }
    }

    private async Task DoRefreshAsync(CancellationToken token)
    {
        string? cachedGpuInstanceId;
        using (await _stateLock.LockAsync(token).ConfigureAwait(false))
            cachedGpuInstanceId = _gpuInstanceId;

        var gpu = NVAPI.GetGPU();
        if (gpu is null)
        {
            using (await _stateLock.LockAsync(token).ConfigureAwait(false))
            {
                _state = GPUState.NvidiaGpuNotFound;
                _performanceState = null;
                _processes = [];
                _allProcesses = [];
                _gpuInstanceId = null;
            }
            return;
        }

        GPUState newState;
        List<Process> newProcesses;
        List<Process> newAllProcesses;
        string? newGpuInstanceId;
        string? newPerformanceState;

        try
        {
            var stateId = gpu.PerformanceStatesInfo.CurrentPerformanceState.StateId.ToString().GetUntilOrEmpty("_");
            newPerformanceState = Resource.GPUController_PoweredOn;
            if (!string.IsNullOrWhiteSpace(stateId))
                newPerformanceState += $", {stateId}";
        }
        catch (NVIDIAApiException ex) when ((int)ex.Status == -105 || (int)ex.Status == -220)
        {
            using (await _stateLock.LockAsync(token).ConfigureAwait(false))
            {
                _state = GPUState.PoweredOff;
                _performanceState = Resource.GPUController_PoweredOff;
                _processes = [];
                _allProcesses = [];
                _gpuInstanceId = null;
            }
            return;
        }
        catch (Exception ex)
        {
            Log.Instance.Trace($"GPU status exception.", ex);
            newPerformanceState = null;
        }

        newGpuInstanceId = cachedGpuInstanceId;
        if (string.IsNullOrEmpty(newGpuInstanceId))
        {
            var pnpDeviceIdPart = NVAPI.GetGPUId(gpu);
            if (!string.IsNullOrEmpty(pnpDeviceIdPart))
                newGpuInstanceId = await WMI.Win32.PnpEntity.GetDeviceIDAsync(pnpDeviceIdPart).ConfigureAwait(false);
        }

        List<Process> processNames;
        try
        {
            var (allProcessNames, filteredProcessNames) = NVAPIExtensions.GetActiveProcesses(gpu);
            newAllProcesses = allProcessNames;
            processNames = filteredProcessNames;
        }
        catch (NVIDIAApiException ex)
        {
            Log.Instance.Trace($"NVAPI exception in GetActiveProcesses.", ex);
            NVAPI.IsInitialized = false;
            using (await _stateLock.LockAsync(token).ConfigureAwait(false))
            {
                _state = GPUState.Unknown;
                _performanceState = null;
                _processes = [];
                _allProcesses = [];
                _gpuInstanceId = null;
            }
            return;
        }

        var feature = IoCContainer.Resolve<HybridModeFeature>();

        if (await feature.GetStateAsync().ConfigureAwait(false) == HybridModeState.Off)
        {
            if (NVAPI.IsDisplayConnected(gpu))
            {
                newProcesses = processNames;
                newState = GPUState.MonitorConnected;
            }
            else
            {
                newProcesses = [];
                newState = GPUState.Unknown;
            }
        }
        else if (processNames.Count != 0)
        {
            newProcesses = processNames;
            newState = GPUState.Active;
        }
        else
        {
            newProcesses = [];
            newState = GPUState.Inactive;
        }

        using (await _stateLock.LockAsync(token).ConfigureAwait(false))
        {
            _state = newState;
            _performanceState = newPerformanceState;
            _processes = newProcesses;
            _allProcesses = newAllProcesses;
            _gpuInstanceId = newGpuInstanceId;
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
