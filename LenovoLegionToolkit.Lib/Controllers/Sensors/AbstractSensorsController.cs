using LenovoLegionToolkit.Lib.System;
using LenovoLegionToolkit.Lib.System.Management;
using LenovoLegionToolkit.Lib.Utils;
using NvAPIWrapper.Native;
using NvAPIWrapper.Native.GPU;
using System;
using System.Linq;
using System.Management;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Windows.Win32;
using Windows.Win32.System.Power;

namespace LenovoLegionToolkit.Lib.Controllers.Sensors;

public abstract class AbstractSensorsController(GPUController gpuController) : ISensorsController
{
    protected readonly struct GPUInfo(
        int utilization,
        int coreClock,
        int maxCoreClock,
        int memoryClock,
        int maxMemoryClock,
        int temperature,
        int maxTemperature)
    {
        public static readonly GPUInfo Empty = new(-1, -1, -1, -1, -1, -1, -1);

        public int Utilization { get; } = utilization;
        public int CoreClock { get; } = coreClock;
        public int MaxCoreClock { get; } = maxCoreClock;
        public int MemoryClock { get; } = memoryClock;
        public int MaxMemoryClock { get; } = maxMemoryClock;
        public int Temperature { get; } = temperature;
        public int MaxTemperature { get; } = maxTemperature;
    }

    #region P / Invoke for GetSystemTimes
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetSystemTimes(out FILETIME lpIdleTime, out FILETIME lpKernelTime, out FILETIME lpUserTime);

    [StructLayout(LayoutKind.Sequential)]
    private struct FILETIME
    {
        public uint dwLowDateTime;
        public uint dwHighDateTime;
    }

    private static ulong ToUInt64(FILETIME ft)
    {
        return ((ulong)ft.dwHighDateTime << 32) | ft.dwLowDateTime;
    }
    #endregion

    private readonly SafePerformanceCounter _percentProcessorPerformanceCounter = new("Processor Information", "% Processor Performance", "_Total");
    private readonly Lock _cpuCalcLock = new();

    private ulong _prevIdleTime;
    private ulong _prevKernelTime;
    private ulong _prevUserTime;

    private int _cachedCpuUtilization;
    private long _lastCpuCalculationTick;
    private const long MinUpdateIntervalTicks = 500 * 10000; // 500ms (1ms = 10,000 ticks)

    protected int? _cpuBaseClockCache;
    protected int? _cpuMaxCoreClockCache;
    protected int? _cpuMaxFanSpeedCache;
    protected int? _gpuMaxFanSpeedCache;
    protected int? _pchMaxFanSpeedCache;

    public abstract Task<bool> IsSupportedAsync();

    public async Task PrepareAsync()
    {
        _percentProcessorPerformanceCounter.Reset();
        _percentProcessorPerformanceCounter.NextValue();

        if (GetSystemTimes(out var idle, out var kernel, out var user))
        {
            _prevIdleTime = ToUInt64(idle);
            _prevKernelTime = ToUInt64(kernel);
            _prevUserTime = ToUInt64(user);
        }

        _lastCpuCalculationTick = DateTime.UtcNow.Ticks;

        await Task.Delay(500).ConfigureAwait(false);

        _percentProcessorPerformanceCounter.NextValue();

        GetCpuUtilization(100);
    }


    public virtual async Task<SensorsData> GetDataAsync()
    {
        const int genericMaxUtilization = 100;
        const int genericMaxTemperature = 100;

        var cpuUtilization = GetCpuUtilization(genericMaxUtilization);

        var cpuMaxCoreClock = _cpuMaxCoreClockCache ??= await GetCpuMaxCoreClockAsync().ConfigureAwait(false);
        var cpuCoreClock = GetCpuCoreClock();
        var cpuCurrentTemperature = await GetCpuCurrentTemperatureAsync().ConfigureAwait(false);
        var cpuCurrentFanSpeed = await GetCpuCurrentFanSpeedAsync().ConfigureAwait(false);
        var cpuMaxFanSpeed = _cpuMaxFanSpeedCache ??= await GetCpuMaxFanSpeedAsync().ConfigureAwait(false);

        var gpuInfo = await GetGPUInfoAsync().ConfigureAwait(false);
        var gpuCurrentTemperature = gpuInfo.Temperature >= 0 ? gpuInfo.Temperature : await GetGpuCurrentTemperatureAsync().ConfigureAwait(false);
        var gpuMaxTemperature = gpuInfo.MaxTemperature >= 0 ? gpuInfo.MaxTemperature : genericMaxTemperature;
        var gpuCurrentFanSpeed = await GetGpuCurrentFanSpeedAsync().ConfigureAwait(false);
        var gpuMaxFanSpeed = _gpuMaxFanSpeedCache ??= await GetGpuMaxFanSpeedAsync().ConfigureAwait(false);

        var pchCurrentTemperature = await GetPchCurrentTemperatureAsync().ConfigureAwait(false);
        var pchMaxTemperature = genericMaxTemperature;
        var pchCurrentFanSpeed = await GetPchCurrentFanSpeedAsync().ConfigureAwait(false);
        var pchMaxFanSpeed = _pchMaxFanSpeedCache ??= await GetPchMaxFanSpeedAsync().ConfigureAwait(false);

        var cpu = new SensorData(cpuUtilization,
            genericMaxUtilization,
            cpuCoreClock,
            cpuMaxCoreClock,
            -1,
            -1,
            cpuCurrentTemperature,
            genericMaxTemperature,
            cpuCurrentFanSpeed,
            cpuMaxFanSpeed);

        var gpu = new SensorData(gpuInfo.Utilization,
            genericMaxUtilization,
            gpuInfo.CoreClock,
            gpuInfo.MaxCoreClock,
            gpuInfo.MemoryClock,
            gpuInfo.MaxMemoryClock,
            gpuCurrentTemperature,
            gpuMaxTemperature,
            gpuCurrentFanSpeed,
            gpuMaxFanSpeed);

        var pch = new SensorData(
            -1,
            -1,
            -1,
            -1,
            -1,
            -1,
            pchCurrentTemperature,
            pchMaxTemperature,
            pchCurrentFanSpeed,
            pchMaxFanSpeed);

        var result = new SensorsData(cpu, gpu, pch);

        return result;
    }

    public async Task<FanSpeedTable> GetFanSpeedsAsync()
    {
        var cpuFanSpeed = await GetCpuCurrentFanSpeedAsync().ConfigureAwait(false);
        var gpuFanSpeed = await GetGpuCurrentFanSpeedAsync().ConfigureAwait(false);
        var pchFanSpeed = await GetPchCurrentFanSpeedAsync().ConfigureAwait(false);
        return new FanSpeedTable(cpuFanSpeed, gpuFanSpeed, pchFanSpeed);
    }

    protected abstract Task<int> GetCpuCurrentTemperatureAsync();

    protected abstract Task<int> GetGpuCurrentTemperatureAsync();

    protected virtual Task<int> GetPchCurrentTemperatureAsync() => Task.FromResult(-1);

    protected abstract Task<int> GetCpuCurrentFanSpeedAsync();

    protected abstract Task<int> GetGpuCurrentFanSpeedAsync();

    protected virtual Task<int> GetPchCurrentFanSpeedAsync() => Task.FromResult(-1);

    protected abstract Task<int> GetCpuMaxFanSpeedAsync();

    protected abstract Task<int> GetGpuMaxFanSpeedAsync();

    protected virtual Task<int> GetPchMaxFanSpeedAsync() => Task.FromResult(-1);

    protected static unsafe int GetCpuBaseClock()
    {
        var ptr = IntPtr.Zero;
        try
        {
            PInvoke.GetSystemInfo(out var systemInfo);

            var numberOfProcessors = Math.Min(32, (int)systemInfo.dwNumberOfProcessors);
            var infoSize = Marshal.SizeOf<PROCESSOR_POWER_INFORMATION>();
            var infosSize = numberOfProcessors * infoSize;

            ptr = Marshal.AllocHGlobal(infosSize);

            var result = PInvoke.CallNtPowerInformation(POWER_INFORMATION_LEVEL.ProcessorInformation,
                null,
                0,
                ptr.ToPointer(),
                (uint)infosSize);
            if (result != 0)
                return 0;

            var infos = new PROCESSOR_POWER_INFORMATION[numberOfProcessors];

            for (var i = 0; i < infos.Length; i++)
                infos[i] = Marshal.PtrToStructure<PROCESSOR_POWER_INFORMATION>(IntPtr.Add(ptr, i * infoSize));

            return (int)infos.Select(p => p.MaxMhz).Max();
        }
        finally
        {
            Marshal.FreeHGlobal(ptr);
        }
    }

    protected virtual async Task<int> GetCpuMaxCoreClockAsync()
    {
        try
        {
            return await WMI.LenovoGameZoneData.GetCPUFrequencyAsync().ConfigureAwait(false);
        }
        catch (ManagementException)
        {
            return GetCpuBaseClock();
        }
    }

    protected async Task<GPUInfo> GetGPUInfoAsync()
    {
        if (gpuController.IsSupported())
            await gpuController.StartAsync().ConfigureAwait(false);

        if (await gpuController.GetLastKnownStateAsync().ConfigureAwait(false) is GPUState.PoweredOff or GPUState.Unknown)
            return GPUInfo.Empty;

        try
        {
            NVAPI.Initialize();

            var gpu = NVAPI.GetGPU();
            if (gpu is null)
                return GPUInfo.Empty;

            var utilization = Math.Min(100, Math.Max(gpu.UsageInformation.GPU.Percentage, gpu.UsageInformation.VideoEngine.Percentage));

            var currentCoreClock = (int)gpu.CurrentClockFrequencies.GraphicsClock.Frequency / 1000;
            var currentMemoryClock = (int)gpu.CurrentClockFrequencies.MemoryClock.Frequency / 1000;

            var maxCoreClock = (int)gpu.BoostClockFrequencies.GraphicsClock.Frequency / 1000;
            var maxMemoryClock = (int)gpu.BoostClockFrequencies.MemoryClock.Frequency / 1000;

            switch (gpu.MemoryInformation.RAMType)
            {
                case GPUMemoryType.GDDR5:
                case GPUMemoryType.GDDR5X:
                    currentMemoryClock /= 2;
                    maxMemoryClock /= 2;
                    break;
                case GPUMemoryType.GDDR6:
                case GPUMemoryType.GDDR6X:
                    currentMemoryClock /= 4;
                    maxMemoryClock /= 4;
                    break;
                case GPUMemoryType.GDDR7:
                    currentMemoryClock /= 8;
                    maxMemoryClock /= 8;
                    break;
                default:
                    break;
            }

            var states = GPUApi.GetPerformanceStates20(gpu.Handle);
            var maxCoreClockOffset = states.Clocks[PerformanceStateId.P0_3DPerformance][0].FrequencyDeltaInkHz.DeltaValue / 1000;
            var maxMemoryClockOffset = states.Clocks[PerformanceStateId.P0_3DPerformance][1].FrequencyDeltaInkHz.DeltaValue / 1000;

            var temperatureSensor = gpu.ThermalInformation.ThermalSensors.FirstOrDefault();
            var currentTemperature = temperatureSensor?.CurrentTemperature ?? -1;
            var maxTemperature = temperatureSensor?.DefaultMaximumTemperature ?? -1;

            return new(utilization,
                currentCoreClock,
                maxCoreClock + maxCoreClockOffset,
                currentMemoryClock,
                maxMemoryClock + maxMemoryClockOffset,
                currentTemperature,
                maxTemperature);
        }
        catch
        {
            return GPUInfo.Empty;
        }
    }

    protected int GetCpuUtilization(int maxUtilization)
    {
        lock (_cpuCalcLock)
        {
            long currentTick = DateTime.UtcNow.Ticks;

            if (currentTick - _lastCpuCalculationTick < MinUpdateIntervalTicks)
            {
                return _cachedCpuUtilization;
            }

            if (!GetSystemTimes(out var idleHandle, out var kernelHandle, out var userHandle))
            {
                return _cachedCpuUtilization;
            }

            ulong currentIdle = ToUInt64(idleHandle);
            ulong currentKernel = ToUInt64(kernelHandle);
            ulong currentUser = ToUInt64(userHandle);

            ulong idleDelta = currentIdle - _prevIdleTime;
            ulong kernelDelta = currentKernel - _prevKernelTime;
            ulong userDelta = currentUser - _prevUserTime;

            ulong totalSys = kernelDelta + userDelta;

            if (totalSys > 0)
            {
                _prevIdleTime = currentIdle;
                _prevKernelTime = currentKernel;
                _prevUserTime = currentUser;
                _lastCpuCalculationTick = currentTick;

                var usagePercent = (double)(totalSys - idleDelta) / totalSys * 100.0;
                var rounded = (int)Math.Round(usagePercent);

                _cachedCpuUtilization = Math.Min(Math.Max(rounded, 0), maxUtilization);
            }

            return _cachedCpuUtilization;
        }
    }

    protected int GetCpuCoreClock()
    {
        var baseClock = _cpuBaseClockCache ??= GetCpuBaseClock();
        var perfValue = _percentProcessorPerformanceCounter.NextValue();
        if (float.IsNaN(perfValue) || perfValue <= 0)
            return -1;

        var clock = (int)(baseClock * (perfValue / 100f));
        if (clock < 1)
            return -1;
        return clock;
    }
}