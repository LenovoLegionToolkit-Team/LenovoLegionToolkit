using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;

namespace LenovoLegionToolkit.Lib.Logging.Storage;

public sealed partial class SqliteLogStore
{
    public Task<IReadOnlyList<LogEntry>> QueryAsync(LogQuery query, CancellationToken cancellationToken = default)
    {
        query.Validate();

        return Task.Run<IReadOnlyList<LogEntry>>(() =>
        {
            using var connection = OpenReader();
            using var command = CreateQuery(connection, query, false);
            using var cancellation = cancellationToken.Register(command.Cancel);
            using var reader = command.ExecuteReader();
            var entries = new List<LogEntry>();

            while (reader.Read())
            {
                cancellationToken.ThrowIfCancellationRequested();
                entries.Add(ReadEntry(reader));
            }

            return entries;
        }, cancellationToken);
    }

    public Task<IReadOnlyList<string>> GetModulesAsync(CancellationToken cancellationToken = default) => Task.Run<IReadOnlyList<string>>(() =>
    {
        using var connection = OpenReader();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT DISTINCT module FROM logs ORDER BY module;";
        using var cancellation = cancellationToken.Register(command.Cancel);
        using var reader = command.ExecuteReader();
        var modules = new List<string>();

        while (reader.Read())
        {
            cancellationToken.ThrowIfCancellationRequested();
            modules.Add(reader.GetString(0));
        }

        return modules;
    }, cancellationToken);

    public async Task BackupAsync(string destination, CancellationToken cancellationToken = default)
    {
        destination = Path.GetFullPath(destination);

        if (string.Equals(destination, DatabasePath, StringComparison.OrdinalIgnoreCase) || destination.StartsWith(DatabasePath + "-", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("Backup must use a different path.", nameof(destination));
        }
        await FlushAsync(cancellationToken).ConfigureAwait(false);
        var temporary = destination + $".{Guid.NewGuid():N}.tmp";
        try
        {
            await Task.Run(() =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                using var source = OpenReader();
                using var target = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = temporary, Pooling = false }.ToString());
                target.Open();
                source.BackupDatabase(target);
                using var check = target.CreateCommand();
                check.CommandText = "PRAGMA quick_check;";

                if (!string.Equals(check.ExecuteScalar()?.ToString(), "ok", StringComparison.OrdinalIgnoreCase))
                {
                    throw new IOException("The SQLite backup failed integrity validation.");
                }
                target.Close();
                File.Move(temporary, destination, true);
            }, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            if (File.Exists(temporary))
            {
                File.Delete(temporary);
            }
        }
    }

    internal SqliteConnection OpenReader()
    {
        var connection = new SqliteConnection(ConnectionString(SqliteOpenMode.ReadOnly));
        try
        {
            connection.Open();

            return connection;
        }
        catch
        {
            connection.Dispose();
            throw;
        }
    }

    private string ConnectionString(SqliteOpenMode mode) => new SqliteConnectionStringBuilder
    {
        DataSource = DatabasePath,
        Mode = mode,
        Pooling = false,
        DefaultTimeout = 1
    }.ToString();

    internal static SqliteCommand CreateQuery(SqliteConnection connection, LogQuery query, bool export)
    {
        query.Validate();
        var command = connection.CreateCommand();
        var filters = new List<string>();
        void Add(string condition, string parameter, object? value)
        {
            if (value is null)
            {
                return;
            }
            filters.Add(condition);
            command.Parameters.AddWithValue(parameter, value);
        }
        Add("timestamp >= $from", "$from", query.From?.ToUnixTimeMilliseconds());
        Add("timestamp <= $to", "$to", query.To?.ToUnixTimeMilliseconds());
        Add("level = $level", "$level", query.Level.HasValue ? (int)query.Level.Value : null);
        Add("instr(lower(module), lower($module)) > 0", "$module", string.IsNullOrWhiteSpace(query.Module) ? null : query.Module);
        Add("instr(lower(event_id), lower($event)) > 0", "$event", string.IsNullOrWhiteSpace(query.EventId) ? null : query.EventId);
        Add("(instr(lower(message),lower($keyword)) > 0 OR instr(lower(coalesce(exception,'')),lower($keyword)) > 0)", "$keyword", string.IsNullOrEmpty(query.Keyword) ? null : query.Keyword);
        Add("exception_type = $type", "$type", query.ExceptionType);
        Add("correlation_id = $correlation", "$correlation", query.CorrelationId);
        Add("session_id = $session", "$session", query.SessionId);
        Add("day_key = $day", "$day", query.DayKey);

        if (!export)
        {
            Add("id < $before", "$before", query.BeforeId);
        }
        command.CommandText = "SELECT * FROM logs" + (filters.Count == 0 ? "" : " WHERE " + string.Join(" AND ", filters)) +
            (export ? " ORDER BY timestamp ASC, id ASC;" : " ORDER BY timestamp ASC, id ASC LIMIT $limit;");

        if (!export)
        {
            command.Parameters.AddWithValue("$limit", query.Limit);
        }

        return command;
    }

    internal static LogEntry ReadEntry(SqliteDataReader reader)
    {
        string? Text(int i) => reader.IsDBNull(i) ? null : reader.GetString(i);

        return new LogEntry
        {
            Id = reader.GetInt64(0),
            Timestamp = DateTimeOffset.FromUnixTimeMilliseconds(reader.GetInt64(1)),
            Level = (LogLevel)reader.GetInt32(2),
            Module = reader.GetString(3),
            EventId = Text(4),
            Message = reader.GetString(5),
            Exception = Text(6),
            ExceptionType = Text(7),
            ThreadId = reader.GetInt32(8),
            ProcessId = reader.GetInt32(9),
            SourceFile = Text(10),
            SourceLine = reader.GetInt32(11),
            Member = Text(12),
            CorrelationId = Text(13) is { Length: > 0 } correlation ? correlation : reader.GetString(15),
            PropertiesJson = Text(14),
            SessionId = reader.GetString(15),
            DayKey = reader.IsDBNull(16) ? string.Empty : reader.GetString(16)
        };
    }

}
