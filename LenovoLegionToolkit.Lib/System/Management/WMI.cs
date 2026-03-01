using System;
using System.Collections.Generic;
using System.Linq;
using System.Management;
using System.Threading.Tasks;
using LenovoLegionToolkit.Lib.Extensions;
using LenovoLegionToolkit.Lib.Utils;

namespace LenovoLegionToolkit.Lib.System.Management;

public static partial class WMI
{
    private static async Task<bool> ExistsAsync(string scope, FormattableString query)
    {
        try
        {
            var queryFormatted = query.ToString(WMIPropertyValueFormatter.Instance);
            var mos = new ManagementObjectSearcher(scope, queryFormatted);
            var managementObjects = await mos.GetAsync().ConfigureAwait(false);
            return managementObjects.Any();
        }
        catch
        {
            return false;
        }
    }

    private static LambdaDisposable Listen(string scope, FormattableString query, Action<PropertyDataCollection> handler)
    {
        var queryFormatted = query.ToString(WMIPropertyValueFormatter.Instance);
        var watcher = new ManagementEventWatcher(scope, queryFormatted);
        watcher.EventArrived += (_, e) => handler(e.NewEvent.Properties);
        watcher.Start();

        return new LambdaDisposable(() =>
        {
            watcher.Stop();
            watcher.Dispose();
        });
    }

    private static async Task<IEnumerable<T>> ReadAsync<T>(string scope, FormattableString query, Func<PropertyDataCollection, T> converter)
    {
        try
        {
            var queryFormatted = query.ToString(WMIPropertyValueFormatter.Instance);
            var mos = new ManagementObjectSearcher(scope, queryFormatted);
            var managementObjects = await mos.GetAsync().ConfigureAwait(false);
            // IMPORTANT: Materialize immediately to avoid lazy evaluation issues with caching
            var result = managementObjects.Select(mo => mo.Properties).Select(converter).ToList();
            return result;
        }
        catch (ManagementException ex)
        {
            throw new ManagementException($"Read failed: {ex.Message} [scope={scope}, query={query}]", ex);
        }
    }

    /// <summary>
    /// Reads WMI data with caching. Use for static hardware info only!
    /// Do NOT use for real-time data like temperatures or power states.
    /// </summary>
    private static async Task<IEnumerable<T>> ReadCachedAsync<T>(
        string scope, 
        FormattableString query, 
        Func<PropertyDataCollection, T> converter,
        TimeSpan? ttl = null)
    {
        var queryFormatted = query.ToString(WMIPropertyValueFormatter.Instance);
        var cacheKey = $"WMI:{scope}:{queryFormatted}";

        return await WmiCache.GetOrCreateAsync(
            cacheKey,
            () => ReadAsync(scope, query, converter),
            ttl
        ).ConfigureAwait(false);
    }

    private static async Task CallAsync(string scope, FormattableString query, string methodName, Dictionary<string, object> methodParams)
    {
        try
        {
            var queryFormatted = query.ToString(WMIPropertyValueFormatter.Instance);
            var mos = new ManagementObjectSearcher(scope, queryFormatted);
            var managementObjects = await mos.GetAsync().ConfigureAwait(false);
            var managementObject = managementObjects.FirstOrDefault() ?? throw new InvalidOperationException("No results in query");

            var mo = (ManagementObject)managementObject;
            var methodParamsObject = mo.GetMethodParameters(methodName);
            foreach (var pair in methodParams)
                methodParamsObject[pair.Key] = pair.Value;

            mo.InvokeMethod(methodName, methodParamsObject, new InvokeMethodOptions());
        }
        catch (ManagementException ex)
        {
            var methodParamsStr = string.Join(", ", methodParams.Select(kv => $"{kv.Key}={kv.Value}"));
            throw new ManagementException($"Call failed: {ex.Message}. [scope={scope}, query={query}, methodName={methodName}, methodParams={{{methodParamsStr}}}]", ex);
        }
    }

    private static async Task<T> CallAsync<T>(string scope, FormattableString query, string methodName, Dictionary<string, object> methodParams, Func<PropertyDataCollection, T> converter)
    {
        try
        {
            var queryFormatted = query.ToString(WMIPropertyValueFormatter.Instance);

            var mos = new ManagementObjectSearcher(scope, queryFormatted);
            var managementObjects = await mos.GetAsync().ConfigureAwait(false);
            var managementObject = managementObjects.FirstOrDefault() ?? throw new InvalidOperationException("No results in query");

            var mo = (ManagementObject)managementObject;
            var methodParamsObject = mo.GetMethodParameters(methodName);
            foreach (var pair in methodParams)
                methodParamsObject[pair.Key] = pair.Value;

            var resultProperties = mo.InvokeMethod(methodName, methodParamsObject, new InvokeMethodOptions());
            var result = converter(resultProperties.Properties);
            return result;
        }
        catch (ManagementException ex)
        {
            var methodParamsStr = string.Join(", ", methodParams.Select(kv => $"{kv.Key}={kv.Value}"));
            throw new ManagementException($"Call failed: {ex.Message}. [scope={scope}, query={query}, methodName={methodName}, methodParams={{{methodParamsStr}}}]", ex);
        }
    }

    private class WMIPropertyValueFormatter : IFormatProvider, ICustomFormatter
    {
        public static readonly WMIPropertyValueFormatter Instance = new();

        private WMIPropertyValueFormatter() { }

        public object GetFormat(Type? formatType)
        {
            if (formatType == typeof(ICustomFormatter))
                return this;

            throw new InvalidOperationException("Invalid type of formatted");
        }

        public string Format(string? format, object? arg, IFormatProvider? formatProvider)
        {
            var stringArg = arg?.ToString()?.Replace("\\", "\\\\");
            return stringArg ?? string.Empty;
        }
    }
}
