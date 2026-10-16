using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using LenovoLegionToolkit.Lib.Utils;
using Microsoft.Data.Sqlite;

namespace LenovoLegionToolkit.Lib.Logging.Storage;

public sealed partial class SqliteLogStore : IAsyncDisposable
{

    private sealed record WorkItem(LogEntry? Entry = null, TaskCompletionSource? Completion = null, bool Maintain = false);
    private readonly Channel<WorkItem> _queue;
    private readonly LogStoreOptions _options;
    private readonly Action<string, Exception?>? _emergency;
    private readonly Task _writer;

    private readonly SemaphoreSlim _batchReady = new(0, 1);
    private long _dropped;
    private long _failed;
    private string? _lastError;
    private int _stopped;
    private DateTimeOffset _lastRecoveryBackup;

    public string DatabasePath { get; }

    public LogStoreStatus Status => new(Interlocked.Read(ref _dropped), Interlocked.Read(ref _failed), Volatile.Read(ref _lastError));

    public SqliteLogStore(string databasePath, LogStoreOptions? options = null, Action<string, Exception?>? emergency = null)
    {
        DatabasePath = Path.GetFullPath(databasePath);
        _options = options ?? new();
        _options.Validate();
        _emergency = emergency;
        _queue = Channel.CreateBounded<WorkItem>(new BoundedChannelOptions(_options.QueueCapacity)
        {
            SingleReader = true,
            SingleWriter = false,
            FullMode = BoundedChannelFullMode.Wait,
            AllowSynchronousContinuations = false
        });
        _writer = Task.Run(WriteLoopAsync);
    }

    public bool TryWrite(LogEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        var module = string.IsNullOrWhiteSpace(entry.Module) ? "Application" : entry.Module.Trim();
        var eventId = string.IsNullOrWhiteSpace(entry.EventId)
            ? $"{module}.{entry.Member ?? "Write"}"
            : entry.EventId;
        var correlationId = string.IsNullOrWhiteSpace(entry.CorrelationId)
            ? (string.IsNullOrWhiteSpace(entry.SessionId) ? $"orphan-{Guid.NewGuid():N}" : entry.SessionId)
            : entry.CorrelationId;
        var timestamp = entry.Timestamp.ToUniversalTime();
        entry = entry with
        {
            Timestamp = timestamp,
            DayKey = timestamp.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            Module = Bound(module, 256) ?? "Application",
            EventId = Bound(eventId, 256),
            CorrelationId = Bound(correlationId, 256),
            Message = Bound(entry.Message, 65536) ?? string.Empty,
            Exception = Bound(entry.Exception, 65536),
            ExceptionType = Bound(entry.ExceptionType, 512),
            SourceFile = Bound(Path.GetFileName(entry.SourceFile), 256),
            Member = Bound(entry.Member, 256),
            PropertiesJson = Bound(entry.PropertiesJson, 16384),
            SessionId = Bound(entry.SessionId, 128) ?? string.Empty
        };

        if (_queue.Writer.TryWrite(new(entry)))
        {
            if (_queue.Reader.Count >= _options.BatchSize)
            {
                SignalBatch();
            }

            return true;
        }

        Interlocked.Increment(ref _dropped);

        if (entry.Level == LogLevel.Error)
        {
            Emergency(entry.Message, entry.Exception is null ? null : new Exception(entry.Exception));
        }

        return false;
    }

    public Task FlushAsync(CancellationToken cancellationToken = default)
    {
        return BarrierAsync(false, cancellationToken);
    }

    public Task MaintainAsync(CancellationToken cancellationToken = default)
    {
        return BarrierAsync(true, cancellationToken);
    }

    private async Task BarrierAsync(bool maintain, CancellationToken cancellationToken)
    {
        if (Volatile.Read(ref _stopped) != 0)
        {
            throw new InvalidOperationException("The log writer has stopped.");
        }

        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        await _queue.Writer.WriteAsync(new(Completion: completion, Maintain: maintain), cancellationToken).ConfigureAwait(false);

        SignalBatch();

        await completion.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<bool> StopAsync(TimeSpan timeout)
    {
        if (Interlocked.Exchange(ref _stopped, 1) == 0)
        {
            _queue.Writer.TryComplete();
        }

        SignalBatch();

        try
        {
            await _writer.WaitAsync(timeout).ConfigureAwait(false);

            return true;
        }
        catch (TimeoutException)
        {
            Emergency("Log writer shutdown timed out; queued diagnostics may be lost.", null);

            return false;
        }
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
    }

    private async Task WriteLoopAsync()
    {
        SqliteConnection? connection = null;
        Exception? failure = null;
        var nextRetry = DateTimeOffset.MinValue;
        var nextMaintenance = DateTimeOffset.MinValue;
        long reportedDrops = 0;

        try
        {
            using var timer = new PeriodicTimer(_options.MaintenanceInterval);
            var maintenance = timer.WaitForNextTickAsync().AsTask();
            var available = _queue.Reader.WaitToReadAsync().AsTask();

            while (true)
            {
                if (await Task.WhenAny(available, maintenance).ConfigureAwait(false) != available)
                {
                    if (connection is not null)
                    {
                        TryMaintain(connection);
                    }

                    maintenance = timer.WaitForNextTickAsync().AsTask();
                    continue;
                }

                if (!await available.ConfigureAwait(false))
                {
                    break;
                }

                if (_queue.Reader.Count < _options.BatchSize && _queue.Reader.TryPeek(out var first) && first.Completion is null)
                {
                    await _batchReady.WaitAsync(_options.BatchInterval).ConfigureAwait(false);
                }

                var batch = new List<LogEntry>(_options.BatchSize + 1);
                long batchBytes = 0;
                WorkItem? barrier = null;

                while (batch.Count < _options.BatchSize && _queue.Reader.TryPeek(out var pending))
                {
                    var bytes = pending.Entry is null ? 0 : EstimateBytes(pending.Entry);

                    if (batch.Count > 0 && batchBytes + bytes > Math.Min(_options.MaxDatabaseBytes / 8, _options.MaxWalBytes / 4))
                    {
                        break;
                    }

                    if (!_queue.Reader.TryRead(out var item))
                    {
                        break;
                    }

                    if (item.Completion is not null)
                    {
                        barrier = item;
                        break;
                    }

                    if (item.Entry is not null)
                    {
                        batch.Add(item.Entry);
                        batchBytes += bytes;
                    }
                }

                var committed = false;
                var attemptedDatabaseAccess = connection is not null || DateTimeOffset.UtcNow >= nextRetry;

                try
                {
                    if (connection is null)
                    {
                        if (DateTimeOffset.UtcNow < nextRetry)
                        {
                            throw failure ?? new IOException("Log database is temporarily unavailable.");
                        }

                        connection = OpenWriter();
                        failure = null;
                        nextMaintenance = DateTimeOffset.MinValue;
                    }

                    var pageSize = ScalarLong(connection, "PRAGMA page_size;");
                    var usedBytes = (ScalarLong(connection, "PRAGMA page_count;") - ScalarLong(connection, "PRAGMA freelist_count;")) * pageSize;

                    if (DateTimeOffset.UtcNow >= nextMaintenance || barrier?.Maintain == true || usedBytes + batchBytes * 2 > _options.MaxDatabaseBytes * 3 / 4)
                    {
                        Maintain(connection, batchBytes * 2);
                        nextMaintenance = DateTimeOffset.UtcNow + _options.MaintenanceInterval;
                    }

                    var dropped = Interlocked.Read(ref _dropped);

                    if (dropped > reportedDrops)
                    {
                        batch.Add(CreateQueueOverflowEntry(dropped - reportedDrops));
                    }

                    if (batch.Count > 0)
                    {
                        EnsureWalSpace(connection, batchBytes * 2);
                        InsertBatch(connection, batch);
                    }

                    committed = true;

                    if (barrier?.Maintain == true)
                    {
                        Maintain(connection);
                    }

                    reportedDrops = dropped;
                    Volatile.Write(ref _lastError, null);
                    barrier?.Completion?.TrySetResult();
                }
                catch (Exception ex)
                {
                    failure = ex;
                    Volatile.Write(ref _lastError, ex.Message);

                    if (!committed)
                    {
                        Interlocked.Add(ref _failed, batch.Count);
                    }

                    if (attemptedDatabaseAccess)
                    {
                        Emergency("Unable to persist diagnostics to SQLite.", ex);
                        nextRetry = DateTimeOffset.UtcNow + TimeSpan.FromSeconds(5);
                    }

                    foreach (var entry in batch.Where(e => e.Level == LogLevel.Error))
                    {
                        Emergency(entry.Message, entry.Exception is null ? null : new Exception(entry.Exception));
                    }

                    connection?.Dispose();
                    connection = null;
                    barrier?.Completion?.TrySetException(new IOException("Unable to flush the log database.", ex));
                }

                available = _queue.Reader.WaitToReadAsync().AsTask();
            }

            if (connection is not null)
            {
                TryMaintain(connection);
            }
        }
        catch (Exception ex)
        {
            Volatile.Write(ref _lastError, ex.Message);
            Emergency("The diagnostic writer stopped unexpectedly.", ex);
            _queue.Writer.TryComplete(ex);
        }
        finally
        {
            connection?.Dispose();

            while (_queue.Reader.TryRead(out var item))
            {
                if (item.Entry is not null)
                {
                    Interlocked.Increment(ref _failed);
                }

                item.Completion?.TrySetException(new IOException("The diagnostic writer has stopped."));
            }
        }
    }
}
