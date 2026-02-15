using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using LenovoLegionToolkit.Lib.Extensions;
using LenovoLegionToolkit.Lib.Resources;
using LenovoLegionToolkit.Lib.System;
using LenovoLegionToolkit.Lib.System.Management;
using LenovoLegionToolkit.Lib.Utils;
using NeoSmart.AsyncLock;
using NvAPIWrapper.GPU;

namespace LenovoLegionToolkit.Lib.Controllers;

public class GPUController
{
    private readonly AsyncLock _lock = new();
    private readonly GPUWatcherService? _watcherService;

    private Task? _refreshTask;
    private CancellationTokenSource? _refreshCancellationTokenSource;

    private GPUState _state = GPUState.Unknown;
    private List<Process> _processes = [];
    private string? _gpuInstanceId;
    private string? _performanceState;

    public event EventHandler<GPUStatus>? Refreshed;
    public bool IsStarted { get => _refreshTask != null; }
    
    /// <summary>
    /// Access to GPU process watcher service for notifications and history
    /// </summary>
    public GPUWatcherService? WatcherService => _watcherService;
    
    public GPUController() { }
    
    public GPUController(GPUWatcherService watcherService)
    {
        _watcherService = watcherService;
    }

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
        using (await _lock.LockAsync().ConfigureAwait(false))
            return _state;
    }

    public async Task<GPUStatus> RefreshNowAsync()
    {
        using (await _lock.LockAsync().ConfigureAwait(false))
        {
            await RefreshLoopAsync(0, 0, CancellationToken.None).ConfigureAwait(false);
            return new GPUStatus(_state, _performanceState, _processes);
        }
    }

    public Task StartAsync(int delay = 1_000, int interval = 5_000)
    {
        if (IsStarted)
            return Task.CompletedTask;
        
        // Comment for quiet debugging.
        // Log.Instance.Trace($"Starting... [delay={delay}, interval={interval}]");

        _refreshCancellationTokenSource = new CancellationTokenSource();
        var token = _refreshCancellationTokenSource.Token;
        _refreshTask = Task.Run(() => RefreshLoopAsync(delay, interval, token), token);
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

    private async Task RefreshLoopAsync(int delay, int interval, CancellationToken token)
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
                    
                    // Notify watcher service of process changes for notifications/history
                    _watcherService?.UpdateProcessList(_processes);
                    
                    Refreshed?.Invoke(this, new GPUStatus(_state, _performanceState, _processes));
                }

                if (interval > 0)
                    await Task.Delay(interval, token).ConfigureAwait(false);
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
        _gpuInstanceId = null;
        _performanceState = null;

        var gpu = NVAPI.GetGPU();
        if (gpu is null)
        {
            _state = GPUState.NvidiaGpuNotFound;

            Log.Instance.Trace($"GPU present [state={_state}, processes.Count={_processes.Count}, gpuInstanceId={_gpuInstanceId}]");

            return;
        }

        try
        {
            var stateId = gpu.PerformanceStatesInfo.CurrentPerformanceState.StateId.ToString().GetUntilOrEmpty("_");
            _performanceState = Resource.GPUController_PoweredOn;
            if (!string.IsNullOrWhiteSpace(stateId))
                _performanceState += $", {stateId}";
        }
        catch (Exception ex) when (ex.Message == "NVAPI_GPU_NOT_POWERED")
        {
            _state = GPUState.PoweredOff;
            _performanceState = Resource.GPUController_PoweredOff;

            Log.Instance.Trace($"Powered off [state={_state}, processes.Count={_processes.Count}, gpuInstanceId={_gpuInstanceId}]");

            return;
        }
        catch (Exception ex)
        {
            Log.Instance.Trace($"GPU status exception.", ex);

            _performanceState = "Unknown";
        }

        var pnpDeviceIdPart = NVAPI.GetGPUId(gpu);

        if (string.IsNullOrEmpty(pnpDeviceIdPart))
            throw new InvalidOperationException("pnpDeviceIdPart is null or empty");

        var gpuInstanceId = await WMI.Win32.PnpEntity.GetDeviceIDAsync(pnpDeviceIdPart).ConfigureAwait(false);
        var processNames = NVAPIExtensions.GetActiveProcesses(gpu);

        if (NVAPI.IsDisplayConnected(gpu))
        {
            _processes = processNames;
            _state = GPUState.MonitorConnected;

            // Comment due to annoying.
            //if (Log.Instance.IsTraceEnabled)
            //    Log.Instance.Trace($"Monitor connected [state={_state}, processes.Count={_processes.Count}, gpuInstanceId={_gpuInstanceId}]");
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
}
