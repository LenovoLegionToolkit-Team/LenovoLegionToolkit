using System;
using System.IO;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using LenovoLegionToolkit.Lib.Logging.Storage;
using LenovoLegionToolkit.Lib.Extensions;
using LenovoLegionToolkit.Lib.Utils;

namespace LenovoLegionToolkit.Lib.Diagnostics;

public sealed class DiagnosticExporter(SqliteLogStore store)
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        Converters = { new JsonStringEnumConverter() }
    };

    public async Task ExportZipAsync(
        string destination,
        LogQuery query,
        string applicationVersion,
        CancellationToken cancellationToken = default,
        DiagnosticConfigOptions? configOptions = null)
    {
        query.Validate();
        query = query with { To = query.To ?? DateTimeOffset.UtcNow };
        var fullDestination = ValidateDestination(destination);
        var snapshot = Path.Combine(Path.GetTempPath(), $"llt-log-snapshot-{Guid.NewGuid():N}.db");
        try
        {
            await store.BackupAsync(snapshot, cancellationToken).ConfigureAwait(false);
            await WriteFileAsync(fullDestination, path =>
            {
                var redactor = new DiagnosticRedactor();
                using var archive = ZipFile.Open(path, ZipArchiveMode.Create);
                AddDatabaseSnapshot(archive, snapshot, cancellationToken);
                var collector = new DiagnosticConfigCollector();
                collector.AddToArchive(archive, GetAppDataPath(), redactor, configOptions);
                AddPreviousManifest(archive, redactor, cancellationToken);
                AddManifest(archive, query, applicationVersion, redactor, cancellationToken);
            }, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            TryDelete(snapshot);
        }
    }

    public async Task ExportTextAsync(string destination, LogQuery query, CancellationToken cancellationToken = default)
    {
        query.Validate();
        await store.FlushAsync(cancellationToken).ConfigureAwait(false);
        await WriteFileAsync(destination, path =>
        {
            var redactor = new DiagnosticRedactor();
            using var connection = store.OpenReader();
            using var command = SqliteLogStore.CreateQuery(connection, query, true);
            using var cancellation = cancellationToken.Register(command.Cancel);
            using var reader = command.ExecuteReader();
            using var text = new StreamWriter(path, false, new UTF8Encoding(false));

            while (reader.Read())
            {
                cancellationToken.ThrowIfCancellationRequested();
                text.WriteLine(LogTextFormatter.Format(redactor.Redact(SqliteLogStore.ReadEntry(reader))));
            }
        }, cancellationToken).ConfigureAwait(false);
    }

    private static void AddDatabaseSnapshot(ZipArchive archive, string snapshot, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var entry = archive.CreateEntry("logs/logs.db", CompressionLevel.Optimal);
        using var output = entry.Open();
        using var input = new FileStream(snapshot, FileMode.Open, FileAccess.Read, FileShare.Read, 64 * 1024, useAsync: false);
        input.CopyTo(output);
    }

    private static void AddPreviousManifest(ZipArchive archive, DiagnosticRedactor redactor, CancellationToken cancellationToken)
    {
        var appData = GetAppDataPath();
        string[] candidates =
        [
            Path.Combine(appData, "manifest.json"),
            Path.Combine(appData, "log", "manifest.json")
        ];

        foreach (var candidate in candidates)
        {
            if (!File.Exists(candidate))
            {
                continue;
            }
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var content = redactor.Redact(File.ReadAllText(candidate)) ?? string.Empty;
                using var writer = new StreamWriter(archive.CreateEntry("manifest.previous.json", CompressionLevel.Optimal).Open(), new UTF8Encoding(false));
                writer.Write(content);
                return;
            }
            catch (IOException)
            {
                return;
            }
            catch (UnauthorizedAccessException)
            {
                return;
            }
        }
    }

    private void AddManifest(ZipArchive archive, LogQuery query, string applicationVersion, DiagnosticRedactor redactor, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var status = store.Status;
        using var manifest = new StreamWriter(archive.CreateEntry("manifest.json", CompressionLevel.Optimal).Open(), new UTF8Encoding(false));
        manifest.Write(JsonSerializer.Serialize(new
        {
            FormatVersion = 2,
            CreatedUtc = DateTimeOffset.UtcNow,
            TimestampFormat = LogTextFormatter.TimestampFormat,
            TimestampTimeZone = LogTextFormatter.TimestampTimeZone,
            ApplicationVersion = redactor.Redact(applicationVersion),
            OperatingSystem = RuntimeInformation.OSDescription,
            Runtime = RuntimeInformation.FrameworkDescription,
            Architecture = RuntimeInformation.ProcessArchitecture.ToString(),
            Database = "logs/logs.db",
            Configuration = "config/",
            PreviousManifest = "manifest.previous.json (when available)",
            Filters = query with
            {
                Module = redactor.Redact(query.Module),
                EventId = redactor.Redact(query.EventId),
                Keyword = redactor.Redact(query.Keyword),
                ExceptionType = redactor.Redact(query.ExceptionType),
                CorrelationId = redactor.Redact(query.CorrelationId),
                SessionId = redactor.Redact(query.SessionId),
                BeforeId = null
            },
            status.DroppedRecords,
            status.FailedRecords,
            LastStorageError = redactor.Redact(status.LastError),
            Privacy = "The SQLite database is a consistent snapshot. Configuration and previous manifest content are sanitized. Review the archive before sharing."
        }, new JsonSerializerOptions(JsonOptions) { WriteIndented = true }));
    }

    private static string GetAppDataPath()
    {
        try
        {
            return Folders.AppData;
        }
        catch
        {
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LenovoLegionToolkit");
        }
    }

    private string ValidateDestination(string destination)
    {
        var fullDestination = Path.GetFullPath(destination);

        if (string.Equals(fullDestination, store.DatabasePath, StringComparison.OrdinalIgnoreCase) ||
            fullDestination.StartsWith(store.DatabasePath + "-", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("Export must not overwrite the log database.", nameof(destination));
        }

        return fullDestination;
    }

    private async Task WriteFileAsync(string destination, Action<string> write, CancellationToken cancellationToken)
    {
        var temporary = destination + $".{Guid.NewGuid():N}.tmp";
        try
        {
            await Task.Run(() =>
            {
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                cancellationToken.ThrowIfCancellationRequested();
                write(temporary);
                cancellationToken.ThrowIfCancellationRequested();
                File.Move(temporary, destination, true);
            }, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            TryDelete(temporary);
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
