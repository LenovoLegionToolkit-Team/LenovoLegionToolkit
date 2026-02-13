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
using LenovoLegionToolkit.WPF.Utils;
using LibreHardwareMonitor.Hardware;

namespace LenovoLegionToolkit.Lib.Controllers.Sensors;

public class SensorsGroupController : IDisposable
{
    #region Constants (Magic Words & Numbers)

    private const float INVALID_VALUE_FLOAT = -1f;
    private const double INVALID_VALUE_DOUBLE = 0.0;
    private const string UNKNOWN_NAME = "UNKNOWN";

    private const string SENSOR_NAME_TOTAL_MEMORY = "Total Memory";
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

    #endregion

    private bool _initialized;
    public LibreHardwareMonitorInitialState InitialState { get; private set; }
    public bool IsHybrid { get; private set; }

    private float _lastGpuPower;
    private readonly SemaphoreSlim _initSemaphore = new(1, 1);

    private readonly List<IHardware> _hardware = [];

    private Computer? _computer;
    private IHardware? _cpuHardware;
    private IHardware? _amdGpuHardware;
    private IHardware? _gpuHardware;
    private IHardware? _memoryHardware;

    private ISensor? _cpuTempSensor;
    private ISensor? _cpuUsageSensor;
    private ISensor? _gpuUsageSensor;
    private ISensor? _gpuTempSensor;
    private ISensor? _gpuClockSensor;

    private readonly List<ISensor> _pCoreClockSensors = [];
    private readonly List<ISensor> _eCoreClockSensors = [];
    private ISensor? _cpuPackagePowerSensor;
    private readonly List<ISensor> _cpuCoreClockSensors = [];

    private ISensor? _gpuPowerSensor;
    private ISensor? _gpuHotspotSensor;

    private ISensor? _memoryLoadSensor;
    private readonly List<ISensor> _memoryTempSensors = [];
    private readonly List<ISensor> _storageTempSensors = [];

    private volatile bool _isResetting;
    private bool _needRefreshGpuHardware;

    private string _cachedCpuName = string.Empty;
    private string _cachedGpuName = string.Empty;

    private float _cachedCpuPower;
    private int _cachedCpuPowerTime;

    private readonly Lock _hardwareLock = new();
    private readonly Lock _dataLock = new();
    private volatile bool _hardwareInitialized;

    private readonly Dictionary<object, TimeSpan> _subscribers = [];
    private CancellationTokenSource? _producerCts;
    private Task? _producerTask;
    public event EventHandler? SensorsUpdated;

    private readonly GPUController _gpuController = IoCContainer.Resolve<GPUController>();

    private float _snapshotCpuTemp = INVALID_VALUE_FLOAT;
    private float _snapshotCpuUsage = INVALID_VALUE_FLOAT;
    private float _snapshotCpuPower = INVALID_VALUE_FLOAT;
    private float _snapshotCpuMaxClock = INVALID_VALUE_FLOAT;
    private float _snapshotCpuPClock = INVALID_VALUE_FLOAT;
    private float _snapshotCpuEClock = INVALID_VALUE_FLOAT;
    private float _snapshotGpuUsage = INVALID_VALUE_FLOAT;
    private float _snapshotGpuTemp = INVALID_VALUE_FLOAT;
    private float _snapshotGpuClock = INVALID_VALUE_FLOAT;
    private float _snapshotGpuPower = INVALID_VALUE_FLOAT;
    private float _snapshotGpuVramTemp = INVALID_VALUE_FLOAT;
    private float _snapshotMemUsage = INVALID_VALUE_FLOAT;
    private double _snapshotMemMaxTemp = INVALID_VALUE_DOUBLE;
    private (float, float) _snapshotSsdTemps = (INVALID_VALUE_FLOAT, INVALID_VALUE_FLOAT);

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
            if (!PawnIOHelper.IsPawnIOInnstalled()) return;

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
                _hardware.AddRange(_computer.Hardware);
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

        _pCoreClockSensors.Clear();
        _eCoreClockSensors.Clear();
        _cpuCoreClockSensors.Clear();
        _memoryTempSensors.Clear();
        _storageTempSensors.Clear();

        _cpuPackagePowerSensor = null;
        _gpuPowerSensor = null;
        _gpuHotspotSensor = null;
        _memoryLoadSensor = null;

        IsHybrid = false;

        _cpuHardware = _hardware.FirstOrDefault(h => h.HardwareType == HardwareType.Cpu);
        _amdGpuHardware = _hardware.FirstOrDefault(h => h.HardwareType == HardwareType.GpuAmd && !Regex.IsMatch(h.Name, REGEX_AMD_GPU_INTEGRATED, RegexOptions.IgnoreCase));
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
                }
            }
            _gpuUsageSensor ??= mainGpu.Sensors.FirstOrDefault(s => s.SensorType == SensorType.Load);
            _gpuTempSensor ??= mainGpu.Sensors.FirstOrDefault(s => s.SensorType == SensorType.Temperature);
            _gpuClockSensor ??= mainGpu.Sensors.FirstOrDefault(s => s.SensorType == SensorType.Clock);
        }

        _memoryLoadSensor = _memoryHardware?.Sensors?.FirstOrDefault(s => s.SensorType == SensorType.Load);

        foreach (var hw in _hardware.Where(h => h.HardwareType == HardwareType.Memory))
        {
            if (hw.Sensors == null) continue;
            _memoryTempSensors.AddRange(hw.Sensors.Where(s => s.SensorType == SensorType.Temperature));
        }

        foreach (var storage in _hardware.Where(h => h.HardwareType == HardwareType.Storage))
        {
            var temp = storage.Sensors?.FirstOrDefault(s => s.SensorType == SensorType.Temperature);
            if (temp != null) _storageTempSensors.Add(temp);
        }
    }

    public Task<float> GetCpuTemperatureAsync()
    {
        lock (_dataLock) return Task.FromResult(_snapshotCpuTemp);
    }

    public Task<float> GetCpuUsageAsync()
    {
        lock (_dataLock) return Task.FromResult(_snapshotCpuUsage);
    }

    public Task<float> GetGpuUsageAsync()
    {
        lock (_dataLock) return Task.FromResult(_snapshotGpuUsage);
    }

    public Task<float> GetGpuTemperatureAsync()
    {
        lock (_dataLock) return Task.FromResult(_snapshotGpuTemp);
    }

    public Task<float> GetGpuCoreClockAsync()
    {
        lock (_dataLock) return Task.FromResult(_snapshotGpuClock);
    }

    public Task<string> GetCpuNameAsync()
    {
        lock (_dataLock)
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
        lock (_dataLock)
        {
            if (_isResetting || !IsLibreHardwareMonitorInitialized())
                return Task.FromResult(UNKNOWN_NAME);

            if (!string.IsNullOrEmpty(_cachedGpuName) && !_needRefreshGpuHardware)
                return Task.FromResult(_cachedGpuName);

            var gpu = _gpuHardware ?? _amdGpuHardware;
            _cachedGpuName = gpu != null ? StripName(gpu.Name) : UNKNOWN_NAME;
            _needRefreshGpuHardware = false;
            return Task.FromResult(_cachedGpuName);
        }
    }

    public Task<float> GetCpuPowerAsync()
    {
        lock (_dataLock) return Task.FromResult(_snapshotCpuPower);
    }

    public Task<float> GetCpuCoreClockAsync()
    {
        lock (_dataLock) return Task.FromResult(_snapshotCpuMaxClock);
    }

    public Task<float> GetCpuPCoreClockAsync()
    {
        lock (_dataLock) return Task.FromResult(_snapshotCpuPClock);
    }

    public Task<float> GetCpuECoreClockAsync()
    {
        lock (_dataLock) return Task.FromResult(_snapshotCpuEClock);
    }

    // Previously, GPU activity was inferred from cached power values.
    // This caused incorrect hiding/showing of sensors on power-gated systems.
    // GPU state is now determined explicitly via GPUController.
    //
    // GPU power readings are only meaningful when the discrete GPU is actually active.
    // When the GPU is power-gated, some drivers still expose stale or zero values.
    // We intentionally hide power readings in inactive states to avoid misleading data.
    public Task<float> GetGpuPowerAsync()
    {
        lock (_dataLock) return Task.FromResult(_snapshotGpuPower);
    }

    // VRAM (memory junction) temperature is only reported reliably when the dGPU is active.
    // If the GPU is inactive, exposed values may be stale or undefined.
    public Task<float> GetGpuVramTemperatureAsync()
    {
        lock (_dataLock) return Task.FromResult(_snapshotGpuVramTemp);
    }

    public Task<(float, float)> GetSsdTemperaturesAsync()
    {
        lock (_dataLock) return Task.FromResult(_snapshotSsdTemps);
    }

    public Task<float> GetMemoryUsageAsync()
    {
        lock (_dataLock) return Task.FromResult(_snapshotMemUsage);
    }

    public Task<double> GetHighestMemoryTemperatureAsync()
    {
        lock (_dataLock) return Task.FromResult(_snapshotMemMaxTemp);
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
            catch { /* Ignore */ }

            _needRefreshGpuHardware = true;
        }
    }

    public async Task UpdateAsync()
    {
        if (_isResetting || !IsLibreHardwareMonitorInitialized()) return;

        var gpuState = await _gpuController.GetLastKnownStateAsync().ConfigureAwait(false);
        bool gpuInactive = IsGpuInActive(gpuState);

        await Task.Run(() =>
        {
            lock (_hardwareLock)
            {
                if (_isResetting || _computer == null || !_hardwareInitialized) return;
                try
                {
                    foreach (var h in _hardware)
                    {
                        if (h == null) continue;

                        if (gpuInactive && h.HardwareType == HardwareType.GpuNvidia)
                        {
                            continue;
                        }

                        h.Update();
                    }

                    lock (_dataLock)
                    {
                        _snapshotCpuTemp = _cpuTempSensor?.Value ?? INVALID_VALUE_FLOAT;
                        _snapshotCpuUsage = _cpuUsageSensor?.Value ?? INVALID_VALUE_FLOAT;
                        _snapshotCpuMaxClock = _cpuCoreClockSensors.Count > 0 ? (_cpuCoreClockSensors.Max(s => s.Value) ?? INVALID_VALUE_FLOAT) : INVALID_VALUE_FLOAT;

                        if (IsHybrid)
                        {
                            float pMax = _pCoreClockSensors.Count > 0 ? (_pCoreClockSensors.Max(s => s.Value) ?? INVALID_VALUE_FLOAT) : INVALID_VALUE_FLOAT;
                            float eMax = _eCoreClockSensors.Count > 0 ? (_eCoreClockSensors.Max(s => s.Value) ?? INVALID_VALUE_FLOAT) : INVALID_VALUE_FLOAT;
                            _snapshotCpuPClock = pMax > 0 ? (float)Math.Round(pMax) : pMax;
                            _snapshotCpuEClock = eMax > 0 ? (float)Math.Round(eMax) : eMax;
                        }

                        if (_cpuPackagePowerSensor != null)
                        {
                            float pVal = _cpuPackagePowerSensor.Value ?? INVALID_VALUE_FLOAT;
                            if (pVal > MAX_VALID_CPU_POWER) { Task.Run(ResetSensors); _snapshotCpuPower = INVALID_VALUE_FLOAT; }
                            else if (pVal <= MIN_VALID_POWER_READING) { _snapshotCpuPower = INVALID_VALUE_FLOAT; }
                            else
                            {
                                if (Math.Abs(pVal - _cachedCpuPower) < float.Epsilon)
                                {
                                    if (++_cachedCpuPowerTime >= MAX_CPU_POWER_STUCK_RETRIES) { Task.Run(ResetSensors); _snapshotCpuPower = INVALID_VALUE_FLOAT; }
                                    else _snapshotCpuPower = pVal;
                                }
                                else { _cachedCpuPower = pVal; _cachedCpuPowerTime = 0; _snapshotCpuPower = pVal; }
                            }
                        }

                        _snapshotGpuUsage = _gpuUsageSensor?.Value ?? INVALID_VALUE_FLOAT;
                        _snapshotGpuTemp = _gpuTempSensor?.Value ?? INVALID_VALUE_FLOAT;
                        _snapshotGpuClock = _gpuClockSensor?.Value ?? INVALID_VALUE_FLOAT;

                        if (gpuInactive)
                        {
                            _snapshotGpuPower = INVALID_VALUE_FLOAT;
                            _snapshotGpuVramTemp = INVALID_VALUE_FLOAT;
                        }
                        else
                        {
                            float gPower = _gpuPowerSensor?.Value ?? INVALID_VALUE_FLOAT;
                            _lastGpuPower = gPower;
                            _snapshotGpuPower = _lastGpuPower > MIN_ACTIVE_GPU_POWER ? _lastGpuPower : INVALID_VALUE_FLOAT;
                            _snapshotGpuVramTemp = _gpuHotspotSensor?.Value ?? INVALID_VALUE_FLOAT;
                        }

                        _snapshotMemUsage = _memoryLoadSensor?.Value ?? INVALID_VALUE_FLOAT;
                        _snapshotMemMaxTemp = _memoryTempSensors.Count > 0 ? (double)(_memoryTempSensors.Max(s => s.Value) ?? 0) : INVALID_VALUE_DOUBLE;

                        float t1 = _storageTempSensors.Count > 0 ? _storageTempSensors[0].Value ?? INVALID_VALUE_FLOAT : INVALID_VALUE_FLOAT;
                        float t2 = _storageTempSensors.Count > 1 ? _storageTempSensors[1].Value ?? INVALID_VALUE_FLOAT : INVALID_VALUE_FLOAT;
                        _snapshotSsdTemps = (t1, t2);
                    }
                }
                catch (Exception ex) { if (ex is IndexOutOfRangeException) Task.Run(ResetSensors); }
            }
        }).ConfigureAwait(false);
    }

    private void ResetSensors()
    {
        _isResetting = true;
        try
        {
            lock (_hardwareLock)
            {
                _computer?.Close(); _hardware.Clear();
                _computer?.Open(); _computer?.Accept(new UpdateVisitor()); _computer?.Reset();
                if (_computer == null)
                {
                    return;
                }

                _hardware.AddRange(_computer.Hardware); RefreshSensorCache();
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

    public void Start(object subscriber, TimeSpan interval)
    {
        lock (_subscribers)
        {
            _subscribers[subscriber] = interval;
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

        // If loop is running, cancel it to pick up new interval or just restart
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
            lock (_subscribers)
            {
                if (_subscribers.Count == 0) return;
                minInterval = _subscribers.Values.Min();
            }

            try
            {
                await UpdateAsync().ConfigureAwait(false);
                SensorsUpdated?.Invoke(this, EventArgs.Empty);

                await Task.Delay(minInterval, token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                Log.Instance.Trace($"ProducerLoop error: {ex}");
                // Basic backoff
                await Task.Delay(1000, token).ConfigureAwait(false);
            }
        }
    }

    public void Dispose()
    {
        lock (_hardwareLock) { _computer?.Close(); _computer = null; _hardwareInitialized = false; }
        _initSemaphore.Dispose();
        GC.SuppressFinalize(this);
    }
}