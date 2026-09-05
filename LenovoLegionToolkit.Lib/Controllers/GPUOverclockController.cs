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

        var clockEntries = new[]
        {
            new PerformanceStates20ClockEntryV1(PublicClockDomain.Graphics, new PerformanceStates20ParameterDelta(coreDelta * 1000)),
            new PerformanceStates20ClockEntryV1(PublicClockDomain.Memory, new PerformanceStates20ParameterDelta(memoryDelta * 1000))
        };
        var voltageEntries = Array.Empty<PerformanceStates20BaseVoltageEntryV1>();
        var performanceStateInfo = new[] { new PerformanceStates20InfoV1.PerformanceState20(PerformanceStateId.P0_3DPerformance, clockEntries, voltageEntries) };

        try
        {
            var overclock = new PerformanceStates20InfoV1(performanceStateInfo, 2, 0);
            GPUApi.SetPerformanceStates20(gpu.Handle, overclock);
        }
        catch (Exception ex)
        {
            Log.Instance.Trace($"Failed to apply performance states.", ex);
        }

        try
        {
            if (info.VoltageCapMv > 0)
            {
                var voltageCap = Math.Clamp(info.VoltageCapMv, GetMinVoltageCapMv(), GetMaxVoltageCapMv());

                var voltageResetEntry = new PrivateClockBoostLockV2.ClockBoostLock(
                    PublicClockDomain.Voltage,
                    ClockLockMode.None,
                    0
                );
                var graphicsResetEntry = new PrivateClockBoostLockV2.ClockBoostLock(
                    PublicClockDomain.Graphics,
                    ClockLockMode.None,
                    0
                );

                GPUApi.SetClockBoostLock(gpu.Handle, new PrivateClockBoostLockV2(new[] { voltageResetEntry }));
                GPUApi.SetClockBoostLock(gpu.Handle, new PrivateClockBoostLockV2(new[] { graphicsResetEntry }));

                var pointsStatus = GPUApi.GetClientClkVFPointsStatus(gpu.Handle);
                var points = pointsStatus.Points;

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
                    var boostTable = GPUApi.GetClockBoostTable(gpu.Handle);
                    var deltaCount = Math.Min(points.Length, boostTable.GPUDeltas.Length);
                    var gpuDeltas = new PrivateClockBoostTableV1.GPUDelta[deltaCount];
                    for (var i = 0; i < deltaCount; i++)
                    {
                        var p = points[i];
                        var delta = (p.VoltageInMicroV > targetPoint.VoltageInMicroV && p.FrequencyInkHz > targetPoint.FrequencyInkHz)
                            ? (int)targetPoint.FrequencyInkHz - (int)p.FrequencyInkHz
                            : 0;
                        gpuDeltas[i] = new PrivateClockBoostTableV1.GPUDelta(delta);
                    }

                    GPUApi.SetClockBoostTable(gpu.Handle, new PrivateClockBoostTableV1(gpuDeltas));
                }
            }
            else if (info.VoltageLockMv > 0)
            {
                var voltageLock = Math.Clamp(info.VoltageLockMv, GetMinVoltageLockMv(), GetMaxVoltageLockMv());
                var voltageLockEntry = new PrivateClockBoostLockV2.ClockBoostLock(
                    PublicClockDomain.Voltage,
                    ClockLockMode.Manual,
                    (uint)(voltageLock * 1000)
                );
                var graphicsResetEntry = new PrivateClockBoostLockV2.ClockBoostLock(
                    PublicClockDomain.Graphics,
                    ClockLockMode.None,
                    0
                );

                GPUApi.SetClockBoostLock(gpu.Handle, new PrivateClockBoostLockV2(new[] { voltageLockEntry }));
                GPUApi.SetClockBoostLock(gpu.Handle, new PrivateClockBoostLockV2(new[] { graphicsResetEntry }));

                GPUApi.SetClockBoostTable(gpu.Handle, new PrivateClockBoostTableV1(Array.Empty<PrivateClockBoostTableV1.GPUDelta>()));
            }
            else
            {
                var voltageResetEntry = new PrivateClockBoostLockV2.ClockBoostLock(
                    PublicClockDomain.Voltage,
                    ClockLockMode.None,
                    0
                );
                var graphicsResetEntry = new PrivateClockBoostLockV2.ClockBoostLock(
                    PublicClockDomain.Graphics,
                    ClockLockMode.None,
                    0
                );

                GPUApi.SetClockBoostLock(gpu.Handle, new PrivateClockBoostLockV2(new[] { voltageResetEntry }));
                GPUApi.SetClockBoostLock(gpu.Handle, new PrivateClockBoostLockV2(new[] { graphicsResetEntry }));

                GPUApi.SetClockBoostTable(gpu.Handle, new PrivateClockBoostTableV1(Array.Empty<PrivateClockBoostTableV1.GPUDelta>()));
            }
        }
        catch (Exception ex)
        {
            Log.Instance.Trace($"Failed to apply undervolting settings.", ex);
        }
    }

    private static GPUOverclockInfo GetOverclockInfo(PhysicalGPU gpu)
    {
        var states = GPUApi.GetPerformanceStates20(gpu.Handle);
        var core = states.Clocks[PerformanceStateId.P0_3DPerformance][0].FrequencyDeltaInkHz.DeltaValue / 1000;
        var memory = states.Clocks[PerformanceStateId.P0_3DPerformance][1].FrequencyDeltaInkHz.DeltaValue / 1000;

        int voltageLock = 0;
        try
        {
            var clockLock = GPUApi.GetClockBoostLock(gpu.Handle, PublicClockDomain.Voltage);
            if (clockLock.ClockBoostLocks.Length > 0 && clockLock.ClockBoostLocks[0].LockMode == ClockLockMode.Manual)
            {
                voltageLock = (int)(clockLock.ClockBoostLocks[0].VoltageInMicroV / 1000);
            }
        }
        catch
        {
        }

        int voltageCap = 0;
        try
        {
            var boostTable = GPUApi.GetClockBoostTable(gpu.Handle);
            var deltas = boostTable.GPUDeltas;
            if (deltas != null && deltas.Any(d => d.FrequencyDeltaInkHz < 0))
            {
                var pointsStatus = GPUApi.GetClientClkVFPointsStatus(gpu.Handle);
                var points = pointsStatus.Points;

                for (var i = 0; i < Math.Min(points.Length, deltas.Length); i++)
                {
                    if (deltas[i].FrequencyDeltaInkHz < 0 && i > 0 && points[i - 1].VoltageInMicroV > 0)
                    {
                        voltageCap = (int)points[i - 1].VoltageInMilliV;
                        break;
                    }
                }
            }
        }
        catch
        {
        }

        return new(core, memory, voltageLock, voltageCap);
    }
}
