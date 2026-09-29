using System;
using System.Threading;
using System.Threading.Tasks;

namespace LenovoLegionToolkit.Lib.Utils;

public class ThrottleLastDispatcher(TimeSpan interval, string? tag = null)
{
    private readonly object _sync = new();
    private CancellationTokenSource? _cancellationTokenSource;
    private int _immediateRequests;

    public IDisposable SuppressThrottle()
    {
        lock (_sync)
        {
            _immediateRequests++;
            _cancellationTokenSource?.Cancel();
        }

        var disposed = 0;
        return new LambdaDisposable(() =>
        {
            if (Interlocked.Exchange(ref disposed, 1) != 0)
            {
                return;
            }

            lock (_sync)
            {
                _immediateRequests--;
            }
        });
    }

    public async Task DispatchAsync(Func<Task> task)
    {
        var source = new CancellationTokenSource();
        bool skipDelay;

        lock (_sync)
        {
            _cancellationTokenSource?.Cancel();
            _cancellationTokenSource = source;
            skipDelay = _immediateRequests > 0;
        }

        try
        {
            var token = source.Token;

            if (!skipDelay)
            {
                await Task.Delay(interval, token).ConfigureAwait(false);
            }

            token.ThrowIfCancellationRequested();

            if (tag is not null)
                Log.Instance.Trace($"Allowing... [tag={tag}]");

            await task().ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            if (tag is not null)
                Log.Instance.Trace($"Throttling... [tag={tag}]");
        }
        finally
        {
            lock (_sync)
            {
                if (ReferenceEquals(_cancellationTokenSource, source))
                {
                    _cancellationTokenSource = null;
                }

                source.Dispose();
            }
        }
    }

    public async Task DispatchImmediateAsync(Func<Task> task)
    {
        using var immediate = SuppressThrottle();

        if (tag is not null)
            Log.Instance.Trace($"Immediate dispatch... [tag={tag}]");

        await task().ConfigureAwait(false);
    }
}
