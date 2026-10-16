using System;
using LenovoLegionToolkit.Lib;
using LenovoLegionToolkit.Lib.Station.Logging;
using LenovoLegionToolkit.Lib.Utils;

namespace LenovoLegionToolkit.WPF.Station.Logging;

public sealed class ExtensionLogger(string pluginId) : IExtensionLogger
{
    public void Trace(string message)
    {
        Write(LogLevel.Trace, message, null);
    }

    public void Error(string message, Exception exception)
    {
        Write(LogLevel.Error, message, exception);
    }

    private void Write(LogLevel level, string message, Exception? exception)
    {
        var normalizedMessage = message.TrimStart();
        var eventId = level.ToString();

        if (normalizedMessage.StartsWith("[", StringComparison.Ordinal))
        {
            var end = normalizedMessage.IndexOf(']');
            if (end > 1)
            {
                eventId = normalizedMessage[1..end].Trim();
                normalizedMessage = normalizedMessage[(end + 1)..].TrimStart();
            }
        }

        Log.Instance.Write(
            level,
            normalizedMessage,
            exception,
            module: NormalizeIdentifier(pluginId),
            eventId: eventId);
    }

    private static string NormalizeIdentifier(string value)
    {
        const string prefix = "LenovoLegionToolkit.Plugin.";

        if (value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            value = value[prefix.Length..];
        }
        else if (value.StartsWith("Extension.", StringComparison.OrdinalIgnoreCase))
        {
            value = value["Extension.".Length..];
        }

        var separator = value.LastIndexOf('.');
        var identifier = separator >= 0 ? value[(separator + 1)..] : value;

        foreach (var suffix in new[] { "Provider", "Plugin" })
        {
            if (identifier.EndsWith(suffix, StringComparison.OrdinalIgnoreCase) && identifier.Length > suffix.Length)
            {
                identifier = identifier[..^suffix.Length];
                break;
            }
        }

        return string.IsNullOrWhiteSpace(identifier) ? "Extensions" : identifier;
    }
}
