using System;
using System.Collections.Generic;
using System.IO;
using System.Globalization;
using System.Linq;
using System.Threading;
using Microsoft.Data.Sqlite;

namespace LenovoLegionToolkit.Lib.Logging.Storage;

public sealed partial class SqliteLogStore
{
    private SqliteConnection OpenWriter()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(DatabasePath)!);

        try
        {
            return InitializeDatabase();
        }
        catch (SqliteException ex) when (ex.SqliteErrorCode is 11 or 26)
        {
            var suffix = $".corrupt-{DateTime.UtcNow:yyyyMMddHHmmssfff}-{Guid.NewGuid():N}";

            foreach (var extension in new[] { "", "-wal", "-shm" })
            {
                var path = DatabasePath + extension;

                if (File.Exists(path))
                {
                    File.Move(path, DatabasePath + suffix + extension);
                }
            }

            Emergency("A corrupt log database was quarantined and rebuilt.", ex);
            var oldBackups = Directory.GetFiles(Path.GetDirectoryName(DatabasePath)!, Path.GetFileName(DatabasePath) + ".corrupt-*")
                .Where(p => !p.EndsWith("-wal") && !p.EndsWith("-shm")).OrderByDescending(p => p).Skip(2);

            foreach (var path in oldBackups)
            {
                foreach (var extension in new[] { "", "-wal", "-shm" })
                {
                    if (File.Exists(path + extension))
                    {
                        File.Delete(path + extension);
                    }
                }
            }

            if (TryRestoreRecoveryBackup())
            {
                return InitializeDatabase();
            }

            return InitializeDatabase();
        }
    }

    private SqliteConnection InitializeDatabase()
    {
        var connection = new SqliteConnection(ConnectionString(SqliteOpenMode.ReadWriteCreate));
        try
        {
            connection.Open();
            var version = ScalarLong(connection, "PRAGMA user_version;");

            if (version > 2)
            {
                throw new InvalidOperationException($"Unsupported log database schema version {version}.");
            }

            if (version != 0 && ScalarString(connection, "PRAGMA quick_check;") != "ok")
            {
                throw new SqliteException("Log database integrity check failed.", 11);
            }

            Execute(connection, "PRAGMA auto_vacuum=INCREMENTAL; PRAGMA journal_mode=WAL; PRAGMA synchronous=NORMAL; PRAGMA wal_autocheckpoint=128; PRAGMA journal_size_limit=2097152;");

            if (version == 0)
            {
                using var transaction = connection.BeginTransaction();
                Execute(connection, """
                    CREATE TABLE IF NOT EXISTS logs (
                        id INTEGER PRIMARY KEY, timestamp INTEGER NOT NULL, level INTEGER NOT NULL, module TEXT NOT NULL,
                        event_id TEXT, message TEXT NOT NULL, exception TEXT, exception_type TEXT,
                        thread_id INTEGER NOT NULL, process_id INTEGER NOT NULL, source_file TEXT, source_line INTEGER,
                        member TEXT, correlation_id TEXT, properties_json TEXT, session_id TEXT NOT NULL, day_key TEXT NOT NULL);
                    CREATE INDEX IF NOT EXISTS ix_logs_time ON logs(timestamp);
                    CREATE INDEX IF NOT EXISTS ix_logs_module_time ON logs(module, timestamp);
                    CREATE INDEX IF NOT EXISTS ix_logs_level_time ON logs(level, timestamp);
                    CREATE INDEX IF NOT EXISTS ix_logs_correlation ON logs(correlation_id, id);
                    CREATE INDEX IF NOT EXISTS ix_logs_session ON logs(session_id, id);
                    CREATE INDEX IF NOT EXISTS ix_logs_day_session ON logs(day_key, session_id, id);
                    PRAGMA user_version=2;
                    """, transaction);
                transaction.Commit();
            }

            EnsureDayKeyColumn(connection);

            if (version < 2)
            {
                Execute(connection, "PRAGMA user_version=2;");
            }
            var pageSize = ScalarLong(connection, "PRAGMA page_size;");
            Execute(connection, $"PRAGMA max_page_count={_options.MaxDatabaseBytes / pageSize};");

            return connection;
        }
        catch
        {
            connection.Dispose();
            throw;
        }
    }

    private static void InsertBatch(SqliteConnection connection, List<LogEntry> batch)
    {
        using var transaction = connection.BeginTransaction();
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO logs(timestamp,level,module,event_id,message,exception,exception_type,thread_id,process_id,
                             source_file,source_line,member,correlation_id,properties_json,session_id,day_key)
            VALUES($time,$level,$module,$event,$message,$exception,$type,$thread,$process,$file,$line,$member,$correlation,$properties,$session,$day);
            """;

        foreach (var name in new[] { "$time", "$level", "$module", "$event", "$message", "$exception", "$type", "$thread", "$process", "$file", "$line", "$member", "$correlation", "$properties", "$session", "$day" })
        {
            command.Parameters.Add(new SqliteParameter(name, DBNull.Value));
        }

        command.Prepare();

        foreach (var entry in batch)
        {
            object?[] values = [entry.Timestamp.ToUnixTimeMilliseconds(), (int)entry.Level, entry.Module, entry.EventId,
                entry.Message, entry.Exception, entry.ExceptionType, entry.ThreadId, entry.ProcessId, entry.SourceFile,
                entry.SourceLine, entry.Member, entry.CorrelationId, entry.PropertiesJson, entry.SessionId,
                string.IsNullOrWhiteSpace(entry.DayKey) ? entry.Timestamp.ToUniversalTime().ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : entry.DayKey];

            for (var i = 0; i < values.Length; i++)
            {
                command.Parameters[i].Value = values[i] ?? DBNull.Value;
            }

            command.ExecuteNonQuery();
        }

        transaction.Commit();
    }

    private void Maintain(SqliteConnection connection, long reserveBytes = 0)
    {
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "DELETE FROM logs WHERE timestamp < $cutoff; DELETE FROM logs WHERE id IN (SELECT id FROM logs ORDER BY id DESC LIMIT -1 OFFSET $count);";
            command.Parameters.AddWithValue("$cutoff", (DateTimeOffset.UtcNow - _options.Retention).ToUnixTimeMilliseconds());
            command.Parameters.AddWithValue("$count", _options.MaxRecords);
            command.ExecuteNonQuery();
        }

        var pageSize = ScalarLong(connection, "PRAGMA page_size;");
        var target = Math.Max(0, Math.Min(_options.MaxDatabaseBytes * 3 / 4, _options.MaxDatabaseBytes - reserveBytes));

        while ((ScalarLong(connection, "PRAGMA page_count;") - ScalarLong(connection, "PRAGMA freelist_count;")) * pageSize > target)
        {
            if (Execute(connection, "DELETE FROM logs WHERE id IN (SELECT id FROM logs ORDER BY id LIMIT 1000);") == 0)
            {
                break;
            }
        }

        Execute(connection, "PRAGMA incremental_vacuum(1024); PRAGMA wal_checkpoint(TRUNCATE);");
    }

    private static void EnsureDayKeyColumn(SqliteConnection connection)
    {
        var hasColumn = false;
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "PRAGMA table_info(logs);";
            using var reader = command.ExecuteReader();

            while (reader.Read())
            {
                if (string.Equals(reader.GetString(1), "day_key", StringComparison.OrdinalIgnoreCase))
                {
                    hasColumn = true;
                    break;
                }
            }
        }

        if (hasColumn)
        {
            return;
        }

        using var transaction = connection.BeginTransaction();
        Execute(connection, "ALTER TABLE logs ADD COLUMN day_key TEXT NOT NULL DEFAULT '';", transaction);
        Execute(connection, "UPDATE logs SET day_key = strftime('%Y-%m-%d', timestamp / 1000, 'unixepoch');", transaction);
        Execute(connection, "CREATE INDEX IF NOT EXISTS ix_logs_day_session ON logs(day_key, session_id, id);", transaction);
        transaction.Commit();
    }

    private void TryMaintain(SqliteConnection connection)
    {
        try
        {
            Maintain(connection);

            if (DateTimeOffset.UtcNow - _lastRecoveryBackup >= TimeSpan.FromMinutes(15))
            {
                CreateRecoveryBackup(connection);
                _lastRecoveryBackup = DateTimeOffset.UtcNow;
            }
        }
        catch (Exception ex)
        {
            Volatile.Write(ref _lastError, ex.Message);
            Emergency("Log database maintenance failed.", ex);
        }
    }

    private void CreateRecoveryBackup(SqliteConnection source)
    {
        var destination = DatabasePath + ".backup";
        var temporary = destination + $".{Guid.NewGuid():N}.tmp";
        try
        {
            using var target = new SqliteConnection(new SqliteConnectionStringBuilder
            {
                DataSource = temporary,
                Pooling = false
            }.ToString());
            target.Open();
            source.BackupDatabase(target);
            using var check = target.CreateCommand();
            check.CommandText = "PRAGMA quick_check;";

            if (!string.Equals(check.ExecuteScalar()?.ToString(), "ok", StringComparison.OrdinalIgnoreCase))
            {
                throw new IOException("The recovery backup failed integrity validation.");
            }
            target.Close();
            File.Move(temporary, destination, true);
        }
        finally
        {
            if (File.Exists(temporary))
            {
                File.Delete(temporary);
            }
        }
    }

    private bool TryRestoreRecoveryBackup()
    {
        var backup = DatabasePath + ".backup";

        if (!File.Exists(backup))
        {
            return false;
        }

        var temporary = DatabasePath + $".{Guid.NewGuid():N}.restore";
        try
        {
            using (var source = new SqliteConnection(new SqliteConnectionStringBuilder
            {
                DataSource = backup,
                Mode = SqliteOpenMode.ReadOnly,
                Pooling = false
            }.ToString()))
            {
                source.Open();
                using var check = source.CreateCommand();
                check.CommandText = "PRAGMA quick_check;";

                if (!string.Equals(check.ExecuteScalar()?.ToString(), "ok", StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }
            }

            File.Copy(backup, temporary, true);
            File.Move(temporary, DatabasePath, true);
            Emergency("The log database was restored from the latest validated recovery backup.", null);

            return true;
        }
        catch (Exception ex)
        {
            Emergency("The validated recovery backup could not be restored.", ex);

            return false;
        }
        finally
        {
            if (File.Exists(temporary))
            {
                File.Delete(temporary);
            }
        }
    }

    private void EnsureWalSpace(SqliteConnection connection, long reserveBytes)
    {
        var wal = new FileInfo(DatabasePath + "-wal");

        if (!wal.Exists || wal.Length + reserveBytes <= _options.MaxWalBytes)
        {
            return;
        }

        Execute(connection, "PRAGMA wal_checkpoint(TRUNCATE);");
        wal.Refresh();

        if (wal.Exists && wal.Length + reserveBytes > _options.MaxWalBytes)
        {
            throw new IOException("The log WAL capacity limit was reached while a reader held a snapshot; diagnostic writes are temporarily suspended.");
        }
    }
}
