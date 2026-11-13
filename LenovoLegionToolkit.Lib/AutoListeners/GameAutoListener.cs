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

    private static readonly Lock Lock = new();

    private readonly InstanceStartedEventAutoAutoListener _instanceStartedEventAutoAutoListener;
    private readonly GameConfigStoreDetector _gameConfigStoreDetector;
    private readonly EffectiveGameModeDetector _effectiveGameModeDetector;
    private readonly GPUController _gpuController;
    private readonly ApplicationSettings _settings;

    private readonly HashSet<ProcessInfo> _detectedGamePathsCache = [];
    private readonly Dictionary<int, Process> _processCache = [];
    private readonly Dictionary<int, Process> _gameModePinnedProcesses = [];
    private bool _lastState;
    private bool _preserveStateOnNextStart;
    private bool _isListening;

    public GameAutoListener(InstanceStartedEventAutoAutoListener instanceStartedEventAutoAutoListener, GPUController gpuController, ApplicationSettings settings)
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
        var toCleanup = new HashSet<Process>(ReferenceEqualityComparer.Instance);
        var toDispose = new HashSet<Process>(ReferenceEqualityComparer.Instance);

        try
        {
            lock (Lock)
            {
                _isListening = true;
                var checkIncluded = _settings.Store.IncludedProcesses.Count > 0;
                var checkGameConfigStore = _settings.Store.GameDetection.UseGameConfigStore;

                if (checkGameConfigStore)
                {
                    foreach (var gamePath in GameConfigStoreDetector.GetDetectedGamePaths())
                    {
                        _detectedGamePathsCache.Add(gamePath);
                    }
                }

                if (_preserveStateOnNextStart)
                {
                    _preserveStateOnNextStart = false;
                    Log.Instance.Trace($"Validating preserved process cache against current rules ({_processCache.Count} process(es))...");
                    var disqualified = new List<int>();

                    foreach (var pair in _processCache)
                    {
                        try
                        {
                            if (pair.Value.HasExited)
                            {
                                disqualified.Add(pair.Key);
                                continue;
                            }

                            var processName = pair.Value.ProcessName;
                            if (IsBlacklisted(processName))
                            {
                                Log.Instance.Trace($"Preserved process is now blacklisted: {processName} [pid={pair.Key}].");
                                disqualified.Add(pair.Key);
                            }
                        }
                        catch (Exception ex)
                        {
                            Log.Instance.Trace($"Failed to validate preserved process {pair.Key}.", ex);
                            disqualified.Add(pair.Key);
                        }
                    }

                    foreach (var id in disqualified)
                    {
                        RemoveTrackedProcess(id, toCleanup);
                    }

                    if (disqualified.Count > 0)
                    {
                        Log.Instance.Trace($"Evicted {disqualified.Count} process(es) during re-validation. Remaining: {_processCache.Count}.");
                    }

                    RaiseChangedIfNeeded();
                }

                if (checkIncluded || checkGameConfigStore)
                {
                    var included = checkIncluded
                        ? _settings.Store.IncludedProcesses.ToHashSet(StringComparer.OrdinalIgnoreCase)
                        : null;

                    foreach (var process in Process.GetProcesses())
                    {
                        var retained = false;

                        try
                        {
                            var id = process.Id;
                            if (id <= 4 || process.HasExited || _processCache.ContainsKey(id))
                            {
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
                                    isConfigStoreGame = _detectedGamePathsCache.Contains(ProcessInfo.FromPath(processPath));
                                }
                            }

                            if (isIncludedGame || isConfigStoreGame)
                            {
                                processPath ??= process.GetFileName();
                                var source = isIncludedGame ? "Included" : "Known Game List";
                                Log.Instance.Trace($"Found already running game: {processName} [Source: {source}] [pid={id}, path={processPath ?? "Unknown"}]");
                                retained = TryTrackProcess(process, id, toCleanup);
                            }
                        }
                        catch (Exception ex)
                        {
                            Log.Instance.Trace($"Failed to check process {TryGetId(process)?.ToString() ?? "Unknown"}.", ex);
                        }
                        finally
                        {
                            if (!retained && !toCleanup.Contains(process))
                            {
                                toDispose.Add(process);
                            }
                        }
                    }
                }
            }
        }
        finally
        {
            CleanupProcesses(toCleanup);
            DisposeProcesses(toDispose);
        }

        if (_settings.Store.GameDetection.UseDiscreteGPU)
        {
            await _gpuController.StartAsync().ConfigureAwait(false);
            _gpuController.Refreshed += GpuController_Refreshed;

            try
            {
                var status = await _gpuController.RefreshNowAsync().ConfigureAwait(false);
                HandleGpuProcesses(status, true);
            }
            catch (Exception ex)
            {
                Log.Instance.Trace($"Failed to check for already running GPU processes.", ex);
            }
        }

        if (_settings.Store.GameDetection.UseGameConfigStore)
        {
            await _gameConfigStoreDetector.StartAsync(_detectedGamePathsCache).ConfigureAwait(false);
        }

        if (_settings.Store.GameDetection.UseEffectiveGameMode)
        {
            await _effectiveGameModeDetector.StartAsync().ConfigureAwait(false);
            var pinnedCleanup = new HashSet<Process>(ReferenceEqualityComparer.Instance);
            var pinnedDisposals = new HashSet<Process>(ReferenceEqualityComparer.Instance);

            try
            {
                lock (Lock)
                {
                    if (_isListening && _effectiveGameModeDetector.IsActive)
                    {
                        Log.Instance.Trace($"Game Mode already active on startup, checking foreground process...");
                        TryPinForegroundProcess(pinnedCleanup, pinnedDisposals);
                    }
                }
            }
            finally
            {
                CleanupProcesses(pinnedCleanup);
                DisposeProcesses(pinnedDisposals);
            }
        }

        await _instanceStartedEventAutoAutoListener.SubscribeChangedAsync(InstanceStartedEventAutoAutoListener_Changed).ConfigureAwait(false);
    }

    protected override async Task StopAsync()
    {
        lock (Lock)
        {
            _isListening = false;
        }

        await _instanceStartedEventAutoAutoListener.UnsubscribeChangedAsync(InstanceStartedEventAutoAutoListener_Changed).ConfigureAwait(false);
        await _gameConfigStoreDetector.StopAsync().ConfigureAwait(false);
        await _effectiveGameModeDetector.StopAsync().ConfigureAwait(false);

        _gpuController.Refreshed -= GpuController_Refreshed;
        var toCleanup = new HashSet<Process>(ReferenceEqualityComparer.Instance);

        try
        {
            lock (Lock)
            {
                if (!_preserveStateOnNextStart)
                {
                    foreach (var id in _processCache.Keys.ToArray())
                    {
                        RemoveTrackedProcess(id, toCleanup);
                    }

                    foreach (var process in _gameModePinnedProcesses.Values)
                    {
                        toCleanup.Add(process);
                    }

                    _gameModePinnedProcesses.Clear();
                    _detectedGamePathsCache.Clear();
                    RaiseChangedIfNeeded();
                }
                else
                {
                    _detectedGamePathsCache.Clear();
                    Log.Instance.Trace($"Preserving process cache during restart: {_processCache.Count} process(es).");
                }
            }
        }
        finally
        {
            CleanupProcesses(toCleanup);
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
        HandleGpuProcesses(e, false);
    }

    private void HandleGpuProcesses(GPUStatus status, bool initial)
    {
        var toCleanup = new HashSet<Process>(ReferenceEqualityComparer.Instance);

        try
        {
            lock (Lock)
            {
                if (!_isListening)
                {
                    return;
                }

                foreach (var process in status.Processes)
                {
                    try
                    {
                        if (process.HasExited)
                        {
                            continue;
                        }

                        var processName = process.ProcessName;
                        if (IsBlacklisted(processName))
                        {
                            continue;
                        }

                        var id = process.Id;
                        if (_processCache.ContainsKey(id))
                        {
                            continue;
                        }

                        var processPath = process.GetFileName();
                        var prefix = initial ? "Found already running game" : "Game detected";
                        Log.Instance.Trace($"{prefix}: {processName} [Source: Discrete GPU] [pid={id}, path={processPath ?? "Unknown"}]");
                        TryTrackProcess(process, id, toCleanup);
                    }
                    catch (Exception ex)
                    {
                        Log.Instance.Trace($"Failed to handle GPU process {TryGetId(process)?.ToString() ?? "Unknown"}.", ex);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Log.Instance.Trace($"Failed to handle GPU process refresh.", ex);
        }
        finally
        {
            CleanupProcesses(toCleanup);
        }
    }

    private void GameConfigStoreDetectorGamesConfigStoreDetected(object? sender, GameConfigStoreDetector.GameDetectedEventArgs e)
    {
        var toCleanup = new HashSet<Process>(ReferenceEqualityComparer.Instance);
        var toDispose = new HashSet<Process>(ReferenceEqualityComparer.Instance);

        try
        {
            lock (Lock)
            {
                if (!_isListening)
                {
                    return;
                }

                _detectedGamePathsCache.Clear();
                foreach (var game in e.Games)
                {
                    _detectedGamePathsCache.Add(game);
                    foreach (var process in Process.GetProcessesByName(game.Name))
                    {
                        var retained = false;

                        try
                        {
                            if (process.HasExited || IsBlacklisted(process.ProcessName))
                            {
                                continue;
                            }

                            var processPath = process.GetFileName();
                            if (processPath is not null && game.ExecutablePath is not null &&
                                !game.ExecutablePath.Equals(processPath, StringComparison.OrdinalIgnoreCase))
                            {
                                continue;
                            }

                            var id = process.Id;
                            if (!_processCache.ContainsKey(id))
                            {
                                Log.Instance.Trace($"Game detected: {process.ProcessName} [Source: Known Game List] [pid={id}, path={processPath ?? game.ExecutablePath ?? "Unknown"}]");
                                retained = TryTrackProcess(process, id, toCleanup);
                            }
                        }
                        catch (Exception ex)
                        {
                            Log.Instance.Trace($"Can't get game \"{game}\" details.", ex);
                        }
                        finally
                        {
                            if (!retained && !toCleanup.Contains(process))
                            {
                                toDispose.Add(process);
                            }
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Log.Instance.Trace($"Failed to handle known game list update.", ex);
        }
        finally
        {
            CleanupProcesses(toCleanup);
            DisposeProcesses(toDispose);
        }
    }

    private void EffectiveGameModeDetectorChanged(object? sender, bool e)
    {
        var toCleanup = new HashSet<Process>(ReferenceEqualityComparer.Instance);
        var toDispose = new HashSet<Process>(ReferenceEqualityComparer.Instance);

        try
        {
            lock (Lock)
            {
                if (!_isListening)
                {
                    return;
                }

                if (e)
                {
                    TryPinForegroundProcess(toCleanup, toDispose);
                }
                else
                {
                    var toRelease = new List<int>();
                    foreach (var pair in _gameModePinnedProcesses)
                    {
                        try
                        {
                            if (pair.Value.HasExited)
                            {
                                toRelease.Add(pair.Key);
                            }
                        }
                        catch
                        {
                            toRelease.Add(pair.Key);
                        }
                    }

                    foreach (var id in toRelease)
                    {
                        if (_gameModePinnedProcesses.TryGetValue(id, out var process))
                        {
                            Log.Instance.Trace($"Game Mode ended and process exited: {GetProcessName(process)} [Source: Windows Game Mode] [pid={id}].");
                        }

                        RemoveTrackedProcess(id, toCleanup);
                    }

                    RaiseChangedIfNeeded();
                }

                if (_processCache.Count != 0)
                {
                    Log.Instance.Trace($"Game Mode deactivation ignored: process cache is not empty ({_processCache.Count} active game(s)).");
                }
            }
        }
        catch (Exception ex)
        {
            Log.Instance.Trace($"Failed to handle Windows Game Mode state change.", ex);
        }
        finally
        {
            CleanupProcesses(toCleanup);
            DisposeProcesses(toDispose);
        }
    }

    private unsafe void TryPinForegroundProcess(HashSet<Process> toCleanup, HashSet<Process> toDispose)
    {
        Process? process = null;
        var retained = false;

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

            process = Process.GetProcessById((int)processId);
            var id = process.Id;
            if (process.HasExited || _processCache.ContainsKey(id))
            {
                return;
            }

            var processName = process.ProcessName;
            var processPath = process.GetFileName();
            if (IsBlacklisted(processName))
            {
                Log.Instance.Trace($"Ignoring blacklisted process: {processName} [Source: Windows Game Mode] [pid={id}, path={processPath ?? "Unknown"}].");
                return;
            }

            Log.Instance.Trace($"Game detected: {processName} [Source: Windows Game Mode] [pid={id}, path={processPath ?? "Unknown"}].");
            retained = TryTrackProcess(process, id, toCleanup, pinGameMode: true);
        }
        catch (Exception ex)
        {
            Log.Instance.Trace($"Failed to pin process on Game Mode detection.", ex);
        }
        finally
        {
            if (process is not null && !retained)
            {
                toCleanup.Add(process);
            }
        }
    }
    private bool IsBlacklisted(string processName)
    {
        if (_settings.Store.IncludedProcesses.Contains(processName, StringComparer.OrdinalIgnoreCase))
        {
            return false;
        }

        if (_settings.Store.ExcludedProcesses.Contains(processName, StringComparer.OrdinalIgnoreCase))
        {
            return true;
        }

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

    private void InstanceStartedEventAutoAutoListener_Changed(object? sender, InstanceStartedEventAutoAutoListener.ChangedEventArgs e)
    {
        var toCleanup = new HashSet<Process>(ReferenceEqualityComparer.Instance);
        var toDispose = new HashSet<Process>(ReferenceEqualityComparer.Instance);
        Process? startedProcess = null;
        var retained = false;

        try
        {
            lock (Lock)
            {
                if (!_isListening || e.ProcessId < 0)
                {
                    return;
                }

                var isIncluded = _settings.Store.IncludedProcesses.Contains(e.ProcessName, StringComparer.OrdinalIgnoreCase);
                var isGameConfigStore = _settings.Store.GameDetection.UseGameConfigStore &&
                    _detectedGamePathsCache.Any(p => e.ProcessName.Equals(p.Name, StringComparison.OrdinalIgnoreCase));

                if (!isIncluded && !isGameConfigStore)
                {
                    return;
                }

                if (_processCache.ContainsKey(e.ProcessId))
                {
                    return;
                }

                startedProcess = Process.GetProcessById(e.ProcessId);
                if (startedProcess.HasExited)
                {
                    return;
                }

                var processPath = startedProcess.GetFileName();
                var source = isIncluded ? "Included" : "Known Game List";
                if (!isIncluded && string.IsNullOrEmpty(processPath))
                {
                    Log.Instance.Trace($"Can't get path for {e.ProcessName} [Source: {source}] [pid={e.ProcessId}].");
                    return;
                }

                var processInfo = string.IsNullOrEmpty(processPath)
                    ? new ProcessInfo(e.ProcessName, null)
                    : ProcessInfo.FromPath(processPath);

                if (!isIncluded && !_detectedGamePathsCache.Contains(processInfo))
                {
                    return;
                }

                if (IsBlacklisted(e.ProcessName))
                {
                    Log.Instance.Trace($"Ignoring blacklisted process: {e.ProcessName} [Source: {source}] [pid={e.ProcessId}, path={processPath ?? "Unknown"}].");
                    return;
                }

                Log.Instance.Trace($"Game detected: {e.ProcessName} [Source: {source}] [pid={e.ProcessId}, path={processPath ?? "Unknown"}].");
                retained = TryTrackProcess(startedProcess, e.ProcessId, toCleanup);
            }
        }
        catch (Exception ex)
        {
            Log.Instance.Trace($"Failed to attach to {e.ProcessName} [pid={e.ProcessId}].", ex);
        }
        finally
        {
            if (startedProcess is not null && !retained && !toCleanup.Contains(startedProcess))
            {
                toDispose.Add(startedProcess);
            }

            CleanupProcesses(toCleanup);
            DisposeProcesses(toDispose);
        }
    }

    private bool TryTrackProcess(Process process, int id, HashSet<Process> toCleanup, bool pinGameMode = false)
    {
        if (!_isListening)
        {
            return false;
        }

        if (_processCache.TryGetValue(id, out var current))
        {
            try
            {
                if (!current.HasExited)
                {
                    return false;
                }
            }
            catch
            {
            }

            RemoveTrackedProcess(id, toCleanup);
        }

        try
        {
            if (process.HasExited)
            {
                return false;
            }
        }
        catch (Exception ex)
        {
            Log.Instance.Trace($"Failed to inspect process [pid={id}].", ex);
            return false;
        }

        _processCache.Add(id, process);
        if (!Attach(process))
        {
            RemoveTrackedProcess(id, toCleanup);
            RaiseChangedIfNeeded();
            return false;
        }

        if (pinGameMode)
        {
            _gameModePinnedProcesses[id] = process;
        }

        try
        {
            if (process.HasExited)
            {
                RemoveTrackedProcess(id, toCleanup);
                RaiseChangedIfNeeded();
                return false;
            }
        }
        catch (Exception ex)
        {
            Log.Instance.Trace($"Failed to confirm process [pid={id}] is running.", ex);
            RemoveTrackedProcess(id, toCleanup);
            RaiseChangedIfNeeded();
            return false;
        }

        RaiseChangedIfNeeded();
        return true;
    }

    private void RaiseChangedIfNeeded()
    {
        var newState = _processCache.Count > 0;
        if (newState == _lastState)
        {
            return;
        }

        _lastState = newState;
        Log.Instance.Trace($"Game running state changed: Running={newState} [activeGamesCount={_processCache.Count}].");

        try
        {
            RaiseChanged(new ChangedEventArgs(newState));
        }
        catch (Exception ex)
        {
            Log.Instance.Trace($"Failed to notify game running state change.", ex);
        }
    }

    private bool Attach(Process process)
    {
        Log.Instance.Trace($"Attaching to process: {GetProcessName(process)} [pid={TryGetId(process)?.ToString() ?? "Unknown"}]...");

        try
        {
            process.Exited += Process_Exited;
            process.EnableRaisingEvents = true;
            return true;
        }
        catch (Exception ex)
        {
            try
            {
                process.Exited -= Process_Exited;
            }
            catch
            {
            }

            Log.Instance.Trace($"Failed to enable events for process: {GetProcessName(process)} [pid={TryGetId(process)?.ToString() ?? "Unknown"}].", ex);
            return false;
        }
    }

    private void Detach(Process process)
    {
        try
        {
            process.Exited -= Process_Exited;
        }
        catch
        {
        }

        try
        {
            process.EnableRaisingEvents = false;
        }
        catch
        {
        }

        Log.Instance.Trace($"Detached from process: {GetProcessName(process)} [pid={TryGetId(process)?.ToString() ?? "Unknown"}].");
    }

    private static string GetProcessName(Process process)
    {
        try
        {
            return process.ProcessName;
        }
        catch
        {
            return "Unknown";
        }
    }

    private static int? TryGetId(Process process)
    {
        try
        {
            return process.Id;
        }
        catch
        {
            return null;
        }
    }

    private static void DisposeProcess(Process? process)
    {
        if (process is null)
        {
            return;
        }

        try
        {
            process.Dispose();
        }
        catch
        {
        }
    }

    private void RemoveTrackedProcess(int id, HashSet<Process> toCleanup)
    {
        if (_processCache.Remove(id, out var process))
        {
            toCleanup.Add(process);
        }

        if (_gameModePinnedProcesses.Remove(id, out var pinned))
        {
            toCleanup.Add(pinned);
        }
    }

    private void CleanupProcesses(IEnumerable<Process> processes)
    {
        foreach (var process in processes)
        {
            try
            {
                Detach(process);
            }
            catch
            {
            }

            DisposeProcess(process);
        }
    }

    private static void DisposeProcesses(IEnumerable<Process> processes)
    {
        foreach (var process in processes)
        {
            DisposeProcess(process);
        }
    }

    private void Process_Exited(object? o, EventArgs args)
    {
        if (o is not Process process)
        {
            return;
        }

        try
        {
            ThreadPool.QueueUserWorkItem(_ => HandleProcessExited(process));
        }
        catch (Exception ex)
        {
            Log.Instance.Trace($"Failed to queue process exit handling.", ex);
        }
    }

    private void HandleProcessExited(Process exitedProcess)
    {
        var toCleanup = new HashSet<Process>(ReferenceEqualityComparer.Instance);

        try
        {
            lock (Lock)
            {
                var exitedId = _processCache
                    .Where(pair => ReferenceEquals(pair.Value, exitedProcess))
                    .Select(pair => (int?)pair.Key)
                    .FirstOrDefault();

                if (exitedId.HasValue)
                {
                    Log.Instance.Trace($"Process exited: {GetProcessName(exitedProcess)} [pid={exitedId.Value}].");
                    RemoveTrackedProcess(exitedId.Value, toCleanup);
                }

                var deadIds = new List<int>();
                foreach (var pair in _processCache)
                {
                    try
                    {
                        if (pair.Value.HasExited)
                        {
                            deadIds.Add(pair.Key);
                        }
                    }
                    catch
                    {
                        deadIds.Add(pair.Key);
                    }
                }

                foreach (var id in deadIds)
                {
                    RemoveTrackedProcess(id, toCleanup);
                }

                if (toCleanup.Count > 0)
                {
                    Log.Instance.Trace($"Removed {toCleanup.Count} exited process object(s) from cache. Remaining: {_processCache.Count}.");
                }

                RaiseChangedIfNeeded();
            }
        }
        catch (Exception ex)
        {
            Log.Instance.Trace($"Failed to handle process exit.", ex);
        }
        finally
        {
            CleanupProcesses(toCleanup);
        }
    }
}
