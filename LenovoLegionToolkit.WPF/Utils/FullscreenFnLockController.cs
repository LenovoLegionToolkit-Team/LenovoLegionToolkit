using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Windows.Media.Control;
using LenovoLegionToolkit.Lib;
using LenovoLegionToolkit.Lib.Controllers;
using LenovoLegionToolkit.Lib.Features;
using LenovoLegionToolkit.Lib.Settings;
using LenovoLegionToolkit.Lib.Utils;

namespace LenovoLegionToolkit.WPF.Utils;

public sealed class FullscreenFnLockController
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(1);

    private readonly ApplicationSettings _settings;
    private readonly FnLockFeature _fnLockFeature;
    private readonly SmartFnLockController _smartFnLockController;
    private readonly CancellationTokenSource _cts = new();
    private readonly SemaphoreSlim _refreshSignal = new(0, 1);
    private readonly Task _monitorTask;

    private GlobalSystemMediaTransportControlsSessionManager? _mediaSessionManager;

    public FullscreenFnLockController(
        ApplicationSettings settings,
        FnLockFeature fnLockFeature,
        SmartFnLockController smartFnLockController)
    {
        _settings = settings;
        _fnLockFeature = fnLockFeature;
        _smartFnLockController = smartFnLockController;
        _monitorTask = Task.Run(() => MonitorAsync(_cts.Token));
    }

    public void Refresh()
    {
        try
        {
            _refreshSignal.Release();
        }
        catch (SemaphoreFullException) { }
    }

    public async Task StopAsync()
    {
        if (!_cts.IsCancellationRequested)
            await _cts.CancelAsync().ConfigureAwait(false);

        try
        {
            await _monitorTask.ConfigureAwait(false);
        }
        catch (OperationCanceledException) { }

        await _smartFnLockController.SetFullscreenStateAsync(null).ConfigureAwait(false);
    }

    private async Task MonitorAsync(CancellationToken token)
    {
        var supportChecked = false;
        var isSupported = false;

        while (!token.IsCancellationRequested)
        {
            try
            {
                var mode = _settings.Store.FullscreenFnLockMode;

                if (mode == FullscreenFnLockMode.Off)
                {
                    await _smartFnLockController.SetFullscreenStateAsync(null).ConfigureAwait(false);
                }
                else
                {
                    if (!supportChecked)
                    {
                        isSupported = await _fnLockFeature.IsSupportedAsync().ConfigureAwait(false);
                        supportChecked = true;
                    }

                    if (isSupported)
                    {
                        var processName = FullscreenHelper.GetForegroundFullscreenProcessName();
                        var fullscreenActive = processName is not null &&
                            (mode == FullscreenFnLockMode.AnyApplication || await IsMediaPlayingAsync(processName).ConfigureAwait(false));

                        await _smartFnLockController.SetFullscreenStateAsync(fullscreenActive).ConfigureAwait(false);
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Instance.Trace($"Failed to update fullscreen Fn Lock state.", ex);
            }

            await WaitForNextCheckAsync(token).ConfigureAwait(false);
        }
    }

    private async Task<bool> IsMediaPlayingAsync(string processName)
    {
        try
        {
            _mediaSessionManager ??= await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();

            return _mediaSessionManager.GetSessions().Any(session =>
            {
                var playbackStatus = session.GetPlaybackInfo()?.PlaybackStatus;
                var hasActiveMedia = playbackStatus is
                    GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing or
                    GlobalSystemMediaTransportControlsSessionPlaybackStatus.Paused or
                    GlobalSystemMediaTransportControlsSessionPlaybackStatus.Changing;

                return hasActiveMedia && IsSessionOwnedByProcess(session.SourceAppUserModelId, processName);
            });
        }
        catch (Exception ex)
        {
            _mediaSessionManager = null;
            Log.Instance.Trace($"Couldn't query active media sessions.", ex);
            return false;
        }
    }

    private static bool IsSessionOwnedByProcess(string? sourceAppUserModelId, string processName)
    {
        if (string.IsNullOrWhiteSpace(sourceAppUserModelId))
            return false;

        var source = sourceAppUserModelId.Trim();
        var sourceFileName = Path.GetFileNameWithoutExtension(source);

        if (sourceFileName.Equals(processName, StringComparison.OrdinalIgnoreCase))
            return true;

        return source.Split(['!', '_'], StringSplitOptions.RemoveEmptyEntries)
            .Any(part => Path.GetFileNameWithoutExtension(part).Equals(processName, StringComparison.OrdinalIgnoreCase));
    }

    private async Task WaitForNextCheckAsync(CancellationToken token)
    {
        using var waitCts = CancellationTokenSource.CreateLinkedTokenSource(token);
        var delayTask = Task.Delay(PollInterval, waitCts.Token);
        var refreshTask = _refreshSignal.WaitAsync(waitCts.Token);

        await Task.WhenAny(delayTask, refreshTask).ConfigureAwait(false);
        await waitCts.CancelAsync().ConfigureAwait(false);
    }
}
