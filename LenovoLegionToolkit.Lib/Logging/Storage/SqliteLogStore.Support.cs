using System;
using System.Text;
using System.Threading;
using Microsoft.Data.Sqlite;

namespace LenovoLegionToolkit.Lib.Logging.Storage;

public sealed partial class SqliteLogStore
{
    private void Emergency(string message, Exception? ex)
    {
        try
        {
            _emergency?.Invoke(message, ex);
        }
        catch { /* Ignore */ }
    }

    private void SignalBatch()
    {
        if (_batchReady.CurrentCount != 0)
        {
            return;
        }
        try
        {
            _batchReady.Release();
        }
        catch (SemaphoreFullException) { /* Ignore */ }
    }

    private static LogEntry CreateQueueOverflowEntry(long count)
    {
        return new LogEntry
        {
            Level = LogLevel.Warning,
            Module = "Diagnostics",
            EventId = "QueueOverflow",
            CorrelationId = "Diagnostics",
            Message = $"Dropped {count} log records because the queue was full."
        };
    }

    private static string? Bound(string? value, int limit)
    {
        if (value is null || value.Length <= limit)
        {
            return value;
        }

        return value[..limit] + " [truncated]";
    }

    private static long EstimateBytes(LogEntry entry)
    {
        return 4096L +
            Encoding.UTF8.GetByteCount(entry.Message) +
            Encoding.UTF8.GetByteCount(entry.Exception ?? string.Empty) +
            Encoding.UTF8.GetByteCount(entry.PropertiesJson ?? string.Empty);
    }

    private static long ScalarLong(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;

        return Convert.ToInt64(command.ExecuteScalar());
    }

    private static string? ScalarString(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;

        return command.ExecuteScalar()?.ToString();
    }

    private static int Execute(SqliteConnection connection, string sql, SqliteTransaction? transaction = null)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.Transaction = transaction;

        return command.ExecuteNonQuery();
    }
}
