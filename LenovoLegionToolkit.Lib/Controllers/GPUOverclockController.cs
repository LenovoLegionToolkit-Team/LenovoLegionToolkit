using System;
using System.Linq;
using System.Threading.Tasks;
using LenovoLegionToolkit.Lib.Features.Hybrid;
using LenovoLegionToolkit.Lib.Listeners;
using LenovoLegionToolkit.Lib.Settings;
using LenovoLegionToolkit.Lib.SoftwareDisabler;
using LenovoLegionToolkit.Lib.System;
using LenovoLegionToolkit.Lib.System.Management;
using LenovoLegionToolkit.Lib.Utils;
using NvAPIWrapper.GPU;
using NvAPIWrapper.Native;
using NvAPIWrapper.Native.GPU;
using NvAPIWrapper.Native.GPU.Structures;
using NvAPIWrapper.Native.Interfaces.GPU;

namespace LenovoLegionToolkit.Lib.Controllers;

public class GPUOverclockController
{
    private readonly GPUOverclockSettings _settings;
    private readonly VantageDisabler _vantageDisabler;
    private readonly LegionSpaceDisabler _legionSpaceDisabler;
    private readonly LegionZoneDisabler _legionZoneDisabler;
    private readonly SmartEngineDisabler _smartEngineDisabler;
    private readonly NativeWindowsMessageListener _nativeWindowsMessageListener;

    public event EventHandler? Changed;

    public GPUOverclockController(GPUOverclockSettings settings,
        VantageDisabler vantageDisabler,
        LegionSpaceDisabler legionSpaceDisabler,
        LegionZoneDisabler legionZoneDisabler,
        SmartEngineDisabler smartEngineDisabler,
        NativeWindowsMessageListener nativeWindowsMessageListener)
    {
        _settings = settings;
        _vantageDisabler = vantageDisabler;
        _legionSpaceDisabler = legionSpaceDisabler;
        _legionZoneDisabler = legionZoneDisabler;
        _smartEngineDisabler = smartEngineDisabler;
        _nativeWindowsMessageListener = nativeWindowsMessageListener;
        _nativeWindowsMessageListener.Changed += NativeWindowsMessageListenerOnChanged;
    }

    public async Task<bool> IsSupportedAsync()
    {
        if (AppFlags.Instance.Debug)
        {
            return true;
        }

        var isSupported = NVAPI.IsAvailable();

        Log.Instance.Trace($"NVAPI status: {isSupported}.");

        if (!isSupported)
            return isSupported;

        try
        {
            isSupported = await WMI.LenovoGameZoneData.IsSupportGpuOCAsync().ConfigureAwait(false) > 0;

            if (!isSupported)
            {
                Log.Instance.Trace($"Clearing settings...");

                _settings.Store.Enabled = false;
                _settings.Store.Info = GPUOverclockInfo.Zero;
                _settings.SynchronizeStore();
            }
        }
        catch
        {
            isSupported = false;
        }

        Log.Instance.Trace($"Supports GPU OC status: {isSupported}");

        return isSupported;
    }

    public (bool, GPUOverclockInfo) GetState() => (_settings.Store.Enabled, _settings.Store.Info);

    public void SaveState(bool enabled, GPUOverclockInfo info)
    {
        Log.Instance.Trace($"Saved GPU overclock settings: [enabled={enabled}, info={info}].");

        _settings.Store.Enabled = enabled;
        _settings.Store.Info = info;
        _settings.SynchronizeStore();
    }

    public async Task<bool> ApplyStateAsync(bool force = false)
    {
        if (await _vantageDisabler.GetStatusAsync().ConfigureAwait(false) == SoftwareStatus.Enabled)
        {
            Log.Instance.Trace($"Can't correctly apply state when Vantage is running.");

            Changed?.Invoke(this, EventArgs.Empty);
            return false;
        }

        if (await _legionSpaceDisabler.GetStatusAsync().ConfigureAwait(false) == SoftwareStatus.Enabled)
        {
            Log.Instance.Trace($"Can't correctly apply state when Legion Space is running.");

            Changed?.Invoke(this, EventArgs.Empty);
            return false;
        }

        if (await _legionZoneDisabler.GetStatusAsync().ConfigureAwait(false) == SoftwareStatus.Enabled)
        {
            Log.Instance.Trace($"Can't correctly apply state when Legion Zone is running.");

            Changed?.Invoke(this, EventArgs.Empty);
            return false;
        }

        if (await _smartEngineDisabler.GetStatusAsync().ConfigureAwait(false) == SoftwareStatus.Enabled)
        {
            Log.Instance.Trace($"Can't correctly apply state when SmartEngine is running.");

            Changed?.Invoke(this, EventArgs.Empty);
            return false;
        }

        if (IoCContainer.Resolve<HybridModeFeature>().ShouldKeepDGPUAsleep())
        {
            Log.Instance.Trace($"dGPU eject is being ensured — skipping overclock apply.");

            Changed?.Invoke(this, EventArgs.Empty);
            return false;
        }

        var enabled = _settings.Store.Enabled;
        var info = _settings.Store.Info;

        if (force)
        {
            info = enabled ? info : GPUOverclockInfo.Zero;
            enabled = true;

            Log.Instance.Trace($"Forcing... [enabled=true, info={info}]");
        }

        if (!enabled)
        {
            Log.Instance.Trace($"Not enabled.");

            Changed?.Invoke(this, EventArgs.Empty);

            return false;
        }

        Log.Instance.Trace($"Applying overclock: {info}.");

        try
        {
            NVAPI.Initialize();

            var gpu = NVAPI.GetGPU();
            if (gpu is null)
            {
                Log.Instance.Trace($"dGPU not found.");

                Changed?.Invoke(this, EventArgs.Empty);

                return false;
            }

            var applied = SetOverclockInfo(gpu, info);

            Log.Instance.Trace($"Applied overclock: {info}, applied: {applied}, current: {GetOverclockInfo(gpu)}.");

            return applied;
        }
        catch (Exception ex)
        {
            Log.Instance.Trace($"Failed to apply overclock: {info}, clearing settings...", ex);

            _settings.Store.Enabled = false;
            _settings.Store.Info = GPUOverclockInfo.Zero;
            _settings.SynchronizeStore();

            return false;
        }
        finally
        {
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    public async Task<bool> EnsureOverclockIsAppliedAsync()
    {
        var (enabled, _) = GetState();
        if (!enabled)
            return false;

        await ApplyStateAsync().ConfigureAwait(false);
        return true;
    }

    private async void NativeWindowsMessageListenerOnChanged(object? sender, NativeWindowsMessageListener.ChangedEventArgs e)
    {
        if (e.Message is not NativeWindowsMessage.DisplayDeviceChanged and not NativeWindowsMessage.MonitorOn)
            return;

        if (await IsSupportedAsync().ConfigureAwait(false))
            await ApplyStateAsync().ConfigureAwait(false);
    }

    private const int FallbackMinCoreDeltaMhz = -500;
    private const int FallbackMaxCoreDeltaMhz = 500;
    private const int FallbackMinMemoryDeltaMhz = -3000;
    private const int FallbackMaxMemoryDeltaMhz = 3000;
    private const int FallbackMinVoltageMv = 700;
    private const int FallbackMaxVoltageMv = 1200;

    public ((int Min, int Max) Core, (int Min, int Max) Memory, (int Min, int Max) Voltage) GetRanges()
    {
        var (core, memory) = GetClockDeltaRangesMhz();

        return (core, memory, GetVoltageRangeMv());
    }

    private (int Min, int Max) GetVoltageRangeMv()
    {
        try
        {
            NVAPI.Initialize();

            var gpu = NVAPI.GetGPU();
            if (gpu is null)
                return (FallbackMinVoltageMv, FallbackMaxVoltageMv);

            var graphicsRange = GetCurveRanges(gpu).Graphics;
            if (graphicsRange is null)
                return (FallbackMinVoltageMv, FallbackMaxVoltageMv);

            var points = ReadCurvePoints(gpu).Points;

            var minMv = int.MaxValue;
            var maxMv = int.MinValue;
            for (var i = graphicsRange.Value.FirstPointIndex; i <= graphicsRange.Value.LastPointIndex && i < points.Length; i++)
            {
                if (points[i].VoltageInMicroV == 0)
                    continue;

                minMv = Math.Min(minMv, (int)points[i].VoltageInMilliV);
                maxMv = Math.Max(maxMv, (int)points[i].VoltageInMilliV);
            }

            return minMv > maxMv ? (FallbackMinVoltageMv, FallbackMaxVoltageMv) : (minMv, maxMv);
        }
        catch (Exception ex)
        {
            Log.Instance.Trace($"Failed to read the V/F curve voltage range.", ex);
            return (FallbackMinVoltageMv, FallbackMaxVoltageMv);
        }
    }

    private ((int Min, int Max) Core, (int Min, int Max) Memory) GetClockDeltaRangesMhz()
    {
        var coreFallback = (FallbackMinCoreDeltaMhz, FallbackMaxCoreDeltaMhz);
        var memoryFallback = (FallbackMinMemoryDeltaMhz, FallbackMaxMemoryDeltaMhz);

        try
        {
            NVAPI.Initialize();

            var gpu = NVAPI.GetGPU();
            if (gpu is null)
                return (coreFallback, memoryFallback);

            var states = GPUApi.GetPerformanceStates20(gpu.Handle);
            if (!states.Clocks.TryGetValue(PerformanceStateId.P0_3DPerformance, out var clocks))
                return (coreFallback, memoryFallback);

            return (GetClockDeltaRangeMhz(clocks, PublicClockDomain.Graphics, FallbackMinCoreDeltaMhz, FallbackMaxCoreDeltaMhz),
                GetClockDeltaRangeMhz(clocks, PublicClockDomain.Memory, FallbackMinMemoryDeltaMhz, FallbackMaxMemoryDeltaMhz));
        }
        catch (Exception ex)
        {
            Log.Instance.Trace($"Failed to read the clock offset ranges.", ex);
            return (coreFallback, memoryFallback);
        }
    }

    private static (int Min, int Max) GetClockDeltaRangeMhz(IPerformanceStates20ClockEntry[] clocks, PublicClockDomain domain, int fallbackMinMhz, int fallbackMaxMhz)
    {
        var clock = clocks.FirstOrDefault(c => c.DomainId == domain);
        if (clock is null)
            return (fallbackMinMhz, fallbackMaxMhz);

        var range = clock.FrequencyDeltaInkHz.DeltaRange;
        if (range.Minimum == 0 && range.Maximum == 0)
            return (fallbackMinMhz, fallbackMaxMhz);

        var minimumMhz = range.Minimum / 1000;
        var maximumMhz = range.Maximum / 1000;

        return minimumMhz > maximumMhz ? (fallbackMinMhz, fallbackMaxMhz) : (minimumMhz, maximumMhz);
    }

    private bool SetOverclockInfo(PhysicalGPU gpu, GPUOverclockInfo info)
    {
        var ((minCoreDeltaMhz, maxCoreDeltaMhz), (minMemoryDeltaMhz, maxMemoryDeltaMhz)) = GetClockDeltaRangesMhz();

        var coreDeltaMhz = Math.Clamp(info.CoreDeltaMhz, minCoreDeltaMhz, maxCoreDeltaMhz);
        var memoryDeltaMhz = Math.Clamp(info.MemoryDeltaMhz, minMemoryDeltaMhz, maxMemoryDeltaMhz);

        if (coreDeltaMhz != info.CoreDeltaMhz)
            Log.Instance.Trace($"Clamped core offset {info.CoreDeltaMhz} MHz to {coreDeltaMhz} MHz (driver range {minCoreDeltaMhz}..{maxCoreDeltaMhz} MHz).");
        if (memoryDeltaMhz != info.MemoryDeltaMhz)
            Log.Instance.Trace($"Clamped memory offset {info.MemoryDeltaMhz} MHz to {memoryDeltaMhz} MHz (driver range {minMemoryDeltaMhz}..{maxMemoryDeltaMhz} MHz).");

        var (graphicsRange, memoryRange) = GetCurveRanges(gpu);
        if (graphicsRange is null)
        {
            Log.Instance.Trace($"V/F curve is not available, applying offsets through performance states.");

            var legacyApplied = ApplyPerformanceStates(gpu, coreDeltaMhz * 1000, memoryDeltaMhz * 1000);
            var voltageRequested = info.VoltageLockMv > 0 || info.VoltageCapMv > 0;

            return legacyApplied && !voltageRequested;
        }

        return ApplyClockCurve(gpu, info, graphicsRange.Value, memoryRange, coreDeltaMhz * 1000, memoryDeltaMhz * 1000, maxCoreDeltaMhz * 1000);
    }

    private static bool ApplyPerformanceStates(PhysicalGPU gpu, int coreDeltaKhz, int memoryDeltaKhz)
    {
        try
        {
            try
            {
                GPUApi.EnableOverclockedPStates(gpu.Handle);
            }
            catch (Exception ex)
            {
                Log.Instance.Trace($"Failed to enable overclocked P-states.", ex);
            }

            var clockEntries = new[]
            {
                new PerformanceStates20ClockEntryV1(PublicClockDomain.Graphics, new PerformanceStates20ParameterDelta(coreDeltaKhz)),
                new PerformanceStates20ClockEntryV1(PublicClockDomain.Memory, new PerformanceStates20ParameterDelta(memoryDeltaKhz))
            };
            var voltageEntries = Array.Empty<PerformanceStates20BaseVoltageEntryV1>();
            var performanceStateInfo = new[] { new PerformanceStates20InfoV1.PerformanceState20(PerformanceStateId.P0_3DPerformance, clockEntries, voltageEntries) };
            var overclock = new PerformanceStates20InfoV1(performanceStateInfo, 2, 0);
            GPUApi.SetPerformanceStates20(gpu.Handle, overclock);

            return true;
        }
        catch (Exception ex)
        {
            Log.Instance.Trace($"Failed to apply performance states.", ex);

            return false;
        }
    }

    private static bool ApplyClockCurve(PhysicalGPU gpu, GPUOverclockInfo info, PrivateClockBoostRangesV1.ClockBoostRange graphicsRange,
        PrivateClockBoostRangesV1.ClockBoostRange? memoryRange, int coreDeltaKhz, int memoryDeltaKhz, int maxCoreDeltaKhz)
    {
        var voltageMv = info.VoltageLockMv > 0 ? info.VoltageLockMv : info.VoltageCapMv;
        var offsetsRequested = coreDeltaKhz != 0 || memoryDeltaKhz != 0;

        try
        {
            var firstIndex = graphicsRange.FirstPointIndex;
            var lastIndex = graphicsRange.LastPointIndex;
            var minDeltaKhz = graphicsRange.MinimumInkHz;

            var (points, structVersion) = ReadCurvePoints(gpu);
            var currentDeltas = GPUApi.GetClockBoostTable(gpu.Handle, structVersion).GPUDeltas;

            var coreOffsetApplied = coreDeltaKhz == 0 || ReadSettableDeltaMhz(currentDeltas, graphicsRange) is not null;
            var memoryOffsetApplied = memoryDeltaKhz == 0 || (memoryRange is not null && ReadSettableDeltaMhz(currentDeltas, memoryRange.Value) is not null);
            var offsetsApplied = coreOffsetApplied && memoryOffsetApplied;

            if (!coreOffsetApplied)
                Log.Instance.Trace($"No V/F curve point accepts a core offset on this GPU, core offset not applied.");

            if (memoryDeltaKhz != 0 && memoryRange is null)
                Log.Instance.Trace($"The driver reported no memory V/F curve range, memory offset not applied.");
            else if (!memoryOffsetApplied)
                Log.Instance.Trace($"No V/F curve point accepts a memory offset on this GPU, memory offset not applied.");

            var available = Math.Min(points.Length, currentDeltas.Length);
            if (firstIndex >= available || lastIndex >= available)
            {
                Log.Instance.Trace($"Unexpected V/F curve size: {points.Length} points, {currentDeltas.Length} deltas, graphics range {firstIndex}..{lastIndex}.");
                return !offsetsRequested && voltageMv <= 0;
            }

            var clockMultiplier = structVersion == AdjustedClockCurveStructVersion || graphicsRange.MaximumInkHz != 2 * maxCoreDeltaKhz ? 1 : 2;
            var coreOffsetKhz = coreDeltaKhz * clockMultiplier;

            var anchorIndex = voltageMv > 0 ? FindCurvePoint(points, firstIndex, lastIndex, voltageMv) : -1;
            var anchorStockKhz = anchorIndex >= 0 ? GetStockFrequencyKhz(points, currentDeltas, anchorIndex) : 0;
            var anchorTargetKhz = anchorStockKhz + coreOffsetKhz;

            if (voltageMv > 0 && anchorIndex < 0)
                Log.Instance.Trace($"No V/F curve point at or above {voltageMv} mV in the {firstIndex}..{lastIndex} range, voltage cap/lock not applied.");

            var deltas = new PrivateClockBoostTableV1.GPUDelta[currentDeltas.Length];
            var flattenedCount = 0;
            var flattenDepthKhz = 0;

            for (var i = 0; i < deltas.Length; i++)
            {
                var deltaKhz = currentDeltas[i].FrequencyDeltaInkHz;

                if (memoryRange is not null && i >= memoryRange.Value.FirstPointIndex && i <= memoryRange.Value.LastPointIndex)
                {
                    deltaKhz = memoryDeltaKhz;
                }
                else if (i >= firstIndex && i <= lastIndex)
                {
                    deltaKhz = coreOffsetKhz;

                    if (anchorIndex >= 0)
                    {
                        var stockFrequencyKhz = GetStockFrequencyKhz(points, currentDeltas, i);
                        if (stockFrequencyKhz > anchorStockKhz)
                        {
                            deltaKhz = Math.Max(anchorTargetKhz - stockFrequencyKhz, minDeltaKhz);
                            flattenedCount++;
                            flattenDepthKhz = Math.Min(flattenDepthKhz, deltaKhz - coreOffsetKhz);
                        }
                    }
                }

                deltas[i] = new PrivateClockBoostTableV1.GPUDelta(deltaKhz);
            }

            GPUApi.SetClockBoostTable(gpu.Handle, new PrivateClockBoostTableV1(deltas), structVersion);

            if (anchorIndex >= 0)
                Log.Instance.Trace($"Flattened V/F curve at point #{anchorIndex} ({points[anchorIndex].VoltageInMilliV} mV, {anchorTargetKhz / (1000 * clockMultiplier)} MHz): {flattenedCount} points, depth {flattenDepthKhz / (1000 * clockMultiplier)} MHz.");

            var lockRequested = info.VoltageLockMv > 0 && anchorIndex >= 0;
            var lockApplied = SetVoltageLock(gpu.Handle, lockRequested ? points[anchorIndex].VoltageInMicroV : 0, lockRequested);

            if (voltageMv <= 0)
                return offsetsApplied;

            if (anchorIndex < 0)
                return false;

            return offsetsApplied && (!lockRequested || lockApplied);
        }
        catch (Exception ex)
        {
            Log.Instance.Trace($"Failed to apply the voltage cap/lock.", ex);

            return !offsetsRequested && voltageMv <= 0;
        }
    }

    private const int DefaultCurveStructVersion = 1;
    private const int AdjustedClockCurveStructVersion = 2;

    private static (PrivateClockBoostRangesV1.ClockBoostRange? Graphics, PrivateClockBoostRangesV1.ClockBoostRange? Memory) GetCurveRanges(PhysicalGPU gpu)
    {
        var ranges = GPUApi.GetClockBoostRanges(gpu.Handle).ClockBoostRanges;
        var graphicsRange = ranges?.FirstOrDefault(r => r.ClockDomain == PublicClockDomain.Graphics);

        if (graphicsRange is null || graphicsRange.Value.LastPointIndex == 0)
            return (null, null);

        return (graphicsRange, ranges?.FirstOrDefault(r => r.ClockDomain == PublicClockDomain.Memory));
    }

    private static (PrivateClientClkVFPointsStatusV1.VFPoint[] Points, int StructVersion) ReadCurvePoints(PhysicalGPU gpu)
    {
        try
        {
            return (GPUApi.GetClientClkVFPointsStatus(gpu.Handle, AdjustedClockCurveStructVersion).Points, AdjustedClockCurveStructVersion);
        }
        catch
        {
            return (GPUApi.GetClientClkVFPointsStatus(gpu.Handle, DefaultCurveStructVersion).Points, DefaultCurveStructVersion);
        }
    }

    private static int FindCurvePoint(PrivateClientClkVFPointsStatusV1.VFPoint[] points, int firstIndex, int lastIndex, int voltageMv)
    {
        var last = Math.Min(lastIndex, points.Length - 1);

        for (var i = firstIndex; i <= last; i++)
        {
            if (points[i].VoltageInMicroV > 0 && points[i].VoltageInMilliV >= voltageMv)
                return i;
        }

        return -1;
    }

    private static int GetStockFrequencyKhz(PrivateClientClkVFPointsStatusV1.VFPoint[] points, PrivateClockBoostTableV1.GPUDelta[] deltas, int index)
        => (int)points[index].FrequencyInkHz - deltas[index].FrequencyDeltaInkHz;

    private static int? ReadSettableDeltaMhz(PrivateClockBoostTableV1.GPUDelta[] deltas, PrivateClockBoostRangesV1.ClockBoostRange range)
    {
        var lastIndex = Math.Min(range.LastPointIndex, deltas.Length - 1);

        for (var i = range.FirstPointIndex; i <= lastIndex; i++)
        {
            if (deltas[i].PointMode == 0)
                return deltas[i].FrequencyDeltaInkHz / 1000;
        }

        return null;
    }

    private static bool SetVoltageLock(PhysicalGPUHandle gpuHandle, uint voltageInMicroV, bool lockVoltage)
    {
        try
        {
            var entry = lockVoltage
                ? PrivateClockBoostLockV2.ClockBoostLock.CreateVoltageLock(voltageInMicroV)
                : PrivateClockBoostLockV2.ClockBoostLock.CreateVoltageReset();

            GPUApi.SetClockBoostLock(gpuHandle, new PrivateClockBoostLockV2(new[] { entry }));

            return true;
        }
        catch (Exception ex)
        {
            Log.Instance.Trace($"Failed to set the voltage lock.", ex);

            return false;
        }
    }

    private static GPUOverclockInfo GetOverclockInfo(PhysicalGPU gpu)
    {
        var core = 0;
        var memory = 0;

        try
        {
            var (graphicsRange, memoryRange) = GetCurveRanges(gpu);
            if (graphicsRange is not null)
            {
                var (_, structVersion) = ReadCurvePoints(gpu);
                var deltas = GPUApi.GetClockBoostTable(gpu.Handle, structVersion).GPUDeltas;

                core = ReadSettableDeltaMhz(deltas, graphicsRange.Value) ?? 0;

                if (memoryRange is not null)
                    memory = ReadSettableDeltaMhz(deltas, memoryRange.Value) ?? 0;
            }
            else
            {
                var states = GPUApi.GetPerformanceStates20(gpu.Handle);
                var p0Clocks = states.Clocks.TryGetValue(PerformanceStateId.P0_3DPerformance, out var clocks)
                    ? clocks
                    : Array.Empty<IPerformanceStates20ClockEntry>();

                memory = p0Clocks.FirstOrDefault(c => c.DomainId == PublicClockDomain.Memory)?.FrequencyDeltaInkHz.DeltaValue / 1000 ?? 0;
                core = p0Clocks.FirstOrDefault(c => c.DomainId == PublicClockDomain.Graphics)?.FrequencyDeltaInkHz.DeltaValue / 1000 ?? 0;
            }
        }
        catch (Exception ex)
        {
            Log.Instance.Trace($"Failed to read the active offsets.", ex);
        }

        int voltageLock = 0;
        try
        {
            var clockLock = GPUApi.GetClockBoostLock(gpu.Handle, PublicClockDomain.Voltage);
            if (clockLock.ClockBoostLocks.Length > 0 && clockLock.ClockBoostLocks[0].LockMode == ClockLockMode.Manual)
            {
                voltageLock = (int)(clockLock.ClockBoostLocks[0].VoltageInMicroV / 1000);
            }
        }
        catch (Exception ex)
        {
            Log.Instance.Trace($"Failed to read active voltage lock.", ex);
        }

        int voltageCap = 0;
        try
        {
            var (graphicsRange, _) = GetCurveRanges(gpu);
            var (points, structVersion) = ReadCurvePoints(gpu);
            var deltas = GPUApi.GetClockBoostTable(gpu.Handle, structVersion).GPUDeltas;

            if (graphicsRange is not null && deltas is not null)
            {
                var lastIndex = Math.Min(graphicsRange.Value.LastPointIndex, Math.Min(points.Length, deltas.Length) - 1);

                for (var i = graphicsRange.Value.FirstPointIndex + 1; i <= lastIndex; i++)
                {
                    if (deltas[i].FrequencyDeltaInkHz < deltas[i - 1].FrequencyDeltaInkHz && points[i - 1].VoltageInMicroV > 0)
                    {
                        voltageCap = (int)points[i - 1].VoltageInMilliV;
                        break;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Log.Instance.Trace($"Failed to read active voltage cap.", ex);
        }

        return new(core, memory, voltageLock, voltageCap);
    }
}
