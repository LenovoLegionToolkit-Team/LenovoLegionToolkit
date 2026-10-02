using System;
using System.Threading.Tasks;
using LenovoLegionToolkit.Lib.Utils;

namespace LenovoLegionToolkit.Lib.Listeners;

public abstract class AbstractWMIListener<TEventArgs, TValue, TRawValue>(Func<Action<TRawValue>, IDisposable> listen)
    : IListener<TEventArgs>
    where TEventArgs : EventArgs
{
    private IDisposable? _disposable;

    public event EventHandler<TEventArgs>? Changed;

    public async Task StartAsync()
    {
        try
        {
            if (_disposable is not null)
            {
                Log.Instance.Trace($"Already started. [listener={GetType().Name}]");
                return;
            }

            if (!await CanStartAsync().ConfigureAwait(false))
            {
                Log.Instance.Trace($"Startup prevented by CanStartAsync check. [listener={GetType().Name}]");
                return;
            }

            Log.Instance.Trace($"Starting... [listener={GetType().Name}]");

            _disposable = listen(Handler);
        }
        catch (Exception ex)
        {
            Log.Instance.Trace($"Couldn't start listener. [listener={GetType().Name}]", ex);
        }
    }

    protected virtual Task<bool> CanStartAsync() => Task.FromResult(true);

    public Task StopAsync()
    {
        try
        {
            Log.Instance.Trace($"Stopping... [listener={GetType().Name}]");

            _disposable?.Dispose();
            _disposable = null;
        }
        catch (Exception ex)
        {
            Log.Instance.Trace($"Couldn't stop listener. [listener={GetType().Name}]", ex);
        }

        return Task.CompletedTask;
    }

    protected abstract TValue GetValue(TRawValue value);

    protected abstract TEventArgs GetEventArgs(TValue value);

    protected abstract Task OnChangedAsync(TValue value);

    protected virtual bool RaiseChangedAutomatically => true;

    protected void RaiseChanged(TValue value) => Changed?.Invoke(this, GetEventArgs(value));

    private async void Handler(TRawValue properties)
    {
        try
        {
            var value = GetValue(properties);

            Log.Instance.Trace($"Event received. [value={value}, listener={GetType().Name}]");

            await OnChangedAsync(value).ConfigureAwait(false);
            if (RaiseChangedAutomatically)
            {
                RaiseChanged(value);
            }
        }
        catch (Exception ex)
        {
            Log.Instance.Trace($"Failed to handle event.  [listener={GetType().Name}]", ex);
        }
    }
}
