using System;
using System.Threading.Tasks;
using LenovoLegionToolkit.Lib.Utils;
using NeoSmart.AsyncLock;

namespace LenovoLegionToolkit.Lib.AutoListeners;

public abstract class AbstractAutoListener<TEventArgs> : IAutoListener<TEventArgs> where TEventArgs : EventArgs
{
    private readonly AsyncLock _startStopLock = new();

    private bool _started;

    private event EventHandler<TEventArgs>? Changed;

    public async Task SubscribeChangedAsync(EventHandler<TEventArgs> eventHandler)
    {
        Changed += eventHandler;
        await StartStopAsync().ConfigureAwait(false);
    }

    public async Task UnsubscribeChangedAsync(EventHandler<TEventArgs> eventHandler)
    {
        Changed -= eventHandler;
        await StartStopAsync().ConfigureAwait(false);
    }

    public async Task RestartAsync()
    {
        using (await _startStopLock.LockAsync().ConfigureAwait(false))
        {
            if (!_started)
                return;

            Log.Instance.Trace($"Restarting... [type={GetType().Name}]");
            await StopInternalAsync().ConfigureAwait(false);
            await StartInternalAsync().ConfigureAwait(false);
            Log.Instance.Trace($"Restarted. [type={GetType().Name}]");
        }
    }

    private async Task StartStopAsync()
    {
        using (await _startStopLock.LockAsync().ConfigureAwait(false))
        {
            var subscribers = Changed?.GetInvocationList().Length ?? 0;

            Log.Instance.Trace($"Subscribers: {subscribers}. [type={GetType().Name}]");

            if (subscribers > 0)
                await StartInternalAsync().ConfigureAwait(false);
            else
                await StopInternalAsync().ConfigureAwait(false);
        }
    }

    private async Task StartInternalAsync()
    {
        if (_started)
        {
            Log.Instance.Trace($"Already started. [type={GetType().Name}]");

            return;
        }

        Log.Instance.Trace($"Starting... [type={GetType().Name}]");

        await StartAsync().ConfigureAwait(false);

        _started = true;

        Log.Instance.Trace($"Started. [type={GetType().Name}]");
    }

    private async Task StopInternalAsync()
    {
        if (!_started)
        {
            Log.Instance.Trace($"Already stopped. [type={GetType().Name}]");

            return;
        }

        Log.Instance.Trace($"Stopping... [type={GetType().Name}]");

        await StopAsync().ConfigureAwait(false);

        _started = false;

        Log.Instance.Trace($"Stopped. [type={GetType().Name}]");
    }

    protected abstract Task StartAsync();

    protected abstract Task StopAsync();

    protected void RaiseChanged(TEventArgs value) => Changed?.Invoke(this, value);
}
