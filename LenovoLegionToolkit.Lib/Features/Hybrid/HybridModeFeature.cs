using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using LenovoLegionToolkit.Lib.Features.Hybrid.Notify;
using LenovoLegionToolkit.Lib.System;
using LenovoLegionToolkit.Lib.System.Management;
using LenovoLegionToolkit.Lib.Utils;

namespace LenovoLegionToolkit.Lib.Features.Hybrid;

public class HybridModeFeature(GSyncFeature gSyncFeature, IGPUModeFeature igpuModeFeature, DGPUNotify dgpuNotify, BiosHybridModeFeature biosHybridMode) : IFeature<HybridModeState>
{
    private readonly CancellationTokenSource _ensureDGPUEjectedIfNeededCancellationTokenSource = new();
    private bool _isEnsuringEjected;

    private HybridModeState? _lastState;

    private static bool IsHybridMode(HybridModeState s) => s
        is HybridModeState.On
        or HybridModeState.OnIGPUOnly
        or HybridModeState.OnAuto;

    public async Task<bool> IsSupportedAsync()
    {
        if (AppFlags.Instance.Debug)
        {
            return true;
        }

        var mi = await Compatibility.GetMachineInformationAsync().ConfigureAwait(false);
        return mi.Properties.SupportsGSync || mi.Properties.SupportsIGPUMode;
    }

    public async Task<HybridModeState[]> GetAllStatesAsync()
    {
        var mi = await Compatibility.GetMachineInformationAsync().ConfigureAwait(false);

        HybridModeState[] hybridModeStates = [HybridModeState.On, HybridModeState.Off];
        var states = (mi.Properties.SupportsGSync, mi.Properties.SupportsIGPUMode) switch
        {
            (true, true) => [HybridModeState.On, HybridModeState.OnIGPUOnly, HybridModeState.OnAuto, HybridModeState.Off],
            (false, true) => [HybridModeState.On, HybridModeState.OnIGPUOnly, HybridModeState.OnAuto],
            (true, false) => hybridModeStates,
            _ => []
        };

        if (states.Length > 0 && await biosHybridMode.IsSupportedAsync().ConfigureAwait(false))
            states = [.. states, HybridModeState.UMA];

        return states;
    }

    public async Task<HybridModeState> GetStateAsync()
    {
        if (_lastState.HasValue)
        {
            return _lastState.Value;
        }

        if (await biosHybridMode.IsSupportedAsync().ConfigureAwait(false) && await biosHybridMode.IsUMAEnabledAsync().ConfigureAwait(false))
        {
            Log.Instance.Trace($"State is {HybridModeState.UMA}");
            _lastState = HybridModeState.UMA;
            return HybridModeState.UMA;
        }

        var gSyncSupported = await gSyncFeature.IsSupportedAsync().ConfigureAwait(false);
        var igpuModeSupported = await igpuModeFeature.IsSupportedAsync().ConfigureAwait(false);

        var gSync = GSyncState.Off;
        var igpuMode = IGPUModeState.Default;

        if (gSyncSupported)
            gSync = await gSyncFeature.GetStateAsync().ConfigureAwait(false);

        if (igpuModeSupported)
            igpuMode = await igpuModeFeature.GetStateAsync().ConfigureAwait(false);

        var state = Pack(gSync, igpuMode);

        Log.Instance.Trace($"State is {state} [gSync={gSync}, igpuMode={igpuMode}]");

        _lastState = state;
        return state;
    }

    public async Task SetStateAsync(HybridModeState state)
    {
        if (state == HybridModeState.UMA)
        {
            await biosHybridMode.SetUMAAsync().ConfigureAwait(false);
            _lastState = state;
            _ = IoCContainer.Resolve<DgpuAwakeManager>().UpdateStateAsync();
            return;
        }

        if (await NeedsGraphicsDeviceSwitchAsync(state).ConfigureAwait(false))
        {
            if (state == HybridModeState.Off)
                await biosHybridMode.SetDiscreteAsync().ConfigureAwait(false);
            else
                await biosHybridMode.SetSwitchableAsync().ConfigureAwait(false);
        }

        await _ensureDGPUEjectedIfNeededCancellationTokenSource.CancelAsync().ConfigureAwait(false);

        var (gSync, igpuMode) = Unpack(state);

        Log.Instance.Trace($"Setting state to {state}... [gSync={gSync}, igpuMode={igpuMode}]");

        var gSyncSupported = await gSyncFeature.IsSupportedAsync().ConfigureAwait(false);
        var igpuModeSupported = await igpuModeFeature.IsSupportedAsync().ConfigureAwait(false);

        var gSyncChanged = false;

        if (gSyncSupported && await gSyncFeature.GetStateAsync().ConfigureAwait(false) != gSync)
        {
            await gSyncFeature.SetStateAsync(gSync).ConfigureAwait(false);
            gSyncChanged = true;
        }

        if (igpuModeSupported && await igpuModeFeature.GetStateAsync().ConfigureAwait(false) != igpuMode)
        {
            try
            {
                await igpuModeFeature.SetStateAsync(igpuMode).ConfigureAwait(false);
            }
            catch (IGPUModeChangeException)
            {
                if (!gSyncChanged)
                {
                    throw;
                }
            }
            finally
            {
                if (!gSyncChanged && igpuMode is IGPUModeState.Default or IGPUModeState.Auto or IGPUModeState.IGPUOnly)
                {
                    await dgpuNotify.NotifyLaterIfNeededAsync().ConfigureAwait(false);
                }
            }
        }

        Log.Instance.Trace($"State set to {state} [gSync={gSync}, igpuMode={igpuMode}]");

        _lastState = state;
        _ = IoCContainer.Resolve<DgpuAwakeManager>().UpdateStateAsync();
    }

    public async Task EnsureDGPUEjectedIfNeededAsync()
    {
        if (_isEnsuringEjected || !await igpuModeFeature.IsSupportedAsync().ConfigureAwait(false) || !await dgpuNotify.IsSupportedAsync().ConfigureAwait(false))
            return;

        _isEnsuringEjected = true;
        _ = Task.Run(async () =>
        {
            try
            {
                const int maxRetries = 5;
                const int delay = 5 * 1000;

                var retry = 1;

                Log.Instance.Trace($"Will make sure that dGPU is ejected. [maxRetries={maxRetries}, delay={delay}ms]");

                while (retry <= maxRetries)
                {
                    await Task.Delay(delay).ConfigureAwait(false);

                    if (_ensureDGPUEjectedIfNeededCancellationTokenSource.IsCancellationRequested)
                    {
                        Log.Instance.Trace($"Cancelled, aborting...");
                        break;
                    }

                    var currentMode = await igpuModeFeature.GetStateAsync().ConfigureAwait(false);

                    if (currentMode != IGPUModeState.IGPUOnly && currentMode != IGPUModeState.Auto)
                    {
                        Log.Instance.Trace($"Not in iGPU-only or Auto mode, aborting... [mode={currentMode}]");
                        break;
                    }

                    if (currentMode == IGPUModeState.Auto)
                    {
                        if (await Power.IsPowerAdapterConnectedAsync().ConfigureAwait(false) == PowerAdapterStatus.Connected)
                        {
                            Log.Instance.Trace($"Auto mode with AC connected, no eject needed.");
                            break;
                        }
                    }

                    if (!await dgpuNotify.IsDGPUAvailableAsync().ConfigureAwait(false))
                    {
                        Log.Instance.Trace($"dGPU already unavailable, aborting...");
                        break;
                    }

                    Log.Instance.Trace($"Notifying dGPU... [retry={retry}, maxRetries={maxRetries}]");

                    await dgpuNotify.NotifyAsync(false).ConfigureAwait(false);

                    retry++;
                }
            }
            catch (Exception ex)
            {
                Log.Instance.Trace($"Failed to ensure dGPU is ejected", ex);
            }
            finally
            {
                _isEnsuringEjected = false;
            }
        });
    }

    private async Task<bool> NeedsGraphicsDeviceSwitchAsync(HybridModeState target)
    {
        if (_lastState.HasValue && IsHybridMode(_lastState.Value) && IsHybridMode(target))
            return false;

        var mi = await Compatibility.GetMachineInformationAsync().ConfigureAwait(false);
        if (mi.LegionSeries != LegionSeries.ThinkBook)
            return false;

        return await biosHybridMode.IsUMAEnabledAsync().ConfigureAwait(false);
    }

    private static (GSyncState, IGPUModeState) Unpack(HybridModeState state) => state switch
    {
        HybridModeState.On => (GSyncState.Off, IGPUModeState.Default),
        HybridModeState.OnIGPUOnly => (GSyncState.Off, IGPUModeState.IGPUOnly),
        HybridModeState.OnAuto => (GSyncState.Off, IGPUModeState.Auto),
        HybridModeState.Off => (GSyncState.On, IGPUModeState.Default),
        _ => throw new InvalidOperationException("Invalid state"),
    };

    private static HybridModeState Pack(GSyncState state1, IGPUModeState state2) => (state1, state2) switch
    {
        (GSyncState.Off, IGPUModeState.Default) => HybridModeState.On,
        (GSyncState.Off, IGPUModeState.IGPUOnly) => HybridModeState.OnIGPUOnly,
        (GSyncState.Off, IGPUModeState.Auto) => HybridModeState.OnAuto,
        (GSyncState.On, _) => HybridModeState.Off,
        _ => throw new InvalidOperationException("Invalid state"),
    };
}