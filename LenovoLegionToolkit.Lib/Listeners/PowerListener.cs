using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using LenovoLegionToolkit.Lib.Controllers;
using LenovoLegionToolkit.Lib.Utils;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.System.Power;

namespace LenovoLegionToolkit.Lib.Listeners;

public class PowerListener(IMainThreadDispatcher mainThreadDispatcher) : NativeWindow, IListener<PowerListener.ChangedEventArgs>, IDisposable
{
    private const int PollIntervalMs = 500;

    private static readonly Guid GuidPowerSchemePersonality = Guid.Parse("245D8541-3943-4422-B025-13A784F679B7");

    public class ChangedEventArgs(Guid activeOverlayAc, Guid activeOverlayDc, Guid activePowerPlan) : EventArgs
    {
        public Guid ActiveOverlayAc { get; } = activeOverlayAc;
        public Guid ActiveOverlayDc { get; } = activeOverlayDc;
        public Guid ActivePowerPlan { get; } = activePowerPlan;
    }

    public bool TrackPowerPlan { get; set; }

    private HPOWERNOTIFY _powerPersonalityNotificationHandle;
    private CancellationTokenSource? _pollCts;

    private Guid _overlayAc;
    private Guid _overlayDc;
    private Guid _powerPlan;

    private volatile bool _enabled;

    public event EventHandler<ChangedEventArgs>? Changed;

    public void Enable()
    {
        _ = mainThreadDispatcher.DispatchAsync(() =>
        {
            if (_enabled)
            {
                return Task.CompletedTask;
            }

            _enabled = true;

            CreateHandle(new CreateParams
            {
                Caption = "LenovoLegionToolkit_PowerListener",
                Parent = new IntPtr(-3)
            });

            _powerPersonalityNotificationHandle = RegisterPowerNotification(GuidPowerSchemePersonality);
            StartPolling();

            Log.Instance.Trace($"Overlay tracking enabled.");

            return Task.CompletedTask;
        });
    }

    public Task StartAsync()
    {
        // On-demand launch, temporarily not enabled for Gaming and other series to reduce resource consumption.
        return Task.CompletedTask;
    }

    public Task StopAsync()
    {
        if (!_enabled)
        {
            return Task.CompletedTask;
        }

        return mainThreadDispatcher.DispatchAsync(() =>
        {
            if (!_enabled)
            {
                return Task.CompletedTask;
            }

            _enabled = false;

            StopPolling();

            if (_powerPersonalityNotificationHandle != HPOWERNOTIFY.Null)
            {
                PInvoke.UnregisterPowerSettingNotification(_powerPersonalityNotificationHandle);
            }

            _powerPersonalityNotificationHandle = HPOWERNOTIFY.Null;

            ReleaseHandle();

            Log.Instance.Trace($"Overlay tracking stopped.");

            return Task.CompletedTask;
        });
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg != PInvoke.WM_POWERBROADCAST || m.WParam != (IntPtr)PInvoke.PBT_POWERSETTINGCHANGE || m.LParam == IntPtr.Zero)
        {
            base.WndProc(ref m);
            return;
        }

        var str = Marshal.PtrToStructure<POWERBROADCAST_SETTING>(m.LParam);

        if (str.PowerSetting == GuidPowerSchemePersonality)
        {
            Log.Instance.Trace($"Power setting changed: [{str.PowerSetting}]");
            CheckForChanges();
        }

        base.WndProc(ref m);
    }

    private void StartPolling()
    {
        _pollCts?.Cancel();
        _pollCts?.Dispose();
        _pollCts = new CancellationTokenSource();
        _ = PollOverlayLoopAsync(_pollCts.Token);
    }

    private void StopPolling()
    {
        _pollCts?.Cancel();
        _pollCts?.Dispose();
        _pollCts = null;
    }

    private async Task PollOverlayLoopAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(PollIntervalMs, token).ConfigureAwait(false);
                CheckForChanges();
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception ex)
            {
                Log.Instance.Trace($"Overlay poll failed.", ex);
            }
        }
    }

    private void CheckForChanges()
    {
        var ac = ReadOverlayAc();
        var dc = ReadOverlayDc();
        if (ac != _overlayAc || dc != _overlayDc)
        {
            var plan = TrackPowerPlan ? ReadPowerPlan() : Guid.Empty;
            Log.Instance.Trace($"Overlay changed: AC={WindowsPowerModeController.WindowsPowerModeNameFromGuid(ac) ?? ac.ToString()}, DC={WindowsPowerModeController.WindowsPowerModeNameFromGuid(dc) ?? dc.ToString()}, PowerPlan={(TrackPowerPlan ? $"{WindowsPowerPlanController.GetPowerPlanName(plan)} [{plan}]" : "N/A")}");
            _overlayAc = ac;
            _overlayDc = dc;
            _powerPlan = plan;
            RaiseChanged();
        }
    }

    private void RaiseChanged()
    {
        Changed?.Invoke(this, new ChangedEventArgs(_overlayAc, _overlayDc, _powerPlan));
    }

    private static unsafe Guid ReadPowerPlan()
    {
        try
        {
            if (PInvoke.PowerGetActiveScheme(null, out var guid) == WIN32_ERROR.ERROR_SUCCESS)
                return *guid;
        }
        catch (Exception ex)
        {
            Log.Instance.Trace($"Failed to read active power plan.", ex);
        }

        return Guid.Empty;
    }

    private static Guid ReadOverlayAc()
    {
        var result = WindowsPowerModeController.PowerGetUserConfiguredACPowerMode(out var guid);
        return result == 0 ? guid : Guid.Empty;
    }

    private static Guid ReadOverlayDc()
    {
        var result = WindowsPowerModeController.PowerGetUserConfiguredDCPowerMode(out var guid);
        return result == 0 ? guid : Guid.Empty;
    }

    private unsafe HPOWERNOTIFY RegisterPowerNotification(Guid guid)
    {
        var local = guid;
        return PInvoke.RegisterPowerSettingNotification(new HANDLE(Handle), &local, 0);
    }

    public void Dispose()
    {
        StopAsync().GetAwaiter().GetResult();
        GC.SuppressFinalize(this);
    }
}
