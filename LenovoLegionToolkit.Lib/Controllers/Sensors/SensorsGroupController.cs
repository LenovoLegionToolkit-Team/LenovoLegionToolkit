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
using LenovoLegionToolkit.Lib.Settings;
using LenovoLegionToolkit.Lib.System;
using LenovoLegionToolkit.Lib.Utils;
using LibreHardwareMonitor.Hardware;

namespace LenovoLegionToolkit.Lib.Controllers.Sensors;

public class SensorsGroupController : IDisposable
{
    #region Constants (Magic Words & Numbers)

    private const float INVALID_VALUE_FLOAT = -1f;
    private const double INVALID_VALUE_DOUBLE = 0.0;
    private const string UNKNOWN_NAME = "UNKNOWN";

    private const string SENSOR_NAME_TOTAL_MEMORY = "Total Memory";
    private const string SENSOR_NAME_MEMORY_USED = "Memory Used";
    private const string SENSOR_NAME_MEMORY_AVAILABLE = "Memory Available";
    private const string SENSOR_NAME_PACKAGE = "Package";
    private const string SENSOR_NAME_GPU_HOTSPOT = "GPU Memory Junction";

    private const string HARDWARE_ID_NVIDIA_GPU = "NvidiaGPU";

    private const string REGEX_AMD_GPU_INTEGRATED = @"AMD Radeon\(TM\)\s+\d+M";
    private const string REGEX_STRIP_AMD = @"\s+with\s+Radeon\s+Graphics$";
    private const string REGEX_STRIP_INTEL = @"\s*\d+(?:th|st|nd|rd)?\s+Gen\b";
    private const string REGEX_STRIP_NVIDIA = @"(?i)\b(?:Nvidia\s+)?(GeForce\s+(?:RTX|GTX)\s+\d{3,4}(?:\s+(Ti|SUPER|Ti\s+SUPER|M))?)\b(?:\s+Laptop\s+GPU)?(?!\S)";
    private const string REGEX_CLEAN_SPACES = @"\s+";

    private const float MAX_VALID_CPU_POWER = 400f;
    private const float MIN_VALID_POWER_READING = 0f;
    private const int MAX_CPU_POWER_STUCK_RETRIES = 10;
    private const float MIN_ACTIVE_GPU_POWER = 10f;
    private const float MB_PER_GB = 1024f;

    #endregion

    private bool _initialized;
    public LibreHardwareMonitorInitialState InitialState { get; private set; }
    public bool IsHybrid { get; private set; }

    private readonly SemaphoreSlim _initSemaphore = new(1, 1);

    private readonly List<IHardware> _hardware = [];

    private Computer? _computer;
    private IHardware? _cpuHardware;
    private IHardware? _amdGpuHardware;
    private IHardware? _gpuHardware;
    private IHardware? _iGpuHardware;
    private IHardware? _memoryHardware;

    private ISensor? _cpuTempSensor;
    private ISensor? _cpuUsageSensor;
    private ISensor? _gpuUsageSensor;
    private ISensor? _gpuTempSensor;
    private ISensor? _gpuClockSensor;

    private ISensor? _iGpuUsageSensor;
    private ISensor? _iGpuTempSensor;
    private ISensor? _iGpuClockSensor;
    private ISensor? _iGpuPowerSensor;

    private ISensor? _gpuD3DVramUsedSensor;
    private ISensor? _gpuVramTotalSensor;
    private float _cachedGpuVramTotal = INVALID_VALUE_FLOAT;

    private ISensor? _iGpuD3DVramUsedSensor;
    private ISensor? _iGpuVramTotalSensor;
    private float _cachedIGpuVramTotal = INVALID_VALUE_FLOAT;

    private readonly List<ISensor> _pCoreClockSensors = [];
    private readonly List<ISensor> _eCoreClockSensors = [];
    private ISensor? _cpuPackagePowerSensor;
    private readonly List<ISensor> _cpuCoreClockSensors = [];

    private ISensor? _gpuPowerSensor;
    private ISensor? _gpuHotspotSensor;

    private ISensor? _memoryLoadSensor;
    private ISensor? _memoryUsedSensor;
    private ISensor? _memoryAvailableSensor;
    private float _cachedMemoryTotal = INVALID_VALUE_FLOAT;
    private readonly List<ISensor> _memoryTempSensors = [];
    private readonly List<ISensor> _storageTempSensors = [];

    private volatile bool _isResetting;
    private bool _needRefreshGpuHardware;

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
                    if (!_isDgpuConnected)
                    {
                        _gpuHardware = null;
                        _amdGpuHardware = null;
                    }
                }
            }
        }
    }

    private string _cachedCpuName = string.Empty;
    private string _cachedGpuName = string.Empty;

    private float _cachedCpuPower;
    private int _cachedCpuPowerTime;

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

    private static readonly SensorItem[] CpuSensorItems = [SensorItem.CpuUtilization, SensorItem.CpuFrequency, SensorItem.CpuTemperature, SensorItem.CpuPower];
    private static readonly SensorItem[] GpuSensorItems = [SensorItem.GpuUtilization, SensorItem.GpuFrequency, SensorItem.GpuCoreTemperature, SensorItem.GpuVramTemperature, SensorItem.GpuTemperatures, SensorItem.GpuPower, SensorItem.GpuVramUtilization];
    private static readonly SensorItem[] MemorySensorItems = [SensorItem.MemoryUtilization, SensorItem.MemoryTemperature];
    private static readonly SensorItem[] FansSensorItems = [SensorItem.CpuFanSpeed, SensorItem.GpuFanSpeed, SensorItem.PchFanSpeed, SensorItem.PchTemperature];
    private static readonly SensorItem[] StorageSensorItems = [SensorItem.Disk1Temperature, SensorItem.Disk2Temperature];

    private static readonly OsdItem[] CpuOsdItems = [OsdItem.CpuFrequency, OsdItem.CpuPCoreFrequency, OsdItem.CpuECoreFrequency, OsdItem.CpuUtilization, OsdItem.CpuTemperature, OsdItem.CpuPower];
    private static readonly OsdItem[] GpuOsdItems = [OsdItem.GpuFrequency, OsdItem.GpuUtilization, OsdItem.GpuTemperature, OsdItem.GpuVramUtilization, OsdItem.GpuVramTemperature, OsdItem.GpuPower];
    private static readonly OsdItem[] MemoryOsdItems = [OsdItem.MemoryUtilization, OsdItem.MemoryTemperature];
    private static readonly OsdItem[] FansOsdItems = [OsdItem.CpuFan, OsdItem.GpuFan, OsdItem.PchTemperature, OsdItem.PchFan];
    private static readonly OsdItem[] StorageOsdItems = [OsdItem.Disk1Temperature, OsdItem.Disk2Temperature];

    public IDisposable Subscribe(TimeSpan interval, IEnumerable<SensorItem> items)
    {
        var scope = ComputeScopeFromSensorItems(items);
        if (scope == HardwareUpdateScope.None)
            return NoOpDisposable;
        return Subscribe(interval, scope);
    }

    public IDisposable Subscribe(TimeSpan interval, IEnumerable<OsdItem> items)
    {
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

    private static HardwareUpdateScope ComputeScopeFromSensorItems(IEnumerable<SensorItem> items)
    {
        var set = items as HashSet<SensorItem> ?? new HashSet<SensorItem>(items);
        return BuildHardwareUpdateScope(
            hasCpu: set.Overlaps(CpuSensorItems),
            hasGpu: set.Overlaps(GpuSensorItems),
            hasMemory: set.Overlaps(MemorySensorItems),
            hasFans: set.Overlaps(FansSensorItems),
            hasStorage: set.Overlaps(StorageSensorItems));
    }

    private static HardwareUpdateScope ComputeScopeFromOsdItems(IEnumerable<OsdItem> items)
    {
        var set = items as HashSet<OsdItem> ?? new HashSet<OsdItem>(items);
        return BuildHardwareUpdateScope(
            hasCpu: set.Overlaps(CpuOsdItems),
            hasGpu: set.Overlaps(GpuOsdItems),
            hasMemory: set.Overlaps(MemoryOsdItems),
            hasFans: set.Overlaps(FansOsdItems),
            hasStorage: set.Overlaps(StorageOsdItems));
    }

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

    private void GetHardware()
    {
        lock (_hardwareLock)
        {
            if (_hardwareInitialized) return;
            if (!PawnIOHelper.IsPawnIOInstalled()) return;

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
                _computer.Accept(new UpdateVisitor());

                foreach (var h in _computer.Hardware)
                {
                    try
                    {
                        h.Update();
                        _hardware.Add(h);
                    }
                    catch { /* Ignore */ }
                }
                RefreshSensorCache();
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

    private void RefreshSensorCache()
    {
        _cpuHardware = null;
        _amdGpuHardware = null;
        _gpuHardware = null;
        _memoryHardware = null;
        _cpuTempSensor = null;
        _cpuUsageSensor = null;
        _gpuUsageSensor = null;
        _gpuTempSensor = null;
        _gpuClockSensor = null;

        _iGpuUsageSensor = null;
        _iGpuTempSensor = null;
        _iGpuClockSensor = null;
        _iGpuPowerSensor = null;

        _gpuD3DVramUsedSensor = null;
        _gpuVramTotalSensor = null;
        _cachedGpuVramTotal = INVALID_VALUE_FLOAT;

        _iGpuD3DVramUsedSensor = null;
        _iGpuVramTotalSensor = null;
        _cachedIGpuVramTotal = INVALID_VALUE_FLOAT;

        _pCoreClockSensors.Clear();
        _eCoreClockSensors.Clear();
        _cpuCoreClockSensors.Clear();
        _memoryTempSensors.Clear();
        _storageTempSensors.Clear();

        _cpuPackagePowerSensor = null;
        _gpuPowerSensor = null;
        _gpuHotspotSensor = null;
        _memoryLoadSensor = null;
        _memoryUsedSensor = null;
        _memoryAvailableSensor = null;
        _cachedMemoryTotal = INVALID_VALUE_FLOAT;

        IsHybrid = false;

        _cpuHardware = _hardware.FirstOrDefault(h => h.HardwareType == HardwareType.Cpu);
        _amdGpuHardware = _hardware.FirstOrDefault(h => h.HardwareType == HardwareType.GpuAmd && !Regex.IsMatch(h.Name, REGEX_AMD_GPU_INTEGRATED, RegexOptions.IgnoreCase));
        _iGpuHardware = _hardware.FirstOrDefault(h => h.HardwareType == HardwareType.GpuIntel || (h.HardwareType == HardwareType.GpuAmd && Regex.IsMatch(h.Name, REGEX_AMD_GPU_INTEGRATED, RegexOptions.IgnoreCase)));
        _gpuHardware = _hardware.FirstOrDefault(h => h.HardwareType == HardwareType.GpuNvidia);
        _memoryHardware = _hardware.FirstOrDefault(h => h is { HardwareType: HardwareType.Memory, Name: SENSOR_NAME_TOTAL_MEMORY });

        if (_cpuHardware?.Sensors != null)
        {
            foreach (var s in _cpuHardware.Sensors)
            {
                switch (s.SensorType)
                {
                    case SensorType.Temperature when s.Name.Contains(SENSOR_NAME_PACKAGE):
                        _cpuTempSensor = s;
                        break;
                    case SensorType.Load when s.Name.Contains("Total"):
                        _cpuUsageSensor = s;
                        break;
                    case SensorType.Clock when s.Name.Contains("P-Core"):
                        _pCoreClockSensors.Add(s);
                        break;
                    case SensorType.Clock when s.Name.Contains("E-Core"):
                        _eCoreClockSensors.Add(s);
                        break;
                    case SensorType.Clock when s.Name.Contains("Core", StringComparison.OrdinalIgnoreCase) && !s.Name.Contains("Average") && !s.Name.Contains("Effective"):
                        _cpuCoreClockSensors.Add(s);
                        break;
                    case SensorType.Power when s.Name.Contains(SENSOR_NAME_PACKAGE):
                        _cpuPackagePowerSensor = s;
                        break;
                }
            }
            IsHybrid = _pCoreClockSensors.Count > 0;
            _cpuTempSensor ??= _cpuHardware.Sensors.FirstOrDefault(s => s.SensorType == SensorType.Temperature);
            _cpuUsageSensor ??= _cpuHardware.Sensors.FirstOrDefault(s => s.SensorType == SensorType.Load);
        }

        var mainGpu = _gpuHardware ?? _amdGpuHardware;
        if (mainGpu?.Sensors != null)
        {
            foreach (var s in mainGpu.Sensors)
            {
                switch (s.SensorType)
                {
                    case SensorType.Load when s.Name.Contains("Core") || s.Name.Contains("Utilization"):
                        _gpuUsageSensor = s;
                        break;
                    case SensorType.Temperature when s.Name.Contains("Core"):
                        _gpuTempSensor = s;
                        break;
                    case SensorType.Clock when s.Name.Contains("Core"):
                        _gpuClockSensor = s;
                        break;
                    case SensorType.Power:
                        _gpuPowerSensor = s;
                        break;
                    case SensorType.Temperature when s.Name.Contains(SENSOR_NAME_GPU_HOTSPOT, StringComparison.OrdinalIgnoreCase):
                        _gpuHotspotSensor = s;
                        break;
                    case SensorType.SmallData when s.Name.Contains("D3D Dedicated Memory Used", StringComparison.OrdinalIgnoreCase):
                        _gpuD3DVramUsedSensor = s;
                        break;
                    case SensorType.SmallData when s.Name.Contains("GPU Memory Total", StringComparison.OrdinalIgnoreCase):
                        _gpuVramTotalSensor = s;
                        break;
                }
            }
            _gpuUsageSensor ??= mainGpu.Sensors.FirstOrDefault(s => s.SensorType == SensorType.Load);
            _gpuTempSensor ??= mainGpu.Sensors.FirstOrDefault(s => s.SensorType == SensorType.Temperature);
            _gpuClockSensor ??= mainGpu.Sensors.FirstOrDefault(s => s.SensorType == SensorType.Clock);
            _gpuD3DVramUsedSensor ??= mainGpu.Sensors.FirstOrDefault(s => s.SensorType == SensorType.SmallData && s.Name.Contains("Used", StringComparison.OrdinalIgnoreCase));
            _gpuVramTotalSensor ??= mainGpu.Sensors.FirstOrDefault(s => s.SensorType == SensorType.SmallData && s.Name.Contains("Total", StringComparison.OrdinalIgnoreCase));
        }

        if (_iGpuHardware?.Sensors != null)
        {
            foreach (var s in _iGpuHardware.Sensors)
            {
                switch (s.SensorType)
                {
                    case SensorType.Load when s.Name.Contains("Core") || s.Name.Contains("Utilization"):
                        _iGpuUsageSensor = s;
                        break;
                    case SensorType.Temperature when s.Name.Contains("Core"):
                        _iGpuTempSensor = s;
                        break;
                    case SensorType.Clock when s.Name.Contains("Core"):
                        _iGpuClockSensor = s;
                        break;
                    case SensorType.Power:
                        _iGpuPowerSensor = s;
                        break;
                    case SensorType.SmallData when s.Name.Contains("D3D Dedicated Memory Used", StringComparison.OrdinalIgnoreCase):
                        _iGpuD3DVramUsedSensor = s;
                        break;
                    case SensorType.SmallData when s.Name.Contains("GPU Memory Total", StringComparison.OrdinalIgnoreCase):
                        _iGpuVramTotalSensor = s;
                        break;
                }
            }
            _iGpuUsageSensor ??= _iGpuHardware.Sensors.FirstOrDefault(s => s.SensorType == SensorType.Load);
            _iGpuTempSensor ??= _iGpuHardware.Sensors.FirstOrDefault(s => s.SensorType == SensorType.Temperature);
            _iGpuClockSensor ??= _iGpuHardware.Sensors.FirstOrDefault(s => s.SensorType == SensorType.Clock);
            _iGpuD3DVramUsedSensor ??= _iGpuHardware.Sensors.FirstOrDefault(s => s.SensorType == SensorType.SmallData && s.Name.Contains("Used", StringComparison.OrdinalIgnoreCase));
            _iGpuVramTotalSensor ??= _iGpuHardware.Sensors.FirstOrDefault(s => s.SensorType == SensorType.SmallData && s.Name.Contains("Total", StringComparison.OrdinalIgnoreCase));
        }

        _memoryLoadSensor = _memoryHardware?.Sensors?.FirstOrDefault(s => s.SensorType == SensorType.Load);
        _memoryUsedSensor = _memoryHardware?.Sensors?.FirstOrDefault(s => s.SensorType == SensorType.Data && s.Name.Contains(SENSOR_NAME_MEMORY_USED, StringComparison.OrdinalIgnoreCase));
        _memoryAvailableSensor = _memoryHardware?.Sensors?.FirstOrDefault(s => s.SensorType == SensorType.Data && s.Name.Contains(SENSOR_NAME_MEMORY_AVAILABLE, StringComparison.OrdinalIgnoreCase));

        foreach (var hw in _hardware.Where(h => h.HardwareType == HardwareType.Memory))
        {
            if (hw.Sensors == null) continue;
            _memoryTempSensors.AddRange(hw.Sensors.Where(s => s.SensorType == SensorType.Temperature && s.Name.Contains("DIMM")));
        }

        foreach (var storage in _hardware.Where(h => h.HardwareType == HardwareType.Storage))
        {
            var temp = storage.Sensors?.FirstOrDefault(s => s.SensorType == SensorType.Temperature);
            if (temp != null) _storageTempSensors.Add(temp);
        }
    }

    public Task<float> GetCpuTemperatureAsync() => Task.FromResult(Snapshot.CpuTemp);
    public Task<float> GetCpuUsageAsync() => Task.FromResult(Snapshot.CpuUsage);
    public Task<float> GetGpuUsageAsync() => Task.FromResult(Snapshot.GpuUsage);
    public Task<float> GetGpuTemperatureAsync() => Task.FromResult(Snapshot.GpuTemp);
    public Task<float> GetGpuCoreClockAsync() => Task.FromResult(Snapshot.GpuClock);

    public Task<string> GetCpuNameAsync()
    {
        lock (_configLock)
        {
            if (_isResetting || !IsLibreHardwareMonitorInitialized() || _cpuHardware == null)
                return Task.FromResult(UNKNOWN_NAME);

            if (!string.IsNullOrEmpty(_cachedCpuName))
                return Task.FromResult(_cachedCpuName);

            _cachedCpuName = StripName(_cpuHardware.Name);
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

            var dGpu = _gpuHardware ?? _amdGpuHardware;
            var forceIgpu = !SelectedGpuIsIgpu && (dGpu == null || !_isDgpuConnected);
            var gpu = (SelectedGpuIsIgpu || forceIgpu) ? _iGpuHardware : dGpu;
            _cachedGpuName = gpu != null ? StripName(gpu.Name) : UNKNOWN_NAME;
            _needRefreshGpuHardware = false;
            return Task.FromResult(_cachedGpuName);
        }
    }

    public Task<float> GetCpuPowerAsync() => Task.FromResult(Snapshot.CpuPower);
    public Task<float> GetCpuCoreClockAsync() => Task.FromResult(_showAverageCpuFrequency ? Snapshot.CpuAvgClock : Snapshot.CpuMaxClock);
    public Task<float> GetCpuPCoreClockAsync() => Task.FromResult(_showAverageCpuFrequency ? Snapshot.CpuPAvgClock : Snapshot.CpuPClock);
    public Task<float> GetCpuECoreClockAsync() => Task.FromResult(_showAverageCpuFrequency ? Snapshot.CpuEAvgClock : Snapshot.CpuEClock);
    public Task<float> GetGpuPowerAsync() => Task.FromResult(Snapshot.GpuPower);
    public Task<float> GetGpuVramTemperatureAsync() => Task.FromResult(Snapshot.GpuVramTemp);
    public Task<float> GetGpuVramUtilizationAsync() => Task.FromResult(Snapshot.GpuVramUtilization);
    public Task<float> GetGpuVramUsedAsync() => Task.FromResult(Snapshot.GpuVramUsed);
    public Task<float> GetGpuVramTotalAsync() => Task.FromResult(Snapshot.GpuVramTotal);
    public Task<(float, float)> GetSsdTemperaturesAsync() => Task.FromResult(Snapshot.SsdTemps);
    public Task<float> GetMemoryUsageAsync() => Task.FromResult(Snapshot.MemUsage);
    public Task<float> GetMemoryUsedAsync() => Task.FromResult(Snapshot.MemUsed);
    public Task<float> GetMemoryTotalAsync() => Task.FromResult(Snapshot.MemTotal);
    public Task<double> GetHighestMemoryTemperatureAsync() => Task.FromResult(Snapshot.MemMaxTemp);

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
            {
                return;
            }

            var gpuState = await _gpuController.GetLastKnownStateAsync().ConfigureAwait(false);
            bool gpuInactive = IsGpuInActive(gpuState);

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

                        float cpuTemp = _cpuTempSensor?.Value ?? INVALID_VALUE_FLOAT;
                        float cpuUsage = _cpuUsageSensor?.Value ?? INVALID_VALUE_FLOAT;

                        float cpuMaxClock = INVALID_VALUE_FLOAT;
                        float cpuAvgClock = INVALID_VALUE_FLOAT;
                        if (_cpuCoreClockSensors.Count > 0)
                        {
                            cpuMaxClock = _cpuCoreClockSensors.Max(s => s.Value) ?? INVALID_VALUE_FLOAT;
                            cpuAvgClock = _cpuCoreClockSensors.Average(s => s.Value) ?? INVALID_VALUE_FLOAT;
                        }

                        float pClock = INVALID_VALUE_FLOAT;
                        float pAvgClock = INVALID_VALUE_FLOAT;
                        float eClock = INVALID_VALUE_FLOAT;
                        float eAvgClock = INVALID_VALUE_FLOAT;
                        if (IsHybrid)
                        {
                            float pMax = _pCoreClockSensors.Count > 0 ? (_pCoreClockSensors.Max(s => s.Value) ?? INVALID_VALUE_FLOAT) : INVALID_VALUE_FLOAT;
                            float eMax = _eCoreClockSensors.Count > 0 ? (_eCoreClockSensors.Max(s => s.Value) ?? INVALID_VALUE_FLOAT) : INVALID_VALUE_FLOAT;
                            pClock = pMax > 0 ? (float)Math.Round(pMax) : pMax;
                            eClock = eMax > 0 ? (float)Math.Round(eMax) : eMax;

                            float pAvg = _pCoreClockSensors.Count > 0 ? (_pCoreClockSensors.Average(s => s.Value) ?? INVALID_VALUE_FLOAT) : INVALID_VALUE_FLOAT;
                            float eAvg = _eCoreClockSensors.Count > 0 ? (_eCoreClockSensors.Average(s => s.Value) ?? INVALID_VALUE_FLOAT) : INVALID_VALUE_FLOAT;
                            pAvgClock = pAvg > 0 ? (float)Math.Round(pAvg) : pAvg;
                            eAvgClock = eAvg > 0 ? (float)Math.Round(eAvg) : eAvg;
                        }

                        float cpuPower = INVALID_VALUE_FLOAT;
                        if (_cpuPackagePowerSensor != null)
                        {
                            float pVal = _cpuPackagePowerSensor.Value ?? INVALID_VALUE_FLOAT;
                            if (pVal > MAX_VALID_CPU_POWER) { Task.Run(ResetSensors); }
                            else if (pVal <= MIN_VALID_POWER_READING) { }
                            else
                            {
                                if (Math.Abs(pVal - _cachedCpuPower) < float.Epsilon)
                                {
                                    if (++_cachedCpuPowerTime >= MAX_CPU_POWER_STUCK_RETRIES) { Task.Run(ResetSensors); }
                                    else cpuPower = pVal;
                                }
                                else { _cachedCpuPower = pVal; _cachedCpuPowerTime = 0; cpuPower = pVal; }
                            }
                        }

                        float gpuUsage = INVALID_VALUE_FLOAT;
                        float gpuTemp = INVALID_VALUE_FLOAT;
                        float gpuClock = INVALID_VALUE_FLOAT;
                        float gpuPower = INVALID_VALUE_FLOAT;
                        float gpuVramTemp = INVALID_VALUE_FLOAT;
                        float gpuVramUtilization = INVALID_VALUE_FLOAT;
                        float gpuVramUsed = INVALID_VALUE_FLOAT;
                        float gpuVramTotal = INVALID_VALUE_FLOAT;

                        var dGpu = _gpuHardware ?? _amdGpuHardware;
                        var forceIgpu = !SelectedGpuIsIgpu && (dGpu == null || !_isDgpuConnected);

                        if (SelectedGpuIsIgpu || forceIgpu)
                        {
                            if (_cachedIGpuVramTotal <= 0 && _iGpuVramTotalSensor != null)
                                _cachedIGpuVramTotal = _iGpuVramTotalSensor.Value ?? INVALID_VALUE_FLOAT;

                            float igpuVramUsageRaw = _iGpuD3DVramUsedSensor?.Value ?? INVALID_VALUE_FLOAT;
                            gpuVramUsed = igpuVramUsageRaw > 0 ? igpuVramUsageRaw / MB_PER_GB : igpuVramUsageRaw;
                            gpuVramTotal = _cachedIGpuVramTotal > 0 ? _cachedIGpuVramTotal / MB_PER_GB : INVALID_VALUE_FLOAT;

                            if (igpuVramUsageRaw != INVALID_VALUE_FLOAT && _cachedIGpuVramTotal > 0)
                                gpuVramUtilization = (igpuVramUsageRaw / _cachedIGpuVramTotal) * 100f;

                            gpuPower = _iGpuPowerSensor?.Value ?? INVALID_VALUE_FLOAT;
                            gpuUsage = _iGpuUsageSensor?.Value ?? INVALID_VALUE_FLOAT;
                            gpuTemp = _iGpuTempSensor?.Value ?? INVALID_VALUE_FLOAT;
                            gpuClock = _iGpuClockSensor?.Value ?? INVALID_VALUE_FLOAT;
                        }
                        else if (!gpuInactive)
                        {
                            if (_cachedGpuVramTotal <= 0 && _gpuVramTotalSensor != null)
                                _cachedGpuVramTotal = _gpuVramTotalSensor.Value ?? INVALID_VALUE_FLOAT;

                            float dgpuVramUsageRaw = _gpuD3DVramUsedSensor?.Value ?? INVALID_VALUE_FLOAT;
                            gpuVramUsed = dgpuVramUsageRaw > 0 ? dgpuVramUsageRaw / MB_PER_GB : dgpuVramUsageRaw;
                            gpuVramTotal = _cachedGpuVramTotal > 0 ? _cachedGpuVramTotal / MB_PER_GB : INVALID_VALUE_FLOAT;

                            if (dgpuVramUsageRaw != INVALID_VALUE_FLOAT && _cachedGpuVramTotal > 0)
                                gpuVramUtilization = (dgpuVramUsageRaw / _cachedGpuVramTotal) * 100f;

                            float gPowerRaw = _gpuPowerSensor?.Value ?? INVALID_VALUE_FLOAT;
                            gpuPower = gPowerRaw > MIN_ACTIVE_GPU_POWER ? gPowerRaw : INVALID_VALUE_FLOAT;
                            gpuVramTemp = _gpuHotspotSensor?.Value ?? INVALID_VALUE_FLOAT;
                            gpuUsage = _gpuUsageSensor?.Value ?? INVALID_VALUE_FLOAT;
                            gpuTemp = _gpuTempSensor?.Value ?? INVALID_VALUE_FLOAT;
                            gpuClock = _gpuClockSensor?.Value ?? INVALID_VALUE_FLOAT;
                        }

                        float memUsage = _memoryLoadSensor?.Value ?? INVALID_VALUE_FLOAT;
                        float memUsed = _memoryUsedSensor?.Value ?? INVALID_VALUE_FLOAT;
                        float memTotal = (_memoryUsedSensor?.Value ?? 0) + (_memoryAvailableSensor?.Value ?? 0);
                        if (memTotal <= 0) memTotal = INVALID_VALUE_FLOAT;

                        if (memUsed >= 0 && memTotal > 0)
                        {
                            if (memUsage < 0) memUsage = (memUsed / memTotal) * 100f;
                        }
                        else if (_cachedMemoryTotal > 0 && memUsage >= 0)
                        {
                            memTotal = _cachedMemoryTotal;
                            memUsed = (memUsage / 100f) * memTotal;
                        }

                        double memMaxTemp = _memoryTempSensors.Count > 0 ? (double)(_memoryTempSensors.Max(s => s.Value) ?? 0) : INVALID_VALUE_DOUBLE;
                        var ssdTemps = (INVALID_VALUE_FLOAT, INVALID_VALUE_FLOAT);
                        if (Includes(scope, HardwareUpdateScope.Storage))
                        {
                            float t1 = _storageTempSensors.Count > 0 ? _storageTempSensors[0].Value ?? INVALID_VALUE_FLOAT : INVALID_VALUE_FLOAT;
                            float t2 = _storageTempSensors.Count > 1 ? _storageTempSensors[1].Value ?? INVALID_VALUE_FLOAT : INVALID_VALUE_FLOAT;
                            ssdTemps = (t1, t2);
                        }

                        Snapshot = new HardwareSensorSnapshot
                        {
                            CpuTemp = cpuTemp,
                            CpuUsage = cpuUsage,
                            CpuPower = cpuPower,
                            CpuMaxClock = cpuMaxClock,
                            CpuAvgClock = cpuAvgClock,
                            CpuPClock = pClock,
                            CpuPAvgClock = pAvgClock,
                            CpuEClock = eClock,
                            CpuEAvgClock = eAvgClock,
                            GpuUsage = gpuUsage,
                            GpuTemp = gpuTemp,
                            GpuClock = gpuClock,
                            GpuPower = gpuPower,
                            GpuVramTemp = gpuVramTemp,
                            GpuVramUtilization = gpuVramUtilization,
                            GpuVramUsed = gpuVramUsed,
                            GpuVramTotal = gpuVramTotal,
                            MemUsage = memUsage,
                            MemUsed = memUsed,
                            MemTotal = memTotal,
                            MemMaxTemp = memMaxTemp,
                            SsdTemps = ssdTemps
                        };

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
            _activeUpdateTask = null;
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
                _computer.Accept(new UpdateVisitor());

                foreach (var h in _computer.Hardware)
                {
                    try
                    {
                        h.Update();
                        _hardware.Add(h);
                    }
                    catch
                    {
                    }
                }
                RefreshSensorCache();
            }
        }
        finally { _isResetting = false; }
    }

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

    public bool IsGpuInActive(GPUState state) => state is GPUState.Inactive or GPUState.PoweredOff or GPUState.Unknown or GPUState.NvidiaGpuNotFound;
    public bool IsLibreHardwareMonitorInitialized() => InitialState is LibreHardwareMonitorInitialState.Initialized or LibreHardwareMonitorInitialState.Success;

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

    public static HardwareUpdateScope BuildHardwareUpdateScope(bool hasCpu, bool hasGpu, bool hasMemory, bool hasFans, bool hasStorage)
    {
        var scope = HardwareUpdateScope.None;

        if (hasCpu)
        {
            scope |= HardwareUpdateScope.Cpu;
        }

        if (hasGpu)
        {
            scope |= HardwareUpdateScope.Gpu;
        }

        if (hasMemory)
        {
            scope |= HardwareUpdateScope.Memory;
        }

        if (hasFans)
        {
            scope |= HardwareUpdateScope.Fans;
        }

        if (hasStorage)
        {
            scope |= HardwareUpdateScope.Storage;
        }

        return scope;
    }

    private static bool Includes(HardwareUpdateScope scope, HardwareUpdateScope flag) => (scope & flag) != 0;

    public void Dispose()
    {
        lock (_hardwareLock) { _computer?.Close(); _computer = null; _hardwareInitialized = false; }
        _initSemaphore.Dispose();
        GC.SuppressFinalize(this);
    }
}
