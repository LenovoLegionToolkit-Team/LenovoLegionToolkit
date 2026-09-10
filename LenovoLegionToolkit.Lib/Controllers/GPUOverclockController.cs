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
    private readonly NativeWindowsMessageListener _nativeWindowsMessageListener;

    public event EventHandler? Changed;

    public GPUOverclockController(GPUOverclockSettings settings,
        VantageDisabler vantageDisabler,
        LegionSpaceDisabler legionSpaceDisabler,
        LegionZoneDisabler legionZoneDisabler,
        NativeWindowsMessageListener nativeWindowsMessageListener)
    {
        _settings = settings;
        _vantageDisabler = vantageDisabler;
        _legionSpaceDisabler = legionSpaceDisabler;
        _legionZoneDisabler = legionZoneDisabler;
        _nativeWindowsMessageListener = nativeWindowsMessageListener;
        _nativeWindowsMessageListener.Changed += NativeWindowsMessageListenerOnChanged;
    }

    public async Task<bool> IsSupportedAsync()
    {
        bool isSupported;

        try
        {
            if (AppFlags.Instance.Debug)
            {
                return true;
            }

            NVAPI.Initialize();
            isSupported = NVAPI.GetGPU() is not null;
        }
        catch
        {
            isSupported = false;
        }

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

    public async Task ApplyStateAsync(bool force = false)
    {
        if (await _vantageDisabler.GetStatusAsync().ConfigureAwait(false) == SoftwareStatus.Enabled)
        {
            Log.Instance.Trace($"Can't correctly apply state when Vantage is running.");

            Changed?.Invoke(this, EventArgs.Empty);
            return;
        }

        if (await _legionSpaceDisabler.GetStatusAsync().ConfigureAwait(false) == SoftwareStatus.Enabled)
        {
            Log.Instance.Trace($"Can't correctly apply state when Legion Space is running.");

            Changed?.Invoke(this, EventArgs.Empty);
            return;
        }

        if (await _legionZoneDisabler.GetStatusAsync().ConfigureAwait(false) == SoftwareStatus.Enabled)
        {
            Log.Instance.Trace($"Can't correctly apply state when Legion Zone is running.");

            Changed?.Invoke(this, EventArgs.Empty);
            return;
        }

        if (IoCContainer.Resolve<HybridModeFeature>().ShouldKeepDGPUAsleep())
        {
            Log.Instance.Trace($"dGPU eject is being ensured — skipping overclock apply.");

            Changed?.Invoke(this, EventArgs.Empty);
            return;
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

            return;
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

                return;
            }

            SetOverclockInfo(gpu, info);

            Log.Instance.Trace($"Applied overclock: {info}, current: {GetOverclockInfo(gpu)}.");
        }
        catch (Exception ex)
        {
            Log.Instance.Trace($"Failed to apply overclock: {info}, clearing settings...", ex);

            _settings.Store.Enabled = false;
            _settings.Store.Info = GPUOverclockInfo.Zero;
            _settings.SynchronizeStore();
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

    public static int GetMinCoreDeltaMhz() => -500;

    public static int GetMaxCoreDeltaMhz() => 500;

    public static int GetMinMemoryDeltaMhz() => -3000;

    public static int GetMaxMemoryDeltaMhz() => 3000;

    public static int GetMinVoltageLockMv() => 700;

    public static int GetMaxVoltageLockMv() => 1200;

    public static int GetMinVoltageCapMv() => 700;

    public static int GetMaxVoltageCapMv() => 1200;

    private static void SetOverclockInfo(PhysicalGPU gpu, GPUOverclockInfo info)
    {
        var coreDelta = Math.Clamp(info.CoreDeltaMhz, GetMinCoreDeltaMhz(), GetMaxCoreDeltaMhz());
        var memoryDelta = Math.Clamp(info.MemoryDeltaMhz, GetMinMemoryDeltaMhz(), GetMaxMemoryDeltaMhz());
        var coreDeltaKhz = coreDelta * 1000;
        var memoryDeltaKhz = memoryDelta * 1000;

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
        }
        catch (Exception ex)
        {
            Log.Instance.Trace($"Failed to apply performance states.", ex);
        }

        try
        {
            if (info.VoltageLockMv > 0)
            {
                var voltageLock = Math.Clamp(info.VoltageLockMv, GetMinVoltageLockMv(), GetMaxVoltageLockMv());
                ApplyClockBoostLock(gpu.Handle, (uint)(voltageLock * 1000), lockVoltage: true);
            }
            else
            {
                ApplyClockBoostLock(gpu.Handle, 0, lockVoltage: false);
            }
        }
        catch (Exception ex)
        {
            Log.Instance.Trace($"Failed to apply clock boost lock.", ex);
        }

        try
        {
            PrivateClockBoostRangesV1 ranges = default;
            bool vfSupported = false;
            try
            {
                ranges = GPUApi.GetClockBoostRanges(gpu.Handle);
                vfSupported = ranges.ClockBoostRanges != null && ranges.ClockBoostRanges.Length > 0;
            }
            catch
            {
                vfSupported = false;
            }

            if (vfSupported)
            {
                var graphicsRange = ranges.ClockBoostRanges?.FirstOrDefault(r => r.ClockDomain == PublicClockDomain.Graphics);
                int minDeltaKhz = graphicsRange?.MinimumInkHz ?? 0;
                int maxDeltaKhz = graphicsRange?.MaximumInkHz ?? 0;

                var pointsStatus = GPUApi.GetClientClkVFPointsStatus(gpu.Handle);
                var points = pointsStatus.Points;

                if (info.VoltageLockMv > 0)
                {
                    GPUApi.SetClockBoostTable(gpu.Handle, new PrivateClockBoostTableV1(Array.Empty<PrivateClockBoostTableV1.GPUDelta>()));
                }
                else if (info.VoltageCapMv > 0)
                {
                    var voltageCap = Math.Clamp(info.VoltageCapMv, GetMinVoltageCapMv(), GetMaxVoltageCapMv());

                    var targetIndex = -1;
                    for (var i = 0; i < points.Length; i++)
                    {
                        if (points[i].VoltageInMicroV > 0 && points[i].VoltageInMilliV >= voltageCap)
                        {
                            targetIndex = i;
                            break;
                        }
                    }

                    if (targetIndex == -1)
                    {
                        for (var i = points.Length - 1; i >= 0; i--)
                        {
                            if (points[i].VoltageInMicroV > 0)
                            {
                                targetIndex = i;
                                break;
                            }
                        }
                    }

                    if (targetIndex >= 0)
                    {
                        var targetPoint = points[targetIndex];
                        var maxAllowedFreq = (int)targetPoint.FrequencyInkHz + coreDeltaKhz;
                        Log.Instance.Trace($"Voltage cap {voltageCap} mV matched point #{targetIndex} ({targetPoint.VoltageInMilliV} mV @ {targetPoint.FrequencyInkHz / 1000} MHz, cap max: {maxAllowedFreq / 1000} MHz).");

                        var gpuDeltas = new PrivateClockBoostTableV1.GPUDelta[points.Length];
                        for (var i = 0; i < points.Length; i++)
                        {
                            var p = points[i];
                            if (p.VoltageInMicroV == 0)
                            {
                                gpuDeltas[i] = new PrivateClockBoostTableV1.GPUDelta(0);
                                continue;
                            }

                            var boostedFreq = (int)p.FrequencyInkHz + coreDeltaKhz;
                            var delta = boostedFreq > maxAllowedFreq
                                ? maxAllowedFreq - (int)p.FrequencyInkHz
                                : coreDeltaKhz;

                            if (minDeltaKhz != 0 && delta < minDeltaKhz)
                                delta = minDeltaKhz;
                            if (maxDeltaKhz != 0 && delta > maxDeltaKhz)
                                delta = maxDeltaKhz;

                            gpuDeltas[i] = new PrivateClockBoostTableV1.GPUDelta(delta);
                        }

                        GPUApi.SetClockBoostTable(gpu.Handle, new PrivateClockBoostTableV1(gpuDeltas));
                    }
                }
                else
                {
                    var gpuDeltas = new PrivateClockBoostTableV1.GPUDelta[points.Length];
                    for (var i = 0; i < points.Length; i++)
                    {
                        var delta = points[i].VoltageInMicroV > 0 ? coreDeltaKhz : 0;
                        if (minDeltaKhz != 0 && delta < minDeltaKhz)
                            delta = minDeltaKhz;
                        if (maxDeltaKhz != 0 && delta > maxDeltaKhz)
                            delta = maxDeltaKhz;

                        gpuDeltas[i] = new PrivateClockBoostTableV1.GPUDelta(delta);
                    }

                    GPUApi.SetClockBoostTable(gpu.Handle, new PrivateClockBoostTableV1(gpuDeltas));
                }
            }
        }
        catch (Exception ex)
        {
            Log.Instance.Trace($"Failed to apply V/F curve clock boost table.", ex);
        }
    }

    private static void ApplyClockBoostLock(PhysicalGPUHandle gpuHandle, uint voltageInMicroV, bool lockVoltage)
    {
        try
        {
            var voltageEntry = lockVoltage
                ? PrivateClockBoostLockV2.ClockBoostLock.CreateVoltageLock(voltageInMicroV)
                : PrivateClockBoostLockV2.ClockBoostLock.CreateVoltageReset();
            var graphicsReset = PrivateClockBoostLockV2.ClockBoostLock.CreateDynamicReset(0);

            GPUApi.SetClockBoostLock(gpuHandle, new PrivateClockBoostLockV2(new[] { voltageEntry }));
            GPUApi.SetClockBoostLock(gpuHandle, new PrivateClockBoostLockV2(new[] { graphicsReset }));
        }
        catch (Exception ex)
        {
            Log.Instance.Trace($"SetClockBoostLock failed.", ex);
        }
    }

    private static GPUOverclockInfo GetOverclockInfo(PhysicalGPU gpu)
    {
        var states = GPUApi.GetPerformanceStates20(gpu.Handle);
        var p0Clocks = states.Clocks.TryGetValue(PerformanceStateId.P0_3DPerformance, out var clocks)
            ? clocks
            : Array.Empty<IPerformanceStates20ClockEntry>();

        var memory = p0Clocks.FirstOrDefault(c => c.DomainId == PublicClockDomain.Memory)?.FrequencyDeltaInkHz.DeltaValue / 1000 ?? 0;
        var core = p0Clocks.FirstOrDefault(c => c.DomainId == PublicClockDomain.Graphics)?.FrequencyDeltaInkHz.DeltaValue / 1000 ?? 0;

        PrivateClockBoostTableV1? boostTable = null;
        PrivateClientClkVFPointsStatusV1? pointsStatus = null;

        try
        {
            boostTable = GPUApi.GetClockBoostTable(gpu.Handle);
            var deltas = boostTable.Value.GPUDeltas;
            if (deltas != null && deltas.Length > 0)
            {
                pointsStatus = GPUApi.GetClientClkVFPointsStatus(gpu.Handle);
                var points = pointsStatus.Value.Points;
                var firstActiveIndex = Array.FindIndex(points, p => p.VoltageInMicroV > 0);
                if (firstActiveIndex >= 0 && firstActiveIndex < deltas.Length)
                {
                    core = deltas[firstActiveIndex].FrequencyDeltaInkHz / 1000;
                }
            }
        }
        catch (Exception ex)
        {
            Log.Instance.Trace($"Failed to read active clock boost table.", ex);
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
            var deltas = boostTable?.GPUDeltas;
            if (deltas != null && deltas.Length > 1)
            {
                pointsStatus ??= GPUApi.GetClientClkVFPointsStatus(gpu.Handle);
                var points = pointsStatus.Value.Points;

                for (var i = 1; i < Math.Min(points.Length, deltas.Length); i++)
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
