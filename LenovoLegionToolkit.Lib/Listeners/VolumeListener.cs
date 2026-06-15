using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using LenovoLegionToolkit.Lib.Utils;
using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;

namespace LenovoLegionToolkit.Lib.Listeners;

public class VolumeListener : IListener<VolumeListener.ChangedEventArgs>, IMMNotificationClient, IDisposable
{
    public class ChangedEventArgs(bool speakerMute) : EventArgs
    {
        public bool SpeakerMute { get; } = speakerMute;
    }

    public event EventHandler<ChangedEventArgs>? Changed;

    private readonly SemaphoreSlim _initLock = new(1, 1);
    private readonly object _muteStateLock = new();
    private MMDeviceEnumerator? _enumerator;
    private MMDevice? _speakerDevice;
    private bool _disposed;

    private bool? _lastMuteState;

    public async Task StartAsync()
    {
        try
        {
            _enumerator = new MMDeviceEnumerator();
            _enumerator.RegisterEndpointNotificationCallback(this);

            try
            {
                _speakerDevice = _enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
            }
            catch (Exception ex)
            {
                Log.Instance.Trace($"VolumeListener: GetDefaultAudioEndpoint failed on start: {ex.Message}, falling back to enumeration.");
                _speakerDevice = _enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active).FirstOrDefault();
            }

            if (_speakerDevice != null)
            {
                try
                {
                    _lastMuteState = _speakerDevice.AudioEndpointVolume.Mute;
                    _speakerDevice.AudioEndpointVolume.OnVolumeNotification += OnSpeakerVolumeNotification;
                    Log.Instance.Trace($"VolumeListener started. [device={_speakerDevice.FriendlyName}, mute={_lastMuteState}]");

                    await SpecialKeyLedHelper.SetLedAsync(_lastMuteState.Value ? SpecialKeyLedState.SpeakerOn : SpecialKeyLedState.SpeakerOff).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    Log.Instance.Trace($"VolumeListener: failed to init device [{_speakerDevice.FriendlyName}]: {ex.Message}");
                    _speakerDevice.Dispose();
                    _speakerDevice = null;
                    _lastMuteState = null;
                }
            }
            else
            {
                Log.Instance.Trace($"VolumeListener started, no active render device found.");
            }
        }
        catch (Exception ex)
        {
            Log.Instance.Trace($"VolumeListener start failed: {ex.Message}", ex);
        }
    }

    public Task StopAsync()
    {
        if (_speakerDevice != null)
            _speakerDevice.AudioEndpointVolume.OnVolumeNotification -= OnSpeakerVolumeNotification;

        _speakerDevice?.Dispose();
        _speakerDevice = null;

        if (_enumerator != null)
        {
            _enumerator.UnregisterEndpointNotificationCallback(this);
            _enumerator.Dispose();
            _enumerator = null;
        }

        try
        {
            _initLock.Dispose();
        }
        catch (ObjectDisposedException) { }

        return Task.CompletedTask;
    }

    public async Task NotifyAsync(bool speakerMute)
    {
        await OnChangedAsync(speakerMute).ConfigureAwait(false);
    }

    protected virtual Task OnChangedAsync(bool speakerMute)
    {
        RaiseChanged(speakerMute);
        return Task.CompletedTask;
    }

    protected void RaiseChanged(bool speakerMute)
    {
        Changed?.Invoke(this, new ChangedEventArgs(speakerMute));
    }

    private async void OnSpeakerVolumeNotification(AudioVolumeNotificationData data)
    {
        try
        {
            Log.Instance.Trace($"VolumeListener notification: muted={data.Muted}, lastMute={_lastMuteState}");

            var sourceDevice = _speakerDevice;
            if (sourceDevice == null)
                return;

            lock (_muteStateLock)
            {
                if (_lastMuteState == data.Muted)
                    return;

                _lastMuteState = data.Muted;
            }

            if (sourceDevice != _speakerDevice)
                return;

            await SpecialKeyLedHelper.SetLedAsync(data.Muted ? SpecialKeyLedState.SpeakerOn : SpecialKeyLedState.SpeakerOff).ConfigureAwait(false);

            await OnChangedAsync(data.Muted).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Log.Instance.Trace($"VolumeListener error: {ex.Message}", ex);
        }
    }

    public void OnDefaultDeviceChanged(DataFlow dataFlow, Role deviceRole, string defaultDeviceId)
    {
        Log.Instance.Trace($"VolumeListener OnDefaultDeviceChanged: flow={dataFlow}, role={deviceRole}, id={defaultDeviceId}");

        if (dataFlow != DataFlow.Render) return;

        ScheduleSync(() => SyncToDevice(defaultDeviceId));
    }

    public void OnDeviceAdded(string deviceId)
    {
        Log.Instance.Trace($"VolumeListener OnDeviceAdded: id={deviceId}");
        HandleDeviceNotification(deviceId);
    }

    public void OnDeviceRemoved(string deviceId)
    {
        Log.Instance.Trace($"VolumeListener OnDeviceRemoved: id={deviceId}");

        if (_speakerDevice?.ID == deviceId)
        {
            Log.Instance.Trace($"VolumeListener: tracked device removed, syncing to active device.");
            ScheduleSync(SyncToActiveDevice);
        }
    }

    public void OnDeviceStateChanged(string deviceId, DeviceState newState)
    {
        Log.Instance.Trace($"VolumeListener OnDeviceStateChanged: id={deviceId}, state={newState}");
        HandleDeviceNotification(deviceId);
    }

    public void OnPropertyValueChanged(string deviceId, PropertyKey propertyKey)
    {
    }

    private void HandleDeviceNotification(string deviceId)
    {
        try
        {
            var device = _enumerator?.GetDevice(deviceId);
            if (device == null)
            {
                Log.Instance.Trace($"VolumeListener: GetDevice({deviceId}) returned null on notification, syncing to be safe.");
                ScheduleSync(SyncToActiveDevice);
                return;
            }

            var isRender = device.DataFlow == DataFlow.Render;
            device.Dispose();

            if (isRender)
                ScheduleSync(SyncToActiveDevice);
        }
        catch (Exception ex)
        {
            Log.Instance.Trace($"VolumeListener HandleDeviceNotification error: {ex.Message}", ex);
        }
    }

    private void ScheduleSync(Action syncAction)
    {
        Task.Run(async () =>
        {
            try
            {
                await _initLock.WaitAsync().ConfigureAwait(false);
            }
            catch (ObjectDisposedException)
            {
                return;
            }

            try
            {
                syncAction();
            }
            catch (Exception ex)
            {
                Log.Instance.Trace($"VolumeListener sync error: {ex.Message}", ex);
            }
            finally
            {
                try { _initLock.Release(); } catch (ObjectDisposedException) { }
            }
        });
    }

    private void SyncToActiveDevice()
    {
        var enumerator = _enumerator;
        if (enumerator == null) return;

        MMDevice? device = null;
        try
        {
            device = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
        }
        catch (Exception ex)
        {
            Log.Instance.Trace($"VolumeListener: GetDefaultAudioEndpoint failed: {ex.Message}, falling back to enumeration.");
            device = enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active).FirstOrDefault();
        }

        if (device == null)
        {
            ClearTrackedDevice();
            return;
        }

        SubscribeToDevice(device);
    }

    private void SyncToDevice(string deviceId)
    {
        var enumerator = _enumerator;
        if (enumerator == null) return;

        var device = enumerator.GetDevice(deviceId);

        if (device == null)
        {
            Log.Instance.Trace($"VolumeListener: GetDevice({deviceId}) returned null, falling back to enumeration.");
            SyncToActiveDevice();
            return;
        }

        SubscribeToDevice(device);
    }

    private void SubscribeToDevice(MMDevice device)
    {
        if (_speakerDevice?.ID == device.ID)
        {
            Log.Instance.Trace($"VolumeListener: already tracking device [{device.FriendlyName}], skipping.");
            device.Dispose();
            return;
        }

        var previousMute = _lastMuteState;

        bool newMute;
        try
        {
            newMute = device.AudioEndpointVolume.Mute;
        }
        catch (Exception ex)
        {
            Log.Instance.Trace($"VolumeListener: failed to read mute state from [{device.FriendlyName}]: {ex.Message}");
            device.Dispose();
            return;
        }

        if (_speakerDevice != null)
        {
            _speakerDevice.AudioEndpointVolume.OnVolumeNotification -= OnSpeakerVolumeNotification;
            Log.Instance.Trace($"VolumeListener unsubscribed from: {_speakerDevice.FriendlyName}");
            _speakerDevice.Dispose();
        }

        _speakerDevice = device;
        _lastMuteState = newMute;
        device.AudioEndpointVolume.OnVolumeNotification += OnSpeakerVolumeNotification;
        Log.Instance.Trace($"VolumeListener subscribed. [device={device.FriendlyName}, mute={_lastMuteState}, previousMute={previousMute}]");

        _ = SpecialKeyLedHelper.SetLedAsync(newMute ? SpecialKeyLedState.SpeakerOn : SpecialKeyLedState.SpeakerOff);

        if (previousMute != newMute && previousMute.HasValue)
        {
            Log.Instance.Trace($"VolumeListener mute state changed after sync. [old={previousMute}, new={newMute}]");
            _ = OnChangedAsync(newMute);
        }
    }

    private void ClearTrackedDevice()
    {
        if (_speakerDevice == null) return;

        _speakerDevice.AudioEndpointVolume.OnVolumeNotification -= OnSpeakerVolumeNotification;
        _speakerDevice.Dispose();
        _speakerDevice = null;
        _lastMuteState = null;
        Log.Instance.Trace($"VolumeListener: no render devices, cleared tracked device.");
    }

    public void Dispose()
    {
        if (_disposed) return;

        StopAsync().GetAwaiter().GetResult();

        _disposed = true;
        GC.SuppressFinalize(this);
    }
}
