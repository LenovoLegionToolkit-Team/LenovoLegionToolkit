using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace LenovoLegionToolkit.Lib.Diagnostics;

public sealed class DiagnosticRedactor
{
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromMilliseconds(250);

    private static readonly Regex UserPath = new(@"(?i)(?:[a-z]:[\\/](?:users|documents and settings)[\\/])[^\\/\s""<>]+", RegexOptions.CultureInvariant, RegexTimeout);

    private static readonly Regex Email = new(@"\b[\w.+-]+@[\w.-]+\.[a-zA-Z]{2,}\b", RegexOptions.CultureInvariant, RegexTimeout);

    private static readonly Regex Secret = new(@"(?i)[""']?\b(password|passwd|token|api[_-]?key|authorization|secret)\b[""']?\s*[:=]\s*(?:""[^""]*""|'[^']*'|(?:(?:Bearer|Basic)\s+)?[^\s,;]+)", RegexOptions.CultureInvariant, RegexTimeout);

    private static readonly Regex Bearer = new(@"(?i)\bBearer\s+[^\s"",;]+", RegexOptions.CultureInvariant, RegexTimeout);

    private static readonly Regex NetworkPath = new(@"\\\\[^\\\s]+\\[^\s""<>]+", RegexOptions.CultureInvariant, RegexTimeout);

    private static readonly Regex IpAddress = new(@"\b(?:\d{1,3}\.){3}\d{1,3}\b", RegexOptions.CultureInvariant, RegexTimeout);
    private readonly string[] _privateValues;

    public DiagnosticRedactor(IEnumerable<string>? privateValues = null)
    {
        _privateValues = (privateValues ?? new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            Environment.MachineName,
            Environment.UserName
        }).Where(v => !string.IsNullOrWhiteSpace(v)).Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(v => v.Length).ToArray();
    }

    public string? Redact(string? value)
    {
        if (value is null)
        {
            return null;
        }
        try
        {
            value = UserPath.Replace(value, "<user-profile>");
            value = NetworkPath.Replace(value, "<network-path>");

            foreach (var privateValue in _privateValues)
            {
                value = value.Replace(privateValue, "<private>", StringComparison.OrdinalIgnoreCase);
            }
            value = Email.Replace(value, "<email>");
            value = Bearer.Replace(value, "Bearer <redacted>");
            value = Secret.Replace(value, "$1=<redacted>");

            return IpAddress.Replace(value, "<ip-address>");
        }
        catch (RegexMatchTimeoutException)
        {
            return "<omitted: sanitization timed out>";
        }
    }

    public LogEntry Redact(LogEntry entry) => entry with
    {
        Module = Redact(entry.Module)!, EventId = Redact(entry.EventId), Message = Redact(entry.Message)!,
        Exception = Redact(entry.Exception), ExceptionType = Redact(entry.ExceptionType),
        SourceFile = Redact(entry.SourceFile), Member = Redact(entry.Member),
        CorrelationId = Redact(entry.CorrelationId), SessionId = Redact(entry.SessionId)!,
        DayKey = Redact(entry.DayKey)!,
        PropertiesJson = null
    };
}
