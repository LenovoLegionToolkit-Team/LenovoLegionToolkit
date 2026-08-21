// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at http://mozilla.org/MPL/2.0/.
// Copyright (C) RAMSPDToolkit and Contributors.
// Partial Copyright (C) Michael Möller <mmoeller@openhardwaremonitor.org> and Contributors.
// All Rights Reserved.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using LenovoLegionToolkit.Lib.Controllers.Sensors.Providers;
using LenovoLegionToolkit.Lib.Features.Hybrid;
using LenovoLegionToolkit.Lib.Settings;
using LenovoLegionToolkit.Lib.System;
using LenovoLegionToolkit.Lib.Utils;
using LibreHardwareMonitor.Hardware;

namespace LenovoLegionToolkit.Lib.Controllers.Sensors;

public class SensorsGroupController : IDisposable
{
    #region Constants

    private const string UNKNOWN_NAME = "UNKNOWN";
    private const string HARDWARE_ID_NVIDIA_GPU = "NvidiaGPU";

    private const string REGEX_STRIP_AMD = @"\s+with\s+Radeon\s+Graphics$";
    private const string REGEX_STRIP_INTEL = @"\s*\d+(?:th|st|nd|rd)?\s+Gen\b";
    private const string REGEX_STRIP_NVIDIA = @"(?i)\b(?:Nvidia\s+)?(GeForce\s+(?:RTX|GTX)\s+\d{3,4}(?:\s+(Ti|SUPER|Ti\s+SUPER|M))?)\b(?:\s+Laptop\s+GPU)?(?!\S)";
    private const string REGEX_CLEAN_SPACES = @"\s+";

    #endregion

    private bool _initialized;
    public LibreHardwareMonitorInitialState InitialState { get; private set; }
    public bool IsHybrid => _cpuProvider.IsHybrid;

    private readonly SemaphoreSlim _initSemaphore = new(1, 1);
    private readonly List<IHardware> _hardware = [];

    private Computer? _computer;

    private readonly CpuSensorProvider _cpuProvider;
    private readonly GpuSensorProvider _gpuProvider;
    private readonly MemorySensorProvider _memoryProvider;
    private readonly StorageSensorProvider _storageProvider;
    private readonly List<ISensorProvider> _allProviders = [];

    private volatile bool _isResetting;
    private volatile bool _needRefreshGpuHardware;
    private bool _dgpuHardwareSkipped;

    private bool _selectedGpuIsIgpu;
    public bool SelectedGpuIsIgpu
    {
        get => _selectedGpuIsIgpu;
        set
        {
            lock (_configLock)
            {
                if (_selectedGpuIsIgpu != value)
                {
                    _selectedGpuIsIgpu = value;
                    _cachedGpuName = string.Empty;
                }
            }
        }
    }

    private bool _showAverageCpuFrequency;
    public bool ShowAverageCpuFrequency
    {
        get => _showAverageCpuFrequency;
        set
        {
            lock (_configLock)
            {
                _showAverageCpuFrequency = value;
            }
        }
    }

    public CpuVoltageMode CpuVoltageMode
    {
        get => _cpuProvider.VoltageMode;
        set
        {
            lock (_configLock)
            {
                _cpuProvider.VoltageMode = value;
            }
        }
    }

    public int CpuVoltageCoreIndex
    {
        get => _cpuProvider.VoltageCoreIndex;
        set
        {
            lock (_configLock)
            {
                _cpuProvider.VoltageCoreIndex = value;
            }
        }
    }

    public int AvailableVoltageCoreCount => _cpuProvider.AvailableCoreCount;

    private bool _isDgpuConnected = true;
    public bool IsDgpuConnected
    {
        get => _isDgpuConnected;
        set
        {
            lock (_configLock)
            {
                if (_isDgpuConnected != value)
                {
                    _isDgpuConnected = value;
                    _cachedGpuName = string.Empty;
                }
            }
        }
    }

    private string _cachedCpuName = string.Empty;
    private string _cachedGpuName = string.Empty;

    private readonly Lock _hardwareLock = new();
    private readonly Lock _configLock = new();
    public HardwareSensorSnapshot Snapshot { get; private set; } = new();
    private volatile bool _hardwareInitialized;

    private long _lastUpdateTick;
    private const int MIN_UPDATE_INTERVAL_MS = 100;

    private readonly Dictionary<object, SensorSubscription> _subscribers = [];
    private CancellationTokenSource? _producerCts;
    private Task? _producerTask;
    public event Action<HardwareSensorSnapshot>? SensorsUpdated;

    private readonly GPUController _gpuController = IoCContainer.Resolve<GPUController>();

    private readonly struct SensorSubscription(TimeSpan interval, HardwareUpdateScope scope)
    {
        public TimeSpan Interval { get; } = interval;
        public HardwareUpdateScope Scope { get; } = scope;
    }

    private static readonly IDisposable NoOpDisposable = new LambdaDisposable(() => { });

    private static bool IsDgpuEjectInProgress() => IoCContainer.Resolve<HybridModeFeature>().ShouldKeepDGPUAsleep();

    public SensorsGroupController()
    {
        _cpuProvider = IoCContainer.Resolve<CpuSensorProvider>();
        _gpuProvider = IoCContainer.Resolve<GpuSensorProvider>();
        _memoryProvider = IoCContainer.Resolve<MemorySensorProvider>();
        _storageProvider = IoCContainer.Resolve<StorageSensorProvider>();

        _allProviders.Add(_cpuProvider);
        _allProviders.Add(_gpuProvider);
        _allProviders.Add(_memoryProvider);
        _allProviders.Add(_storageProvider);

        _cpuProvider.SensorResetNeeded += OnSensorResetNeeded;
    }

    private void OnSensorResetNeeded()
    {
        Task.Run(ResetSensors);
    }

    #region Subscription

    public IDisposable Subscribe(TimeSpan interval, IEnumerable<SensorItem> items)
    {
        if (items is null)
            return NoOpDisposable;
        var scope = ComputeScopeFromSensorItems(items);
        if (scope == HardwareUpdateScope.None)
            return NoOpDisposable;
        return Subscribe(interval, scope);
    }

    public IDisposable Subscribe(TimeSpan interval, IEnumerable<OsdItem> items)
    {
        if (items is null)
            return NoOpDisposable;
        var scope = ComputeScopeFromOsdItems(items);
        if (scope == HardwareUpdateScope.None)
            return NoOpDisposable;
        return Subscribe(interval, scope);
    }

    public IDisposable Subscribe(TimeSpan interval, HardwareUpdateScope scope)
    {
        var key = new object();
        Start(key, interval, scope);
        return new LambdaDisposable(() => Stop(key));
    }

    private HardwareUpdateScope ComputeScopeFromSensorItems(IEnumerable<SensorItem> items)
    {
        var set = items as HashSet<SensorItem> ?? new HashSet<SensorItem>(items);
        return _allProviders
            .Where(p => p.IsAvailable && set.Overlaps(p.ProvidedSensorItems))
            .Select(p => p.Scope)
            .Aggregate(HardwareUpdateScope.None, (a, b) => a | b);
    }

    private HardwareUpdateScope ComputeScopeFromOsdItems(IEnumerable<OsdItem> items)
    {
        var set = items as HashSet<OsdItem> ?? new HashSet<OsdItem>(items);
        return _allProviders
            .Where(p => p.IsAvailable && set.Overlaps(p.ProvidedOsdItems))
            .Select(p => p.Scope)
            .Aggregate(HardwareUpdateScope.None, (a, b) => a | b);
    }

    #endregion

    #region Initialization

    public async Task<LibreHardwareMonitorInitialState> IsSupportedAsync()
    {
        LibreHardwareMonitorInitialState result = await InitializeAsync().ConfigureAwait(false);
        try
        {
            bool haveHardware;
            lock (_hardwareLock) { haveHardware = _hardware.Count != 0; }
            if (haveHardware && result is LibreHardwareMonitorInitialState.Initialized or LibreHardwareMonitorInitialState.Success) return result;
        }
        catch (Exception ex)
        {
            Log.Instance.Trace($"Sensor group check failed: {ex}");
            return result;
        }

        return LibreHardwareMonitorInitialState.Fail;
    }

    private void PopulateHardware()
    {
        if (_computer is null)
        {
            return;
        }

        foreach (var h in _computer.Hardware)
        {
            try
            {
                if (h.HardwareType == HardwareType.GpuNvidia && IsDgpuEjectInProgress())
                {
                    _dgpuHardwareSkipped = true;
                    _hardware.Add(h);
                    continue;
                }

                h.Update();
                _hardware.Add(h);
            }
            catch { /* Ignore */ }
        }
    }

    private void GetHardware()
    {
        lock (_hardwareLock)
        {
            if (_hardwareInitialized)
            {
                return;
            }

            if (PawnIOHelper.GetPawnIOState() != PawnIOState.Installed)
            {
                return;
            }

            try
            {
                _computer = new Computer
                {
                    IsCpuEnabled = true,
                    IsGpuEnabled = true,
                    IsMemoryEnabled = true,
                    IsMotherboardEnabled = false,
                    IsControllerEnabled = false,
                    IsNetworkEnabled = false,
                    IsStorageEnabled = true
                };

                _computer.Open();
                if (!IsDgpuEjectInProgress())
                {
                    _computer.Accept(new UpdateVisitor());
                }

                PopulateHardware();
                DiscoverHardware();
            }
            catch (Exception ex)
            {
                Log.Instance.Trace($"GetHardware failed: {ex}");
                _computer?.Close();
                _computer = null;
                _hardware.Clear();
                throw;
            }
            finally { _hardwareInitialized = true; }
        }
    }

    private void DiscoverHardware()
    {
        foreach (var provider in _allProviders)
        {
            provider.Discover(_hardware);
        }
    }

    private async Task<LibreHardwareMonitorInitialState> InitializeAsync()
    {
        if (_initialized) { InitialState = LibreHardwareMonitorInitialState.Initialized; return InitialState; }
        await _initSemaphore.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_initialized) { InitialState = LibreHardwareMonitorInitialState.Initialized; return InitialState; }
            await Task.Run(GetHardware).ConfigureAwait(false);
            _initialized = true;
            InitialState = _hardware.Count == 0 ? LibreHardwareMonitorInitialState.Fail : LibreHardwareMonitorInitialState.Success;
            return InitialState;
        }
        catch (DllNotFoundException) { HandleInitException("DLL Not Found"); InitialState = LibreHardwareMonitorInitialState.PawnIONotInstalled; return InitialState; }
        catch (Exception ex) { HandleInitException(ex.Message); throw; }
        finally { _initSemaphore.Release(); }
    }

    private void HandleInitException(string reason)
    {
        var settings = IoCContainer.Resolve<ApplicationSettings>();
        settings.Store.EnableHardwareSensors = false;
        settings.Store.UseNewSensorDashboard = false;
        settings.SynchronizeStore();
        InitialState = LibreHardwareMonitorInitialState.Fail;
    }

    #endregion

    #region Name Getters

    public Task<string> GetCpuNameAsync()
    {
        lock (_configLock)
        {
            if (_isResetting || !IsLibreHardwareMonitorInitialized() || !_cpuProvider.IsAvailable)
                return Task.FromResult(UNKNOWN_NAME);

            if (!string.IsNullOrEmpty(_cachedCpuName))
                return Task.FromResult(_cachedCpuName);

            var cpuHardware = _hardware.FirstOrDefault(h => h.HardwareType == HardwareType.Cpu);
            _cachedCpuName = cpuHardware != null ? StripName(cpuHardware.Name) : UNKNOWN_NAME;
            return Task.FromResult(_cachedCpuName);
        }
    }

    public Task<string> GetGpuNameAsync()
    {
        lock (_configLock)
        {
            if (_isResetting || !IsLibreHardwareMonitorInitialized())
                return Task.FromResult(UNKNOWN_NAME);

            if (!string.IsNullOrEmpty(_cachedGpuName) && !_needRefreshGpuHardware)
                return Task.FromResult(_cachedGpuName);

            var dGpu = _gpuProvider.DgpuHardware;
            var forceIgpu = !SelectedGpuIsIgpu && (dGpu == null || !_isDgpuConnected);
            var gpu = (SelectedGpuIsIgpu || forceIgpu) ? _gpuProvider.IgpuHardware : dGpu;
            _cachedGpuName = gpu != null ? StripName(gpu.Name) : UNKNOWN_NAME;
            _needRefreshGpuHardware = false;
            return Task.FromResult(_cachedGpuName);
        }
    }

    #endregion

    #region Hardware Refresh

    public void NeedRefreshHardware(string hardwareId)
    {
        if (!IsLibreHardwareMonitorInitialized() || _computer == null || hardwareId != HARDWARE_ID_NVIDIA_GPU) return;
        lock (_hardwareLock)
        {
            ResetSensors();

            try
            {
                NVAPI.Initialize();
            }
            catch { }

            _needRefreshGpuHardware = true;
        }
    }

    #endregion

    #region Update

    private Task? _activeUpdateTask;
    private readonly Lock _updateTaskLock = new();

    public async Task UpdateAsync(bool force = false, HardwareUpdateScope scope = HardwareUpdateScope.All)
    {
        if (_isResetting || !IsLibreHardwareMonitorInitialized()) return;

        var now = Environment.TickCount64;
        if (!force && now - _lastUpdateTick < MIN_UPDATE_INTERVAL_MS) return;

        Task? updateTask;
        lock (_updateTaskLock)
        {
            if (_activeUpdateTask == null)
            {
                _activeUpdateTask = PerformUpdateInternal(scope);
            }
            updateTask = _activeUpdateTask;
        }

        if (updateTask != null)
            await updateTask.ConfigureAwait(false);
    }

    private async Task PerformUpdateInternal(HardwareUpdateScope scope)
    {
        try
        {
            var now = Environment.TickCount64;
            _lastUpdateTick = now;

            if (scope == HardwareUpdateScope.None)
                return;

            var gpuState = await _gpuController.GetLastKnownStateAsync().ConfigureAwait(false);
            var dgpuEjectInProgress = IsDgpuEjectInProgress();
            bool gpuInactive = IsGpuInActive(gpuState) || dgpuEjectInProgress;

            await Task.Run(() =>
            {
                lock (_hardwareLock)
                {
                    if (_isResetting || _computer == null || !_hardwareInitialized) return;
                    try
                    {
                        var brokenHardware = new List<IHardware>();
                        foreach (var h in _hardware)
                        {
                            if (h == null) continue;
                            if (!ShouldUpdateHardware(h, scope, gpuInactive)) continue;
                            try
                            {
                                h.Update();
                            }
                            catch (Exception ex)
                            {
                                Log.Instance.Trace($"Failed to update hardware {h.Name}: {ex.Message}. It will be removed from the update list.", ex);
                                brokenHardware.Add(h);
                            }
                        }

                        foreach (var h in brokenHardware)
                        {
                            _hardware.Remove(h);
                        }

                        if (!dgpuEjectInProgress && _dgpuHardwareSkipped)
                        {
                            DiscoverHardware();
                            _dgpuHardwareSkipped = false;
                        }

                        // Read from providers — each populates provider.Values
                        if (_cpuProvider.IsAvailable && Includes(scope, HardwareUpdateScope.Cpu))
                            _cpuProvider.Read();

                        IReadOnlyDictionary<SensorItem, float> gpuValues = new Dictionary<SensorItem, float>();
                        var dGpu = _gpuProvider.DgpuHardware;
                        var forceIgpu = !SelectedGpuIsIgpu && (dGpu == null || !_isDgpuConnected);

                        if (_gpuProvider.IsAvailable && Includes(scope, HardwareUpdateScope.Gpu))
                        {
                            if (SelectedGpuIsIgpu || forceIgpu)
                                gpuValues = _gpuProvider.ReadIgpu();
                            else if (!gpuInactive)
                                gpuValues = _gpuProvider.ReadDgpu();
                            else
                                gpuValues = GpuSensorProvider.ReadInactive();
                        }

                        if (_memoryProvider.IsAvailable && Includes(scope, HardwareUpdateScope.Memory))
                            _memoryProvider.Read();

                        if (Includes(scope, HardwareUpdateScope.Storage))
                            _storageProvider.Read();

                        // Merge all provider values into a single snapshot
                        var snap = new HardwareSensorSnapshot();
                        snap = snap.WithValues(_cpuProvider.Values);
                        snap = snap.WithValues(gpuValues);
                        snap = snap.WithValues(_memoryProvider.Values);
                        snap = snap.WithValues(_storageProvider.Values);
                        Snapshot = snap;

                        SensorsUpdated?.Invoke(Snapshot);
                    }
                    catch (Exception ex)
                    {
                        if (ex is IndexOutOfRangeException) Task.Run(ResetSensors);
                    }
                }
            }).ConfigureAwait(false);
        }
        finally
        {
            lock (_updateTaskLock)
            {
                _activeUpdateTask = null;
            }
        }
    }

    private void ResetSensors()
    {
        _isResetting = true;
        try
        {
            lock (_hardwareLock)
            {
                _computer?.Close(); _hardware.Clear();
                _computer?.Open();
                _computer?.Reset();
                if (_computer == null)
                {
                    return;
                }

                if (!IsDgpuEjectInProgress())
                {
                    _computer.Accept(new UpdateVisitor());
                }

                PopulateHardware();
                DiscoverHardware();
            }
        }
        finally { _isResetting = false; }
    }

    #endregion

    #region Name Helpers

    private static string StripName(string name)
    {
        if (string.IsNullOrEmpty(name)) return UNKNOWN_NAME;
        var cleaned = name.Trim();
        if (cleaned.Contains("AMD", StringComparison.OrdinalIgnoreCase)) cleaned = Regex.Replace(cleaned, REGEX_STRIP_AMD, "", RegexOptions.IgnoreCase);
        else if (cleaned.Contains("Intel", StringComparison.OrdinalIgnoreCase)) cleaned = Regex.Replace(cleaned, REGEX_STRIP_INTEL, "", RegexOptions.IgnoreCase);
        else if (cleaned.Contains("Nvidia", StringComparison.OrdinalIgnoreCase) || cleaned.Contains("GeForce", StringComparison.OrdinalIgnoreCase))
        {
            var m = Regex.Match(cleaned, REGEX_STRIP_NVIDIA);
            if (m.Success) cleaned = m.Groups[1].Value;
        }
        return Regex.Replace(cleaned, REGEX_CLEAN_SPACES, " ").Trim();
    }

    #endregion

    #region Helpers

    public bool IsGpuInActive(GPUState state) => state is GPUState.Inactive or GPUState.PoweredOff or GPUState.NvidiaGpuNotFound;
    public bool IsLibreHardwareMonitorInitialized() => InitialState is LibreHardwareMonitorInitialState.Initialized or LibreHardwareMonitorInitialState.Success;

    #endregion

    #region Producer Loop

    public void Start(object subscriber, TimeSpan interval) => Start(subscriber, interval, HardwareUpdateScope.All);

    public void Start(object subscriber, TimeSpan interval, HardwareUpdateScope scope)
    {
        lock (_subscribers)
        {
            _subscribers[subscriber] = new SensorSubscription(interval, scope);
            UpdateProducerLoop();
        }
    }

    public void Stop(object subscriber)
    {
        lock (_subscribers)
        {
            if (_subscribers.Remove(subscriber))
            {
                UpdateProducerLoop();
            }
        }
    }

    private void UpdateProducerLoop()
    {
        if (_subscribers.Count == 0)
        {
            StopProducerLoop();
            return;
        }

        StopProducerLoop();

        _producerCts = new CancellationTokenSource();
        var token = _producerCts.Token;
        _producerTask = Task.Run(() => ProducerLoop(token), token);
    }

    private void StopProducerLoop()
    {
        _producerCts?.Cancel();
        _producerCts?.Dispose();
        _producerCts = null;
        _producerTask = null;
    }

    private async Task ProducerLoop(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            TimeSpan minInterval;
            HardwareUpdateScope scope;
            lock (_subscribers)
            {
                if (_subscribers.Count == 0) return;
                minInterval = _subscribers.Values.Min(s => s.Interval);
                scope = _subscribers.Values.Aggregate(HardwareUpdateScope.None, (current, s) => current | s.Scope);
            }

            try
            {
                await UpdateAsync(true, scope).ConfigureAwait(false);

                await Task.Delay(minInterval, token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                Log.Instance.Trace($"ProducerLoop error: {ex}");
                await Task.Delay(1000, token).ConfigureAwait(false);
            }
        }
    }

    #endregion

    #region Static Helpers

    private static bool ShouldUpdateHardware(IHardware hardware, HardwareUpdateScope scope, bool gpuInactive)
    {
        if (gpuInactive && hardware.HardwareType == HardwareType.GpuNvidia)
            return false;

        return hardware.HardwareType switch
        {
            HardwareType.Cpu => Includes(scope, HardwareUpdateScope.Cpu),
            HardwareType.GpuAmd or HardwareType.GpuIntel or HardwareType.GpuNvidia => Includes(scope, HardwareUpdateScope.Gpu),
            HardwareType.Memory => Includes(scope, HardwareUpdateScope.Memory),
            HardwareType.Storage => Includes(scope, HardwareUpdateScope.Storage),
            _ => false
        };
    }

    private static bool Includes(HardwareUpdateScope scope, HardwareUpdateScope flag) => (scope & flag) != 0;

    #endregion

    public void Dispose()
    {
        StopProducerLoop();
        _cpuProvider.SensorResetNeeded -= OnSensorResetNeeded;
        lock (_hardwareLock) { _computer?.Close(); _computer = null; _hardwareInitialized = false; }
        _initSemaphore.Dispose();
        GC.SuppressFinalize(this);
    }
}
