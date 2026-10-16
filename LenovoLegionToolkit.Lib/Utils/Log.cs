using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading;
using LenovoLegionToolkit.Lib.Diagnostics;
using LenovoLegionToolkit.Lib.Logging.Storage;
using LenovoLegionToolkit.Lib.Extensions;

namespace LenovoLegionToolkit.Lib.Utils;

public class Log
{
    private static readonly Lazy<Log> Singleton = new(() => new Log());

    private static readonly AsyncLocal<string?> Correlation = new();

    private readonly object _emergencyLock = new();
    private readonly string _folderPath;
    private DateTimeOffset _lastEmergency;
    private bool _isTraceEnabled;
    public static Log Instance => Singleton.Value;

    public static string? CurrentCorrelationId => Correlation.Value ?? Activity.Current?.TraceId.ToString();

    public bool IsTraceEnabled { get => Volatile.Read(ref _isTraceEnabled); set => Volatile.Write(ref _isTraceEnabled, value); }

    public string SessionId { get; } = Guid.NewGuid().ToString("N");
    public SqliteLogStore Store { get; }
    public DiagnosticExporter Exporter { get; }
    public string DatabasePath => Store.DatabasePath;
    public string LogPath
    {
        get
        {
            var path = Path.Combine(_folderPath, "current-session.txt");
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            try
            {
                Exporter.ExportTextAsync(path, new LogQuery { SessionId = SessionId }, timeout.Token).GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                WriteEmergency("Unable to export the current session log.", ex);

                return Path.Combine(_folderPath, "emergency.txt");
            }

            return path;
        }
    }

    private Log()
    {
        try
        {
            _folderPath = Path.Combine(Folders.AppData, "log");
        }
        catch
        {
            _folderPath = Path.Combine(Path.GetTempPath(), "LenovoLegionToolkit", "log");
        }
        Store = new(Path.Combine(_folderPath, "logs.db"), emergency: WriteEmergency);
        Exporter = new(Store);
        AppDomain.CurrentDomain.ProcessExit += (_, _) => Shutdown(TimeSpan.FromSeconds(2));
    }

    public void Trace(FormattableString message, Exception? ex = null,
        [CallerFilePath] string? file = null, [CallerLineNumber] int lineNumber = -1, [CallerMemberName] string? caller = null)
    {
        if (!IsTraceEnabled)
        {
            return;
        }

        Write(ex is null ? LogLevel.Trace : LogLevel.Error, message.ToString(), ex, file: file, lineNumber: lineNumber, caller: caller);
    }

    public void Info(FormattableString message, [CallerFilePath] string? file = null,
        [CallerLineNumber] int lineNumber = -1, [CallerMemberName] string? caller = null) =>
        Write(LogLevel.Info, message.ToString(), file: file, lineNumber: lineNumber, caller: caller);

    public void Warning(FormattableString message, Exception? ex = null, [CallerFilePath] string? file = null,
        [CallerLineNumber] int lineNumber = -1, [CallerMemberName] string? caller = null) =>
        Write(LogLevel.Warning, message.ToString(), ex, file: file, lineNumber: lineNumber, caller: caller);

    public void Error(FormattableString message, Exception? ex = null, [CallerFilePath] string? file = null,
        [CallerLineNumber] int lineNumber = -1, [CallerMemberName] string? caller = null) =>
        Write(LogLevel.Error, message.ToString(), ex, file: file, lineNumber: lineNumber, caller: caller);

    public void Write(LogLevel level, string message, Exception? ex = null, string? module = null,
        string? eventId = null, string? correlationId = null, string? propertiesJson = null,
        [CallerFilePath] string? file = null, [CallerLineNumber] int lineNumber = -1, [CallerMemberName] string? caller = null)
    {
        try
        {
            var resolvedModule = string.IsNullOrWhiteSpace(module) ? ResolveModule(file) : module.Trim();
            var resolvedEventId = string.IsNullOrWhiteSpace(eventId)
                ? $"{resolvedModule}.{caller ?? "Write"}"
                : eventId;
            var resolvedCorrelationId = string.IsNullOrWhiteSpace(correlationId)
                ? CurrentCorrelationId ?? SessionId
                : correlationId;
            var entry = new LogEntry
            {
                Level = level, Message = message, Exception = ex?.ToStringDemystified(), ExceptionType = ex?.GetType().FullName,
                Module = resolvedModule, EventId = resolvedEventId, SourceFile = Path.GetFileName(file),
                SourceLine = lineNumber, Member = caller, SessionId = SessionId,
                CorrelationId = resolvedCorrelationId, PropertiesJson = propertiesJson
            };
#if DEBUG
            Debug.WriteLine(LogTextFormatter.Format(entry));
#endif
            Store.TryWrite(entry);
        }
        catch (Exception error)
        {
            WriteEmergency("Unable to enqueue a log record.", error);
        }
    }

    public void ErrorReport(string header, Exception ex)
    {
        Write(LogLevel.Error, header, ex, eventId: "ErrorReport");
        WriteEmergency(header, ex);
    }

    public static IDisposable BeginScope(string? correlationId = null)
    {
        var previous = Correlation.Value;
        Correlation.Value = correlationId ?? Guid.NewGuid().ToString("N");

        return new Scope(previous);
    }

    public void Shutdown(TimeSpan timeout)
    {
        try
        {
            Store.StopAsync(timeout).GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            WriteEmergency("Unable to stop the log writer.", ex);
        }
    }

    private sealed class Scope(string? previous) : IDisposable
    {
        private bool _disposed;

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            Correlation.Value = previous;
        }
    }

    private void WriteEmergency(string message, Exception? ex)
    {
        lock (_emergencyLock)
        {
            if (DateTimeOffset.UtcNow - _lastEmergency < TimeSpan.FromSeconds(1))
            {
                return;
            }
            _lastEmergency = DateTimeOffset.UtcNow;
            try
            {
                Directory.CreateDirectory(_folderPath);
                var path = Path.Combine(_folderPath, "emergency.txt");

                if (File.Exists(path) && new FileInfo(path).Length >= 1024 * 1024)
                {
                    File.Move(path, path + ".old", true);
                }
                var text = $"[{DateTimeOffset.UtcNow:O}] {message}\n{ex}";

                if (text.Length > 65536)
                {
                    text = text[..65536] + " [truncated]";
                }
                File.AppendAllText(path, text + Environment.NewLine);
            }
            catch
            {
            }
        }
    }

    internal static string ResolveModule(string? file)
    {
        return NormalizeIdentifier(Path.GetFileNameWithoutExtension(file));
    }

    internal static string NormalizeIdentifier(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "Application";
        }

        var name = value.Trim();
        var namespaceSeparator = name.LastIndexOf('.');
        if (namespaceSeparator >= 0 && namespaceSeparator < name.Length - 1)
        {
            name = name[(namespaceSeparator + 1)..];
        }

        foreach (var suffix in new[]
        {
            "Controller",
            "Feature",
            "Listener",
            "Service",
            "Provider",
            "Manager",
            "Handler",
            "Helper",
            "Settings",
            "Processor",
            "Control",
            "Window"
        })
        {
            if (name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase) && name.Length > suffix.Length)
            {
                name = name[..^suffix.Length];
                break;
            }
        }

        return string.IsNullOrWhiteSpace(name) ? "Application" : name;
    }

}
