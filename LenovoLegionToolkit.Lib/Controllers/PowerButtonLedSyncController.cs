using System;
using System.Threading;
using System.Threading.Tasks;
using LenovoLegionToolkit.Lib.Listeners;
using LenovoLegionToolkit.Lib.Settings;
using LenovoLegionToolkit.Lib.Utils;

namespace LenovoLegionToolkit.Lib.Controllers;

public sealed class PowerButtonLedSyncController(
    ApplicationSettings settings,
    PowerButtonLedController powerButton,
    SpectrumKeyboardBacklightController keyboard,
    SpecialKeyListener specialKeys,
    PowerStateListener powerState) : IDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly ThrottleLastDispatcher _dispatcher = new(TimeSpan.FromMilliseconds(200));
    private bool _started;
    private bool _faulted;
    private bool _applied;
    private int? _lastBrightness;

    public bool IsEnabled => settings.Store.PowerButtonLedSyncEnabled;
    public string? LastError { get; private set; }

    public async Task<bool> IsSupportedAsync()
    {
        try
        {
            return await keyboard.IsSupportedAsync().ConfigureAwait(false) && await powerButton.IsSupportedAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Log.Instance.Trace($"Could not read power-button LED capability.", ex);
            return false;
        }
    }

    public async Task StartAsync()
    {
        if (_started)
        {
            return;
        }

        _started = true;
        keyboard.BacklightChanged += Keyboard_BacklightChanged;
        specialKeys.Changed += SpecialKeys_Changed;
        powerState.Changed += PowerState_Changed;

        await ScheduleSyncAsync().ConfigureAwait(false);
    }

    public async Task SetEnabledAsync(bool enabled)
    {
        await _gate.WaitAsync().ConfigureAwait(false);

        try
        {
            settings.Store.PowerButtonLedSyncEnabled = enabled;
            settings.SynchronizeStore();

            _faulted = false;
            LastError = null;
            _lastBrightness = null;

            if (enabled)
            {
                await SyncCoreAsync().ConfigureAwait(false);
            }
            else if (_applied)
            {
                await powerButton.SetKeyboardBrightnessAsync(PowerButtonLedController.DefaultKeyboardBrightness).ConfigureAwait(false);
                _applied = false;
            }
        }
        catch (Exception ex)
        {
            RecordFailure(ex);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task SyncCoreAsync()
    {
        if (!_started || !IsEnabled || _faulted)
        {
            return;
        }

        if (!await IsSupportedAsync().ConfigureAwait(false))
        {
            throw new NotSupportedException("Power-button LED synchronization is not supported by this device's lighting interface.");
        }

        var profile = await keyboard.GetProfileAsync().ConfigureAwait(false);
        var brightness = profile == 0 ? 0 : await keyboard.GetBrightnessAsync().ConfigureAwait(false);

        if (_lastBrightness == brightness)
        {
            return;
        }

        await powerButton.SetKeyboardBrightnessAsync(brightness).ConfigureAwait(false);

        _lastBrightness = brightness;
        _applied = true;
        LastError = null;
    }

    private async Task ScheduleSyncAsync(bool refresh = false)
    {
        if (!_started || !IsEnabled)
        {
            return;
        }

        try
        {
            await _dispatcher.DispatchAsync(async () =>
            {
                await _gate.WaitAsync().ConfigureAwait(false);

                try
                {
                    if (refresh)
                    {
                        _lastBrightness = null;
                        _faulted = false;
                    }

                    await SyncCoreAsync().ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    RecordFailure(ex);
                }
                finally
                {
                    _gate.Release();
                }
            }).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            RecordFailure(ex);
        }
    }

    private void RecordFailure(Exception ex)
    {
        LastError = ex.Message;
        _faulted = true;

        Log.Instance.Trace($"Power-button LED synchronization stopped; the saved preference is retained.", ex);
    }

    private async void Keyboard_BacklightChanged(object? sender, EventArgs e) => await ScheduleSyncAsync().ConfigureAwait(false);

    private async void SpecialKeys_Changed(object? sender, SpecialKeyListener.ChangedEventArgs e)
    {
        if (e.SpecialKey is SpecialKey.SpectrumBacklightOff or SpecialKey.SpectrumBacklight1
            or SpecialKey.SpectrumBacklight2 or SpecialKey.SpectrumBacklight3
            or >= SpecialKey.SpectrumPreset1 and <= SpecialKey.SpectrumPreset6)
            await ScheduleSyncAsync().ConfigureAwait(false);
    }

    private async void PowerState_Changed(object? sender, PowerStateListener.ChangedEventArgs e)
    {
        if (e.PowerStateEvent == PowerStateEvent.Resume)
        {
            await ScheduleSyncAsync(refresh: true).ConfigureAwait(false);
        }
    }

    public void Dispose()
    {
        _started = false;
        keyboard.BacklightChanged -= Keyboard_BacklightChanged;
        specialKeys.Changed -= SpecialKeys_Changed;
        powerState.Changed -= PowerState_Changed;
    }

    public async Task StopAsync()
    {
        Dispose();
        await _gate.WaitAsync().ConfigureAwait(false);
        _gate.Release();
    }
}
