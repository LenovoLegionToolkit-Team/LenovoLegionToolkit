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
            var checkIncluded = _settings.Store.IncludedProcesses.Count > 0;
            var checkGameConfigStore = _settings.Store.GameDetection.UseGameConfigStore;

            if (checkGameConfigStore)
            {
                foreach (var gamePath in GameConfigStoreDetector.GetDetectedGamePaths())
                    _detectedGamePathsCache.Add(gamePath);
            }

            if (_preserveStateOnNextStart)
            {
                _preserveStateOnNextStart = false;
                Log.Instance.Trace($"Validating preserved process cache against current rules ({_processCache.Count} process(es))...");

                var disqualified = new List<Process>();
                foreach (var process in _processCache)
                {
                    try
                    {
                        if (process.HasExited)
                        {
                            disqualified.Add(process);
                            continue;
                        }

                        var processName = process.ProcessName;
                        if (IsBlacklisted(processName))
                        {
                            Log.Instance.Trace($"Preserved process is now blacklisted: {processName} [pid={process.Id}].");
                            disqualified.Add(process);
                        }
                    }
                    catch (Exception ex)
                    {
                        Log.Instance.Trace($"Failed to validate preserved process {process.Id}.", ex);
                        disqualified.Add(process);
                    }
                }

                foreach (var process in disqualified)
                {
                    _processCache.Remove(process);
                    _gameModePinnedProcesses.Remove(process);
                    Detach(process);
                    DisposeProcess(process);
                }

                if (disqualified.Count > 0)
                {
                    Log.Instance.Trace($"Evicted {disqualified.Count} process(es) during re-validation. Remaining: {_processCache.Count}.");
                }

                var hasActiveGames = _processCache.Count > 0;
                _lastState = hasActiveGames;
                if (!hasActiveGames)
                {
                    Log.Instance.Trace($"All active games disqualified during restart.");
                    RaiseChanged(new ChangedEventArgs(false));
                }
            }

            if (checkIncluded || checkGameConfigStore)
            {
                var included = checkIncluded
                    ? _settings.Store.IncludedProcesses.ToHashSet(StringComparer.OrdinalIgnoreCase)
                    : null;

                foreach (var process in Process.GetProcesses())
                {
                    try
                    {
                        if (process.Id <= 4 || process.HasExited)
                        {
                            DisposeProcess(process);
                            continue;
                        }

                        if (_processCache.Contains(process))
                        {
                            DisposeProcess(process);
                            continue;
                        }

                        var processName = process.ProcessName;
                        var isIncludedGame = included?.Contains(processName) ?? false;

                        string? processPath = null;
                        var isConfigStoreGame = false;
                        if (!isIncludedGame && checkGameConfigStore && !IsBlacklisted(processName))
                        {
                            processPath = process.GetFileName();
                            if (!string.IsNullOrEmpty(processPath))
                            {
                                var processInfo = ProcessInfo.FromPath(processPath);
                                isConfigStoreGame = _detectedGamePathsCache.Contains(processInfo);
                            }
                        }

                        if (isIncludedGame || isConfigStoreGame)
                        {
                            processPath ??= process.GetFileName();
                            var source = isIncludedGame ? "Included" : "Known Game List";
                            Log.Instance.Trace($"Found already running game: {processName} [Source: {source}] [pid={process.Id}, path={processPath ?? "Unknown"}]");
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
                                var processPath = process.GetFileName();
                                Log.Instance.Trace(
                                    $"Found already running game: {processName} [Source: Discrete GPU] [pid={process.Id}, path={processPath ?? "Unknown"}]");
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
            await _gameConfigStoreDetector.StartAsync(_detectedGamePathsCache).ConfigureAwait(false);

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
                Log.Instance.Trace($"Preserving process cache during restart: {_processCache.Count} process(es).");
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
                        var processPath = process.GetFileName();
                        Log.Instance.Trace($"Game detected: {processName} [Source: Discrete GPU] [pid={process.Id}, path={processPath ?? "Unknown"}]");
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
                        if (IsBlacklisted(process.ProcessName))
                        {
                            DisposeProcess(process);
                            continue;
                        }

                        var processPath = process.GetFileName();

                        if (processPath is not null && game.ExecutablePath is not null &&
                            !game.ExecutablePath.Equals(processPath, StringComparison.OrdinalIgnoreCase))
                        {
                            DisposeProcess(process);
                            continue;
                        }

                        if (!_processCache.Contains(process))
                        {
                            Log.Instance.Trace($"Game detected: {process.ProcessName} [Source: Known Game List] [pid={process.Id}, path={processPath ?? game.ExecutablePath ?? "Unknown"}]");
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
                            if (process.HasExited)
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
                        Log.Instance.Trace($"Game Mode ended and process exited: {process.ProcessName} [Source: Windows Game Mode] [pid={process.Id}].");
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
                Log.Instance.Trace($"Game Mode deactivation ignored: process cache is not empty ({_processCache.Count} active game(s)).");
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
                var processPath = process.GetFileName();
                if (IsBlacklisted(processName))
                {
                    Log.Instance.Trace($"Ignoring blacklisted process: {processName} [Source: Windows Game Mode] [pid={process.Id}, path={processPath ?? "Unknown"}].");
                    DisposeProcess(process);
                    return;
                }

                Log.Instance.Trace($"Game detected: {processName} [Source: Windows Game Mode] [pid={process.Id}, path={processPath ?? "Unknown"}].");
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
        if (_settings.Store.IncludedProcesses.Contains(processName, StringComparer.OrdinalIgnoreCase))
            return false;

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

            var isIncluded = _settings.Store.IncludedProcesses.Contains(e.ProcessName, StringComparer.OrdinalIgnoreCase);
            var isGameConfigStore = _settings.Store.GameDetection.UseGameConfigStore &&
                                    _detectedGamePathsCache.Any(p =>
                                        e.ProcessName.Equals(p.Name, StringComparison.OrdinalIgnoreCase));

            if (!isIncluded && !isGameConfigStore)
                return;

            Process? startedProcess = null;
            try
            {
                startedProcess = Process.GetProcessById(e.ProcessId);
                if (_processCache.Contains(startedProcess))
                {
                    DisposeProcess(startedProcess);
                    return;
                }

                var processPath = startedProcess.GetFileName();
                var source = isIncluded ? "Included" : "Known Game List";

                if (!isIncluded && string.IsNullOrEmpty(processPath))
                {
                    Log.Instance.Trace($"Can't get path for {e.ProcessName} [Source: {source}] [pid={e.ProcessId}].");
                    DisposeProcess(startedProcess);
                    return;
                }

                var processInfo = string.IsNullOrEmpty(processPath)
                    ? new ProcessInfo(e.ProcessName, null)
                    : ProcessInfo.FromPath(processPath);

                if (!isIncluded && !_detectedGamePathsCache.Contains(processInfo))
                {
                    DisposeProcess(startedProcess);
                    return;
                }

                if (IsBlacklisted(e.ProcessName))
                {
                    Log.Instance.Trace($"Ignoring blacklisted process: {e.ProcessName} [Source: {source}] [pid={e.ProcessId}, path={processPath ?? "Unknown"}].");
                    DisposeProcess(startedProcess);
                    return;
                }

                Log.Instance.Trace(
                    $"Game detected: {e.ProcessName} [Source: {source}] [pid={e.ProcessId}, path={processPath ?? "Unknown"}].");

                Attach(startedProcess);
                _processCache.Add(startedProcess);

                RaiseChangedIfNeeded(true);
            }
            catch (Exception ex)
            {
                Log.Instance.Trace($"Failed to attach to {e.ProcessName} [pid={e.ProcessId}].", ex);
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
            Log.Instance.Trace($"Game running state changed: Running={newState} [activeGamesCount={_processCache.Count}].");

            RaiseChanged(new ChangedEventArgs(newState));
        }
    }

    private void Attach(Process process)
    {
        string processName;
        try { processName = process.ProcessName; } catch { processName = "Unknown"; }
        Log.Instance.Trace($"Attaching to process: {processName} [pid={process.Id}]...");

        try
        {
            process.EnableRaisingEvents = true;
            process.Exited += Process_Exited;
        }
        catch (Exception ex)
        {
            Log.Instance.Trace($"Failed to enable events for process: {processName} [pid={process.Id}].", ex);
        }
    }

    private void Detach(Process process)
    {
        string processName;
        try { processName = process.ProcessName; } catch { processName = "Unknown"; }

        try
        {
            process.EnableRaisingEvents = false;
            process.Exited -= Process_Exited;
        }
        catch { /* Ignore */ }

        Log.Instance.Trace($"Detached from process: {processName} [pid={process.Id}].");
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
                string procName;
                try { procName = exitedProc.ProcessName; } catch { procName = "Unknown"; }
                Log.Instance.Trace($"Process exited: {procName} [pid={exitedProc.Id}].");

                if (!_processCache.Contains(exitedProc))
                {
                    _gameModePinnedProcesses.Remove(exitedProc);
                    Detach(exitedProc);
                    DisposeProcess(exitedProc);
                }
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
                Log.Instance.Trace($"Removed {deadProcesses.Count} exited process(es) from cache. Remaining: {_processCache.Count}.");
            }

            if (_processCache.Count != 0)
            {
                Log.Instance.Trace($"Active games remaining in cache: {_processCache.Count}.");

                return;
            }

            Log.Instance.Trace($"No more games running. All active processes cleared.");

            RaiseChangedIfNeeded(false);
        }
    }
}
