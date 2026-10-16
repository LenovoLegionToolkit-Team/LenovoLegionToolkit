using System;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace LenovoLegionToolkit.Lib.Extensions;

public static class LogTextFormatter
{
    private static readonly Regex LeadingModule = new(
        "^\\s*\\[(?<module>[^\\]]+)\\]\\s*",
        RegexOptions.Compiled | RegexOptions.CultureInvariant,
        TimeSpan.FromMilliseconds(100));
    public const string TimestampFormat = "yyyy-MM-dd HH:mm:ss.fff";
    public const string TimestampTimeZone = "UTC";

    public static string Format(LogEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        var builder = new StringBuilder();
        builder.Append('[').Append(entry.Timestamp.ToUniversalTime().ToString(TimestampFormat, CultureInfo.InvariantCulture)).Append("] ");
        builder.Append("[").Append(entry.Level).Append("] ");
        var module = string.IsNullOrWhiteSpace(entry.Module) ? "Application" : entry.Module.Trim();
        var eventId = string.IsNullOrWhiteSpace(entry.EventId) ? $"{module}.{entry.Member ?? "Write"}" : entry.EventId.Trim();
        var correlationId = string.IsNullOrWhiteSpace(entry.CorrelationId) ? "session-unknown" : entry.CorrelationId.Trim();
        builder.Append('[').Append(module).Append("] ");
        builder.Append('[').Append(eventId).Append("] ");
        builder.Append(NormalizeMessage(entry.Message, module));
        if (!string.IsNullOrWhiteSpace(correlationId))
        {
            builder.Append(" {correlation=").Append(correlationId).Append('}');
        }

        if (!string.IsNullOrWhiteSpace(entry.Exception))
        {
            builder.AppendLine();
            builder.Append(entry.Exception);
        }

        return builder.ToString();
    }

    private static string NormalizeMessage(string? message, string module)
    {
        var normalized = string.IsNullOrWhiteSpace(message) ? string.Empty : message.Trim();

        while (true)
        {
            var match = LeadingModule.Match(normalized);

            if (!match.Success || !string.Equals(match.Groups["module"].Value.Trim(), module, StringComparison.OrdinalIgnoreCase))
            {
                break;
            }
            normalized = normalized[match.Length..].TrimStart();
        }

        return normalized;
    }
}
