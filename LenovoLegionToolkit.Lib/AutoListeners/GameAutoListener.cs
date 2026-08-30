using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using LenovoLegionToolkit.Lib.Controllers;
using LenovoLegionToolkit.Lib.Extensions;
using LenovoLegionToolkit.Lib.GameDetection;
using LenovoLegionToolkit.Lib.Settings;
using LenovoLegionToolkit.Lib.Utils;

namespace LenovoLegionToolkit.Lib.AutoListeners;

public class GameAutoListener : AbstractAutoListener<GameAutoListener.ChangedEventArgs>
{
    public class ChangedEventArgs(bool running) : EventArgs
    {
        public bool Running { get; } = running;
    }

    private class ProcessEqualityComparer : IEqualityComparer<Process>
    {
        public bool Equals(Process? x, Process? y)
        {
            if (ReferenceEquals(x, y)) return true;
            if (x is null) return false;
            if (y is null) return false;
            if (x.GetType() != y.GetType()) return false;
            return x.Id == y.Id;
        }

        public int GetHashCode(Process obj) => obj.Id;
    }

    private static readonly Lock Lock = new();

    private readonly InstanceStartedEventAutoAutoListener _instanceStartedEventAutoAutoListener;

    private readonly GameConfigStoreDetector _gameConfigStoreDetector;
    private readonly EffectiveGameModeDetector _effectiveGameModeDetector;
    private readonly GPUController _gpuController;
    private readonly ApplicationSettings _settings;

    private readonly HashSet<ProcessInfo> _detectedGamePathsCache = [];
    private readonly HashSet<Process> _processCache = new(new ProcessEqualityComparer());
    private readonly HashSet<Process> _gameModePinnedProcesses = new(new ProcessEqualityComparer());

    private bool _lastState;
    private bool _preserveStateOnNextStart;

    public GameAutoListener(InstanceStartedEventAutoAutoListener instanceStartedEventAutoAutoListener,
        GPUController gpuController, ApplicationSettings settings)
    {
        _instanceStartedEventAutoAutoListener = instanceStartedEventAutoAutoListener;
        _gpuController = gpuController;
        _settings = settings;

        _gameConfigStoreDetector = new GameConfigStoreDetector();
        _gameConfigStoreDetector.GamesDetected += GameConfigStoreDetectorGamesConfigStoreDetected;

        _effectiveGameModeDetector = new EffectiveGameModeDetector();
        _effectiveGameModeDetector.Changed += EffectiveGameModeDetectorChanged;
    }

    protected override async Task StartAsync()
    {
        lock (Lock)
        {
            if (_preserveStateOnNextStart)
            {
                _lastState = true;
                _preserveStateOnNextStart = false;
                Log.Instance.Trace($"Preserving game running state during restart.");
            }
        }

        lock (Lock)
        {
            if (_settings.Store.GameDetection.UseGameConfigStore)
            {
                foreach (var gamePath in GameConfigStoreDetector.GetDetectedGamePaths())
                    _detectedGamePathsCache.Add(gamePath);

                foreach (var process in Process.GetProcesses())
                {
                    try
                    {
                        if (process.HasExited)
                        {
                            DisposeProcess(process);
                            continue;
                        }

                        var processPath = process.GetFileName();
                        if (string.IsNullOrEmpty(processPath))
                        {
                            DisposeProcess(process);
                            continue;
                        }

                        var processInfo = ProcessInfo.FromPath(processPath);
                        if (_detectedGamePathsCache.Contains(processInfo))
                        {
                            Log.Instance.Trace($"Found already running game: {processInfo.Name} ({process.Id})");
                            Attach(process);
                            _processCache.Add(process);
                            RaiseChangedIfNeeded(true);
                        }
                        else
                        {
                            DisposeProcess(process);
                        }
                    }
                    catch (Exception ex)
                    {
                        Log.Instance.Trace($"Failed to check process {process.Id}.", ex);
                        DisposeProcess(process);
                    }
                }
            }
        }

        if (_settings.Store.GameDetection.UseDiscreteGPU)
        {
            await _gpuController.StartAsync().ConfigureAwait(false);
            _gpuController.Refreshed += GpuController_Refreshed;

            try
            {
                var status = await _gpuController.RefreshNowAsync().ConfigureAwait(false);
                lock (Lock)
                {
                    foreach (var process in status.Processes)
                    {
                        try
                        {
                            if (process.HasExited)
                                continue;

                            var processName = process.ProcessName;

                            if (IsBlacklisted(processName))
                                continue;

                            if (!_processCache.Contains(process))
                            {
                                Log.Instance.Trace(
                                    $"Found already running GPU-accelerated process: {processName} ({process.Id})");
                                Attach(process);
                                _processCache.Add(process);
                                RaiseChangedIfNeeded(true);
                            }
                        }
                        catch (Exception ex)
                        {
                            Log.Instance.Trace($"Failed to check GPU process {process.Id}.", ex);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Instance.Trace($"Failed to check for already running GPU processes.", ex);
            }
        }

        if (_settings.Store.GameDetection.UseGameConfigStore)
            await _gameConfigStoreDetector.StartAsync().ConfigureAwait(false);

        if (_settings.Store.GameDetection.UseEffectiveGameMode)
        {
            await _effectiveGameModeDetector.StartAsync().ConfigureAwait(false);

            lock (Lock)
            {
                if (_effectiveGameModeDetector.IsActive)
                {
                    Log.Instance.Trace($"Game Mode already active on startup, checking foreground process...");
                    TryPinForegroundProcess();
                }
            }
        }

        await _instanceStartedEventAutoAutoListener.SubscribeChangedAsync(InstanceStartedEventAutoAutoListener_Changed)
            .ConfigureAwait(false);
    }

    protected override async Task StopAsync()
    {
        await _instanceStartedEventAutoAutoListener
            .UnsubscribeChangedAsync(InstanceStartedEventAutoAutoListener_Changed).ConfigureAwait(false);

        await _gameConfigStoreDetector.StopAsync().ConfigureAwait(false);
        await _effectiveGameModeDetector.StopAsync().ConfigureAwait(false);

        _gpuController.Refreshed -= GpuController_Refreshed;

        lock (Lock)
        {
            if (!_preserveStateOnNextStart)
            {
                foreach (var process in _processCache)
                {
                    Detach(process);
                    DisposeProcess(process);
                }

                _processCache.Clear();
                _detectedGamePathsCache.Clear();
                _gameModePinnedProcesses.Clear();
                if (_lastState)
                {
                    _lastState = false;
                    RaiseChanged(new ChangedEventArgs(false));
                }
            }
            else
            {
                _detectedGamePathsCache.Clear();
                Log.Instance.Trace($"Preserving process cache during mode switch: {_processCache.Count} process(es)");
            }
        }
    }

    public bool AreGamesRunning()
    {
        lock (Lock)
        {
            return _lastState;
        }
    }

    public void PreserveStateOnRestart()
    {
        lock (Lock)
        {
            _preserveStateOnNextStart = _lastState;
            Log.Instance.Trace($"Will preserve state on next start: {_preserveStateOnNextStart}");
        }
    }

    private void GpuController_Refreshed(object? sender, GPUStatus e)
    {
        lock (Lock)
        {
            foreach (var process in e.Processes)
            {
                try
                {
                    if (process.HasExited)
                        continue;

                    var processName = process.ProcessName;

                    if (IsBlacklisted(processName))
                        continue;

                    if (!_processCache.Contains(process))
                    {
                        Attach(process);
                        _processCache.Add(process);
                        RaiseChangedIfNeeded(true);
                    }
                }
                catch { /* Ignore */ }
            }
        }
    }

    private void GameConfigStoreDetectorGamesConfigStoreDetected(object? sender,
        GameConfigStoreDetector.GameDetectedEventArgs e)
    {
        lock (Lock)
        {
            _detectedGamePathsCache.Clear();

            foreach (var game in e.Games)
            {
                _detectedGamePathsCache.Add(game);

                foreach (var process in Process.GetProcessesByName(game.Name))
                {
                    try
                    {
                        var processPath = process.GetFileName();

                        if (processPath is not null && game.ExecutablePath is not null &&
                            !game.ExecutablePath.Equals(processPath, StringComparison.OrdinalIgnoreCase))
                        {
                            DisposeProcess(process);
                            continue;
                        }

                        if (!_processCache.Contains(process))
                        {
                            Attach(process);
                            _processCache.Add(process);
                        }
                        else
                        {
                            DisposeProcess(process);
                        }

                        RaiseChangedIfNeeded(true);
                    }
                    catch (Exception)
                    {
                        Log.Instance.Trace($"Can't get game \"{game}\" details.");
                        DisposeProcess(process);
                    }
                }
            }
        }
    }

    private void EffectiveGameModeDetectorChanged(object? sender, bool e)
    {
        lock (Lock)
        {
            if (e)
            {
                TryPinForegroundProcess();
            }
            else
            {
                if (_gameModePinnedProcesses.Count > 0)
                {
                    var toRelease = new List<Process>();
                    foreach (var process in _gameModePinnedProcesses)
                    {
                        try
                        {
                            var processPath = process.GetFileName();
                            var isKnownGame = !string.IsNullOrEmpty(processPath) && _detectedGamePathsCache.Contains(ProcessInfo.FromPath(processPath));
                            var isGpuActive = _gpuController.ActiveProcesses.Any(p => p.Id == process.Id);

                            if (!isKnownGame && !isGpuActive)
                            {
                                toRelease.Add(process);
                            }
                        }
                        catch
                        {
                            toRelease.Add(process);
                        }
                    }

                    foreach (var process in toRelease)
                    {
                        Log.Instance.Trace($"Game Mode ended. Releasing non-game foreground process {process.Id} ({process.ProcessName}).");
                        _gameModePinnedProcesses.Remove(process);
                        _processCache.Remove(process);
                        Detach(process);
                        DisposeProcess(process);
                    }

                    if (_processCache.Count == 0)
                    {
                        RaiseChangedIfNeeded(false);
                    }
                }
            }

            if (_processCache.Count != 0)
            {
                Log.Instance.Trace($"Ignoring, process cache is not empty.");
                return;
            }
        }
    }

    private unsafe void TryPinForegroundProcess()
    {
        try
        {
            var hWnd = Windows.Win32.PInvoke.GetForegroundWindow();
            if (hWnd == Windows.Win32.Foundation.HWND.Null)
            {
                return;
            }

            uint processId = 0;
            Windows.Win32.PInvoke.GetWindowThreadProcessId(hWnd, &processId);

            if (processId == 0)
            {
                return;
            }

            var process = Process.GetProcessById((int)processId);
            try
            {
                if (process.HasExited)
                {
                    DisposeProcess(process);
                    return;
                }

                if (_processCache.Contains(process))
                {
                    DisposeProcess(process);
                    return;
                }

                var processName = process.ProcessName;
                if (IsBlacklisted(processName))
                {
                    Log.Instance.Trace($"Ignoring blacklisted process {processName} ({process.Id}).");
                    DisposeProcess(process);
                    return;
                }

                Log.Instance.Trace($"Game Mode detected. Pinning process {process.Id} ({processName}).");
                Attach(process);
                _processCache.Add(process);
                _gameModePinnedProcesses.Add(process);
                RaiseChangedIfNeeded(true);
            }
            catch
            {
                DisposeProcess(process);
                throw;
            }
        }
        catch (Exception ex)
        {
            Log.Instance.Trace($"Failed to pin process on Game Mode detection.", ex);
        }
    }

    private bool IsBlacklisted(string processName)
    {
        if (_settings.Store.ExcludedProcesses.Contains(processName, StringComparer.OrdinalIgnoreCase))
            return true;

        return processName.Equals("explorer", StringComparison.OrdinalIgnoreCase)
               || processName.Equals("Lenovo Legion Toolkit", StringComparison.OrdinalIgnoreCase)
               || processName.Equals("SearchUI", StringComparison.OrdinalIgnoreCase)
               || processName.Equals("LockApp", StringComparison.OrdinalIgnoreCase)
               || processName.Equals("TextInputHost", StringComparison.OrdinalIgnoreCase)
               || processName.Equals("ShellExperienceHost", StringComparison.OrdinalIgnoreCase)
               || processName.Equals("StartMenuExperienceHost", StringComparison.OrdinalIgnoreCase)
               || processName.Equals("SearchHost", StringComparison.OrdinalIgnoreCase)
               || processName.Equals("dwm", StringComparison.OrdinalIgnoreCase)
               || processName.Equals("csrss", StringComparison.OrdinalIgnoreCase)
               || processName.Equals("WmiApSrv", StringComparison.OrdinalIgnoreCase)
               || processName.Equals("HWiNFO64", StringComparison.OrdinalIgnoreCase)
               || processName.Equals("HWiNFO32", StringComparison.OrdinalIgnoreCase)
               || processName.Equals("nvidia-smi", StringComparison.OrdinalIgnoreCase)
               || processName.Equals("steamwebhelper", StringComparison.OrdinalIgnoreCase)
               || processName.Equals("EpicWebHelper", StringComparison.OrdinalIgnoreCase)
               || processName.Equals("GalaxyCommunicationService", StringComparison.OrdinalIgnoreCase)
               || processName.Equals("GalaxyClientHelper", StringComparison.OrdinalIgnoreCase)
               || processName.Equals("EABackgroundService", StringComparison.OrdinalIgnoreCase)
               || processName.Equals("Link2EA", StringComparison.OrdinalIgnoreCase)
               || processName.Equals("RiotClientCrashHandler", StringComparison.OrdinalIgnoreCase)
               || processName.Equals("CrashReportClient", StringComparison.OrdinalIgnoreCase)
               || processName.Equals("UnityCrashHandler64", StringComparison.OrdinalIgnoreCase)
               || processName.Equals("UnityCrashHandler32", StringComparison.OrdinalIgnoreCase)
               || processName.Equals("UE4-CrashTracker", StringComparison.OrdinalIgnoreCase)
               || processName.Equals("crs-handler", StringComparison.OrdinalIgnoreCase)
               || processName.Equals("cefsharp.browsersubprocess", StringComparison.OrdinalIgnoreCase)
               || processName.Equals("conhost", StringComparison.OrdinalIgnoreCase)
               || processName.Equals("nvngx_update", StringComparison.OrdinalIgnoreCase)
               || processName.Equals("GameGuard", StringComparison.OrdinalIgnoreCase)
               || processName.Equals("GameGuard.des", StringComparison.OrdinalIgnoreCase)
               || processName.Equals("GameMon", StringComparison.OrdinalIgnoreCase)
               || processName.Equals("GameMon64", StringComparison.OrdinalIgnoreCase)
               || processName.Equals("EasyAntiCheat", StringComparison.OrdinalIgnoreCase)
               || processName.Equals("EasyAntiCheat_EOS", StringComparison.OrdinalIgnoreCase)
               || processName.Equals("BEService", StringComparison.OrdinalIgnoreCase)
               || processName.Equals("BEService_x64", StringComparison.OrdinalIgnoreCase)
               || processName.Equals("vgc", StringComparison.OrdinalIgnoreCase)
               || processName.Equals("vgtray", StringComparison.OrdinalIgnoreCase)
               || processName.Equals("EAAntiCheat.GameService", StringComparison.OrdinalIgnoreCase)
               || processName.Equals("EAAntiCheat.GameServiceLauncher", StringComparison.OrdinalIgnoreCase)
               || processName.Equals("ACE-Base", StringComparison.OrdinalIgnoreCase)
               || processName.Equals("UbisoftConnect", StringComparison.OrdinalIgnoreCase)
               || processName.Equals("UbisoftGameLauncher", StringComparison.OrdinalIgnoreCase)
               || processName.Equals("Battle.net Helper", StringComparison.OrdinalIgnoreCase)
               || processName.Equals("Agent", StringComparison.OrdinalIgnoreCase);
    }

    private void InstanceStartedEventAutoAutoListener_Changed(object? sender,
        InstanceStartedEventAutoAutoListener.ChangedEventArgs e)
    {
        lock (Lock)
        {
            if (e.ProcessId < 0)
                return;

            if (!_settings.Store.GameDetection.UseGameConfigStore)
                return;

            if (!_detectedGamePathsCache.Any(p =>
                    e.ProcessName.Equals(p.Name, StringComparison.OrdinalIgnoreCase)))
                return;

            Process? startedProcess = null;
            try
            {
                startedProcess = Process.GetProcessById(e.ProcessId);
                var processPath = startedProcess.GetFileName();

                if (string.IsNullOrEmpty(processPath))
                {
                    Log.Instance.Trace($"Can't get path for {e.ProcessName}. [processId={e.ProcessId}]");
                    DisposeProcess(startedProcess);
                    return;
                }

                var processInfo = ProcessInfo.FromPath(processPath);
                if (!_detectedGamePathsCache.Contains(processInfo))
                {
                    DisposeProcess(startedProcess);
                    return;
                }

                Log.Instance.Trace(
                    $"Game {processInfo} is running. [processId={e.ProcessId}, processPath={processPath}]");

                Attach(startedProcess);
                _processCache.Add(startedProcess);

                RaiseChangedIfNeeded(true);
            }
            catch (Exception ex)
            {
                Log.Instance.Trace($"Failed to attach to {e.ProcessName}. [processId={e.ProcessId}]", ex);
                DisposeProcess(startedProcess);
            }
        }
    }

    private void RaiseChangedIfNeeded(bool newState)
    {
        lock (Lock)
        {
            if (newState == _lastState)
                return;

            _lastState = newState;

            RaiseChanged(new ChangedEventArgs(newState));
        }
    }

    private void Attach(Process process)
    {
        Log.Instance.Trace($"Attaching to process {process.Id}...");

        try
        {
            process.EnableRaisingEvents = true;
            process.Exited += Process_Exited;
        }
        catch (Exception ex)
        {
            Log.Instance.Trace($"Failed to enable events for process {process.Id}.", ex);
        }
    }

    private void Detach(Process process)
    {
        try
        {
            process.EnableRaisingEvents = false;
            process.Exited -= Process_Exited;
        }
        catch { /* Ignore */ }

        Log.Instance.Trace($"Detached from process {process.Id}.");
    }

    private static void DisposeProcess(Process? process)
    {
        if (process is null)
            return;

        try
        {
            process.Dispose();
        }
        catch { /* Ignore */ }
    }

    private void Process_Exited(object? o, EventArgs args)
    {
        lock (Lock)
        {
            if (o is Process exitedProc)
            {
                Log.Instance.Trace($"Process {exitedProc.Id} exited.");
                Detach(exitedProc);
            }

            var deadProcesses = new List<Process>();
            foreach (var p in _processCache)
            {
                try
                {
                    if (p.HasExited)
                        deadProcesses.Add(p);
                }
                catch
                {
                    deadProcesses.Add(p);
                }
            }

            foreach (var p in deadProcesses)
            {
                _processCache.Remove(p);
                _gameModePinnedProcesses.Remove(p);
                Detach(p);
                DisposeProcess(p);
            }

            if (deadProcesses.Count > 0)
            {
                Log.Instance.Trace($"Removed {deadProcesses.Count} exited processes.");
            }

            if (_processCache.Count != 0)
            {
                Log.Instance.Trace($"More games are running...");

                return;
            }

            Log.Instance.Trace($"No more games are running.");

            RaiseChangedIfNeeded(false);
        }
    }
}
